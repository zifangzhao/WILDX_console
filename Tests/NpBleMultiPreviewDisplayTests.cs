using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CE32_console;
using USB512_interface;

static class NpBleMultiPreviewDisplayTests
{
    const BindingFlags All=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    static int checks;
    static void Need(bool b,string why){checks++;if(!b)throw new Exception(why);}
    static object Get(object a,string f){return a.GetType().GetField(f,All).GetValue(a);}
    static void Set(object a,string f,object v){a.GetType().GetField(f,All).SetValue(a,v);}
    static object Call(object a,string m,params object[] args){return a.GetType().GetMethod(m,All).Invoke(a,args);}
    static void Age(object timer,int ms){var f=typeof(Stopwatch).GetField("elapsed",All)??typeof(Stopwatch).GetField("_elapsed",All);f.SetValue(timer,(long)f.GetValue(timer)+(long)Stopwatch.Frequency*ms/1000);}
    static void Tick(object form){var task=(Task)Call(form,"Tick");var clock=Stopwatch.StartNew();while(!task.IsCompleted&&clock.ElapsedMilliseconds<6000){Application.DoEvents();Thread.Sleep(1);}Need(task.IsCompleted,"Multi preview callback stalled");task.GetAwaiter().GetResult();}
    static uint U(byte[] b,int a){return BitConverter.ToUInt32(b,a);}
    static void Put(byte[] b,int a,uint v){Buffer.BlockCopy(BitConverter.GetBytes(v),0,b,a,4);}
    static void Put16(byte[] b,int a,ushort v){Buffer.BlockCopy(BitConverter.GetBytes(v),0,b,a,2);}
    static void Crc(byte[] b){Put(b,220,NpSdControl.Crc(b,220));}
    static byte[] Header(uint id,uint hz){var b=new byte[224];Put(b,0,NpV2PreviewBurst.Magic);Put(b,4,id);Put(b,8,71);Put(b,12,hz);return b;}
    static byte[] Cap(uint id,uint hz){var b=Header(id,hz);Put(b,28,8<<8);Put(b,32,1);Put(b,36,2);Put(b,40,500);Put(b,44,40);Put(b,48,32);Put(b,52,7);Crc(b);return b;}
    static NpV2PreviewBurst Snapshot(uint id,uint p,uint c,uint hz){
        var method=typeof(NpBlePreviewDisplayTests).GetMethod("Calibration",All);
        var first=(NpV2PreviewBurst)method.Invoke(null,new object[]{id,1U,c,hz});
        var second=(NpV2PreviewBurst)method.Invoke(null,new object[]{id,2U,c,hz});
        for(int i=0;i<first.Data.Length;i++)first.Data[i]|=second.Data[i];first.Probe=p;first.Session=71;return first;
    }
    static byte[] Data(byte[] config,uint hz,uint seq){
        var b=Header(U(config,12),hz);ulong first=16000000UL+(seq-1)*40UL*(hz/500)*1600;
        Put(b,16,(uint)first);Put(b,20,(uint)(first>>32));Put(b,24,(160U<<16)|40);Put(b,28,(6U<<8)|(U(config,20)<<16));Put(b,32,seq);Put(b,36,500);
        for(int ch=0;ch<U(config,20);ch++){
            ushort d=BitConverter.ToUInt16(config,32+ch*2);var pick=new NpV2MultiPreview.Selection((uint)(1+(d>>9)),(uint)(d&511),BitConverter.ToUInt16(config,40+ch*2));
            Put16(b,40+ch*2,d);Put16(b,48+ch*2,pick.WordOffset);
            for(int row=0;row<40;row++)Put16(b,60+(row*2+ch)*2,unchecked((ushort)(short)((row-20)*8+ch)));
        }
        Put16(b,52,1);Put16(b,54,NpV2MultiPreview.Delay(hz));Crc(b);return b;
    }
    static void Framing(){
        var config=NpV2MultiPreview.Configure(29,71,new[]{new NpV2MultiPreview.Selection(1,0,0),new NpV2MultiPreview.Selection(2,1,0)});
        var preview=Data(config,20000,1);var receipt=new byte[64];Put(receipt,0,0x32524c4e);Put(receipt,4,2);Put(receipt,12,85);Put(receipt,60,NpSdControl.Crc(receipt,60));
        var control=new byte[68];control[0]=0xad;control[1]=0xb9;Buffer.BlockCopy(receipt,0,control,2,64);control[66]=control[67]=0xda;
        var frame=new byte[228];frame[0]=0xad;frame[1]=0xba;Buffer.BlockCopy(preview,0,frame,2,224);frame[226]=frame[227]=0xda;
        for(int cut=34;cut<150;cut++){
            var stream=new byte[cut+control.Length];Buffer.BlockCopy(frame,0,stream,0,cut);Buffer.BlockCopy(control,0,stream,cut,control.Length);
            var parsed=new NpBleWire().Feed(stream);Need(parsed.Count==1&&parsed[0].Item1==0xb9,"Truncated multi preview held a checked recording receipt");
        }
        var radio=new NpBleConnection();foreach(var request in new[]{config,NpV2MultiPreview.CapabilityRequest(31),NpV2MultiPreview.Feedback(29,71,2,3)})
            Need(radio.TryWritePreview(request,1,CancellationToken.None).GetAwaiter().GetResult(),"Actual writer refused new request");
        var v1=NpV2PreviewBurst.Request(39,5,1,0,0,71);Put(v1,0,0x3156504e);Put(v1,36,2);Put(v1,40,17);Put(v1,60,NpSdControl.Crc(v1,60));Need(radio.TryWritePreview(v1,1,CancellationToken.None).GetAwaiter().GetResult(),"Existing V1 compact kind5 was blocked");
        radio.ImuPending=true;Need(!radio.TryWritePreview(config,1,CancellationToken.None).GetAwaiter().GetResult(),"New preview bypassed IMU ownership");
    }
    internal static void Run(string output){
        Framing();Need(new NpV2MultiPreview.ContinuityException(6).Message.Contains("recording was busy"),"Pressure refusal lacks an understandable reason");
        foreach(uint hz in new uint[]{7500,15000,20000,30000}){
            var s=new NpSdStatus{State=3,Mask=7,Hz=hz,Flags=0x0030FF95,Start=8192,LastId=71};
            bool ownerBusy=false;var radio=new NpBleConnection{Calibration=(id,p,c,h)=>Task.FromResult(Snapshot(id,p,c,h))};
            byte[] config=null;
            radio.OnWrite=b=>{uint kind=U(b,8);if(kind==8)radio.Emit(Cap(U(b,12),hz));if(kind==6)config=(byte[])b.Clone();};
            using(var form=new NpBlePreviewForm(radio,()=>s,()=>ownerBusy)){
                Age(Get(form,"retry"),1100);Tick(form);
                Need((int)Get(form,"multiSupport")==1&&config!=null&&radio.Captures==1,"New firmware did not use one shared snapshot");
                Need(U(config,16)==500&&U(config,20)==2&&U(config,24)==71,"Two-channel 500 Hz configuration changed");
                Need(((CheckBox)Get(form,"enabled")).Checked,"Subscription unchecked preview");
                var plot=Get(form,"plot");var second=Get(form,"plot2");
                ownerBusy=true;radio.Emit(Data(config,hz,2));Tick(form);
                Need((int)Get(plot,"count")==0,"Out-of-order waveform became visible");
                Age(Get(form,"multiFeedback"),80);Tick(form);
                var nack=radio.Requests[radio.Requests.Count-1];Need(U(nack,8)==7&&U(nack,24)==1,"Parent busy guard starved missing-packet feedback");
                radio.Emit(Data(config,hz,1));Tick(form);
                Need((int)Get(plot,"count")==80&&(int)Get(second,"count")==80,"Replay lost either trace");
                var raw=(short[])Get(plot,"values");Need(raw[0]==-160&&raw[39]==152,"Filtered Q3 values were decoded or corrected again");
                Need((double)Get(plot,"Units")==8,"Q3 display scale changed");
                long start=(long)Get(plot,"playbackStarted");Need((int)Call(plot,"VisibleSamples",start-1)==0&&(int)Call(plot,"VisibleSamples",start)==1,"Display lead revealed future samples");
                Age(Get(form,"multiFeedback"),80);Tick(form);var ack=radio.Requests[radio.Requests.Count-1];Need(U(ack,8)==7&&U(ack,20)==2&&U(ack,24)==0,"Cumulative replay ACK incorrect");
                int before=radio.Writes;radio.ImuPending=true;Age(Get(form,"multiFeedback"),800);radio.Emit(Data(config,hz,3));Tick(form);
                Need(radio.Writes==before&&(int)Get(plot,"count")==120,"IMU priority blocked display drain or was bypassed");radio.ImuPending=false;
                ((NumericUpDown)Get(form,"windowSeconds")).Value=0.5M;((NumericUpDown)Get(form,"gainBox")).Value=2;
                Need(Get(form,"multiReceiver")!=null&&radio.Writes==before,"Display settings reconfigured acquisition");
                if(hz==20000&&!String.IsNullOrEmpty(output)){
                    form.ClientSize=new System.Drawing.Size(1000,650);form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new System.Drawing.Point(-4000,-4000);form.Show();((System.Windows.Forms.Timer)Get(form,"timer")).Stop();form.PerformLayout();
                    using(var image=new System.Drawing.Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,form.ClientRectangle);image.Save(Path.Combine(output,"ble-preview-two-channel-500.png"));}
                }
                ((NumericUpDown)Get(form,"channel2")).Value=17;
                Need(((CheckBox)Get(form,"enabled")).Checked&&Get(form,"multiReceiver")==null,"Channel switch unchecked preview or retained old receiver");
                Tick(form);Need(U(radio.Requests[radio.Requests.Count-1],8)==7&&U(radio.Requests[radio.Requests.Count-1],28)==1,"Channel switch did not stop only its preview lease");
                Need(radio.Requests.TrueForAll(b=>U(b,8)==6||U(b,8)==7||U(b,8)==8),"Preview sent a recording/control command");
            }
        }
        var old=new NpBleConnection();bool block=false;var status=new NpSdStatus{State=3,Mask=3,Hz=20000,Flags=128};
        old.OnWrite=b=>{var body=Header(U(b,12),20000);Put(body,28,129<<8);block=true;old.Emit(body);};
        using(var form=new NpBlePreviewForm(old,()=>status,()=>block)){
            Age(Get(form,"retry"),1100);Tick(form);Need((int)Get(form,"multiSupport")==-1&&old.Captures==0&&((CheckBox)Get(form,"enabled")).Checked,"Explicit old-firmware fallback broke logging or preview selection");
        }
        var bad=new NpBleConnection{OnWrite=null};
        bad.OnWrite=b=>{var body=Cap(U(b,12),20000);body[80]^=1;bad.Emit(body);};
        using(var form=new NpBlePreviewForm(bad,()=>status,()=>false)){
            Age(Get(form,"retry"),1100);Tick(form);Need((int)Get(form,"multiSupport")==0&&!((CheckBox)Get(form,"enabled")).Checked&&bad.Captures==0,"Corrupt capability was mistaken for legacy firmware");
        }
        var uncertain=new NpBleConnection{Calibration=(id,p,c,h)=>Task.FromResult(Snapshot(id,p,c,h))};
        uncertain.OnWrite=b=>{if(U(b,8)==8)uncertain.Emit(Cap(U(b,12),20000));else if(U(b,8)==6){uncertain.PendingWrite=new TaskCompletionSource<bool>();uncertain.PendingWrite.SetException(new IOException("Partial GATT delivery"));}};
        using(var form=new NpBlePreviewForm(uncertain,()=>status,()=>false)){
            Age(Get(form,"retry"),1100);Tick(form);int writes=uncertain.Writes;
            Need(!((CheckBox)Get(form,"enabled")).Checked&&Get(form,"multiReceiver")==null,"Uncertain delivery retained preview");
            Tick(form);Need(uncertain.Writes==writes&&Get(form,"multiStop")==null,"Uncertain delivery replayed a frame or attempted STOP repair");
            Need(((Label)Get(form,"info")).Text.Contains("Partial GATT delivery"),"A later timer tick erased the preview failure reason");
        }
        Console.WriteLine("PASS "+checks+" two-channel 500 Hz actual-source GUI/replay/IMU-priority/legacy-fallback checks");
    }
}
