using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

internal static class NpMediaPreviewTests
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static int checks;
    static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static byte[] Wave(int hz,int samples,Func<int,short> sample){using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream)){
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+samples*2);writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);writer.Write((ushort)1);writer.Write((ushort)1);writer.Write(hz);writer.Write(hz*2);writer.Write((ushort)2);writer.Write((ushort)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));writer.Write(samples*2);for(int i=0;i<samples;i++)writer.Write(sample(i));return stream.ToArray();}}
    [STAThread] static int Main(string[] args){try{
        if(args.Length<2 || args.Length>3 || (args.Length==3 && args[2]!="--camera-rates"))throw new ArgumentException("Pass app, fresh output directory, and optional --camera-rates");
        var app=Assembly.LoadFrom(args[0]);string root=args[1];Directory.CreateDirectory(root);
        bool cameraRatesOnly=args.Length==3;
        if(!cameraRatesOnly){
        var audio=app.GetType("USB512_interface.NpAudioPreview",true);var read=audio.GetMethod("ReadClip",All);
        foreach(int hz in new[]{44100,48000,200000,240000}){
            string path=Path.Combine(root,hz+".wav");byte[] original=Wave(hz,hz/10,i=>-12000);File.WriteAllBytes(path,original);
            using(var clip=(MemoryStream)read.Invoke(null,new object[]{path})){
                byte[] data=clip.ToArray();Check(BitConverter.ToInt32(data,24)==48000,"Listening rate wrong");Check(data.Length==44+4800*2,"Duration changed");
                for(int i=44;i<data.Length;i+=2)Check(BitConverter.ToInt16(data,i)==-12000,"DC/sign changed");
                using(var image=(System.Drawing.Bitmap)audio.GetMethod("Waveform",All).Invoke(null,new object[]{clip}))Check(image.Width==800,"Waveform missing");
            }
            Check(File.ReadAllBytes(path).SequenceEqual(original),"Native source changed");
        }
        string exact=Path.Combine(root,"exact.wav");byte[] pcm=Wave(48000,4800,i=>(short)(Math.Sin(i*2*Math.PI*400/48000)*28000));File.WriteAllBytes(exact,pcm);
        using(var clip=(MemoryStream)read.Invoke(null,new object[]{exact}))Check(pcm.SequenceEqual(clip.ToArray()),"48 kHz PCM path not byte-exact");
        string longFile=Path.Combine(root,"long.wav");File.WriteAllBytes(longFile,Wave(48000,11*48000,i=>32767));
        using(var clip=(MemoryStream)read.Invoke(null,new object[]{longFile}))Check(clip.Length==44+10*48000*2,"Listening allocation is unbounded");
        foreach(byte[] invalid in new[]{new byte[44],pcm.Take(pcm.Length-1).ToArray(),Wave(999,10,i=>0),Wave(250000,10,i=>0)}){
            string path=Path.Combine(root,"invalid.wav");File.WriteAllBytes(path,invalid);bool rejected=false;try{using(var clip=(MemoryStream)read.Invoke(null,new object[]{path})){} }catch(TargetInvocationException ex){rejected=ex.InnerException is IOException;}Check(rejected,"Invalid/truncated WAV accepted");
        }
        }
        using(var form=(Form)Activator.CreateInstance(app.GetType("USB512_interface.NpMediaUsbForm",true),All,null,new object[]{null,root},null)){
            var rate=(NumericUpDown)form.GetType().GetField("rate",All).GetValue(form);
            Check(rate.Value==40,"V1 USB media UI does not default to40fps");
            Check(rate.Minimum<=25 && rate.Maximum>=50,"Explicit25/40/50 camera selection lost");
            foreach(int fps in new[]{25,30,40,50}){rate.Value=fps;Check(rate.Value==fps,"Explicit camera rate choice changed: "+fps);}
            rate.Value=40;
            Check(form.Text=="Media preview","Media title wrong");using(var image=new System.Drawing.Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new System.Drawing.Rectangle(0,0,form.Width,form.Height));image.Save(Path.Combine(root,"media-preview.png"));}
            Check(!((bool)form.GetType().GetProperty("Active",All).GetValue(form,null)),"Construction started hardware");
        }
        Console.WriteLine(cameraRatesOnly?"PASS "+checks+" V1 camera UI checks: default40fps, explicit25/30/40/50 choices and no auto-start hardware.":"PASS "+checks+" media checks: native source retained, bounded signed PCM listening, malformed WAV rejection, waveform and no-auto-start UI.");return 0;
    }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
