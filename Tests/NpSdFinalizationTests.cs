using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using USB512_interface;

static class NpSdFinalizationTests {
 static int checks;
 static void Need(bool value,string why){checks++;if(!value)throw new Exception(why);}
 static string Hash(string path){using(var stream=File.OpenRead(path))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");}
 static uint Crc(byte[] b,int count){uint crc=uint.MaxValue;for(int i=0;i<count;i++){crc^=b[i];for(int bit=0;bit<8;bit++)crc=(crc>>1)^((crc&1)!=0?0xedb88320U:0);}return ~crc;}
 static void Export(string image,string folder){
  int previous=-1,finished=0;
  using(var card=File.OpenRead(image))NpV2SdDatExport.ExportCommitted(card,8192,folder,0,()=>false,p=>{
   Need(p>=previous && p>=0 && p<=100,"Progress regressed");previous=p;
   if(p!=100)return;finished++;
   Need(File.ReadAllText(Path.Combine(folder,"np2_sd_export_status.txt")).Contains("Export complete: True"),"100% preceded success receipt");
   foreach(string file in Directory.GetFiles(folder,"*.dat")){
    Need(File.Exists(Path.ChangeExtension(file,".xml")),"100% preceded XML finalization");
    using(File.Open(file,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){}
   }
  });
  Need(finished==1 && previous==100,"Terminal100% must be emitted exactly once");
  string report=File.ReadAllText(Path.Combine(folder,"np2_sd_export_status.txt"));
  foreach(string file in Directory.GetFiles(folder,"*.dat")){
   var match=Regex.Match(report,@"file="+Regex.Escape(Path.GetFileName(file))+@";[^\r\n]*SHA256=([0-9A-F]{64})");
   Need(match.Success && match.Groups[1].Value==Hash(file),"Inline hash differs from independent stored-DAT hash");
  }
 }
 static void Main(string[] args){
  string root=Path.GetFullPath(args[0]);if(Directory.Exists(root))throw new IOException("Fresh test destination required");Directory.CreateDirectory(root);
  var baseline=Assembly.LoadFile(Path.GetFullPath(args[1])).GetType("USB512_interface.NpV2SdDatExport",true);
  Need(baseline.Assembly!=typeof(NpV2SdDatExport).Assembly,"Old/new SDK contexts must remain separate");
  var oldExport=baseline.GetMethod("ExportCommitted",BindingFlags.Public|BindingFlags.Static);
  var factory=Assembly.LoadFile(Path.GetFullPath(args[2])).GetType("NpV2SdDatExportTests",true).GetMethod("MakeImage",BindingFlags.NonPublic|BindingFlags.Static);
  foreach(string name in new[]{"healthy","gap","overflow"}){
   string image=Path.Combine(args[3],name+".img"),output=Path.Combine(root,name);string sourceHash=Hash(image);Export(image,output);
   foreach(string file in Directory.GetFiles(output,"*.dat"))Need(File.ReadAllBytes(file).SequenceEqual(File.ReadAllBytes(Path.Combine(args[3],name+"_dat",Path.GetFileName(file)))),"Saved native DAT bytes changed");
   string actualReport=File.ReadAllText(Path.Combine(output,"np2_sd_export_status.txt")).Replace("\r\n","\n");
   actualReport=Regex.Replace(actualReport,@"^SHA256 scope:[^\n]*\n","",RegexOptions.Multiline);
   Need(actualReport==File.ReadAllText(Path.Combine(args[3],name+"_dat/np2_sd_export_status.txt")).Replace("\r\n","\n"),"Stored state/loss/segment report differs from checkedBF32");
   Need(Hash(image)==sourceHash,"Source image changed");
  }
  foreach(int mask in new[]{1,3,7}){
   string image=Path.Combine(root,"mask"+mask+".img"),oldFolder=Path.Combine(root,"old_mask"+mask),newFolder=Path.Combine(root,"mask"+mask);
   var values=factory.GetParameters().Select(p=>p.IsOptional?p.DefaultValue:Type.Missing).ToArray();values[0]=image;values[1]=20000U;values[2]="";
   int portArgument=Array.FindIndex(factory.GetParameters(),p=>p.Name=="mask");Need(portArgument>=0,"Existing native fixture lacks probe selector");values[portArgument]=(uint)mask;factory.Invoke(null,values);
   using(var card=File.OpenRead(image))oldExport.Invoke(null,new object[]{card,8192U,oldFolder,0L,null,null,null});
   Export(image,newFolder);
   foreach(string file in Directory.GetFiles(newFolder,"*.dat")){
    Need(File.ReadAllBytes(file).SequenceEqual(File.ReadAllBytes(Path.Combine(oldFolder,Path.GetFileName(file)))),"Probe layout differs from pinnedBF32");
    Need(new FileInfo(file).Length>768*128,"Fixture must exercise full hash batches and remainder");
   }
  }
  string interrupted=Path.Combine(root,"interrupted.img");byte[] bytes=File.ReadAllBytes(Path.Combine(args[3],"healthy.img"));int footer=(8192+4)*512;
  BitConverter.GetBytes(4U).CopyTo(bytes,footer+100);BitConverter.GetBytes(2U).CopyTo(bytes,footer+104);var sector=new byte[512];Buffer.BlockCopy(bytes,footer,sector,0,512);BitConverter.GetBytes(Crc(sector,508)).CopyTo(bytes,footer+508);File.WriteAllBytes(interrupted,bytes);
  Export(interrupted,Path.Combine(root,"interrupted"));Need(File.ReadAllText(Path.Combine(root,"interrupted/np2_sd_export_status.txt")).Contains("Recording complete: False"),"Incomplete source was promoted complete");
  string canceled=Path.Combine(root,"canceled");bool stop=false,threw=false,terminal=false;
  try{using(var card=File.OpenRead(Path.Combine(args[3],"healthy.img")))NpV2SdDatExport.ExportCommitted(card,8192,canceled,0,()=>stop,p=>{if(p==99)stop=true;if(p==100)terminal=true;});}catch(OperationCanceledException){threw=true;}
  Need(threw && !terminal,"Finalization cancellation emitted success or was ignored");
  Need(Directory.GetFiles(canceled,"*.dat.partial").Length==2 && Directory.GetFiles(canceled,"*.dat").Length==0,"Cancellation discarded/published partial files");
  Need(File.ReadAllText(Path.Combine(canceled,"np2_sd_export_status.txt")).Contains("Export complete: False"),"Cancellation success receipt incorrect");
  if(args.Length>4){
   var app=Assembly.LoadFrom(Path.GetFullPath(args[4]));var bridge=app.GetType("CE32_console.NpCe64Export",true).GetMethod("ExportCalibrated",BindingFlags.NonPublic|BindingFlags.Static);
   string output=Path.Combine(root,"actual_app_export");object result;
   using(var card=File.OpenRead(Path.Combine(args[3],"healthy.img")))result=bridge.Invoke(null,new object[]{card,8192L,0L,output,(Func<bool>)(()=>false),null,null});
   Need(result!=null && File.Exists(Path.Combine(output,"recording_settings.json")) && File.Exists(Path.Combine(output,"recording_files.json")),"Actual application bridge did not finish metadata");
   Need(Hash(result.GetType().Assembly.Location)==Hash(typeof(NpV2SdDatExport).Assembly.Location),"Actual app used another SDK");
  }
  Console.WriteLine("PASS SD finalization: "+checks+" checks; inline checksums, byte-exact probe/gap/incomplete exports, terminal progress and cancellation; no hardware.");
 }
}
