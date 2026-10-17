using System;
using System.Collections;
using System.Collections.Generic;
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
    static void RequireFps(string folder,string output,long gaps,int fps) {
        var status=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(string line in File.ReadAllLines(Path.Combine(folder,"camera_final_status.txt"))){int at=line.IndexOf('=');if(at>0)status[line.Substring(0,at)]=line.Substring(at+1);}
        string[] fields={"requested_rate_hz","effective_rate_hz","measured_capture_millihz","captured_frames","dropped_frames","missed_deadlines","failure_count","recoveries"};
        long[] expected={fps,fps,fps*1000,fps,0,0,0,0};
        var result=new List<string>();var failed=new List<string>();
        for(int i=0;i<fields.Length;i++){
            string value;long number;bool present=status.TryGetValue(fields[i],out value);
            result.Add(fields[i]+"="+(present?value:"missing"));
            if(!present || !long.TryParse(value,out number) || (i==3?number<expected[i]:number!=expected[i]))failed.Add(fields[i]);
        }
        result.Add("app_camera_sequence_gaps="+gaps);if(gaps!=0)failed.Add("app_camera_sequence_gaps");
        string tick;if(status.TryGetValue("frame_tick_ms",out tick))result.Add("final_frame_tick_ms="+tick);
        result.Add("result="+(failed.Count==0?"pass":"fail"));
        result.Add("timing_evidence=final device CAM1 aggregate cadence/counters; exact "+fps*1000+"mHz required after at least "+fps+" captured frames");
        result.Add("per_frame_source_timestamps=not available in USB camera packets or misc.frames.csv; final frame tick is not a per-frame timing trace");
        result.Add("scope=actual camera-only USB capture with IMU saving; no SD writes or host-arrival timing inference");
        File.WriteAllLines(Path.Combine(output,"camera-timing-hil-result.txt"),result);
        if(failed.Count!=0)throw new IOException("Strict continuous "+fps+"fps gate failed: "+string.Join(", ",failed)+".");
        Console.WriteLine("PASS strict camera USB "+fps+"fps: measured "+fps*1000+"mHz; no dropped frames, missed deadlines, failures, recoveries or app sequence gaps.");
    }
    static void RequireImu(string folder,string output) {
        var status=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(string line in File.ReadAllLines(Path.Combine(folder,"imu_capture_status.txt"))){int at=line.IndexOf('=');if(at>0)status[line.Substring(0,at)]=line.Substring(at+1);}
        if(!status.ContainsKey("availability") || status["availability"]!="available" || !status.ContainsKey("error") || status["error"]!="none")
            throw new IOException("Camera IMU was unavailable or its sidecar failed.");
        long analog=new FileInfo(Path.Combine(folder,"analogin.dat")).Length;
        byte[] raw=File.ReadAllBytes(Path.Combine(folder,"imu_snapshots.bin"));
        if(analog==0 || analog%32!=0 || raw.Length==0 || raw.Length%140!=0)throw new IOException("Missing/unaligned camera IMU sidecars.");
        var sequences=new HashSet<uint>();var acceleration=new HashSet<string>();var gyro=new HashSet<string>();
        uint firstTick=0,lastTick=0;int valid=0;
        for(int at=0;at<raw.Length;at+=140){
            if(BitConverter.ToUInt32(raw,at)!=0x31534e53U || BitConverter.ToUInt32(raw,at+4)!=1)throw new IOException("Invalid native IMU snapshot ABI.");
            if((BitConverter.ToUInt32(raw,at+12)&1)==0)continue;
            uint seq=BitConverter.ToUInt32(raw,at+20),tick=BitConverter.ToUInt32(raw,at+32);
            if(seq==0 || BitConverter.ToUInt32(raw,at+104)!=0)throw new IOException("Invalid/errored native AG snapshot.");
            if(valid++==0)firstTick=tick;lastTick=tick;sequences.Add(seq);
            acceleration.Add(BitConverter.ToString(raw,at+48,12));gyro.Add(BitConverter.ToString(raw,at+60,12));
        }
        if(valid==0 || sequences.Count<2 || firstTick==lastTick || acceleration.Count<2 || gyro.Count<2)
            throw new IOException("IMU sidecars contain no fresh changing native measurements.");
        if(!status.ContainsKey("records") || long.Parse(status["records"])!=raw.Length/140)throw new IOException("IMU record report disagrees with saved bytes.");
        File.WriteAllLines(Path.Combine(output,"imu-hil-result.txt"),new[]{"records="+raw.Length/140,"valid_ag_records="+valid,
            "distinct_imu_sequences="+sequences.Count,"first_imu_tick_ms="+firstTick,"last_imu_tick_ms="+lastTick,
            "distinct_accel_vectors="+acceleration.Count,"distinct_gyro_vectors="+gyro.Count,"analogin_rows="+analog/32,
            "scope=actual camera-only USB save; native snapshots plus derived held1250Hz grid; no SD writes"});
        Console.WriteLine("PASS camera USB IMU: native records="+raw.Length/140+"; distinct AG="+sequences.Count+"; analogin rows="+analog/32);
    }
    [STAThread] static int Main(string[] args){Form form=null;try{
        if((args.Length!=3 && args.Length!=4 && args.Length!=5 && args.Length!=6) || !Regex.IsMatch(args[2],@"\A[0-9a-fA-F]{24}\z"))throw new IOException("Pass app, output and the exact selected V1 UID; optional expect-startup-fault or seconds=1..120 require-imu [require-40fps|require-50fps].");
        int seconds=10,fps=50;bool faultExpected=args.Length>=4 && args[3]=="expect-startup-fault",requireImu=args.Length>=5,requireTiming=args.Length==6;
        if(requireImu && (args[4]!="require-imu" || faultExpected))throw new IOException("IMU HIL requires a bounded successful camera run.");
        if(requireTiming){if(args[5]=="require-40fps")fps=40;else if(args[5]!="require-50fps")throw new IOException("Invalid strict camera timing option.");}
        if(args.Length>=4 && !faultExpected && (!args[3].StartsWith("seconds=") || !int.TryParse(args[3].Substring(8),out seconds) || seconds<1 || seconds>120))throw new IOException("Invalid bounded camera duration.");
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
        ((NumericUpDown)Get(form,"rate")).Value=fps;
        ((CheckBox)Get(form,"record")).Checked=true;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-16000,-16000);form.Show();
        var task=(Task)Call(form,"StartPreview");var clock=Stopwatch.StartNew();bool stopped=false;long seen=0;
        while(!task.IsCompleted){Application.DoEvents();Thread.Sleep(5);seen=(long)Get(form,"Frames");
            if(!stopped && clock.ElapsedMilliseconds>seconds*1000){Call(form,"StopPreview");stopped=true;}
            if(clock.ElapsedMilliseconds>seconds*1000+20000){Call(form,"StopPreview");throw new IOException("Camera test exceeded the bounded deadline.");}}
        task.GetAwaiter().GetResult();Application.DoEvents();
        string error=(string)Get(form,"LastError");long frames=(long)Get(form,"Frames"),gaps=(long)Get(form,"SequenceGaps"),bytes=(long)Get(form,"Bytes");string folder=(string)Get(form,"LastFolder");
        var preview=Get(form,"camera");var picture=(PictureBox)Get(preview,"picture");
        if(picture.Image!=null)picture.Image.Save(Path.Combine(root,"camera-preview.png"),ImageFormat.Png);
        using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(0,0,form.Width,form.Height));image.Save(Path.Combine(root,"media-window.png"),ImageFormat.Png);}
        File.WriteAllText(Path.Combine(root,"hil-result.txt"),"uid="+args[2]+"\r\nframes="+frames+"\r\ngaps="+gaps+"\r\nbytes="+bytes+"\r\nerror="+(error??"none")+"\r\nrendered="+(picture.Image!=null)+"\r\nfolder="+folder+"\r\naudio_live_tested=false\r\n");
        Console.WriteLine("V1 camera HIL: frames="+frames+"; gaps="+gaps+"; bytes="+bytes+"; rendered="+(picture.Image!=null)+"; error="+(error??"none"));
        if(faultExpected){if(frames!=0 || error==null || !error.Contains("Camera startup failed:"))throw new IOException("Expected native startup-fault report was not shown.");}
        else if(error!=null || frames==0 || gaps!=0 || picture.Image==null || folder==null || new FileInfo(Path.Combine(folder,"misc.dat")).Length!=bytes)
            throw new IOException("Camera capture/render/raw preservation did not pass.");
        using(var control=(IDisposable)Call(type,"OpenForControl",path))if((bool)Get(Call(control,"GetStatus"),"Streaming"))throw new IOException("V1 did not stop.");
        if(requireImu)RequireImu(folder,root);
        if(requireTiming)RequireFps(folder,root,gaps,fps);
        Console.WriteLine(faultExpected?"PASS V1 startup-fault reporting and stopped state; camera delivery remains failed/unqualified.":"PASS bounded V1 camera streaming, rendering, raw bytes and stopped state. No live microphone qualification.");return 0;
    }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}finally{if(form!=null){try{Call(form,"StopPreview");}catch{}form.Close();form.Dispose();}}}
}
