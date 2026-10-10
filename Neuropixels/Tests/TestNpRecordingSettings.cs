using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using USB512_interface;
using WildNpConfiguration;

static class NpRecordingSettingsTests {
 static int checks;
 static readonly JavaScriptSerializer Json=new JavaScriptSerializer {MaxJsonLength=4*1024*1024};
 static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
 static Dictionary<string,object> Read(string folder,string file="recording_settings.json"){return (Dictionary<string,object>)Json.DeserializeObject(File.ReadAllText(Path.Combine(folder,file)));}
 static Dictionary<string,object> Part(Dictionary<string,object> d,string key){return (Dictionary<string,object>)d[key];}
 static double Number(object v){return Convert.ToDouble(v,CultureInfo.InvariantCulture);}
 static string Folder(string root,string name){string f=Path.Combine(root,name);Directory.CreateDirectory(f);return f;}
 static bool Equal(byte[] a,byte[] b){if(a.Length!=b.Length)return false;for(int n=0;n<a.Length;n++)if(a[n]!=b[n])return false;return true;}
 static void Run(string helper,string arguments) {
  using(var p=Process.Start(new ProcessStartInfo(helper,arguments){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true})) {
   string error=p.StandardError.ReadToEnd();p.WaitForExit();if(p.ExitCode!=0)throw new Exception(error);
  }
 }
 static string Q(string v){return "\""+v+"\"";}
 static void Put(byte[] b,int n,uint v){Buffer.BlockCopy(BitConverter.GetBytes(v),0,b,n,4);}
 static NpProbeManifest Manifest(byte[] selection) {
  var b=new byte[NpProbeManifest.WireBytes];Encoding.ASCII.GetBytes("NPM1").CopyTo(b,0);b[4]=1;b[5]=selection==null?(byte)0:selection[5];
  b[6]=(byte)(selection==null?0:1);Put(b,8,2);Put(b,16,2);Put(b,20,1234);b[30]=(byte)(selection==null?0:3);b[31]=3;
  if(selection!=null){Put(b,32,NpConfigClient.Crc(selection,selection.Length));Put(b,36,(uint)selection.Length);selection.CopyTo(b,108);}
  return NpProbeManifest.Parse(b);
 }
 static void Rates(Dictionary<string,object> d,int ap,int outputAp,int numerator,double lfp) {
  Check(Number(Part(d,"actual")["ap_sample_rate_hz"])==ap,"actual AP rate");
  Check(Number(Part(d,"decoded")["ap_sample_rate_hz"])==ap,"decoded AP rate");
  Check(Number(Part(d,"output")["ap_sample_rate_hz"])==outputAp,"saved AP rate");
  foreach(string key in new[]{"actual","decoded","output"}) {
   var rate=Part(Part(d,key),"lfp_sample_rate");Check((bool)rate["present"],"LFP present");
   Check(Number(rate["rate_numerator"])==numerator && Number(rate["rate_denominator"])==12,"exact native AP/12 fraction");
   Check(Math.Abs(Number(rate["rate_hz"])-lfp)<1e-9,"LFP must retain native fractional rate");
  }
 }
 static int Main(string[] args) {
  try {
   string helper=args[0],root=Folder(args[1],"fixtures"),input=Path.Combine(root,"original.imro"),native=Path.Combine(root,"request.json"),binary=Path.Combine(root,"request.npb1");
   CultureInfo.CurrentCulture=new CultureInfo("fr-FR");
   var text=new StringBuilder("(0,384)\r\n");for(int ch=0;ch<384;ch++)text.Append("("+ch+" 0 0 1000 50 1)\r\n");
   byte[] body=Encoding.UTF8.GetBytes(text.ToString()),original=new byte[body.Length+3];original[0]=239;original[1]=187;original[2]=191;body.CopyTo(original,3);File.WriteAllBytes(input,original);
   Run(helper,"--prepare-v1 "+Q(input)+" "+Q(native)+" "+Q(binary)+" 1");
   string request=File.ReadAllText(native);byte[] selection=File.ReadAllBytes(binary);
   string run=Folder(root,"segment_0001");var s=new NpRecordingSettings(run,helper,"live_usb",false,DateTime.UtcNow,"offline-fixture","uid-fixture");
   s.Requested(30000,7,false,true);s.Request(1,request,selection);s.PlannedLayout(7,false,true);s.Write();
   Check(Part(Read(run),"actual")["ap_sample_rate_hz"]==null,"a request is not an actual profile");
   s.ConfirmRequest(1);s.Actual(15000,20000,7,false,true,"offline READY fixture");
   var manifest=Manifest(selection);manifest.Save(run);s.Receipt(manifest);s.Finished(true,null);s.Write();
   var d=Read(run);Check(Number(Part(d,"requested")["ap_sample_rate_hz"])==30000,"original 30 kHz request retained");Rates(d,15000,20000,15000,1250);
   Check(Number(Part(d,"misc_output")["sample_rate_hz"])==1250 && Number(Part(d,"misc_output")["words_per_record"])==16 &&
    ((string)Part(d,"misc_output")["sampling_semantics"]).Contains("zero-order hold"),"live analogin output grid and held-value semantics");
   Check(Equal(original,File.ReadAllBytes(Path.Combine(run,"probe_1.imro"))),"unchanged imported BOM/CRLF IMRO preserved exactly");
   Check((string)Read(run,"probe_1.json")["imro_state"]=="device_confirmed_applied","matching held selection confirms IMRO");
   for(int p=1;p<=3;p++) {
    string child=Path.Combine(run,"probe"+p);Rates(Read(child),15000,20000,15000,1250);
    Check(File.Exists(Path.Combine(child,"probe_"+p+".json")),"each DAT child has its probe snapshot");
    Check(Number(Part(Read(child),"output")["channel_count_per_dat"])==384,"child DAT channel count");
    Check((string)Part(Read(child),"misc_output")["file"]=="../analogin.dat","child references shared root misc file");
   }
   Check(Read(run,"probe_2.json")["imro_file"]==null,"unknown default never gets fabricated IMRO");
   string second=Folder(root,"segment_0002");var next=new NpRecordingSettings(second,helper,"live_usb",false,DateTime.UtcNow,null,null);
   next.Requested(20000,3,true,false);next.Actual(20000,20000,3,true,false,"offline native mode");Manifest(null).Save(second);next.Receipt(Manifest(null));next.Write();Rates(Read(second),20000,20000,20000,20000/12.0);
   Check(Number(Part(Read(second),"output")["channel_count_per_dat"])==768,"merged DAT layout");
   Check(!Directory.Exists(Path.Combine(second,"probe1")),"merged recording stays at root");
   Check(Read(second,"probe_1.json")["channel_mapping"]==null && !File.Exists(Path.Combine(second,"probe_1.imro")),"default no-selection receipt stays unknown");
   var edited=(Dictionary<string,object>)Json.DeserializeObject(request);var channels=(object[])edited["Channels"];((Dictionary<string,object>)channels[0])["ApGain"]=500;
   string editJson=Path.Combine(root,"edited.json"),editImro=Path.Combine(root,"edited.imro");File.WriteAllText(editJson,Json.Serialize(edited));Run(helper,"--recording-imro "+Q(editJson)+" "+Q(editImro));
   Check(!Equal(original,File.ReadAllBytes(editImro)) && File.ReadAllText(editImro).Contains("(0 0 0 500 50 1)"),"edited configuration cannot reuse stale imported IMRO");
   string unicode=Path.Combine(root,"unicode.imro"),unicodeJson=Path.Combine(root,"unicode.json"),unicodeSaved=Path.Combine(root,"unicode-saved.imro");
   File.WriteAllText(unicode,text.ToString(),Encoding.Unicode);Run(helper,"--import-imro "+Q(unicode)+" "+Q(unicodeJson));Run(helper,"--recording-imro "+Q(unicodeJson)+" "+Q(unicodeSaved));
   Check(Equal(File.ReadAllBytes(unicode),File.ReadAllBytes(unicodeSaved)),"unchanged UTF16 BOM import also preserves original bytes");
   string copy=Path.Combine(Path.GetDirectoryName(helper),"metadata-test-helper.exe");File.Copy(helper,copy,true);
   string mismatch=Folder(root,"mismatch-helper-failure");var wrong=new NpRecordingSettings(mismatch,copy,"live_usb",false,DateTime.UtcNow,null,null);
   wrong.Request(1,request,selection);wrong.ConfirmRequest(1);File.Delete(copy);
   byte[] changed=(byte[])selection.Clone();changed[42]^=1;var changedReceipt=Manifest(changed);changedReceipt.Save(mismatch);wrong.Receipt(changedReceipt);wrong.Actual(20000,20000,1,false,false,"fixture");wrong.Write();
   var probe=Read(mismatch,"probe_1.json");Check(probe["imro_file"]==null && (string)probe["imro_state"]=="unavailable","mismatching receipt plus serializer failure must clear requested IMRO");
   Check(!File.Exists(Path.Combine(mismatch,"probe_1.imro")) && File.Exists(Path.Combine(mismatch,"probe_1.requested.imro")),"retain request only under explicit requested filename");
   string historical=Folder(root,"sd-historical");var saved=NpRecordingSettings.Saved(historical,false,20000,3,6,42,"sd_export");saved.Write();Rates(Read(historical),20000,20000,20000,20000/12.0);
   Check(Part(Read(historical),"requested")["ap_sample_rate_hz"]==null && Read(historical,"probe_1.json")["channel_mapping"]==null,"SD does not borrow GUI settings");
   Check(Read(historical)["recording_start_time_utc_host"]==null && Part(Read(historical),"streams")["imu"]==null,"historical UTC and IMU are unknown without evidence");
   Check(Part(Read(historical),"misc_output")["sample_rate_hz"]==null,"SD misc rate is not invented from live output grid");
   string np2=Folder(root,"np2");NpRecordingSettings.Saved(np2,true,30000,3,1,12,"sd_export").Write();
   foreach(string key in new[]{"actual","decoded","output"}) {var l=Part(Part(Read(np2),key),"lfp_sample_rate");Check(!(bool)l["present"] && l["rate_hz"]==null && l["rate_numerator"]==null,"NP2 must not invent LFP");}
   string archive=Folder(root,"raw-only");NpRecordingSettings.Saved(archive,false,15000,1,4,41,"sd_download",null,false).Write();
   Check(Part(Read(archive),"decoded")["ap_sample_rate_hz"]==null && Part(Read(archive),"output")["ap_sample_rate_hz"]==null,"raw archive does not claim decoded/output DAT");
   string occupied=Folder(root,"occupied-export");File.WriteAllBytes(Path.Combine(occupied,"descriptor.bin"),new byte[512]);File.WriteAllText(Path.Combine(occupied,"recording_settings.json"),"keep-original");
   var card=new byte[4096];Encoding.ASCII.GetBytes("N2SDLOG1").CopyTo(card,3*512);bool refused=false;
   try {using(var stream=new MemoryStream(card))CE32_console.NpCe64Export.Export(stream,0,8,occupied,null,null);}
   catch(IOException){refused=true;}
   Check(refused && File.ReadAllText(Path.Combine(occupied,"recording_settings.json"))=="keep-original","refused SD export must not overwrite an existing setup snapshot");
   Console.WriteLine("PASS recording settings: "+checks+" offline assertions; fixtures="+root);return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
 }
}
