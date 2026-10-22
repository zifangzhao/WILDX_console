using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace USB512_interface
{
    // A folder of immutable factory files, indexed by their contents, not names.
    internal sealed class NpCalibrationLibrary
    {
        sealed class FactoryFile { internal string Path,Hash;internal int Columns; }
        internal sealed class Mapping { internal bool Np2;internal int ApGain,LfpGain;internal string Serial,File,Hash; }
        readonly Dictionary<string,List<FactoryFile>> files=new Dictionary<string,List<FactoryFile>>();
        readonly Dictionary<string,NpAdcCalibration> profiles=new Dictionary<string,NpAdcCalibration>();
        readonly Dictionary<string,Mapping> maps=new Dictionary<string,Mapping>(StringComparer.OrdinalIgnoreCase);
        internal readonly List<string> Warnings=new List<string>();
        internal string Folder {get;private set;}
        internal IEnumerable<string> Serials {get{return files.Where(p=>p.Value.Any(f=>f.Columns==2||f.Columns==17)).Select(p=>p.Key).OrderBy(p=>p,StringComparer.Ordinal);}}
        internal int Count {get{return Serials.Count();}}
        static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=4*1024*1024};
        static readonly int[] Gains={50,125,250,500,1000,1500,2000,3000};
        internal static string Serial(object value)
        {
            string text=value as string;if(String.IsNullOrWhiteSpace(text))return null;
            bool hex=text.StartsWith("0x",StringComparison.OrdinalIgnoreCase);ulong serial;
            if(!UInt64.TryParse(hex?text.Substring(2):text,hex?NumberStyles.HexNumber:NumberStyles.None,CultureInfo.InvariantCulture,out serial)||serial==0||serial==UInt64.MaxValue)throw new InvalidDataException("Invalid probe serial number.");
            return serial.ToString(CultureInfo.InvariantCulture);
        }
        internal static object Value(IDictionary<string,object> doc,string key){object value;return doc!=null&&doc.TryGetValue(key,out value)?value:null;}
        internal static string Hash(string path){using(var stream=File.OpenRead(path))using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","");}
        internal static NpCalibrationLibrary Scan(string folder,Func<bool> cancelled=null)
        {
            folder=Path.GetFullPath(folder);if(!Directory.Exists(folder))throw new DirectoryNotFoundException("Select an existing calibration folder.");
            var library=new NpCalibrationLibrary{Folder=folder};var pending=new Stack<string>();pending.Push(folder);
            while(pending.Count!=0){
                if(cancelled!=null&&cancelled())throw new OperationCanceledException();string directory=pending.Pop();
                if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)continue;
                foreach(string child in Directory.GetDirectories(directory).OrderByDescending(p=>p,StringComparer.OrdinalIgnoreCase))pending.Push(child);
                foreach(string path in Directory.GetFiles(directory,"*.csv").OrderBy(p=>p,StringComparer.OrdinalIgnoreCase)){
                    if(cancelled!=null&&cancelled())throw new OperationCanceledException();
                    try{
                        if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)continue;
                        var info=new FileInfo(path);if(info.Length==0||info.Length>2*1024*1024)continue;
                        string serial,line;using(var reader=new StreamReader(path,new UTF8Encoding(false,true),true)){serial=Serial(reader.ReadLine());line=reader.ReadLine();}
                        if(serial==null||line==null)continue;int columns=line.Split(',').Length;if(columns!=2&&columns!=9&&columns!=17)continue;
                        List<FactoryFile> entry;if(!library.files.TryGetValue(serial,out entry)){entry=new List<FactoryFile>();library.files.Add(serial,entry);}
                        entry.Add(new FactoryFile{Path=path,Hash=Hash(path),Columns=columns});
                    }catch(Exception ex) when(ex is IOException||ex is InvalidDataException||ex is ArgumentException||ex is FormatException){library.Warnings.Add(Path.GetFileName(path)+": "+ex.Message);}
                }
            }
            if(library.Count==0)throw new InvalidDataException("No factory gain CSVs with valid probe serial headers were found.");return library;
        }
        FactoryFile FileFor(string serial,bool adc)
        {
            List<FactoryFile> candidates;if(!files.TryGetValue(serial,out candidates))throw new InvalidDataException("No calibration files for SN "+serial+".");
            var selected=candidates.Where(f=>adc?f.Columns==9:f.Columns==2||f.Columns==17).ToArray();
            if(selected.Length==0)throw new InvalidDataException("Missing "+(adc?"NP1 ADC":"gain")+" CSV for SN "+serial+".");
            if(selected.Select(p=>p.Hash).Distinct().Count()!=1)throw new InvalidDataException("Conflicting "+(adc?"ADC":"gain")+" CSVs for SN "+serial+"; remove the ambiguous copy.");
            return selected[0];
        }
        internal NpAdcCalibration Resolve(string serial,Mapping mapping=null)
        {
            serial=Serial(serial);if(serial==null)throw new InvalidDataException("Probe SN is unavailable. Select it once for legacy recordings.");
            var gain=FileFor(serial,false);bool np2=gain.Columns==2;
            if(mapping!=null&&(mapping.Np2!=np2||mapping.Serial!=null&&mapping.Serial!=serial))throw new InvalidDataException("IMRO/configuration identity conflicts with SN "+serial+".");
            if(!np2&&mapping==null)throw new InvalidDataException("SN "+serial+": NP1 acquisition gains need an IMRO or recorded channel configuration.");
            int ap=np2?0:mapping.ApGain,lfp=np2?0:mapping.LfpGain;string key=serial+":"+ap+":"+lfp;
            NpAdcCalibration profile;if(profiles.TryGetValue(key,out profile))return profile;
            var adc=np2?null:FileFor(serial,true);
            profile=new NpAdcCalibration(gain.Path,adc==null?null:adc.Path,serial,ap,lfp);
            if(profile.GainHash!=gain.Hash||adc!=null&&profile.AdcHash!=adc.Hash)throw new IOException("Factory files changed since the folder was indexed; scan again.");
            profiles.Add(key,profile);return profile;
        }
        internal static Mapping Configuration(string text)
        {
            var document=Json.Deserialize<Dictionary<string,object>>(text);string model=Value(document,"Profile") as string;
            bool np2=model=="Np2Single"||model=="Np2Multi";if(!np2&&model!="Np1")throw new InvalidDataException("Select a Neuropixels NP1/NP2 IMRO.");
            var channels=(Value(document,"Channels") as IEnumerable);if(channels==null)throw new InvalidDataException("Configuration has no channels.");
            var rows=channels.Cast<Dictionary<string,object>>().ToArray();if(rows.Length!=384||rows.Select(p=>Convert.ToInt32(Value(p,"Channel"))).Distinct().Count()!=384)throw new InvalidDataException("Configuration must describe all384 output channels.");
            int[] ap=rows.Select(p=>Convert.ToInt32(Value(p,"ApGain"))).Distinct().ToArray(),lfp=rows.Select(p=>Convert.ToInt32(Value(p,"LfpGain"))).Distinct().ToArray();
            if(!np2&&(ap.Length!=1||lfp.Length!=1))throw new InvalidDataException("Mixed channel gains require per-channel calibration; this recording was not guessed.");
            if(!np2&&(!Gains.Contains(ap[0])||!Gains.Contains(lfp[0])))throw new InvalidDataException("Unsupported NP1 acquisition gain.");
            return new Mapping{Np2=np2,ApGain=np2?0:ap[0],LfpGain=np2?0:lfp[0],Serial=Serial(Value(document,"SerialNumber"))};
        }
        internal Mapping ReadMapping(string path)
        {
            path=Path.GetFullPath(path);Mapping mapping;if(maps.TryGetValue(path,out mapping))return mapping;
            var file=new FileInfo(path);if(!file.Exists||file.Length==0||file.Length>1024*1024)throw new IOException("Missing or oversized IMRO/configuration file.");
            string hash=Hash(path),text;
            if(!Path.GetExtension(path).Equals(".imro",StringComparison.OrdinalIgnoreCase))text=File.ReadAllText(path);
            else{
                string output=Path.Combine(Path.GetTempPath(),"WILDX_calibration_"+Guid.NewGuid().ToString("N")+".json");
                if(path.Contains('"'))throw new ArgumentException("Invalid IMRO path.");
                try{if(NpConfigurationDialog.Run("--import-imro \""+path+"\" \""+output+"\"").GetAwaiter().GetResult()!=0)throw new IOException("IMRO import was cancelled.");text=NpConfigurationDialog.ReadResult(output);}
                finally{if(File.Exists(output))File.Delete(output);}
            }
            mapping=Configuration(text);if(Hash(path)!=hash)throw new IOException("IMRO changed during import; scan again.");
            mapping.File=path;mapping.Hash=hash;maps.Add(path,mapping);return mapping;
        }
        internal NpAdcCalibration Recording(string serial,Dictionary<string,object> probe,string metadataFolder,out string imro,NpAdcCalibration fallback=null)
        {
            Mapping mapping=null;imro=null;
            var rows=Value(probe,"channel_mapping") as IEnumerable;
            bool np2=FileFor(Serial(serial),false).Columns==2;
            if(rows!=null&&!np2){var channels=rows.Cast<Dictionary<string,object>>().ToArray();
                var ap=channels.Select(p=>Value(p,"ap_gain_index")).Where(p=>p!=null).Select(Convert.ToInt32).Distinct().ToArray();
                var lfp=channels.Select(p=>Value(p,"lfp_gain_index")).Where(p=>p!=null).Select(Convert.ToInt32).Distinct().ToArray();
                if(ap.Length!=1||lfp.Length!=1||ap[0]<0||ap[0]>7||lfp[0]<0||lfp[0]>7)throw new InvalidDataException("Recorded NP1 gains are missing or mixed.");
                mapping=new Mapping{ApGain=Gains[ap[0]],LfpGain=Gains[lfp[0]]};
            }
            string name=Value(probe,"imro_file") as string;
            if(!String.IsNullOrWhiteSpace(name)&&metadataFolder!=null){
                string state=Value(probe,"imro_state") as string;
                if(state==null||state=="device_confirmed_applied"||state=="device_confirmed_requested"||state=="device_snapshot"||state=="device_snapshot_unverified"){
                    string path=Path.GetFullPath(Path.Combine(metadataFolder,name));string prefix=Path.GetFullPath(metadataFolder).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
                    if(!path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Recorded IMRO path leaves its metadata folder.");
                    var recorded=ReadMapping(path);if(mapping!=null&&(recorded.Np2||recorded.ApGain!=mapping.ApGain||recorded.LfpGain!=mapping.LfpGain))throw new InvalidDataException("IMRO gains conflict with recorded channel gains.");
                    mapping=recorded;imro=path;
                }
            }
            if(mapping==null&&fallback!=null&&fallback.Serial==serial)mapping=new Mapping{Np2=fallback.Np2,ApGain=fallback.ApGain,LfpGain=fallback.LfpGain};
            return Resolve(serial,mapping);
        }
    }
}
