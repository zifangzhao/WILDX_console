using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Windows.Forms;

static class NpV2BundledUpdateTests {
 const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;const string Uid="001E002A5942500B20313354";
 static Assembly app;static string root;static int checks;static Type T(string n){return app.GetType("USB512_interface."+n,true);}static void Need(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
 static object Get(object x,string n){var p=x.GetType().GetProperty(n,All);return p==null?x.GetType().GetField(n,All).GetValue(x):p.GetValue(x,null);}
 static void Set(object x,string n,object value){x.GetType().GetField(n,All).SetValue(x,value);}
 static object Call(object x,string n,params object[] args){try{return(x is Type?(Type)x:x.GetType()).GetMethod(n,All).Invoke(x is Type?null:x,args);}catch(TargetInvocationException ex){throw ex.InnerException;}}
 static void Refuse(Action action,string why){try{action();}catch(IOException){checks++;return;}catch(InvalidDataException){checks++;return;}throw new Exception(why);}
 static IEnumerable<Control> Controls(Control c){foreach(Control child in c.Controls){yield return child;foreach(var item in Controls(child))yield return item;}}
 sealed class Model {
  internal byte[] Flash;internal bool Dfu,WrongUid,CorruptReadback;internal int Requests,Writes,Starts,Reads;
  internal object Result(string output){var r=Activator.CreateInstance(T("NpFirmwareFlash+CliResult"),true);Set(r,"ExitCode",0);Set(r,"Output",output);return r;}
  public object Invoke(string[] args){
   if(args[0]=="-l")return Result("Total number of available devices in DFU mode: "+(Dfu?1:0)+"\n"+(Dfu?"Device Index : USB1\nSerial number : 123456789ABC\n":""));
   Need(args.Take(5).SequenceEqual(new[]{"-c","port=USB1","sn=123456789ABC","VID=0x0483","PID=0xDF11"}),"Programmer target not serial-pinned");
   Need(!args.Any(a=>a=="-e"||a=="all"||a=="-ob"||a=="-rdu"||a=="--skipErase"),"Forbidden erase/option command");
   string text="";for(int at=5;at<args.Length;){
    if(args[at]=="-u"){string address=args[at+1],path=args[at+3];byte[] bytes;
     if(address=="0x0BFA0700"){bytes=new byte[12];for(int p=0;p<3;p++)BitConverter.GetBytes(Convert.ToUInt32(Uid.Substring(p*8,8),16)).CopyTo(bytes,p*4);if(WrongUid)bytes[0]^=1;}
     else if(address=="0x0BFA07A0")bytes=BitConverter.GetBytes((ushort)(Flash.Length/1024));
     else{Need(address=="0x08000000","Unexpected flash read address");bytes=(byte[])Flash.Clone();Reads++;if(Reads>1&&CorruptReadback)bytes[0x1c100]^=1;}
     File.WriteAllBytes(path,bytes);at+=4;
    }else if(args[at]=="-d"){Writes++;ApplyHex(args[at+1]);at+=2;text="Download verified successfully";}
    else if(args[at]=="-v")at++;else if(args[at]=="-s"){Need(args[at+1]=="0x08000000","DFU exit bypasses recovery loader");Starts++;at+=2;}else throw new Exception("Unexpected programmer command");
   }return Result(text);
  }
  void ApplyHex(string path){uint upper=0;foreach(string line in File.ReadLines(path)){int count=Convert.ToByte(line.Substring(1,2),16),kind=Convert.ToByte(line.Substring(7,2),16);if(kind==4)upper=Convert.ToUInt32(line.Substring(9,4),16)<<16;
    if(kind!=0)continue;uint address=upper+Convert.ToUInt32(line.Substring(3,4),16);Need(address>=0x08000000&&address+count<=0x08200000&&!(address<0x0801e000&&address+count>0x0801c000),"Protected config or upper flash programmed");for(int n=0;n<count;n++)Flash[address-0x08000000+n]=Convert.ToByte(line.Substring(9+n*2,2),16);}}
 }
 static string Execute(Model model,object image,string name){Type result=T("NpFirmwareFlash+CliResult");var parameter=Expression.Parameter(typeof(string[]));var invoke=Expression.Call(Expression.Constant(model),typeof(Model).GetMethod("Invoke"),parameter);
  var callback=Expression.Lambda(typeof(Func<,>).MakeGenericType(typeof(string[]),result),Expression.Convert(invoke,result),parameter).Compile();
  var flow=Activator.CreateInstance(T("NpFirmwareFlash"),All,null,new object[]{callback,(Action<string,int>)((s,p)=>{}),Path.Combine(root,name)},null);
  return(string)Call(flow,"Execute",image,Uid,false,(Action)(()=>{model.Requests++;model.Dfu=true;}),(Func<bool>)(()=>true));
 }
 [STAThread]static int Main(string[] args){try{
  app=Assembly.LoadFrom(Path.GetFullPath(args[0]));root=Path.GetFullPath(args[1]);Directory.CreateDirectory(root);var catalog=T("NpFirmwareCatalog").GetProperty("Bundled",All).GetValue(null,null);Need(Get(catalog,"Error")==null,"Invalid installed firmware catalog");
  var release=Call(catalog,"For",2U);var asset=Get(release,"full");Need(asset!=null,"V2 full bundle absent");string path=(string)Call(catalog,"CheckedPath",asset),hash=(string)Get(asset,"sha256");
  var image=Call(T("NpFirmwareImage"),"LoadBundledV2",path,hash);Need((bool)Get(image,"Full")&&(uint)Get(image,"family")==2,"V2 full image admission failed");
  Refuse(()=>Call(T("NpFirmwareImage"),"LoadBundledV2",path,new string('0',64)),"Wrong fused SHA admitted");Refuse(()=>Call(T("NpFirmwareImage"),"Load",path),"V2 image admitted through V1 parser");
  var v1=Call(catalog,"For",1U);var v1asset=((IEnumerable)Get(v1,"files")).Cast<object>().Single(f=>((string)Get(f,"path")).Contains("fused_"));Refuse(()=>Call(T("NpFirmwareImage"),"LoadBundledV2",Call(catalog,"CheckedPath",v1asset),(string)Get(v1asset,"sha256")),"V1 fused image admitted as V2");
  var v1full=Call(T("NpFirmwareImage"),"Load",Call(catalog,"CheckedPath",v1asset));Need((bool)Get(v1full,"Full")&&(uint)Get(v1full,"family")==1,"Existing V1 fused image no longer admitted");
  var v1app=Call(T("NpFirmwareImage"),"Load",Call(catalog,"CheckedPath",Get(v1,"app")));Need(!(bool)Get(v1app,"Full"),"Existing V1 APP-only image no longer admitted");
  var before=new byte[4*1024*1024];for(int n=0;n<before.Length;n++)before[n]=(byte)(n*13+n/257);var expected=(byte[])Call(image,"ExpectedFlash",before);
  Need(expected.Skip(0x1c000).Take(8192).SequenceEqual(before.Skip(0x1c000).Take(8192)),"V2 config changed");Need(expected.Skip(0x200000).SequenceEqual(before.Skip(0x200000)),"V2 upper physical flash changed");
  var presence=(BitArray)Get(image,"present");Need(Enumerable.Range(0,presence.Length).Where(i=>!presence[i]).All(i=>expected[i]==before[i]),"V2 omitted flash bytes changed");Need(Enumerable.Range(0x1fd000,0x3000).All(i=>!presence[i]&&expected[i]==before[i]),"Current V2 omitted tail not preserved");
  foreach(uint missing in new[]{0x08000000U,0x0801e000U,0x08180000U}){
   uint upper=0;bool removed=false;var lines=new List<string>();foreach(string line in File.ReadAllLines(path)){
    int type=Int32.Parse(line.Substring(7,2),System.Globalization.NumberStyles.HexNumber);if(type==4)upper=UInt32.Parse(line.Substring(9,4),System.Globalization.NumberStyles.HexNumber)<<16;
    uint address=upper+UInt32.Parse(line.Substring(3,4),System.Globalization.NumberStyles.HexNumber);if(type==0&&address==missing){removed=true;continue;}lines.Add(line);
   }
   Need(removed,"Missing-record test did not remove required bytes");string broken=Path.Combine(root,"missing-"+missing.ToString("X8")+".hex");File.WriteAllLines(broken,lines,System.Text.Encoding.ASCII);string digest;using(var h=System.Security.Cryptography.SHA256.Create())digest=BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(broken))).Replace("-","");Refuse(()=>Call(T("NpFirmwareImage"),"LoadBundledV2",broken,digest),"Missing required recovery/APP/USB bytes admitted");
  }
  var model=new Model{Flash=(byte[])before.Clone()};Need(Execute(model,image,"success").Contains("Verified full firmware")&&model.Requests==1&&model.Writes==1&&model.Starts==1,"Full V2 flow failed");Need(model.Flash.SequenceEqual(expected),"Full V2 readback differs from expected/preserved bytes");
  model=new Model{Flash=(byte[])before.Clone(),WrongUid=true};Refuse(()=>Execute(model,image,"wrong-uid"),"Wrong V2 MCU UID accepted");Need(model.Writes==0&&model.Starts==0,"Wrong UID caused write/exit");
  model=new Model{Flash=(byte[])before.Clone(),CorruptReadback=true};Refuse(()=>Execute(model,image,"bad-readback"),"Corrupted preserved config accepted");Need(model.Writes==1&&model.Starts==0,"Bad readback exited DFU");
  model=new Model{Flash=(byte[])expected.Clone()};Execute(model,image,"unchanged");Need(model.Writes==0&&model.Starts==1,"Identical bundle was reflashed");
  Application.EnableVisualStyles();using(var form=(Form)Activator.CreateInstance(T("NpV2FirmwareForm"),All,null,new object[]{null},null)){
   var part=(ComboBox)Get(form,"bundledPart");Need(part.Items.Count==2&&part.Items[1].ToString().Contains("USB"),"Explicit full and APP options missing");Need(Controls(form).OfType<Button>().Any(b=>b.Text=="Update bundled firmware…"),"Visible bundled update button missing");Need(((string)Call(form,"FirmwareVersions")).Contains("comparison unavailable"),"Legacy V2 version comparison invented");
   ((Label)Get(form,"status")).Text=(string)Call(form,"FirmwareVersions");part.SelectedIndex=1;
   var controls=form.Controls.Cast<Control>().ToArray();using(var host=new Form{ClientSize=form.ClientSize,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-20000,-20000)}){host.Controls.AddRange(controls);host.Show();Application.DoEvents();using(var bitmap=new Bitmap(host.Width,host.Height)){host.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(root,"v2-bundled-update.png"));}host.Hide();form.Controls.AddRange(controls);}
  }
  Console.WriteLine("PASS "+checks+" actual-app V2 bundle/parser/guarded DFU model/UI checks; fake programmer only, no device access or real firmware writes.");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
