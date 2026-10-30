using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace USB512_interface
{
    internal sealed class NpSdTestResult
    {
        public int Schema=1;
        public string PathKind,Target,Identity,StartedUtc=DateTime.UtcNow.ToString("O"),Status="Not tested",Error,CleanupWarning;
        public long RequestedBytes,WrittenBytes,VerifiedBytes;
        public double WriteSeconds,ReadSeconds,WriteIoSeconds,ReadIoSeconds;
        public double? MaxWriteMs,P95WriteMs,P99WriteMs;
        public uint? StoredSourceGaps,OverflowEvents,RecordedCardCidCrc,CurrentCardCidCrc;
        public ulong? MissingSourceTicks,DroppedBlocks,ProducerDiscardedTicks;
        public string Scope;
        public int LoggerVersion;
        public uint ProbeMask,SampleHz,Session,Chunks,CardCapacitySectors,StartRequestId,RequestedBlocks,ExpectedChunks;
        public long StartLba,EndLba,RequestedEndLba;
        public string DeviceUid,Firmware,Details,ReportFolder;
        public double RecordingSeconds;
        public bool RecordingCompleted,LossCountersAvailable;
        public uint? CardCidCrc;
        public double WriteMBs {get{return WriteSeconds>0?WrittenBytes/WriteSeconds/1e6:0;}}
        public double ReadMBs {get{return ReadSeconds>0?VerifiedBytes/ReadSeconds/1e6:0;}}
        static string Latency(double? value){return value.HasValue?value.Value.ToString("F1")+" ms":"n/a";}
        public string Summary()
        {
            if(PathKind=="logger")return Status+"\r\n"+Target+"\r\n"+Identity+"\r\n"+Details+"\r\n"+(Error??"")+"\r\n"+Scope;
            return Status+"\r\n"+Target+"\r\n"+Identity+"\r\n"+
                "Written "+(WrittenBytes/1048576.0).ToString("N0")+" MiB · verified "+(VerifiedBytes/1048576.0).ToString("N0")+" MiB\r\n"+
                "Write "+WriteMBs.ToString("F1")+" MB/s · read + verify "+ReadMBs.ToString("F1")+" MB/s\r\n"+
                "Write calls: longest "+Latency(MaxWriteMs)+" · p95 "+Latency(P95WriteMs)+" · p99 "+Latency(P99WriteMs)+"\r\n"+
                (Error==null?"":Error+"\r\n")+(CleanupWarning==null?"":CleanupWarning+"\r\n")+Scope;
        }
    }

    // File-only test: never opens a raw disk, edits a partition or formats media.
    internal static class NpSdCardTest
    {
        internal const int BlockBytes=1048576;
        const long FileBytes=1024L*1024*1024;
        internal sealed class Volume
        {
            internal string Root,Label;
            public override string ToString(){return Label;}
        }
        internal static List<Volume> Volumes()
        {
            var removable=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var d in DriveInfo.GetDrives())if(d.DriveType==DriveType.Removable)removable.Add(d.Name);
            // Some USB readers advertise a fixed disk. Resolve their mounted volumes,
            // rather than offering every internal disk or accepting a typed raw path.
            try
            {
            using(var search=new ManagementObjectSearcher("SELECT DeviceID,InterfaceType,MediaType FROM Win32_DiskDrive"))
            using(var disks=search.Get())foreach(ManagementObject disk in disks)
            {
                if(Convert.ToString(disk["InterfaceType"])!="USB" && Convert.ToString(disk["MediaType"]).IndexOf("Removable",StringComparison.OrdinalIgnoreCase)<0)continue;
                using(var parts=disk.GetRelated("Win32_DiskPartition"))foreach(ManagementObject part in parts)
                using(var logical=part.GetRelated("Win32_LogicalDisk"))foreach(ManagementObject drive in logical)removable.Add(Convert.ToString(drive["DeviceID"])+"\\");
            }
            }
            catch(ManagementException){} // Mounted removable drives remain usable if WMI is unavailable.
            catch(UnauthorizedAccessException){}
            string system=Path.GetPathRoot(Environment.SystemDirectory);
            var result=new List<Volume>();
            foreach(string root in removable.OrderBy(x=>x))try
            {
                var d=new DriveInfo(root);
                if(!d.IsReady || String.Equals(root,system,StringComparison.OrdinalIgnoreCase))continue;
                result.Add(new Volume {Root=root,Label=root+"  "+d.VolumeLabel+" · "+d.DriveFormat+" · "+(d.AvailableFreeSpace/1073741824.0).ToString("F1")+" GiB free"});
            }catch(IOException){}
            return result;
        }
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
        static extern bool GetVolumeInformation(string root,StringBuilder name,int nameSize,out uint serial,out uint maxName,out uint flags,StringBuilder fs,int fsSize);
        internal static string Identity(string folder)
        {
            uint serial,max,flags;var name=new StringBuilder(261);var fs=new StringBuilder(261);
            if(!GetVolumeInformation(Path.GetPathRoot(Path.GetFullPath(folder)),name,name.Capacity,out serial,out max,out flags,fs,fs.Capacity))throw new Win32Exception(Marshal.GetLastWin32Error());
            return "Windows volume "+serial.ToString("X8")+" · "+name+" · "+fs+" (volume ID, not SD CID)";
        }
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
        static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
        [DllImport("kernel32.dll",SetLastError=true)] static extern bool WriteFile(SafeFileHandle file,IntPtr buffer,int count,out int written,IntPtr overlapped);
        [DllImport("kernel32.dll",SetLastError=true)] static extern bool ReadFile(SafeFileHandle file,IntPtr buffer,int count,out int read,IntPtr overlapped);
        [DllImport("kernel32.dll",SetLastError=true)] static extern bool FlushFileBuffers(SafeFileHandle file);
        static SafeFileHandle Open(string path,bool write)
        {
            var handle=CreateFile(path,write?0x40000000U:0x80000000U,0,IntPtr.Zero,write?1U:3U,
                0x20000000U|0x80000000U|0x08000000U|0x00200000U,IntPtr.Zero); // Unbuffered, write-through, sequential, no reparse following.
            if(handle.IsInvalid){int error=Marshal.GetLastWin32Error();handle.Dispose();throw new Win32Exception(error,"Cannot open the unbuffered SD test file.");}
            return handle;
        }
        internal static void Pattern(byte[] bytes,ulong block,uint seed)
        {
            uint state=seed^(uint)block^((uint)(block>>32)*0x9e3779b9U)^0xa5366b4dU;
            if(state==0)state=1;
            for(int i=0;i<bytes.Length;i+=4){state^=state<<13;state^=state>>17;state^=state<<5;
                bytes[i]=(byte)state;bytes[i+1]=(byte)(state>>8);bytes[i+2]=(byte)(state>>16);bytes[i+3]=(byte)(state>>24);}
            Buffer.BlockCopy(BitConverter.GetBytes(block),0,bytes,0,8);
            Buffer.BlockCopy(BitConverter.GetBytes(seed),0,bytes,8,4);
        }
        internal static int Mismatch(byte[] expected,byte[] actual)
        {for(int i=0;i<expected.Length;i++)if(expected[i]!=actual[i])return i;return -1;}
        static double Percentile(List<double> sorted,double p){return sorted.Count==0?0:sorted[Math.Max(0,(int)Math.Ceiling(sorted.Count*p)-1)];}

        internal static NpSdTestResult Run(string folder,long total,CancellationToken cancel,Action<int,string> progress)
        {
            if(total<BlockBytes || total>32L*1024*1024*1024 || total%BlockBytes!=0)throw new ArgumentException("Choose a sector-aligned test size from 1 MiB to 32 GiB.");
            folder=Path.GetFullPath(folder);
            if(!Directory.Exists(folder)||(File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)throw new IOException("A local, existing card folder is required.");
            var volume=new DriveInfo(Path.GetPathRoot(folder));
            if(total>volume.AvailableFreeSpace-256L*1024*1024)throw new IOException("Not enough free space. The test leaves at least 256 MiB free.");
            var result=new NpSdTestResult {PathKind="pc_card_reader",Target=folder,Identity=Identity(folder),RequestedBytes=total,
                Scope="PC reader + card path only. 1 MiB unbuffered, write-through I/O; readback compares every byte. Rates include host pattern generation/verification. Call latencies are host-observed, not isolated SD command timing. This test does not qualify any logger recording profile or prove full card capacity."};
            string directory=Path.Combine(folder,"WILDX_SD_TEST_"+Guid.NewGuid().ToString("N"));
            var files=new List<string>();var latencies=new List<double>();
            IntPtr allocation=IntPtr.Zero;var phase=new Stopwatch();
            try
            {
                cancel.ThrowIfCancellationRequested();Directory.CreateDirectory(directory);
                allocation=Marshal.AllocHGlobal(BlockBytes+65536);
                var aligned=new IntPtr((allocation.ToInt64()+65535)&~65535L);
                var expected=new byte[BlockBytes];var actual=new byte[BlockBytes];
                uint seed=BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(),0);ulong block=0;
                var estimate=new NpProgressEstimate();phase.Start();
                while(result.WrittenBytes<total)
                {
                    if(Identity(folder)!=result.Identity)throw new IOException("The selected volume changed.");
                    string path=Path.Combine(directory,files.Count.ToString("D4")+".tmp");
                    long count=Math.Min(FileBytes,total-result.WrittenBytes);
                    using(var file=Open(path,true))
                    {
                        files.Add(path);
                        for(long at=0;at<count;at+=BlockBytes)
                        {
                            cancel.ThrowIfCancellationRequested();Pattern(expected,block++,seed);Marshal.Copy(expected,0,aligned,BlockBytes);
                            var io=Stopwatch.StartNew();int bytes;
                            if(!WriteFile(file,aligned,BlockBytes,out bytes,IntPtr.Zero))throw new Win32Exception(Marshal.GetLastWin32Error());
                            double ms=io.Elapsed.TotalMilliseconds;latencies.Add(ms);result.WriteIoSeconds+=ms/1000;
                            if(bytes!=BlockBytes)throw new IOException("Incomplete test write.");
                            result.WrittenBytes+=bytes;result.WriteSeconds=phase.Elapsed.TotalSeconds;
                            if(progress!=null)progress((int)(result.WrittenBytes*50/total),"Writing · "+estimate.Describe(result.WrittenBytes,total));
                        }
                        if(!FlushFileBuffers(file))throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                }
                result.WriteSeconds=phase.Elapsed.TotalSeconds;phase.Restart();estimate=new NpProgressEstimate();block=0;
                foreach(string path in files)
                {
                    if(Identity(folder)!=result.Identity)throw new IOException("The selected volume changed before verification.");
                    long count=new FileInfo(path).Length;
                    using(var file=Open(path,false))for(long at=0;at<count;at+=BlockBytes)
                    {
                        cancel.ThrowIfCancellationRequested();var io=Stopwatch.StartNew();int bytes;
                        if(!ReadFile(file,aligned,BlockBytes,out bytes,IntPtr.Zero))throw new Win32Exception(Marshal.GetLastWin32Error());
                        result.ReadIoSeconds+=io.Elapsed.TotalSeconds;
                        if(bytes!=BlockBytes)throw new IOException("Incomplete test read.");
                        Marshal.Copy(aligned,actual,0,bytes);Pattern(expected,block++,seed);
                        int mismatch=Mismatch(expected,actual);
                        if(mismatch>=0)throw new IOException("Readback mismatch at test byte "+(result.VerifiedBytes+mismatch)+".");
                        result.VerifiedBytes+=bytes;result.ReadSeconds=phase.Elapsed.TotalSeconds;
                        if(progress!=null)progress(50+(int)(result.VerifiedBytes*50/total),"Verifying · "+estimate.Describe(result.VerifiedBytes,total));
                    }
                }
                result.ReadSeconds=phase.Elapsed.TotalSeconds;
                if(result.VerifiedBytes!=total)throw new IOException("Readback did not cover the complete test.");
                result.Status="Passed reader write/readback test";
            }
            catch(OperationCanceledException){result.Status="Cancelled — not passed";}
            catch(Exception ex){result.Status="Failed";result.Error=ex.Message;}
            finally
            {
                if(allocation!=IntPtr.Zero)Marshal.FreeHGlobal(allocation);
                latencies.Sort();if(latencies.Count>0){result.MaxWriteMs=latencies[latencies.Count-1];
                result.P95WriteMs=Percentile(latencies,.95);result.P99WriteMs=Percentile(latencies,.99);}
                try
                {
                    if(Identity(folder)!=result.Identity)throw new IOException("Volume changed; test files were not removed.");
                    foreach(string path in files)File.Delete(path);
                    if(Directory.Exists(directory))Directory.Delete(directory,false);
                }
                catch(Exception ex){result.CleanupWarning="Test-file cleanup incomplete: "+directory+". "+ex.Message;}
            }
            return result;
        }
    }
}
