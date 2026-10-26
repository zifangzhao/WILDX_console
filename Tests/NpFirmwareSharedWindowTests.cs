using System;using System.Drawing;using System.IO;using System.Linq;using System.Reflection;using System.Windows.Forms;
class NpFirmwareSharedWindowTests{
 const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;static Assembly app;static int checks;
 static Type T(string n){return app.GetType("USB512_interface."+n,true);}static void Need(bool b,string s){checks++;if(!b)throw new Exception(s);}
 static FieldInfo Field(Type t,string n){for(;t!=null;t=t.BaseType){var f=t.GetField(n,All|BindingFlags.DeclaredOnly);if(f!=null)return f;}throw new Exception(n);}
 static object Get(object o,string n){return Field(o.GetType(),n).GetValue(o);}static void Set(object o,string n,object v){Field(o.GetType(),n).SetValue(o,v);}
 static object Call(object o,string n,params object[] a){for(Type t=o.GetType();t!=null;t=t.BaseType){var m=t.GetMethod(n,All|BindingFlags.DeclaredOnly);if(m!=null)return m.Invoke(o,a);}throw new Exception(n);}
 static object Target(bool v2,string uid){Type t=v2?T("NpFirmwareForm").GetNestedType("V2Target",All):T("NpV1Target");var o=Activator.CreateInstance(t,true);Set(o,"Uid",uid);Set(o,"Path",@"\\?\usb#vid_1209&pid_b386#"+(v2?"":"npv1-")+uid+"#{no-hardware}");return o;}
 static void Select(Form f,object target){var devices=(ComboBox)Get(f,"devices");if(!devices.Items.Contains(target))devices.Items.Add(target);devices.SelectedItem=target;}
 static uint Family(Form f){return (uint)Get(f,"selectedFamily");}
 static object Release(Form f){return f.GetType().GetProperty("SelectedFirmwareTarget",All).GetValue(f,null);}
 static uint ReleaseFamily(Form f){return (uint)Release(f).GetType().GetProperty("family",All).GetValue(Release(f),null);}
 static void Render(Form f,string file){var controls=f.Controls.Cast<Control>().ToArray();using(var host=new Form{ClientSize=f.ClientSize,Font=f.Font,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-10000,-10000)}){
  host.Controls.AddRange(controls);host.Show();Application.DoEvents();var target=(ComboBox)Get(f,"firmwareTarget");var part=(ComboBox)Get(f,"bundledPart");Need(target.Visible&&target.Parent.ClientRectangle.Contains(target.Bounds),"Firmware selector clipped");Need(part.Visible&&part.Parent.ClientRectangle.Contains(part.Bounds),"Update part selector clipped");
  using(var b=new Bitmap(host.Width,host.Height)){host.DrawToBitmap(b,new Rectangle(Point.Empty,host.Size));b.Save(file);}host.Hide();f.Controls.AddRange(controls);}}
 [STAThread]static int Main(string[] args){try{app=Assembly.LoadFrom(Path.GetFullPath(args[0]));Directory.CreateDirectory(args[1]);Application.EnableVisualStyles();
  Need(T("NpV2FirmwareForm").BaseType==T("NpFirmwareForm"),"V2 has a separate layout");const string a="001D00065942500B20313354",b="001A0004434250062039314E";
  using(var f=(Form)Activator.CreateInstance(T("NpFirmwareForm"),All,null,new object[]{@"\\?\usb#vid_1209&pid_b386#npv1-"+a+"#{offline}"},null)){
   var target=(ComboBox)Get(f,"firmwareTarget");var part=(ComboBox)Get(f,"bundledPart");Need(f.Text=="Firmware update"&&Family(f)==1&&ReleaseFamily(f)==1,"V1 entry is not shared/matched");Need(!(bool)Get(f,"working"),"Constructor started an update");Need(part.Items.Count==2&&part.SelectedIndex==0,"Shared APP/full options absent");
   var v1=Target(false,a);var v2=Target(true,b);Select(f,v1);Need(target.Items.Count==2&&target.SelectedIndex==0,"V1 targets missing");target.SelectedIndex=1;Need(ReleaseFamily(f)==1,"Camera selection changed family");Need(Release(f).GetType().GetProperty("full",All).GetValue(Release(f),null)!=null,"Camera full bundle missing");
   Select(f,v2);Need(Family(f)==2&&ReleaseFamily(f)==2&&target.Items.Count==1&&target.SelectedIndex==0,"V2 did not select its own bundle");Need(Get(f,"firmwareIdentity")==null&&Get(f,"deviceStatus")==null,"Stale V1 status retained");Need(!(bool)Get(f,"working"),"Changing family started update");Need(!((Button)Get(f,"abortUpdate")).Enabled&&!((Button)Get(f,"flash")).Enabled,"V1-only commands enabled for V2");Need(((Button)Get(f,"bundledApp")).Enabled,"V2 bundled update absent");
   Call(f,"SelectFullBundle");Need(part.SelectedIndex==1&&((Button)Get(f,"bundledApp")).Enabled,"V2 full bundle unavailable");Render(f,Path.Combine(args[1],"shared-v2.png"));
   var duplicate=Target(true,b);((ComboBox)Get(f,"devices")).Items.Add(duplicate);Call(f,"UpdateControls");Need(!((Button)Get(f,"bundledApp")).Enabled&&!((Button)Get(f,"update")).Enabled&&!((Button)Get(f,"inspect")).Enabled,"Ambiguous V2 UID allows commands");bool refused=false;try{Call(f,"SelectedV2");}catch(TargetInvocationException e){refused=e.InnerException is IOException&&e.InnerException.Message.Contains("ambiguous");}Need(refused,"Ambiguous V2 UID was substituted");((ComboBox)Get(f,"devices")).Items.Remove(duplicate);
   Select(f,v1);Need(Family(f)==1&&ReleaseFamily(f)==1&&part.SelectedIndex==0&&target.SelectedIndex==0,"V1 restored stale V2/full/camera selection");Need((string)Get(f,"selectedUid")==a,"Target UID substituted");f.Size=f.MinimumSize;Render(f,Path.Combine(args[1],"shared-v1-minimum.png"));
   Need(!Controls(f).OfType<Button>().Any(x=>x.Text.Contains("NP1 firmware manager")),"Separate V1 manager still linked");
  }
  using(var f=(Form)Activator.CreateInstance(T("NpV2FirmwareForm"),All,null,new object[]{null},null)){Need(f.Text=="Firmware update"&&Family(f)==2,"Legacy V2 entry does not share the window");Need(Controls(f).OfType<Button>().Any(x=>x.Text=="Update bundled firmware…"),"Shared update button missing");}
  Console.WriteLine("PASS "+checks+" shared V1/V2 window, bundle/UID selection, unsupported-command and layout checks; no discovery or hardware writes.");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
 static System.Collections.Generic.IEnumerable<Control> Controls(Control root){foreach(Control c in root.Controls){yield return c;foreach(var x in Controls(c))yield return x;}}
}
