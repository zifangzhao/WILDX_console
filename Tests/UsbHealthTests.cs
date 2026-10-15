using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

static class UsbHealthTests {
    const BindingFlags All=BindingFlags.Static|BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    static Assembly app;static Type health;static int checks;
    static object Call(string method,params object[] args){try{return health.GetMethod(method,All).Invoke(null,args);}catch(TargetInvocationException ex){throw ex.InnerException;}}
    static object Field(object value,string name){return value.GetType().GetField(name,All).GetValue(value);}
    static long N(object value,string name){return (long)Field(value,name);}
    static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static object[] Rows(string path){return ((Array)Call("Snapshots",path)).Cast<object>().ToArray();}
    static object Begin(IntPtr h,byte pipe,long now){return Call("Begin",h,pipe,now);}
    static void Submit(IntPtr h,object attempt,IntPtr ov,bool ok,int error,uint bytes,bool deferred,long now){Call("Submitted",h,attempt,ov,ok,error,bytes,deferred,now);}
    static void Complete(IntPtr h,IntPtr ov,bool ok,int error,uint bytes,long now){Call("Completed",h,ov,ok,error,bytes,now);}
    static void Accounting() {
        IntPtr h=new IntPtr(100),ov=new IntPtr(200);const string path="usb-health-fixture";
        Call("Open",h,path,(byte?)3,0);Check((string)Call("SpeedName",(byte?)3)=="High speed or higher"&&(string)Call("SpeedName",(byte?)1)=="Low speed"&&(string)Call("SpeedName",(byte?)2)=="Full speed"&&((string)Call("SpeedName",(byte?)4)).Contains("unavailable"),"SDK WinUSB speed mapping");
        var a=Begin(h,0x81,100);Submit(h,a,IntPtr.Zero,true,0,100U,false,200);
        a=Begin(h,0x81,210);Submit(h,a,ov,false,997,0U,false,211);
        for(int i=0;i<20;i++)Complete(h,ov,false,996,0U,220+i);
        object p=Rows(path).Single();Check(N(p,"Success")==1&&N(p,"Failed")==0&&N(p,"Pending")==1,"Pending polls inflated completed/failure counters");
        Complete(h,ov,true,0,200U,400);Complete(h,ov,true,0,200U,401);
        p=Rows(path).Single();Check(N(p,"Success")==2&&N(p,"Bytes")==300&&N(p,"Pending")==0,"Completion counted twice");
        a=Begin(h,0x81,410);Submit(h,a,ov,true,0,0U,true,411);Check(N(Rows(path).Single(),"Success")==2,"Deferred immediate completion counted before byte result");Complete(h,ov,true,0,300U,500);
        a=Begin(h,0x81,510);Submit(h,a,IntPtr.Zero,false,121,99U,false,600);
        p=Rows(path).Single();Check(N(p,"Failed")==1&&N(p,"Bytes")==600,"Failed transfer bytes included");Check((double)p.GetType().GetProperty("FailureRate",All).GetValue(p,null)==0.25,"Wrong failure denominator");
        a=Begin(h,0x81,610);Submit(h,a,ov,false,997,0U,false,611);Call("Aborting",h,(byte)0x81);Complete(h,ov,false,995,0U,700);
        // Synchronous reads aborted on another thread also count as cancellation.
        a=Begin(h,0x81,710);Call("Aborting",h,(byte)0x81);Submit(h,a,IntPtr.Zero,false,995,0U,false,800);
        p=Rows(path).Single();Check(N(p,"Cancelled")==2&&N(p,"Failed")==1,"Intentional cancellation inflated faults");
        a=Begin(h,0x81,810);Submit(h,a,ov,false,997,0U,false,811);Complete(h,ov,false,995,0U,900);Check(N(Rows(path).Single(),"Failed")==2,"Unexpected abort hidden");
        foreach(int error in new[]{1167,31,23}){a=Begin(h,0x81,910);Submit(h,a,IntPtr.Zero,false,error,0U,false,1000);}
        var errors=(Dictionary<int,long>)Field(Rows(path).Single(),"Errors");Check(errors[121]==1&&errors[995]==1&&errors[1167]==1&&errors[31]==1&&errors[23]==1,"Native error categories/codes lost");
        Check((string)Call("Category",31)=="generic I/O (cause unspecified)","Generic error relabeled as CRC/stall");
        Call("Integrity",h,(byte)0x81,true,"fixture CRC");p=Rows(path).Single();Check(N(p,"Integrity")==1&&N(p,"Failed")==5,"Integrity failure added to transport denominator");
        Check(N(p,"DurationTicks")==830&&N(p,"MinDuration")==90&&N(p,"MaxDuration")==190,"Transfer-duration arithmetic");
        Check(N(p,"GapCount")==7&&N(p,"MaxGap")==300,"Observed completion-gap arithmetic");
        a=Begin(h,0x82,1010);Submit(h,a,ov,false,997,0U,false,1011);Call("Close",h);
        p=Rows(path).Single(row=>(byte)Field(row,"Pipe")==0x82);Check(N(p,"Unobserved")==1&&N(p,"Pending")==0&&N(p,"Failed")==0,"Unobserved close invented a fault/result");
        Call("Open",h,path,(byte?)null,50);a=Begin(h,0x81,1200);Submit(h,a,IntPtr.Zero,true,0,50U,false,1300);
        Check(Rows(path).Select(row=>N(row,"Session")).Distinct().Count()==2,"Reused native handle combined sessions");Check(Rows("other-device").Length==0,"Other device telemetry mixed in");
        string report=(string)Call("Report",path);Check(report.Contains("Stalls, controller transaction errors/retries")&&report.Contains("unavailable")&&report.Contains("Windows 121")&&report.Contains("0x00000079"),"Unavailable or native error reporting");
        Call("BeginRecording",path);Check(Rows(path).Length==0,"New recording retained previous counters");
        a=Begin(h,0x81,1400);Submit(h,a,ov,false,997,0U,false,1401);Call("BeginRecording",path);Complete(h,ov,true,0,100U,1500);Check(Rows(path).Length==0,"Pre-window pending transfer contaminated new recording");
        Call("Close",h);
        var native=app.GetType("USB512_interface.UsbHealthNative",true);native.GetMethod("Return",All).Invoke(null,new object[]{false,121});Check(Marshal.GetLastWin32Error()==121,"Telemetry did not restore native Windows error");
    }
    static bool Calls(MethodInfo method,string type,string name){byte[] il=method.GetMethodBody().GetILAsByteArray();for(int i=0;i<il.Length-4;i++)if(il[i]==0x28||il[i]==0x6f)try{var target=method.Module.ResolveMethod(BitConverter.ToInt32(il,i+1));if(target.DeclaringType.FullName==type&&target.Name==name)return true;}catch(ArgumentException){}return false;}
    static void Routing(){
        foreach(string type in new[]{"USB512_interface.USB512WinUsbNative","CE32_console.NpDownloadNative"})foreach(var pair in new[]{new[]{"WinUsbInitialize","Initialize"},new[]{"WinUsbFree","Free"},new[]{"WinUsbReadPipe","Read"},new[]{"WinUsbReadPipeOverlapped","ReadOverlapped"},new[]{"WinUsbGetOverlappedResult","Result"},new[]{"WinUsbAbortPipe","Abort"}})
            Check(Calls(app.GetType(type,true).GetMethod(pair[0],All),"USB512_interface.UsbHealthNative",pair[1]),"Transport bypassed health observer: "+type+"."+pair[0]);
        var device=app.GetTypes().Single(t=>t.Name=="NpV2UsbDevice");Check(Calls(device.GetMethod("ReadPipe",All),"USB512_interface.UsbHealthNative","ReadDeferred"),"V2 private overlapped reader bypassed observer");
    }
    static void Quality() {
        var runner=app.GetType("USB512_interface.UsbQualityTest",true).GetMethod("Run",All);int reads=0;
        Func<Func<int>,int,Func<bool>,Func<TimeSpan>,object> run=(read,block,cancel,time)=>runner.Invoke(null,new object[]{read,block,cancel,null,time,"software fixture",false});
        int blockBytes=1020*512;
        var result=run(()=>{reads++;return blockBytes;},blockBytes,()=>false,()=>TimeSpan.FromTicks(reads));
        Check((string)Field(result,"Outcome")=="completed"&&N(result,"Bytes")<=256L*1024*1024&&256L*1024*1024-N(result,"Bytes")<blockBytes,"USB test exceeded byte budget or stopped early");
        reads=0;result=run(()=>{reads++;return 512;},512,()=>false,()=>TimeSpan.FromSeconds(reads*4));Check(reads==3&&(string)Field(result,"Outcome")=="completed","USB test ignored time budget");
        reads=0;result=run(()=>{reads++;return 512;},512,()=>true,()=>TimeSpan.Zero);Check(reads==0&&(string)Field(result,"Outcome")=="cancelled","Pre-start cancellation read data");
        result=run(()=>{reads++;throw new System.IO.IOException("fixture transfer failure");},512,()=>false,()=>TimeSpan.Zero);Check(reads==1&&(string)Field(result,"Outcome")=="failed"&&N(result,"Bytes")==0,"Failed quality read retried or passed");
        result=run(()=>511,512,()=>false,()=>TimeSpan.Zero);Check((string)Field(result,"Outcome")=="failed","Short quality block passed");
        result=run(()=>{throw new TimeoutException("partial frame deadline");},512,()=>false,()=>TimeSpan.Zero);Check((string)Field(result,"Outcome")=="incomplete","Partial deadline was reported as a completed quality test");
        reads=0;result=runner.Invoke(null,new object[]{(Func<int>)(()=>{reads++;return blockBytes;}),blockBytes,(Func<bool>)(()=>reads>=600),null,(Func<TimeSpan>)(()=>TimeSpan.FromSeconds(reads)),"continuous fixture",true});
        Check(reads==600&&N(result,"Bytes")>256L*1024*1024&&(TimeSpan)Field(result,"Elapsed")>TimeSpan.FromSeconds(10),"Continuous test retained the bounded time/byte limits");
        Check((string)Field(result,"Outcome")=="stopped"&&(bool)Field(result,"Continuous"),"Manual continuous stop reported as fault/cancellation");
        reads=0;result=runner.Invoke(null,new object[]{(Func<int>)(()=>{if(++reads==3)throw new System.IO.InvalidDataException("continuous CRC fixture");return 512;}),512,(Func<bool>)(()=>false),null,(Func<TimeSpan>)(()=>TimeSpan.FromSeconds(reads)),"continuous fixture",true});
        Check(reads==3&&(string)Field(result,"Outcome")=="failed"&&N(result,"Blocks")==2,"Continuous integrity failure retried or lost validated prefix");
        var deadline=runner.DeclaringType.GetMethod("CheckDeadline",All);
        deadline.Invoke(null,new object[]{true,TimeSpan.FromHours(48),TimeSpan.FromSeconds(1)});Check(true,"Continuous healthy block received a session deadline");
        foreach(bool continuous in new[]{false,true}) {bool expired=false;try{deadline.Invoke(null,new object[]{continuous,TimeSpan.FromSeconds(16),continuous?TimeSpan.FromSeconds(16):TimeSpan.FromSeconds(1)});}catch(TargetInvocationException ex){expired=ex.InnerException is TimeoutException;}Check(expired,"USB test lost its per-block/session deadline");}
        IntPtr h=new IntPtr(400);const string path="usb-health-diagnostic-isolation";Call("Open",h,path,(byte?)3,0);Call("Diagnostic",h);var a=Begin(h,0x81,100);Submit(h,a,IntPtr.Zero,true,0,512U,false,200);
        Check(Rows(path).Length==0&&((string)Call("DiagnosticReport",path)).Contains("successful: 1"),"Diagnostic counts contaminated recording window or disappeared");Call("Close",h);
    }
    static void Admission(Form form,Type type) {
        var button=(Button)Descendants(form).Single(c=>c.Name=="usbQualityTest");Check(button.Parent==Descendants(form).Single(c=>c.Name=="usbHealthDetails").Parent,"Quality test action left recording info");
        var continuous=(CheckBox)Descendants(form).Single(c=>c.Name=="usbQualityContinuous");Check(continuous.Checked&&continuous.Parent==button.Parent.Parent,"Continuous option missing from the USB panel or not the default");
        var kind=type.GetField("selectedDeviceKind",All);kind.SetValue(form,Enum.Parse(kind.FieldType,"XeNp"));
        Func<bool> can=()=> (bool)type.GetProperty("CanTestUsb",All).GetValue(form,null);Check(can(),"Idle selected NP device not admitted");
        foreach(string name in new[]{"running","ztestRunning","systemInfoActive","deviceRefreshActive","firmwareManagementActive","npRecoveryCancelled"}) {
            var field=type.GetField(name,All);object old=field.GetValue(form);field.SetValue(form,field.FieldType==typeof(int)?(object)1:name=="npRecoveryCancelled"?(object)false:true);Check(!can(),"Quality test admitted during "+name);field.SetValue(form,old);
        }
        type.GetField("usbQualityRunning",All).SetValue(form,true);
        type.GetField("firmwareManagementActive",All).SetValue(form,true);type.GetMethod("UpdateStartEnabled",All).Invoke(form,null);
        Check(!((Button)type.GetField("btnStart",All).GetValue(form)).Enabled,"Recording start remained enabled during test");
        Check(!((ComboBox)type.GetField("comboDrives",All).GetValue(form)).Enabled,"Device selection remained enabled during test");
        Check(button.Enabled,"Running test cannot be stopped from its button");
        Check(!continuous.Enabled,"Test mode can change during an active run");
        ((System.Threading.Tasks.Task)type.GetMethod("TestUsbQualityAsync",All).Invoke(form,null)).GetAwaiter().GetResult();Check((bool)type.GetField("usbQualityCancel",All).GetValue(form),"Running test button did not request cancellation");
        type.GetField("usbQualityRunning",All).SetValue(form,false);type.GetField("firmwareManagementActive",All).SetValue(form,false);kind.SetValue(form,Enum.Parse(kind.FieldType,"NpV2"));Check(can(),"Idle V2 not admitted");
    }
    static IEnumerable<Control> Descendants(Control c){foreach(Control child in c.Controls){yield return child;foreach(var sub in Descendants(child))yield return sub;}}
    static void Ui(string output) {
        var type=app.GetType("USB512_interface.Form1",true);
        using(var form=(Form)Activator.CreateInstance(type)) {
            var label=(Label)type.GetField("usbHealthSummary",All).GetValue(form);var summary=(Label)type.GetField("recordingSummary",All).GetValue(form);
            var usbCard=Descendants(form).Single(c=>c.Name=="usbHealthCard");
            Check(label.Parent!=summary.Parent&&Descendants(usbCard).Contains(label),"USB info did not move to its own right-side panel");Check(Descendants(usbCard).Any(c=>c.Name=="usbHealthDetails"),"USB panel detail action missing");
            var split=(TableLayoutPanel)Descendants(form).Single(c=>c.Name=="captureActivitySplit");var capture=Descendants(form).Single(c=>c.Name=="captureActivityCard");
            Check(capture.Parent==split&&usbCard.Parent==split&&split.GetColumn(capture)==0&&split.GetColumn(usbCard)==1,"Capture activity was not split within its original cell");
            var online=(TableLayoutPanel)Descendants(form).Single(c=>c.Name=="usbLayout");var right=(TableLayoutPanel)online.GetControlFromPosition(1,0);
            Check(online.GetColumn(split)==0&&online.GetRow(split)==1&&right.RowCount==3&&!Descendants(right).Contains(usbCard),"USB card changed the existing right-column panels");
            IntPtr h=new IntPtr(300);const string path="usb-health-inline-fixture";Call("Open",h,path,(byte?)3,0);
            long tick=Stopwatch.GetTimestamp();var a=Begin(h,0x81,tick);Submit(h,a,IntPtr.Zero,true,0,1048576U,false,tick+Stopwatch.Frequency/2);
            a=Begin(h,0x81,tick+Stopwatch.Frequency/2);Submit(h,a,IntPtr.Zero,false,121,0U,false,tick+Stopwatch.Frequency);
            type.GetField("devicePath",All).SetValue(form,path);type.GetMethod("RefreshRecordingSummary",All).Invoke(form,new object[]{DateTime.UtcNow});
            Check(label.Text.Contains("1 OK / 1 failed")&&label.Text.Contains("50.000%"),"Inline summary not populated from selected target");
            Admission(form,type);type.GetField("usbQualityPath",All).SetValue(form,null);type.GetMethod("UpdateStartEnabled",All).Invoke(form,null);type.GetMethod("RefreshRecordingSummary",All).Invoke(form,new object[]{DateTime.UtcNow});
            // Only reparent controls to an offscreen host. Product Load/Shown
            // handlers that enumerate or connect devices are never invoked.
            using(var host=new Form{ClientSize=new Size(1044,741),ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-20000,-20000),Font=form.Font,BackColor=form.BackColor}) {
                foreach(var child in form.Controls.Cast<Control>().ToArray())host.Controls.Add(child);host.Show();Application.DoEvents();
                Check(label.Parent.ClientSize.Width>0&&label.Height>=label.GetPreferredSize(new Size(label.Width,0)).Height,"Inline health label clipped");
                Check(usbCard.PointToScreen(Point.Empty).X>summary.PointToScreen(Point.Empty).X,"USB panel is not on the right side");
                using(var bitmap=new Bitmap(host.Width,host.Height)){host.DrawToBitmap(bitmap,new Rectangle(Point.Empty,host.Size));bitmap.Save(output);}
                host.ClientSize=new Size(1180,920);Application.DoEvents();
                var time=Descendants(host).Single(c=>c.Name=="networkTimeCard");
                Check(time.Bottom<=time.Parent.ClientSize.Height&&usbCard.Bottom<=usbCard.Parent.ClientSize.Height,"Right-side cards overlap or leave the workspace at the default size");
                using(var bitmap=new Bitmap(host.Width,host.Height)){host.DrawToBitmap(bitmap,new Rectangle(Point.Empty,host.Size));bitmap.Save(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(output),"usb-panel-default.png"));}
            }
            Call("Close",h);
        }
    }
    [STAThread]static int Main(string[] args){try{app=Assembly.LoadFrom(args[0]);health=app.GetType("USB512_interface.UsbHealth",true);Accounting();Routing();Quality();Ui(args[1]);Console.WriteLine("PASS "+checks+" actual-assembly USB health/quality accounting, bounds, ownership, routing and UI checks; no USB enumeration, hardware I/O, or ETW session.");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
