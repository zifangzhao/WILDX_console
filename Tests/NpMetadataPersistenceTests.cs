using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

// Actual checked assemblies and real local file-sharing locks; no devices.
static class NpMetadataPersistenceTests {
 const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 static Assembly app,companion;static int checks;
 static void Check(bool value,string why){checks++;if(!value)throw new Exception(why);}
 static Type T(string name){return app.GetType("USB512_interface."+name,false)??companion.GetType("USB512_interface."+name,true);}
 static object Call(Type type,object target,string name,params object[] args){try{return type.GetMethod(name,All).Invoke(target,args);}catch(TargetInvocationException e){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}}
 static object Settings(string folder){var type=T("NpRecordingSettings");var s=Activator.CreateInstance(type,All,null,new object[]{folder,null,"live_usb",false,(DateTime?)DateTime.UtcNow,null,null},null);Call(type,s,"Actual",20000,20000,3,true,false,"file-only fixture");return s;}
 static void Save(object settings){Call(T("NpRecordingSettings"),settings,"Write");}
 static void Text(string path,string text){Call(T("NpMetadataFile"),null,"WriteText",path,text);}
 static Exception Failure(Action action){try{action();throw new Exception("Expected metadata failure");}catch(IOException e){return e;}}
 static string Folder(string root,string name){string path=Path.Combine(root,name);Directory.CreateDirectory(path);return path;}
 static void Legacy(string root){
  string folder=Folder(root,"published18-repro");string dat=Path.Combine(folder,"neuropixels_ap.dat"),xml=Path.ChangeExtension(dat,".xml");File.WriteAllBytes(dat,new byte[1536]);var s=Settings(folder);Save(s);
  using(var block=new FileStream(xml,FileMode.Open,FileAccess.Read,FileShare.Read)) {
   var error=Failure(()=>Save(s));Check(error.StackTrace.Contains("NpNeuroscopeXml.Write")&&error.HResult==unchecked((int)0x80070020),"Published18 did not fail at the locked XML replacement: "+error);
   Console.WriteLine("PASS published 1.0.0.18 replacement-lock reproducer: "+error.Message+"; HRESULT=0x"+error.HResult.ToString("X8")+". This models the same writer path; it does not establish Yoshi's exact Windows cause.");
  }
  File.SetAttributes(xml,FileAttributes.ReadOnly);
  try{try{Save(s);throw new Exception("Read-only metadata replacement unexpectedly succeeded");}catch(IOException error){Console.WriteLine("Published18 read-only destination: "+error.Message+"; HRESULT=0x"+error.HResult.ToString("X8"));}catch(UnauthorizedAccessException error){Console.WriteLine("Published18 read-only destination: "+error.Message+"; HRESULT=0x"+error.HResult.ToString("X8"));}}
  finally{File.SetAttributes(xml,FileAttributes.Normal);}
 }
 static void Writer(string root){
  string folder=Folder(root,"writer"),path=Path.Combine(folder,"sidecar.json");Text(path,"old");Text(path,"new");Check(File.ReadAllText(path)=="new","Existing metadata update failed");
  var block=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
  var release=new Thread(()=>{Thread.Sleep(250);block.Dispose();});release.Start();Text(path,"after transient lock");release.Join();Check(File.ReadAllText(path)=="after transient lock","Transient lock was not retried");Check(Directory.GetFiles(folder,"*.tmp").Length==0,"Successful save leaked temporary metadata");
  byte[] old=File.ReadAllBytes(path);
  using(var held=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)) {
   var error=Failure(()=>Text(path,"complete failed candidate"));Check(error.GetType()==T("NpMetadataWriteException")&&error.Message.Contains(path)&&error.Message.Contains("Win32")&&error.Message.Contains("retained"),"Persistent lock lost file/error/candidate evidence");
   Check(old.SequenceEqual(File.ReadAllBytes(path)),"Failed save changed old metadata");var candidates=Directory.GetFiles(folder,"*.tmp");Check(candidates.Length==1&&File.ReadAllText(candidates[0])=="complete failed candidate","Complete candidate lost");
  }
  Text(path,"after release");Check(File.ReadAllText(path)=="after release","Recording metadata cannot recover after releasing lock");
  string[] before=Directory.GetFiles(folder,"*.tmp");var failure=Failure(()=>Call(T("NpMetadataFile"),null,"Write",path,(Action<Stream>)(stream=>{stream.WriteByte(42);throw new IOException("injected serialization failure");})));
  Check(failure.Message.Contains(path)&&File.ReadAllText(path)=="after release","Partial serialization replaced good metadata");Check(before.SequenceEqual(Directory.GetFiles(folder,"*.tmp")),"Incomplete serialization candidate was retained as complete");
  string parallel=Path.Combine(folder,"parallel.json");Parallel.For(0,30,i=>Text(parallel,"{\"value\":"+i+"}"));var parsed=new JavaScriptSerializer().DeserializeObject(File.ReadAllText(parallel));Check(parsed!=null&&Directory.GetFiles(folder,"parallel.json.*.tmp").Length==0,"Concurrent writers produced torn JSON or collided");
 }
 static void Bundle(string root){
  string folder=Folder(root,"finalization"),dat=Path.Combine(folder,"neuropixels_ap.dat"),xml=Path.ChangeExtension(dat,".xml");byte[] samples=Enumerable.Range(0,3072).Select(i=>(byte)i).ToArray();File.WriteAllBytes(dat,samples);File.WriteAllBytes(Path.Combine(folder,"neuropixels_lfp.dat"),new byte[1536]);var s=Settings(folder);Save(s);
  Call(T("NpRecordingSettings"),s,"Finished",true,null);string lfpXml=Path.Combine(folder,"neuropixels_lfp.xml");File.WriteAllText(lfpXml,"old LFP sidecar");
  using(var block=new FileStream(xml,FileMode.Open,FileAccess.Read,FileShare.Read)) {
   var error=Failure(()=>Save(s));Check(error.GetType()==T("NpMetadataWriteException"),"Bundle lost metadata failure category");
   var state=(System.Collections.Generic.Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(folder,"recording_settings.json")));Check((bool)state["recording_complete"],"XML failure prevented saving acquisition completion");
   Check(File.ReadAllText(lfpXml).Contains("<parameters"),"Locked AP metadata prevented saving the LFP sidecar");
   Check(File.Exists(Path.Combine(folder,"probe_1.json"))&&File.Exists(Path.Combine(folder,"probe_2.json"))&&File.Exists(Path.Combine(folder,"recording_files.json")),"One locked sidecar prevented the remaining metadata saves");
   string warning=(string)Call(T("NpRecordingSettings"),null,"ReportWriteFailure",folder,error);Check(File.ReadAllText(Path.Combine(folder,"metadata_save_error.txt")).Contains(xml),"Metadata error journal lost destination path");
   string message=(string)Call(T("Form1"),null,"NpCaptureOutcome",null,warning);Check(message.StartsWith("Stopped;")&&message.Contains("metadata warning")&&!message.StartsWith("Capture error:"),"Metadata-only failure falsely reported capture corruption");
   message=(string)Call(T("Form1"),null,"NpCaptureOutcome","NP global sequence gap",warning);Check(message.StartsWith("Capture error: NP global sequence gap")&&message.Contains("metadata warning"),"Real acquisition error was hidden by metadata warning");
  }
  Save(s);Call(T("NpRecordingSettings"),null,"RefreshBundle",folder);Check(samples.SequenceEqual(File.ReadAllBytes(dat)),"Metadata or retry changed neural DAT bytes");
  var index=File.ReadAllText(Path.Combine(folder,"recording_files.json"));Check(index.Contains("metadata_save_error.txt")&&!index.Contains(".tmp"),"Bundle omitted the metadata warning or indexed an uncommitted candidate");
  Check((string)Call(T("Form1"),null,"NpCaptureOutcome",null,null)=="Stopped","Normal Stop changed");
  string settingsPath=Path.Combine(folder,"recording_settings.json"),snapshot=File.ReadAllText(settingsPath);
  Call(T("NpRecordingSettings"),s,"Context","ui_must_not_rewrite",true);
  using(var form=(IDisposable)Activator.CreateInstance(T("Form1"),true))using(var held=new FileStream(Path.Combine(folder,"probe_1.json"),FileMode.Open,FileAccess.Read,FileShare.Read)) {
   T("Form1").GetField("runFolder",All).SetValue(form,folder);var kind=T("Form1").GetField("selectedDeviceKind",All);kind.SetValue(form,Enum.Parse(kind.FieldType,"XeNp"));T("Form1").GetField("npRecordingSettings",All).SetValue(form,s);
   Call(T("Form1"),form,"FinishCapturePresentation","Stopped");
   Check(File.ReadAllText(settingsPath)==snapshot,"Completion UI repeated the owner's finalized settings write");
   Check(!((string)T("Form1").GetField("lastCaptureReport",All).GetValue(form)).Contains("Report persistence failed"),"Locked probe sidecar caused the report UI to repeat metadata finalization");
  }
 }
 [STAThread]static int Main(string[] args){try{string binary=Path.GetFullPath(args[0]);app=Assembly.LoadFrom(binary);companion=Assembly.LoadFrom(Path.Combine(Path.GetDirectoryName(binary),"NpV2SdDatExport.dll"));string root=Folder(Path.GetFullPath(args[1]),"run_"+Guid.NewGuid().ToString("N"));if(args.Length>2&&args[2]=="--legacy"){Legacy(root);return 0;}Writer(root);Bundle(root);Console.WriteLine("PASS "+checks+" actual-assembly metadata persistence checks: real transient/persistent locks, candidate retention, old-file preservation, serialization cleanup, concurrent writes, independent bundle saves, Stop classification, UI ownership and untouched DAT; no hardware.");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
