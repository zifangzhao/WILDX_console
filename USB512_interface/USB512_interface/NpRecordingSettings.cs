using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace USB512_interface {
 // File-only provenance writer. Call before START or after STOP, never from
 // USB completion, packet, sample or preview callbacks.
 internal sealed class NpRecordingSettings {
  readonly string folder,helper;
  readonly bool np2;
  readonly JavaScriptSerializer json=new JavaScriptSerializer {MaxJsonLength=4*1024*1024};
  readonly SortedDictionary<int,Dictionary<string,object>> probes=new SortedDictionary<int,Dictionary<string,object>>();
  readonly Dictionary<int,byte[]> requestedSelections=new Dictionary<int,byte[]>();
  readonly Dictionary<int,byte[]> appliedSelections=new Dictionary<int,byte[]>();
  readonly Dictionary<string,object> document;
  int activeMask;bool probeChildren;
  internal static Dictionary<string,object> Fields(params object[] values) {
   var d=new Dictionary<string,object>();for(int i=0;i<values.Length;i+=2)d.Add((string)values[i],values[i+1]);return d;
  }
  internal NpRecordingSettings(string folder,string helper,string source,bool np2,DateTime? startUtc,string path,string uid) {
   this.folder=folder;this.helper=helper;this.np2=np2;
   document=Fields("schema_version",1,"format","neuropixels.recording_settings","source",source,
    "software_version",typeof(NpRecordingSettings).Assembly.GetName().Version.ToString(),
    "recording_start_time_utc_host",startUtc.HasValue?(object)startUtc.Value.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture):null,
    "metadata_created_time_utc",DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture),
    "device",Fields("usb_path",path,"uid",uid,"firmware_version",null,"firmware_version_evidence","not advertised"),
    "requested",Fields("ap_sample_rate_hz",null,"probe_mask",null,"merge_probe_dat",null,"upsample_ap_15_to_20",null),
    "actual",Fields("ap_sample_rate_hz",null,"probe_mask",null,"lfp_sample_rate",Lfp(null),"evidence","not confirmed"),
    "decoded",Fields("ap_sample_rate_hz",null,"lfp_sample_rate",Lfp(null)),
    "output",Fields("ap_sample_rate_hz",null,"lfp_sample_rate",Lfp(null),"dat_layout","not established","channels_per_probe",384),
    "streams",Fields("neuropixels",true,"misc",null,"imu",null,"camera",false),
    "state","created","recording_complete",null,"failure",null,
    "provenance_note","Requested/editor settings are not device confirmation. Missing historical configuration is never filled from GUI defaults.");
   bool live=source=="live_usb" && !np2;
   document["misc_output"]=Fields("present",!np2,"file",np2?null:"analogin.dat","sample_rate_hz",live?(object)1250:null,
    "words_per_record",np2?null:(object)16,"record_bytes",np2?null:(object)32,
    "sample_format",np2?null:"headerless little-endian int16 words",
    "sampling_semantics",live?"800 us device-time grid; zero-order hold of latest sensor values; not 1250 Hz sensor acquisition":"historical collection rate unavailable");
   document["camera_output"]=Fields("enabled",false,"file",null,"frame_index_file",null,"requested_rate_hz",null,"actual_rate_hz",null);
  }
  Dictionary<string,object> Part(string name){return (Dictionary<string,object>)document[name];}
  Dictionary<string,object> Lfp(int? hz) {return Fields("present",!np2,"rate_numerator",np2?null:(object)hz,
   "rate_denominator",np2?null:(object)12,"rate_hz",np2 || !hz.HasValue?null:(object)(hz.Value/12.0));}
  internal void Requested(int? ap,int? mask,bool? merge,bool? upsample) {
   document["requested"]=Fields("ap_sample_rate_hz",ap,"probe_mask",mask,"merge_probe_dat",merge,"upsample_ap_15_to_20",upsample);
  }
  internal void Streams(bool misc,bool? imu,bool camera){document["streams"]=Fields("neuropixels",true,"misc",misc,"imu",imu,"camera",camera);
   document["camera_output"]=Fields("enabled",camera,"file",camera?"misc.dat":null,"frame_index_file",camera?"misc.frames.csv":null,"requested_rate_hz",null,"actual_rate_hz",null);}
  internal void DeviceEvidence(string key,object value){Part("device")[key]=value;}
  internal void Context(string key,object value){document[key]=value;}
  internal void PlannedLayout(int mask,bool merged,bool children) {
   activeMask=mask;probeChildren=children;
   for(int p=1;p<=3;p++)if((mask&(1<<(p-1)))!=0)Probe(p);
   Part("output")["dat_layout"]=merged?"merged":"per_probe";
   Part("output")["probe_subfolders"]=children;
   document["planned_probe_mask"]=mask;
  }
  internal void Actual(int ap,int outputAp,int mask,bool merged,bool children,string evidence) {
   if(ap<=0 || outputAp<=0 || mask<1 || mask>7)throw new ArgumentOutOfRangeException("mask");
   activeMask=mask;probeChildren=children;
   document["actual"]=Fields("ap_sample_rate_hz",ap,"probe_mask",mask,"lfp_sample_rate",Lfp(ap),"evidence",evidence);
   document["decoded"]=Fields("ap_sample_rate_hz",ap,"lfp_sample_rate",Lfp(ap));
   int count=0;for(int p=1;p<=3;p++)if((mask&(1<<(p-1)))!=0){Probe(p);count++;}
   foreach(var item in probes)item.Value["active"]=(mask&(1<<(item.Key-1)))!=0;
   document["output"]=Fields("ap_sample_rate_hz",outputAp,"lfp_sample_rate",Lfp(ap),"dat_layout",merged?"merged":"per_probe",
    "probe_subfolders",children,"channel_count_per_dat",merged?384*count:384,"channels_per_probe",384,
    "ap_upsampled_15_to_20",ap==15000 && outputAp==20000,"channel_order",merged?"ascending physical probe; contiguous 384-channel blocks":"hardware output channels 0..383 per probe");
   document["state"]="profile_confirmed";
  }
  Dictionary<string,object> Probe(int port) {
   Dictionary<string,object> p;if(probes.TryGetValue(port,out p))return p;
   if(port<1 || port>3)throw new ArgumentOutOfRangeException("port");
   p=Fields("schema_version",1,"probe",port,"active",null,"channels_per_band",384,"probe_file","probe_"+port+".json",
    "identity",null,"channel_mapping",null,"configuration_state","unavailable","configuration_evidence","No channel-selection receipt is available.",
    "requested_configuration_file",null,"applied_configuration_file",null,"device_receipt_file",null,"selection_file",null,
    "imro_file",null,"imro_state","unavailable","imro_origin",null);
   probes.Add(port,p);return p;
  }
  static string Quote(string value){if(value.Contains("\""))throw new ArgumentException("Invalid file path");return "\""+value+"\"";}
  string Helper(string arguments) {
   if(String.IsNullOrEmpty(helper) || !File.Exists(helper))throw new FileNotFoundException("Bundled NP configurator missing.",helper);
   using(var process=Process.Start(new ProcessStartInfo(helper,arguments) {
    UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true})) {
    var error=process.StandardError.ReadToEndAsync();var output=process.StandardOutput.ReadToEndAsync();process.WaitForExit();
    string detail=error.GetAwaiter().GetResult(),result=output.GetAwaiter().GetResult();
    if(process.ExitCode!=0)throw new IOException("NP snapshot serializer: "+detail.Trim());return result.Trim();
   }
  }
  bool Imro(Dictionary<string,object> p,string configuration,string output) {
   try {
    p["imro_origin"]=Helper("--recording-imro "+Quote(Path.Combine(folder,configuration))+" "+Quote(Path.Combine(folder,output)));
    p["imro_file"]=output;p.Remove("imro_unavailable_reason");return true;
   } catch(IOException ex) {p["imro_unavailable_reason"]=ex.Message;return false;}
  }
  internal void Request(int port,string configuration,byte[] selection=null) {
   if(String.IsNullOrWhiteSpace(configuration))return;
   var p=Probe(port);string prefix="probe_"+port,request=prefix+".requested.npconfig.json";
   File.WriteAllText(Path.Combine(folder,request),configuration,new UTF8Encoding(false));
   p["requested_configuration_file"]=request;p["configuration_state"]="requested_unverified";
   if(selection!=null)requestedSelections[port]=(byte[])selection.Clone();
   if(Imro(p,request,prefix+".requested.imro")) {
    File.Copy(Path.Combine(folder,prefix+".requested.imro"),Path.Combine(folder,prefix+".imro"),true);
    p["imro_file"]=prefix+".imro";p["imro_state"]="requested_unverified";
   }
  }
  internal void ConfirmRequest(int port) {
   var p=Probe(port);if(p["requested_configuration_file"]==null)throw new InvalidOperationException("No saved request to confirm.");
   p["configuration_state"]="device_confirmed_requested";p["configuration_evidence"]="Matching ID, CRC and verification receipt before capture release.";
   p["device_receipt_file"]="requested-configuration-applied-receipts.txt";p["imro_state"]=p["imro_file"]==null?"unavailable":"device_confirmed_requested";
   byte[] selection;if(requestedSelections.TryGetValue(port,out selection))appliedSelections[port]=(byte[])selection.Clone();
  }
  internal void Receipt(NpProbeManifest manifest) {
   int port=manifest.Port+1;var p=Probe(port);string prefix="probe_"+port;
   string receipt="neuropixels_probe"+port+"_metadata.json",selection="neuropixels_probe"+port+"_applied-selection.npb1";
   var saved=(Dictionary<string,object>)json.DeserializeObject(manifest.BuildJson(selection));
   p["identity"]=saved["identity"];p["device_snapshot"]=saved["configuration"];p["device_receipt_file"]=receipt;
   p["device_receipt_time"]="after_stop";
   if(!manifest.HasSelection) {
    if((string)p["configuration_state"]!="device_confirmed_requested") {
     p["configuration_state"]="device_default_without_selection_snapshot";
     p["configuration_evidence"]="Device receipt has no selection; exact default routing/gains are unavailable.";
     appliedSelections.Remove(port);
    }
    return;
   }
   p["selection_file"]=selection;
   p["channel_mapping"]=((Dictionary<string,object>)saved["configuration"])["channels"];
   string native=prefix+".applied.npconfig.json";
   byte[] requested;
   bool same=(string)p["configuration_state"]=="device_confirmed_requested" && p["imro_file"]!=null &&
    requestedSelections.TryGetValue(port,out requested) && Equal(requested,manifest.Selection);
   if(!same) {
    p["imro_file"]=null;p["imro_state"]="unavailable";p["imro_origin"]=null;
    if(File.Exists(Path.Combine(folder,prefix+".imro")))File.Delete(Path.Combine(folder,prefix+".imro"));
   }
   try {
    Helper("--snapshot-v1 "+Quote(Path.Combine(folder,selection))+" "+Quote(Path.Combine(folder,native)));
    p["applied_configuration_file"]=native;
    // Keep an exact imported table when the held device selection matches the
    // saved request; otherwise serialize the held snapshot, never GUI defaults.
    if(!same) {
     if(Imro(p,native,prefix+".imro"))p["imro_state"]="device_snapshot";
    }
   } catch(IOException ex) {p["configuration_snapshot_error"]=ex.Message;}
   int required=manifest.Selection[4]==2?3:4;
   if(manifest.ConfigurationState==3 && (manifest.Verification&required)==required)appliedSelections[port]=(byte[])manifest.Selection.Clone();
   else appliedSelections.Remove(port);
   p["configuration_state"]=manifest.ConfigurationState==3 && (manifest.Verification&required)==required?"device_confirmed_applied":"device_snapshot_unverified";
   p["configuration_evidence"]="Post-stop firmware-held selection and CRC; verification level is recorded in the receipt; analog signals are not verified.";
   if(p["imro_file"]!=null)p["imro_state"]=p["configuration_state"];
  }
  static bool Equal(byte[] a,byte[] b){if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
  internal void Finished(bool complete,string failure){document["state"]=complete?"complete":"incomplete";document["recording_complete"]=complete;document["failure"]=failure;}
  void SaveJson(string path,object value) {
   string temporary=path+".tmp";File.WriteAllText(temporary,json.Serialize(value)+Environment.NewLine,new UTF8Encoding(false));
   if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
  }
  internal void Write() {
   WriteXml(folder,activeMask,(string)Part("output")["dat_layout"]=="merged");
   var list=new List<object>();foreach(var item in probes){list.Add(item.Value);SaveJson(Path.Combine(folder,"probe_"+item.Key+".json"),item.Value);}
   document["probes"]=list;SaveJson(Path.Combine(folder,"recording_settings.json"),document);
   if(!probeChildren)return;
   foreach(var item in probes)if((activeMask&(1<<(item.Key-1)))!=0) {
    string child=Path.Combine(folder,"probe"+item.Key);Directory.CreateDirectory(child);
    foreach(var file in Directory.GetFiles(folder,"probe_"+item.Key+".*"))File.Copy(file,Path.Combine(child,Path.GetFileName(file)),true);
    foreach(var name in new[]{"neuropixels_probe"+item.Key+"_metadata.json","neuropixels_probe"+item.Key+"_applied-selection.npb1","requested-configuration-applied-receipts.txt"})
     if(File.Exists(Path.Combine(folder,name)))File.Copy(Path.Combine(folder,name),Path.Combine(child,name),true);
    var local=(Dictionary<string,object>)json.DeserializeObject(json.Serialize(document));
    local["parent_recording_settings"]="../recording_settings.json";local["folder_probe_mask"]=1<<(item.Key-1);
    foreach(string kind in new[]{"misc_output","camera_output"}) {
     var shared=(Dictionary<string,object>)local[kind];
     foreach(string key in new[]{"file","frame_index_file"})if(shared.ContainsKey(key) && shared[key]!=null)shared[key]="../"+shared[key];
    }
    local["probes"]=new object[]{item.Value};var output=(Dictionary<string,object>)local["output"];
    output["dat_layout"]="per_probe";output["channel_count_per_dat"]=384;output["probe_subfolders"]=false;
    WriteXml(child,1<<(item.Key-1),false);
    SaveJson(Path.Combine(child,"recording_settings.json"),local);
   }
  }
  void WriteXml(string target,int mask,bool merged) {
   object rate;if(mask==0 || !Part("output").TryGetValue("ap_sample_rate_hz",out rate) || rate==null)return;
   var lfp=(Dictionary<string,object>)Part("output")["lfp_sample_rate"];
   double? lfpRate=lfp["rate_hz"]==null?(double?)null:Convert.ToDouble(lfp["rate_hz"],CultureInfo.InvariantCulture);
   NpNeuroscopeXml.WriteFolder(target,mask,merged,Convert.ToInt32(rate,CultureInfo.InvariantCulture),lfpRate,appliedSelections);
  }
  internal static NpRecordingSettings Saved(string folder,bool np2,int ap,int mask,uint version,uint session,string source,string uid=null,bool decoded=true) {
   var settings=new NpRecordingSettings(folder,null,source,np2,null,null,uid);
   settings.DeviceEvidence("recording_format_version",version);settings.DeviceEvidence("session_id",session);
   settings.Context("original_start_time_evidence","UTC unavailable in this descriptor; no export timestamp is substituted for recording start.");
   settings.Actual(ap,ap,mask,false,false,"validated saved recording descriptor");
   if(!decoded) {
    settings.document["decoded"]=Fields("ap_sample_rate_hz",null,"lfp_sample_rate",settings.Lfp(null));
    settings.document["output"]=Fields("ap_sample_rate_hz",null,"lfp_sample_rate",settings.Lfp(null),"dat_layout","none; raw archive");
   }
   settings.Streams(!np2,np2?(bool?)false:null,false);
   settings.Context("imu_format_supported",!np2 && version>=4);settings.Write();return settings;
  }
 }
}
