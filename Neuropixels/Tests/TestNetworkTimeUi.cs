using System;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

// Actual WILDX controls, with a synthetic clock and device-owning Load removed.
internal static class TestNetworkTimeUi
{
    const BindingFlags All=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    static int checks;
    static void Check(bool value,string reason){checks++;if(!value)throw new Exception(reason);}
    static object Field(object o,string name){return o.GetType().GetField(name,All).GetValue(o);}
    static System.Collections.Generic.IEnumerable<Control> Children(Control owner){foreach(Control child in owner.Controls){yield return child;foreach(var next in Children(child))yield return next;}}
    static void Render(Form form,string path){
        form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-20000,-20000);form.Show();Application.DoEvents();
        using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(path);}
    }
    [STAThread]static int Main(string[] args){try{
        Application.EnableVisualStyles();var app=Assembly.LoadFrom(Path.GetFullPath(args[0]));Directory.CreateDirectory(args[1]);
        Check(app.GetName().Name=="WILDX_console","Actual WILDX assembly");
        var clockType=app.GetType("USB512_interface.NetworkTimeClock",true);var program=app.GetType("USB512_interface.Program",true);
        var configure=clockType.GetMethod("Configure",All,null,new[]{typeof(bool),typeof(string),typeof(bool)},null);
        object clock=Activator.CreateInstance(clockType,true);program.GetField("<NetworkClock>k__BackingField",All).SetValue(null,clock);
        try{
            configure.Invoke(clock,new object[]{true,"time.cloudflare.com",true});
            var sampleType=app.GetType("USB512_interface.NtpSample",true);
            string[] hosts={"time.cloudflare.com","time.nist.gov","time.windows.com"};
            for(int i=0;i<hosts.Length;i++){
                var sample=Activator.CreateInstance(sampleType,true);
                sampleType.GetField("Timestamp",All).SetValue(sample,Stopwatch.GetTimestamp());
                sampleType.GetField("OffsetMilliseconds",All).SetValue(sample,12.0);
                sampleType.GetField("DelayMilliseconds",All).SetValue(sample,9.0+i*20);
                sampleType.GetField("UncertaintyMilliseconds",All).SetValue(sample,10.0+i*10);
                sampleType.GetField("Stratum",All).SetValue(sample,3);
                sampleType.GetField("Endpoint",All).SetValue(sample,"UI fixture "+i);
                clockType.GetMethod("ObserveServer",All).Invoke(clock,new[]{hosts[i],sample});
            }
            var mainType=app.GetType("USB512_interface.Form1",true);
            using(var main=(Form)Activator.CreateInstance(mainType)){
                main.Load-=(EventHandler)Delegate.CreateDelegate(typeof(EventHandler),main,mainType.GetMethod("Form1_Load",All));
                foreach(IComponent component in ((IContainer)Field(main,"components")).Components)if(component is Timer)((Timer)component).Stop();
                ((Timer)Field(Field(main,"bleWorkspace"),"timer")).Stop();
                var card=main.Controls.Find("networkTimeCard",true).Single();var label=main.Controls.Find("networkTimeStatus",true).Single();
                Check(Children(card).Any(c=>c.Text=="Network time / Local"),"Panel title not local time");
                Check(label.Text.Contains(DateTimeOffset.Now.ToString("zzz")) && label.Text.Contains("above 10 ms target") && label.Text.Contains("time.cloudflare.com"),"Local offset/source/uncertainty omitted");
                Check(Children(card).OfType<Button>().Single().Enabled,"Time settings unavailable independently of USB");
                main.ClientSize=new Size(1320,1060);Render(main,Path.Combine(args[1],"network-time-local-usb.png"));
                foreach(int width in new[]{1044,1180,1450}){main.ClientSize=new Size(width,920);Application.DoEvents();Check(label.Parent.ClientRectangle.Contains(label.Bounds) && label.Height>=label.Font.Height*3,"Local clock text clipped on resize");}
            }
            var formType=app.GetType("USB512_interface.NetworkTimeForm",true);
            using(var form=(Form)Activator.CreateInstance(formType,All,null,new[]{clock},null)){
                var status=(TextBox)Field(form,"status");var automatic=(CheckBox)Field(form,"automatic");var enabled=(CheckBox)Field(form,"enabled");var server=(TextBox)Field(form,"server");
                Check(automatic.Checked && enabled.Checked && !server.Enabled,"Automatic setting or manual-field gating incorrect");
                Check(status.Text.Contains("Local time:") && status.Text.Contains(DateTimeOffset.Now.ToString("zzz")) && !status.Text.Contains("UTC:"),"Settings did not use local time with offset");
                Check(status.Text.Contains("above 10 ms target") && status.Text.Contains("stratum 3"),"Accuracy diagnosis omitted");
                Check(hosts.All(host=>status.Text.Contains(host)) && status.Text.Contains("quality"),"Candidate comparison omitted");
                automatic.Checked=false;Check(server.Enabled,"Manual server override unavailable");
                enabled.Checked=false;Check(!automatic.Enabled && !server.Enabled,"Disabled clock still permits server edits");
                enabled.Checked=automatic.Checked=true;
                Render(form,Path.Combine(args[1],"network-time-settings.png"));
                foreach(int width in new[]{680,720,960}){
                    form.ClientSize=new Size(width,480);Application.DoEvents();
                    foreach(var control in Children(form).Where(c=>c.Visible && (c is TextBox || c is Button || c is CheckBox || c is Label)))Check(control.Parent.ClientRectangle.Contains(control.Bounds),"Clipped time settings control: "+control.Text);
                }
                configure.Invoke(clock,new object[]{false,"localhost",false});formType.GetMethod("RefreshStatus",All).Invoke(form,null);
                Check(status.Text.Contains("Disabled") && !status.Text.Contains("Local time:"),"Disabled clock claims synchronization");
            }
            var settingsType=app.GetType("USB512_interface.Properties.Settings",true);
            Check(((DefaultSettingValueAttribute)settingsType.GetProperty("NetworkTimeAutomatic").GetCustomAttributes(typeof(DefaultSettingValueAttribute),false).Single()).Value=="True","Automatic selection not default");
            Check(((DefaultSettingValueAttribute)settingsType.GetProperty("NetworkTimeEnabled").GetCustomAttributes(typeof(DefaultSettingValueAttribute),false).Single()).Value=="True","Background sync default disabled");
        }finally{((IDisposable)clock).Dispose();program.GetField("<NetworkClock>k__BackingField",All).SetValue(null,null);}
        Console.WriteLine("PASS: "+checks+" actual WILDX network-time UI/default checks; no discovery, network or hardware.");return 0;
    }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
