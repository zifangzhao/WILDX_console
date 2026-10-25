using System;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Collections.Generic;
namespace CE32_console
{
    internal sealed class NpDownloadForm : Form {
        private sealed class Source {internal string Path,Label,MediaType;internal bool Usb;internal bool CanDelete{get{return !Usb && NpCardFormat.IsRemovableMedia(MediaType);}}internal bool CanFormat{get{return CanDelete || Usb && USB512_interface.NpUsbCatalogRepair.IsEligiblePath(Path);}}public override string ToString(){return Label;}}
        private readonly ComboBox sources=new ComboBox{Dock=DockStyle.Top,DropDownStyle=ComboBoxStyle.DropDownList};
        private readonly CheckedListBox records=new CheckedListBox{Dock=DockStyle.Fill,CheckOnClick=true};
        private readonly TextBox sessionLabel=new TextBox{Name="downloadSessionLabel",Dock=DockStyle.Fill,MaxLength=80,AccessibleName="Session label"};
        private readonly TextBox destination=new TextBox{Name="downloadDestinationFolder",Dock=DockStyle.Fill,AccessibleName="Download destination folder",AutoCompleteMode=AutoCompleteMode.SuggestAppend,AutoCompleteSource=AutoCompleteSource.FileSystemDirectories,Text=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"WILDX downloads")};
        private readonly Button browseDestination=new Button{Text="Browse…",AutoSize=true,Dock=DockStyle.Fill};
        internal string SessionLabel{get{return sessionLabel.Text;}set{sessionLabel.Text=value??"";}}
        internal event Action<string> SessionLabelChanged;
        private readonly Label status=new Label{Dock=DockStyle.Bottom,Height=55,Text="Stop recording, then list recordings. The selected USB logger enters download mode automatically."};
        private readonly ProgressBar progress=new ProgressBar{Dock=DockStyle.Bottom};
        private readonly Button refresh=new Button{Text="Refresh sources",AutoSize=true},list=new Button{Text="List recordings",AutoSize=true},save=new Button{Text="Download selected…",AutoSize=true},cancel=new Button{Text="Cancel",AutoSize=true},play=new Button{Text="Play exported camera…",AutoSize=true};
        private readonly Button convert=new Button{Text="Convert exported media…",AutoSize=true};
        private readonly Button recover=new Button{Name="recoverNpCatalog",Text="Recover recordings…",AutoSize=true};
        private readonly Button calibrationButton=new Button{Text="Calibration…",AutoSize=true};
        private readonly Button format=new Button{Name="quickFormatNpSd",Text="Format card…",AutoSize=true};
        private readonly Button deleteLast=new Button{Name="deleteLastNpRecording",Text="Delete last recording…",AutoSize=true};
        private readonly Button repairCatalog=new Button{Name="repairUsbCatalog",Text="Repair SD catalog…",AutoSize=true};
        private readonly Label cardHelp=new Label{AutoSize=true,Text="Delete and format require an SD card reader.",Margin=new Padding(8,9,0,0)};

        private readonly CheckBox convertAfterDownload=new CheckBox{Text="MP4 + WAV",Checked=true,AutoSize=true};
        private readonly CheckBox mergeDat=new CheckBox{Name="mergeSdProbeDat",Text="Merge probes into one DAT",AutoSize=true};
        private bool busy,refreshing,usbBlocked,maintenanceActive,listedRecoverySearch;private volatile bool cancelled;private Source listedSource;
        private readonly string requestedSourcePath;
        private string boundSourcePath;
        private readonly Timer enumerationTimer=new Timer{Interval=1000};
        private string parentUsbPath;
        internal bool IsBusy{get{return busy;}}
        internal string SelectedSourceDescription{get{var source=sources.SelectedItem as Source;return source==null?null:source.Label;}}
        internal static string UsbSourceLabel(string path) {
            string uid=USB512_interface.NpV1Management.UidFromPath(path);
            if(uid!=null)return "Neuropixels V1 USB — "+uid;
            uid=USB512_interface.NpV2FirmwareForm.Uid(path);
            return uid!=null?"Neuropixels V2 USB — "+uid:"Neuropixels USB — version unknown — "+path;
        }
        IWin32Window DialogOwner{get{return TopLevelControl??this;}}
        internal event Action BusyChanged;
        internal NpDownloadForm(string sourcePath=null):this(sourcePath,true){}
        internal NpDownloadForm(string sourcePath,bool autoDiscover) {
            sourcePath=sourcePath==null?null:sourcePath.Trim();
            requestedSourcePath=sourcePath!=null && sourcePath.StartsWith("PhysicalDrive",StringComparison.OrdinalIgnoreCase)?@"\\.\"+sourcePath:sourcePath;
            boundSourcePath=requestedSourcePath;
            Text="WILD-X NP1 / NP2 / media — SD Download";Width=950;Height=500;
            var actions=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true};actions.Controls.AddRange(new Control[]{refresh,list,recover,save,cancel,play,convert,convertAfterDownload});
            actions.Controls.Add(mergeDat);actions.Controls.Add(calibrationButton);
            calibrationButton.Click+=(s,e)=>{if(!busy)USB512_interface.NpCalibrationStore.Show(DialogOwner,"offline");};

            var cardActions=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true};cardActions.Controls.AddRange(new Control[]{deleteLast,format,repairCatalog,cardHelp});

            Controls.Add(records);Controls.Add(cardActions);Controls.Add(actions);Controls.Add(sources);Controls.Add(progress);Controls.Add(status);
            var nameRow=new TableLayoutPanel {Dock=DockStyle.Top,Height=76,ColumnCount=3,RowCount=2,Padding=new Padding(0,4,0,4)};
            nameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,115));nameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));nameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,100));
            nameRow.RowStyles.Add(new RowStyle(SizeType.Percent,50));nameRow.RowStyles.Add(new RowStyle(SizeType.Percent,50));
            nameRow.Controls.Add(new Label {Text="Session label",Dock=DockStyle.Fill,TextAlign=System.Drawing.ContentAlignment.MiddleLeft},0,0);nameRow.Controls.Add(sessionLabel,1,0);nameRow.SetColumnSpan(sessionLabel,2);
            nameRow.Controls.Add(new Label {Text="Destination",Dock=DockStyle.Fill,TextAlign=System.Drawing.ContentAlignment.MiddleLeft},0,1);nameRow.Controls.Add(destination,1,1);nameRow.Controls.Add(browseDestination,2,1);Controls.Add(nameRow);
            browseDestination.Click+=(s,e)=>{using(var dialog=new FolderBrowserDialog{SelectedPath=Directory.Exists(destination.Text)?destination.Text:""})if(dialog.ShowDialog(DialogOwner)==DialogResult.OK)destination.Text=dialog.SelectedPath;};
            Controls.SetChildIndex(nameRow,3);
            sessionLabel.TextChanged+=(s,e)=>{var changed=SessionLabelChanged;if(changed!=null)changed(sessionLabel.Text);};
            refresh.Click+=(s,e)=>{RefreshSources();enumerationTimer.Start();};list.Click+=async(s,e)=>await ListAsync();save.Click+=async(s,e)=>await SaveAsync();cancel.Click+=(s,e)=>cancelled=true;
            recover.Click+=async(s,e)=>await ListCoreAsync(true);
            records.ItemCheck+=(s,e)=>{var r=records.Items[e.Index] as NpDownloadRecord;if(r!=null&&!r.Downloadable){e.NewValue=CheckState.Unchecked;status.Text="Session "+r.Session+" is not downloadable: "+r.ValidationError;}};
            play.Click+=(s,e)=>new NpMediaPlaybackForm().Show(DialogOwner);
            convert.Click+=async(s,e)=>await ConvertExistingAsync();
            format.Click+=async(s,e)=>await FormatSelectedAsync();
            deleteLast.Click+=async(s,e)=>await DeleteLastAsync();
            repairCatalog.Click+=async(s,e)=>await RepairSelectedCatalogAsync();
            sources.SelectedIndexChanged+=(s,e)=>{var source=sources.SelectedItem as Source;if(source!=null&&!refreshing)boundSourcePath=source.Path;if(listedSource!=null && source!=listedSource)ClearListing();UpdateControls();};
            FormClosing+=(s,e)=>{if(busy){cancelled=true;e.Cancel=true;status.Text="Wait for the current operation to finish before closing. A catalog reset cannot be interrupted.";}};
            Shown+=(s,e)=>{if(autoDiscover){RefreshSources();enumerationTimer.Start();}};
            enumerationTimer.Tick+=(s,e)=>{if(!busy && Visible)try{PollUsb();}catch(Exception ex){status.Text="USB discovery: "+ex.Message;}};
            FormClosed+=(s,e)=>{enumerationTimer.Stop();enumerationTimer.Dispose();};
            SetBusy(false);
            USB512_interface.WildXTheme.Apply(this);Padding=new Padding(0);
            records.IntegralHeight=false;records.Font=USB512_interface.WildXTheme.Body;
            sources.Margin=new Padding(0,0,0,12);status.Padding=new Padding(0,10,0,0);status.Height=68;
            status.TextChanged+=(s,e)=>FitStatus();status.SizeChanged+=(s,e)=>FitStatus();
            status.ForeColor=USB512_interface.WildXTheme.Muted;
            Controls.Add(new Label {Text="STORAGE SOURCE  /  USB device or card reader",Dock=DockStyle.Top,Height=26,ForeColor=USB512_interface.WildXTheme.Muted});
            USB512_interface.WildXTheme.Button(save,true);USB512_interface.WildXTheme.Button(format,false,true);USB512_interface.WildXTheme.Button(deleteLast,false,true);cardHelp.ForeColor=USB512_interface.WildXTheme.Muted;
            if(!autoDiscover)status.Text="Refresh sources to choose a USB device or card reader. List its recordings before downloading.";
        }
        internal void SetUsbSelection(string path){if(!busy && !string.Equals(path,parentUsbPath,StringComparison.OrdinalIgnoreCase)){parentUsbPath=path;if(!string.IsNullOrEmpty(path))boundSourcePath=path;}}
        void FitStatus(){int width=Math.Max(160,status.ClientSize.Width-status.Padding.Horizontal);int height=Math.Max(68,TextRenderer.MeasureText(status.Text,status.Font,new System.Drawing.Size(width,Int32.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.TextBoxControl).Height+status.Padding.Vertical+4);if(status.Height!=height)status.Height=height;}
        internal void SetUsbBlocked(bool value){usbBlocked=value;UpdateControls();}
        internal void RequestCancel(){if(busy){cancelled=true;status.Text="Waiting for the operation to finish. A catalog reset cannot be interrupted.";}}
        protected override void Dispose(bool disposing){if(disposing){enumerationTimer.Stop();enumerationTimer.Dispose();}base.Dispose(disposing);}
        private void PollUsb() { PollUsbPaths(NpDownloadNative.EnumerateNpPaths()); }
        internal void PollUsbPaths(IEnumerable<string> discovered) {
            refreshing=true;
            try {
            var paths=discovered.Where(p=>p.IndexOf("vid_1209&pid_b386",StringComparison.OrdinalIgnoreCase)>=0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var retained=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for(int i=sources.Items.Count-1;i>=0;i--){var item=(Source)sources.Items[i];if(!item.Usb)continue;
                var matches=paths.Where(p=>SameSource(item.Path,p)).ToList();
                if(matches.Count!=1 || !retained.Add(matches[0]))sources.Items.RemoveAt(i);
                else {item.Path=matches[0];item.Label=UsbSourceLabel(item.Path);}
            }
            foreach(string path in paths)if(!sources.Items.Cast<Source>().Any(p=>p.Usb&&string.Equals(p.Path,path,StringComparison.OrdinalIgnoreCase)))sources.Items.Insert(0,new Source{Usb=true,Path=path,Label=UsbSourceLabel(path)});
            SelectBoundSource();
            }finally{refreshing=false;UpdateControls();}
        }
        private void ClearListing(){records.Items.Clear();listedSource=null;listedRecoverySearch=false;progress.Value=0;}
        private void UpdateControls(){var source=sources.SelectedItem as Source;sessionLabel.Enabled=calibrationButton.Enabled=refresh.Enabled=sources.Enabled=records.Enabled=play.Enabled=convert.Enabled=convertAfterDownload.Enabled=mergeDat.Enabled=!busy;list.Enabled=save.Enabled=!busy && source!=null && !(usbBlocked && source.Usb);recover.Enabled=!busy && source!=null && HeaderRecoveryEligible(source.Usb,source.Path) && !(usbBlocked && source.Usb);deleteLast.Enabled=!busy && source!=null && source.CanDelete;format.Enabled=!busy && source!=null && source.CanFormat && !(usbBlocked && source.Usb);repairCatalog.Enabled=!busy && !usbBlocked && source!=null && source.Usb && USB512_interface.NpUsbCatalogRepair.IsEligiblePath(source.Path);cardHelp.Visible=true;cardHelp.Text=source!=null && source.Usb?"Downloads are read-only. Catalog repair requires supported USB firmware and confirmation; Format is a separate erase action.":"Recover is read-only; delete/format require removable SD media.";cancel.Enabled=busy && !maintenanceActive;}
        private void SetBusy(bool value){busy=value;destination.Enabled=browseDestination.Enabled=!busy;UpdateControls();var changed=BusyChanged;if(changed!=null)changed();}
        internal async Task RepairUsbCatalogAsync(string path) {
            if(busy || usbBlocked || !USB512_interface.NpUsbCatalogRepair.IsEligiblePath(path))return;
            var source=sources.Items.Cast<Source>().FirstOrDefault(s=>s.Usb && SameSource(s.Path,path));
            if(source==null){source=new Source{Usb=true,Path=path,Label=UsbSourceLabel(path)};sources.Items.Insert(0,source);}
            sources.SelectedItem=source;
            await RepairSelectedCatalogAsync();
        }
        private async Task RepairSelectedCatalogAsync() {
            var source=sources.SelectedItem as Source;
            if(busy || usbBlocked || source==null || !source.Usb || !USB512_interface.NpUsbCatalogRepair.IsEligiblePath(source.Path))return;
            maintenanceActive=true;cancelled=false;SetBusy(true);
            USB512_interface.NpUsbCatalogRepair.Receipt completed=null;
            try {
                await PrepareSourceAsync(source);
                using(var manager=USB512_interface.NpUsbCatalogRepair.Open(source.Path,()=>!usbBlocked && !IsDisposed,()=>cancelled)){
                    completed=await manager.ConfirmAndRepairAsync(card=>MessageBox.Show(DialogOwner,
                        "Repair the SD catalog on V"+manager.Generation+" "+manager.Uid+"?\r\n\r\n"+card.Description+
                        "\r\n\r\nThis repairs catalog metadata without formatting the card. Recorded payload is not validated; interrupted recordings may remain incomplete. Keep USB and power connected until finished.",
                        "Repair SD catalog",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)==DialogResult.Yes,
                        message=>status.Text=message,()=>{records.Items.Clear();listedSource=null;listedRecoverySearch=false;progress.Value=0;});
                }
                if(completed==null)status.Text="Repair cancelled; card unchanged.";
            }catch(Exception ex){status.Text="USB catalog repair not confirmed: "+ex.Message;MessageBox.Show(DialogOwner,status.Text,"SD catalog",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
            finally{maintenanceActive=false;SetBusy(false);}
            if(completed!=null){
                await ListAsync();
                status.Text="V"+completed.Generation+" catalog repair confirmed (metadata only), flags 0x"+completed.Flags.ToString("X8")+(completed.IsFull?" (full)":"")+". "+status.Text;
            }
        }
        private async Task FormatSelectedAsync() {
            var source=sources.SelectedItem as Source;if(source==null || !source.CanFormat || source.Usb && usbBlocked){status.Text="Quick Format requires removable SD media or an idle, supported V1/V2 USB logger.";return;}
            maintenanceActive=true;SetBusy(true);
            try {
                if(source.Usb){
                    cancelled=false;await PrepareSourceAsync(source);
                    if(USB512_interface.NpV1Management.UidFromPath(source.Path)==null){
                        using(var manager=new USB512_interface.NpV2UsbFormat(source.Path,()=>!usbBlocked && !IsDisposed)){
                            var card=await Task.Run(()=>manager.Inspect());
                            string description="V2 "+card.Uid+"\r\nSD CID "+card.Cid+"\r\n"+(card.Capacity*512.0/1073741824).ToString("N2")+" GiB ("+card.Capacity+" sectors)";
                            if(!ConfirmUsbFormat("V2",description)){status.Text="Format cancelled; nothing written.";return;}
                            ClearListing();status.Text="Formatting the confirmed V2 SD card and verifying metadata…";
                            await Task.Run(()=>manager.Format(card));
                        }
                        progress.Value=100;status.Text="V2 USB Quick Format verified: empty NP catalog. Settings retained; old payload is not securely erased.";return;
                    }
                    var selected=new USB512_interface.NpV1Target{Path=source.Path,Uid=USB512_interface.NpV1Management.UidFromPath(source.Path)};
                    using(var manager=new USB512_interface.NpV1Management(selected)){
                        var card=await Task.Run(()=>manager.InspectSdFormat());
                        if(!ConfirmUsbFormat("V1",card.Description)){status.Text="Format cancelled; nothing written.";return;}
                        ClearListing();status.Text="Formatting the confirmed V1 SD card and verifying metadata…";
                        await Task.Run(()=>manager.FormatSd(card));
                    }
                    records.Items.Clear();listedSource=null;status.Text="V1 USB Quick Format verified: empty NP catalog. Settings retained; old payload is not securely erased.";return;
                }
                var target=await Task.Run(()=>NpCardFormat.Inspect(source.Path));
                if(MessageBox.Show(DialogOwner,"Quick Format this connected NP/media SD card:\r\n\r\n"+target.Description+"\r\n\r\nAll recordings will be removed from the catalog and can be overwritten. This is not a secure erase. Settings are retained.\r\nDo not swap cards until finished; the reader may not expose the card's own serial.\r\n\r\nFormat this selected SD card?","Destructive NP/media Quick Format",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes){status.Text="Format cancelled; nothing written.";return;}
                status.Text="Resetting and verifying native NP/media catalog…";
                await Task.Run(()=>NpCardFormat.Format(target));records.Items.Clear();listedSource=null;
                status.Text="Quick Format verified: empty NP/media catalog. Settings retained; old payload data is not securely erased.";
            }catch(NotSupportedException ex) when(source.Usb && USB512_interface.NpV1Management.UidFromPath(source.Path)==null){
                status.Text=ex.Message;
                if(MessageBox.Show(DialogOwner,ex.Message+"\r\n\r\nOpen the bundled full firmware update? After updating, press Format again.","V2 USB firmware update required",MessageBoxButtons.YesNo,MessageBoxIcon.Information,MessageBoxDefaultButton.Button2)==DialogResult.Yes)
                    using(var updater=new USB512_interface.NpV2FirmwareForm(source.Path)){updater.SelectFullBundle();updater.ShowDialog(DialogOwner);}
            }catch(Exception ex){status.Text="Format not confirmed: "+ex.Message;MessageBox.Show(DialogOwner,status.Text,"NP/media SD",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
            finally{maintenanceActive=false;SetBusy(false);}
        }
        private bool ConfirmUsbFormat(string generation,string description){return MessageBox.Show(DialogOwner,"Erase the recording catalog on this "+generation+" SD card?\r\n\r\n"+description+"\r\n\r\nAll recordings become unavailable and may be overwritten, including interrupted data still recoverable now. Recover needed data FIRST. A broken/foreign catalog is allowed. Settings are retained; this is not a secure erase.\r\nDo not remove power or swap the card until finished.","Destructive "+generation+" USB SD Quick Format",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)==DialogResult.Yes;}
        private async Task DeleteLastAsync() {
            var source=sources.SelectedItem as Source;if(source==null || !source.CanDelete)return;
            maintenanceActive=true;SetBusy(true);
            try {
                var target=await Task.Run(()=>NpCardFormat.Inspect(source.Path));
                var latest=await Task.Run(()=>{using(var stream=NpDownloadStream.OpenDisk(target.Path))return NpCardFormat.Latest(stream);});
                if(MessageBox.Show(DialogOwner,"Delete the last recording on this card:\r\n\r\n"+target.Description+"\r\n\r\n"+latest.Record+"\r\nLBA "+latest.Record.Start+"–"+latest.Record.End+"\r\n\r\nEarlier recordings and settings will be kept. The deleted space can be overwritten. This is not a secure erase.\r\nDo not swap cards until finished.\r\n\r\nDelete this recording?","Delete last recording",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes){status.Text="Delete cancelled; nothing written.";return;}
                status.Text="Deleting and verifying the last recording…";
                await Task.Run(()=>NpCardFormat.DeleteLast(target,latest));records.Items.Clear();listedSource=null;
                status.Text="Last recording deleted. Earlier recordings and settings retained.";
            }catch(Exception ex){status.Text="Delete not confirmed: "+ex.Message;MessageBox.Show(DialogOwner,status.Text,"NP/media SD",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
            finally{maintenanceActive=false;SetBusy(false);}
        }
        private async Task ConvertExistingAsync() {
            string folder;using(var dialog=new OpenFileDialog{Title="Select exported recording metadata",Filter="Recording metadata (recording.json)|recording.json",InitialDirectory=Directory.Exists(destination.Text)?destination.Text:""}){if(dialog.ShowDialog(DialogOwner)!=DialogResult.OK)return;folder=Path.GetDirectoryName(dialog.FileName);}
            SetBusy(true);cancelled=false;progress.Value=0;
            var estimate=new USB512_interface.NpProgressEstimate();
            var ui=new Progress<Tuple<int,string>>(p=>{progress.Value=Math.Max(0,Math.Min(99,p.Item1));status.Text=p.Item2;});
            try{string warning=await Task.Run(()=>NpMediaExport.ConvertFiles(folder,()=>cancelled,(p,message)=>((IProgress<Tuple<int,string>>)ui).Report(Tuple.Create(p,message))));progress.Value=100;status.Text="Converted media · "+estimate.Elapsed+". Native files and timestamp CSVs retained; video and audio durations are independent. "+warning;}
            catch(OperationCanceledException){status.Text="Conversion cancelled. Native export and any completed outputs are retained.";}
            catch(Exception ex){status.Text="Conversion failed; native export retained: "+ex.Message;}
            finally{SetBusy(false);}
        }
        private void RefreshSources() {
            refreshing=true;
            try {sources.Items.Clear();records.Items.Clear();listedSource=null;
                foreach(string path in NpDownloadNative.EnumerateNpPaths())
                    if(path.IndexOf("vid_1209&pid_b386",StringComparison.OrdinalIgnoreCase)>=0)sources.Items.Add(new Source{Usb=true,Path=path,Label=UsbSourceLabel(path)});
                using(var search=new ManagementObjectSearcher("SELECT DeviceID,Model,MediaType,InterfaceType FROM Win32_DiskDrive"))using(var disks=search.Get())foreach(ManagementObject d in disks) {
                    string mediaType=Convert.ToString(d["MediaType"]);
                    if(NpDownloadCatalog.IsReadOnlyDiskSource(mediaType,Convert.ToString(d["InterfaceType"])))
                        sources.Items.Add(new Source{Path=(string)d["DeviceID"],MediaType=mediaType,Label=(NpCardFormat.IsRemovableMedia(mediaType)?"SD/card reader":"USB disk/card reader (read-only)")+" — "+d["Model"]+" — "+d["DeviceID"]});
                }
                SelectBoundSource();
            }catch(Exception ex){status.Text="SD reader discovery unavailable: "+ex.Message+". USB discovery remains active.";}
            finally{if(sources.SelectedIndex<0)SelectBoundSource();refreshing=false;}
        }
        internal static bool SameSource(string first,string second) {
            if(string.IsNullOrEmpty(first)||string.IsNullOrEmpty(second))return false;
            if(string.Equals(first,second,StringComparison.OrdinalIgnoreCase))return true;
            if(USB512_interface.NpUsbCatalogRepair.IsEligiblePath(first) && USB512_interface.NpUsbCatalogRepair.IsEligiblePath(second)){
                string v1a=USB512_interface.NpV1Management.UidFromPath(first),v1b=USB512_interface.NpV1Management.UidFromPath(second);
                return (v1a==null)==(v1b==null) && string.Equals(v1a??USB512_interface.NpV2FirmwareForm.Uid(first),v1b??USB512_interface.NpV2FirmwareForm.Uid(second),StringComparison.OrdinalIgnoreCase);
            }
            int a=first.LastIndexOf('#'),b=second.LastIndexOf('#');
            return a>0&&b>0&&string.Equals(first.Substring(0,a),second.Substring(0,b),StringComparison.OrdinalIgnoreCase);
        }
        private void SelectBoundSource() {
            if(string.IsNullOrEmpty(boundSourcePath)){if(sources.Items.Count==1){sources.SelectedIndex=0;boundSourcePath=((Source)sources.SelectedItem).Path;}return;}
            int selected=-1;for(int i=0;i<sources.Items.Count;i++)if(SameSource(boundSourcePath,((Source)sources.Items[i]).Path)) {
                if(selected>=0){sources.SelectedIndex=-1;return;}selected=i;
            }
            sources.SelectedIndex=selected;
            if(selected>=0){boundSourcePath=((Source)sources.SelectedItem).Path;if(status.Text.StartsWith("Selected device is absent",StringComparison.Ordinal))status.Text="Selected "+((Source)sources.SelectedItem).Label+". List recordings to refresh the card.";}
            if(selected<0)status.Text="Selected device is absent; choose a source explicitly. Another board was not substituted.";
        }
        internal static long RequiredBytes(NpDownloadRecord record) {
            return record.V2?checked(((long)record.Chunks*61440+1599)/1600*2*384*2):checked((long)record.Chunks*65536*(record.Media?4L:2L));
        }
        private Stream Open(Source source) {
            if(source.Usb){if(usbBlocked)throw new IOException("Disconnect BLE and finish the USB operation before accessing this USB logger.");return new NpDownloadStream(source.Path){Cancelled=()=>cancelled};}
            return NpDownloadStream.OpenDisk(source.Path);
        }
        private async Task PrepareSourceAsync(Source source){
            if(!source.Usb)return;
            if(usbBlocked)throw new IOException("Disconnect BLE and finish the USB operation before accessing this USB logger.");
            status.Text="Preparing USB download mode for "+source.Label+"…";
            string path=source.Path;
            if(USB512_interface.NpV1Management.UidFromPath(path)!=null)
                path=await Task.Run(()=>USB512_interface.NpV1Management.EnsureDownload(path,()=>cancelled));
            else if(USB512_interface.NpV2FirmwareForm.Uid(path)!=null)
                path=await USB512_interface.NpV2FirmwareForm.EnsureDownload(path,()=>cancelled);
            source.Path=boundSourcePath=path;source.Label=UsbSourceLabel(path);
            if(cancelled)throw new OperationCanceledException();
        }
        private Task ListAsync(){return ListCoreAsync(false);}
        private async Task ListCoreAsync(bool forceHeaders) {
            var source=sources.SelectedItem as Source;if(source==null)return;
            if(forceHeaders && !HeaderRecoveryEligible(source.Usb,source.Path)){status.Text="This recovery path supports V1 neural/camera recordings through V1 USB or an SD reader.";return;}
            SetBusy(true);cancelled=false;records.Items.Clear();listedSource=null;listedRecoverySearch=false;
            progress.Value=0;
            var estimate=new USB512_interface.NpProgressEstimate();
            var ui=new Progress<int>(p=>{progress.Value=Math.Max(0,Math.Min(99,p));status.Text="Recovering recording list: "+p+"% · "+estimate.Percent(p);});
            try {await PrepareSourceAsync(source);status.Text="Reading NP recording directory (read-only)…";
                string transport="SD reader";
                var found=await Task.Run(()=>{using(var stream=Open(source)){if(stream is NpDownloadStream)transport=((NpDownloadStream)stream).TransportDescription;return ReadCatalog(stream,source,p=>((IProgress<int>)ui).Report(p),forceHeaders);}});
                foreach(var r in found)records.Items.Add(r,false);listedSource=source;listedRecoverySearch=forceHeaders;
                progress.Value=100;
                status.Text=transport+". "+found.Count(r=>r.Downloadable)+" downloadable, "+found.Count(r=>!r.Downloadable)+" warning/unavailable entries. "+(found.Any(r=>r.RecoveryNotice!=null&&!r.V2)?"Recording list recovered read-only; card unchanged. ":"")+"Payload is checked during export; unfinished V1 neural/camera footers offer explicit prefix recovery. No free space is inferred.";
            }catch(Exception ex){status.Text=ex.Message;}finally{SetBusy(false);}
        }
        internal static bool HeaderRecoveryEligible(bool usb,string path)
        {return !usb || USB512_interface.NpV1Management.UidFromPath(path)!=null;}
        private List<NpDownloadRecord> ReadCatalog(Stream stream,Source source,Action<int> report=null,bool forceHeaders=false)
        {return HeaderRecoveryEligible(source.Usb,source.Path)?NpCatalogRecovery.Read(stream,()=>cancelled,report,forceHeaders):NpDownloadCatalog.ReadAvailable(stream);}
        internal static string DownloadTimeRemaining(int percent,double elapsedSeconds,bool knownSize) {
            return USB512_interface.NpProgressEstimate.Remaining(percent,100,elapsedSeconds,knownSize);
        }
        private async Task SaveAsync() {
            string label;try{label=USB512_interface.NpFolderNaming.Label(sessionLabel.Text);}catch(ArgumentException ex){status.Text=ex.Message;return;}
            if(listedSource==null || !ReferenceEquals(listedSource,sources.SelectedItem)){status.Text="List this source first.";return;}
            var chosen=records.CheckedItems.Cast<NpDownloadRecord>().ToList();if(chosen.Count==0){status.Text="Select at least one recording.";return;}
            if(chosen.Any(r=>!r.Downloadable)){status.Text="A selected recording has invalid metadata or geometry. Read its listing details before downloading.";return;}
            bool recoverInterrupted=false;
            if(chosen.Any(r=>r.PowerInterruptedRecovery)){
                var answer=MessageBox.Show(DialogOwner,"A selected V1 recording has an unfinished footer reporting zero chunks. Stored payload may still exist.\r\n\r\nScan and export its structurally validated contiguous prefix, without changing the card? Recovery stops at the first invalid chunk and remains INCOMPLETE, not firmware-committed or lossless. Native sample values have no independent payload checksum. NP exports use nominal counts (not calibration). Its size is unknown until scanned; ensure enough destination space.\r\n\r\nYes: recover validated prefix. No: export reported metadata only.","Read-only interrupted recording recovery",MessageBoxButtons.YesNoCancel,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button3);
                if(answer==DialogResult.Cancel)return;recoverInterrupted=answer==DialogResult.Yes;
            }
            string root=destination.Text.Trim().Trim('"');if(root.Length==0){status.Text="Enter a destination folder or choose Browse.";return;}
            bool merge=mergeDat.Checked;
            try {root=Path.GetFullPath(root);destination.Text=root;long required=checked(chosen.Sum(r=>RequiredBytes(r)*(merge&&!r.Media?2L:1L))+16*1024*1024);
                var drive=new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root)));
                if(drive.AvailableFreeSpace<required){status.Text="Insufficient space: allow "+(required/1073741824.0).ToString("N1")+" GiB for decoded data and auxiliary outputs.";return;}
            }catch(Exception ex){status.Text="Cannot check destination space: "+ex.Message;return;}
            bool makeMedia=convertAfterDownload.Checked;var conversionErrors=new List<string>();var conversionWarnings=new List<string>();
            var calibration=USB512_interface.NpCalibrationStore.Get("offline");
            SetBusy(true);cancelled=false;progress.Value=0;var source=listedSource;
            var elapsed=new USB512_interface.NpProgressEstimate();double nativeTotal=chosen.Sum(r=>(double)r.Chunks*(r.V2?46592:65536)),nativeDone=0,nativeSeconds=0;
            bool queueSizeKnown=!(recoverInterrupted&&chosen.Any(r=>r.PowerInterruptedRecovery));
            status.Text="Preparing download · "+elapsed.Percent(0);
            var ui=new Progress<Tuple<int,string>>(p=>{progress.Value=Math.Max(0,Math.Min(100,p.Item1));status.Text=p.Item2;});
            try {await PrepareSourceAsync(source);await Task.Run(()=>{using(var stream=Open(source)) {
                // Revalidate directory on the new connection; never trust stale UI extents.
                var current=ReadCatalog(stream,source,null,listedRecoverySearch);
                for(int i=0;i<chosen.Count;i++) {
                    var r=chosen[i];if(!current.Any(n=>n.Downloadable&&n.Start==r.Start&&n.End==r.End&&n.Session==r.Session&&n.Chunks==r.Chunks&&n.State==r.State&&n.Fault==r.Fault&&n.FormatVersion==r.FormatVersion&&n.CameraHz==r.CameraHz&&n.MicrophoneHz==r.MicrophoneHz&&n.ProbeMask==r.ProbeMask&&n.ApHz==r.ApHz))throw new IOException("Recording directory changed. Refresh before downloading.");
                    string name=USB512_interface.NpFolderNaming.DownloadName(label,NpDownloadCatalog.RecordingStartTime(stream,r),"NP_"+r.Session+"_LBA"+r.Start);
                    using(var destination=USB512_interface.NpFolderNaming.Reserve(root,name)) {
                    string output=destination.Path;int index=i;
                    var watch=System.Diagnostics.Stopwatch.StartNew();
                    Action<int> reportProgress=p=>{
                        double totalBytes=(double)r.Chunks*(r.V2?46592:65536),bytes=totalBytes*p/100,seconds=Math.Max(watch.Elapsed.TotalSeconds,0.001);
                        bool knownSize=!(recoverInterrupted&&r.PowerInterruptedRecovery);
                        int weight=r.Media&&makeMedia?75:merge&&!r.Media?70:100;int local=Math.Min(99,p*weight/100);
                        string activity=knownSize?"native download ~"+(bytes/1073741824).ToString("F2")+" / "+(totalBytes/1073741824).ToString("F2")+" GiB · ~"+(bytes/seconds/1e6).ToString("F1")+" MB/s":"read-only prefix recovery";
                        ((IProgress<Tuple<int,string>>)ui).Report(Tuple.Create((index*100+local)/chosen.Count,"Session "+r.Session+" ("+(index+1)+"/"+chosen.Count+"): "+
                            (p>=100?"Finalizing recording files…":p+"% | "+activity+" | "+DownloadTimeRemaining(p,seconds,knownSize))+
                            " | Selected downloads: "+USB512_interface.NpProgressEstimate.Remaining(nativeDone+bytes,nativeTotal,nativeSeconds+seconds,queueSizeKnown)+" · "+elapsed.Elapsed+"\r\n"+output));
                    };
                    reportProgress(0);
                    using(var prefetch=source.Usb?new NpDownloadPrefetchStream(stream,checked(r.Start*512),checked(r.End*512),()=>cancelled):null){
                    Stream input=prefetch??stream;
                    if(recoverInterrupted&&r.PowerInterruptedRecovery){
                        if(r.Media)NpMediaExport.ExportPowerInterruptedPrefix(input,r.Start,r.End,output,()=>cancelled,reportProgress);
                        else NpCe64Export.ExportPowerInterruptedPrefix(input,r.Start,r.End,output,()=>cancelled,reportProgress);
                    }
                    else {
                        var result=NpCe64Export.ExportCalibrated(input,r.Start,r.End,output,()=>cancelled,reportProgress,calibration.Count==0?null:calibration);
                        string warning=NpCe64Export.V2RecordingWarning(result);
                        if(warning!=null)conversionWarnings.Add("Session "+r.Session+": "+warning);
                    }
                    }
                    nativeDone+=(double)r.Chunks*(r.V2?46592:65536);nativeSeconds+=watch.Elapsed.TotalSeconds;
                    if(merge && !r.Media && (r.Chunks>0 || recoverInterrupted&&r.PowerInterruptedRecovery))NpSdDatMerge.ExportTimed(output,r.V2,(int)r.ProbeMask,(int)r.ApHz,()=>cancelled,
                        (p,message)=>((IProgress<Tuple<int,string>>)ui).Report(Tuple.Create((index*100+70+Math.Min(29,p*29/100))/chosen.Count,message+"\r\n"+output)));
                    if(r.RecoveryNotice!=null)File.WriteAllText(Path.Combine(output,"download_catalog.txt"),
                        (r.V2?"published_catalog_end_lba="+r.CatalogEnd+"\ncommitted_download_end_lba="+r.End:"record_start_lba="+r.Start+"\nexport_bound_end_lba="+r.End+"\nfooter_reported_chunks="+r.Chunks)+"\nstored_state="+r.State+"\nstored_fault="+r.Fault+"\n"+r.RecoveryNotice+"\nThe card was not modified. Stored recording metadata is retained unchanged. Recovery is not a free-space or payload-durability proof.\n");
                    if(r.Media&&makeMedia&&recoverInterrupted&&r.PowerInterruptedRecovery)conversionWarnings.Add("Session "+r.Session+": recovered native prefix remains incomplete; convert separately after inspection.");
                    if(r.Media&&makeMedia&&!(recoverInterrupted&&r.PowerInterruptedRecovery))try{
                        string warning=NpMediaExport.ConvertFiles(output,()=>cancelled,(p,message)=>((IProgress<Tuple<int,string>>)ui).Report(Tuple.Create((index*100+75+Math.Min(24,p*24/100))/chosen.Count,message)));
                        if(warning!=null)conversionWarnings.Add("Session "+r.Session+": "+warning);
                    }catch(OperationCanceledException){throw;}catch(Exception ex){conversionErrors.Add("Session "+r.Session+": "+ex.Message);}
                    }
                }
            }});progress.Value=100;status.Text=(conversionErrors.Count==0?"Export finished · "+elapsed.Elapsed+". Data, metadata and timestamps saved. NP: capture_status.txt.":"Native download complete; media conversion failed: "+string.Join("; ",conversionErrors))+" "+(recoverInterrupted?"Recovered prefixes remain INCOMPLETE; not firmware-finalized. ":chosen.Any(r=>!r.Complete)?"Incomplete recordings contain footer-reported data only. ":"")+string.Join("; ",conversionWarnings);
            }catch(OperationCanceledException){status.Text="Cancelled. Partial files are retained and marked incomplete.";}
            catch(Exception ex){status.Text="Download stopped: "+ex.Message;MessageBox.Show(DialogOwner,status.Text,"NP download",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
            finally{SetBusy(false);}
        }
    }
}

