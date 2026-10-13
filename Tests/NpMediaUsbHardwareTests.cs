using System;
using System.Collections;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Windows.Forms;

// Explicit HIL only. Exact V1 UID; no firmware, SD writes or V2 access.
internal static class NpMediaUsbHardwareTests
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static object Get(object target,string name){var type=target.GetType();var field=type.GetField(name,All);return field!=null?field.GetValue(target):type.GetProperty(name,All).GetValue(target,null);}
    static object Call(object target,string name,params object[] args){var type=target as Type??target.GetType();return type.GetMethod(name,All).Invoke(target is Type?null:target,args);}
    [STAThread] static int Main(string[] args){Form form=null;try{
        if((args.Length!=3 && args.Length!=4) || !Regex.IsMatch(args[2],@"\A[0-9a-fA-F]{24}\z") || (args.Length==4 && args[3]!="expect-startup-fault"))throw new IOException("Pass app, output and the exact selected V1 UID; optional expect-startup-fault.");
        string root=Path.GetFullPath(args[1]);Directory.CreateDirectory(root);var app=Assembly.LoadFrom(Path.GetFullPath(args[0]));
        var type=app.GetType("USB512_interface.XeNpWinUsbDevice",true);string path=null;int matches=0;
        foreach(string item in (IEnumerable)Call(type,"EnumerateDevicePaths"))if(item.IndexOf("npv1-"+args[2],StringComparison.OrdinalIgnoreCase)>=0){path=item;matches++;}
        if(matches!=1)throw new IOException("Exact V1 device must be present once.");
        path=(string)Call(app.GetType("USB512_interface.NpV1Management",true),"EnsureStreaming",path);
        using(var control=(IDisposable)Call(type,"OpenForControl",path)){
            if((bool)Get(Call(control,"GetStatus"),"Streaming"))throw new IOException("Device is busy; no STOP or START sent.");
            var caps=Call(control,"GetModeCapabilities");if(!(bool)Get(caps,"CameraOnlyActive"))throw new IOException("HIL requires an already selected camera-only profile; no reset sent.");
            var status=Call(control,"GetCameraStatus");Call(status,"Save",Path.Combine(root,"before-camera.txt"));
        }
        form=(Form)Activator.CreateInstance(app.GetType("USB512_interface.NpMediaUsbForm",true),All,null,new object[]{path,root},null);
        ((CheckBox)Get(form,"record")).Checked=true;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-16000,-16000);form.Show();
        var task=(Task)Call(form,"StartPreview");var clock=Stopwatch.StartNew();bool stopped=false;long seen=0;
        while(!task.IsCompleted){Application.DoEvents();Thread.Sleep(5);seen=(long)Get(form,"Frames");
            if(!stopped && clock.ElapsedMilliseconds>10000){Call(form,"StopPreview");stopped=true;}
            if(clock.ElapsedMilliseconds>30000){Call(form,"StopPreview");throw new IOException("Camera test exceeded the bounded deadline.");}}
        task.GetAwaiter().GetResult();Application.DoEvents();
        string error=(string)Get(form,"LastError");long frames=(long)Get(form,"Frames"),gaps=(long)Get(form,"SequenceGaps"),bytes=(long)Get(form,"Bytes");string folder=(string)Get(form,"LastFolder");
        var preview=Get(form,"camera");var picture=(PictureBox)Get(preview,"picture");
        if(picture.Image!=null)picture.Image.Save(Path.Combine(root,"camera-preview.png"),ImageFormat.Png);
        using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(0,0,form.Width,form.Height));image.Save(Path.Combine(root,"media-window.png"),ImageFormat.Png);}
        File.WriteAllText(Path.Combine(root,"hil-result.txt"),"uid="+args[2]+"\r\nframes="+frames+"\r\ngaps="+gaps+"\r\nbytes="+bytes+"\r\nerror="+(error??"none")+"\r\nrendered="+(picture.Image!=null)+"\r\nfolder="+folder+"\r\naudio_live_tested=false\r\n");
        Console.WriteLine("V1 camera HIL: frames="+frames+"; gaps="+gaps+"; bytes="+bytes+"; rendered="+(picture.Image!=null)+"; error="+(error??"none"));
        bool faultExpected=args.Length==4;
        if(faultExpected){if(frames!=0 || error==null || !error.Contains("Camera startup failed:"))throw new IOException("Expected native startup-fault report was not shown.");}
        else if(error!=null || frames==0 || gaps!=0 || picture.Image==null || folder==null || new FileInfo(Path.Combine(folder,"misc.dat")).Length!=bytes)
            throw new IOException("Camera capture/render/raw preservation did not pass.");
        using(var control=(IDisposable)Call(type,"OpenForControl",path))if((bool)Get(Call(control,"GetStatus"),"Streaming"))throw new IOException("V1 did not stop.");
        Console.WriteLine(faultExpected?"PASS V1 startup-fault reporting and stopped state; camera delivery remains failed/unqualified.":"PASS bounded V1 camera streaming, rendering, raw bytes and stopped state. No live microphone qualification.");return 0;
    }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}finally{if(form!=null){try{Call(form,"StopPreview");}catch{}form.Close();form.Dispose();}}}
}
