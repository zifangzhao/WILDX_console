using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace CE32_console
{
    internal sealed partial class NpDownloadStream
    {
        long burstRangeEnd;
        uint burstMaximum,burstRemaining,burstNextLba,burstInitialSequence,burstIssued;
        bool burstOwned,burstCancelAttempted;
        Task<int> burstRead;
        byte[] burstReadBuffer,burstSpare;
        Exception burstFailure;

        // Only the bounded export wrapper opts in. Catalog/quality tests and
        // firmware without NBC1 keep the original single-request path.
        internal void BeginSequentialRange(long first,long last)
        {
            if(first<0||last<first||last>length)throw new ArgumentOutOfRangeException("last");
            if(qualityTestSession||IsSynthetic||maxBlocks!=1020||first%512!=0||last%512!=0||
               !TransportDescription.StartsWith("High-speed USB",StringComparison.Ordinal))return;
            byte[] info;
            try{info=Control(0xc0,0x67,0,0,32);}
            catch(IOException){return;} // Optional query cannot start a read.
            if(info.Length!=32||U(info,0)!=0x3143424e||U(info,4)!=1||U(info,8)<2||
               U(info,8)>4096||U(info,12)!=1020||U(info,24)!=0||U(info,28)!=0)return;
            if(U(info,16)!=0||U(info,20)!=epoch)throw new IOException("SD burst service changed before export; no burst started.");
            burstMaximum=Math.Min(256U,U(info,8));burstRangeEnd=last;
        }
        internal void EndSequentialRange()
        {
            burstRangeEnd=0;burstMaximum=0;
            try{if(burstOwned)RetireBurst();else JoinBurstRead();}
            catch(Exception error){
                if(burstFailure==null){burstFailure=error;throw;}
                burstFailure.Data["BurstRetirementFailure"]=error;
                try{Trace.WriteLine("SD burst retirement failed: "+error.Message);}catch{}
            }
            finally{burstSpare=null;}
        }
        bool PrepareBurstRead(uint lba)
        {
            if(burstOwned){
                if(burstRemaining!=0&&lba==burstNextLba)return true;
                if(burstRemaining!=0)RetireBurst();
                else{JoinBurstRead();WaitForDownloadReady();burstOwned=false;}
            }
            if(burstMaximum==0||lba>burstRangeEnd/512)return false;
            uint count=(uint)Math.Min(burstMaximum,(burstRangeEnd/512-lba)/1020);
            if(count<2)return false;
            if(validatedRead)WaitForDownloadReady();
            burstOwned=true;burstCancelAttempted=false;burstFailure=null;
            burstRemaining=burstIssued=count;burstNextLba=lba;burstInitialSequence=sequence;
            // Mark ownership before ARM: a failed control may have reached the
            // device. Cleanup retires once; neither command is replayed.
            Control(0x40,0x66,(ushort)lba,(ushort)(lba>>16),0);
            Control(0x40,0x64,(ushort)count,0,0);
            return true;
        }
        int ReadBurstReply(byte[] reply,uint lba,uint expectedSequence,uint expectedEpoch)
        {
            int got=0;
            while(got<reply.Length){
                if(Cancelled!=null&&Cancelled())throw new OperationCanceledException();
                byte[] part=got==0?reply:new byte[reply.Length-got];uint n;
                bool ok=NpDownloadNative.WinUsbReadPipe(usb,0x81,part,(uint)(reply.Length-got),out n,IntPtr.Zero);
                int error=Marshal.GetLastWin32Error();
                if(!ok)throw new NativeBulkReadFailure("SD burst read failed (Windows "+error+"); LBA="+lba+". Partial download retained.",error);
                if(n==0||n>reply.Length-got)throw new IOException("SD burst returned an invalid transfer length; partial download retained.");
                if(got!=0)Buffer.BlockCopy(part,0,reply,got,(int)n);got+=(int)n;
                if(got>=32&&got<reply.Length&&got-(int)n<32)
                    ValidateReplyHeader(reply,got,lba,1020,expectedSequence,expectedEpoch);
                if(got==32&&U(reply,16)!=0)break;
            }
            return got;
        }
        int TakeBurstReply(uint lba,out byte[] reply)
        {
            int got;
            if(burstRead!=null){
                Task<int> owned=burstRead;reply=burstReadBuffer;burstRead=null;burstReadBuffer=null;
                got=owned.GetAwaiter().GetResult();
            }else{reply=wireBuffer;got=ReadBurstReply(reply,lba,unchecked(sequence+1),epoch);}
            if(burstRemaining>1){
                byte[] next=burstSpare;
                if(next==null||Object.ReferenceEquals(next,reply))next=new byte[32+1020*512];
                uint nextLba=lba+1020,nextSequence=unchecked(sequence+2),readEpoch=epoch;
                burstReadBuffer=next;
                burstRead=Task.Run(()=>ReadBurstReply(next,nextLba,nextSequence,readEpoch));
                burstSpare=reply;
            }else if(Object.ReferenceEquals(burstSpare,reply))burstSpare=null;
            wireBuffer=reply;return got;
        }
        void CommitBurstReply(uint lba)
        {
            --burstRemaining;burstNextLba=lba+1020;
        }
        void JoinBurstRead()
        {
            Task<int> owned=burstRead;burstRead=null;burstReadBuffer=null;
            if(owned==null)return;
            try{owned.GetAwaiter().GetResult();}
            catch(Exception error){
                // This reply was speculative within the declared range and
                // will be discarded. Preserve the original validation error.
                if(burstFailure!=null)burstFailure.Data["PendingBurstReadFailure"]=error;
            }
        }
        void RetireBurst()
        {
            JoinBurstRead();
            if(burstCancelAttempted)throw new IOException("SD burst retirement outcome is unknown; cancel was not replayed.");
            burstCancelAttempted=true;
            uint nextEpoch=unchecked(epoch+1);
            Control(0x40,0x62,0,0,0);
            var wait=Stopwatch.StartNew();byte[] info;
            while(true){
                info=Control(0xc0,0x60,0,0,32);
                if(info.Length!=32||U(info,0)!=0x3144504e||U(info,4)!=1||U(info,8)!=(uint)(length/512)||
                   U(info,12)!=1020||U(info,24)!=nextEpoch||U(info,28)!=1||
                   unchecked(U(info,20)-burstInitialSequence)>burstIssued)
                    throw new IOException("SD burst retirement identity/epoch/sequence mismatch; no read replayed.");
                if(wait.ElapsedMilliseconds>=1000)throw new TimeoutException("SD burst retirement timed out; no read replayed.");
                uint state=U(info,16);if(state==0)break;
                if(state!=4)throw new IOException("SD burst retirement returned state "+state+"; no read replayed.");
                Thread.Sleep(1);
            }
            if(!NpDownloadNative.WinUsbResetPipe(usb,0x81)||!NpDownloadNative.WinUsbFlushPipe(usb,0x81))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            epoch=nextEpoch;sequence=U(info,20);validatedRead=false;
            burstOwned=false;burstRemaining=0;burstReadBuffer=burstSpare=null;
            cache=null;wireBuffer=null;cachePayloadCount=0;cacheStart=-1;
        }
    }
}
