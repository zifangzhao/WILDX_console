using System;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;
namespace CE32_console {
    internal sealed partial class NpDownloadStream {
#if NP_RAW_TEST
        private static bool GetBenchPipePolicy(IntPtr handle,byte pipe,uint policy,ref uint size,out uint value) {
            return NpDownloadNative.GetBenchPipePolicy(handle,pipe,policy,ref size,out value);
        }
#else
        [DllImport("winusb.dll", EntryPoint="WinUsb_GetPipePolicy", SetLastError=true)]
        private static extern bool GetBenchPipePolicy(IntPtr handle,byte pipe,uint policy,ref uint size,out uint value);
#endif
        // Each P/Invoke read is synchronous: the CLR owns its array pin until
        // it returns. All workers are joined before buffers/USB can be freed.
        // RAW_IO requires packet-aligned read capacity <= MAXIMUM_TRANSFER_SIZE.
        internal string BenchmarkRawBurst(int rounds,int readers) {
            if(!IsSynthetic)throw new InvalidOperationException("Synthetic firmware required");
            return RunRawBurst(rounds,readers,0,null,null,0);
        }
        internal string CompareRawDownload(uint startLba,int rounds,int readers,string referencePath) {
            if(IsSynthetic)throw new InvalidOperationException("Real SD firmware required");
            using(var reference=File.OpenRead(referencePath)) {
                if(reference.Length<(long)rounds*1020*512)throw new IOException("Reference is shorter than requested comparison");
                var caps=Control(0xc0,0x67,0,0,32);
                if(U(caps,0)!=0x3143424e || U(caps,4)!=1 || U(caps,8)!=4096 || U(caps,12)!=1020)
                    throw new IOException("Continuous read-only download extension required");
                return RunRawBurst(rounds,readers,startLba,reference,null,0);
            }
        }
        internal string SaveRawDownload(uint startLba,long blocks,int readers,string output,Action<long,long> progress=null) {
            if(IsSynthetic || blocks<1 || blocks>length/512 || startLba>length/512-blocks || readers<1 || readers>4)
                throw new ArgumentOutOfRangeException("Real, in-capacity bounded download required");
            if(File.Exists(output) || File.Exists(output+".status.txt"))throw new IOException("Output already exists");
            var caps=Control(0xc0,0x67,0,0,32);
            if(U(caps,0)!=0x3143424e || U(caps,4)!=1 || U(caps,8)!=4096 || U(caps,12)!=1020)
                throw new IOException("Continuous download extension required");
            if(new DriveInfo(Path.GetPathRoot(Path.GetFullPath(output))).AvailableFreeSpace<blocks*512+67108864)
                throw new IOException("Insufficient output space");
            File.WriteAllText(output+".status.txt","complete=False\nstart_lba="+startLba+"\nrequested_blocks="+blocks+"\n");
            var watch=System.Diagnostics.Stopwatch.StartNew();long done=0;
            using(var destination=new FileStream(output,FileMode.CreateNew,FileAccess.Write,FileShare.Read,131072)) {
                while(blocks-done>=1020) {
                    if(Cancelled!=null && Cancelled())throw new OperationCanceledException();
                    int rounds=(int)Math.Min(4096,(blocks-done)/1020);
                    long batchBase=done*512;
                    RunRawBurst(rounds,readers,checked(startLba+(uint)done),null,destination,batchBase,
                        progress==null ? (Action<long>)null : bytes=>progress(batchBase+bytes,blocks*512));
                    done+=(long)rounds*1020;
                }
                // The bounded protocol deliberately does not round the plan up.
                // A short final tail uses the original checked request path.
                cache=null;cachePayloadCount=0;position=(startLba+done)*512;
                destination.Position=done*512;var tail=new byte[522240];
                while(done<blocks) {
                    int n=Read(tail,0,checked((int)((blocks-done)*512)));
                    if(n==0 || n%512!=0)throw new EndOfStreamException();
                    destination.Write(tail,0,n);done+=n/512;
                    if(progress!=null)progress(done*512,blocks*512);
                }
                destination.Flush(true);
            }
            watch.Stop();
            File.WriteAllText(output+".status.txt","complete=True\nstart_lba="+startLba+"\nblocks="+done+"\nbytes="+(done*512)+"\ntransport_crc_sequence_epoch=passed\nnp_decoding=not_performed\n");
            return "REAL SD RAW SAVE PASS: bytes="+(done*512)+" seconds="+watch.Elapsed.TotalSeconds.ToString("F3")+" MB/s="+(done*512/watch.Elapsed.TotalSeconds/1e6).ToString("F3")+"; transport integrity checked, NP decode not performed";
        }
        private string RunRawBurst(int rounds,int readers,uint startLba,FileStream referenceFile,FileStream destination,long destinationOffset,Action<long> progress=null) {
            if(maxBlocks!=1020 || rounds<1 || rounds>4096 || readers<1 || readers>4 ||
               (long)startLba+(long)rounds*1020>length/512)
                throw new InvalidOperationException("Bounded synthetic benchmark required");
            const int payload=1020*512,wire=payload+32,capacity=1021*512;
            uint size=4,maximum;
            if(!GetBenchPipePolicy(usb,0x81,8,ref size,out maximum) || size!=4 || maximum<capacity)
                throw new IOException("Host maximum transfer size cannot hold a complete benchmark packet");
            uint raw=1;
            if(!NpDownloadNative.WinUsbSetPipePolicy(usb,0x81,7,1,ref raw))throw new IOException("Cannot enable bench RAW_IO");
            var free=new BlockingCollection<byte[]>(readers*2);
            var ready=new BlockingCollection<byte[]>(readers*2);
            var cancel=new CancellationTokenSource();
            for(int i=0;i<readers*2;i++)free.Add(new byte[capacity]);
            var workers=new Thread[readers];int issued=0,active=readers,started=0,received=0;
            Exception failure=null;var seen=new bool[rounds];uint first=unchecked(sequence+1);
            byte[] pattern=new byte[payload];
            if(IsSynthetic)for(uint i=0;i<payload/4;i++)Buffer.BlockCopy(BitConverter.GetBytes(unchecked(i*0x9e3779b9U)^0xa5c37e19U),0,pattern,(int)i*4,4);
            uint reference=IsSynthetic?Crc(pattern,0,payload):0;
            var watch=System.Diagnostics.Stopwatch.StartNew();
            try {
                if(!IsSynthetic)Control(0x40,0x66,(ushort)startLba,(ushort)(startLba>>16),0);
                Control(0x40,0x64,(ushort)rounds,0,0);
                for(int w=0;w<readers;w++) {
                    workers[w]=new Thread(()=>{
                        try {
                            while(Interlocked.Increment(ref issued)<=rounds) {
                                byte[] b=free.Take(cancel.Token);uint n;
                                if(!NpDownloadNative.WinUsbReadPipe(usb,0x81,b,capacity,out n,IntPtr.Zero) || n!=wire)
                                    throw new IOException("RAW_IO failed/short: "+n+", Windows "+Marshal.GetLastWin32Error());
                                ready.Add(b,cancel.Token);
                            }
                        } catch(Exception e) {Interlocked.CompareExchange(ref failure,e,null);cancel.Cancel();}
                        finally {if(Interlocked.Decrement(ref active)==0)ready.CompleteAdding();}
                    });
                    workers[w].IsBackground=true;workers[w].Start();started++;
                }
                foreach(byte[] b in ready.GetConsumingEnumerable(cancel.Token)) {
                    if(Cancelled!=null && Cancelled())throw new OperationCanceledException();
                    // Thread completion order can differ from wire order.
                    // Require each sequence exactly once and its exact LBA.
                    uint index=unchecked(U(b,4)-first);
                    if(index>=rounds || seen[index])throw new InvalidDataException("Missing/duplicate/out-of-range burst sequence");
                    ValidateReplyInPlace(b,wire,startLba+index*1020,1020,unchecked(first+index),epoch);
                    if(IsSynthetic){if(U(b,20)!=reference)throw new InvalidDataException("Synthetic reference CRC mismatch");}
                    else if(referenceFile!=null) {
                        referenceFile.Position=(long)index*payload;int got=0;
                        while(got<payload){int n=referenceFile.Read(pattern,got,payload-got);if(n==0)throw new EndOfStreamException();got+=n;}
                        for(int at=0;at<payload;at+=8)
                            if(BitConverter.ToUInt64(pattern,at)!=BitConverter.ToUInt64(b,at+32))throw new InvalidDataException("Real recording differs at packet "+index+", byte "+at);
                    }
                    if(destination!=null){destination.Position=destinationOffset+(long)index*payload;destination.Write(b,32,payload);}
                    seen[index]=true;received++;free.Add(b,cancel.Token);
                    if(progress!=null)progress((long)received*payload);
                }
                for(int i=0;i<started;i++)workers[i].Join();watch.Stop();
                if(failure!=null)throw new IOException("RAW_IO receiver failed",failure);
                if(received!=rounds)throw new IOException("Incomplete RAW_IO benchmark");
                sequence=unchecked(sequence+(uint)received);
                return (IsSynthetic?"SYNTHETIC RAW PASS":referenceFile!=null?"REAL SD RAW COMPARE PASS":"REAL SD RAW BLOCK PASS")+": readers="+readers+" bytes="+((long)received*payload)+" seconds="+watch.Elapsed.TotalSeconds.ToString("F3")+" MB/s="+((long)received*payload/watch.Elapsed.TotalSeconds/1e6).ToString("F3")+"; all CRCs, unique sequences, LBAs and epochs checked; "+(IsSynthetic?"no SD or live capture":referenceFile!=null?"every byte identical to reference; no live capture":"saved at verified offsets; no live capture");
            } catch(OperationCanceledException) {
                if(failure==null && Cancelled!=null && Cancelled())throw;
                throw new IOException("RAW_IO receiver cancelled after transport failure",failure);
            }
            finally {
                cancel.Cancel();for(int i=0;i<started;i++)workers[i].Join();
                if(received!=rounds){faulted=true;cache=null;try{Control(0x40,0x62,0,0,0);}catch{}}
                raw=0;
                if(!NpDownloadNative.WinUsbSetPipePolicy(usb,0x81,7,1,ref raw)){faulted=true;cache=null;}
                free.Dispose();ready.Dispose();cancel.Dispose();
            }
        }
    }
}
