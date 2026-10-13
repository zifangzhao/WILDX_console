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
        private sealed class Source {internal string Path,Label,MediaType;internal bool Usb;internal bool CanFormat{get{return !Usb && NpCardFormat.IsRemovableMedia(MediaType);}}public override string ToString(){return Label;}}
        private readonly ComboBox sources=new ComboBox{Dock=DockStyle.Top,DropDownStyle=ComboBoxStyle.DropDownList};
        private readonly CheckedListBox records=new CheckedListBox{Dock=DockStyle.Fill,CheckOnClick=true};
        private readonly Label status=new Label{Dock=DockStyle.Bottom,Height=55,Text="Stop recording before download. USB requires the read-only NP download firmware."};
        private readonly ProgressBar progress=new ProgressBar{Dock=DockStyle.Bottom};
        private readonly Button refresh=new Button{Text="Refresh sources",AutoSize=true},list=new Button{Text="List recordings",AutoSize=true},save=new Button{Text="Download selected…",AutoSize=true},cancel=new Button{Text="Cancel",AutoSize=true},play=new Button{Text="Play exported camera…",AutoSize=true};
        private readonly Button convert=new Button{Text="Convert exported media…",AutoSize=true};
        private readonly Button calibrationButton=new Button{Text="Calibration…",AutoSize=true};
        private readonly Button format=new Button{Name="quickFormatNpSd",Text="Quick Format selected SD…",AutoSize=true};
        private readonly Button fast=new Button{Text="NP1 fast archive…",AutoSize=true};
        private readonly CheckBox convertAfterDownload=new CheckBox{Text="MP4 + WAV",Checked=true,AutoSize=true};
        private bool busy,refreshing,usbBlocked;private volatile bool cancelled;private Source listedSource;
        private readonly string requestedSourcePath;
        private string boundSourcePath;
        private readonly Timer enumerationTimer=new Timer{Interval=1000};
        private string parentUsbPath;
        internal bool IsBusy{get{return busy;}}
        internal Button FormatButton{get{return format;}}
        internal string SelectedSourceDescription{get{var source=sources.SelectedItem as Source;return source==null?null:source.Label;}}
        IWin32Window DialogOwner{get{return TopLevelControl??this;}}
        internal event Action BusyChanged;
        internal NpDownloadForm(string sourcePath=null):this(sourcePath,true){}
        internal NpDownloadForm(string sourcePath,bool autoDiscover) {
            sourcePath=sourcePath==null?null:sourcePath.Trim();
            requestedSourcePath=sourcePath!=null && sourcePath.StartsWith("PhysicalDrive",StringComparison.OrdinalIgnoreCase)?@"\\.\"+sourcePath:sourcePath;
            boundSourcePath=requestedSourcePath;
            Text="WILD-X NP1 / NP2 / media — SD Download";Width=950;Height=500;
            var actions=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true};actions.Controls.AddRange(new Control[]{refresh,list,save,cancel,play,convert,convertAfterDownload,format});
            actions.Controls.Add(calibrationButton);
            calibrationButton.Click+=(s,e)=>{if(!busy)USB512_interface.NpCalibrationStore.Show(DialogOwner,"offline");};
            actions.Controls.Add(fast);
            fast.Click+=(s,e)=>{if(busy || usbBlocked)return;var source=sources.SelectedItem as Source;using(var form=new USB512_interface.NpLoggerForm(source!=null&&source.Usb?source.Path:null))form.ShowDialog(DialogOwner);};
            Controls.Add(records);Controls.Add(actions);Controls.Add(sources);Controls.Add(progress);Controls.Add(status);
            refresh.Click+=(s,e)=>{RefreshSources();enumerationTimer.Start();};list.Click+=async(s,e)=>await ListAsync();save.Click+=async(s,e)=>await SaveAsync();cancel.Click+=(s,e)=>cancelled=true;
            records.ItemCheck+=(s,e)=>{var r=records.Items[e.Index] as NpDownloadRecord;if(r!=null&&!r.Downloadable){e.NewValue=CheckState.Unchecked;status.Text="Session "+r.Session+" is not downloadable: "+r.ValidationError;}};
            play.Click+=(s,e)=>new NpMediaPlaybackForm().Show(DialogOwner);
            convert.Click+=async(s,e)=>await ConvertExistingAsync();
            format.Click+=async(s,e)=>await FormatSelectedAsync();
            sources.SelectedIndexChanged+=(s,e)=>{var source=sources.SelectedItem as Source;if(source!=null&&!refreshing)boundSourcePath=source.Path;UpdateControls();};
            FormClosing+=(s,e)=>{if(busy){cancelled=true;e.Cancel=true;status.Text="Wait for the current operation to finish before closing. A catalog reset cannot be interrupted.";}};
            Shown+=(s,e)=>{if(autoDiscover){RefreshSources();enumerationTimer.Start();}};
            enumerationTimer.Tick+=(s,e)=>{if(!busy && Visible)try{PollUsb();}catch(Exception ex){status.Text="USB discovery: "+ex.Message;}};
            FormClosed+=(s,e)=>{enumerationTimer.Stop();enumerationTimer.Dispose();};
            SetBusy(false);
            USB512_interface.WildXTheme.Apply(this);Padding=new Padding(0);
            records.IntegralHeight=false;records.Font=USB512_interface.WildXTheme.Body;
            sources.Margin=new Padding(0,0,0,12);status.Padding=new Padding(0,10,0,0);status.Height=68;
            status.ForeColor=USB512_interface.WildXTheme.Muted;
            Controls.Add(new Label {Text="STORAGE SOURCE  /  USB device or card reader",Dock=DockStyle.Top,Height=26,ForeColor=USB512_interface.WildXTheme.Muted});
            USB512_interface.WildXTheme.Button(save,true);USB512_interface.WildXTheme.Button(format,false,true);
            if(!autoDiscover)status.Text="Refresh sources to choose a USB device or card reader. List its recordings before downloading.";
        }
        internal void SetUsbSelection(string path){if(!busy && !string.Equals(path,parentUsbPath,StringComparison.OrdinalIgnoreCase)){parentUsbPath=path;if(!string.IsNullOrEmpty(path))boundSourcePath=path;}}
        internal void SetUsbBlocked(bool value){usbBlocked=value;UpdateControls();}
        internal void RequestCancel(){if(busy){cancelled=true;status.Text="Waiting for the operation to finish. A catalog reset cannot be interrupted.";}}
        protected override void Dispose(bool disposing){if(disposing){enumerationTimer.Stop();enumerationTimer.Dispose();}base.Dispose(disposing);}
        private void PollUsb() {
            refreshing=true;
            try {
            var paths=NpDownloadNative.EnumerateNpPaths()
                .Where(p=>p.IndexOf("vid_1209&pid_b386",StringComparison.OrdinalIgnoreCase)>=0).ToList();
            for(int i=sources.Items.Count-1;i>=0;i--){var item=(Source)sources.Items[i];if(item.Usb&&!paths.Contains(item.Path))sources.Items.RemoveAt(i);}
            foreach(string path in paths)if(!sources.Items.Cast<Source>().Any(p=>p.Usb&&p.Path==path))sources.Items.Insert(0,new Source{Usb=true,Path=path,Label="NP board USB — "+path});
            if(sources.SelectedIndex<0)SelectBoundSource();
            }finally{refreshing=false;}
        }
        private void UpdateControls(){var source=sources.SelectedItem as Source;calibrationButton.Enabled=refresh.Enabled=sources.Enabled=play.Enabled=convert.Enabled=convertAfterDownload.Enabled=!busy;list.Enabled=save.Enabled=!busy && !(usbBlocked && source!=null && source.Usb);fast.Enabled=!busy && !usbBlocked;format.Enabled=!busy && source!=null && source.CanFormat;cancel.Enabled=busy;}
        private void SetBusy(bool value){busy=value;UpdateControls();var changed=BusyChanged;if(changed!=null)changed();}
        private async Task FormatSelectedAsync() {
            var source=sources.SelectedItem as Source;if(source==null || !source.CanFormat){status.Text="Quick Format requires removable SD media. USB external disks/readers and logger USB endpoints remain read-only.";return;}
            SetBusy(true);cancel.Enabled=false;
            try {
                var target=await Task.Run(()=>NpCardFormat.Inspect(source.Path));
                if(MessageBox.Show(DialogOwner,"Quick Format this connected NP/media SD card:\r\n\r\n"+target.Description+"\r\n\r\nAll recordings will be removed from the catalog and can be overwritten. This is not a secure erase. Settings are retained.\r\nDo not swap cards until finished; the reader may not expose the card's own serial.\r\n\r\nFormat this selected SD card?","Destructive NP/media Quick Format",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes){status.Text="Format cancelled; nothing written.";return;}
                status.Text="Resetting and verifying native NP/media catalog…";
                await Task.Run(()=>NpCardFormat.Format(target));records.Items.Clear();listedSource=null;
                status.Text="Quick Format verified: empty NP/media catalog. Settings retained; old payload data is not securely erased.";
            }catch(Exception ex){status.Text="Format not confirmed: "+ex.Message;MessageBox.Show(DialogOwner,status.Text,"NP/media SD",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
            finally{SetBusy(false);}
        }
        private async Task ConvertExistingAsync() {
            string folder;using(var dialog=new FolderBrowserDialog{Description="Select a validated native V1 media export (recording.json, camera.raw, microphone.dat)."}){if(dialog.ShowDialog(DialogOwner)!=DialogResult.OK)return;folder=dialog.SelectedPath;}
            SetBusy(true);cancelled=false;progress.Value=0;
            var ui=new Progress<Tuple<int,string>>(p=>{progress.Value=p.Item1;status.Text=p.Item2;});
            try{string warning=await Task.Run(()=>NpMediaExport.ConvertFiles(folder,()=>cancelled,(p,message)=>((IProgress<Tuple<int,string>>)ui).Report(Tuple.Create(p,message))));progress.Value=100;status.Text="Converted media. Native files and timestamp CSVs retained; video and audio durations are independent. "+warning;}
            catch(OperationCanceledException){status.Text="Conversion cancelled. Native export and any completed outputs are retained.";}
            catch(Exception ex){status.Text="Conversion failed; native export retained: "+ex.Message;}
            finally{SetBusy(false);}
        }
        private void RefreshSources() {
            refreshing=true;
            try {sources.Items.Clear();records.Items.Clear();listedSource=null;
                foreach(string path in NpDownloadNative.EnumerateNpPaths())
                    if(path.IndexOf("vid_1209&pid_b386",StringComparison.OrdinalIgnoreCase)>=0)sources.Items.Add(new Source{Usb=true,Path=path,Label="NP board USB — "+path});
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
            if(string.Equals(first,second,StringComparison.OrdinalIgnoreCase))return true;
            if(string.IsNullOrEmpty(first)||string.IsNullOrEmpty(second))return false;
            int a=first.LastIndexOf('#'),b=second.LastIndexOf('#');
            return a>0&&b>0&&string.Equals(first.Substring(0,a),second.Substring(0,b),StringComparison.OrdinalIgnoreCase);
        }
        private void SelectBoundSource() {
            if(string.IsNullOrEmpty(boundSourcePath)){if(sources.Items.Count==1){sources.SelectedIndex=0;boundSourcePath=((Source)sources.SelectedItem).Path;}return;}
            int selected=-1;for(int i=0;i<sources.Items.Count;i++)if(SameSource(boundSourcePath,((Source)sources.Items[i]).Path)) {
                if(selected>=0){sources.SelectedIndex=-1;return;}selected=i;
            }
            sources.SelectedIndex=selected;
            if(selected<0)status.Text="Selected device is absent; choose a source explicitly. Another board was not substituted.";
        }
        internal static long RequiredBytes(NpDownloadRecord record) {
            return record.V2?checked(((long)record.Chunks*61440+1599)/1600*2*384*2):checked((long)record.Chunks*65536*4);
        }
        private Stream Open(Source source) {
            if(source.Usb){if(usbBlocked)throw new IOException("Disconnect BLE and finish the USB operation before accessing this USB logger.");return new NpDownloadStream(source.Path){Cancelled=()=>cancelled};}
            return NpDownloadStream.OpenDisk(source.Path);
        }
        private async Task ListAsync() {
            var source=sources.SelectedItem as Source;if(source==null)return;SetBusy(true);cancelled=false;records.Items.Clear();listedSource=null;
            try {status.Text="Reading NP recording directory (read-only)…";
                string transport="SD reader";
                var found=await Task.Run(()=>{using(var stream=Open(source)){if(stream is NpDownloadStream)transport=((NpDownloadStream)stream).TransportDescription;return NpDownloadCatalog.Read(stream);}});
                foreach(var r in found)records.Items.Add(r,false);listedSource=source;
                status.Text=transport+". "+found.Count+" recording(s); "+found.Count(r=>r.Downloadable)+" downloadable, "+found.Count(r=>!r.Downloadable)+" failed/empty. Select the desired sessions; payload integrity is checked during download.";
            }catch(Exception ex){status.Text=ex.Message;}finally{SetBusy(false);}
        }
        private async Task SaveAsync() {
            if(listedSource==null || !ReferenceEquals(listedSource,sources.SelectedItem)){status.Text="List this source first.";return;}
            var chosen=records.CheckedItems.Cast<NpDownloadRecord>().ToList();if(chosen.Count==0){status.Text="Select at least one recording.";return;}
            if(chosen.Any(r=>!r.Downloadable)){status.Text="Failed/empty recordings cannot be downloaded.";return;}
            string root;using(var dialog=new FolderBrowserDialog()){if(dialog.ShowDialog(DialogOwner)!=DialogResult.OK)return;root=dialog.SelectedPath;}
            try {long required=chosen.Sum(RequiredBytes)+16*1024*1024;
                var drive=new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root)));
                if(drive.AvailableFreeSpace<required){status.Text="Insufficient space: allow "+(required/1073741824.0).ToString("N1")+" GiB for raw, AP/LFP and auxiliary outputs.";return;}
            }catch(Exception ex){status.Text="Cannot check destination space: "+ex.Message;return;}
            bool makeMedia=convertAfterDownload.Checked;var conversionErrors=new List<string>();var conversionWarnings=new List<string>();
            var calibration=USB512_interface.NpCalibrationStore.Get("offline");
            SetBusy(true);cancelled=false;progress.Value=0;var source=listedSource;
            var ui=new Progress<Tuple<int,string>>(p=>{progress.Value=Math.Max(0,Math.Min(100,p.Item1));status.Text=p.Item2;});
            try {await Task.Run(()=>{using(var stream=Open(source)) {
                // Revalidate directory on the new connection; never trust stale UI extents.
                var current=NpDownloadCatalog.Read(stream);
                for(int i=0;i<chosen.Count;i++) {
                    var r=chosen[i];if(!current.Any(n=>n.Downloadable&&n.Start==r.Start&&n.End==r.End&&n.Session==r.Session&&n.Chunks==r.Chunks&&n.State==r.State&&n.Fault==r.Fault&&n.FormatVersion==r.FormatVersion&&n.CameraHz==r.CameraHz&&n.MicrophoneHz==r.MicrophoneHz&&n.ProbeMask==r.ProbeMask&&n.ApHz==r.ApHz))throw new IOException("Recording directory changed. Refresh before downloading.");
                    string output=Path.Combine(root,"NP_"+r.Session+"_LBA"+r.Start);int index=i;
                    var watch=System.Diagnostics.Stopwatch.StartNew();
                    NpCe64Export.ExportCalibrated(stream,r.Start,r.End,output,()=>cancelled,p=>{
                        double bytes=(double)r.Chunks*65536*p/100,seconds=Math.Max(watch.Elapsed.TotalSeconds,0.001);
                        int local=r.Media&&makeMedia?p*75/100:p;
                        ((IProgress<Tuple<int,string>>)ui).Report(Tuple.Create((index*100+local)/chosen.Count,"Session "+r.Session+": "+p+"% | native download "+(bytes/seconds/1e6).ToString("F1")+" MB/s — "+output));
                    },calibration.Count==0?null:calibration);
                    if(r.Media&&makeMedia)try{
                        string warning=NpMediaExport.ConvertFiles(output,()=>cancelled,(p,message)=>((IProgress<Tuple<int,string>>)ui).Report(Tuple.Create((index*100+75+p*25/100)/chosen.Count,message)));
                        if(warning!=null)conversionWarnings.Add("Session "+r.Session+": "+warning);
                    }catch(OperationCanceledException){throw;}catch(Exception ex){conversionErrors.Add("Session "+r.Session+": "+ex.Message);}
                }
            }});progress.Value=100;status.Text=(conversionErrors.Count==0?"Download complete. Native files and timestamps retained; MP4/WAV created when selected. NP: capture_status.txt.":"Native download complete; media conversion failed: "+string.Join("; ",conversionErrors))+" "+string.Join("; ",conversionWarnings);
            }catch(OperationCanceledException){status.Text="Cancelled. Partial files are retained and marked incomplete.";}
            catch(Exception ex){status.Text="Download stopped: "+ex.Message;MessageBox.Show(DialogOwner,status.Text,"NP download",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
            finally{SetBusy(false);}
        }
    }
}

