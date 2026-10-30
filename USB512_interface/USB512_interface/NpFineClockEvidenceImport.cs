using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace USB512_interface {
    internal sealed class NpFineClockImportResult {
        internal string Status="unavailable",Error;
        internal string[] JournalPaths=new string[0];
        internal bool Associated;
    }
    // The sealed on-card UID/token supplies the association that a persisted
    // host Start attempt alone cannot establish. Clock qualification is separate.
    internal static class NpFineClockEvidenceImport {
        internal const string StatusFile="host_clock_association.json";
        const int MaximumSessions=4096,MaximumFiles=128,MaximumBytes=32*1024*1024;
        const string LocatorFile="session-folder.json",LocatorFormat="wildx.ble_session_folder";
        const int MaximumLocatorBytes=16384;
        static readonly JavaScriptSerializer Json=new JavaScriptSerializer {MaxJsonLength=MaximumBytes};
        sealed class Match {internal string Path,Key,Id,RequestHash;}
        static bool Plain(string path){return (File.GetAttributes(path)&FileAttributes.ReparsePoint)==0;}
        static bool PlainDirectory(string path){
            for(var d=new DirectoryInfo(path);d!=null;d=d.Parent)if(!d.Exists||(d.Attributes&FileAttributes.ReparsePoint)!=0)return false;
            return true;
        }
        internal static string RegisterSessionFolder(string folder,string bleAddress,string metadataRoot=null){
            if(!Regex.IsMatch(bleAddress??"",@"\A[0-9A-Fa-f]{12}\z"))throw new ArgumentException("Exact BLE address required.","bleAddress");
            if(String.IsNullOrWhiteSpace(folder)||!Path.IsPathRooted(folder))throw new ArgumentException("Full existing session folder required.","folder");
            folder=Path.GetFullPath(folder);if(!PlainDirectory(folder))throw new IOException("Session folder is missing or contains a reparse directory.");
            metadataRoot=metadataRoot??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WILDX","RecordingMetadata");
            metadataRoot=Path.GetFullPath(metadataRoot);Directory.CreateDirectory(metadataRoot);
            if(!PlainDirectory(metadataRoot))throw new IOException("Metadata root contains a reparse directory.");
            string peer=Path.Combine(metadataRoot,"BLE-"+bleAddress.ToUpperInvariant());Directory.CreateDirectory(peer);
            if(!PlainDirectory(peer))throw new IOException("Metadata peer contains a reparse directory.");
            string index=Path.Combine(peer,DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ",CultureInfo.InvariantCulture)+"-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(index);string path=Path.Combine(index,LocatorFile);
            byte[] bytes=System.Text.Encoding.UTF8.GetBytes(Json.Serialize(new{schema=1,format=LocatorFormat,ble_address=bleAddress.ToUpperInvariant(),session_folder=folder})+"\n");
            if(bytes.Length>MaximumLocatorBytes)throw new IOException("Session folder locator exceeds16KiB.");
            using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read)){file.Write(bytes,0,bytes.Length);file.Flush(true);}
            return path;
        }
        static string LocatedFolder(string session,string peer){
            string path=Path.Combine(session,LocatorFile);if(!File.Exists(path))return null;
            var d=Json.Deserialize<Dictionary<string,object>>(System.Text.Encoding.UTF8.GetString(Snapshot(path,MaximumLocatorBytes)));
            string address=Convert.ToString(Field(d,"ble_address")),folder=Convert.ToString(Field(d,"session_folder"));
            if(Number(d,"schema")!=1||Convert.ToString(Field(d,"format"))!=LocatorFormat||
                !Regex.IsMatch(address??"",@"\A[0-9A-Fa-f]{12}\z")||!String.Equals(Path.GetFileName(peer),"BLE-"+address,StringComparison.OrdinalIgnoreCase)||
                String.IsNullOrWhiteSpace(folder)||!Path.IsPathRooted(folder))throw new InvalidDataException("Invalid session folder locator.");
            folder=Path.GetFullPath(folder);if(!PlainDirectory(folder))throw new IOException("Located session folder is missing or contains a reparse directory.");
            return folder; // Exactly one hop; never follow another locator.
        }
        static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","");}
        static byte[] Snapshot(string path,int maximum=MaximumBytes){
            if(!Plain(path))throw new IOException("Reparse files are not imported.");
            using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)){
                long length=file.Length;if(length>maximum)throw new IOException("Metadata file exceeds its bounded snapshot size.");
                var bytes=new byte[(int)length];int used=0,n;while(used<bytes.Length&&(n=file.Read(bytes,used,bytes.Length-used))!=0)used+=n;
                if(used!=bytes.Length)Array.Resize(ref bytes,used);return bytes; // exact bounded snapshot, including any partial tail
            }
        }
        static byte[] Hex(string value){
            if(value==null||value.Length!=128)throw new InvalidDataException("Start request must contain 64 exact bytes.");
            var bytes=new byte[64];for(int i=0;i<64;i++)if(!Byte.TryParse(value.Substring(i*2,2),NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out bytes[i]))throw new InvalidDataException("Invalid request hex.");return bytes;
        }
        static object Field(Dictionary<string,object> d,string name){object value;if(!d.TryGetValue(name,out value))throw new InvalidDataException("Missing attempt field: "+name);return value;}
        static uint Number(Dictionary<string,object> d,string name){object value=Field(d,name);if(!(value is int||value is long))throw new InvalidDataException("Integer attempt field required: "+name);return Convert.ToUInt32(value);}
        static Match Validate(string path,byte[] bytes,NpFirstSyncSdBound bound){
            var d=Json.Deserialize<Dictionary<string,object>>(System.Text.Encoding.UTF8.GetString(bytes));var r=bound.Receipt;
            string id=Convert.ToString(Field(d,"attempt_id"));ulong token;
            if(Number(d,"schema")!=1||Convert.ToString(Field(d,"format"))!="neuropixels.sd_first_sync_attempt"||
                !String.Equals(Convert.ToString(Field(d,"device_uid")),bound.Uid,StringComparison.OrdinalIgnoreCase)||Number(d,"device_family")!=r.Family||
                Convert.ToString(Field(d,"token_hex")).Length!=16||!UInt64.TryParse(Convert.ToString(Field(d,"token_hex")),NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out token)||token!=bound.Token||String.IsNullOrWhiteSpace(id)||id.Length>128)
                throw new InvalidDataException("Attempt format/UID/family/token/immutable ID mismatch.");
            byte[] request=Hex(Convert.ToString(Field(d,"request_hex")));var q=NpFirstSyncSd.ParseRequest(request,(int)r.Family,bound.Uid);
            if(q.Token!=bound.Token||(q.Operation!=NpFirstSyncSd.Start&&q.Operation!=NpFirstSyncSd.Scheduled)||
                BitConverter.ToUInt32(request,32)!=r.Mask||BitConverter.ToUInt32(request,36)!=r.ApHz||
                r.Family==1&&BitConverter.ToUInt32(request,48)!=r.Session||Number(d,"request_id")!=q.Id||Number(d,"operation")!=q.Operation)
                throw new InvalidDataException("Start request does not match the sealed recording profile/session.");
            string hash=Hash(request);return new Match {Path=path,Id=id,RequestHash=hash,Key=id+":"+hash};
        }
        static string Copy(string output,string prefix,byte[] bytes){
            string target=Path.Combine(output,prefix+Hash(bytes)+(prefix=="ble_clock_import_"?".jsonl":".json"));
            if(File.Exists(target)){if(!Plain(target)||!Snapshot(target).SequenceEqual(bytes))throw new IOException("Occupied import filename has different content.");return target;}
            string temporary=Path.Combine(output,Guid.NewGuid().ToString("N")+".partial");
            try {using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.Read)){file.Write(bytes,0,bytes.Length);file.Flush(true);}
                if(File.Exists(target)){if(!Plain(target)||!Snapshot(target).SequenceEqual(bytes))throw new IOException("Occupied import filename has different content.");}
                else File.Move(temporary,target);
            }finally{if(File.Exists(temporary))File.Delete(temporary);}return target;
        }
        internal static NpFineClockImportResult Import(string output,NpFineSdCompanion companion,string metadataRoot=null){
            var result=new NpFineClockImportResult();var copied=new List<object>();int sessions=0,candidates=0,rejected=0,located=0,rejectedLocators=0;string requestHash=null,attemptId=null;
            NpFirstSyncSdBound bound=companion==null?null:companion.Bound;
            if(bound==null){result.Status="no_qualified_companion";return result;} // Legacy: no scan or output I/O.
            try {
                if(bound.Receipt==null||companion.Anchor==null||companion.Anchor.Length!=64)throw new InvalidDataException("Strict on-card companion required.");
                // Reparse the immutable receipt; the caller obtained the typed
                // companion through strict +5/+6 validation, never a host attempt.
                var receipt=NpFirstSyncWire.Parse(bound.Receipt.Raw);
                if((receipt.Flags&(NpFirstSyncWire.Final|NpFirstSyncWire.AllPorts|NpFirstSyncWire.SourceClock))!=(NpFirstSyncWire.Final|NpFirstSyncWire.AllPorts|NpFirstSyncWire.SourceClock))throw new InvalidDataException("Qualified on-card source context required.");
                bound=new NpFirstSyncSdBound {Uid=bound.Uid,Token=bound.Token,Receipt=receipt};
                metadataRoot=metadataRoot??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WILDX","RecordingMetadata");
                if(!Directory.Exists(metadataRoot)){result.Status="metadata_root_missing";return result;}
                if(!Plain(metadataRoot))throw new IOException("Metadata root is a reparse directory.");
                var matches=new List<Match>();var scanned=new HashSet<string>(StringComparer.OrdinalIgnoreCase);string pattern="sd_start_"+bound.Token.ToString("X16")+"_*.json";int peers=0;
                foreach(string peer in Directory.EnumerateDirectories(metadataRoot,"BLE-*",SearchOption.TopDirectoryOnly)){
                    if(++peers>MaximumSessions)throw new IOException("Bounded metadata scan exceeded 4096 BLE directories.");if(!Plain(peer))continue;
                    foreach(string session in Directory.EnumerateDirectories(peer,"*",SearchOption.TopDirectoryOnly)){
                        if(++sessions>MaximumSessions)throw new IOException("Bounded metadata scan exceeded 4096 sessions.");if(!Plain(session))continue;
                        string target=null;
                        try{target=LocatedFolder(session,peer);if(target!=null)located++;}
                        catch(Exception ex) when(ex is IOException||ex is InvalidDataException||ex is UnauthorizedAccessException||ex is ArgumentException||ex is InvalidOperationException||ex is FormatException||ex is OverflowException){rejectedLocators++;}
                        foreach(string directory in target==null?new[]{session}:new[]{session,target}){
                            if(!scanned.Add(Path.GetFullPath(directory)))continue;
                            foreach(string path in Directory.EnumerateFiles(directory,pattern,SearchOption.TopDirectoryOnly)){
                                if(++candidates>MaximumFiles)throw new IOException("Bounded metadata scan exceeded 128 matching attempt files.");if(!Plain(path))continue;
                                try{matches.Add(Validate(path,Snapshot(path),bound));}catch(Exception){rejected++;}
                            }
                        }
                    }
                }
                if(matches.Count==0){result.Status="matching_attempt_missing";return result;}
                if(matches.Select(m=>m.Key).Distinct(StringComparer.Ordinal).Take(2).Count()!=1){result.Status="ambiguous_immutable_attempts";return result;}
                requestHash=matches[0].RequestHash;attemptId=matches[0].Id;
                var files=matches.Select(m=>m.Path).Concat(matches.Select(m=>Path.GetDirectoryName(m.Path)).Distinct(StringComparer.OrdinalIgnoreCase)
                    .SelectMany(folder=>Directory.EnumerateFiles(folder,"ble_clock_*.jsonl",SearchOption.TopDirectoryOnly).Take(MaximumFiles+1)))
                    .Distinct(StringComparer.OrdinalIgnoreCase).Take(MaximumFiles+1).ToArray();
                if(files.Length>MaximumFiles)throw new IOException("Bounded import exceeded 128 files.");
                // Revalidate each mutable attempt snapshot before any copy.
                var attempts=new Dictionary<string,byte[]>();foreach(var match in matches){byte[] bytes=Snapshot(match.Path);if(Validate(match.Path,bytes,bound).Key!=match.Key)throw new IOException("Immutable Start attempt changed during import.");attempts[match.Path]=bytes;}
                var journals=new List<string>();foreach(string path in files){
                    if(!Plain(path))continue;bool journal=Path.GetFileName(path).StartsWith("ble_clock_",StringComparison.OrdinalIgnoreCase);
                    byte[] bytes=journal?Snapshot(path):attempts[path];string target=Copy(output,journal?"ble_clock_import_":"sd_start_import_",bytes);
                    int complete=Array.LastIndexOf(bytes,(byte)10)+1;copied.Add(new {source=path,file=Path.GetFileName(target),snapshot_sha256=Hash(bytes),snapshot_bytes=bytes.Length,complete_lf_bytes=journal?(int?)complete:null,trailing_partial_bytes=journal?(int?)(bytes.Length-complete):null});
                    if(journal)journals.Add(target);
                }
                result.Associated=true;result.JournalPaths=journals.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();result.Status=journals.Count==0?"matched_no_clock_journals":"imported";
            }catch(Exception ex){result.Status="error";result.Error=ex.Message;result.JournalPaths=new string[0];result.Associated=false;}
            finally {
                try {var r=bound==null?null:bound.Receipt;var report=new {schema=1,status=result.Status,error=result.Error,association_confirmed=result.Associated,
                    recorded_uid=bound==null?null:bound.Uid,recorded_token_hex=bound==null?null:bound.Token.ToString("X16"),device_family=r==null?(uint?)null:r.Family,
                    attempt_id=attemptId,request_sha256=requestHash,receipt_sha256=r==null?null:Hash(r.Raw),sessions_scanned=sessions,matching_files_scanned=candidates,rejected_attempt_files=rejected,copied=copied,
                    located_session_folders=located,rejected_session_locators=rejectedLocators,
                    pc_mapping_claimed=false,profile_crc_used_as_identity=false,
                    association_scope="Exact saved Start request/immutable attempt plus independently sealed on-card +5/+6 UID/token/profile binding. Before-delivery persistence is sufficient when that independent on-card proof exists; host attempt alone is not.",
                    clock_scope="Copied bounded raw snapshots, including partial tails; hashes describe those snapshots, not the entire growing source file. Clock/protocol validation and PC mapping are separate."};
                    byte[] bytes=System.Text.Encoding.UTF8.GetBytes(Json.Serialize(report)+Environment.NewLine);string target=Path.Combine(output,StatusFile);
                    if(File.Exists(target)){if(!Plain(target)||!Snapshot(target).SequenceEqual(bytes))throw new IOException("Existing association report differs; no overwrite.");}
                    else using(var file=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.Read)){file.Write(bytes,0,bytes.Length);file.Flush(true);}
                }catch(Exception ex){result.Error=(result.Error==null?"":result.Error+"; ")+"Association report: "+ex.Message;}
            }
            return result;
        }
    }
}
