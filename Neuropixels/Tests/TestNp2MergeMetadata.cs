using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Xml;
static class Np2MergeMetadataTests {
 const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
 static int checks;
 static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
 static int Main(string[] args){try{
  var a=Assembly.LoadFrom(Path.GetFullPath(args[0]));var t=a.GetType("USB512_interface.NpRecordingSettings",true);string root=Path.GetFullPath(args[1]);
  foreach(int mask in new[]{3,7})foreach(bool merged in new[]{false,true}){
   string folder=Path.Combine(root,mask+"-"+merged);Directory.CreateDirectory(folder);
   for(int p=1;p<=3;p++)if((mask&(1<<(p-1)))!=0){string child=Path.Combine(folder,"probe"+p);Directory.CreateDirectory(child);File.WriteAllBytes(Path.Combine(child,"neuropixels_ap.dat"),new byte[768]);}
   if(merged)File.WriteAllBytes(Path.Combine(folder,"neuropixels_ap.dat"),new byte[mask==7?2304:1536]);
   object s=t.GetConstructor(F,null,new[]{typeof(string),typeof(string),typeof(string),typeof(bool),typeof(DateTime?),typeof(string),typeof(string)},null).Invoke(new object[]{folder,null,"live_usb",true,DateTime.UtcNow,"fixture","001A0004434250062039314E"});
   t.GetMethod("Requested",F).Invoke(s,new object[]{15000,mask,merged,false});
   t.GetMethod("Actual",F).Invoke(s,new object[]{15000,15000,mask,merged,true,"file-only regression"});
   t.GetMethod("Finished",F).Invoke(s,new object[]{false,"transport gap; incomplete prefix"});t.GetMethod("Write",F).Invoke(s,null);
   var doc=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(folder,"recording_settings.json")));
   var output=(Dictionary<string,object>)doc["output"];Check((string)output["dat_layout"]==(merged?"merged":"per_probe"),"Root layout");Check((int)output["channel_count_per_dat"]==(merged?(mask==7?1152:768):384),"Root channels");Check((bool)doc["recording_complete"]==false,"Gap lost in settings");
   var lfp=(Dictionary<string,object>)output["lfp_sample_rate"];Check(!(bool)lfp["present"],"NP2 synthetic LFP advertised");
   for(int p=1;p<=3;p++)if((mask&(1<<(p-1)))!=0){string child=Path.Combine(folder,"probe"+p);var local=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(child,"recording_settings.json")));var childOutput=(Dictionary<string,object>)local["output"];Check((int)childOutput["channel_count_per_dat"]==384&&(string)childOutput["dat_layout"]=="per_probe","Retained source metadata changed");var xml=new XmlDocument();xml.Load(Path.Combine(child,"neuropixels_ap.xml"));Check(xml.SelectSingleNode("//acquisitionSystem/nChannels").InnerText=="384","Source XML channels");}
   if(merged){var xml=new XmlDocument();xml.Load(Path.Combine(folder,"neuropixels_ap.xml"));Check(xml.SelectSingleNode("//acquisitionSystem/nChannels").InnerText==(mask==7?"1152":"768"),"Merged XML channels");Check(xml.SelectSingleNode("//acquisitionSystem/samplingRate").InnerText=="15000","Merged XML rate");}
  }
  Console.WriteLine("PASS "+checks+" actual GUI merged/per-probe JSON/XML and incomplete-prefix metadata checks; no hardware accessed.");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
