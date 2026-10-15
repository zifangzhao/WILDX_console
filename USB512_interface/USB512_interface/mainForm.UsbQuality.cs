using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace USB512_interface {
    public partial class Form1 {
        Button usbQualityTestButton;
        CheckBox usbQualityContinuous;
        bool usbQualityRunning;
        volatile bool usbQualityCancel;
        string usbQualityPath,usbQualityStatus,lastUsbQualityReport;
        bool CanTestUsb {get{return NpControlsAvailable && (mediaPreview==null || mediaPreview.IsDisposed);}}
        async Task TestUsbQualityAsync() {
            if(usbQualityRunning){usbQualityCancel=true;usbQualityStatus="Stopping USB test after the current transfer…";RefreshRecordingSummary(DateTime.UtcNow);return;}
            if(!CanTestUsb)return;
            string path=devicePath;bool continuous=usbQualityContinuous.Checked;usbQualityPath=path;usbQualityCancel=false;usbQualityRunning=true;lastUsbQualityReport=null;
            firmwareManagementActive=true;usbQualityStatus="USB test: checking the selected device…";usbQualityTestButton.Text="Stop USB test";UpdateStartEnabled();RefreshRecordingSummary(DateTime.UtcNow);
            var watch=Stopwatch.StartNew();
            var progress=new Progress<long>(bytes=>{if(!usbQualityRunning)return;TimeSpan elapsed=watch.Elapsed;usbQualityStatus="USB test"+(continuous?" (continuous)":"")+" | "+((long)elapsed.TotalHours).ToString("00")+":"+elapsed.Minutes.ToString("00")+":"+elapsed.Seconds.ToString("00")+" | "+(bytes/1048576.0).ToString("F1")+" MiB validated…";RefreshRecordingSummary(DateTime.UtcNow);});
            try {
                var result=await Task.Run(()=>{
                    using(var stream=new CE32_console.NpDownloadStream(path,true,true))
                        return stream.TestUsbQuality(()=>usbQualityCancel,bytes=>((IProgress<long>)progress).Report(bytes),continuous);
                });
                usbQualityStatus=result.Summary;lastUsbQualityReport=result.Report+"\r\n"+UsbHealth.DiagnosticReport(path);
            }catch(Exception ex){usbQualityStatus="USB test unavailable: "+ex.Message;var native=ex as Win32Exception;lastUsbQualityReport=usbQualityStatus+"\r\n\r\n"+(native==null?"":"Windows error "+native.NativeErrorCode+"\r\n")+ex+"\r\n"+UsbHealth.DiagnosticReport(path);}
            finally{usbQualityRunning=false;firmwareManagementActive=false;usbQualityTestButton.Text="USB quality test…";UpdateStartEnabled();RefreshRecordingSummary(DateTime.UtcNow);}
            if(!continuous)using(var report=new NpReportForm("USB quality test",lastUsbQualityReport))report.ShowDialog(this);
        }
        string CurrentUsbHealthSummary {get{return usbQualityPath==devicePath&&usbQualityStatus!=null?usbQualityStatus:UsbHealth.Summary(devicePath);}}
        string CurrentUsbHealthReport {get{return usbQualityPath==devicePath?(usbQualityRunning?UsbHealth.DiagnosticReport(devicePath):lastUsbQualityReport??UsbHealth.Report(devicePath)):UsbHealth.Report(devicePath);}}
    }
}
