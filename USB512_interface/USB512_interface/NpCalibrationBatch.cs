using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace USB512_interface
{
    internal static class NpCalibrationBatch
    {
        internal enum Encoding { Auto, SignedNominal, LegacyUnsigned }
        internal sealed class Item
        {
            internal string Input,Output,Relative,Status,Detail,MetadataFolder;
            internal int[] Ports;
            internal bool Lfp,Legacy,Overwrite;
            internal string StagingFolder;
            internal long Bytes;
            internal NpCalibrationSet Calibration;
            internal bool Ready { get { return Status=="Ready"; } }
        }
        internal sealed class Progress
        {
            internal Item Item;internal long Done,Total;internal string Message;
        }
        static readonly Regex Single=new Regex(@"^(?:np|neuropixels_probe|probe)([1-3])_(ap|lfp)\.dat$",RegexOptions.IgnoreCase);
        static readonly Regex Generic=new Regex(@"^neuropixels_(ap|lfp)\.dat$",RegexOptions.IgnoreCase);
        static readonly int[] Gains={50,125,250,500,1000,1500,2000,3000};
        static readonly JavaScriptSerializer Json=new JavaScriptSerializer {MaxJsonLength=8*1024*1024};
        static object Value(IDictionary<string,object> d,string name){object value;return d!=null&&d.TryGetValue(name,out value)?value:null;}
        static Dictionary<string,object> Part(IDictionary<string,object> d,string name){return Value(d,name) as Dictionary<string,object>;}
        static int Number(object value){return value==null?0:Convert.ToInt32(value,CultureInfo.InvariantCulture);}
        static string Text(object value){return value as string;}
        static bool True(object value){return value is bool&&(bool)value;}
        static Dictionary<string,object> Read(string path){return Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));}
        static IEnumerable<string> EvidenceFolders(string input)
        {
            string folder=Path.GetDirectoryName(input);yield return folder;
            string parent=Path.GetDirectoryName(folder);if(parent!=null)yield return parent;
        }
        static int[] Mask(int mask){if(mask<1||mask>7)throw new InvalidDataException("Invalid recorded probe mask.");return Enumerable.Range(1,3).Where(p=>(mask&(1<<(p-1)))!=0).ToArray();}
        static void CheckPorts(int[] ports){if(ports==null||ports.Length<1||ports.Length>3||ports.Distinct().Count()!=ports.Length||ports.Any(p=>p<1||p>3))throw new InvalidDataException("Invalid recorded probe order.");}
        static bool Inside(string path,string root){return path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);}
        static bool Excluded(string path)
        {
            string name=Path.GetFileName(path);
            return name.Equals("calibration",StringComparison.OrdinalIgnoreCase)||name.StartsWith(".wildx_post_calibration_",StringComparison.OrdinalIgnoreCase)||name.EndsWith(".calibration",StringComparison.OrdinalIgnoreCase)||name.EndsWith(".source-metadata",StringComparison.OrdinalIgnoreCase)||name.EndsWith("_calibrated",StringComparison.OrdinalIgnoreCase);
        }
        internal static string DefaultOutput(string root)
        {
            root=Path.GetFullPath(root);
            return root.TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetPathRoot(root).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)?Path.Combine(root,"calibrated"):root.TrimEnd(Path.DirectorySeparatorChar)+"_calibrated";
        }
        internal static List<Item> Scan(string root,string output,NpCalibrationSet profiles,Encoding encoding,Func<bool> cancelled,Action<string> progress=null,bool overwrite=false)
        {
            root=Path.GetFullPath(root);output=Path.GetFullPath(output);
            if(!Directory.Exists(root))throw new DirectoryNotFoundException("Select an existing recording folder.");
            if(!overwrite){
                if(File.Exists(output))throw new IOException("The output path is a file; choose a folder.");
                if(File.Exists(Path.Combine(output,"post_calibration_report.json")))throw new IOException("A batch report already exists; choose a fresh output folder.");
                if(root.TrimEnd(Path.DirectorySeparatorChar).Equals(output.TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)||Inside(root,output))throw new IOException("Choose a separate output folder outside the selected recordings.");
            }
            if(profiles==null||profiles.Count==0)throw new InvalidDataException("Load factory calibration for the probes before scanning folders.");
            var items=new List<Item>();var pending=new Stack<string>();pending.Push(root);
            while(pending.Count!=0){
                if(cancelled!=null&&cancelled())throw new OperationCanceledException();
                string folder=pending.Pop();if(!overwrite&&(folder.Equals(output,StringComparison.OrdinalIgnoreCase)||Inside(folder,output)))continue;
                if(progress!=null)progress("Scanning "+folder);
                try{
                    if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0){items.Add(new Item{Input=folder,Relative=folder,Status="Skipped",Detail="Linked folders are not followed."});continue;}
                    foreach(string child in Directory.GetDirectories(folder).OrderByDescending(p=>p,StringComparer.OrdinalIgnoreCase))if(!Excluded(child))pending.Push(child);
                    foreach(string file in Directory.GetFiles(folder,"*.dat").OrderBy(p=>p,StringComparer.OrdinalIgnoreCase)){
                        string name=Path.GetFileName(file);if(!Single.IsMatch(name)&&!Generic.IsMatch(name))continue;
                        if(cancelled!=null&&cancelled())throw new OperationCanceledException();
                        string relative=file.Substring(root.TrimEnd(Path.DirectorySeparatorChar).Length).TrimStart(Path.DirectorySeparatorChar);
                        var item=new Item{Input=file,Relative=relative,Output=overwrite?file:Path.Combine(output,relative),Overwrite=overwrite,Status="Ready"};items.Add(item);
                        try{Plan(item,profiles,encoding);}catch(Exception ex){item.Status="Skipped";item.Detail=ex.Message;}
                    }
                }catch(OperationCanceledException){throw;}catch(Exception ex){items.Add(new Item{Input=folder,Relative=folder,Status="Skipped",Detail="Cannot scan folder: "+ex.Message});}
            }
            return items;
        }
        static void Plan(Item item,NpCalibrationSet profiles,Encoding encoding)
        {
            if((File.GetAttributes(item.Input)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Linked files are not followed.");
            string[] folders=EvidenceFolders(item.Input).ToArray();
            if(File.Exists(item.Input+".post-calibration.pending.json"))throw new InvalidDataException("An interrupted overwrite needs review; see the pending marker and batch report.");
            if(File.Exists(item.Input+".calibration.json")||Directory.Exists(item.Input+".calibration")||folders.Any(p=>File.Exists(Path.Combine(p,NpCalibrationSet.ManifestName))))throw new InvalidDataException("Already calibrated; correction provenance exists.");
            if((!item.Overwrite&&(File.Exists(item.Output)||Directory.Exists(item.Output)||File.Exists(Path.ChangeExtension(item.Output,".xml"))))||
                new[]{item.Output+".partial",item.Output+".calibration.json",item.Output+".calibration",item.Output+".source-metadata"}.Any(p=>File.Exists(p)||Directory.Exists(p)))throw new IOException("Output already exists; choose a fresh output folder.");
            string metadata=folders.Select(p=>Path.Combine(p,"recording_settings.json")).FirstOrDefault(File.Exists);
            item.MetadataFolder=metadata==null?null:Path.GetDirectoryName(metadata);
            var doc=metadata==null?null:Read(metadata);var output=Part(doc,"output");var actual=Part(doc,"actual");
            if(Value(doc,"adc_calibration_file")!=null||True(Value(doc,"calibration_applied"))||
                (Text(Value(doc,"sample_encoding"))??"").StartsWith("signed_calibrated",StringComparison.Ordinal)||
                (Text(Value(output,"sample_encoding"))??"").StartsWith("signed_calibrated",StringComparison.Ordinal))throw new InvalidDataException("Recording metadata says calibration was already applied.");
            string name=Path.GetFileName(item.Input);var single=Single.Match(name);var generic=Generic.Match(name);
            item.Lfp=(single.Success?single.Groups[2].Value:generic.Groups[1].Value).Equals("lfp",StringComparison.OrdinalIgnoreCase);
            var notes=new List<string>();
            if(single.Success)item.Ports=new[]{Int32.Parse(single.Groups[1].Value,CultureInfo.InvariantCulture)};
            else{
                string folderName=Path.GetFileName(Path.GetDirectoryName(item.Input));var child=Regex.Match(folderName,@"^probe([1-3])$",RegexOptions.IgnoreCase);
                if(child.Success)item.Ports=new[]{Int32.Parse(child.Groups[1].Value,CultureInfo.InvariantCulture)};
                else if(Value(doc,"folder_probe_mask")!=null)item.Ports=Mask(Number(Value(doc,"folder_probe_mask")));
                else{
                    string merge=Path.Combine(Path.GetDirectoryName(item.Input),"dat_merge.json");
                    if(File.Exists(merge))item.Ports=((IEnumerable)Value(Read(merge),"physical_probe_order")).Cast<object>().Select(Number).ToArray();
                    else if(Text(Value(output,"dat_layout"))=="merged"){
                        string order=Text(Value(output,"channel_order"));
                        if(order==null||order.IndexOf("ascending physical probe",StringComparison.OrdinalIgnoreCase)<0)throw new InvalidDataException("Merged channel order is not declared; use single-file correction with explicit probe order.");
                        item.Ports=Mask(Number(Value(actual,"probe_mask")));
                    }else if(Value(actual,"probe_mask")!=null&&Mask(Number(Value(actual,"probe_mask"))).Length==1)item.Ports=Mask(Number(Value(actual,"probe_mask")));
                    else throw new InvalidDataException("Probe layout unavailable; use single-file correction with explicit probe order.");
                }
            }
            CheckPorts(item.Ports);
            if(Value(actual,"probe_mask")!=null){int mask=Number(Value(actual,"probe_mask"));Mask(mask);if(item.Ports.Any(p=>(mask&(1<<(p-1)))==0))throw new InvalidDataException("File probe identity conflicts with the recorded probe mask.");}
            if(generic.Success&&item.Ports.Length>1&&Value(output,"channel_count_per_dat")!=null&&Number(Value(output,"channel_count_per_dat"))!=item.Ports.Length*384)throw new InvalidDataException("Recorded merged channel count conflicts with probe order.");
            if(!item.Lfp&&True(Value(output,"ap_upsampled_15_to_20")))throw new InvalidDataException("AP was resampled; post-calibration cannot reproduce correction before resampling.");
            bool? np2=null;var lfp=Part(output,"lfp_sample_rate");if(Value(lfp,"present") is bool)np2=!(bool)Value(lfp,"present");
            item.Calibration=new NpCalibrationSet();
            foreach(int port in item.Ports){
                Dictionary<string,object> probe=null;var list=Value(doc,"probes") as IEnumerable;
                if(list!=null)probe=list.Cast<object>().OfType<Dictionary<string,object>>().FirstOrDefault(p=>Number(Value(p,"probe"))==port);
                string serial=RecordedSerial(Part(probe,"identity"));NpAdcCalibration profile;
                if(serial!=null){
                    var matches=profiles.Ports.Select(profiles.Get).Where(p=>p.Serial==serial).ToArray();
                    if(matches.Length==0)throw new InvalidDataException("No loaded calibration matches probe "+port+" serial "+serial+".");
                    profile=matches.FirstOrDefault(p=>Object.ReferenceEquals(p,profiles.Get(port)))??matches[0];
                    if(matches.Any(p=>p.ApGain!=profile.ApGain||p.LfpGain!=profile.LfpGain||p.GainHash!=profile.GainHash||p.AdcHash!=profile.AdcHash))throw new InvalidDataException("Multiple loaded profiles for serial "+serial+" have different correction settings.");
                    notes.Add("P"+port+" SN "+serial+" matched");
                }else{profile=profiles.Get(port);if(profile==null)throw new InvalidDataException("Load calibration for physical probe "+port+"; recorded serial unavailable.");notes.Add("P"+port+" SN "+profile.Serial+" manual assignment; recorded serial unavailable");}
                if(np2.HasValue&&profile.Np2!=np2.Value||item.Lfp&&profile.Np2)throw new InvalidDataException("Calibration model does not match the recording/band.");
                CheckGain(probe,profile,item.Lfp);item.Calibration.Set(port,profile);
            }
            CheckMissingRows(item,folders);
            item.Legacy=SourceEncoding(item.Input,folders,encoding,notes);
            item.Bytes=new FileInfo(item.Input).Length;
            if(item.Bytes==0||item.Bytes%(item.Ports.Length*768)!=0)throw new InvalidDataException("Empty DAT or incomplete channel row.");
            item.Detail=String.Join("; ",notes);
        }
        static string RecordedSerial(Dictionary<string,object> identity)
        {
            string value=Text(Value(identity,"serial"))??Text(Value(identity,"serial_hex"));if(value==null)return null;
            ulong serial;bool hex=value.StartsWith("0x",StringComparison.OrdinalIgnoreCase);
            if(!UInt64.TryParse(hex?value.Substring(2):value,hex?NumberStyles.HexNumber:NumberStyles.None,CultureInfo.InvariantCulture,out serial))throw new InvalidDataException("Invalid recorded probe serial.");
            return serial==0?null:serial.ToString(CultureInfo.InvariantCulture);
        }
        static void CheckGain(Dictionary<string,object> probe,NpAdcCalibration profile,bool lfp)
        {
            if(profile.Np2)return;var channels=Value(probe,"channel_mapping") as IEnumerable;if(channels==null)return;
            string key=lfp?"lfp_gain_index":"ap_gain_index";int expected=Array.IndexOf(Gains,lfp?profile.LfpGain:profile.ApGain);
            foreach(var row in channels.Cast<object>().OfType<Dictionary<string,object>>())if(Value(row,key)!=null&&Number(Value(row,key))!=expected)throw new InvalidDataException("Recorded "+(lfp?"LFP":"AP")+" gains differ from the loaded calibration profile.");
        }
        static void CheckMissingRows(Item item,IEnumerable<string> folders)
        {
            foreach(string folder in folders)foreach(string name in new[]{"capture_status.txt","neuropixels_probes.txt"}){
                string path=Path.Combine(folder,name);if(!File.Exists(path))continue;
                foreach(string line in File.ReadLines(path)){
                    var match=Regex.Match(line,@"^(?:probe([1-3])\.|np([1-3])_)missing_(ap|lfp)_rows=(\d+)$");
                    if(match.Success&&item.Ports.Contains(Int32.Parse(match.Groups[1].Success?match.Groups[1].Value:match.Groups[2].Value,CultureInfo.InvariantCulture))&&
                        match.Groups[3].Value==(item.Lfp?"lfp":"ap")&&UInt64.Parse(match.Groups[4].Value,CultureInfo.InvariantCulture)>0)
                        throw new InvalidDataException("Missing-sample placeholder rows need mask-aware correction; this DAT was skipped.");
                }
            }
        }
        static bool SourceEncoding(string input,string[] folders,Encoding selected,List<string> notes)
        {
            bool? legacy=null;
            Action<bool> record=value=>{if(legacy.HasValue&&legacy.Value!=value)throw new InvalidDataException("Recorded sample encodings conflict.");legacy=value;};
            foreach(string folder in folders)foreach(string name in new[]{"capture_status.txt","neuropixels_probes.txt","np2_sd_export_status.txt","neuropixels_np2usb_status.txt"}){
                string path=Path.Combine(folder,name);if(!File.Exists(path))continue;
                foreach(string line in File.ReadLines(path)){
                    if(line.StartsWith("sample_encoding=signed_calibrated",StringComparison.Ordinal)||line.StartsWith("codes=signed calibrated",StringComparison.Ordinal)||line.StartsWith("sample_transform=factory",StringComparison.Ordinal))throw new InvalidDataException("Recorded output is already calibrated.");
                    if(line.StartsWith("sample_encoding=signed_nominal_adc_counts",StringComparison.Ordinal)||line.StartsWith("codes=signed nominal ADC counts",StringComparison.Ordinal)||line=="sample_transform=512 - raw10"||line.StartsWith("sample_transform=2048 - raw12",StringComparison.Ordinal))record(false);
                    if(line.StartsWith("sample_encoding=unsigned_raw_adc_codes",StringComparison.Ordinal))record(true);
                    if(line=="ap_upsampled_15_to_20khz=True"&&input.EndsWith("_ap.dat",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("AP was resampled; post-calibration is not supported.");
                }
            }
            if(legacy.HasValue){if(selected!=Encoding.Auto&&legacy.Value!=(selected==Encoding.LegacyUnsigned))throw new InvalidDataException("Selected encoding conflicts with recorded sample encoding.");return legacy.Value;}
            if(selected==Encoding.Auto)throw new InvalidDataException("Sample encoding unavailable; select signed nominal counts or legacy unsigned codes explicitly, then scan again.");
            notes.Add("Source encoding explicitly selected");return selected==Encoding.LegacyUnsigned;
        }
        internal static string Run(IList<Item> items,string output,Func<bool> cancelled,Action<Progress> progress,bool overwrite=false)
        {
            var ready=items.Where(p=>p.Ready).ToArray();long total=ready.Sum(p=>p.Bytes),done=0;
            if(ready.Any(p=>p.Overwrite!=overwrite))throw new InvalidDataException("Overwrite mode changed; scan again.");
            Directory.CreateDirectory(output);string report=Path.Combine(output,overwrite?"post_calibration_report_"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff",CultureInfo.InvariantCulture)+"_"+Guid.NewGuid().ToString("N")+".json":"post_calibration_report.json");
            if(File.Exists(report))throw new IOException("A batch report already exists; choose a fresh output folder.");
            try{
                foreach(var item in ready){
                    if(cancelled!=null&&cancelled())throw new OperationCanceledException();
                    item.Status="Calibrating";var watch=Stopwatch.StartNew();
                    if(progress!=null)progress(new Progress{Item=item,Done=done,Total=total,Message="Calibrating "+item.Relative});
                    try{
                        // Re-plan immediately before writing: do not trust an old scan
                        // if provenance, metadata or an output changed in the meantime.
                        var check=new Item{Input=item.Input,Output=item.Output,Overwrite=overwrite};Plan(check,item.Calibration,item.Legacy?Encoding.LegacyUnsigned:Encoding.SignedNominal);
                        if(!check.Ports.SequenceEqual(item.Ports)||check.Bytes!=item.Bytes)throw new IOException("Recording layout/size changed after scan; scan again.");
                        Directory.CreateDirectory(Path.GetDirectoryName(item.Output));
                        string target=item.Output;
                        if(overwrite){item.StagingFolder=Path.Combine(Path.GetDirectoryName(item.Input),".wildx_post_calibration_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(item.StagingFolder);target=Path.Combine(item.StagingFolder,Path.GetFileName(item.Input));}
                        using(var sourceGuard=new FileStream(item.Input,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete)){
                        if(sourceGuard.Length!=item.Bytes)throw new IOException("Recording size changed after scan; scan again.");
                        NpCalibrationDat.Correct(item.Input,target,item.Calibration,item.Ports,item.Lfp,item.Legacy,()=>{
                            if(progress!=null&&watch.ElapsedMilliseconds>=250){watch.Restart();long written=0;string partial=target+".partial";if(File.Exists(partial))written=Math.Min(item.Bytes,new FileInfo(partial).Length);progress(new Progress{Item=item,Done=done+written,Total=total,Message="Calibrating "+item.Relative});}
                            return cancelled!=null&&cancelled();
                        });
                        NpRecordingSettings.CopySourceMetadata(item.Input,target);
                        if(item.MetadataFolder!=null&&!item.MetadataFolder.Equals(Path.GetDirectoryName(item.Input),StringComparison.OrdinalIgnoreCase))
                            NpRecordingSettings.CopySourceMetadata(Path.Combine(item.MetadataFolder,"recording_settings.json"),Path.Combine(target+".source-metadata","parent"));
                        AnnotateCorrection(item,target);
                        if(cancelled!=null&&cancelled())throw new OperationCanceledException();
                        if(overwrite)CommitOverwrite(item,target);
                        else{string xml=Path.ChangeExtension(item.Input,".xml");if(File.Exists(xml))File.Copy(xml,Path.ChangeExtension(item.Output,".xml"),false);}
                        }
                        item.Status="Complete";
                    }catch(OperationCanceledException){item.Status="Cancelled";item.Detail+="; partial output retained";throw;}
                    catch(Exception ex){item.Status="Failed";item.Detail+="; "+ex.Message;}
                    done=checked(done+item.Bytes);if(progress!=null)progress(new Progress{Item=item,Done=done,Total=total,Message=item.Status+": "+item.Relative});
                }
            }finally{
                foreach(var item in ready.Where(p=>p.Ready))item.Status="Not processed";
                File.WriteAllText(report,Json.Serialize(new{schema_version=1,format="neuropixels.batch_post_calibration",created_utc=DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture),overwrite_originals=overwrite,
                    files=items.Select(p=>new{input=p.Input,output=p.Output,band=p.Lfp?"LFP":"AP",physical_probe_order=p.Ports,source_bytes=p.Bytes,status=p.Status,detail=p.Detail,staging_folder=p.StagingFolder})})+Environment.NewLine,new UTF8Encoding(false));
            }
            return report;
        }
        static void AnnotateCorrection(Item item,string target)
        {
            string receipt=target+".calibration.json";var document=Read(receipt);
            document["operation"]="batch_post_calibration";document["source_original_preserved"]=!item.Overwrite;
            document["source_recording_metadata"]=Path.GetFileName(item.Output)+".source-metadata";
            File.WriteAllText(receipt,Json.Serialize(document)+Environment.NewLine,new UTF8Encoding(false));
            string manifest=Path.Combine(target+".calibration",NpCalibrationSet.ManifestName);document=Read(manifest);
            document["stage"]="batch_host_post_calibration";document["raw_preserved"]=!item.Overwrite;
            document["source_original_preserved"]=!item.Overwrite;
            File.WriteAllText(manifest,Json.Serialize(document)+Environment.NewLine,new UTF8Encoding(false));
        }
        static void CommitOverwrite(Item item,string target)
        {
            string pending=item.Input+".post-calibration.pending.json";
            string receipt=File.ReadAllText(target+".calibration.json");
            using(var file=new FileStream(pending,FileMode.CreateNew,FileAccess.Write,FileShare.Read)){
                byte[] bytes=new UTF8Encoding(false).GetBytes(Json.Serialize(new{format="neuropixels.pending_calibration_overwrite",input=item.Input,staged_dat=target,calibration_receipt=Json.DeserializeObject(receipt),note="Metadata is published before an atomic DAT replacement. If interrupted, inspect hashes before retrying; do not apply correction again blindly."}));file.Write(bytes,0,bytes.Length);file.Flush(true);
            }
            File.Move(target+".calibration.json",item.Input+".calibration.json");
            Directory.Move(target+".calibration",item.Input+".calibration");
            Directory.Move(target+".source-metadata",item.Input+".source-metadata");
            File.Replace(target,item.Input,null);
            try{File.Delete(pending);Directory.Delete(item.StagingFolder);}catch(Exception ex){item.Detail+="; overwrite complete, cleanup needs attention: "+ex.Message;}
        }
    }
}
