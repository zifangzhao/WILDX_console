using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using CE32_console;

namespace USB512_interface
{
    internal partial class NpFirmwareForm
    {
        sealed class V2Target
        {
            internal string Path,Uid;internal bool Boot;
            public override string ToString(){return "NP V2 "+Uid+(Path==null?" — reconnect / DFU":Boot?" — recovery":" — USB service");}
        }
        protected readonly ComboBox bundledPart=new ComboBox{Name="bundledFirmwarePart",DropDownStyle=ComboBoxStyle.DropDownList,Width=245};
        uint selectedFamily;
        bool IsV2 {get{return selectedFamily==2;}}
        internal void SelectFullBundle(){bundledPart.SelectedIndex=1;}
        void SetFirmwareTargets(){firmwareTarget.Items.Clear();firmwareTarget.Items.AddRange(IsV2?new object[]{"V2 Neuropixels"}:new object[]{"V1 Neuropixels","V1 Camera"});firmwareTarget.SelectedIndex=0;}
        string DeviceUid(object target){var v1=target as NpV1Target;var v2=target as V2Target;return v1!=null?v1.Uid:v2==null?null:v2.Uid;}
        void SelectDevice()
        {
            object target=devices.SelectedItem;if(target==null)return;
            uint family=target is V2Target?2U:1U;string uid=DeviceUid(target);
            bool changed=family!=selectedFamily||!String.Equals(uid,selectedUid,StringComparison.OrdinalIgnoreCase);
            selectedFamily=family;selectedUid=uid;deviceStatus=null;firmwareIdentity=null;
            if(changed){updateReturnMode=0;SetFirmwareTargets();bundledPart.SelectedIndex=0;}
            status.Text="Read status before updating this device."+FirmwareVersions();UpdateControls();
        }
        void DiscoverDevices()
        {
            if(working)return;devices.Items.Clear();
            foreach(var target in NpV1Management.Enumerate())devices.Items.Add(target);
            foreach(string path in NpDownloadNative.EnumerateInterfacePaths(NpDownloadNative.NeuropixelsInterfaceGuid)){
                string uid=NpV2FirmwareForm.Uid(path);bool boot=path.IndexOf("vid_1209&pid_b387",StringComparison.OrdinalIgnoreCase)>=0;
                bool service=path.IndexOf("vid_1209&pid_b386",StringComparison.OrdinalIgnoreCase)>=0;
                if(uid!=null&&(boot||service)&&NpV1Management.UidFromPath(path)==null)devices.Items.Add(new V2Target{Path=path,Uid=uid,Boot=boot});
            }
            var matches=devices.Items.Cast<object>().Where(t=>(t is V2Target?2U:1U)==selectedFamily&&String.Equals(DeviceUid(t),selectedUid,StringComparison.OrdinalIgnoreCase)).ToList();
            if(selectedUid!=null&&matches.Count==0){object pending=IsV2?(object)new V2Target{Uid=selectedUid}:new NpV1Target{Uid=selectedUid};devices.Items.Add(pending);matches.Add(pending);}
            if(selectedUid!=null&&matches.Count==1)devices.SelectedItem=matches[0];
            else if(selectedUid==null&&devices.Items.Count==1)devices.SelectedIndex=0;
            deviceStatus=null;firmwareIdentity=null;
            status.Text=(devices.Items.Count==0?"No V1/V2 USB logger found.":devices.SelectedIndex<0?"Select the exact V1/V2 MCU UID; another logger was not substituted.":"Select the exact UID, then read status.")+FirmwareVersions();UpdateControls();
        }
        void SuggestFullBundle(){var release=SelectedFirmwareTarget;if(release!=null&&firmwareIdentity!=null&&firmwareIdentity.Usb!=null&&!release.usbIdentity.Same(firmwareIdentity.Usb))SelectFullBundle();}
        async Task UpdateBundledFirmware()
        {
            var release=SelectedFirmwareTarget;if(working||release==null)return;
            if(bundledPart.SelectedIndex==0){
                if(IsV2){string path=NpFirmwareNotice.SelectAppPath(this,release);if(path!=null)await UpdateV2App(path);}
                else await UseBundledApp(r=>NpFirmwareNotice.SelectAppPath(this,r),path=>UpdateApp(path));
                return;
            }
            if(release.full==null)throw new IOException("No full firmware is bundled for this target.");
            if(IsV2)await UpdateFullV2(release);
            else await FlashFirmware(NpFirmwareCatalog.Bundled.CheckedPath(release.full),release.full.sha256);
        }
        void UpdateV2Controls()
        {
            var target=devices.SelectedItem as V2Target;bool connected=!working&&target!=null&&target.Path!=null&&devices.Items.Cast<object>().OfType<V2Target>().Count(t=>String.Equals(t.Uid,target.Uid,StringComparison.OrdinalIgnoreCase))==1;
            inspect.Enabled=update.Enabled=connected;
            bundledApp.Enabled=connected&&SelectedFirmwareTarget!=null&&(bundledPart.SelectedIndex==0||!target.Boot&&SelectedFirmwareTarget.full!=null);
            modeButtons[0].Text="Recovery";modeButtons[0].Enabled=romDfu.Enabled=connected;exitRecovery.Enabled=connected&&target.Boot;
            flash.Enabled=abortUpdate.Enabled=false;firmwareFiles.Enabled=locateCli.Enabled=!working;
        }
        V2Target SelectedV2(){var target=devices.SelectedItem as V2Target;if(!IsV2||target==null||target.Path==null)throw new IOException("Select the connected V2 MCU UID.");if(devices.Items.Cast<object>().OfType<V2Target>().Count(t=>String.Equals(t.Uid,target.Uid,StringComparison.OrdinalIgnoreCase))!=1)throw new IOException("The selected V2 UID is ambiguous; no command was sent.");return target;}
        async Task V2Helper(V2Target target,string operation)
        {
            status.Text="Working on V2 UID "+target.Uid+"…";
            string output=await NpV2FirmwareForm.RunHelper(target.Uid,operation,null);Append(output.Trim());status.Text=output.Trim();
        }
        async Task ReadV2Status()
        {
            var target=SelectedV2();await Run(async()=>{
                await V2Helper(target,target.Boot?"--boot-info":"--mode-info");firmwareIdentity=null;
                if(!target.Boot)try{firmwareIdentity=await Task.Run(()=>NpFirmwareIdentity.ReadUsb(target.Path,2));}catch(Exception ex){Append("Firmware identity unavailable: "+ex.Message);}
                SuggestFullBundle();status.Text+=FirmwareVersions();
            });
        }
        async Task V2Mode(string operation)
        {
            var target=SelectedV2();if(operation=="--stream"&&!target.Boot)throw new IOException("Select the recovery loader to exit. USB streaming/download controls are in Advanced.");
            if(operation=="--dfu"&&MessageBox.Show(this,"Reset V2 UID "+target.Uid+" into ROM DFU? USB disconnects; no firmware is written by this request.","Enter ROM DFU",MessageBoxButtons.OKCancel,MessageBoxIcon.Warning)!=DialogResult.OK)return;
            await Run(async()=>{await V2Helper(target,operation);status.Text="Mode request acknowledged for V2 UID "+target.Uid+". Refresh after USB re-enumeration.";});
        }
        async Task UpdateV2App(string path)
        {
            var target=SelectedV2();if(path==null)using(var dialog=new OpenFileDialog{Filter="V2 APP package (*.xenp)|*.xenp",Title="Select a V2 application package"}){if(dialog.ShowDialog(this)!=DialogResult.OK)return;path=dialog.FileName;}
            if(MessageBox.Show(this,"Update only the APP on V2 UID "+target.Uid+"?\r\n\r\n"+path+"\r\n\r\nKeep USB and power connected until the loader confirms the CRC commit.","Confirm APP update",MessageBoxButtons.OKCancel,MessageBoxIcon.Warning)!=DialogResult.OK)return;
            await Run(async()=>{await V2Helper(target,path);firmwareIdentity=null;status.Text="APP CRC commit acknowledged for V2 UID "+target.Uid+". Reconnect to confirm firmware operation.";});
        }
        async Task UpdateFullV2(NpFirmwareRelease release)
        {
            var target=SelectedV2();if(target.Boot)throw new IOException("Exit recovery into the V2 USB service before a full update.");
            await Run(async()=>{
                string path=NpFirmwareCatalog.Bundled.CheckedPath(release.full);var image=await Task.Run(()=>NpFirmwareImage.LoadBundledV2(path,release.full.sha256));
                await Task.Run(()=>NpV2FirmwareForm.RequireIdleV2(target.Path,target.Uid));
                if(programmerPath==null||!File.Exists(programmerPath)){if(!LocateCli())return;}
                if(!Path.GetFileName(programmerPath).Equals("STM32_Programmer_CLI.exe",StringComparison.OrdinalIgnoreCase))throw new IOException("Select the installed STM32_Programmer_CLI.exe.");
                if(MessageBox.Show(this,"Install bundled full V2 firmware on UID "+target.Uid+"?\r\n\r\n"+release.Versions+"\r\n\r\nThis replaces APP, USB service and recovery through ROM DFU. Configuration and omitted flash are backed up, preserved and verified. Keep USB and power connected; interruption can require physical recovery.","Confirm full firmware update",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
                string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WILDX_console","FirmwareOperations",Guid.NewGuid().ToString("N"));
                string result=await Task.Run(()=>new NpFirmwareFlash(a=>NpFirmwareFlash.RunProcess(programmerPath,a),Report,directory).Execute(image,target.Uid,false,
                    ()=>NpV2FirmwareForm.RunHelper(target.Uid,"--dfu",null).GetAwaiter().GetResult(),()=>NpV2FirmwareForm.WaitForV2(target.Uid)));
                firmwareIdentity=null;Report(result,100);status.Text=result+FirmwareVersions();Append(result);
            });
        }
    }
}
