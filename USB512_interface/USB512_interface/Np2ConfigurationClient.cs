using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace USB512_interface
{
    // V2 configuration is intentionally a separate wire from the V1 NPB1/NPC1 protocol.
    internal sealed class Np2ConfigRequest
    {
        internal const int PayloadBytes = 876;
        internal readonly string Json;
        internal readonly byte[] Selection;

        internal byte Port { get { return Selection[5]; } }

        internal Np2ConfigRequest(string json, byte[] selection)
        {
            if (String.IsNullOrWhiteSpace(json) || selection == null || selection.Length != PayloadBytes ||
                selection[0] != (byte)'N' || selection[1] != (byte)'2' || selection[2] != (byte)'B' || selection[3] != (byte)'1' ||
                selection[4] != 4 || selection[5] > 2 || selection[6] != 0 || selection[7] != 0)
                throw new InvalidDataException("Invalid NP2013 four-shank configuration request.");
            Json = json;
            Selection = (byte[])selection.Clone();
        }
    }

    internal interface INp2ConfigTransport
    {
        Task<byte[]> ExchangeAsync(byte[] request, CancellationToken cancel);
    }

    internal sealed class Np2ConfigReply
    {
        internal byte Error, GlobalState, PortState, Verification, Port, Model;
        internal uint Transaction, Received, PayloadBytes, PayloadCrc;
        internal byte[] ConfigurationId;
        internal uint AvailableMask, SupportedModel, Uid0, Uid1, Uid2;
        // Exactly low-nibble 2 with one to three failed UNUSED shanks. Value 1
        // still means all six chains passed; unknown combinations are rejected.
        internal byte FailedUnusedShankMask { get { return (Verification & 15) == 2 && (Verification >> 4) >= 1 && (Verification >> 4) <= 14 ? (byte)(Verification >> 4) : (byte)0; } }
    }

    internal sealed class Np2ConfigClient
    {
        internal const int FrameBytes = 64;
        internal const int ChunkBytes = 32;
        internal const byte Status = 0, Begin = 1, Write = 2, Commit = 3, Abort = 4, Caps = 5;
        private readonly INp2ConfigTransport transport;
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);

        internal Np2ConfigClient(INp2ConfigTransport transport)
        {
            this.transport = transport ?? throw new ArgumentNullException("transport");
        }

        internal static uint Crc(byte[] bytes, int count)
        {
            if (bytes == null || count < 0 || count > bytes.Length) throw new ArgumentOutOfRangeException("count");
            uint crc = ~0U;
            for (int i = 0; i < count; i++)
            {
                crc ^= bytes[i];
                for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((0U - (crc & 1U)) & 0xEDB88320U);
            }
            return ~crc;
        }

        private static void Put(byte[] bytes, int offset, uint value)
        {
            for (int i = 0; i != 4; i++) bytes[offset + i] = (byte)(value >> (8 * i));
        }

        private static uint Get(byte[] bytes, int offset)
        {
            return (uint)(bytes[offset] | ((uint)bytes[offset + 1] << 8) | ((uint)bytes[offset + 2] << 16) | ((uint)bytes[offset + 3] << 24));
        }

        internal static byte[] Frame(byte operation, byte port, uint transaction, uint offset, uint length, uint payloadCrc, byte[] data = null)
        {
            if (operation > Caps || port > 2 || transaction == 0 || (data != null && data.Length > ChunkBytes))
                throw new ArgumentOutOfRangeException();
            if (operation == Write && (data == null || data.Length == 0 || data.Length != (int)length))
                throw new ArgumentException("Configuration WRITE requires one matching data chunk.");
            if (operation != Write && data != null) throw new ArgumentException("Only configuration WRITE carries data.");
            var frame = new byte[FrameBytes];
            frame[0] = (byte)'N'; frame[1] = (byte)'P'; frame[2] = (byte)'C'; frame[3] = (byte)'2';
            frame[4] = 2; frame[5] = operation; frame[6] = port;
            Put(frame, 8, transaction); Put(frame, 12, offset); Put(frame, 16, length); Put(frame, 20, payloadCrc);
            if (data != null) Buffer.BlockCopy(data, 0, frame, 24, data.Length);
            Put(frame, 60, Crc(frame, 60));
            return frame;
        }

        internal static Np2ConfigReply Parse(byte[] frame, byte operation, uint transaction)
        {
            if (frame == null || frame.Length != FrameBytes || frame[0] != (byte)'N' || frame[1] != (byte)'P' || frame[2] != (byte)'R' || frame[3] != (byte)'2' ||
                frame[4] != 2 || frame[5] != operation || Get(frame, 8) != transaction || Get(frame, 16) != Np2ConfigRequest.PayloadBytes || Get(frame, 60) != Crc(frame, 60))
                throw new IOException("NP2 configuration response identity/version/CRC mismatch.");
            var result = new Np2ConfigReply
            {
                Error = frame[6], GlobalState = frame[7], Transaction = transaction, Received = Get(frame, 12), PayloadBytes = Get(frame, 16),
                PortState = frame[20], Verification = frame[21], Port = frame[22], Model = frame[23], PayloadCrc = Get(frame, 24), ConfigurationId = new byte[32]
            };
            Buffer.BlockCopy(frame, 28, result.ConfigurationId, 0, result.ConfigurationId.Length);
            if (operation == Caps)
            {
                result.AvailableMask = Get(frame, 24); result.SupportedModel = Get(frame, 28);
                result.Uid0 = Get(frame, 32); result.Uid1 = Get(frame, 36); result.Uid2 = Get(frame, 40);
            }
            return result;
        }

        private static uint NewTransaction()
        {
            uint transaction = BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(), 0);
            return transaction == 0 ? 1U : transaction;
        }

        private async Task<Np2ConfigReply> Send(byte[] frame, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            return Parse(await transport.ExchangeAsync(frame, cancel), frame[5], Get(frame, 8));
        }

        private static void Require(Np2ConfigReply reply)
        {
            if (reply.Error != 0) throw new IOException("NP2 configuration rejected by firmware, code " + reply.Error + ". No applied-state claim is made.");
        }

        private static void RequireIdentity(Np2ConfigReply reply, Np2ConfigRequest request)
        {
            if (reply.Port != request.Port || reply.Model != 4 || reply.PayloadCrc != Crc(request.Selection, request.Selection.Length))
                throw new IOException("NP2 configuration receipt does not match the requested port, model, or payload CRC.");
            for (int i = 0; i < 32; i++) if (reply.ConfigurationId[i] != request.Selection[8 + i])
                throw new IOException("NP2 configuration receipt ID differs from the request.");
        }

        internal async Task<Np2ConfigReply> StatusAsync(byte port, CancellationToken cancel)
        {
            if (port > 2) throw new ArgumentOutOfRangeException("port");
            await gate.WaitAsync(cancel);
            try
            {
                var reply = await Send(Frame(Status, port, NewTransaction(), 0, 0, 0), cancel);
                Require(reply);
                return reply;
            }
            finally { gate.Release(); }
        }

        internal async Task<Np2ConfigReply> CapsAsync(byte port, CancellationToken cancel)
        {
            if (port > 2) throw new ArgumentOutOfRangeException("port");
            await gate.WaitAsync(cancel);
            try { return await RequireCaps(port, cancel); }
            finally { gate.Release(); }
        }

        private async Task<Np2ConfigReply> RequireCaps(byte port, CancellationToken cancel)
        {
            var reply = await Send(Frame(Caps, port, NewTransaction(), 0, 0, 0), cancel);
            Require(reply);
            if (reply.SupportedModel != 4U || (reply.AvailableMask & (1U << port)) == 0U)
                throw new IOException("This firmware does not advertise NP2013 configuration for the selected connector.");
            return reply;
        }

        internal async Task<Np2ConfigReply> StageAsync(Np2ConfigRequest request, CancellationToken cancel)
        {
            if (request == null) throw new ArgumentNullException("request");
            await gate.WaitAsync(cancel);
            uint transaction = NewTransaction(); uint crc = Crc(request.Selection, request.Selection.Length);
            bool begun = false, commitSent = false;
            try
            {
                await RequireCaps(request.Port, cancel);
                Require(await Send(Frame(Status, request.Port, NewTransaction(), 0, 0, 0), cancel));
                Require(await Send(Frame(Begin, request.Port, transaction, 0, Np2ConfigRequest.PayloadBytes, crc), cancel)); begun = true;
                for (int offset = 0; offset < request.Selection.Length; offset += ChunkBytes)
                {
                    int count = Math.Min(ChunkBytes, request.Selection.Length - offset);
                    var chunk = new byte[count]; Buffer.BlockCopy(request.Selection, offset, chunk, 0, count);
                    var reply = await Send(Frame(Write, request.Port, transaction, (uint)offset, (uint)count, crc, chunk), cancel);
                    Require(reply);
                    if (reply.Received != (uint)(offset + count)) throw new IOException("NP2 configuration upload offset mismatch.");
                }
                commitSent = true;
                var result = await Send(Frame(Commit, request.Port, transaction, 0, 0, crc), cancel);
                Require(result); RequireIdentity(result, request);
                if (result.PortState != 2) throw new IOException("NP2 configuration was not staged as requested.");
                return result;
            }
            finally
            {
                if (begun && !commitSent)
                {
                    try { await transport.ExchangeAsync(Frame(Abort, request.Port, transaction, 0, 0, crc), CancellationToken.None); }
                    catch (Exception) { }
                }
                gate.Release();
            }
        }

        internal static void RequireApplied(Np2ConfigReply reply, Np2ConfigRequest request)
        {
            if (reply == null || request == null) throw new ArgumentNullException();
            Require(reply); RequireIdentity(reply, request);
            if (reply.PortState != 3 || (reply.Verification != 1 && reply.FailedUnusedShankMask == 0))
                throw new IOException("NP2 configuration lacks the required shift/status/control verification receipt; probe application is not confirmed.");
        }

        internal static string AppliedWarning(Np2ConfigReply reply)
        {
            if(reply.FailedUnusedShankMask==0)return null;
            var failed=new List<string>();
            for(int shank=0;shank<4;shank++)if((reply.FailedUnusedShankMask&(1<<shank))!=0)failed.Add("P"+(reply.Port+1)+" shank "+shank);
            return "DEGRADED: "+String.Join(", ",failed)+" failed unused-shank SR_OUT_OK. Selected chains verified; full six-chain verification unavailable. Requested mapping retained.";
        }
    }
}
