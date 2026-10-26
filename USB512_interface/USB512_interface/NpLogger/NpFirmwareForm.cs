using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace USB512_interface
{
    internal partial class NpFirmwareForm : Form
    {
        protected readonly ComboBox devices=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill};
        readonly ComboBox firmwareTarget=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill,AccessibleName="Bundled firmware"};
        readonly NpFirmwareRelease cameraTarget;
        readonly string cameraTargetError;
        protected readonly Label status=new Label{Text="Refresh to discover V1/V2 loggers (identified by MCU UID).",AutoSize=true,MaximumSize=new Size(810,0)};
        readonly Label progressText=new Label{AutoSize=true,Text="Idle",MaximumSize=new Size(850,0)};
        readonly ProgressBar progress=new ProgressBar{Dock=DockStyle.Fill};
        readonly TextBox log=new TextBox{Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill};
        readonly Button refresh=new Button{Text="Refresh",AutoSize=true};
        readonly Button inspect=new Button{Text="Read status",AutoSize=true};
        readonly Button update=new Button{Text="Update APP file…",AutoSize=true};
        readonly Button abortUpdate=new Button{Text="Abort APP update…",AutoSize=true};
        readonly Button romDfu=new Button{Text="Request ROM DFU…",AutoSize=true};
        readonly Button flash=new Button{Text="Flash firmware…",AutoSize=true};
        readonly Button locateCli=new Button{Text="Locate CubeProgrammer…",AutoSize=true};
        readonly Button bundledApp=new Button{Text="Update bundled firmware…",AutoSize=true};
        readonly Button firmwareFiles=new Button{Text="Bundled firmware files…",AutoSize=true};
        readonly Button exitRecovery=new Button{Text="Exit update / recovery…",AutoSize=true};
        readonly Button[] modeButtons=new Button[1];
        readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer{Interval=100};
        readonly object progressLock=new object();
        NpV1Management.Status deviceStatus;
        protected NpFirmwareIdentity firmwareIdentity;
        string selectedUid,pendingText;
        int pendingPercent;
        bool working;
        ushort updateReturnMode;
        string programmerPath=NpFirmwareFlash.DefaultCli();

        internal NpFirmwareForm(string selectedPath):this(selectedPath,1){}
        protected NpFirmwareForm(string selectedPath,uint preferredFamily)
        {
            selectedFamily=NpV1Management.UidFromPath(selectedPath)!=null?1U:NpV2FirmwareForm.Uid(selectedPath)!=null?2U:preferredFamily;
            selectedUid=NpV1Management.UidFromPath(selectedPath)??NpV2FirmwareForm.Uid(selectedPath);
            Text="Firmware update";
            Icon=NpPreviewTheme.ApplicationIcon;
            ClientSize=new Size(900,560);MinimumSize=new Size(780,510);StartPosition=FormStartPosition.CenterParent;
            try{cameraTarget=ReadCameraTarget();}catch(Exception error){cameraTargetError=error.Message;}
            SetFirmwareTargets();
            bundledPart.Items.AddRange(new object[]{"APP only","Full bundle: APP + USB + boot"});bundledPart.SelectedIndex=0;
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(12),ColumnCount=1,RowCount=7};
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            for(int i=0;i<7;i++)layout.RowStyles.Add(new RowStyle(i==6?SizeType.Percent:SizeType.AutoSize,i==6?100:0));
            var top=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=3};
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.Controls.Add(devices,0,0);top.Controls.Add(refresh,1,0);top.Controls.Add(inspect,2,0);
            layout.Controls.Add(top,0,0);
            var targetRow=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=2};
            targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            targetRow.Controls.Add(WildXTheme.Label("Bundled firmware"),0,0);targetRow.Controls.Add(firmwareTarget,1,0);
            layout.Controls.Add(targetRow,0,1);layout.Controls.Add(status,0,2);
            var actions=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,WrapContents=true};
            string[] names={"Recovery"};ushort[] modes={4};
            for(int i=0;i<modes.Length;i++){
                ushort mode=modes[i];string name=names[i];var button=new Button{Text=name,AutoSize=true};
                modeButtons[i]=button;actions.Controls.Add(button);button.Click+=async delegate{await SwitchMode(mode,name);};
            }
            actions.Controls.Add(bundledPart);actions.Controls.Add(bundledApp);actions.Controls.Add(update);actions.Controls.Add(flash);actions.Controls.Add(firmwareFiles);actions.Controls.Add(abortUpdate);actions.Controls.Add(exitRecovery);actions.Controls.Add(romDfu);actions.Controls.Add(locateCli);layout.Controls.Add(actions,0,3);
            layout.Controls.Add(progressText,0,4);layout.Controls.Add(progress,0,5);layout.Controls.Add(log,0,6);Controls.Add(layout);
            refresh.Click+=delegate{Discover();};inspect.Click+=async delegate{await InspectStatus();};
            devices.SelectedIndexChanged+=delegate{SelectDevice();};
            firmwareTarget.SelectedIndexChanged+=delegate{status.Text=(deviceStatus==null?"Read status before updating or changing mode on this device.":deviceStatus.Description)+FirmwareVersions();UpdateControls();};
            update.Click+=async delegate{await UpdateApp();};abortUpdate.Click+=async delegate{await AbortUpdate();};romDfu.Click+=async delegate{await RequestRomDfu();};
            exitRecovery.Click+=async delegate{if(IsV2)await V2Mode("--stream");else await SwitchMode(2,"USB streaming");};
            flash.Click+=async delegate{await FlashFirmware();};locateCli.Click+=delegate{LocateCli();};
            bundledApp.Click+=async delegate{try{await UpdateBundledFirmware();}catch(Exception ex){MessageBox.Show(this,ex.Message,"Bundled firmware");}};
            bundledPart.SelectedIndexChanged+=delegate{UpdateControls();};
            firmwareFiles.Click+=delegate{try{NpFirmwareNotice.OpenFiles();}catch(Exception ex){MessageBox.Show(this,ex.Message,"Bundled firmware");}};
            FormClosing+=delegate(object sender,FormClosingEventArgs e){if(working){e.Cancel=true;MessageBox.Show(this,"Wait for the firmware operation to finish. Do not disconnect the logger during an update.");}};
            timer.Tick+=delegate{lock(progressLock){if(pendingText!=null){progressText.Text=pendingText;progress.Value=pendingPercent;pendingText=null;}}};timer.Start();
            Resize+=delegate{status.MaximumSize=progressText.MaximumSize=new Size(Math.Max(200,ClientSize.Width-40),0);};
            Shown+=async delegate{Discover();if(devices.SelectedItem!=null)await InspectStatus();};UpdateControls();
            WildXTheme.Apply(this);WildXTheme.Button(bundledApp,true);WildXTheme.Button(abortUpdate,false,true);WildXTheme.Button(romDfu,false,true);
            status.ForeColor=progressText.ForeColor=WildXTheme.Muted;status.Text+=FirmwareVersions();
        }
        void Discover(){DiscoverDevices();}
        NpFirmwareRelease SelectedFirmwareTarget {get{return IsV2?NpFirmwareCatalog.Bundled.For(2):firmwareTarget.SelectedIndex==0?NpFirmwareCatalog.Bundled.For(1):firmwareTarget.SelectedIndex==1?cameraTarget:null;}}
        static NpFirmwareRelease ReadCameraTarget()
        {
            var catalog=NpFirmwareCatalog.Bundled;
            var manifest=new FileInfo(Path.Combine(catalog.Root,"V1Camera","release.json"));
            if(!manifest.Exists || manifest.Length>128*1024)throw new IOException("Camera target metadata unavailable.");
            var release=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<NpFirmwareRelease>(File.ReadAllText(manifest.FullName));
            if(release==null || release.family!=1 || release.files==null || release.files.Length!=2 ||
                String.IsNullOrWhiteSpace(release.appVersion) || String.IsNullOrWhiteSpace(release.usbVersion) ||
                String.IsNullOrWhiteSpace(release.stage) || String.IsNullOrWhiteSpace(release.limits) ||
                !System.Text.RegularExpressions.Regex.IsMatch(release.appCrc32??"","\\A[0-9a-fA-F]{8}\\z") ||
                !System.Text.RegularExpressions.Regex.IsMatch(release.usbCrc32??"","\\A[0-9a-fA-F]{8}\\z"))throw new IOException("Invalid camera target metadata.");
            foreach(var file in release.files){
                if(file==null || String.IsNullOrWhiteSpace(file.path) || Path.GetFileName(file.path)!=file.path)throw new IOException("Invalid camera target filename.");
                file.path="V1Camera/"+file.path;catalog.CheckedPath(file);
            }
            release.app=release.files.Single(file=>Path.GetFileName(file.path).StartsWith("V1_SD_BLE_app_only_",StringComparison.Ordinal));
            release.full=release.files.Single(file=>Path.GetFileName(file.path).StartsWith("V1_fused_",StringComparison.Ordinal));
            release.board="V1 Camera";
            release.recoveryVersion="Included in fused HEX; version not reported";
            return release;
        }
        protected string FirmwareVersions()
        {
            var release=SelectedFirmwareTarget;
            return "\r\nInstalled: "+NpFirmwareCatalog.Bundled.Installed(selectedFamily,firmwareIdentity)+"\r\nBundled: "+(release==null?"Unavailable":release.Versions)+
                (release==null?"\r\n"+(!IsV2&&firmwareTarget.SelectedIndex==1?cameraTargetError:NpFirmwareCatalog.Bundled.Error):"")+
                (firmwareIdentity==null?"\r\nVersion comparison unavailable; installed firmware does not report image versions.":"");
        }
        async Task UseBundledApp(Func<NpFirmwareRelease,string> selectApp,Func<string,Task> applyApp)
        {
            if(working || !NpV1Management.CanUpdateApp(deviceStatus))return;
            var release=SelectedFirmwareTarget;if(release==null)throw new IOException("Selected firmware target is unavailable.");
            string path=selectApp(release);
            if(!Object.ReferenceEquals(release,SelectedFirmwareTarget))throw new IOException("Firmware target changed. Select the bundled APP again; no update was started.");
            if(path!=null)await applyApp(path);
        }
        void UpdateControls()
        {
            firmwareTarget.Enabled=bundledPart.Enabled=devices.Enabled=refresh.Enabled=!working;inspect.Enabled=!working && devices.SelectedItem!=null;
            if(IsV2){UpdateV2Controls();return;}
            bool manage=!working && deviceStatus!=null && deviceStatus.Split && deviceStatus.Busy==0 && deviceStatus.PendingMode==0;
            foreach(var button in modeButtons)button.Enabled=manage;
            exitRecovery.Enabled=manage && deviceStatus.Role==4;
            modeButtons[0].Text=deviceStatus!=null && !deviceStatus.Recovery && (deviceStatus.Capabilities&64U)!=0?"APP update mode":"Recovery";
            update.Enabled=!working && NpV1Management.CanUpdateApp(deviceStatus);
            bundledApp.Enabled=SelectedFirmwareTarget!=null && (bundledPart.SelectedIndex==0?update.Enabled:!working && SelectedFirmwareTarget.full!=null && devices.SelectedItem is NpV1Target && (((NpV1Target)devices.SelectedItem).Path==null || NpV1Management.CanEnterRomDfu(deviceStatus)));firmwareFiles.Enabled=!working;
            abortUpdate.Enabled=!working && NpV1Management.CanAbortAppUpdate(deviceStatus);
            romDfu.Enabled=!working && NpV1Management.CanEnterRomDfu(deviceStatus);
            var target=devices.SelectedItem as NpV1Target;
            flash.Enabled=!working && target!=null && (target.Path==null || NpV1Management.CanUpdateApp(deviceStatus) || NpV1Management.CanEnterRomDfu(deviceStatus));
            locateCli.Enabled=!working;inspect.Enabled=inspect.Enabled && target!=null && target.Path!=null;
        }
        void Report(string text,int percent){lock(progressLock){pendingText=text;pendingPercent=Math.Max(0,Math.Min(100,percent));}}
        void Append(string text){log.AppendText(DateTime.Now.ToString("HH:mm:ss")+" "+text+Environment.NewLine);}
        async Task Run(Func<Task> operation)
        {
            if(working)return;working=true;UpdateControls();
            try{await operation();}
            catch(Exception ex){deviceStatus=null;Report("Stopped: "+ex.Message,0);Append(ex.Message);MessageBox.Show(this,ex.Message+"\n\nNo automatic retry was performed. Refresh and read the device status before proceeding.","NP firmware",MessageBoxButtons.OK,MessageBoxIcon.Error);}
            finally{working=false;UpdateControls();}
        }
        async Task InspectStatus()
        {
            if(IsV2){await ReadV2Status();return;}
            var target=devices.SelectedItem as NpV1Target;if(target==null || target.Path==null)return;deviceStatus=null;
            await Run(async ()=>{var result=await Task.Run(()=>{using(var manager=new NpV1Management(target))return manager.ReadStatus();});deviceStatus=result;
                firmwareIdentity=null;
                if(!result.Recovery && result.Busy==0 && result.PendingMode==0 && (result.Capabilities&NpFirmwareIdentity.UsbCapability)!=0)
                    try{firmwareIdentity=await Task.Run(()=>NpFirmwareIdentity.ReadUsb(target.Path,1));}catch(Exception ex){Append("Firmware identity unavailable: "+ex.Message);}
                SuggestFullBundle();status.Text=result.Description+FirmwareVersions();Append(target+": "+result.Description);});
        }
        async Task SwitchMode(ushort mode,string name)
        {
            if(IsV2){await V2Mode(mode==4?"--recovery":NpV2FirmwareForm.UsbModeOperation(mode));return;}
            var target=devices.SelectedItem as NpV1Target;if(target==null)return;
            bool sameInterface=NpV1Management.UsesInPlaceTransition(deviceStatus,mode);
            string warning=mode==1?"The SD app disconnects USB. Automatic return to USB is not implemented; SWD may be required. Enter the SD application?":sameInterface?"Switch this V1 to APP update mode on the same USB interface?":"Switch this V1 to "+name+"? The USB mode will be verified for this UID; changing personality may re-enumerate USB.";
            if(MessageBox.Show(this,warning,"V1 "+target.Uid,MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;
            await Run(()=>Task.Run(()=>Report(NpV1Management.SwitchMode(target,mode),100)));
            Discover();
        }
        async Task UpdateApp(string bundledPath=null)
        {
            if(IsV2){await UpdateV2App(bundledPath);return;}
            var target=devices.SelectedItem as NpV1Target;if(target==null || !NpV1Management.CanUpdateApp(deviceStatus))return;
            string path=bundledPath;if(path==null)using(var dialog=new OpenFileDialog{Filter="V1 SD/BLE APP-only HEX (*.hex)|*.hex|Legacy V1 app package (*.xenp)|*.xenp",Title="Select APP-only HEX — not factory or APP+USB"}){if(dialog.ShowDialog(this)!=DialogResult.OK)return;path=dialog.FileName;}
            try{NpV1Management.ValidatePackage(path);}catch(Exception error){MessageBox.Show(this,error.Message,"Package rejected");return;}
            bool sameInterface=!deviceStatus.Recovery && (deviceStatus.Capabilities&64U)!=0;
            ushort originalMode=(ushort)(sameInterface && deviceStatus.Role!=4?deviceStatus.Role:0);
            string after=sameInterface?(originalMode==0?"APP update mode remains active on this USB interface.":"The logger returns to its current USB mode on the same interface."):"Recovery remains active afterward.";
            if(MessageBox.Show(this,"Replace only the SD app on V1 "+target.Uid+"?\nLoader / USB service partitions and SD recordings are not modified. Do not unplug power during update. "+after,"Confirm V1 app update",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;
            updateReturnMode=originalMode;
            await Run(()=>Task.Run(()=>{
                ushort role;using(var manager=new NpV1Management(target))role=manager.Update(path,p=>Report("Writing V1 SD application: "+p+"%",p));
                updateReturnMode=0;
                Report("Application committed and valid. "+(role==2?"Returned to USB streaming on the same interface.":role==3?"Returned to SD download on the same interface.":target.Recovery?"Recovery remains active.":"APP update mode remains active on the same interface."),100);
            }));
            deviceStatus=null;UpdateControls();
        }
        async Task AbortUpdate()
        {
            var target=devices.SelectedItem as NpV1Target;if(target==null || !NpV1Management.CanAbortAppUpdate(deviceStatus))return;
            if(MessageBox.Show(this,"Abort the APP update on V1 "+target.Uid+"? An uncommitted APP remains invalid. Protected firmware and SD recordings are not changed.","Abort V1 APP update",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;
            await Run(()=>Task.Run(()=>{
                ushort role;using(var manager=new NpV1Management(target))role=manager.AbortAppUpdate(updateReturnMode);
                updateReturnMode=0;Report("APP update aborted. "+(role==4?"APP update mode is idle; select USB streaming or SD download to return.":"Original USB mode verified on the same interface."),0);
            }));
            deviceStatus=null;UpdateControls();
        }
        async Task RequestRomDfu()
        {
            if(IsV2){await V2Mode("--dfu");return;}
            var target=devices.SelectedItem as NpV1Target;if(target==null || !NpV1Management.CanEnterRomDfu(deviceStatus))return;
            if(MessageBox.Show(this,"Request guarded ROM DFU on V1 "+target.Uid+"?\n\nUSB will disconnect. No firmware is programmed by this command. Verify the DFU device identity in STM32CubeProgrammer before uploading.\n\nThe fused image rewrites the recovery bootloader and is NOT implant-update-qualified. Never use mass erase or change option bytes.","Request ROM DFU",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;
            await Run(()=>Task.Run(()=>{
                using(var manager=new NpV1Management(target))manager.SetMode(5);
                Report("ROM DFU request acknowledged for "+target.Uid+". DFU enumeration is not yet verified; confirm it in CubeProgrammer. No firmware was flashed.",0);
            }));
            deviceStatus=null;UpdateControls();
        }
        bool LocateCli()
        {
            using(var dialog=new OpenFileDialog{Filter="STM32CubeProgrammer CLI (STM32_Programmer_CLI.exe)|STM32_Programmer_CLI.exe",Title="Locate the CLI from an installed STM32CubeProgrammer distribution"}){
                if(dialog.ShowDialog(this)!=DialogResult.OK)return false;
                if(!Path.GetFileName(dialog.FileName).Equals("STM32_Programmer_CLI.exe",StringComparison.OrdinalIgnoreCase)){MessageBox.Show(this,"Select STM32_Programmer_CLI.exe from STM32CubeProgrammer's bin folder.");return false;}
                programmerPath=dialog.FileName;Append("STM32CubeProgrammer location: "+programmerPath);return true;
            }
        }
        async Task FlashFirmware(string bundledPath=null,string expectedSha=null)
        {
            var target=devices.SelectedItem as NpV1Target;if(target==null || working)return;
            string path=bundledPath;if(path==null)using(var dialog=new OpenFileDialog{Filter="V1 firmware HEX (*.hex)|*.hex",Title="Select complete fused V1 firmware or APP-only HEX"}){if(dialog.ShowDialog(this)!=DialogResult.OK)return;path=dialog.FileName;}
            await Run(async ()=>{
                Report("Validating firmware addresses, manifests, vectors and CRCs",0);
                var image=await Task.Run(()=>NpFirmwareImage.Load(path));
                if(expectedSha!=null&&!image.SourceSha256.Equals(expectedSha,StringComparison.OrdinalIgnoreCase))throw new IOException("Bundled firmware changed before loading; no update was started.");
                bool resident=!image.Full && target.Path!=null && NpV1Management.CanUpdateApp(deviceStatus);
                if(!resident && target.Path!=null && !NpV1Management.CanEnterRomDfu(deviceStatus))
                    throw new IOException("This V1 role does not advertise guarded ROM DFU. Select idle USB streaming or SD download, read status, then try again. No mode request was sent.");
                string route=resident?"Protected resident APP updater":image.Full?"Full fused firmware over ROM DFU":"Bounded APP-only update over ROM DFU";
                if(!resident && (programmerPath==null || !File.Exists(programmerPath))){
                    MessageBox.Show(this,"Install STM32CubeProgrammer, then locate STM32_Programmer_CLI.exe in its bin folder. No DFU request or flash write has been sent.","STM32CubeProgrammer required");
                    if(!LocateCli())return;
                }
                string warning=image.Full?"This rewrites the recovery bootloader and resident USB service. It is NOT implant-field-update-qualified or atomic. Loss of power can leave the logger needing physical recovery.\n\n":
                    "Only the SD/BLE application is replaced. Recovery, resident USB service and configuration are preserved.\n\n";
                warning+=route+"\nSelected full MCU UID: "+target.Uid+"\nImage SHA-256: "+image.SourceSha256+"\n\nSD recordings and the configuration page are preserved. Keep power and USB connected until verification and reconnection finish. Continue?";
                if(MessageBox.Show(this,warning,"Confirm V1 firmware flash",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;
                string result=await Task.Run(()=>{
                    if(resident){
                        using(var manager=new NpV1Management(target)){
                            var state=manager.ReadStatus();if(!NpV1Management.CanUpdateApp(state))throw new IOException("Resident APP update capability or idle state changed. No DFU fallback was attempted.");
                            updateReturnMode=(ushort)(!state.Recovery && state.Role!=4?state.Role:0);
                            ushort role=manager.UpdateBytes(image.AppPackage,p=>Report("Writing resident APP: "+p+"%",p));updateReturnMode=0;
                            return "APP committed and verified on selected UID "+target.Uid+"; "+(role==4?"APP/recovery mode remains active.":"original idle USB mode restored.");
                        }
                    }
                    string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WILDX_console","FirmwareOperations",Guid.NewGuid().ToString("N"));
                    var flow=new NpFirmwareFlash(a=>NpFirmwareFlash.RunProcess(programmerPath,a),Report,folder);
                    return flow.Execute(image,target.Uid,target.Path==null,()=>{using(var manager=new NpV1Management(target))manager.SetMode(5);},()=>WaitForFirmwareUid(target.Uid));
                });
                Append(result);Report(result,100);deviceStatus=null;
            });
            Discover();
        }
        internal static bool WaitForFirmwareUid(string uid)
        {
            var watch=Stopwatch.StartNew();
            while(watch.ElapsedMilliseconds<30000){
                var found=NpV1Management.Enumerate().FindAll(t=>String.Equals(t.Uid,uid,StringComparison.OrdinalIgnoreCase));
                if(found.Count>1)throw new IOException("The reconnecting full MCU UID is ambiguous.");
                if(found.Count==1)try{using(var manager=new NpV1Management(found[0])){var state=manager.ReadStatus();if(state.Split && state.Busy==0 && state.PendingMode==0 && state.Role>=2 && state.Role<=4)return true;}}catch(Win32Exception){}
                Thread.Sleep(250);
            }
            return false;
        }
        protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
    }
}
