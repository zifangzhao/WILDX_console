using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

// Actual release dialog and its bundled-APP handler. Only confirmation and
// application boundaries are replaced; device Shown/discovery never runs.
static class NpFirmwareTargetTests
{
    const BindingFlags All=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    static Assembly app;static Form form;static ComboBox targets;static object catalog,selected;
    static int checks,selections,applications;static bool cancel,changeTarget;static string applied;
    static object Get(object value,string name){var type=value.GetType();var p=type.GetProperty(name,All);return p==null?type.GetField(name,All).GetValue(value):p.GetValue(value,null);}
    static void Set(object value,string name,object data){value.GetType().GetField(name,All).SetValue(value,data);}
    static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static string PathFor(object release){return (string)catalog.GetType().GetMethod("CheckedPath",All).Invoke(catalog,new[]{Get(release,"app")});}
    static string Select(object release){selections++;selected=release;string path=PathFor(release);if(changeTarget)targets.SelectedIndex=1-targets.SelectedIndex;return cancel?null:path;}
    static Task Apply(string path){applications++;applied=path;return Task.CompletedTask;}
    static void Use()
    {
        var type=typeof(Func<,>).MakeGenericType(app.GetType("USB512_interface.NpFirmwareRelease",true),typeof(string));
        var select=Delegate.CreateDelegate(type,typeof(NpFirmwareTargetTests).GetMethod("Select",All));
        ((Task)form.GetType().GetMethod("UseBundledApp",All).Invoke(form,new object[]{select,(Func<string,Task>)Apply})).GetAwaiter().GetResult();
    }
    static void Update(){form.GetType().GetMethod("UpdateControls",All).Invoke(form,null);}
    static object Ready()
    {
        var state=Activator.CreateInstance(app.GetType("USB512_interface.NpV1Management+Status",true),true);
        Set(state,"Split",true);Set(state,"Recovery",true);Set(state,"Role",4U);Set(state,"Description","Idle — offline UI check");return state;
    }
    [STAThread]static int Main(string[] args)
    {
        try{
            app=Assembly.LoadFrom(Path.GetFullPath(args[0]));Directory.CreateDirectory(args[1]);Application.EnableVisualStyles();
            catalog=app.GetType("USB512_interface.NpFirmwareCatalog",true).GetProperty("Bundled",All).GetValue(null,null);
            const string uid="001D00065942500B20313354";
            string path=@"\\?\usb#vid_1209&pid_b386#npv1-"+uid+"#{offline}";
            using(form=(Form)Activator.CreateInstance(app.GetType("USB512_interface.NpFirmwareForm",true),All,null,new object[]{path},null)){
                targets=(ComboBox)Get(form,"firmwareTarget");
                Check(targets.DropDownStyle==ComboBoxStyle.DropDownList && targets.Items.Count==2,"Explicit firmware target choices missing");
                Check((string)targets.Items[0]=="V1 NP (91k/91k)" && (string)targets.Items[1]=="V1 Camera40 — candidate","Target labels changed");
                Check(targets.SelectedIndex==0,"Camera target selected without user choice");
                Check(!(bool)Get(form,"working") && Get(form,"deviceStatus")==null && applications==0,"Constructing the dialog started a device operation");
                var state=Ready();Set(form,"deviceStatus",state);Update();
                foreach(int index in new[]{0,1,0,1}){
                    int before=applications;targets.SelectedIndex=index;
                    Check(applications==before && !(bool)Get(form,"working"),"Changing target automatically applied firmware");
                    var release=Get(form,"SelectedFirmwareTarget");string crc=index==0?"BC6B506E":"99BA4F27",usb=index==0?"40D39075":"82C6E21A";
                    Check((string)Get(release,"appCrc32")==crc && (string)Get(release,"usbCrc32")==usb,"Selected target identity is stale");
                    string expected=PathFor(release);Check(expected.Contains(index==0?"idle-sleep-test":"camera40-default-candidate"),"Wrong target APP resolved");
                    string summary=((Label)Get(form,"status")).Text;
                    if(index==1)Check(summary.Contains("99BA4F27") && summary.Contains("82C6E21A") && summary.Contains("strict USB completeness failed with 11 reported camera drops"),"Camera identity/limits are hidden or stale");
                    Use();Check(Object.ReferenceEquals(selected,release) && applications==before+1 && applied==expected,"Bundled-APP handler routed a stale payload");
                    Check((string)Get(form,"selectedUid")==uid,"Target choice changed the exact device UID");
                }
                int count=applications;cancel=true;Use();cancel=false;Check(applications==count,"Cancel still applied an APP");
                targets.SelectedIndex=0;changeTarget=true;bool refused=false;
                try{Use();}catch(IOException){refused=true;}changeTarget=false;
                Check(refused && applications==count && targets.SelectedIndex==1,"Changing target during confirmation applied stale NP firmware");
                Set(form,"working",true);Update();int chosen=selections;Use();
                Check(!targets.Enabled && !((Button)Get(form,"bundledApp")).Enabled && selections==chosen && applications==count,"Working target/update gate was bypassed");
                Set(form,"working",false);Set(form,"deviceStatus",null);Update();Use();Check(selections==chosen && applications==count,"APP choice admitted without read status");
                var devices=(ComboBox)Get(form,"devices");var targetType=app.GetType("USB512_interface.NpV1Target",true);
                var same=Activator.CreateInstance(targetType,true);Set(same,"Uid",uid);Set(same,"Path",path);devices.Items.Add(same);devices.SelectedIndex=0;
                Check(targets.SelectedIndex==1,"Refreshing the same UID discarded explicit camera choice");
                var neighbor=Activator.CreateInstance(targetType,true);Set(neighbor,"Uid","001D00065942500B20313355");devices.Items.Add(neighbor);devices.SelectedIndex=1;
                Check(targets.SelectedIndex==0 && applications==count,"Camera choice carried silently to a different UID");
                devices.SelectedIndex=0;Set(form,"deviceStatus",Ready());targets.SelectedIndex=1;Update();
                form.Size=form.MinimumSize;var layout=form.Controls[0];var status=(Label)Get(form,"status");
                using(var host=new Form{ClientSize=form.ClientSize,Font=form.Font,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-20000,-20000)}){
                    host.Controls.Add(layout);host.Show();Application.DoEvents();
                    Check(targets.Visible && targets.Parent.ClientRectangle.Contains(targets.Bounds),"Target selector clipped at minimum size");
                    Check(status.Height>=status.GetPreferredSize(new Size(status.Width,0)).Height && status.Parent.ClientRectangle.Contains(status.Bounds),"Camera limits clipped at minimum size");
                    using(var image=new Bitmap(host.Width,host.Height)){host.DrawToBitmap(image,new Rectangle(Point.Empty,host.Size));image.Save(Path.Combine(args[1],"v1-camera-target.png"),ImageFormat.Png);}
                    host.Hide();form.Controls.Add(layout);
                }
            }
            Console.WriteLine("PASS "+checks+" actual-dialog target/APP-handler checks: explicit NP/camera mapping, switching, cancel/stale-choice and UID/busy gates, visible candidate limits; no Shown discovery, flash or hardware.");return 0;
        }catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
