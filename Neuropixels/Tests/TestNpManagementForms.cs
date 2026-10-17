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
        Check(((Button)Field(form,"exitRecovery")).Enabled==(idle && role==4),"Cannot exit recovery safely after moving USB modes");
        foreach(var button in (Button[])Field(form,"modeButtons"))Check(button.Enabled==idle,"Firmware mode busy gate changed");
        Check(((Button[])Field(form,"modeButtons"))[0].Text==(inPlace?"APP update mode":"Recovery"),"Update mode label does not reflect capability");
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
    static void AdvancedModes(string path,string output)
    {
        var type=assembly.GetType("USB512_interface.Form1",true);
        using(var form=(Form)Activator.CreateInstance(type)){
            var buttons=(Button[])Field(form,"usbModeButtons");var firmware=(Button)Field(form,"btnDfu");
            Check(buttons.Length==3,"USB operational modes missing from Advanced");
            ushort[] modes={2,3,1};string[] names={"USB streaming","USB download","SD / BLE"};
            for(int i=0;i<buttons.Length;i++){
                Check(buttons[i].Parent==firmware.Parent && buttons[i].Text==names[i] && (ushort)buttons[i].Tag==modes[i],"USB mode is not beside Firmware Update or has wrong mode");
            }
            var page=firmware.Parent;while(page!=null && !(page is TabPage))page=page.Parent;
            Check(page==Field(form,"advancedPage"),"USB modes are outside Advanced");
            var kind=type.GetField("selectedDeviceKind",Instance);Set(form,"devicePath",path);
            Action update=()=>type.GetMethod("UpdateStartEnabled",Instance).Invoke(form,null);
            foreach(string family in new[]{"XeNp","NpV2"}){
                kind.SetValue(form,Enum.Parse(kind.FieldType,family));update();
                foreach(var button in buttons)Check(button.Enabled,"Idle "+family+" cannot select USB mode before reading recording settings");
                foreach(string busy in new[]{"running","ztestRunning","systemInfoActive","deviceRefreshActive","firmwareManagementActive","npRecoveryCancelled"}){
                    var field=type.GetField(busy,Instance);object old=field.GetValue(form);
                    field.SetValue(form,field.FieldType==typeof(int)?(object)1:busy=="npRecoveryCancelled"?(object)false:true);update();
                    foreach(var button in buttons)Check(!button.Enabled,"USB mode enabled during "+busy);
                    var blocked=(Task)type.GetMethod("SwitchUsbMode",Instance).Invoke(form,new object[]{(ushort)2});
                    Check(blocked.IsCompleted && !blocked.IsFaulted,"Blocked USB mode attempted work");
                    field.SetValue(form,old);
                }
            }
            update();
            var download=Field(form,"downloadWorkspace");download.GetType().GetMethod("SetBusy",Instance).Invoke(download,new object[]{true});
            foreach(var button in buttons)Check(!button.Enabled,"USB mode enabled during download");
            download.GetType().GetMethod("SetBusy",Instance).Invoke(download,new object[]{false});
            var ble=(Form)Field(form,"bleWorkspace");var done=new TaskCompletionSource<object>();
            var bleTask=(Task)ble.GetType().GetMethod("Run",Instance).Invoke(ble,new object[]{(Func<Task>)(()=>done.Task)});
            foreach(var button in buttons)Check(!button.Enabled,"USB mode enabled during BLE ownership");Complete(bleTask,done);
            Set(form,"usbModeChanging",true);var closing=new FormClosingEventArgs(CloseReason.UserClosing,false);
            typeof(Form).GetMethod("OnFormClosing",Instance).Invoke(form,new object[]{closing});
            Check(closing.Cancel,"Main window can close during USB mode transition");Set(form,"usbModeChanging",false);
            kind.SetValue(form,Enum.Parse(kind.FieldType,"NpUnavailable"));update();
            foreach(var button in buttons)Check(!button.Enabled,"Unidentified USB device can select mode");
            kind.SetValue(form,Enum.Parse(kind.FieldType,"XeNp"));update();
            var advanced=(TabPage)page;var layout=advanced.Controls[0];
            using(var host=new Form {ClientSize=new Size(1000,520),Font=form.Font,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-20000,-20000)}){
                host.Controls.Add(layout);host.Show();Application.DoEvents();
                foreach(var button in buttons)Check(button.Visible && button.Parent.ClientRectangle.Contains(button.Bounds),"USB mode clipped at minimum window width");
                Check(firmware.Parent.ClientRectangle.Contains(((Button)Field(form,"diagnosticsButton")).Bounds),"Diagnostics clipped by moved USB modes");
                using(var bitmap=new Bitmap(host.Width,host.Height)){host.DrawToBitmap(bitmap,new Rectangle(Point.Empty,host.Size));bitmap.Save(Path.Combine(output,"advanced-usb-modes.png"),ImageFormat.Png);}
                host.Hide();advanced.Controls.Add(layout);
            }
        }
        var v2=assembly.GetType("USB512_interface.NpV2FirmwareForm",true);var operation=v2.GetMethod("UsbModeOperation",Static);
        Check((string)operation.Invoke(null,new object[]{(ushort)1})=="--sd" && (string)operation.Invoke(null,new object[]{(ushort)2})=="--stream" && (string)operation.Invoke(null,new object[]{(ushort)3})=="--download","V2 operational mode mapping changed");
        bool rejected=false;try{operation.Invoke(null,new object[]{(ushort)4});}catch(TargetInvocationException error){rejected=error.InnerException is ArgumentOutOfRangeException;}Check(rejected,"Firmware recovery exposed as operational USB mode");
        rejected=false;try{v2.GetMethod("SwitchUsbMode",Static).Invoke(null,new object[]{path,(ushort)2});}catch(TargetInvocationException error){rejected=error.InnerException is IOException;}Check(rejected,"V1 path passed to V2 mode helper");
        using(var firmware=(Form)Activator.CreateInstance(v2,Instance,null,new object[]{null},null)){
            foreach(var control in Descendants(firmware))if(control is Button)Check(control.Text!="USB streaming" && control.Text!="USB download" && control.Text!="SD / BLE" && control.Text!="Diagnostics & self-test…","Operational control remains in V2 Firmware Update");
            Snapshot(firmware,output,"firmware-v2");
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
                Check(!firmwareButtons.Contains("Download session") && !firmwareButtons.Contains("Decode saved archive…") && !firmwareButtons.Contains("Output folder…") && !firmwareButtons.Contains("Enter USB download mode") && !firmwareButtons.Contains("Cancel") && !firmwareButtons.Contains("USB streaming") && !firmwareButtons.Contains("SD app (USB disconnects)"),"Operational controls leaked into Firmware Update");
                foreach(string name in new[]{"UpdateApp","RequestRomDfu","SwitchMode"})Check(downloadType.GetMethod(name,Instance)==null,"Download retained firmware operation "+name);
                foreach(string name in new[]{"Download","DecodeArchive","EnterDownloadMode"})Check(firmwareType.GetMethod(name,Instance)==null,"Firmware retained download operation "+name);
                foreach(string name in new[]{"sessions","folder","method","catalogPath","cancelled"})Check(firmwareType.GetField(name,Instance)==null,"Firmware retained download state "+name);
                Check(((Button[])Field(firmware,"modeButtons")).Length==1,"Firmware contains operational USB modes");
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
            AdvancedModes(path,output);
            Console.WriteLine("PASS actual Release management forms: "+checks+" separation, exact-UID, busy/capability, cancellation, Advanced mode layout and operation-gate checks; device Shown handlers, USB discovery and hardware operations never invoked.");return 0;
        }catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
