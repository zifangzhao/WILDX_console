using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CE32_console;
using USB512_interface;

namespace USB512_interface {
    // Only GATT delivery is doubled; the capture wait and burst parser are production code.
    internal sealed partial class NpBleConnection {
        internal int Epoch=1,Writes;
        internal bool Connected=true,PreviewLowerPriorityBusy;
        internal readonly SemaphoreSlim transaction=new SemaphoreSlim(1,1);
        internal event Action<int,byte[]> PreviewReceived;
        Task WriteFrame(byte[] frame,int epoch,CancellationToken cancel){Writes++;return Task.CompletedTask;}
        internal void Emit(byte[] body){var handler=PreviewReceived;if(handler!=null)handler(Epoch,body);}
        internal bool HasReceiver {get{return PreviewReceived!=null;}}
    }
}

static class NpBlePreviewCaptureTests {
    static void Need(bool value,string reason){if(!value)throw new Exception(reason);}
    static void Put(byte[] b,int at,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,at,4);}
    static async Task Run(){
        var radio=new NpBleConnection();
        var capture=radio.CapturePreview(17,1,0,20000,CancellationToken.None);
        Need(radio.transaction.Wait(0),"Waiting for preview owns the command transaction");radio.transaction.Release();
        radio.PreviewLowerPriorityBusy=true;
        Need(!await radio.TryWritePreview(NpV2PreviewBurst.Request(18,0,1,0,0,0),1,CancellationToken.None),"Preview ignored higher-priority work");
        radio.PreviewLowerPriorityBusy=false;
        await Task.Delay(2300); // Reproduces delivery beyond the old two-second deadline.
        Need(!capture.IsCompleted,"Preview expired before delayed fragments arrived");
        for(uint offset=0;offset<3200;offset+=192){
            var body=new byte[224];Put(body,0,NpV2PreviewBurst.Magic);Put(body,4,17);Put(body,8,123);Put(body,12,20000);
            Put(body,24,(3200U<<16)|offset);Put(body,28,1U|(64U<<8));radio.Emit(body);
        }
        var result=await capture;
        Need(result.Data.Length==3200&&result.Session==123&&radio.Writes==1&&!radio.HasReceiver,"Capture replayed a request or retained a receiver");
        using(var cancel=new CancellationTokenSource()){
            var canceled=radio.CapturePreview(19,1,0,20000,cancel.Token);cancel.Cancel();
            bool stopped=false;try{await canceled;}catch(OperationCanceledException){stopped=true;}
            Need(stopped&&!radio.HasReceiver,"Canceled preview did not retire its receiver");
        }
        Console.WriteLine("PASS: delayed complete capture, control/IMU priority, one write, cancellation cleanup");
    }
    static int Main(){try{Run().GetAwaiter().GetResult();return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
