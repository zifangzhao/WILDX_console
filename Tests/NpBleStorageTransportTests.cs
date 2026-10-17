using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using USB512_interface;

// Compiles the production native transaction against a fake GATT boundary.
namespace USB512_interface
{
    internal sealed partial class NpBleConnection
    {
        internal readonly SemaphoreSlim transaction=new SemaphoreSlim(1,1);
        readonly object receiveLock=new object();
        TaskCompletionSource<byte[]> receipt;Func<byte,byte[],bool> matches;
        int epoch;internal int Epoch{get{return epoch;}}
        internal bool Connected=true;internal int Writes,Closes;internal byte[] Sent;
        internal Func<Task> OnWrite;
        Task WriteFrame(byte[] frame,int generation,CancellationToken cancel){
            if(!Connected || generation!=Epoch)throw new IOException("Stale connection");
            Writes++;Sent=frame;return OnWrite==null?Task.CompletedTask:OnWrite();
        }
        void Disconnect(){Connected=false;epoch++;Closes++;if(receipt!=null)receipt.TrySetException(new IOException("Disconnected"));}
        internal void Feed(byte command,byte[] body){lock(receiveLock){if(receipt!=null && matches(command,body))receipt.TrySetResult(body);}}
        internal void ChangeEpoch(){epoch++;}
    }
}

static class NpBleStorageTransportTests
{
    static int checks;static void Need(bool ok,string reason){checks++;if(!ok)throw new Exception(reason);}
    static async Task Reject(Func<Task> call){bool rejected=false;try{await call();}catch(Exception ex) when(ex is IOException || ex is TimeoutException){rejected=true;}Need(rejected,"Uncertain native operation reported success");}
    static async Task Test()
    {
        var page=new byte[512];page[17]=21;var source=new NpBleConnection();
        source.OnWrite=()=>{source.Feed(0x94,new byte[4]);source.Feed(0x95,new byte[4]);source.Feed(0x94,page);return Task.CompletedTask;};
        Need(ReferenceEquals(await source.ExchangeV1Storage(513,false),page),"Catalog page response lost");
        Need(BitConverter.ToString(source.Sent)=="3C-94-01-02-3E" && source.Writes==1,"Wrong native page frame or retry");
        foreach(bool ackFirst in new[]{true,false}){
            source=new NpBleConnection();var target=source;
            target.OnWrite=()=>{
                if(ackFirst){target.Feed(0x95,BitConverter.GetBytes(7U));target.Feed(0x90,new byte[512]);}
                else{target.Feed(0x90,new byte[512]);target.Feed(0x95,BitConverter.GetBytes(7U));}
                return Task.CompletedTask;
            };
            Need(BitConverter.ToUInt32(await target.ExchangeV1Storage(0,true),0)==7,"Delete/post-parameters coalesced reply lost");
            Need(BitConverter.ToString(target.Sent)=="3C-95-01-3E" && target.Writes==1 && target.Closes==0,"Delete was repeated or connection closed after success");
        }
        source=new NpBleConnection();source.OnWrite=()=>{source.Feed(0x95,new byte[4]);return Task.CompletedTask;};
        var pending=source.ExchangeV1Storage(0,true);Need(!pending.IsCompleted,"Delete completed before post-delete parameters were drained");source.Feed(0x90,new byte[512]);await pending;
        source=new NpBleConnection();source.OnWrite=()=>{throw new IOException("Partial native write");};
        await Reject(async()=>await source.ExchangeV1Storage(0,true));Need(source.Writes==1 && !source.Connected,"Failed mutation replayed or ambiguous native connection retained");
        await Reject(async()=>await source.ExchangeV1Storage(0,true));Need(source.Writes==1,"Disconnected mutation attempted another write");
        source=new NpBleConnection();source.OnWrite=()=>{source.Feed(0x95,new byte[4]);source.Feed(0x90,new byte[512]);source.ChangeEpoch();return Task.CompletedTask;};
        await Reject(async()=>await source.ExchangeV1Storage(0,true));Need(!source.Connected && source.Writes==1,"Changed-epoch receipt accepted/replayed");
        source=new NpBleConnection();await source.transaction.WaitAsync();pending=source.ExchangeV1Storage(0,false);
        Need(source.Writes==0,"Native catalog operation bypassed the shared control lease");source.OnWrite=()=>{source.Feed(0x94,page);return Task.CompletedTask;};source.transaction.Release();await pending;
        source=new NpBleConnection();await Reject(async()=>await source.ExchangeV1Storage(0,true));
        Need(source.Writes==1 && !source.Connected && source.transaction.CurrentCount==1,"Missing receipt replayed or stranded the command lease");
        bool invalid=false;try{await source.ExchangeV1Storage(1024,false);}catch(ArgumentOutOfRangeException){invalid=true;}Need(invalid,"Out-of-range catalog page admitted");
    }
    static int Main(){try{Test().GetAwaiter().GetResult();Console.WriteLine("PASS "+checks+" production BLE native transaction checks; fake GATT only.");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
