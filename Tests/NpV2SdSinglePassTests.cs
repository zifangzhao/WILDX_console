using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

// File-only public SDK regression. Baseline and candidate run in separate
// processes so identical assembly identities cannot resolve to the wrong SDK.
internal static class NpV2SdSinglePassTests {
    const uint Start=8192;
    const long GroupNeuralBytes=4L*46592, ReorderLimit=8L*1024*1024;
    static int checks;
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer { MaxJsonLength=16*1024*1024 };
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static string Hash(string path){using(var h=SHA256.Create())using(var f=File.OpenRead(path))return BitConverter.ToString(h.ComputeHash(f)).Replace("-","");}
    static object Invoke(MethodInfo method,object[] args){try{return method.Invoke(null,args);}catch(TargetInvocationException e){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException??e).Throw();throw;}}
    static Dictionary<string,object> ReadJson(string path){return Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));}
    static ulong Number(object value){return Convert.ToUInt64(value,CultureInfo.InvariantCulture);}
    static void WriteJson(string path,object value){File.WriteAllText(path,Json.Serialize(value)+Environment.NewLine);}
    static object Field(object value,string name){var f=value.GetType().GetField(name);return f==null?null:f.GetValue(value);}
    static string Quote(string value){if(value.Contains("\""))throw new ArgumentException("No quotes in fixture paths");return "\""+value+"\"";}

    sealed class ForwardCard:Stream {
        readonly FileStream input;
        readonly long low,high;
        readonly bool enforce;
        readonly long failAt;
        long next;
        internal ulong PayloadRead;
        internal readonly long GroupBytes;
        internal readonly uint Groups;
        internal ForwardCard(string image,bool strict,long failGroup){
            input=File.OpenRead(image);var d=new byte[512];var f=new byte[512];
            input.Position=(Start+3L)*512;Exact(input,d);input.Position=(Start+4L)*512;Exact(input,f);
            low=BitConverter.ToUInt32(d,60)*512L;high=BitConverter.ToUInt32(f,116)*512L;
            GroupBytes=(364L+BitConverter.ToUInt32(d,228))*512;Groups=BitConverter.ToUInt32(f,112);
            next=low;enforce=strict;failAt=failGroup<0?long.MaxValue:low+failGroup*GroupBytes+512;input.Position=0;
        }
        static void Exact(Stream s,byte[] bytes){int at=0,n;while(at<bytes.Length){n=s.Read(bytes,at,bytes.Length-at);if(n==0)throw new EndOfStreamException();at+=n;}}
        public override int Read(byte[] buffer,int offset,int count){
            long before=input.Position;
            if(before>=failAt)throw new IOException("Injected physical source read failure after spill");
            count=(int)Math.Min(count,failAt-before);
            int n=input.Read(buffer,offset,count);long first=Math.Max(low,before),last=Math.Min(high,before+n);
            if(last>first){if(enforce&&first!=next)throw new InvalidOperationException("Physical payload reread/backward/skip: expected "+next+", got "+first);next=last;PayloadRead+=(ulong)(last-first);}
            return n;
        }
        internal void Complete(){Check(PayloadRead==(ulong)(high-low),"Committed physical payload was not read exactly once");Check(next==high,"Forward payload scan did not reach committed end");}
        public override bool CanRead{get{return true;}}public override bool CanSeek{get{return true;}}public override bool CanWrite{get{return false;}}
        public override long Length{get{return 65536L*512;}}public override long Position{get{return input.Position;}set{input.Position=value;}}
        public override long Seek(long value,SeekOrigin origin){return input.Seek(value,origin);}
        public override void Flush(){}public override void SetLength(long value){throw new NotSupportedException();}public override void Write(byte[] b,int o,int n){throw new NotSupportedException();}
        protected override void Dispose(bool disposing){if(disposing)input.Dispose();base.Dispose(disposing);}
    }

    static int ExportChild(string[] args){
        // --export SDK IMAGE OUTPUT STRICT CANCEL_AFTER_GROUP FAIL_AFTER_GROUP
        string output=args[3];bool strict=args[4]=="1";int cancelGroup=int.Parse(args[5]),failGroup=int.Parse(args[6]);
        var report=new Dictionary<string,object>();bool spillSeen=false;
        using(var card=new ForwardCard(args[2],strict,failGroup)){
            try{
                var assembly=Assembly.LoadFrom(Path.GetFullPath(args[1]));var type=assembly.GetType("USB512_interface.NpV2SdDatExport",true);
                Func<bool> cancel=()=>{spillSeen|=Directory.Exists(output)&&Directory.GetFiles(output,".np2-reorder-*").Length!=0;return cancelGroup>=0&&card.PayloadRead>=(ulong)(cancelGroup*card.GroupBytes);};
                var progress=new List<int>();var method=type.GetMethod("ExportCommitted",BindingFlags.Public|BindingFlags.Static);
                object result=Invoke(method,new object[]{card,Start,output,0L,cancel,new Action<int>(progress.Add),null});
                if(strict)card.Complete();
                var fields=new Dictionary<string,object>();
                foreach(string name in new[]{"RecordingComplete","SampleHz","Session","CommittedBlocks","ProbeMask","StoredState","StoredFault","LogicalPrefixBlocks","LogicalHighWaterBlocks","AdmittedHighWaterKnown","AdmittedHighWaterBlocks","MissingStoredBlocks","SourceGapCount","MissingSourceTicks","DroppedBlocks","Rows","Phases","Segments","ImuAnalogGridRows","ImuAnalogGridNpLocked","ImuGridSourceWindowVerified","OptionalImuError"}){
                    var property=result.GetType().GetProperty(name);fields[name]=property==null?Field(result,name):property.GetValue(result,null);
                }
                report["result"]=fields;report["status"]="success";
                foreach(string name in new[]{"PhysicalSourceBytesRead","PhysicalGroupsRead","ReorderPeakBytes","ReorderPeakGroups","ReorderSpilledGroups","ReorderSpilledBytes","TemporarySpillUsed"})report[name]=Field(result,name);
                Check(progress.Count>0&&progress.Last()==100&&progress.All(x=>x>=0&&x<=100),"Progress did not finish after actual export");
            }catch(Exception e){report["status"]="error";report["exception"]=e.GetType().FullName;report["error"]=e.Message;}
            report["tracked_payload_bytes"]=card.PayloadRead;report["physical_groups"]=card.Groups;report["group_bytes"]=card.GroupBytes;
        }
        Directory.CreateDirectory(output);report["temporary_spill_observed"]=spillSeen;report["spill_files_remaining"]=Directory.GetFiles(output,".np2-reorder-*").Length;
        WriteJson(Path.Combine(output,"single-pass-result.json"),report);return 0;
    }

    static Dictionary<string,object> Export(string sdk,string image,string output,bool strict,int cancel=-1,int fail=-1){
        var info=new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,string.Join(" ",new[]{"--export",sdk,image,output,strict?"1":"0",cancel.ToString(),fail.ToString()}.Select(Quote))){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        using(var child=Process.Start(info)){string stdout=child.StandardOutput.ReadToEnd(),stderr=child.StandardError.ReadToEnd();child.WaitForExit();File.WriteAllText(output+".log",stdout+stderr);Check(child.ExitCode==0,"Export child failed: "+stderr);}
        return ReadJson(Path.Combine(output,"single-pass-result.json"));
    }

    static void SameFiles(string baseline,string candidate){
        string[] fixedFiles={"analogin.dat","analogin_validity.dat","analogin_encoding.txt","neural_segments.csv","neural_storage_index.csv","imu_grid_phase.bin","imu_grid_sectors.bin","imu_grid_segments.csv","imu_grid_gaps.csv","first_sample_timestamps.json"};
        var expected=fixedFiles.Concat(Directory.GetFiles(baseline,"neuropixels_probe*_ap.dat").Select(Path.GetFileName)).Where(x=>File.Exists(Path.Combine(baseline,x))).ToArray();
        foreach(string name in expected)Check(File.Exists(Path.Combine(candidate,name))&&Hash(Path.Combine(baseline,name))==Hash(Path.Combine(candidate,name)),"Published221 output parity: "+name);
        // Existing merged-DAT writer is unchanged; compare the exact segment
        // row/channel interleave independently, without invoking a GUI or merge.
        var left=MergedHash(baseline);var right=MergedHash(candidate);Check(left==right,"Segment-aware merged DAT byte order changed");
    }
    static string MergedHash(string folder){
        var files=Directory.GetFiles(folder,"neuropixels_probe*_ap.dat").OrderBy(x=>x).Select(File.OpenRead).ToArray();
        try{
            var lines=File.ReadAllLines(Path.Combine(folder,"neural_segments.csv"));using(var sha=SHA256.Create()){
                // CSV includes explicit row offsets/counts; Result parity also
                // checks every per-probe segment. Parse column names, not order.
                string[] header=lines[0].Split(',');int count=Array.IndexOf(header,"rows"),offset=Array.IndexOf(header,"file_row_offset"),probe=Array.IndexOf(header,"probe");
                Check(count>=0&&offset>=0&&probe>=0,"Segment row geometry columns missing");
                foreach(var group in lines.Skip(1).Where(x=>x.Length!=0).Select(x=>x.Split(',')).GroupBy(x=>x[0])){
                    var rows=group.OrderBy(x=>int.Parse(x[probe])).ToArray();long common=rows.Min(x=>long.Parse(x[count]));var bytes=new byte[768];
                    for(int p=0;p<files.Length;p++)files[p].Position=long.Parse(rows[p][offset])*768;
                    for(long row=0;row<common;row++)foreach(var file in files){int at=0,n;while(at<bytes.Length){n=file.Read(bytes,at,bytes.Length-at);if(n==0)throw new EndOfStreamException();at+=n;}sha.TransformBlock(bytes,0,bytes.Length,bytes,0);}
                }
                sha.TransformFinalBlock(new byte[0],0,0);return BitConverter.ToString(sha.Hash);
            }
        }finally{foreach(var file in files)file.Dispose();}
    }
    static void NoSpill(Dictionary<string,object> report){Check(Number(report["spill_files_remaining"])==0,"Temporary reorder spill leaked");}
    static void FailurePartial(string folder,Dictionary<string,object> report,string expected){
        Check((string)report["status"]=="error"&&(string)report["exception"]==expected,"Expected "+expected+", got "+Json.Serialize(report));NoSpill(report);
        Check(Directory.GetFiles(folder,"neuropixels_probe*_ap.dat").Length==0,"Failed export published completed DAT");
        string status=Path.Combine(folder,"np2_sd_export_status.txt");if(File.Exists(status))Check(File.ReadAllText(status).StartsWith("Export complete: False"),"Failure reported complete");
    }

    static void MetadataPermutation(string folder){
        string path=Path.Combine(folder,"neural_storage_index.zip");if(!File.Exists(path))return;
        using(var archive=ZipFile.OpenRead(path)){
            var entry=archive.Entries.Single(x=>x.FullName.EndsWith(".csv",StringComparison.OrdinalIgnoreCase));
            var physical=new List<uint>();using(var reader=new StreamReader(entry.Open())){reader.ReadLine();string line;while((line=reader.ReadLine())!=null){var words=line.Split(',');physical.Add(uint.Parse(words[1],CultureInfo.InvariantCulture));}}
            Check(physical.Count==880688,"Yoshi metadata fixture group count changed");var ordered=physical.OrderBy(x=>x).ToArray();Check(ordered.Select((x,n)=>x==(uint)n).All(x=>x),"Yoshi physical permutation has duplicate/missing group");
        }
    }
    static void ExpandReservation(string path,Type fixture){
        using(var file=new FileStream(path,FileMode.Open,FileAccess.ReadWrite))foreach(uint sector in new[]{Start+3,Start+4}){
            var bytes=new byte[512];file.Position=sector*512L;if(file.Read(bytes,0,512)!=512)throw new EndOfStreamException();
            //48groups need a larger declared reservation than the retained
            //3group fixture. Reuse its original metadata seal implementation.
            uint limit=BitConverter.ToUInt32(bytes,64),reserved=checked((limit+16383U)/16384U*16384U);
            Buffer.BlockCopy(BitConverter.GetBytes(reserved),0,bytes,68,4);
            Invoke(fixture.GetMethod("Seal",BindingFlags.Static|BindingFlags.NonPublic),new object[]{bytes});
            file.Position=sector*512L;file.Write(bytes,0,bytes.Length);
        }
    }
    static void Corrupt(string source,string target,long at){File.Copy(source,target);using(var file=new FileStream(target,FileMode.Open,FileAccess.ReadWrite)){file.Position=at;int value=file.ReadByte();if(value<0)throw new EndOfStreamException();file.Position=at;file.WriteByte((byte)(value^1));}}
    static int Main(string[] args){
        try{
            if(args.Length>0&&args[0]=="--export")return ExportChild(args);
            if(args.Length!=6)throw new ArgumentException("candidateSDK baseline221SDK retainedFixtureEXE retainedImages output yoshiMetadataFolder");
            string sdk=Path.GetFullPath(args[0]),baseline=Path.GetFullPath(args[1]),generator=Path.GetFullPath(args[2]),fixtures=Path.GetFullPath(args[3]),output=Path.GetFullPath(args[4]);
            Check(Hash(baseline)=="8B80702C9829C62FB12403A7B5B081C7CE8C63195024C84C7B514536B5B883F5","Published221 reference SDK identity changed");
            Check(Hash(generator)=="4DAEC01F16523086FE49D0922D241C85F0955C904168134E9A2A25AFF70E78E6","Retained generator identity changed");
            if(Directory.Exists(output))throw new IOException("Fresh regression output required");Directory.CreateDirectory(output);
            var results=new List<object>();string stem=Path.Combine(fixtures,"append16_20000_7");
            foreach(string name in new[]{"ordered","reordered","hole","prefix_missing","unknown_admitted","aux_order","aux_parent"}){
                string image=stem+(name=="ordered"||name=="reordered"?".":"")+name;
                string a=Path.Combine(output,name+"-221"),b=Path.Combine(output,name+"-new");var expected=Export(baseline,image,a,false);var actual=Export(sdk,image,b,true);
                Check((string)expected["status"]=="success"&&(string)actual["status"]=="success","Success/parity fixture failed: "+name);
                Check(Json.Serialize(expected["result"])==Json.Serialize(actual["result"]),"Result/segment/first-source parity: "+name);SameFiles(a,b);NoSpill(actual);
                Check(Number(actual["PhysicalSourceBytesRead"])==Number(actual["tracked_payload_bytes"])&&Number(actual["PhysicalGroupsRead"])==Number(actual["physical_groups"]),"Physical read counters differ from actual stream");
                results.Add(new{name,actual});
            }
            foreach(string name in new[]{"bad_prefix","bad_high","bad_admitted","bad_unknown","coverage_flags","reserved","complete_hole","backward_tick","duplicate"}){
                string dest=Path.Combine(output,"reject-"+name);var r=Export(sdk,stem+name,dest,true);FailurePartial(dest,r,"System.IO.InvalidDataException");
            }
            // Raw codec6 stores probe1's wire bits in each tick byte. In block1
            // source40960, phase137 places the next SuperSync at tick503.
            foreach(var fault in new[]{Tuple.Create("descriptor_crc",(Start+3L)*512+508),Tuple.Create("neural_header",16384L*512+46592+24),Tuple.Create("super_sync",16384L*512+46592+512+503)}){
                string image=Path.Combine(output,fault.Item1+".img"),dest=Path.Combine(output,"reject-"+fault.Item1);Corrupt(stem+".ordered",image,fault.Item2);
                var expected=Export(baseline,image,dest+"-221",false);Check((string)expected["status"]=="error"&&(string)expected["exception"]=="System.IO.InvalidDataException","Malformed golden was not rejected by published221: "+fault.Item1);
                FailurePartial(dest,Export(sdk,image,dest,true),"System.IO.InvalidDataException");
            }
            var fixture=Assembly.LoadFrom(generator).GetType("NpV2SdDatExportTests",true);var make=fixture.GetMethod("MakeGridAppend",BindingFlags.Static|BindingFlags.NonPublic);
            string prefixImage=Path.Combine(output,"sealed-prefix.img");Invoke(make,new object[]{stem+".grid",prefixImage,new uint[]{0,1},12U,"unknown_admitted"});
            string prefixOld=Path.Combine(output,"sealed-prefix-221"),prefixNew=Path.Combine(output,"sealed-prefix-new");var po=Export(baseline,prefixImage,prefixOld,false);var pn=Export(sdk,prefixImage,prefixNew,true);
            Check((string)po["status"]=="success"&&(string)pn["status"]=="success"&&Json.Serialize(po["result"])==Json.Serialize(pn["result"]),"Incomplete sealed-prefix parity");SameFiles(prefixOld,prefixNew);NoSpill(pn);
            foreach(bool prefix in new[]{false,true}){
                string image=Path.Combine(output,prefix?"spill-prefix.img":"spill.img");uint[] order=Enumerable.Range(0,48).Select(x=>(uint)x).Reverse().ToArray();if(prefix)order=new[]{0U}.Concat(order.Where(x=>x!=0)).ToArray();
                Invoke(make,new object[]{stem+".grid",image,order,192U,null});ExpandReservation(image,fixture);
                if(!prefix){
                    string a=Path.Combine(output,"spill-221"),b=Path.Combine(output,"spill-new");var expected=Export(baseline,image,a,false);var actual=Export(sdk,image,b,true);Check((string)actual["status"]=="success","Spilled export failed");SameFiles(a,b);NoSpill(actual);
                    Check((bool)actual["TemporarySpillUsed"]&&Number(actual["ReorderSpilledGroups"])>0&&Number(actual["ReorderSpilledBytes"])>0,"Reverse48 did not exercise real local spill");
                    Check(Number(actual["ReorderPeakBytes"])<=(ulong)ReorderLimit,"Queued reorder NN RAM exceeded8MiB");results.Add(new{name="spill48",actual});
                }else{
                    foreach(bool cancel in new[]{true,false}){string dest=Path.Combine(output,cancel?"cancel-after-spill":"io-after-spill");var r=Export(sdk,image,dest,true,cancel?48:-1,cancel?-1:47);FailurePartial(dest,r,cancel?"System.OperationCanceledException":"System.IO.IOException");Check(Directory.GetFiles(dest,"neuropixels_probe*_ap.dat.partial").Any(x=>new FileInfo(x).Length>0),"Real prefix data was not retained after interruption");Check((bool)r["temporary_spill_observed"],"Interrupted test did not observe spill before failure");}
                    byte[] d=new byte[512];using(var f=File.OpenRead(image)){f.Position=(Start+3L)*512;f.Read(d,0,512);}long stride=(364L+BitConverter.ToUInt32(d,228))*512;
                    string broken=Path.Combine(output,"corrupt-after-spill.img"),badOut=Path.Combine(output,"corrupt-after-spill");Corrupt(image,broken,16384L*512+47*stride+512+823);
                    var bad=Export(sdk,broken,badOut,true);FailurePartial(badOut,bad,"System.IO.InvalidDataException");Check((bool)bad["temporary_spill_observed"],"Corruption test did not traverse actual spill");
                }
            }
            MetadataPermutation(args[5]);WriteJson(Path.Combine(output,"receipt.json"),new{status="PASS_SOFTWARE_FIXTURES_ONLY",checks,candidate=new{path=sdk,sha256=Hash(sdk)},baseline221=new{path=baseline,sha256=Hash(baseline)},generator=new{path=generator,sha256=Hash(generator)},results,limits="No hardware, raw Yoshi session or throughput claim. Yoshi index is metadata-only. Unknown gaps/tail and optional AUX failure semantics retained."});
            Console.WriteLine("PASS "+checks+" one-pass public SDK regression checks");return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
