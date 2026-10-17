using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CE32_console;

namespace USB512_interface
{
    internal sealed class NpLoggerForm : Form
    {
        readonly ComboBox devices=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill};
        readonly ListBox sessions=new ListBox{Dock=DockStyle.Fill,HorizontalScrollbar=true};
        readonly ComboBox method=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=270};
        readonly TextBox folder=new TextBox{Dock=DockStyle.Fill,ReadOnly=true};
        readonly TextBox sessionLabel=new TextBox {Name="archiveSessionLabel",Dock=DockStyle.Fill,MaxLength=80,AccessibleName="Session label"};
        readonly TextBox log=new TextBox{Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill};
        readonly Label status=new Label{Text="Refresh to discover V1 loggers (identified by MCU UID).",AutoSize=true,MaximumSize=new Size(810,0)};
        readonly Label progressText=new Label{AutoSize=true,Text="Idle",MaximumSize=new Size(850,0)};
        readonly ProgressBar progress=new ProgressBar{Dock=DockStyle.Fill};
        readonly FlowLayoutPanel actions=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,WrapContents=true};
        readonly FlowLayoutPanel connection=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,WrapContents=true};
        readonly Button refresh=new Button{Text="Refresh",AutoSize=true};
        readonly Button inspect=new Button{Text="Read status / sessions",AutoSize=true};
        readonly Button download=new Button{Text="Download session",AutoSize=true};
        readonly Button decode=new Button{Text="Decode saved archive…",AutoSize=true};
        readonly Button browse=new Button{Text="Output folder…",AutoSize=true};
        readonly Button cancel=new Button{Text="Cancel",AutoSize=true,Enabled=false};
        readonly Button enterDownload=new Button{Text="Enter USB download mode",AutoSize=true};
        readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer{Interval=100};
        NpV1Management.Status deviceStatus;
        string selectedUid;
        string catalogPath;
        volatile bool cancelled;
        bool working;
        readonly object progressLock=new object();
        string pendingText;
        int pendingPercent;

        internal NpLoggerForm() : this(null) {}
        internal NpLoggerForm(string selectedPath) : this(selectedPath,"") {}
        internal NpLoggerForm(string selectedPath,string label)
        {
            selectedUid=NpV1Management.UidFromPath(selectedPath);
            Text="NP V1 — SD Download";
            ClientSize=new Size(900,650);MinimumSize=new Size(780,590);StartPosition=FormStartPosition.CenterParent;
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(12),ColumnCount=1,RowCount=9};
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            for(int i=0;i<9;i++)layout.RowStyles.Add(new RowStyle(i==2||i==8?SizeType.Percent:SizeType.AutoSize,i==2?40:i==8?60:0));
            var top=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=3};
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.Controls.Add(devices,0,0);top.Controls.Add(refresh,1,0);top.Controls.Add(inspect,2,0);
            layout.Controls.Add(top,0,0);layout.Controls.Add(status,0,1);layout.Controls.Add(sessions,0,2);
            var output=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=2};output.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));output.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            folder.Text=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"NP downloads");output.Controls.Add(folder,0,0);output.Controls.Add(browse,1,0);layout.Controls.Add(output,0,3);
            sessionLabel.Text=label;
            output.Controls.Add(new Label {Text="Session label",AutoSize=true,Anchor=AnchorStyles.Left},0,1);output.SetColumnSpan(output.GetControlFromPosition(0,1),2);
            output.Controls.Add(sessionLabel,0,2);output.SetColumnSpan(sessionLabel,2);
            method.Items.AddRange(new object[]{"Fast archive + decode (recommended)","Fast archive only","Compatible download + decode"});method.SelectedIndex=0;
            actions.Controls.AddRange(new Control[]{method,download,decode,cancel});layout.Controls.Add(actions,0,4);
            connection.Controls.Add(enterDownload);layout.Controls.Add(connection,0,5);layout.Controls.Add(progressText,0,6);layout.Controls.Add(progress,0,7);layout.Controls.Add(log,0,8);Controls.Add(layout);
            refresh.Click+=delegate{Discover();};inspect.Click+=async delegate{await Inspect();};
            devices.SelectedIndexChanged+=delegate{var selected=devices.SelectedItem as NpV1Target;if(selected!=null)selectedUid=selected.Uid;deviceStatus=null;catalogPath=null;sessions.Items.Clear();status.Text="Read status / sessions before downloading from this device.";UpdateControls();};
            sessions.SelectedIndexChanged+=delegate{UpdateControls();};
            browse.Click+=delegate{using(var d=new FolderBrowserDialog{SelectedPath=folder.Text})if(d.ShowDialog(this)==DialogResult.OK)folder.Text=d.SelectedPath;};
            download.Click+=async delegate{await Download();};decode.Click+=async delegate{await DecodeArchive();};
            enterDownload.Click+=async delegate{await EnterDownloadMode();};cancel.Click+=delegate{cancelled=true;cancel.Enabled=false;progressText.Text="Cancelling; waiting for active I/O workers to return…";};
            FormClosing+=delegate(object sender,FormClosingEventArgs e){if(working){e.Cancel=true;MessageBox.Show(this,"Wait for the operation to finish, or cancel the download. USB buffers cannot be freed while reads are active.");}};
            timer.Tick+=delegate{lock(progressLock){if(pendingText!=null){progressText.Text=pendingText;progress.Value=pendingPercent;pendingText=null;}}};timer.Start();
            Resize+=delegate{status.MaximumSize=progressText.MaximumSize=new Size(Math.Max(200,ClientSize.Width-40),0);};
            Shown+=async delegate{Discover();if(devices.SelectedItem!=null)await InspectStatus(false);};UpdateControls();
            WildXTheme.Apply(this);WildXTheme.Button(download,true);status.ForeColor=progressText.ForeColor=WildXTheme.Muted;
        }
        void Discover()
        {
            if(working)return;
            devices.Items.Clear();
            var found=NpV1Management.Enumerate();
            foreach(var t in found)devices.Items.Add(t);
            devices.SelectedIndex=NpV1Management.SelectionAfterRefresh(found,selectedUid);
            deviceStatus=null;catalogPath=null;sessions.Items.Clear();
            status.Text=devices.Items.Count==0?"No V1 logger enumerated. Generic WILDX / V2 boards are deliberately excluded.":devices.SelectedIndex<0 && selectedUid!=null?"Selected logger "+selectedUid+" is not uniquely connected. Another logger will not be substituted.":"Select the exact UID, then read status / sessions.";UpdateControls();
        }
        void UpdateControls()
        {
            sessionLabel.Enabled=devices.Enabled=refresh.Enabled=browse.Enabled=method.Enabled=decode.Enabled=!working;
            inspect.Enabled=!working && devices.SelectedItem!=null;
            download.Enabled=!working && deviceStatus!=null && deviceStatus.Download && sessions.SelectedItem!=null && catalogPath!=null;
            enterDownload.Enabled=!working && deviceStatus!=null && deviceStatus.Split && deviceStatus.Busy==0 && deviceStatus.PendingMode==0 && !deviceStatus.Download;
        }
        void Report(string text,int percent){lock(progressLock){pendingText=text;pendingPercent=Math.Max(0,Math.Min(100,percent));}}
        void Append(string text){log.AppendText(DateTime.Now.ToString("HH:mm:ss")+" "+text+Environment.NewLine);}
        async Task Run(Func<Task> operation,bool canCancel=true)
        {
            if(working)return;working=true;cancelled=false;cancel.Enabled=canCancel;UpdateControls();
            try {await operation();}
            catch(OperationCanceledException){Report("Cancelled — partial files retained, not complete.",0);Append("Cancelled. Check output status reports before using files.");}
            catch(Exception ex){Report("Stopped: "+ex.Message,0);Append(ex.ToString());MessageBox.Show(this,ex.Message+"\n\nNo automatic retry or silent resynchronization was performed. Partial files are retained.","NP logger",MessageBoxButtons.OK,MessageBoxIcon.Error);}
            finally{working=false;cancel.Enabled=false;UpdateControls();}
        }
        async Task Inspect()
        {
            await InspectStatus(true);
        }
        async Task InspectStatus(bool readSessions)
        {
            var t=devices.SelectedItem as NpV1Target;if(t==null)return;
            deviceStatus=null;catalogPath=null;sessions.Items.Clear();
            await Run(async ()=>{
                var result=await Task.Run(()=>{if(readSessions)t.Path=NpV1Management.EnsureDownload(t.Path,()=>cancelled);using(var m=new NpV1Management(t))return m.ReadStatus();});
                deviceStatus=result;status.Text=result.Description;Append(t+": "+result.Description);
                if(readSessions && result.Download) {
                    var records=await Task.Run(()=>{using(var s=new NpDownloadStream(t.Path)){s.Cancelled=()=>cancelled;Report(s.TransportDescription,0);return NpDownloadCatalog.Read(s);}});
                    foreach(var record in records)sessions.Items.Add(record);catalogPath=t.Path;
                    if(records.Count>0)sessions.SelectedIndex=records.Count-1;
                }
            });
        }
        async Task Download()
        {
            var t=devices.SelectedItem as NpV1Target;var selected=sessions.SelectedItem as NpDownloadRecord;
            if(t==null || selected==null || catalogPath!=t.Path)return;
            int choice=method.SelectedIndex;string root=folder.Text;
            string label;try{label=NpFolderNaming.Label(sessionLabel.Text);}catch(ArgumentException ex){progressText.Text=ex.Message;return;}
            if(!selected.Complete && MessageBox.Show(this,"Firmware marked this recording incomplete. Export only its committed prefix for diagnosis?", "Incomplete recording",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;
            await Run(()=>Task.Run(()=>{
                Directory.CreateDirectory(root);
                long archiveBytes=checked((selected.End-selected.Start)*512);
                long needed=choice==1?archiveBytes:checked((long)selected.Chunks*65536*4+(choice==0?archiveBytes:0));
                if(new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))).AvailableFreeSpace<needed+67108864)throw new IOException("Insufficient disk space for archive and decoded outputs.");
                string recordingFolder=null,archive=null,output=null;
                NpFolderNaming.Destination destination=null;
                USB512_interface.NpRecordingSettings settings=null;bool finished=false,firmwareComplete=false;string failure=null;
                try {
                using(var s=new NpDownloadStream(t.Path)) {
                    s.Cancelled=()=>cancelled;
                    var current=NpDownloadCatalog.Read(s).Find(r=>r.Session==selected.Session && r.Start==selected.Start && r.End==selected.End && r.Chunks==selected.Chunks && r.State==selected.State && r.Fault==selected.Fault);
                    if(current==null)throw new IOException("SD catalog changed. Refresh before downloading.");
                    string name=NpFolderNaming.DownloadName(label,NpDownloadCatalog.RecordingStartTime(s,current),
                        "NPV1_"+t.Uid+"_session"+current.Session+"_LBA"+current.Start);
                    destination=NpFolderNaming.Reserve(root,name);destination.Create();recordingFolder=destination.Path;
                    archive=Path.Combine(recordingFolder,Path.GetFileName(recordingFolder)+".npraw");output=Path.Combine(recordingFolder,"decoded");
                    firmwareComplete=current.Complete;
                    settings=USB512_interface.NpRecordingSettings.Saved(recordingFolder,false,(int)current.ApHz,(int)current.ProbeMask,
                        current.FormatVersion,current.Session,"sd_download",t.Uid,false);
                    settings.Context("raw_archive_file",choice==2?null:Path.GetFileName(archive));
                    settings.Context("decoded_output_folder",choice==1?null:"decoded");
                    settings.Context("firmware_recording_complete",current.Complete);settings.Write();
                    if(choice==2){NpCe64Export.Export(s,current.Start,current.End,output,()=>cancelled,p=>Report("Compatible USB transfer + decoding: "+p+"%",p));}
                    else {
                        var watch=Stopwatch.StartNew();
                        string result=s.SaveRawDownload(checked((uint)current.Start),current.End-current.Start,2,archive,
                            (done,total)=>Report(string.Format("USB archive: {0:N1} / {1:N1} MiB | {2:N2} MB/s",done/1048576.0,total/1048576.0,done/Math.Max(.001,watch.Elapsed.TotalSeconds)/1e6),(int)(done*100/total)));
                        File.AppendAllText(archive+".status.txt","summary="+result+Environment.NewLine+"device_uid="+t.Uid+Environment.NewLine);
                    }
                } // USB is closed before CPU/disk-intensive decoding starts.
                if(choice!=2)using(var saved=new NpArchive(archive)){
                    foreach(var part in new[]{Tuple.Create("CE_params.bin",0,1536),Tuple.Create("descriptor.bin",3,512),Tuple.Create("footer.bin",4,512)}){
                        saved.Position=(saved.StartLba+part.Item2)*512;var bytes=new byte[part.Item3];int read=0;
                        while(read<bytes.Length){int got=saved.Read(bytes,read,bytes.Length-read);if(got==0)throw new EndOfStreamException("Recording metadata archive is truncated.");read+=got;}
                        File.WriteAllBytes(Path.Combine(recordingFolder,part.Item1),bytes);
                    }
                }
                if(choice==0)using(var saved=new NpArchive(archive))NpCe64Export.Export(saved,saved.StartLba,saved.EndLba,output,()=>cancelled,p=>Report("Archive saved. Offline NP decoding: "+p+"%",p));
                finished=true;
                Report(choice==1?"Archive complete (transport checked; NP decoding not performed).":"Download + NP decoding complete. See capture_status.txt.",100);
                } catch(Exception ex){failure=ex.Message;throw;}
                finally {try{if(settings!=null){settings.Finished(finished && firmwareComplete,failure??(firmwareComplete?null:"Firmware marked recording incomplete"));settings.Context("download_complete",finished);settings.Context("completion_evidence",!finished?"transfer/export did not finish":choice==1?"archive transport checked; neural decoding not performed":"strict DAT export completed");settings.Write();}}finally{if(destination!=null)destination.Dispose();}}
            }));
        }
        async Task DecodeArchive()
        {
            string path;using(var d=new OpenFileDialog{Filter="NP raw archive (*.npraw)|*.npraw",Title="Select completed archive (keep its .status.txt alongside it)"}){if(d.ShowDialog(this)!=DialogResult.OK)return;path=d.FileName;}
            string output=Path.Combine(folder.Text,Path.GetFileNameWithoutExtension(path)+"_decoded_"+DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
            await Run(()=>Task.Run(()=>{using(var s=new NpArchive(path))NpCe64Export.Export(s,s.StartLba,s.EndLba,output,()=>cancelled,p=>Report("Offline NP decode: "+p+"%",p));Report("Decoded. See "+Path.Combine(output,"capture_status.txt"),100);}));
        }
        async Task EnterDownloadMode()
        {
            var t=devices.SelectedItem as NpV1Target;if(t==null)return;
            if(MessageBox.Show(this,"Enter USB download mode on V1 "+t.Uid+"? USB will re-enumerate.","V1 "+t.Uid,MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;
            await Run(()=>Task.Run(()=>{
                using(var m=new NpV1Management(t))m.SetMode(3);
                var watch=Stopwatch.StartNew();
                while(watch.Elapsed.TotalSeconds<20) {
                    var matching=NpV1Management.Enumerate().FindAll(d=>d.Uid==t.Uid && !d.Recovery);
                    if(matching.Count==1)try {using(var m=new NpV1Management(matching[0])){var s=m.ReadStatus();if(s.Split && s.Role==3){Report("Mode verified: "+s.Description,100);return;}}}catch(Win32Exception){}catch(IOException){}
                    Thread.Sleep(250);
                }
                throw new IOException("Mode acknowledged, but target role did not enumerate within 20 seconds. Refresh; do not assume entry succeeded.");
            }),false);
            Discover();
        }
        protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
    }
}
