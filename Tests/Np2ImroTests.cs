using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

// Software-only check of the installed host dialog and existing bundled parser.
// Compile with csc /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll.
internal static class Np2ImroTests {
 const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 static readonly JavaScriptSerializer Json=new JavaScriptSerializer {MaxJsonLength=1024*1024};
 static int checks;
 static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
 static object Call(object target,string name,params object[] args){return target.GetType().GetMethod(name,All).Invoke(target,args);}
 static void Wait(Task task){while(!task.IsCompleted){Application.DoEvents();System.Threading.Thread.Sleep(5);}task.GetAwaiter().GetResult();}
 static string Request(Form form,int port=2){string json;((Dictionary<int,string>)form.GetType().GetField("requests",All).GetValue(form)).TryGetValue(port,out json);return json;}
 static Control Find(Control parent,string text){foreach(Control child in parent.Controls){if(child.Text==text)return child;var found=Find(child,text);if(found!=null)return found;}return null;}
 static Form Dialog(Type type,Dictionary<int,string> retained,bool np2=true){return (Form)Activator.CreateInstance(type,All,null,new object[]{null,null,np2,7,retained,null,null,null},null);}
 static string Table(string model,Dictionary<string,object> mapping){
  bool multi=model=="24"||model=="2013";var text=new StringBuilder("("+model+",384)");
  for(int ch=0;ch<384;ch++){
   int shank=multi?ch%4:0;var sites=(IList)mapping[multi?"Multi-shank Shank "+shank:"Single-shank"];
   int site=-1;for(int i=0;i<384;i++)if(Convert.ToInt32(sites[i])==ch){site=i;break;}
   if(site<0)throw new Exception("No fixture channel in first bank");
   text.Append(multi?"("+ch+" "+shank+" 0 0 "+site+")":"("+ch+" 1 0 "+site+")");
  }return text.ToString();
 }
 [STAThread] static void Main(string[] args){
  var assembly=Assembly.LoadFrom(Path.GetFullPath(args[0]));var type=assembly.GetType("USB512_interface.NpConfigurationDialog",true);
  string output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
  var catalog=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(args[2]));var mapping=(Dictionary<string,object>)catalog["Np2Mapping"];
  var retained=new Dictionary<int,string>();string expected=null;
  using(var form=Dialog(type,retained)){
   Check(Find(form,"Load IMRO / JSON…")!=null,"NP2 import control missing");
   Check(Find(form,"Edit channel map…")!=null,"Shared editor control missing");
   Check(Find(form,"Device staging / receipt…")==null,"Unsupported V1 staging exposed for NP2");
   foreach(string model in new[]{"21","2003","24","2013"}){
    string input=Path.Combine(output,model+".imro");File.WriteAllText(input,Table(model,mapping));
    Wait((Task)Call(form,"ImportFile",input,2));var document=Json.Deserialize<Dictionary<string,object>>(Request(form));
    Check((string)document["Profile"]==(model=="24"||model=="2013"?"Np2Multi":"Np2Single"),"Wrong NP2 profile");
    Check(Convert.ToInt32(document["Connector"])==2,"IMRO assigned to wrong connector");
    Check((string)document["HardwareState"]=="NotApplied","Host request falsely marked applied");
    Check(Encoding.UTF8.GetString(Convert.FromBase64String((string)document["OriginalImro"]))==File.ReadAllText(input),"Exact IMRO not retained");
    Check(((IList)document["Channels"]).Count==384,"Channel table incomplete");
    expected=Request(form);
   }
   Wait((Task)Call(form,"ImportFile",Path.Combine(output,"21.imro"),3));string third=Request(form,3);
   Check(Request(form)==expected,"Switching ports lost request");
   Check(retained.Count==0,"Host requests committed before acceptance");
   string native=Path.Combine(output,"native.json");var saved=Json.Deserialize<Dictionary<string,object>>(expected);File.WriteAllText(native,Json.Serialize(saved));
   Wait((Task)Call(form,"ImportFile",native,2));Check((string)Json.Deserialize<Dictionary<string,object>>(Request(form))["HardwareState"]=="NotApplied","Native import retained an applied claim");expected=Request(form);
   saved["HardwareState"]="Applied";File.WriteAllText(native,Json.Serialize(saved));bool appliedRejected=false;
   try{Wait((Task)Call(form,"ImportFile",native,2));}catch(IOException){appliedRejected=true;}
   Check(appliedRejected && Request(form)==expected,"Unverifiable applied-state claim accepted or replaced request");
   foreach(string text in new[]{"(2013,384)(0 0 0 0 0)",Np1Table()}){
    string bad=Path.Combine(output,"bad.imro");File.WriteAllText(bad,text);bool rejected=false;
    try{Wait((Task)Call(form,"ImportFile",bad,2));}catch(IOException){rejected=true;}
    Check(rejected,"Incomplete or NP1 IMRO accepted");Check(Request(form)==expected,"Failed import replaced valid request");
   }
   Call(Find(form,"Save settings"),"OnClick",EventArgs.Empty);
   Check(retained.Count==2 && retained[2]==expected && retained[3]==third,"Per-port requests not committed independently");
  }
  using(var cancel=Dialog(type,retained)){
   Wait((Task)Call(cancel,"ImportFile",Path.Combine(output,"21.imro"),2));
  }
  Check(retained[2]==expected,"Cancel overwrote committed request");
  using(var layout=Dialog(type,retained)){
   Wait((Task)Call(layout,"ImportFile",Path.Combine(output,"2013.imro"),1));
   Check(Convert.ToInt32(Json.Deserialize<Dictionary<string,object>>(Request(layout,1))["Connector"])==1 && Request(layout,2)==expected,"Probe 1 import altered a different probe");
   layout.StartPosition=FormStartPosition.Manual;layout.Location=new Point(-32000,-32000);layout.ShowInTaskbar=false;layout.Opacity=0;
   layout.Show();Application.DoEvents();layout.PerformLayout();
   using(var image=new Bitmap(layout.Width,layout.Height)){layout.DrawToBitmap(image,new Rectangle(Point.Empty,layout.Size));image.Save(Path.Combine(output,"np2-imro-dialog.png"));}
   var cards=(Array)type.GetField("cards",All).GetValue(layout);
   var maps=(Array)type.GetField("maps",All).GetValue(layout);
   for(int i=0;i<3;i++){
    var card=(Control)cards.GetValue(i);Check(card.Controls[0].Bottom+card.Padding.Bottom<=card.Height,"Probe card content clipped");
    if(i>0)Check(card.Top>=((Control)cards.GetValue(i-1)).Bottom,"Probe cards overlap");
    object map=maps.GetValue(i);var first=(RectangleF)Call(map,"SiteBounds",0,0);var pair=(RectangleF)Call(map,"SiteBounds",0,1);var next=(RectangleF)Call(map,"SiteBounds",0,2);
    Check(first.X==pair.X && first.Y<pair.Y && next.X>first.X,"Physical electrode rows/columns replaced by channel order");
    Check(first.Left>=0 && next.Right<((Control)map).Width,"Probe geometry outside card");
   }
   var scroller=(Control)type.GetField("scroll",All).GetValue(layout);Check(((Control)cards.GetValue(2)).Bottom<=scroller.ClientSize.Height,"Three probes not visible at the default size");
   layout.Size=layout.MinimumSize;Application.DoEvents();
   for(int i=0;i<3;i++){var card=(Control)cards.GetValue(i);Check(card.Controls[0].Bottom+card.Padding.Bottom<=card.Height,"Minimum-size probe card clipped");}
   var actions=(Control)type.GetField("actions",All).GetValue(layout);
   foreach(Control action in actions.Controls)Check(actions.ClientRectangle.Contains(action.Bounds),"NP2 control clipped: "+action.Text);
  }
  using(var v1=Dialog(type,null,false))Check(Find(v1,"Edit channel map…")!=null && Find(v1,"Load IMRO / JSON…")!=null,"V1 did not use the shared editor workflow");
  int samples=0,unsupported=0;
  using(var examples=Dialog(type,new Dictionary<int,string>()))foreach(string path in new List<string>(args).GetRange(3,args.Length-3)){
   string model=Regex.Match(File.ReadAllText(path),@"\((\d+)[, ]").Groups[1].Value;
   if(model=="0"){
    string prepared=Path.Combine(output,"example-v1.json"),binary=Path.Combine(output,"example-v1.bin");
    if(File.Exists(binary))File.Delete(binary);
    Wait((Task)type.GetMethod("Run",All).Invoke(null,new object[]{"--prepare-v1 \""+path+"\" \""+prepared+"\" \""+binary+"\" 2"}));
    byte[] selection=File.ReadAllBytes(binary);Check(selection.Length==1576 && selection[4]==2 && selection[5]==1,"NP1 example did not prepare for its own device path");
   }else if(model=="21"||model=="24"||model=="2003"||model=="2013"){
    int port=1+samples%3;
    Wait((Task)Call(examples,"ImportFile",path,port));var document=Json.Deserialize<Dictionary<string,object>>(Request(examples,port));
    Check((string)document["Profile"]==(model=="24"||model=="2013"?"Np2Multi":"Np2Single"),"Example changed NP2 model");
    Check(Convert.ToInt32(document["Connector"])==port,"Example lost selected connector");
    Check((string)document["OriginalImro"]==Convert.ToBase64String(File.ReadAllBytes(path)),"Example's exact bytes changed");
   }else{unsupported++;Console.WriteLine("NOT SUPPORTED: "+Path.GetFileName(path)+" (type "+model+", not a standard NP1/NP2 model)");continue;}
   samples++;Console.WriteLine("PASS example: "+Path.GetFileName(path)+" (type "+model+")");
  }
  string recording=Path.Combine(output,"metadata");Directory.CreateDirectory(recording);
  var settingsType=assembly.GetType("USB512_interface.NpRecordingSettings",true);
  object settings=Activator.CreateInstance(settingsType,All,null,new object[]{recording,type.GetProperty("HelperPath",All).GetValue(null,null),"live_usb",true,DateTime.UtcNow,"fixture","fixture"},null);
  foreach(var request in retained)Call(settings,"Request",request.Key,request.Value,null);
  Call(settings,"Write");
  foreach(int p in new[]{2,3}){
   var receipt=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(recording,"probe_"+p+".json")));
   Check((string)receipt["configuration_state"]=="requested_unverified" && (string)receipt["imro_state"]=="requested_unverified","Host request falsely confirmed by metadata");
   Check(File.ReadAllText(Path.Combine(recording,"probe_"+p+".requested.imro"))==File.ReadAllText(Path.Combine(output,p==2?"2013.imro":"21.imro")),"Requested IMRO changed during metadata export");
  }
  Console.WriteLine("PASS: "+checks+" NP2 IMRO import/UI/per-port/cancel/provenance checks; "+samples+" supplied/vendor NP1/NP2 examples, "+unsupported+" non-NP1/NP2 examples unsupported. No hardware access.");
 }
 static string Np1Table(){var text=new StringBuilder("(0,384)");for(int ch=0;ch<384;ch++)text.Append("("+ch+" 0 0 1000 50 1)");return text.ToString();}
}
