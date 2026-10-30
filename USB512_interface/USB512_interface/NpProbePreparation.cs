using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace USB512_interface
{
    // Optional probe-only preparation; this never replaces the normal START.
    internal static class NpProbePreparation
    {
        internal const uint RequestMagic=0x4350504e,ReplyMagic=0x5250504e;
        internal const uint Status=0,Prepare=1,Cancel=2,Usb=1,Sd=2;
        internal const uint Empty=0,Preparing=1,Ready=2,Consumed=3,Failed=4,Canceling=5;
        internal sealed class Receipt
        {
            internal uint Result,State,Generation,Mode,Mask,Hz,Fingerprint,Detail;
            internal string Uid;
            internal bool CancelIssued;
            internal string Text {get{return Result!=0?"Probe preparation not confirmed":State==Ready?"Probes ready":State==Preparing?"Preparing probes…":State==Canceling?"Canceling probe preparation…":State==Failed?"Probe preparation failed":"Probe preparation unavailable";}}
        }
        internal sealed class Attempt
        {
            internal string Uid;
            internal uint Mode,Mask,Hz;
            internal bool Unknown=true,StopIssued;
        }
        static void Put(byte[] b,int at,uint v){Buffer.BlockCopy(BitConverter.GetBytes(v),0,b,at,4);}
        static uint U(byte[] b,int at){return BitConverter.ToUInt32(b,at);}
        internal static uint Crc(byte[] b,int count){uint c=~0U;for(int i=0;i<count;i++){c^=b[i];for(int j=0;j<8;j++)c=(c>>1)^((0U-(c&1U))&0xedb88320U);}return ~c;}
        static bool Profile(uint mask,uint hz){return mask!=0&&(mask&~7U)==0&&(hz==7500||hz==15000||hz==20000||hz==30000);}
        static bool Fields(uint op,uint id,uint mode,uint mask,uint hz,uint generation)
        {return id!=0&&op<=Cancel&&(op==Status?(mode|mask|hz|generation)==0:
            (mode==Usb||mode==Sd)&&(op==Cancel?generation!=0&&(mask|hz)==0:Profile(mask,hz)));}
        internal static bool IsRequest(byte[] b){return b!=null&&b.Length==64&&U(b,0)==RequestMagic;}
        internal static byte[] Request(uint op,uint id,uint mode=0,uint mask=0,uint hz=0,uint generation=0)
        {
            if(!Fields(op,id,mode,mask,hz,generation))
                throw new ArgumentException("Invalid probe preparation request.");
            var b=new byte[64];Put(b,0,RequestMagic);Put(b,4,1);Put(b,8,op);Put(b,12,id);
            Put(b,16,mode);Put(b,20,mask);Put(b,24,hz);Put(b,28,generation);Put(b,60,Crc(b,60));return b;
        }
        internal static Receipt Parse(byte[] b,byte[] request)
        {
            if(!IsRequest(request)||U(request,4)!=1||U(request,60)!=Crc(request,60)||
                !Fields(U(request,8),U(request,12),U(request,16),U(request,20),U(request,24),U(request,28))||
                b==null||b.Length!=64||U(b,0)!=ReplyMagic||U(b,4)!=1||
                U(b,8)!=U(request,8)||U(b,12)!=U(request,12)||U(b,60)!=Crc(b,60))
                throw new IOException("Probe preparation receipt identity/version/CRC mismatch.");
            for(int i=32;i<60;i++)if(request[i]!=0)throw new IOException("Invalid probe preparation request extension.");
            var r=new Receipt{Result=U(b,16),State=U(b,20),Generation=U(b,24),Mode=U(b,28),Mask=U(b,32),Hz=U(b,36),Fingerprint=U(b,40),Detail=U(b,44),
                Uid=U(b,48).ToString("X8")+U(b,52).ToString("X8")+U(b,56).ToString("X8")};
            if(r.Result>5||r.State>Canceling||r.Mode>Sd||(U(b,48)|U(b,52)|U(b,56))==0||r.Uid==new string('F',24)||
                ((r.State==Preparing||r.State==Ready||r.State==Consumed||r.State==Canceling)&&
                 (r.Generation==0||r.Mode==0||!Profile(r.Mask,r.Hz))))
                throw new IOException("Invalid probe preparation state.");
            return r;
        }
        internal static bool Matches(byte[] request,byte[] reply)
        {try{Parse(reply,request);return true;}catch(IOException){return false;}}
        internal static void RequireUid(Receipt r,string uid)
        {if(!String.IsNullOrEmpty(uid)&&!String.Equals(uid,r.Uid,StringComparison.OrdinalIgnoreCase))throw new IOException("Probe preparation came from a different logger.");}
        internal static bool MatchesProfile(Receipt r,string uid,uint mode,uint mask,uint hz)
        {return r!=null&&r.Result==0&&r.Generation!=0&&(r.State==Preparing||r.State==Ready)&&r.Mode==mode&&r.Mask==mask&&r.Hz==hz&&
            !String.IsNullOrEmpty(uid)&&String.Equals(r.Uid,uid,StringComparison.OrdinalIgnoreCase);}
        static void RequireSuccess(Receipt r)
        {if(r.Result!=0)throw new IOException("Probe preparation status is unconfirmed ("+r.Result+").");}
        static uint Next(){return BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(),0)|1U;}
        internal static async Task<Receipt> Query(Func<byte[],CancellationToken,Task<byte[]>> exchange,CancellationToken cancel)
        {var q=Request(Status,Next());return Parse(await exchange(q,cancel),q);}
        internal static async Task<Receipt> Begin(Func<byte[],CancellationToken,Task<byte[]>> exchange,uint mode,uint mask,uint hz,
            string uid,Action check,CancellationToken cancel,Action<Receipt> accepted=null,Action<Attempt> attempting=null)
        {
            check();Receipt status;
            // A single read-only discovery attempt; legacy replies remain legacy.
            try{status=await Query(exchange,cancel);}
            catch(Exception ex) when(ex is IOException||ex is TimeoutException||ex is NotSupportedException){check();return null;}
            check();RequireUid(status,uid);
            if(status.Result==1)return null;
            if(status.Result!=0)throw new IOException("Probe preparation status is unavailable. Start remains available.");
            var q=Request(Prepare,Next(),mode,mask,hz,status.Generation);
            var attempt=new Attempt{Uid=status.Uid,Mode=mode,Mask=mask,Hz=hz};
            if(attempting!=null)attempting(attempt);
            // Never replay PREPARE after an uncertain acknowledgement.
            var receipt=Parse(await exchange(q,cancel),q);RequireUid(receipt,status.Uid);
            if(receipt.Result!=0){attempt.Unknown=false;throw new IOException("Probe preparation was not accepted ("+receipt.Result+"). Start remains available.");}
            if(receipt.Mode!=mode||receipt.Mask!=mask||receipt.Hz!=hz||
                (receipt.State!=Preparing&&receipt.State!=Ready))throw new IOException("Probe preparation did not confirm the requested settings.");
            attempt.Unknown=false;
            // Preserve the acknowledged job token before cancellation is observed,
            // so an explicit Stop can cancel this exact job without replaying it.
            if(accepted!=null)accepted(receipt);
            check();
            return receipt;
        }
        internal static async Task<Receipt> CancelPrepared(Func<byte[],CancellationToken,Task<byte[]>> exchange,Receipt expected,
            Action check,CancellationToken cancel,int waitMilliseconds=30000,bool idleSafe=false)
        {
            if(expected==null) return null;
            check();var current=await Query(exchange,cancel);check();RequireUid(current,expected.Uid);
            RequireSuccess(current);
            if(current.State==Empty)return current;
            if(current.Generation!=expected.Generation||current.Mode!=expected.Mode||current.Mask!=expected.Mask||current.Hz!=expected.Hz||
                (current.State!=Preparing&&current.State!=Ready&&current.State!=Canceling&&
                 !(idleSafe&&(current.State==Consumed||current.State==Failed))))
                throw new IOException("Probe preparation ownership changed; no cancellation was sent.");
            if(current.State==Canceling||expected.CancelIssued)return await WaitEmpty(exchange,expected.Uid,check,cancel,waitMilliseconds); // Never replay CANCEL.
            var q=Request(Cancel,Next(),expected.Mode,0,0,expected.Generation);
            expected.CancelIssued=true;
            var reply=Parse(await exchange(q,cancel),q);check();RequireUid(reply,expected.Uid);
            if(reply.Result!=0||(reply.State!=Empty&&reply.State!=Canceling))throw new IOException("Probe preparation cancellation was not confirmed.");
            return reply.State==Empty?reply:await WaitEmpty(exchange,expected.Uid,check,cancel,waitMilliseconds);
        }
        internal static async Task<Receipt> WaitEmpty(Func<byte[],CancellationToken,Task<byte[]>> exchange,string uid,
            Action check,CancellationToken cancel,int waitMilliseconds=30000)
        {
            var wait=System.Diagnostics.Stopwatch.StartNew();
            while(true){
                check();var reply=await Query(exchange,cancel);check();RequireUid(reply,uid);RequireSuccess(reply);
                if(reply.State==Empty)return reply;
                if(reply.State==Failed)throw new IOException("Probe preparation cleanup failed; read device status.");
                if(wait.ElapsedMilliseconds>=waitMilliseconds)throw new IOException("Probe preparation is still retiring; no command replayed.");
                await Task.Delay(100,cancel);
            }
        }
    }
}
