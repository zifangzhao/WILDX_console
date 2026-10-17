using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

static class NpBleContinuousRecordingTests {
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static Assembly app;static int checks;
    static void Need(bool value,string reason){checks++;if(!value)throw new Exception(reason);}
    static object Call(string type,string method,params object[] args){try{return app.GetType(type,true).GetMethod(method,All).Invoke(null,args);}catch(TargetInvocationException e){throw e.InnerException;}}
    static object Status(uint mask,uint hz,uint state,uint flags){var type=app.GetType("CE32_console.NpSdStatus",true);var value=Activator.CreateInstance(type,true);foreach(var pair in new[]{Tuple.Create("Mask",mask),Tuple.Create("Hz",hz),Tuple.Create("State",state),Tuple.Create("Flags",flags),Tuple.Create("Start",8192U),Tuple.Create("Capacity",131072U)})type.GetField(pair.Item1,All).SetValue(value,pair.Item2);return value;}
    static System.Collections.Generic.IEnumerable<Control> Children(Control parent){foreach(Control child in parent.Controls){yield return child;foreach(var descendant in Children(child))yield return descendant;}}
    static void Recording(){
        foreach(bool media in new[]{false,true}){
            object status=Status(media?0U:1U,media?0U:7500U,0,17U|512U|(media?0x600000U:0U));
            uint end=(uint)Call("CE32_console.NpV1BleProtocol","EndLba",status,0U,40U,true);
            Need(end==131072U,"Continuous V1 recording did not reserve remaining whole card allocation units");
            status.GetType().GetField("Flags",All).SetValue(status,17U|(media?0x600000U:0U));
            bool refused=false;try{Call("CE32_console.NpV1BleProtocol","EndLba",status,0U,40U,true);}catch(IOException){refused=true;}
            Need(refused,"Continuous V1 recording admitted without firmware support");
        }
        foreach(uint mask in new uint[]{1,3,7}){
            var status=Status(mask,7500,1,128U|512U|0x100000U);
            Need((uint)Call("CE32_console.NpV2SdControl","Blocks",status,7500U,0U)==0,"Continuous V2 recording sent a finite block budget");
        }
    }
    [STAThread]static int Main(string[] args){try{
        app=Assembly.LoadFrom(args[0]);Recording();var type=app.GetType("USB512_interface.NpBleForm",true);
        using(var form=(Form)Activator.CreateInstance(type,true)){
            Need(type.GetField("seconds",All)==null,"BLE duration input still exists");
            Need(!Children(form).Any(c=>c.Text.Contains("Storage bound")||c.Text.Contains("Duration reserves")),"Bounded recording timer remains visible");
            var settings=(TableLayoutPanel)Children(form).Single(c=>c.Name=="bleRecordingFields");
            var profiles=(ComboBox)type.GetField("profiles",All).GetValue(form);
            Need(settings.ColumnCount==1&&profiles.Parent==settings&&settings.GetColumn(profiles)==0,"Recording profile retains an empty timer column");
            var connection=(TableLayoutPanel)Children(form).Single(c=>c.Name=="bleConnectionFields");
            Need(connection.ColumnCount==2,"Removing the recording timer changed connection fields");
            using(var host=new Form{ClientSize=new Size(1050,650),ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-20000,-20000),Font=form.Font,BackColor=form.BackColor}){
                foreach(var child in form.Controls.Cast<Control>().ToArray())host.Controls.Add(child);host.Show();Application.DoEvents();
                Need(profiles.Width>settings.Width/2,"Profile field did not expand into the removed timer space");
                using(var bitmap=new Bitmap(host.Width,host.Height)){host.DrawToBitmap(bitmap,new Rectangle(Point.Empty,host.Size));bitmap.Save(args[1]);}
            }
        }
        Console.WriteLine("PASS "+checks+" actual-assembly BLE continuous-recording and layout checks; no scan, connection or hardware recording.");return 0;
    }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
