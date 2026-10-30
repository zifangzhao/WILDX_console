using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using USB512_interface;

internal static class NpFineClockEvidenceImportTests {
    const string Uid="001A0004434250062039314E",Id="0123456789abcdef0123456789abcdef";
    const uint Session=0x12345,ApHz=20000;
    const ulong Token=0x1020304050607080UL;
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
    static readonly byte[] Journal=Encoding.UTF8.GetBytes("{\"kind\":\"clock\",\"n\":1}\r\n{\"partial\":");
    static int checks;
    static void Need(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static void Put(byte[] b,int at,uint n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,at,4);}
    static void Q(byte[] b,int at,ulong n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,at,8);}
    static void Seal(byte[] b){Put(b,b.Length-4,NpFirstSyncWire.Crc(b,b.Length-4));}
    static string Hash(byte[] b){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(b)).Replace("-","");}
    static string Hex(byte[] b){return BitConverter.ToString(b).Replace("-","");}
    static NpFineSdCompanion Companion(int family,bool unknownProfile=false){
        var clock=new byte[512];Encoding.ASCII.GetBytes(family==1?"N1SDUTC2":"N2SDUTC2").CopyTo(clock,0);
        Put(clock,8,2);Put(clock,12,Session);Put(clock,16,3);Put(clock,20,31);Put(clock,24,1500);Put(clock,28,1000);Q(clock,32,1793200000000000UL);
        Put(clock,family==1?64:48,1002);Put(clock,family==1?68:52,1003);Q(clock,family==1?48:56,1793200000002000UL);Q(clock,family==1?56:64,1793200000003000UL);
        Put(clock,72,ApHz);Put(clock,76,1000);Seal(clock);
        byte[] receipt=NpFirstSyncWireTests.Fixture(family,false,ApHz,3);
        if(unknownProfile){Put(receipt,12,BitConverter.ToUInt32(receipt,12)&~NpFirstSyncWire.Profile);Put(receipt,44,uint.MaxValue);Seal(receipt);}
        clock=NpFirstSyncSd.BindSector(clock,receipt,family,Uid,Token);
        var a=new byte[64];Put(a,0,0x5241504e);Put(a,4,1);Put(a,8,57);Put(a,12,((uint)family<<8)|31);Put(a,16,9);
        Put(a,20,1002);Put(a,24,1002);Put(a,28,128256000);Put(a,32,128256100);Put(a,36,128000000);
        Put(a,40,ApHz*(family==1?780U:1600U));Q(a,44,100);Put(a,52,ApHz);Put(a,56,1000);Seal(a);
        return NpFineClockWire.ReadCompanion(NpFineClockWire.Companion(clock,a,family,Session,ApHz,Uid,Token),clock,family,Session,ApHz,Uid,Token);
    }
    static byte[] Request(int family,bool scheduled=false,bool grid16=false){
        var legacy=new byte[64];Put(legacy,0,family==1?0x31444c4eU:0x32444c4eU);Put(legacy,4,(uint)family);Put(legacy,8,scheduled?4U:2U);Put(legacy,12,57);
        Put(legacy,16,3);Put(legacy,20,ApHz);Put(legacy,24,8192);if(family==1){Put(legacy,28,1048576);Put(legacy,32,Session);}Seal(legacy);
        return grid16?NpFirstSyncSd.WrapLegacyGrid16(legacy,family,Uid,Token):NpFirstSyncSd.WrapLegacy(legacy,family,Uid,Token);
    }
    static Dictionary<string,object> Attempt(int family,byte[] request=null){
        request=request??Request(family);var q=NpFirstSyncSd.ParseRequest(request,family,Uid);
        return new Dictionary<string,object>{{"schema",1},{"format","neuropixels.sd_first_sync_attempt"},{"attempt_id",Id},{"device_uid",Uid},{"device_family",family},
            {"token_hex",Token.ToString("X16")},{"protocol_version",q.Version},{"request_id",q.Id},{"operation",q.Operation},{"request_hex",Hex(request)},
            {"phase","before_delivery"},{"start_transport_entered",false},{"ack_validated",false},{"record_association_confirmed",false}};
    }
    static string Folder(string root,string name){string p=Path.Combine(root,name);Directory.CreateDirectory(p);return p;}
    static string Save(string session,Dictionary<string,object> d,string suffix="one",ulong token=Token){
        string path=Path.Combine(session,"sd_start_"+token.ToString("X16")+"_"+suffix+".json");File.WriteAllText(path,Json.Serialize(d),new UTF8Encoding(false));return path;
    }
    static string Metadata(string root){return Folder(Folder(root,"BLE-F5FAAFB34D50"),"session");}
    static Dictionary<string,object> Report(string output){return Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(output,NpFineClockEvidenceImport.StatusFile)));}
    static void NoClock(string output,NpFineClockImportResult r,string why){Need(!r.Associated&&r.JournalPaths.Length==0&&!Directory.GetFiles(output,"ble_clock_*.jsonl").Any(),why);}
    static void Good(string root,int family,bool scheduled=false,bool unknownProfile=false,bool grid16=false){
        string baseDir=Folder(root,"good"+family+scheduled+unknownProfile+grid16),metadata=Folder(baseDir,"metadata"),session=Metadata(metadata),output=Folder(baseDir,"output");
        var request=Request(family,scheduled,grid16);string attempt=Save(session,Attempt(family,request)),source=Path.Combine(session,"ble_clock_session.jsonl");File.WriteAllBytes(source,Journal);
        byte[] original=File.ReadAllBytes(attempt);var companion=Companion(family,unknownProfile);byte[] raw=(byte[])companion.Bound.Receipt.Raw.Clone(),anchor=(byte[])companion.Anchor.Clone();
        var r=NpFineClockEvidenceImport.Import(output,companion,metadata);var report=Report(output);
        Need(r.Associated&&r.Status=="imported"&&r.Error==null&&r.JournalPaths.Length==1,"Exact sealed association not imported");
        Need(File.ReadAllBytes(r.JournalPaths[0]).SequenceEqual(Journal)&&File.ReadAllBytes(source).SequenceEqual(Journal),"Raw partial journal was trimmed or rewritten");
        Need(Path.GetFileName(r.JournalPaths[0])=="ble_clock_import_"+Hash(Journal)+".jsonl","Copied journal name does not hash exact snapshot");
        Need(File.ReadAllBytes(Directory.GetFiles(output,"sd_start_import_*.json").Single()).SequenceEqual(original)&&File.ReadAllBytes(attempt).SequenceEqual(original),"Start attempt raw bytes changed");
        Need(companion.Bound.Receipt.Raw.SequenceEqual(raw)&&companion.Anchor.SequenceEqual(anchor),"On-card evidence was modified");
        Need((bool)report["association_confirmed"]&&!(bool)report["pc_mapping_claimed"]&&!(bool)report["profile_crc_used_as_identity"],"Association became PC/profile qualification");
        Need((string)report["request_sha256"]==Hash(request)&&(string)report["receipt_sha256"]==Hash(raw),"Raw binding hashes wrong");
        var copied=Json.ConvertToType<Dictionary<string,object>[]>(report["copied"]);var journal=copied.Single(d=>Convert.ToString(d["file"]).EndsWith(".jsonl"));int lf=Array.LastIndexOf(Journal,(byte)10)+1;
        Need(Convert.ToInt32(journal["snapshot_bytes"])==Journal.Length&&Convert.ToInt32(journal["complete_lf_bytes"])==lf&&Convert.ToInt32(journal["trailing_partial_bytes"])==Journal.Length-lf,"Partial raw tail provenance missing");
        var again=NpFineClockEvidenceImport.Import(output,companion,metadata);Need(again.Associated&&again.Error==null&&again.JournalPaths.SequenceEqual(r.JournalPaths),"Identical hashed outputs not reusable");
        Need(Directory.GetFiles(output,"*.partial").Length==0,"Temporary import file leaked");
    }
    static void Rejected(string root,int family){
        foreach(string fault in new[]{"format","schema","schema_string","schema_bool","uid","family","token","request_token","request_uid","request_mask","request_ap","request_session","request_crc","request_hex","request_id","operation","query","attempt_id","missing_field","missing_decl"}){
            if(family==2&&fault=="request_session")continue;
            string baseDir=Folder(root,"bad"+family+fault),metadata=Folder(baseDir,"metadata"),session=Metadata(metadata),output=Folder(baseDir,"output");var d=Attempt(family);var q=Request(family);
            if(fault=="format")d["format"]="other";if(fault=="schema")d["schema"]=2;if(fault=="schema_string")d["schema"]="1";if(fault=="schema_bool")d["schema"]=true;
            if(fault=="uid")d["device_uid"]="001A0004434250062039314F";if(fault=="family")d["device_family"]=family==1?2:1;if(fault=="token")d["token_hex"]=(Token+1).ToString("X16");
            if(fault=="request_token")Q(q,24,Token+1);if(fault=="request_uid")q[12]^=1;if(fault=="request_mask")Put(q,32,1);if(fault=="request_ap")Put(q,36,30000);if(fault=="request_session")Put(q,48,Session+1);
            if(fault.StartsWith("request_")){Seal(q);if(fault=="request_crc")q[60]^=1;d["request_hex"]=Hex(q);}
            if(fault=="request_hex")d["request_hex"]="00";if(fault=="request_id")d["request_id"]=58;if(fault=="operation")d["operation"]=2;
            if(fault=="query")d["request_hex"]=Hex(NpFirstSyncSd.Query(NpFirstSyncSd.Status,57,family,Uid,Token));
            if(fault=="attempt_id")d["attempt_id"]=" ";if(fault=="missing_field")d.Remove("token_hex");
            Save(session,d);File.WriteAllBytes(Path.Combine(session,"ble_clock_session.jsonl"),Journal);
            var companion=Companion(family);if(fault=="missing_decl")companion=null;
            var r=NpFineClockEvidenceImport.Import(output,companion,metadata);NoClock(output,r,"Unbound metadata accepted: "+family+"/"+fault);
            Need(r.Status==(fault=="missing_decl"?"no_qualified_companion":"matching_attempt_missing"),"Wrong rejection state: "+fault);
            if(fault=="missing_decl")Need(!Directory.GetFiles(output).Any(),"Legacy path gained metadata scan/output I/O");
            else Need(!(bool)Report(output)["association_confirmed"],"Rejected attempt reported confirmed");
        }
    }
    static void Ambiguous(string root,int family,bool changedRequest){
        string baseDir=Folder(root,"ambiguous"+family+changedRequest),metadata=Folder(baseDir,"metadata"),session=Metadata(metadata),output=Folder(baseDir,"output");
        Save(session,Attempt(family));var second=Attempt(family);if(changedRequest){var q=Request(family);Put(q,8,58);Seal(q);second["request_id"]=58;second["request_hex"]=Hex(q);}else second["attempt_id"]="other-immutable-id";
        Save(session,second,"two");File.WriteAllBytes(Path.Combine(session,"ble_clock_session.jsonl"),Journal);
        var r=NpFineClockEvidenceImport.Import(output,Companion(family),metadata);NoClock(output,r,"Ambiguous immutable attempt was selected");Need(r.Status=="ambiguous_immutable_attempts","Ambiguity hidden");
        Need(Directory.GetFiles(output,"sd_start_import_*.json").Length==0,"Ambiguous attempt copied before decision");
    }
    static void Boundaries(string root){
        string baseDir=Folder(root,"boundaries"),metadata=Folder(baseDir,"metadata"),session=Metadata(metadata),output=Folder(baseDir,"outside_output");
        Save(Folder(metadata,"USB-other"),Attempt(1));Save(Folder(Folder(session,"nested"),"extra"),Attempt(1));Save(Folder(Folder(metadata,"USB-peer"),"session"),Attempt(1));Save(session,Attempt(1),"wrong-token",Token+1);
        var r=NpFineClockEvidenceImport.Import(output,Companion(1),metadata);NoClock(output,r,"Scan escaped exact BLE/session/token path");Need(r.Status=="matching_attempt_missing","Unexpected scan boundary state");
        output=Folder(baseDir,"missing_output");r=NpFineClockEvidenceImport.Import(output,Companion(1),Path.Combine(baseDir,"absent"));NoClock(output,r,"Missing root imported clocks");Need(r.Status=="metadata_root_missing","Missing root hidden");
        output=Folder(baseDir,"bad_receipt");var companion=Companion(1);companion.Bound.Receipt.Raw[324]^=1;r=NpFineClockEvidenceImport.Import(output,companion,metadata);NoClock(output,r,"Invalid raw on-card seal accepted");Need(r.Status=="error","Raw seal failure not diagnosed");
        output=Folder(baseDir,"no_ack_no_journal");Save(session,Attempt(1));r=NpFineClockEvidenceImport.Import(output,Companion(1),metadata);Need(r.Associated&&r.Status=="matched_no_clock_journals"&&r.JournalPaths.Length==0,"Before-delivery persisted request required ACK or invented clock");
        string duplicate=Folder(Path.GetDirectoryName(session),"duplicate");Save(duplicate,Attempt(1));File.WriteAllBytes(Path.Combine(session,"ble_clock_a.jsonl"),Journal);File.WriteAllBytes(Path.Combine(duplicate,"ble_clock_b.jsonl"),Journal);
        output=Folder(baseDir,"duplicates");r=NpFineClockEvidenceImport.Import(output,Companion(1),metadata);Need(r.Associated&&r.JournalPaths.Length==1,"Identical immutable attempt snapshots became ambiguity");
    }
    static void Limits(string root){
        foreach(string fault in new[]{"attempt_cap","file_cap","session_cap","exact_cap","journal_size","attempt_size","collision","report_error","output_absent"}){
            string baseDir=Folder(root,fault),metadata=Folder(baseDir,"metadata"),session=Metadata(metadata),output=Folder(baseDir,"output");Save(session,Attempt(1));
            if(fault=="attempt_cap")for(int i=0;i<128;i++)Save(session,Attempt(1),"duplicate"+i);
            else if(fault=="file_cap")for(int i=0;i<128;i++)File.WriteAllBytes(Path.Combine(session,"ble_clock_"+i+".jsonl"),Journal);
            else if(fault=="session_cap")for(int i=0;i<4096;i++)Folder(Path.GetDirectoryName(session),"empty"+i);
            else if(fault=="exact_cap")for(int i=0;i<127;i++)File.WriteAllBytes(Path.Combine(session,"ble_clock_"+i+".jsonl"),Journal);
            else File.WriteAllBytes(Path.Combine(session,"ble_clock_session.jsonl"),Journal);
            if(fault=="journal_size"||fault=="attempt_size")using(var f=new FileStream(fault=="journal_size"?Path.Combine(session,"ble_clock_session.jsonl"):Directory.GetFiles(session,"sd_start_*.json").Single(),FileMode.Create,FileAccess.Write))f.SetLength(32L*1024*1024+1);
            string occupied=Path.Combine(output,"ble_clock_import_"+Hash(Journal)+".jsonl");byte[] other=Encoding.ASCII.GetBytes("occupied");if(fault=="collision")File.WriteAllBytes(occupied,other);
            if(fault=="report_error")Directory.CreateDirectory(Path.Combine(output,NpFineClockEvidenceImport.StatusFile));
            if(fault=="output_absent")output=Path.Combine(output,"missing");
            var r=NpFineClockEvidenceImport.Import(output,Companion(1),metadata);
            if(fault=="exact_cap"){Need(r.Associated&&r.Error==null&&r.JournalPaths.Length==1,"Exact 128-file limit was rejected or identical snapshots duplicated");continue;}
            if(fault=="report_error"){Need(r.Associated&&r.JournalPaths.Length==1&&r.Error!=null,"Optional report failure lost valid imported paths or escaped");continue;}
            Need(!r.Associated&&r.JournalPaths.Length==0,"Unbounded or failed import still reported usable: "+fault);
            Need(r.Status==(fault=="attempt_size"?"matching_attempt_missing":"error"),"Wrong limit/collision state: "+fault);
            if(fault=="attempt_cap"||fault=="file_cap"||fault=="session_cap")Need(!Directory.GetFiles(output,"ble_clock_*.jsonl").Any()&&!Directory.GetFiles(output,"sd_start_import_*.json").Any(),"Bounded scan copied before admission");
            if(fault=="collision")Need(File.ReadAllBytes(occupied).SequenceEqual(other),"Occupied filename overwritten");
        }
    }
    static int Main(string[] args){try{
        string root=Folder(args[0],Guid.NewGuid().ToString("N"));foreach(int family in new[]{1,2}){Good(root,family);Good(root,family,false,true);Rejected(root,family);Ambiguous(root,family,false);Ambiguous(root,family,true);}Good(root,2,true);Good(root,2,false,false,true);Good(root,2,true,false,true);Boundaries(root);Limits(root);
        Console.WriteLine("PASS "+checks+" strict V1/V2 sealed SD-to-host attempt association, raw partial-tail snapshot, matching, ambiguity, bounded scan/size and optional output checks; no hardware.");return 0;
    }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
