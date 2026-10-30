using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace USB512_interface {
    internal sealed class NpRtcStartBinding {
        internal readonly byte[] Receipt,Anchor;
        internal readonly string Uid;
        internal readonly uint Family,Session;
        internal readonly ulong Token;
        internal readonly bool Continuous64;
        internal NpRtcStartBinding(byte[] receipt,byte[] anchor,string uid,uint family,uint session,ulong token,bool continuous64){
            Receipt=(byte[])receipt.Clone();Anchor=(byte[])anchor.Clone();Uid=uid;Family=family;Session=session;Token=token;Continuous64=continuous64;
        }
    }
    internal sealed class NpRtcStartStamp {
        internal uint Flags,Family,Session,Generation,SourceEpoch,Domain,FineHz,CalibrationGeneration;
        internal string Uid;
        internal ulong Token,UtcLowerUs,UtcUpperUs,Before64,After64;
        internal uint Psca=74,Pscs=9999,InputHz=750000,Ssr,PcUncertaintyUs,ReceiptCrc,Calr;
    }
    internal enum NpRtcStartReason { Ok,Invalid,Flags,Configuration,Calendar,Counter,Overflow }
    internal sealed class NpRtcStartBounds {
        internal long LowerUs,UpperUs;
        internal uint PcUncertaintyUs;
        internal NpRtcStartReason Reason=NpRtcStartReason.Invalid;
        internal string Error;
        internal bool Qualified {get{return Reason==NpRtcStartReason.Ok;}}
        internal long WidthUs {get{return Qualified?UpperUs-LowerUs:0;}}
        internal decimal HalfWidthUs {get{return WidthUs/2m;}}
        internal bool SubMillisecond {get{return Qualified&&WidthUs<1000;}}
    }
    // Pure source-bound evidence only; no RTC reads/writes or feature policy.
    internal static class NpRtcStartWire {
        internal const int Bytes=128;
        internal const uint RequiredFlags=59,PcCalibrated=4,UsbFeatures=15,PageMagic=0x47505452;
        internal const ulong MinUtcUs=946684800000000,EndUtcUs=4102444800000000;
        static uint U(byte[] b,int o){return BitConverter.ToUInt32(b,o);}
        static ulong L(byte[] b,int o){return BitConverter.ToUInt64(b,o);}
        static void Put(byte[] b,int o,uint n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,o,4);}
        static void Put64(byte[] b,int o,ulong n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,o,8);}
        static bool Zero(byte[] b,int o,int n){for(int i=o;i<o+n;i++)if(b[i]!=0)return false;return true;}
        static void Need(bool ok,string why){if(!ok)throw new InvalidDataException("NP RTC start: "+why);}
        static void Seal(byte[] b){Put(b,b.Length-4,NpFirstSyncWire.Crc(b,b.Length-4));}
        static string Uid(byte[] b,int at){return U(b,at).ToString("X8",CultureInfo.InvariantCulture)+U(b,at+4).ToString("X8",CultureInfo.InvariantCulture)+U(b,at+8).ToString("X8",CultureInfo.InvariantCulture);}
        static NpFirstSyncReceipt Check(NpRtcStartBinding b){
            Need(b!=null&&b.Continuous64&&b.Token!=0&&(b.Family==1||b.Family==2),"explicit continuous64 binding required");
            NpFirstSyncUsb.UidWords(b.Uid);var r=NpFirstSyncWire.ValidateSourceAnchor(b.Anchor,b.Receipt);
            Need(r.Family==b.Family&&(b.Session==0?r.Session==r.Generation:r.Session==b.Session),"source family/session");return r;
        }
        internal static NpRtcStartBinding BindSd(byte[] clock,byte[] companion,int family,uint session,uint apHz,string uid,ulong token){
            Need(session!=0&&uid!=null&&token!=0,"explicit SD identity/session/token");
            var c=NpFineClockWire.ReadCompanion(companion,clock,family,session,apHz,uid,token);
            Need(c!=null&&c.FineCounter64,"SD continuous64 declaration required");
            var b=new NpRtcStartBinding(c.Bound.Receipt.Raw,c.Anchor,c.Bound.Uid,(uint)family,session,token,true);Check(b);return b;
        }
        internal static NpRtcStartBinding BindUsb(byte[] receipt,byte[] anchor,int family,string uid,ulong token,uint selectedFeatures){
            Need(selectedFeatures==UsbFeatures&&receipt!=null&&anchor!=null,"explicit RTC/continuous64 USB selection required");
            var b=new NpRtcStartBinding(receipt,anchor,uid,(uint)family,0,token,true);Check(b);return b;
        }
        internal static NpRtcStartStamp Parse(byte[] raw,NpRtcStartBinding b){
            var r=Check(b);Need(raw!=null&&raw.Length==Bytes,"body size");
            Need(Encoding.ASCII.GetString(raw,0,8)=="NPRTC001"&&BitConverter.ToUInt16(raw,8)==1&&BitConverter.ToUInt16(raw,10)==Bytes&&
                U(raw,124)==NpFirstSyncWire.Crc(raw,124)&&(U(raw,12)&~63U)==0,"body tag/version/flags/CRC");
            Need(String.Equals(Uid(raw,16),b.Uid,StringComparison.OrdinalIgnoreCase)&&U(raw,28)==b.Family&&U(raw,32)==b.Session&&
                U(raw,36)==r.Generation&&U(raw,40)==r.SourceEpoch&&U(raw,44)==r.Domain&&U(raw,48)==r.CounterHz&&
                L(raw,56)==b.Token&&U(raw,116)==U(r.Raw,324),"body UID/token/source context/receipt CRC");
            var s=new NpRtcStartStamp {Flags=U(raw,12),Uid=Uid(raw,16),Family=U(raw,28),Session=U(raw,32),Generation=U(raw,36),SourceEpoch=U(raw,40),
                Domain=U(raw,44),FineHz=U(raw,48),CalibrationGeneration=U(raw,52),Token=L(raw,56),UtcLowerUs=L(raw,64),UtcUpperUs=L(raw,72),
                Before64=L(raw,80),After64=L(raw,88),Psca=U(raw,96),Pscs=U(raw,100),InputHz=U(raw,104),Ssr=U(raw,108),
                PcUncertaintyUs=U(raw,112),ReceiptCrc=U(raw,116),Calr=U(raw,120)};
            Need((s.Flags&PcCalibrated)!=0?s.CalibrationGeneration!=0:s.CalibrationGeneration==0&&s.PcUncertaintyUs==0,"PC calibration generation/uncertainty flag");return s;
        }
        internal static byte[] Pack(NpRtcStartStamp s,NpRtcStartBinding b){
            Need(s!=null,"stamp required");var r=Check(b);var raw=new byte[Bytes];Encoding.ASCII.GetBytes("NPRTC001").CopyTo(raw,0);
            Put(raw,8,1U|(128U<<16));Put(raw,12,s.Flags);var uid=NpFirstSyncUsb.UidWords(b.Uid);for(int i=0;i<3;i++)Put(raw,16+i*4,uid[i]);
            uint[] context={b.Family,b.Session,r.Generation,r.SourceEpoch,r.Domain,r.CounterHz,s.CalibrationGeneration};for(int i=0;i<context.Length;i++)Put(raw,28+i*4,context[i]);
            Put64(raw,56,b.Token);Put64(raw,64,s.UtcLowerUs);Put64(raw,72,s.UtcUpperUs);Put64(raw,80,s.Before64);Put64(raw,88,s.After64);
            uint[] tail={s.Psca,s.Pscs,s.InputHz,s.Ssr,s.PcUncertaintyUs,U(r.Raw,324),s.Calr};for(int i=0;i<tail.Length;i++)Put(raw,96+i*4,tail[i]);
            Seal(raw);Parse(raw,b);return raw;
        }
        // Exact Euclidean quotient/remainder rounding, matching the C helper.
        static long Offset(long delta,ulong source,uint f,uint h,bool up){
            checked{
                long q=delta/f,rem=delta%f;if(rem<0){q--;rem+=f;}
                ulong sq=source/h,sr=source%h;long value=q*1000000+(long)(sq*1000000);
                ulong an=(ulong)rem*1000000,bn=sr*1000000,n=(an%f)*h+(bn%h)*f,d=(ulong)f*h;
                return value+(long)(an/f+bn/h+n/d+(up&&n%d!=0?1UL:0UL));
            }
        }
        internal static NpRtcStartBounds Map(byte[] raw,NpRtcStartBinding b){
            var result=new NpRtcStartBounds();
            try{
                var s=Parse(raw,b);result.PcUncertaintyUs=s.PcUncertaintyUs;
                result.Reason=NpRtcStartReason.Flags;if((s.Flags&RequiredFlags)!=RequiredFlags)return result;
                result.Reason=NpRtcStartReason.Configuration;if(s.Psca!=74||s.Pscs!=9999||s.InputHz!=750000||s.Calr!=0)return result;
                result.Reason=NpRtcStartReason.Calendar;
                if(s.Ssr>9999||s.UtcLowerUs<MinUtcUs||s.UtcUpperUs>EndUtcUs||s.UtcLowerUs>EndUtcUs-100||s.UtcUpperUs!=s.UtcLowerUs+100||
                    s.UtcLowerUs%1000000!=(9999UL-s.Ssr)*100)return result;
                result.Reason=NpRtcStartReason.Counter;if(s.Before64>s.After64||s.After64>=(ulong)Int64.MaxValue)return result;
                result.Reason=NpRtcStartReason.Overflow;
                checked{
                    ulong ticks=L(b.Anchor,44)+L(b.Receipt,52);uint c=U(b.Anchor,28),w=unchecked(U(b.Anchor,32)-c);
                    long lo=Offset((long)c-(long)(s.After64+1),ticks,s.FineHz,U(b.Receipt,32),false);
                    long hi=Offset((long)c+w+1-(long)s.Before64,ticks,s.FineHz,U(b.Receipt,32),true);
                    long lower=(long)s.UtcLowerUs+lo,upper=(long)s.UtcUpperUs+hi;
                    if(lower<(long)MinUtcUs||upper>(long)EndUtcUs||lower>upper)return result;
                    result.LowerUs=lower;result.UpperUs=upper;result.Reason=NpRtcStartReason.Ok;return result;
                }
            }catch(InvalidDataException error){result.Reason=NpRtcStartReason.Invalid;result.Error=error.Message;return result;}
            catch(OverflowException error){result.Reason=NpRtcStartReason.Overflow;result.Error=error.Message;return result;}
        }
        internal static byte[] Sector(byte[] raw,NpRtcStartBinding b){Parse(raw,b);var output=new byte[512];Buffer.BlockCopy(raw,0,output,0,Bytes);Seal(output);return output;}
        internal static NpRtcStartStamp ParseSector(byte[] sector,NpRtcStartBinding b){
            Need(sector!=null&&sector.Length==512&&Zero(sector,128,380)&&U(sector,508)==NpFirstSyncWire.Crc(sector,508),"SD sector padding/CRC");return Parse(sector.Take(Bytes).ToArray(),b);
        }
        internal static byte[] Page(byte[] raw,NpRtcStartBinding b,int index,ulong sequence){
            Parse(raw,b);Need(b.Session==0&&index>=0&&index<3&&sequence!=UInt64.MaxValue,"USB page mode/index/sequence");var output=new byte[108];
            Put(output,0,NpFirstSyncUsb.EnvelopeMagic);Put(output,4,0x60001);Put64(output,8,b.Token);Put(output,16,U(b.Receipt,20));
            var uid=NpFirstSyncUsb.UidWords(b.Uid);for(int i=0;i<3;i++)Put(output,20+4*i,uid[i]);Put64(output,32,sequence);
            Put(output,40,PageMagic);output[44]=1;output[45]=(byte)index;output[46]=3;Put(output,48,U(raw,124));Buffer.BlockCopy(raw,index*48,output,52,index==2?32:48);
            Put(output,100,NpFirstSyncWire.Crc(output.Skip(40).Take(64).ToArray(),60));Seal(output);return output;
        }
        internal static byte[] ParsePage(byte[] page,NpRtcStartBinding b,ulong sequence){
            Check(b);Need(page!=null&&page.Length==108&&b.Session==0&&sequence!=UInt64.MaxValue,"USB envelope size/mode/sequence");
            Need(U(page,0)==NpFirstSyncUsb.EnvelopeMagic&&U(page,4)==0x60001&&L(page,8)==b.Token&&U(page,16)==U(b.Receipt,20)&&
                String.Equals(Uid(page,20),b.Uid,StringComparison.OrdinalIgnoreCase)&&L(page,32)==sequence&&U(page,104)==NpFirstSyncWire.Crc(page,104),"USB envelope context/CRC");
            var p=page.Skip(40).Take(64).ToArray();Need(U(p,0)==PageMagic&&p[4]==1&&p[5]<3&&p[6]==3&&p[7]==0&&U(p,60)==NpFirstSyncWire.Crc(p,60),"USB page body");
            Need(p[5]!=2||Zero(p,44,16)&&U(p,40)==U(p,8),"USB last-page padding/hash");return p;
        }
    }
    internal sealed class NpRtcStartAssembler {
        readonly NpRtcStartBinding binding;
        readonly byte[][] pages=new byte[3][];
        readonly ulong[] sequences=new ulong[3];
        uint hash,seen;byte[] frozen;
        internal bool Failed {get;private set;}
        internal string Error {get;private set;}
        internal bool Complete {get{return !Failed&&frozen!=null;}}
        internal byte MissingMask {get{return (byte)(7&~seen);}}
        internal byte[] Raw {get{return frozen==null?null:(byte[])frozen.Clone();}}
        internal NpRtcStartAssembler(NpRtcStartBinding binding){this.binding=binding;}
        bool Reject(string why){Failed=true;Error=why;return false;}
        internal bool Feed(byte[] envelope,ulong sequence){
            if(Failed)return false;
            try{
                var page=NpRtcStartWire.ParsePage(envelope,binding,sequence);int index=page[5];uint h=BitConverter.ToUInt32(page,8);
                if(seen!=0&&hash!=h)return Reject("Mixed RTC page-set hash");
                if(pages[index]!=null)return sequences[index]==sequence&&pages[index].SequenceEqual(page)||Reject("Changed RTC duplicate page/sequence");
                for(int i=0;i<3;i++)if(pages[i]!=null&&sequences[i]==sequence)return Reject("RTC page sequence collision");
                pages[index]=page;sequences[index]=sequence;hash=h;seen|=1U<<index;
                if(seen==7){
                    var raw=new byte[128];for(int i=0;i<3;i++)Buffer.BlockCopy(pages[i],12,raw,i*48,i==2?32:48);
                    if(BitConverter.ToUInt32(raw,124)!=hash)return Reject("RTC body/page hash mismatch");NpRtcStartWire.Parse(raw,binding);frozen=raw;
                }
                return true;
            }catch(InvalidDataException error){return Reject(error.Message);}
        }
    }
}
