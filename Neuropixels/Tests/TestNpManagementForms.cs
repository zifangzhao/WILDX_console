using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

// Construct the actual Release forms without showing them: Shown discovery and
// USB operations are never invoked. Exercise controls with in-memory status.
internal static class TestNpManagementForms
{
    const BindingFlags Instance=BindingFlags.Instance|BindingFlags.NonPublic;
    const BindingFlags Static=BindingFlags.Static|BindingFlags.NonPublic;
    static Assembly assembly;
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static object Field(object target,string name){return target.GetType().GetField(name,Instance).GetValue(target);}
    static void Set(object target,string name,object value){target.GetType().GetField(name,Instance).SetValue(target,value);}
    static void RefreshControls(Form form){form.GetType().GetMethod("UpdateControls",Instance).Invoke(form,null);}
    static IEnumerable<Control> Descendants(Control control){foreach(Control child in control.Controls){yield return child;foreach(var descendant in Descendants(child))yield return descendant;}}
    static object Status(bool recovery,uint busy,uint pending,uint capabilities,uint role=0)
    {
        var type=assembly.GetType("USB512_interface.NpV1Management+Status",true);
        var status=Activator.CreateInstance(type,true);
        Set(status,"Split",true);Set(status,"Recovery",recovery);Set(status,"Role",role==0?(recovery?4U:2U):role);
        Set(status,"Busy",busy);Set(status,"PendingMode",pending);Set(status,"Capabilities",capabilities);
        return status;
    }
    static void FirmwareGates(Form form,bool recovery,uint busy,uint pending,uint capabilities,bool working,uint role=0)
    {
        role=role==0?(recovery?4U:2U):role;
        Set(form,"deviceStatus",Status(recovery,busy,pending,capabilities,role));Set(form,"working",working);RefreshControls(form);
        bool idle=!working && busy==0 && pending==0;
        bool inPlace=!recovery && (capabilities&64)!=0;
        Check(((Button)Field(form,"update")).Enabled==(idle && (recovery || inPlace)),"APP update gate changed");
        Check(((Button)Field(form,"abortUpdate")).Enabled==(!working && pending==0 && inPlace && role==4),"APP abort gate changed");
        Check(((Button)Field(form,"romDfu")).Enabled==(idle && !recovery && role!=4 && (capabilities&16)!=0),"ROM DFU idle/capability gate changed");
        foreach(var button in (Button[])Field(form,"modeButtons"))Check(button.Enabled==idle,"Firmware mode busy gate changed");
        Check(((Button[])Field(form,"modeButtons"))[2].Text==(inPlace?"APP update mode":"Recovery"),"Update mode label does not reflect capability");
    }
    static void Complete(Task task,TaskCompletionSource<object> operation)
    {
        operation.SetResult(null);var watch=Stopwatch.StartNew();
        while(!task.IsCompleted && watch.ElapsedMilliseconds<2000)Application.DoEvents();
        Check(task.IsCompleted && !task.IsFaulted,"Offline UI operation did not finish");
    }
    static void Cancellation(Form form)
    {
        var operation=new TaskCompletionSource<object>();Func<Task> work=()=>operation.Task;
        var task=(Task)form.GetType().GetMethod("Run",Instance).Invoke(form,new object[]{work,true});
        Check((bool)Field(form,"working") && ((Button)Field(form,"cancel")).Enabled,"Download cancellation not available while working");
        Check(!((Button)Field(form,"download")).Enabled && !((Button)Field(form,"enterDownload")).Enabled && !((ComboBox)Field(form,"devices")).Enabled,"Working download gates changed");
        typeof(Button).GetMethod("OnClick",Instance).Invoke(Field(form,"cancel"),new object[]{EventArgs.Empty});
        Check((bool)Field(form,"cancelled") && !((Button)Field(form,"cancel")).Enabled,"Cancel does not signal existing workers");
        Complete(task,operation);Check(!(bool)Field(form,"working"),"Download busy gate did not clear");
    }
    static void Selection()
    {
        var targetType=assembly.GetType("USB512_interface.NpV1Target",true);
        var list=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(targetType));
        Func<string,object> target=uid=>{var value=Activator.CreateInstance(targetType,true);Set(value,"Uid",uid);return value;};
        var select=assembly.GetType("USB512_interface.NpV1Management",true).GetMethod("SelectionAfterRefresh",Static);
        list.Add(target("A"));list.Add(target("B"));
        Check((int)select.Invoke(null,new object[]{list,"B"})==1,"Refresh lost exact UID");
        Check((int)select.Invoke(null,new object[]{list,"C"})==-1,"Refresh substituted a different UID");
        Check((int)select.Invoke(null,new object[]{list,null})==-1,"Multiple loggers selected implicitly");
        list.Add(target("B"));Check((int)select.Invoke(null,new object[]{list,"B"})==-1,"Ambiguous UID accepted");
    }
    static void Snapshot(Form form,string output,string name)
    {
        // Render the existing controls in a plain offscreen host. The device
        // form's Shown handler (which discovers USB devices) is never raised.
        var layout=form.Controls[0];
        using(var host=new Form{Text=form.Text,AutoScaleMode=AutoScaleMode.None,Font=form.Font,BackColor=form.BackColor,ForeColor=form.ForeColor,
            ClientSize=form.ClientSize,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-20000,-20000)}){
            host.Controls.Add(layout);host.Show();Application.DoEvents();
            foreach(var control in Descendants(host))if(control is Button)Check(control.Visible && control.Width>0 && control.Height>0,"Action button is not laid out");
            using(var bitmap=new Bitmap(host.Width,host.Height)){host.DrawToBitmap(bitmap,new Rectangle(Point.Empty,host.Size));bitmap.Save(Path.Combine(output,name+".png"),ImageFormat.Png);}
            host.Hide();form.Controls.Add(layout);
        }
    }
    [STAThread] static int Main(string[] args)
    {
        try{
            assembly=Assembly.LoadFrom(Path.GetFullPath(args[0]));string output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
            Application.EnableVisualStyles();
            const string uid="001C00145942500B20313354";
            string path=@"\\?\usb#vid_1209&pid_b386#npv1-"+uid+"#{test}";
            var downloadType=assembly.GetType("USB512_interface.NpLoggerForm",true);
            var firmwareType=assembly.GetType("USB512_interface.NpFirmwareForm",true);
            using(var download=(Form)Activator.CreateInstance(downloadType,Instance,null,new object[]{path},null))
            using(var firmware=(Form)Activator.CreateInstance(firmwareType,Instance,null,new object[]{path},null)){
                Check(download.Text=="NP V1 — SD Download" && firmware.Text=="NP V1 — Firmware Update","Form purposes not separated");
                Check((string)Field(download,"selectedUid")==uid && (string)Field(firmware,"selectedUid")==uid,"Selected UID not forwarded");
                var downloadButtons=new List<string>();foreach(var control in Descendants(download))if(control is Button)downloadButtons.Add(control.Text);
                var firmwareButtons=new List<string>();foreach(var control in Descendants(firmware))if(control is Button)firmwareButtons.Add(control.Text);
                Check(downloadButtons.Contains("Download session") && downloadButtons.Contains("Decode saved archive…") && downloadButtons.Contains("Enter USB download mode") && downloadButtons.Contains("Cancel"),"Download controls missing");
                Check(!downloadButtons.Contains("Update SD app…") && !downloadButtons.Contains("Recovery") && !downloadButtons.Contains("Request ROM DFU…") && !downloadButtons.Contains("USB streaming") && !downloadButtons.Contains("SD app (USB disconnects)"),"Firmware controls leaked into Download");
                Check(firmwareButtons.Contains("Update SD app…") && firmwareButtons.Contains("Recovery") && firmwareButtons.Contains("Request ROM DFU…"),"Firmware controls missing");
                Check(!firmwareButtons.Contains("Download session") && !firmwareButtons.Contains("Decode saved archive…") && !firmwareButtons.Contains("Output folder…") && !firmwareButtons.Contains("Enter USB download mode") && !firmwareButtons.Contains("Cancel"),"Download controls leaked into Firmware Update");
                foreach(string name in new[]{"UpdateApp","RequestRomDfu","SwitchMode"})Check(downloadType.GetMethod(name,Instance)==null,"Download retained firmware operation "+name);
                foreach(string name in new[]{"Download","DecodeArchive","EnterDownloadMode"})Check(firmwareType.GetMethod(name,Instance)==null,"Firmware retained download operation "+name);
                foreach(string name in new[]{"sessions","folder","method","catalogPath","cancelled"})Check(firmwareType.GetField(name,Instance)==null,"Firmware retained download state "+name);
                Check(((Button[])Field(firmware,"modeButtons")).Length==3,"Firmware contains the download mode button");
                foreach(bool recovery in new[]{false,true})foreach(uint busy in new[]{0U,1U})foreach(uint pending in new[]{0U,3U})foreach(uint capabilities in new[]{0U,16U})foreach(bool working in new[]{false,true})FirmwareGates(firmware,recovery,busy,pending,capabilities,working);
                foreach(uint role in new[]{2U,3U,4U})foreach(uint busy in new[]{0U,1U})foreach(uint pending in new[]{0U,4U})foreach(uint capabilities in new[]{64U,80U})foreach(bool working in new[]{false,true})FirmwareGates(firmware,false,busy,pending,capabilities,working,role);
                FirmwareGates(firmware,true,0,0,0,false);
                var operation=new TaskCompletionSource<object>();Func<Task> work=()=>operation.Task;
                var task=(Task)firmwareType.GetMethod("Run",Instance).Invoke(firmware,new object[]{work});
                Check((bool)Field(firmware,"working") && !((Button)Field(firmware,"update")).Enabled && !((Button)Field(firmware,"romDfu")).Enabled,"Firmware operation is not gated");
                Complete(task,operation);Check(((Button)Field(firmware,"update")).Enabled,"Firmware gate did not restore");
                var targets=(ComboBox)Field(firmware,"devices");
                var targetType=assembly.GetType("USB512_interface.NpV1Target",true);
                var same=Activator.CreateInstance(targetType,true);Set(same,"Uid",uid);targets.Items.Add(same);
                Set(firmware,"updateReturnMode",(ushort)3);targets.SelectedIndex=0;
                Check((ushort)Field(firmware,"updateReturnMode")==3,"Refresh lost the selected UID's abort return role");
                var neighbor=Activator.CreateInstance(targetType,true);Set(neighbor,"Uid","001C00145942500B20313355");targets.Items.Add(neighbor);targets.SelectedIndex=1;
                Check((ushort)Field(firmware,"updateReturnMode")==0,"Abort return state leaked to a different UID");
                Set(download,"deviceStatus",Status(false,0,0,16));RefreshControls(download);Check(((Button)Field(download,"enterDownload")).Enabled,"Download mode unavailable while idle");
                foreach(uint busy in new[]{0U,1U})foreach(uint pending in new[]{0U,3U}){Set(download,"deviceStatus",Status(false,busy,pending,16));RefreshControls(download);Check(((Button)Field(download,"enterDownload")).Enabled==(busy==0 && pending==0),"Download mode busy/pending guard changed");}
                var ready=Status(false,0,0,16);Set(ready,"Download",true);Set(download,"deviceStatus",ready);RefreshControls(download);Check(!((Button)Field(download,"enterDownload")).Enabled,"Already in download mode still offers transition");
                Cancellation(download);Selection();
                Set(download,"deviceStatus",null);Set(firmware,"deviceStatus",null);RefreshControls(download);RefreshControls(firmware);
                ((Label)Field(download,"progressText")).Text="Idle";
                Snapshot(download,output,"download");Snapshot(firmware,output,"firmware");
                Set(firmware,"deviceStatus",Status(false,0,0,79));RefreshControls(firmware);Snapshot(firmware,output,"firmware-inplace");
            }
            Console.WriteLine("PASS actual Release management forms: "+checks+" separation, exact-UID, busy/capability, cancellation and operation-gate checks; device Shown handlers, USB discovery and hardware operations never invoked.");return 0;
        }catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
