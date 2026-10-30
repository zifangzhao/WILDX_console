using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using USB512_interface;

internal static class NpFineClockLocatorTests {
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
    const string Address="F5D68A6BCC7C";static int checks;
    static void Need(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static object Old(string name,params object[] args){
        try{return typeof(NpFineClockEvidenceImportTests).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);}
        catch(TargetInvocationException ex){throw ex.InnerException??ex;}
    }
    static string Folder(string root,string name){string p=Path.Combine(root,name);Directory.CreateDirectory(p);return p;}
    static NpFineSdCompanion Companion(int family){return (NpFineSdCompanion)Old("Companion",family,false);}
    static Dictionary<string,object> Attempt(int family){return (Dictionary<string,object>)Old("Attempt",family,null);}
    static void Save(string folder,Dictionary<string,object> attempt){Old("Save",folder,attempt,"one",0x1020304050607080UL);}
    static Dictionary<string,object> Locator(string path){return Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));}
    static void Write(string path,Dictionary<string,object> row){File.WriteAllText(path,Json.Serialize(row));}
    static void Raw(string folder,int family){Save(folder,Attempt(family));File.WriteAllText(Path.Combine(folder,"ble_clock_actual.jsonl"),"{\"actual_raw_snapshot\":1}\n{\"partial\":",new UTF8Encoding(false));}
    static void NoMatch(string output,NpFineClockImportResult r,string why){Need(!r.Associated&&r.JournalPaths.Length==0&&!Directory.GetFiles(output,"ble_clock_import_*.jsonl").Any(),why);}
    static void Good(string root,int family){
        string data=Folder(root,"chosen"),metadata=Folder(root,"metadata"),output=Folder(root,"output");Raw(data,family);
        var before=Directory.GetFiles(data).ToDictionary(Path.GetFileName,File.ReadAllBytes);
        string locator=NpFineClockEvidenceImport.RegisterSessionFolder(data,Address.ToLowerInvariant(),metadata);
        var row=Locator(locator);Need((int)row["schema"]==1&&(string)row["session_folder"]==Path.GetFullPath(data)&&(string)row["ble_address"]==Address,"Wrong locator identity/path");
        Need(new FileInfo(locator).Length<16384&&Directory.GetFiles(metadata,"*",SearchOption.AllDirectories).Length==1,"Index copied raw data or extra files");
        NpFineClockEvidenceImport.RegisterSessionFolder(data,Address,metadata);var r=NpFineClockEvidenceImport.Import(output,Companion(family),metadata);
        Need(r.Associated&&r.Status=="imported"&&r.Error==null&&r.JournalPaths.Length==1,"Located strict recording not imported");
        Need(File.ReadAllBytes(r.JournalPaths[0]).SequenceEqual(before["ble_clock_actual.jsonl"]),"Raw located journal changed");
        Need(before.All(p=>File.ReadAllBytes(Path.Combine(data,p.Key)).SequenceEqual(p.Value)),"Chosen-folder source changed");
        Need(Directory.GetFiles(metadata,"*",SearchOption.AllDirectories).All(p=>Path.GetFileName(p)=="session-folder.json"),"Raw metadata leaked into index");
    }
    static void Invalid(string root){
        foreach(string fault in new[]{"schema","format","address","relative","missing","malformed","oversize","nested","uid","token","request_crc"}){
            string own=Folder(root,fault),data=Folder(own,"chosen"),metadata=Folder(own,"metadata"),output=Folder(own,"output");Raw(data,2);
            string path=NpFineClockEvidenceImport.RegisterSessionFolder(data,Address,metadata);var d=Locator(path);
            if(fault=="schema")d["schema"]="1";if(fault=="format")d["format"]="other";if(fault=="address")d["ble_address"]="F5D68A6BCC7D";
            if(fault=="relative")d["session_folder"]="chosen";if(fault=="missing")d["session_folder"]=Path.Combine(own,"absent");
            if(fault=="nested"){string empty=Folder(own,"one-hop");File.WriteAllText(Path.Combine(empty,"session-folder.json"),Json.Serialize(d));d["session_folder"]=empty;}
            Write(path,d);if(fault=="malformed")File.WriteAllText(path,"{");if(fault=="oversize")File.WriteAllBytes(path,new byte[16385]);
            if(fault=="uid"||fault=="token"||fault=="request_crc"){
                string attempt=Directory.GetFiles(data,"sd_start_*.json").Single();var a=Locator(attempt);
                if(fault=="uid")a["device_uid"]="001A0004434250062039314F";
                if(fault=="token")a["token_hex"]="1020304050607081";
                if(fault=="request_crc"){string hex=(string)a["request_hex"];a["request_hex"]=hex.Substring(0,120)+"00000000";}
                Write(attempt,a);
            }
            var r=NpFineClockEvidenceImport.Import(output,Companion(2),metadata);NoMatch(output,r,"Invalid locator or raw binding admitted: "+fault);
            Need(r.Status=="matching_attempt_missing","Optional locator failure escaped as global failure: "+fault);
        }
    }
    static void LegacyAndAmbiguous(string root){
        string metadata=Folder(root,"metadata"),legacy=Folder(Folder(metadata,"BLE-"+Address),"legacy"),output=Folder(root,"legacy_output");
        Raw(legacy,1);File.WriteAllText(Path.Combine(legacy,"session-folder.json"),"{");
        Need(NpFineClockEvidenceImport.Import(output,Companion(1),metadata).Associated,"Bad optional locator blocked legacy direct folder");
        string other=Folder(root,"other");var a=Attempt(1);a["attempt_id"]="different-immutable-id";Save(other,a);NpFineClockEvidenceImport.RegisterSessionFolder(other,Address,metadata);
        output=Folder(root,"ambiguous_output");var r=NpFineClockEvidenceImport.Import(output,Companion(1),metadata);NoMatch(output,r,"Locator bypassed immutable ambiguity");Need(r.Status=="ambiguous_immutable_attempts","Ambiguity not preserved");
        bool rejected=false;try{NpFineClockEvidenceImport.RegisterSessionFolder(other,"not-a-BLE-address",metadata);}catch(ArgumentException){rejected=true;}Need(rejected,"Invalid registration address accepted");
        rejected=false;try{NpFineClockEvidenceImport.RegisterSessionFolder(Path.Combine(root,"missing"),Address,metadata);}catch(IOException){rejected=true;}Need(rejected,"Missing registration folder accepted");
    }
    static void Reparse(string root){
        string metadata=Folder(root,"metadata"),data=Folder(root,"data");Raw(data,2);
        // Existing Windows compatibility junction, read-only. No link creation
        // privilege, writes outside the fixture, or system-directory scan.
        string link=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Application Data");
        Need((File.GetAttributes(link)&FileAttributes.ReparsePoint)!=0,"Expected read-only Windows reparse witness missing");
        string target=Directory.Exists(Path.Combine(link,"Microsoft"))?Path.Combine(link,"Microsoft"):link;
        string path=NpFineClockEvidenceImport.RegisterSessionFolder(data,Address,metadata);var d=Locator(path);d["session_folder"]=target;Write(path,d);
        string output=Folder(root,"output");NoMatch(output,NpFineClockEvidenceImport.Import(output,Companion(2),metadata),"Reparse ancestor locator admitted");
        var report=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(output,NpFineClockEvidenceImport.StatusFile)));
        Need(Convert.ToInt32(report["rejected_session_locators"])==1,"Reparse target was scanned instead of rejected");
        bool refused=false;try{NpFineClockEvidenceImport.RegisterSessionFolder(target,Address,metadata);}catch(IOException){refused=true;}Need(refused,"Registration followed reparse ancestor");
    }
    static int Main(string[] args){try{
        string root=Folder(Path.GetFullPath(args[0]),"locators_"+Guid.NewGuid().ToString("N").Substring(0,12));
        Good(Folder(root,"family1"),1);Good(Folder(root,"family2"),2);Invalid(Folder(root,"invalid"));LegacyAndAmbiguous(Folder(root,"legacy"));Reparse(Folder(root,"reparse"));
        Need((int)Old("Main",new object[]{new[]{Path.GetFullPath(args[0])}})==0,"Existing importer regression failed");
        Console.WriteLine("PASS "+checks+" selected-folder locator/strict association/single-hop/reparse/raw-preservation checks; no hardware");return 0;
    }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
