using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CE32_console;

static class NpDownloadPrefetchTests
{
    static int checks;
    static void Need(bool value,string message){checks++;if(!value)throw new Exception(message);}
    sealed class Source : MemoryStream
    {
        internal int Active,Calls,FailCall,BlockCall,MaxRead=Int32.MaxValue;
        internal long Furthest;
        internal readonly ManualResetEvent Reading=new ManualResetEvent(false),Release=new ManualResetEvent(true);
        internal Source(byte[] data):base(data,false){}
        public override int Read(byte[] b,int o,int n){
            Need(Interlocked.Increment(ref Active)==1,"Concurrent source access");
            try{Calls++;if(Calls==BlockCall){Reading.Set();Release.WaitOne();}if(Calls==FailCall)throw new InvalidDataException("test integrity failure");
                int got=base.Read(b,o,Math.Min(n,MaxRead));Furthest=Math.Max(Furthest,Position);return got;
            }finally{Interlocked.Decrement(ref Active);}
        }
    }
    static void Refuse<T>(Action action)where T:Exception{try{action();}catch(T){checks++;return;}throw new Exception("Expected "+typeof(T).Name);}
    static byte[] Copy(Stream stream,int size=779){using(var output=new MemoryStream()){byte[] buffer=new byte[size];int n;while((n=stream.Read(buffer,0,buffer.Length))!=0)output.Write(buffer,0,n);return output.ToArray();}}
    static void Main(){
        byte[] data=new byte[200123];new Random(179).NextBytes(data);
        using(var source=new Source(data){MaxRead=257}){
            using(var input=new NpDownloadPrefetchStream(source,123,199999,null,4096)){
                Need(Copy(input).SequenceEqual(data.Skip(123).Take(199876)),"Sequential data changed at buffer/EOF boundaries");
                var random=new Random(91);for(int n=0;n<120;n++){int at=random.Next(123,199899);input.Position=at;byte[] part=new byte[100];Need(input.Read(part,0,100)==100&&part.SequenceEqual(data.Skip(at).Take(100)),"Seek changed bytes");}
                Need(input.Read(new byte[0],0,0)==0,"Zero read started work");Refuse<ArgumentOutOfRangeException>(()=>input.Position=122);
            }
            Need(source.CanRead&&source.Active==0&&source.Furthest<=199999,"Dispose closed source, retained a worker or read outside extent");
        }
        using(var source=new Source(data){FailCall=2}){
            var input=new NpDownloadPrefetchStream(source,0,data.Length,null,512);byte[] buffer=new byte[1024];
            Refuse<InvalidDataException>(()=>input.Read(buffer,0,1024));int calls=source.Calls;
            Refuse<InvalidDataException>(()=>input.Read(buffer,0,1));Need(source.Calls==calls,"Failed producer was replayed");input.Dispose();
        }
        using(var source=new Source(data){BlockCall=2}){
            source.Release.Reset();
            var input=new NpDownloadPrefetchStream(source,0,data.Length,null,512);byte[] one=new byte[1];input.Read(one,0,1);
            Need(source.Reading.WaitOne(3000),"Worker did not run");
            var dispose=Task.Run(()=>input.Dispose());Thread.Sleep(20);Need(!dispose.IsCompleted,"Dispose released an active source");source.Release.Set();dispose.GetAwaiter().GetResult();
            Need(source.Active==0&&source.CanRead,"Dispose did not join worker / preserve source");
        }
        bool cancelled=false;using(var source=new Source(data))using(var input=new NpDownloadPrefetchStream(source,0,data.Length,()=>cancelled,512)){
            cancelled=true;Refuse<OperationCanceledException>(()=>input.Read(new byte[1],0,1));Need(source.Calls==0,"Cancelled read touched source");
        }
        using(var source=new Source(data.Take(1000).ToArray())){
            Refuse<ArgumentOutOfRangeException>(()=>new NpDownloadPrefetchStream(source,0,1001,null));
        }
        Console.WriteLine("PASS "+checks+" prefetch byte/seek/bounds/cancellation/fault/worker-lifetime checks.");
    }
}
