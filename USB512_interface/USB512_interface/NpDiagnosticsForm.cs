using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace USB512_interface
{
    internal sealed class NpReportForm : Form
    {
        internal NpReportForm(string title,string report)
        {
            Text=title;ClientSize=new Size(850,560);StartPosition=FormStartPosition.CenterParent;
            var text=new TextBox {Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false,Text=report};
            var actions=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=48,Padding=new Padding(8)};
            var save=new Button {Text="Save report…",AutoSize=true};
            save.Click+=delegate {using(var file=new SaveFileDialog {Filter="Text report|*.txt",FileName="neuropixels-report.txt"})if(file.ShowDialog(this)==DialogResult.OK)
                try{File.WriteAllText(file.FileName,text.Text);}catch(Exception ex){MessageBox.Show(this,ex.Message,"Report",MessageBoxButtons.OK,MessageBoxIcon.Error);}};
            var close=new Button {Text="Close",AutoSize=true,DialogResult=DialogResult.OK};actions.Controls.AddRange(new Control[]{save,close});
            Controls.Add(text);Controls.Add(actions);CancelButton=close;WildXTheme.Apply(this);
        }
        internal static string CaptureText(string device,string path,string outcome,string folder)
        {
            var report=new StringBuilder();report.AppendLine("Neuropixels capture report");report.AppendLine("UTC: "+DateTime.UtcNow.ToString("O"));
            report.AppendLine("Device: "+device);report.AppendLine("USB interface: "+path);report.AppendLine("Outcome: "+outcome);report.AppendLine("Folder: "+folder);
            // Keep native firmware fields and timing evidence; do not infer an
            // overall pass from missing/optional records or repair the stream.
            if(!String.IsNullOrEmpty(folder))foreach(string name in new[]{"neuropixels_capture_status.txt","neuropixels_np2usb_status.txt","np2_merge_status.txt","np2_merge_failure.txt","metadata_save_error.txt","requested-configuration-applied-receipts.txt"}){
                string file=Path.Combine(folder,name);if(!File.Exists(file))continue;
                report.AppendLine();report.AppendLine(name);
                try{if(new FileInfo(file).Length<=1024*1024)report.AppendLine(File.ReadAllText(file));else report.AppendLine("See the full report in the recording folder.");}
                catch(IOException ex){report.AppendLine("Read unavailable: "+ex.Message);}
            }
            return report.ToString();
        }
    }

    internal sealed class NpDiagnosticsForm : Form
    {
        readonly string path;
        readonly bool np2;
        readonly TextBox report=new TextBox {Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical};
        readonly FlowLayoutPanel actions=new FlowLayoutPanel {Dock=DockStyle.Top,AutoSize=true,Padding=new Padding(12)};
        readonly Label outcome=new Label {Dock=DockStyle.Top,Height=64,Padding=new Padding(12)};
        bool busy;
        internal NpDiagnosticsForm(string devicePath,bool v2)
        {
            path=devicePath;np2=v2;Text="Diagnostics & self-test";ClientSize=new Size(870,580);StartPosition=FormStartPosition.CenterParent;
            Add("Read device info",async()=>{report.Text=await Task.Run(()=>NpSystemInfo.Read(path,np2?"NP V2":"NP V1").Report(typeof(Form1).Assembly.GetName().Version.ToString()));});
            Add("Read self-test report",()=>Test(null),np2);
            Add("Run + hold self-test",()=>Test(1U),np2);Add("Release rails",()=>Test(4U),np2);
            Add("Hold rails",()=>Test(2U),np2);Add("SD 1.8 V",()=>Test(3U),np2);Add("SD 3.3 V",()=>Test(5U),np2);
            var save=new Button {Text="Save report…",AutoSize=true};save.Click+=delegate {using(var dialog=new NpReportForm(Text,outcome.Text+"\r\n"+report.Text))dialog.ShowDialog(this);};
            var close=new Button {Text="Close",AutoSize=true};close.Click+=delegate {Close();};actions.Controls.AddRange(new Control[]{save,close});
            outcome.Text=np2?"Power self-test requires the idle USB streaming service. External probe / ADC / camera / BLE checks can be skipped; skipped checks are not a pass.":
                "This V1 firmware has no compatible powered self-test command. Read device info for passive diagnostics.";
            Controls.Add(report);Controls.Add(outcome);Controls.Add(actions);WildXTheme.Apply(this);outcome.ForeColor=WildXTheme.Muted;
            FormClosing+=delegate(object sender,FormClosingEventArgs e){if(busy)e.Cancel=true;};
        }
        void Add(string title,Func<Task> action,bool supported=true)
        {
            var button=new Button {Text=title,AutoSize=true,Enabled=supported};actions.Controls.Add(button);
            button.Click+=async delegate {
                if(busy || !supported)return;busy=true;actions.Enabled=false;
                try{await action();}catch(Exception ex){outcome.Text="Not confirmed: "+ex.Message;report.AppendText("\r\n"+ex.Message);}
                finally{busy=false;actions.Enabled=true;}
            };
        }
        async Task Test(uint? command)
        {
            if(!np2)throw new IOException("The device does not support this self-test protocol.");
            string uid=NpV2FirmwareForm.Uid(path);if(uid==null)throw new IOException("Exact MCU UID required.");
            var result=await Task.Run(()=>{
                using(var device=NpV2UsbDevice.Open(path)){
                    if(!command.HasValue)return device.GetSelfTestReport(uid);
                    uint baseline=device.StartSelfTest(command.Value,uid);var watch=Stopwatch.StartNew();
                    while(watch.Elapsed.TotalSeconds<20){var next=device.GetSelfTestReport(uid);if(next.IsNewTerminalFor(command.Value,baseline))return next;Thread.Sleep(200);}
                    throw new TimeoutException("No new terminal self-test report within 20 seconds. No retry or automatic release was sent; a requested hold may remain active.");
                }
            });
            report.AppendText(DateTime.UtcNow.ToString("O")+" UID "+uid+"\r\n"+result.Text()+"\r\n");
            outcome.Text=result.State==NpV2UsbSelfTestReport.Rejected || result.Error!=0?"Self-test rejected or failed":
                result.State==NpV2UsbSelfTestReport.Queued || result.State==NpV2UsbSelfTestReport.Running?"Self-test in progress":
                result.Checked==0 && result.Skipped==0?"No checks run":result.Failed!=0?"Failed checks":result.Skipped!=0?"Skipped checks; no overall pass":"No failed or skipped checks";
            outcome.Text+=". "+(result.Holding?"Power hold active. Release rails when finished.":"Reboot into USB streaming before recording.");
        }
    }
}
