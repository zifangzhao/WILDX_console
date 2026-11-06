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
        long consumed;
        internal long ReceivedBytes {get{return consumed+pending.Count;}}
        internal void Clear() { pending.Clear();consumed=0; }
        internal static bool DeleteRequestValid(byte[] q)
        {
            if(q==null)return false;
            if(q.Length==8){uint op=BitConverter.ToUInt32(q,0),nonce=BitConverter.ToUInt32(q,4);return op==0?nonce==0:op==1&&nonce!=0;}
            return q.Length==128 && BitConverter.ToUInt32(q,0)==0x3155444e && BitConverter.ToUInt32(q,4)==1 &&
                BitConverter.ToUInt32(q,8)==2 && BitConverter.ToUInt32(q,16)!=0 && BitConverter.ToUInt32(q,124)==NpSdControl.Crc(q,124);
        }
        internal static bool DeleteMatches(byte[] q,byte[] b)
        {
            if(!DeleteRequestValid(q)||b==null||b.Length!=128||BitConverter.ToUInt32(b,0)!=0x3155444e||BitConverter.ToUInt32(b,4)!=1||
                BitConverter.ToUInt32(b,124)!=NpSdControl.Crc(b,124))return false;
            uint op=BitConverter.ToUInt32(b,8),phase=BitConverter.ToUInt32(b,12),nonce=BitConverter.ToUInt32(b,16);
            if(op>2||phase>4||(op==0)!=(phase==0)||op!=0&&nonce==0)return false;
            if(q.Length==8)return BitConverter.ToUInt32(q,0)==0 || op==1&&nonce==BitConverter.ToUInt32(q,4);
            return op==2&&nonce==BitConverter.ToUInt32(q,16)&&BitConverter.ToUInt32(b,96)==BitConverter.ToUInt32(q,96);
        }
        internal static byte[] Frame(byte command, byte[] body)
        {
            if (body == null || (command==0xa7 ? !DeleteRequestValid(body) : body.Length!=64)) throw new ArgumentException("Invalid NP request size or envelope.");
            var frame = new byte[body.Length+3]; frame[0] = 0x3c; frame[1] = command; frame[frame.Length-1] = 0x3e;
            Buffer.BlockCopy(body, 0, frame, 2, body.Length); return frame;
        }
        int CheckedControlAfter(int start)
        {
            for(int i=start;i+67<=pending.Count;i++){
                byte lead=pending[i],cmd=pending[i+1];
                if((lead!=0x3c&&lead!=0xad)||(cmd!=0xb8&&cmd!=0xb9&&cmd!=0xa7))continue;
                int bodyBytes=cmd==0xa7?128:cmd==0xb9&&BitConverter.ToUInt32(pending.GetRange(i+2,4).ToArray(),0)==NpFineClockWire.ReplyMagic?80:64;
                int length=bodyBytes+(lead==0xad?4:3);if(i+length>pending.Count)continue;
                if(lead==0xad?(pending[i+bodyBytes+2]!=0xda||pending[i+bodyBytes+3]!=0xda):pending[i+bodyBytes+2]!=0x3e)continue;
                var body=pending.GetRange(i+2,bodyBytes).ToArray();
                if(BitConverter.ToUInt32(body,bodyBytes-4)==NpSdControl.Crc(body,bodyBytes-4))return i;
            }
            return -1;
        }
        internal List<Tuple<byte, byte[]>> Feed(byte[] bytes)
        {return FeedObserved(bytes,null);}
        // Optional timing metadata uses the actual accepted frame range,
        // never an independent scanner looking inside another frame's body.
        internal List<Tuple<byte, byte[]>> FeedObserved(byte[] bytes,Action<byte,byte[],long,long> accepted)
        {
            pending.AddRange(bytes);
            var frames = new List<Tuple<byte, byte[]>>();
            int at = 0;
            while (at + 2 <= pending.Count) {
                byte lead = pending[at], command = pending[at + 1];
                if ((lead != 0x3c && lead != 0xad) || (command != 0xb9 && command != 0xb8 && command!=0xba && command!=0xbc && command!=0x90 && command!=0x94 && command!=0x95 && command!=0xa7)) { at++; continue; }
                bool preview=command==0xba;
                bool identity=command==0xbc;
                bool native=command==0x90 || command==0x94 || command==0x95;
                uint previewMagic=0;
                if(preview){
                    if(pending.Count-at<6)break;
                    previewMagic=BitConverter.ToUInt32(pending.GetRange(at+2,4).ToArray(),0);
                    if(previewMagic!=NpV2PreviewBurst.Magic && previewMagic!=0x3156504eU){at++;continue;}
                }
                int bodyLength=command==0xa7?128:identity?192:command==0x95?4:native?512:preview?224:64;
                if(command==0xb9&&pending.Count-at>=6&&BitConverter.ToUInt32(pending.GetRange(at+2,4).ToArray(),0)==NpFineClockWire.ReplyMagic)bodyLength=80;
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
                // NPRF is an explicitly tagged 80-byte read-only reply. Other
                // B8/B9 controls retain their established 64-byte framing.
                int crcAt=identity?188:bodyLength-4;
                if (!tail || (v1Compact && BitConverter.ToUInt32(body,220)!=NpSdControl.Crc(body,220)) || (!preview && !native && BitConverter.ToUInt32(body, crcAt) != NpSdControl.Crc(body, crcAt))) { at++; continue; }
                frames.Add(Tuple.Create(command, body));
                if(accepted!=null)accepted(command,body,consumed+at,consumed+at+length);
                at += length;
            }
            if (at != 0){pending.RemoveRange(0, at);consumed+=at;}
            // At most one incomplete fixed-length candidate is retained.
            if (pending.Count > 516){consumed+=pending.Count;pending.Clear();}
            return frames;
        }
    }
}
