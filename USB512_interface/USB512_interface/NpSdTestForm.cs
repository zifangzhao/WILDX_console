using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using CE32_console;

namespace USB512_interface
{
    internal sealed class NpSdTestForm : Form
    {
        sealed class UsbTarget
        {
            internal string Path;
            public override string ToString(){return NpDownloadForm.UsbSourceLabel(Path);}
        }
        readonly NpBleForm logger;
        readonly ComboBox volumes=Choice(),usb=Choice(),size=Choice();
        readonly NumericUpDown seconds=new NumericUpDown {Minimum=10,Maximum=300,Value=60,Width=85};
        readonly TabControl tabs=new TabControl {Dock=DockStyle.Top,Height=280};
        readonly TextBox output=new TextBox {Dock=DockStyle.Fill,ReadOnly=true,Multiline=true,ScrollBars=ScrollBars.Vertical};
        readonly ProgressBar progress=new ProgressBar {Dock=DockStyle.Bottom,Height=18};
        readonly Label status=new Label {Dock=DockStyle.Bottom,AutoSize=false,Height=44};
        readonly Button cancel=new Button {Text="Cancel test",AutoSize=true,Enabled=false};
        readonly Button verify=new Button {Text="Verify test via USB",AutoSize=true,Enabled=false};
        readonly Button switchMode=new Button {Text="Switch logger to USB",AutoSize=true,Enabled=false};
        readonly Button save=new Button {Text="Save report…",AutoSize=true,Enabled=false};
        CancellationTokenSource cancellation;
        NpSdTestResult result,loggerResult;
        string reportPath;
        internal NpSdTestForm(NpBleForm ble=null,string selectedUsb=null)
        {
            logger=ble;Text="SD card test";ClientSize=new Size(900,620);MinimumSize=new Size(760,530);
            StartPosition=FormStartPosition.CenterParent;Padding=new Padding(14);
            var reader=new TabPage("PC card reader");var onboard=new TabPage("V1 / V2 logger");tabs.TabPages.AddRange(new[]{reader,onboard});
            size.Width=100;size.Items.AddRange(new object[]{"1 GiB","4 GiB","16 GiB","32 GiB"});size.SelectedIndex=1;
            var refresh=new Button {Text="Refresh",AutoSize=true};refresh.Click+=(s,e)=>Discover(selectedUsb);
            var runReader=new Button {Text="Test reader",AutoSize=true};runReader.Click+=async(s,e)=>await Reader();
            var readerRows=Rows();reader.Controls.Add(readerRows);
            readerRows.Controls.Add(new Label {Text="Mounted SD card / USB reader",AutoSize=true});readerRows.Controls.Add(volumes);
            readerRows.Controls.Add(Line(new Label {Text="Test size",AutoSize=true,Padding=new Padding(0,7,4,0)},size,refresh,runReader));
            readerRows.Controls.Add(new Label {AutoSize=true,MaximumSize=new Size(820,0),Text="Writes temporary files into free space, verifies them, then removes only those test files. Logger-formatted cards without a Windows drive letter must use the logger test."});
            var loggerRows=Rows();onboard.Controls.Add(loggerRows);
            loggerRows.Controls.Add(new Label {AutoSize=true,MaximumSize=new Size(820,0),Text=logger==null?"Connect in BLE Mode and open SD card test there to create a test recording.":logger.SdTestTarget});
            var runLogger=new Button {Text="Record test",AutoSize=true,Enabled=logger!=null};runLogger.Click+=async(s,e)=>await Record();
            loggerRows.Controls.Add(Line(new Label {Text="Target seconds",AutoSize=true,Padding=new Padding(0,7,4,0)},seconds,runLogger,switchMode));
            loggerRows.Controls.Add(new Label {Text="USB readback device",AutoSize=true});loggerRows.Controls.Add(usb);
            var refreshUsb=new Button {Text="Refresh USB",AutoSize=true};refreshUsb.Click+=(s,e)=>Discover(selectedUsb);
            loggerRows.Controls.Add(Line(refreshUsb,verify));
            loggerRows.Controls.Add(new Label {AutoSize=true,MaximumSize=new Size(820,0),Text="Uses the current neural profile and connected probes. A finite test recording remains on SD. After completion, switch to USB download and verify all stored data."});
            switchMode.Click+=async(s,e)=>await Execute(async()=>{await logger.SdTestDownloadMode();status.Text="Logger handoff requested. Select USB download mode, refresh USB, then verify the same device.";});
            verify.Click+=async(s,e)=>await Verify();
            cancel.Click+=(s,e)=>{cancellation?.Cancel();status.Text="Cancel requested; waiting for the current transfer or recorder finalization.";};
            save.Click+=(s,e)=>{if(result==null)return;using(var file=new SaveFileDialog {Filter="SD test report|*.json",FileName="sd-test.json"})if(file.ShowDialog(this)==DialogResult.OK)try{File.Copy(reportPath,file.FileName,true);}catch(Exception ex){status.Text=ex.Message;}};
            var actions=Line(cancel,save);actions.Dock=DockStyle.Bottom;actions.Height=44;
            Controls.Add(output);Controls.Add(tabs);Controls.Add(status);Controls.Add(progress);Controls.Add(actions);
            WildXTheme.Apply(this);status.ForeColor=WildXTheme.Muted;
            Shown+=(s,e)=>Discover(selectedUsb);
            FormClosing+=(s,e)=>{if(cancellation!=null){cancellation.Cancel();e.Cancel=true;status.Text="Waiting for test cancellation before closing.";}};
        }
        static ComboBox Choice(){return new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Width=460};}
        static FlowLayoutPanel Rows(){return new FlowLayoutPanel {Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(12),AutoScroll=true};}
        static FlowLayoutPanel Line(params Control[] controls){var p=new FlowLayoutPanel {AutoSize=true,WrapContents=true,Margin=Padding.Empty};p.Controls.AddRange(controls);return p;}
        void Discover(string path)
        {
            if(cancellation!=null)return;
            try
            {
                string prior=(volumes.SelectedItem as NpSdCardTest.Volume)?.Root;volumes.Items.Clear();
                foreach(var item in NpSdCardTest.Volumes())volumes.Items.Add(item);
                volumes.SelectedItem=volumes.Items.Cast<NpSdCardTest.Volume>().FirstOrDefault(v=>v.Root==prior);
                if(volumes.SelectedIndex<0&&volumes.Items.Count==1)volumes.SelectedIndex=0;
                string old=(usb.SelectedItem as UsbTarget)?.Path??path;usb.Items.Clear();
                foreach(string item in NpDownloadNative.EnumerateNpPaths())usb.Items.Add(new UsbTarget {Path=item});
                usb.SelectedItem=usb.Items.Cast<UsbTarget>().FirstOrDefault(t=>NpDownloadForm.SameSource(t.Path,old));
                if(usb.SelectedIndex<0&&usb.Items.Count==1)usb.SelectedIndex=0;
                if(volumes.Items.Count==0)status.Text="No mounted reader found. A card with the logger filesystem has no Windows drive letter.";
            }
            catch(Exception ex){status.Text="Discovery: "+ex.Message;}
        }
        async Task Execute(Func<Task> action)
        {
            if(cancellation!=null)return;cancellation=new CancellationTokenSource();tabs.Enabled=false;cancel.Enabled=true;save.Enabled=false;progress.Value=0;
            try{await action();}
            catch(OperationCanceledException){status.Text="Cancelled — not passed.";if(result!=null){result.Status="Cancelled — not passed";SaveResult();}}
            catch(Exception ex){status.Text=ex.Message;if(result!=null){result.Status="Not passed";result.Error=ex.Message;SaveResult();}}
            finally{cancellation.Dispose();cancellation=null;tabs.Enabled=true;cancel.Enabled=false;save.Enabled=result!=null&&reportPath!=null;verify.Enabled=loggerResult!=null&&loggerResult.RecordingCompleted;switchMode.Enabled=verify.Enabled&&logger!=null;}
        }
        Action<int,string> Progress()
        {
            var ui=new Progress<Tuple<int,string>>(p=>{progress.Value=Math.Max(0,Math.Min(100,p.Item1));status.Text=p.Item2;});
            return (p,text)=>((IProgress<Tuple<int,string>>)ui).Report(Tuple.Create(p,text));
        }
        async Task Reader()
        {
            var drive=volumes.SelectedItem as NpSdCardTest.Volume;if(drive==null){status.Text="Select a mounted card reader.";return;}
            long bytes=new long[]{1,4,16,32}[size.SelectedIndex]*1073741824;
            await Execute(async()=>{var token=cancellation.Token;var ui=Progress();result=null;
                if(!NpSdCardTest.Volumes().Any(v=>v.Root==drive.Root))throw new IOException("The selected removable volume is no longer available.");
                result=await Task.Run(()=>NpSdCardTest.Run(drive.Root,bytes,token,ui));SaveResult();});
        }
        async Task Record()
        {
            uint duration=(uint)seconds.Value;
            await Execute(async()=>{result=null;result=await logger.TestSdRecording(duration,cancellation.Token,Progress());loggerResult=result;SaveResult();});
        }
        async Task Verify()
        {
            var target=usb.SelectedItem as UsbTarget;if(target==null){status.Text="Select the same logger in USB download mode.";return;}
            if(loggerResult==null)return;
            if(logger!=null&&logger.SdTestConnected){status.Text="Use Switch logger to USB before readback.";return;}
            await Execute(async()=>{
                result=loggerResult;result.Error=null;result.Status="USB readback pending";result.VerifiedBytes=0;string folder=NewReportFolder();result.ReportFolder=folder;
                string export=Path.Combine(folder,"readback");var token=cancellation.Token;var ui=Progress();
                long free=new DriveInfo(Path.GetPathRoot(folder)).AvailableFreeSpace;
                if(free<result.WrittenBytes*2+512L*1024*1024)throw new IOException("Not enough local disk space for strict readback decoding.");
                if(!String.IsNullOrEmpty(result.DeviceUid)&&!String.Equals(result.DeviceUid,NpV1Management.UidFromPath(target.Path)??NpV2FirmwareForm.Uid(target.Path),StringComparison.OrdinalIgnoreCase))throw new IOException("Select the MCU UID used in the BLE test.");
                string path=target.Path;
                if(NpV1Management.UidFromPath(path)!=null)path=await Task.Run(()=>NpV1Management.EnsureDownload(path,()=>token.IsCancellationRequested));
                else path=await NpV2FirmwareForm.EnsureDownload(path,()=>token.IsCancellationRequested);
                try{await Task.Run(()=>NpSdLoggerTest.Verify(path,result,export,token,ui));}
                finally
                {
                    // Export created this fresh directory. Preserve reports/metadata,
                    // remove only its generated neural/timestamp DAT test outputs.
                    if(Directory.Exists(export))foreach(string file in Directory.GetFiles(export,"*.dat",SearchOption.AllDirectories))try{File.Delete(file);}catch(IOException ex){result.CleanupWarning=ex.Message;}
                }
                SaveResult();
            });
        }
        static string NewReportFolder()
        {
            string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WILDX_console","SdTests",DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);return folder;
        }
        void SaveResult()
        {
            if(result==null)return;
            try{
                if(result.ReportFolder==null)result.ReportFolder=NewReportFolder();
                reportPath=Path.Combine(result.ReportFolder,"sd-test.json");
                File.WriteAllText(reportPath,new JavaScriptSerializer().Serialize(result));
            }
            catch(Exception ex){reportPath=null;output.Text=result.Summary()+"\r\n\r\nCould not save report: "+ex.Message;status.Text=result.Status;return;}
            output.Text=result.Summary()+"\r\n\r\nReport: "+reportPath;status.Text=result.Status;
            if(result.Status.StartsWith("Passed")||result.Status.StartsWith("Stored checks"))progress.Value=100;
        }
    }
}
