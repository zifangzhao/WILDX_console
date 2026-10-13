using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace USB512_interface
{
    // Read-only, once-per-port confirmation after START. Never gates or stops an
    // active recorder; the firmware applies and verifies maps before acquisition.
    internal sealed class NpBleConfigurationReceipts
    {
        readonly Queue<Np2ConfigRequest> pending = new Queue<Np2ConfigRequest>();
        uint generation;
        internal int Pending { get { return pending.Count; } }

        internal void Reset() { generation++; pending.Clear(); }
        internal void Expect(IEnumerable<Np2ConfigRequest> requests)
        {
            Reset();
            foreach (var request in requests) pending.Enqueue(request);
        }

        internal async Task<string> Observe(uint state,
            Func<byte, CancellationToken, Task<Np2ConfigReply>> readStatus, CancellationToken cancel)
        {
            // PREPARING is allowed to take time; a staged receipt is not failure
            // or proof of application. One exchange per poll keeps controls free.
            if (pending.Count == 0 || (state != 3 && state != 4 && state != 5 && state != 6 && state != 7 && state != 255)) return null;
            uint epoch = generation;
            var request = pending.Peek();
            var reply = await readStatus(request.Port, cancel);
            if (epoch != generation) return null; // Disconnect/new START invalidates old evidence.
            pending.Dequeue();
            string port = "NP2 P" + (request.Port + 1) + " IMRO: ";
            try { Np2ConfigClient.RequireApplied(reply, request); }
            catch (IOException ex) { return port + "NOT confirmed applied. " + ex.Message + " Recording is unchanged by this read-only check."; }
            string warning=Np2ConfigClient.AppliedWarning(reply);
            return port + "applied; matching ID/CRC and shift/control verification received. This does not verify the analog signal."+(warning==null?"":" "+warning);
        }
    }
}
