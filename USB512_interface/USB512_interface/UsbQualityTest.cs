using System;
using System.Diagnostics;
using System.IO;

namespace USB512_interface {
    internal sealed class UsbQualityResult {
        internal string Outcome,Scope,Error;
        internal long Bytes,Blocks;
        internal bool Continuous;
        internal TimeSpan Elapsed;
        internal string Summary {get{return string.Format("USB test {0} | {1:F2} MiB validated | {2:F2} MB/s\r\n{3}",Outcome,Bytes/1048576.0,Elapsed.TotalSeconds>0?Bytes/Elapsed.TotalSeconds/1e6:0,Scope);}}
        internal string Report {get{return Summary+"\r\nMode: "+(Continuous?"Continuous (until Stop or error)":"Bounded (10 seconds / 256 MiB)")+"\r\nValidated blocks: "+Blocks+"\r\nRead time: "+Elapsed.TotalSeconds.ToString("F3")+" s\r\n"+(Error==null?"":Error+"\r\n")+"\r\nCRC/sequence checks describe observed data integrity, not a physical cable bit-error rate. SD-backed results include storage latency. Slow transfers do not establish a bad cable.\r\n";}}
    }
    internal static class UsbQualityTest {
        internal const long MaximumBytes=256L*1024*1024;
        internal const int Seconds=10;
        // The injected clock/read boundary permits a focused check without USB.
        internal static UsbQualityResult Run(Func<int> read,int blockBytes,Func<bool> cancelled,Action<long> progress,Func<TimeSpan> elapsed,string scope,bool continuous=false) {
            if(read==null || elapsed==null || blockBytes<512 || blockBytes>1020*512 || blockBytes%512!=0)throw new ArgumentException("Invalid USB test geometry.");
            var result=new UsbQualityResult{Outcome="completed",Scope=scope,Continuous=continuous};TimeSpan nextProgress=TimeSpan.Zero;
            try {
                while(continuous || (result.Bytes<=MaximumBytes-blockBytes && elapsed()<TimeSpan.FromSeconds(Seconds))) {
                    if(cancelled!=null&&cancelled())throw new OperationCanceledException();
                    int bytes=read();if(bytes!=blockBytes)throw new InvalidDataException("USB test returned a short or oversized validated block.");
                    checked {result.Bytes+=bytes;result.Blocks++;}
                    TimeSpan now=elapsed();if(progress!=null&&now>=nextProgress){progress(result.Bytes);nextProgress=now+TimeSpan.FromMilliseconds(100);}
                }
                if(cancelled!=null&&cancelled())throw new OperationCanceledException();
                if(result.Blocks==0)throw new IOException("No USB test data was received.");
            }catch(OperationCanceledException){result.Outcome=continuous?"stopped":"cancelled";}
            catch(TimeoutException ex){result.Outcome="incomplete";result.Error=ex.Message;}
            catch(Exception ex){result.Outcome="failed";result.Error=ex.ToString();}
            result.Elapsed=elapsed();return result;
        }
        internal static void CheckDeadline(bool continuous,TimeSpan total,TimeSpan block) {
            if((continuous?block:total)>TimeSpan.FromSeconds(Seconds+5))throw new TimeoutException("USB test "+(continuous?"block ":"")+"time budget expired before a complete validated block; no integrity conclusion for the partial tail.");
        }
    }
}
namespace CE32_console {
    internal sealed partial class NpDownloadStream {
        internal USB512_interface.UsbQualityResult TestUsbQuality(Func<bool> cancelled,Action<long> progress,bool continuous=false) {
            int block=checked((int)Math.Min(maxBlocks,length/512)*512);
            if(IsSynthetic && block!=1020*512)throw new IOException("Unsupported RAM diagnostic geometry.");
            var watch=Stopwatch.StartNew();
            var blockWatch=Stopwatch.StartNew();
            Cancelled=()=>{if(cancelled!=null&&cancelled())return true;USB512_interface.UsbQualityTest.CheckDeadline(continuous,watch.Elapsed,blockWatch.Elapsed);return false;};
            return USB512_interface.UsbQualityTest.Run(()=>{
                blockWatch.Restart();
                position=0;Fill(); // Always submit a fresh read; never serve the cache.
                if(IsSynthetic) {
                    for(int word=0;word<cachePayloadCount/4;word++)if(U(cache,32+word*4)!=(unchecked((uint)word*0x9e3779b9U)^0xa5c37e19U)) {
                        USB512_interface.UsbHealth.Integrity(usb,0x81,true,"RAM pattern + header/sequence/CRC");throw new InvalidDataException("RAM diagnostic reference pattern mismatch.");
                    }
                    USB512_interface.UsbHealth.Integrity(usb,0x81,false,"RAM pattern + header/sequence/CRC");
                }
                return cachePayloadCount;
            },block,cancelled,progress,()=>watch.Elapsed,IsSynthetic?"RAM pattern + header/sequence/CRC":"Read-only SD-backed data + header/sequence/CRC",continuous);
        }
    }
}
