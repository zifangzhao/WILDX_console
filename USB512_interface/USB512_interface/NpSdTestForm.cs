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
        readonly ComboBox volumes=Choice(),usb=Choice(),size=Choice();
        readonly CheckBox[] probes=Enumerable.Range(1,3).Select(p=>new CheckBox {Text="Probe "+p,AutoSize=true,Checked=p==1}).ToArray();
        readonly NumericUpDown seconds=new NumericUpDown {Minimum=10,Maximum=120,Value=60,Width=80};
        readonly TabControl tabs=new TabControl {Dock=DockStyle.Fill};
        readonly TextBox output=new TextBox {Dock=DockStyle.Fill,ReadOnly=true,Multiline=true,ScrollBars=ScrollBars.Vertical};
        readonly TextBox reportRoot=new TextBox {Dock=DockStyle.Fill};
        readonly ListView rates=new ListView {Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true,GridLines=true,HideSelection=false,ShowItemToolTips=true};
        readonly ProgressBar progress=new ProgressBar {Dock=DockStyle.Fill,Height=18};
        readonly Label status=new Label {Dock=DockStyle.Fill,AutoSize=false,AutoEllipsis=true,Height=44,MinimumSize=new Size(0,44)};
        readonly Label cardInfo=new Label {AutoSize=true,Dock=DockStyle.Fill};
        readonly Label order=new Label {AutoSize=true,Dock=DockStyle.Fill};
        readonly Label gapDetails=new Label {AutoSize=false,AutoEllipsis=true,Height=46,Dock=DockStyle.Fill,Text="Select a tested rate for write-pause and saved-data gap statistics."};
        readonly Button cancel=new Button {Text="Cancel test",AutoSize=true,Enabled=false};
        readonly Button start=new Button {Text="Find supported rate",AutoSize=true,Enabled=false};
        readonly Button save=new Button {Text="Save report…",AutoSize=true,Enabled=false};
        CancellationTokenSource cancellation;
        NpSdTestResult readerResult;
        NpSdUsbSweepResult sweepResult;
        NpSdUsbTestReport inspected;
        string inspectedPath,reportPath;
        bool discovering;
        internal NpSdTestForm(NpBleForm ble=null,string selectedUsb=null)
        {
            Text="SD card test";ClientSize=new Size(940,650);MinimumSize=new Size(780,560);
            StartPosition=FormStartPosition.CenterParent;Padding=new Padding(14);
            reportRoot.Text=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WILDX_console","SdTests");
            var onboard=new TabPage("USB logger");var reader=new TabPage("PC card reader");tabs.TabPages.AddRange(new[]{onboard,reader});
            var loggerRows=Table();loggerRows.RowCount=8;
            for(int i=0;i<8;i++)loggerRows.RowStyles.Add(new RowStyle(i==5?SizeType.Percent:SizeType.AutoSize,i==5?100:0));
            onboard.Controls.Add(loggerRows);
            loggerRows.Controls.Add(new Label {Text="V1 / V2 USB logger",AutoSize=true},0,0);
            var refreshUsb=new Button {Text="Refresh USB",AutoSize=true};refreshUsb.Click+=async(s,e)=>await Discover(selectedUsb);
            var inspect=new Button {Text="Read card",AutoSize=true};inspect.Click+=async(s,e)=>await Inspect();
            usb.Dock=DockStyle.Fill;
            var source=Table();source.ColumnCount=3;source.ColumnStyles.Clear();source.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));source.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));source.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            source.Controls.Add(usb,0,0);source.Controls.Add(refreshUsb,1,0);source.Controls.Add(inspect,2,0);loggerRows.Controls.Add(source,0,1);
            loggerRows.Controls.Add(cardInfo,0,2);
            loggerRows.Controls.Add(Line(probes.Cast<Control>().Concat(new Control[]{new Label {Text="Seconds per rate",AutoSize=true,Padding=new Padding(15,7,2,0)},seconds,start}).ToArray()),0,3);
            loggerRows.Controls.Add(order,0,4);
            rates.Columns.Add("Rate",90);rates.Columns.Add("SD rate",105);rates.Columns.Add("Max write",90);rates.Columns.Add("Gaps",80);rates.Columns.Add("Max NP gap",100);rates.Columns.Add("Result",330);loggerRows.Controls.Add(rates,0,5);
            rates.ClientSizeChanged+=(s,e)=>{rates.Columns[5].Width=Math.Max(180,rates.ClientSize.Width-rates.Columns.Cast<ColumnHeader>().Take(5).Sum(c=>c.Width)-SystemInformation.VerticalScrollBarWidth-4);};
            rates.SelectedIndexChanged+=(s,e)=>{if(rates.SelectedItems.Count==1&&sweepResult!=null){var item=sweepResult.Profiles.FirstOrDefault(p=>p.SampleHz==(uint)rates.SelectedItems[0].Tag);if(item!=null)gapDetails.Text=GapDescription(item.Gaps);}};
            var statistics=new Button {Text="Statistics…",AutoSize=true};statistics.Click+=(s,e)=>MessageBox.Show(this,gapDetails.Text,"SD test statistics",MessageBoxButtons.OK,MessageBoxIcon.Information);
            var details=Table();details.ColumnCount=2;details.ColumnStyles.Clear();details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));details.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));details.Controls.Add(gapDetails,0,0);details.Controls.Add(statistics,1,0);loggerRows.Controls.Add(details,0,6);
            loggerRows.Controls.Add(new Label {AutoSize=true,Dock=DockStyle.Fill,Text="Test recordings stay on SD. Readback is required for a pass. Cancel waits for Stop. — unavailable."},0,7);
            foreach(var probe in probes)probe.CheckedChanged+=(s,e)=>UpdateRates();
            usb.SelectedIndexChanged+=async(s,e)=>{if(!discovering&&cancellation==null)await Inspect();};
            start.Click+=async(s,e)=>await Logger();
            var readerRows=Table();readerRows.RowCount=5;
            for(int i=0;i<5;i++)readerRows.RowStyles.Add(new RowStyle(i==4?SizeType.Percent:SizeType.AutoSize,i==4?100:0));
            reader.Controls.Add(readerRows);volumes.Dock=DockStyle.Fill;
            size.Width=100;size.Items.AddRange(new object[]{"1 GiB","4 GiB","16 GiB","32 GiB"});size.SelectedIndex=1;
            var refresh=new Button {Text="Refresh readers",AutoSize=true};refresh.Click+=async(s,e)=>await Discover(selectedUsb);
            var runReader=new Button {Text="Test reader",AutoSize=true};runReader.Click+=async(s,e)=>await Reader();
            readerRows.Controls.Add(new Label {Text="Mounted SD card / USB reader",AutoSize=true},0,0);readerRows.Controls.Add(volumes,0,1);
            readerRows.Controls.Add(Line(new Label {Text="Test size",AutoSize=true,Padding=new Padding(0,7,4,0)},size,refresh,runReader),0,2);
            readerRows.Controls.Add(new Label {AutoSize=true,Dock=DockStyle.Fill,Text="Writes and verifies temporary files in free space, then removes only those files. This measures the reader path; use USB logger to test actual recording support."},0,3);
            readerRows.Controls.Add(output,0,4);
            var browse=new Button {Text="Browse…",AutoSize=true};browse.Click+=(s,e)=>{using(var dialog=new FolderBrowserDialog {SelectedPath=reportRoot.Text,Description="SD test reports and temporary readback"})if(dialog.ShowDialog(this)==DialogResult.OK)reportRoot.Text=dialog.SelectedPath;};
            var destination=Table();destination.ColumnCount=3;destination.ColumnStyles.Clear();destination.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));destination.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));destination.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            destination.Controls.Add(new Label {Text="Report folder",AutoSize=true,Padding=new Padding(0,7,6,0)},0,0);destination.Controls.Add(reportRoot,1,0);destination.Controls.Add(browse,2,0);
            cancel.Click+=(s,e)=>{cancellation?.Cancel();status.Text="Cancel requested; waiting for the finite recording or current readback to finish safely.";};
            save.Click+=(s,e)=>{if(reportPath==null)return;using(var file=new SaveFileDialog {Filter="SD test report|*.json",FileName="sd-test.json"})if(file.ShowDialog(this)==DialogResult.OK)try{File.Copy(reportPath,file.FileName,true);}catch(Exception ex){status.Text=ex.Message;}};
            var layout=Table();layout.RowCount=5;layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));for(int i=1;i<5;i++)layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(tabs,0,0);layout.Controls.Add(destination,0,1);layout.Controls.Add(Line(cancel,save),0,2);layout.Controls.Add(progress,0,3);layout.Controls.Add(status,0,4);Controls.Add(layout);
            WildXTheme.Apply(this);status.ForeColor=WildXTheme.Muted;cardInfo.ForeColor=WildXTheme.Muted;order.ForeColor=WildXTheme.Muted;
            Shown+=async(s,e)=>await Discover(selectedUsb);
            FormClosing+=(s,e)=>{if(cancellation!=null){cancellation.Cancel();e.Cancel=true;status.Text="Waiting for the active test to finish safely before closing.";}};
            UpdateRates();
        }
        static ComboBox Choice(){return new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Width=460};}
        static TableLayoutPanel Table(){var table=new TableLayoutPanel {Dock=DockStyle.Fill,AutoSize=true,ColumnCount=1,Padding=new Padding(6),Margin=Padding.Empty};table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));return table;}
        static FlowLayoutPanel Line(params Control[] controls){var p=new FlowLayoutPanel {AutoSize=true,Dock=DockStyle.Fill,WrapContents=true,Margin=Padding.Empty};p.Controls.AddRange(controls);return p;}
        uint Mask {get{uint value=0;for(int p=0;p<probes.Length;p++)if(probes[p].Checked&&probes[p].Enabled)value|=1U<<p;return value;}}
        void UpdateRates()
        {
            if(cancellation!=null)return;rates.Items.Clear();
            var available=inspected==null||Mask==0||inspected.MaximumSeconds<10?new uint[0]:NpSdSweepPolicy.Rates(inspected.Family,Mask,hz=>inspected.Supports(Mask,hz));
            foreach(uint hz in available)rates.Items.Add(new ListViewItem(new[]{hz.ToString("N0")+" Hz",(NpSdSweepPolicy.PhysicalBytesPerSecond(inspected.Family,Mask,hz)/1000000m).ToString("F2")+" MB/s","—","—","—","Not tested"}) {Tag=hz});
            order.Text=available.Length==0?"Select a USB logger, read its card, then choose the connected probes.":"Test order: "+String.Join(" → ",available.Select(hz=>(hz/1000m).ToString("0.#")+" kHz"))+" · stop at the first verified pass";
            start.Enabled=available.Length>0;
        }
        async Task Discover(string path)
        {
            if(cancellation!=null||discovering)return;bool foundTargets=false;
            await Execute(async()=>{
                discovering=true;
                try{
                status.Text="Looking for USB loggers and mounted card readers…";
                string prior=(volumes.SelectedItem as NpSdCardTest.Volume)?.Root;
                string old=(usb.SelectedItem as UsbTarget)?.Path??path;
                var found=await Task.Run(()=>new {Volumes=NpSdCardTest.Volumes(),Usb=NpDownloadNative.EnumerateNpPaths().ToArray()});
                cancellation.Token.ThrowIfCancellationRequested();
                volumes.Items.Clear();foreach(var item in found.Volumes)volumes.Items.Add(item);
                volumes.SelectedItem=volumes.Items.Cast<NpSdCardTest.Volume>().FirstOrDefault(v=>v.Root==prior);
                if(volumes.SelectedIndex<0&&volumes.Items.Count==1)volumes.SelectedIndex=0;
                usb.Items.Clear();foreach(string item in found.Usb)usb.Items.Add(new UsbTarget {Path=item});
                usb.SelectedItem=usb.Items.Cast<UsbTarget>().FirstOrDefault(t=>NpDownloadForm.SameSource(t.Path,old));
                if(usb.SelectedIndex<0&&usb.Items.Count==1)usb.SelectedIndex=0;
                foundTargets=true;
                }finally{discovering=false;}
            });
            if(foundTargets)await Inspect();
        }
        async Task Inspect()
        {
            if(cancellation!=null)return;var target=usb.SelectedItem as UsbTarget;
            inspected=null;sweepResult=null;start.Enabled=false;
            if(target==null){cardInfo.Text="Select the USB logger to test. BLE is not required.";UpdateRates();return;}
            await Execute(async()=>{
                status.Text="Reading the selected logger and card…";
                ApplyInspection(await new NpSdUsbTestController(target.Path).Inspect(cancellation.Token),target.Path);
                status.Text="Ready. Choose the connected probes and start the test.";
            });
        }
        internal void ApplyInspection(NpSdUsbTestReport report,string path)
        {
            if(report.MaximumSeconds<10){inspected=null;UpdateRates();throw new NotSupportedException("The installed firmware does not support a 10-second SD test.");}
            inspected=report;inspectedPath=path;
            uint selected=report.PriorMask??1U;
            for(int p=0;p<probes.Length;p++){
                bool supported=false;for(uint mask=1;mask<=7;mask++)if((mask&(1U<<p))!=0&&((report.SupportedRates>>((int)mask*4))&15U)!=0)supported=true;
                probes[p].Enabled=supported;probes[p].Checked=(selected&(1U<<p))!=0;
            }
            seconds.Maximum=Math.Min(120,report.MaximumSeconds);
            cardInfo.Text="Neuropixels V"+report.Family+" · "+report.Uid+" · "+(report.Capacity.HasValue?(report.Capacity.Value*512.0/1073741824).ToString("F1")+" GiB":"card identity unavailable");
            UpdateRates();
        }
        async Task Execute(Func<Task> action)
        {
            if(cancellation!=null)return;cancellation=new CancellationTokenSource();tabs.Enabled=false;reportRoot.Enabled=false;cancel.Enabled=true;save.Enabled=false;progress.Value=0;
            try{await action();}
            catch(OperationCanceledException){status.Text="Cancelled — not passed.";}
            catch(Exception ex){status.Text=ex.Message;}
            finally{cancellation.Dispose();cancellation=null;tabs.Enabled=true;reportRoot.Enabled=true;cancel.Enabled=false;save.Enabled=reportPath!=null;if(sweepResult==null)UpdateRates();else start.Enabled=inspected!=null&&Mask!=0;}
        }
        Action<int,string> Progress()
        {
            var ui=new Progress<Tuple<int,string>>(p=>{progress.Value=Math.Max(0,Math.Min(100,p.Item1));status.Text=p.Item2;});
            return (p,text)=>((IProgress<Tuple<int,string>>)ui).Report(Tuple.Create(p,text));
        }
        async Task Logger()
        {
            var target=usb.SelectedItem as UsbTarget;
            if(target==null||inspected==null||!NpDownloadForm.SameSource(target.Path,inspectedPath)){status.Text="Read the selected logger's card before testing.";return;}
            uint mask=Mask,duration=(uint)seconds.Value;
            if(mask==0){status.Text="Select at least one connected probe.";return;}
            string folder;
            try{folder=Path.GetFullPath(reportRoot.Text.Trim());Directory.CreateDirectory(folder);}catch(Exception ex){status.Text="Report folder: "+ex.Message;return;}
            sweepResult=null;reportPath=null;UpdateRates();
            await Execute(async()=>{
                var ui=new Progress<NpSdUsbProfileResult>(ApplyProfileResult);
                sweepResult=await new NpSdUsbTestController(target.Path).Run(mask,duration,folder,cancellation.Token,Progress(),p=>((IProgress<NpSdUsbProfileResult>)ui).Report(p));
                foreach(var profile in sweepResult.Profiles)ApplyProfileResult(profile);
                reportPath=Path.Combine(sweepResult.ReportFolder,"sd-test.json");
                status.Text=sweepResult.Status+(sweepResult.Error==null?"":" · "+sweepResult.Error)+"\r\nReport: "+reportPath;
                if(sweepResult.SupportedSampleHz.HasValue)progress.Value=100;
            });
        }
        internal void ApplyProfileResult(NpSdUsbProfileResult profile)
        {
            var row=rates.Items.Cast<ListViewItem>().FirstOrDefault(r=>(uint)r.Tag==profile.SampleHz);
            if(row==null)return;
            var gaps=profile.Gaps;
            row.SubItems[2].Text=Ms(gaps.CardMaximumWriteMs);
            row.SubItems[3].Text=gaps.NeuralGapCount.HasValue?gaps.NeuralGapCount.Value.ToString("N0"):gaps.V1LossEventsLowerBound.HasValue?"≥"+gaps.V1LossEventsLowerBound.Value.ToString("N0"):"—";
            row.SubItems[4].Text=Ms(gaps.NeuralMaximumGapMs);
            row.SubItems[5].Text=profile.Status+(String.IsNullOrEmpty(profile.Error)?"":" · "+profile.Error);
            row.ToolTipText=GapDescription(gaps);gapDetails.Text=row.ToolTipText;
        }
        static string Ms(double? value){return value.HasValue?value.Value.ToString("0.###")+" ms":"—";}
        static string Ms(decimal? value){return value.HasValue?value.Value.ToString("0.###")+" ms":"—";}
        internal static string GapDescription(NpSdUsbGapStats gaps)
        {
            if(gaps==null)return "Write-pause and saved-data gap statistics are not available.";
            string text="SD write · max "+Ms(gaps.CardMaximumWriteMs)+" · mean "+Ms(gaps.CardMeanWriteMs)+" · recorder service gap "+Ms(gaps.MaximumServiceMs);
            if(gaps.NeuralDistributionKnown)text+="\r\nSaved NP gaps · "+gaps.NeuralGapCount+" events · total "+Ms(gaps.NeuralTotalMissingMs)+" · max "+Ms(gaps.NeuralMaximumGapMs)+" · mean "+Ms(gaps.NeuralMeanGapMs)+" · p95 "+Ms(gaps.NeuralP95GapMs)+" · p99 "+Ms(gaps.NeuralP99GapMs);
            else text+="\r\nSaved NP gap durations: not reported"+(gaps.V1LossEventsLowerBound.HasValue?" · at least "+gaps.V1LossEventsLowerBound+" loss events":"");
            if(gaps.V1SkippedLaneBytesLowerBound!=null)text+=" · skipped lane bytes (lower bounds): "+String.Join(" / ",gaps.V1SkippedLaneBytesLowerBound);
            if(gaps.DeviceSourceGaps.HasValue||gaps.DeviceMissingTicks.HasValue||gaps.DeviceDroppedBlocks.HasValue||gaps.DeviceOverflows.HasValue)
                text+="\r\nDevice counters · gaps "+(gaps.DeviceSourceGaps?.ToString("N0")??"—")+" · missing ticks "+(gaps.DeviceMissingTicks?.ToString("N0")??"—")+" · dropped blocks "+(gaps.DeviceDroppedBlocks?.ToString("N0")??"—")+" · overflows "+(gaps.DeviceOverflows?.ToString("N0")??"—");
            if(gaps.MissingStoredBlocks.HasValue)text+="\r\nMissing stored blocks: "+gaps.MissingStoredBlocks+" · storage breaks: "+gaps.StorageBreaks;
            if(!String.IsNullOrEmpty(gaps.NeuralGapError))text+="\r\n"+gaps.NeuralGapError;
            return text;
        }
        async Task Reader()
        {
            var drive=volumes.SelectedItem as NpSdCardTest.Volume;if(drive==null){status.Text="Select a mounted card reader.";return;}
            long bytes=new long[]{1,4,16,32}[size.SelectedIndex]*1073741824;
            await Execute(async()=>{
                var token=cancellation.Token;var ui=Progress();readerResult=null;
                if(!NpSdCardTest.Volumes().Any(v=>v.Root==drive.Root))throw new IOException("The selected removable volume is no longer available.");
                readerResult=await Task.Run(()=>NpSdCardTest.Run(drive.Root,bytes,token,ui));
                string folder=NpFolderNaming.CreateFresh(Path.GetFullPath(reportRoot.Text.Trim()),"reader-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                readerResult.ReportFolder=folder;reportPath=Path.Combine(folder,"sd-test.json");File.WriteAllText(reportPath,new JavaScriptSerializer().Serialize(readerResult));
                output.Text=readerResult.Summary()+"\r\n\r\nReport: "+reportPath;status.Text=readerResult.Status;
                if(readerResult.Status.StartsWith("Passed"))progress.Value=100;
            });
        }
    }
}
