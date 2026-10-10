using System;
using System.Collections.Generic;
using CE32_console;

namespace USB512_interface
{
    // Notifications are arbitrary fragments of the UART stream, not packets.
    // A fixed body length and CRC prevent '<'/'>' inside a body ending a reply.
    internal sealed class NpBleWire
    {
        readonly List<byte> pending = new List<byte>();
        internal void Clear() { pending.Clear(); }
        internal static byte[] Frame(byte command, byte[] body)
        {
            if (body == null || body.Length != 64) throw new ArgumentException("64-byte NP request required.");
            var frame = new byte[67]; frame[0] = 0x3c; frame[1] = command; frame[66] = 0x3e;
            Buffer.BlockCopy(body, 0, frame, 2, 64); return frame;
        }
        internal List<Tuple<byte, byte[]>> Feed(byte[] bytes)
        {
            pending.AddRange(bytes);
            var frames = new List<Tuple<byte, byte[]>>();
            int at = 0;
            while (at + 2 <= pending.Count) {
                byte lead = pending[at], command = pending[at + 1];
                if ((lead != 0x3c && lead != 0xad) || (command != 0xb9 && command != 0xb8 && command!=0xba)) { at++; continue; }
                bool preview=command==0xba;
                if(preview){
                    if(pending.Count-at<6)break;
                    if(BitConverter.ToUInt32(pending.GetRange(at+2,4).ToArray(),0)!=NpV2PreviewBurst.Magic){at++;continue;}
                }
                int bodyLength=preview?224:64;
                int length = bodyLength+(lead == 0x3c ? 3 : 4);
                if(preview && pending.Count-at>=34){
                    byte kind=pending[at+31];
                    if((kind&128)==0 && (kind&64)!=0){
                        bool impossible=false;
                        for(int i=at+34;i<Math.Min(pending.Count,at+226);i++)if(pending[i]>=64){impossible=true;break;}
                        // Marked preview bytes cannot contain AD/B9 control leads.
                        // Discard a truncated preview before it holds a CRC-valid control.
                        if(impossible){at++;continue;}
                    }
                }
                if (pending.Count - at < length) break;
                bool tail = lead == 0x3c ? pending[at+bodyLength+2] == 0x3e : pending[at+bodyLength+2] == 0xda && pending[at+bodyLength+3] == 0xda;
                var body = pending.GetRange(at + 2, bodyLength).ToArray();
                // Preview has its own fixed metadata/body contract and no payload CRC.
                // B8/B9 remain independent fixed 64-byte, CRC-checked controls.
                if (!tail || (!preview && BitConverter.ToUInt32(body, 60) != NpSdControl.Crc(body, 60))) { at++; continue; }
                frames.Add(Tuple.Create(command, body)); at += length;
            }
            if (at != 0) pending.RemoveRange(0, at);
            // At most one incomplete fixed-length candidate is retained.
            if (pending.Count > 228) pending.Clear();
            return frames;
        }
    }
}
