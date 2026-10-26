using System;
using System.Collections.Generic;
using CE32_console;

namespace USB512_interface
{
    // Notifications are arbitrary fragments of the UART stream, not packets.
    // Fixed body lengths prevent '<'/'>' inside a body ending a reply. B8/B9
    // additionally carry CRCs; native V1 parameters use their existing ABI.
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
        int CheckedControlAfter(int start)
        {
            for(int i=start;i+67<=pending.Count;i++){
                byte lead=pending[i],cmd=pending[i+1];
                if((lead!=0x3c&&lead!=0xad)||(cmd!=0xb8&&cmd!=0xb9))continue;
                int length=lead==0xad?68:67;if(i+length>pending.Count)continue;
                if(lead==0xad?(pending[i+66]!=0xda||pending[i+67]!=0xda):pending[i+66]!=0x3e)continue;
                var body=pending.GetRange(i+2,64).ToArray();
                if(BitConverter.ToUInt32(body,60)==NpSdControl.Crc(body,60))return i;
            }
            return -1;
        }
        internal List<Tuple<byte, byte[]>> Feed(byte[] bytes)
        {
            pending.AddRange(bytes);
            var frames = new List<Tuple<byte, byte[]>>();
            int at = 0;
            while (at + 2 <= pending.Count) {
                byte lead = pending[at], command = pending[at + 1];
                if ((lead != 0x3c && lead != 0xad) || (command != 0xb9 && command != 0xb8 && command!=0xba && command!=0xbc && command!=0x90 && command!=0x94 && command!=0x95)) { at++; continue; }
                bool preview=command==0xba;
                bool identity=command==0xbc;
                bool native=command==0x90 || command==0x94 || command==0x95;
                uint previewMagic=0;
                if(preview){
                    if(pending.Count-at<6)break;
                    previewMagic=BitConverter.ToUInt32(pending.GetRange(at+2,4).ToArray(),0);
                    if(previewMagic!=NpV2PreviewBurst.Magic && previewMagic!=0x3156504eU){at++;continue;}
                }
                int bodyLength=identity?192:command==0x95?4:native?512:preview?224:64;
                int length = bodyLength+(lead == 0x3c ? 3 : 4);
                if(preview && previewMagic==0x3156504eU && pending.Count-at>=34){
                    int kind=pending[at+31]&15;
                    if(kind!=0 && kind!=2 && kind!=5){at++;continue;}
                }
                bool v1Compact=preview && previewMagic==0x3156504eU && pending.Count-at>=34 && (pending[at+31]&15)==5;
                if(v1Compact){
                    bool impossible=false;
                    for(int i=at+35;i<Math.Min(pending.Count,at+162);i+=2)if(pending[i]>=4){impossible=true;break;}
                    if(impossible){at++;continue;}
                }
                if(preview && previewMagic==NpV2PreviewBurst.Magic && pending.Count-at>=34){
                    byte kind=pending[at+31];
                    if((kind&128)==0 && (kind&64)!=0 && (kind&63)<=2){
                        bool impossible=false;
                        for(int i=at+34;i<Math.Min(pending.Count,at+226);i++)if(pending[i]>=64){impossible=true;break;}
                        // Marked preview bytes cannot contain AD/B9 control leads.
                        // Discard a truncated preview before it holds a CRC-valid control.
                        if(impossible){at++;continue;}
                    }
                    if(kind==(64|5)){
                        bool impossible=false;
                        // Compact words keep only12 serial bits. Their high
                        // byte and fixed zero tail also expose truncated
                        // previews before they can hold a valid control reply.
                        for(int i=at+51;i<Math.Min(pending.Count,at+178);i+=2)if(pending[i]>=16){impossible=true;break;}
                        for(int i=at+178;!impossible && i<Math.Min(pending.Count,at+226);i++)if(pending[i]!=0){impossible=true;break;}
                        if(impossible){at++;continue;}
                    }
                }
                if (pending.Count - at < length) {
                    // A truncated binary preview must not hold a later short
                    // CRC-checked recording receipt indefinitely. Full preview
                    // packets retain their fixed framing; lost data is replayed.
                    if(preview && previewMagic==NpV2PreviewBurst.Magic && pending.Count-at>=34 && (pending[at+31]==6 || pending[at+31]==8)){
                        int next=CheckedControlAfter(at+2);
                        if(next>=0){at=next;continue;}
                    }
                    break;
                }
                bool tail = lead == 0x3c ? pending[at+bodyLength+2] == 0x3e : pending[at+bodyLength+2] == 0xda && pending[at+bodyLength+3] == 0xda;
                var body = pending.GetRange(at + 2, bodyLength).ToArray();
                // Each preview profile validates its own metadata/body and CRC contract.
                // B8/B9 remain independent fixed 64-byte, CRC-checked controls.
                int crcAt=identity?188:60;
                if (!tail || (v1Compact && BitConverter.ToUInt32(body,220)!=NpSdControl.Crc(body,220)) || (!preview && !native && BitConverter.ToUInt32(body, crcAt) != NpSdControl.Crc(body, crcAt))) { at++; continue; }
                frames.Add(Tuple.Create(command, body)); at += length;
            }
            if (at != 0) pending.RemoveRange(0, at);
            // At most one incomplete fixed-length candidate is retained.
            if (pending.Count > 516) pending.Clear();
            return frames;
        }
    }
}
