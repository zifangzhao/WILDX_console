using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

// Exercises the actual compiled wire, client, parser/journal handler and
// connection semaphore. No radio, device handle, Start or Stop is invoked.
static class NpBleImuTests {
    const BindingFlags All=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static Type protocol,connection,session,wire;static int checks;
    static void Need(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static uint U(byte[] b,int at){return BitConverter.ToUInt32(b,at);}
    static void Put(byte[] b,int at,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,at,4);}
    static uint Crc(byte[] b){uint c=0xffffffff;for(int i=0;i<60;i++){c^=b[i];for(int bit=0;bit<8;bit++)c=c>>1^((0U-(c&1))&0xedb88320);}return ~c;}
    static byte[] Seal(byte[] b){Put(b,60,Crc(b));return b;}
    static object Call(Type type,string name,object target,params object[] args){
        var methods=type.GetMethods(All).Where(m=>m.Name==name && m.GetParameters().Length==args.Length).ToArray();
        try{return methods.Single().Invoke(target,args);}catch(TargetInvocationException ex){throw ex.InnerException;}
    }
    static void Refuse(Action action,string why,Type kind=null){
        try{action();}catch(Exception ex){Need(kind==null || kind.IsInstanceOfType(ex),why+": wrong exception "+ex);return;}
        throw new Exception("Accepted "+why);
    }
    static byte[] Request(uint op,uint id,uint token=0,uint offset=0){return (byte[])Call(protocol,"Request",null,op,id,token,offset);}
    static byte[] Snapshot(uint sequence=1,uint valid=3){
        var b=new byte[140];Put(b,0,0x31534e53);Put(b,4,1);Put(b,8,3);Put(b,12,valid);Put(b,16,1000);
        Put(b,20,sequence);Put(b,24,sequence);Put(b,32,995);Put(b,36,994);Put(b,44,sequence*128);
        Put(b,48,1000000);Put(b,52,unchecked((uint)-500000));Put(b,60,12500);Put(b,72,42000);
        return b;
    }
    static byte[] Reply(byte[] request,byte[] snapshot,uint token=71,uint error=0){
        var b=new byte[64];Put(b,0,0x5253504e);Put(b,4,1);Put(b,8,U(request,8));Put(b,12,U(request,12));
        Put(b,16,error);Put(b,24,U(request,20));if(error==0){Put(b,20,token);Put(b,28,140);Buffer.BlockCopy(snapshot,(int)U(request,20),b,32,28);}return Seal(b);
    }
    static Task<byte[]> Read(Func<byte[],CancellationToken,Task<byte[]>> exchange,Func<uint> next=null,Func<int> epoch=null,Func<bool> yield=null,CancellationToken cancel=default(CancellationToken)){
        uint id=100;return (Task<byte[]>)Call(protocol,"ReadAsync",null,exchange,next??(()=>++id),epoch??(()=>1),yield??(()=>false),cancel);
    }
    static bool Match(byte command,byte[] request,byte[] response){return (bool)Call(connection,"Matches",null,command,request,command,response);}
    static byte[] Frame(byte[] body,bool legacy){var b=new byte[body.Length+(legacy?4:3)];b[0]=legacy?(byte)0xad:(byte)0x3c;b[1]=0xb9;Buffer.BlockCopy(body,0,b,2,body.Length);b[body.Length+2]=legacy?(byte)0xda:(byte)0x3e;if(legacy)b[body.Length+3]=0xda;return b;}
    static void Wire(){
        var request=Request(0,92);Need(U(request,0)==0x5153504e && U(request,4)==1 && U(request,60)==Crc(request),"request ABI");
        Need(request.Skip(24).Take(36).All(x=>x==0),"reserved request bytes");
        Refuse(()=>Request(0,0),"zero ID");Refuse(()=>Request(0,1,2),"latch token");Refuse(()=>Request(1,1,0,28),"zero read token");
        foreach(uint offset in new uint[]{0,1,27,29,140})Refuse(()=>Request(1,1,71,offset),"invalid offset");
        var snapshot=Snapshot();snapshot[55]=0xad;snapshot[56]=0xda;snapshot[57]=0x3e;
        var reply=Reply(request,snapshot);Need(Match(0xb9,request,reply),"production B9 sensor matching");Need(!Match(0xb8,request,reply),"B8 config not overloaded");
        var wrong=(byte[])reply.Clone();Put(wrong,12,93);Seal(wrong);Need(!Match(0xb9,request,wrong),"late ID ignored");
        var clock=new byte[64];Put(clock,0,0x3254504e);Put(clock,4,1);Put(clock,12,9);Seal(clock);
        var clockReply=(byte[])clock.Clone();Put(clockReply,0,0x3252504e);Seal(clockReply);
        Need(Match(0xb9,clock,clockReply),"existing time receipt matching retained");Need(!Match(0xb9,request,clockReply),"time reply cannot satisfy sensor request");
        foreach(bool legacy in new[]{false,true})for(int split=1;split<67;split++){
            var parser=Activator.CreateInstance(wire,true);var frame=Frame(reply,legacy);
            var first=(IList)Call(wire,"Feed",parser,frame.Take(split).ToArray());var last=(IList)Call(wire,"Feed",parser,frame.Skip(split).ToArray());
            Need(first.Count==0 && last.Count==1,"fragmented binary BLE frame "+split);
            var body=(byte[])last[0].GetType().GetProperty("Item2").GetValue(last[0]);Need(body.SequenceEqual(reply),"exact sensor bytes");
        }
    }
    static void Client(){
        byte[] snapshot=Snapshot();int calls=0;var ids=new HashSet<uint>();
        var result=Read((request,cancel)=>{calls++;Need(ids.Add(U(request,12)),"unique request ID");Need(U(request,20)==(uint)((calls-1)*28),"ordered offset");return Task.FromResult(Reply(request,snapshot));}).GetAwaiter().GetResult();
        Need(calls==5 && result.SequenceEqual(snapshot),"one complete coherent140B view");
        for(int at=0;at<5;at++){
            int selected=at;calls=0;
            Refuse(()=>Read((request,cancel)=>{var reply=Reply(request,snapshot);if(calls++==selected)reply[35]^=1;return Task.FromResult(reply);}).GetAwaiter().GetResult(),"corrupt chunk "+at,typeof(IOException));
            calls=0;Refuse(()=>Read((request,cancel)=>{var reply=Reply(request,snapshot);if(calls++==selected){Put(reply,24,U(reply,24)+28);Seal(reply);}return Task.FromResult(reply);}).GetAwaiter().GetResult(),"wrong chunk offset",typeof(IOException));
            calls=0;Refuse(()=>Read((request,cancel)=>{var reply=Reply(request,snapshot);if(calls++==selected){Put(reply,28,139);Seal(reply);}return Task.FromResult(reply);}).GetAwaiter().GetResult(),"wrong total",typeof(IOException));
        }
        for(int at=1;at<5;at++){
            int selected=at;calls=0;Refuse(()=>Read((request,cancel)=>Task.FromResult(Reply(request,snapshot,++calls==selected+1?72U:71U))).GetAwaiter().GetResult(),"mixed snapshot token",typeof(IOException));
        }
        for(uint error=1;error<=4;error++){
            uint code=error;Refuse(()=>Read((request,cancel)=>Task.FromResult(Reply(request,snapshot,error:code))).GetAwaiter().GetResult(),"explicit optional error "+error,typeof(IOException));
        }
        foreach(int stopAt in new[]{0,1,2,3,4})using(var cancel=new CancellationTokenSource()){
            calls=0;if(stopAt==0)cancel.Cancel();
            Refuse(()=>Read((request,token)=>{var reply=Reply(request,snapshot);if(++calls==stopAt)cancel.Cancel();return Task.FromResult(reply);},cancel:cancel.Token).GetAwaiter().GetResult(),"cancel whole view",typeof(OperationCanceledException));
            Need(calls==stopAt,"no command after cancellation");
        }
        calls=0;Need(Read((request,cancel)=>{calls++;return Task.FromResult(Reply(request,snapshot));},yield:()=>true).GetAwaiter().GetResult()==null && calls==0,"foreground yields before sending");
        calls=0;Need(Read((request,cancel)=>{calls++;return Task.FromResult(Reply(request,snapshot));},yield:()=>calls>0).GetAwaiter().GetResult()==null && calls==1,"foreground discards partial view");
        calls=0;Need(Read((request,cancel)=>{calls++;return Task.FromResult<byte[]>(null);}).GetAwaiter().GetResult()==null && calls==1,"busy semaphore retires whole view");
        int generation=1;Refuse(()=>Read((request,cancel)=>{generation++;return Task.FromResult(Reply(request,snapshot));},epoch:()=>generation).GetAwaiter().GetResult(),"connection epoch changed",typeof(IOException));
        Refuse(()=>Read((request,cancel)=>Task.FromResult(Reply(request,snapshot)),next:()=>7).GetAwaiter().GetResult(),"reused transaction ID",typeof(IOException));
        var invalid=Snapshot();invalid[0]=0;Refuse(()=>Read((request,cancel)=>Task.FromResult(Reply(request,invalid))).GetAwaiter().GetResult(),"invalid SNS1 ABI",typeof(IOException));
    }
    static void Handler(string folder){
        Directory.CreateDirectory(folder);var sentinel=Path.Combine(folder,"neural.dat");File.WriteAllBytes(sentinel,new byte[]{11,22,33});
        var handler=Activator.CreateInstance(session,All,null,new object[]{folder,"001A0004434250062039314E"},null);
        try {
            var raw=Snapshot();var original=(byte[])raw.Clone();var display=Call(session,"Accept",handler,raw);var values=(double[])display.GetType().GetField("Values",All).GetValue(display);
            Need(values[0]==1 && values[1]==-0.5 && values[3]==12.5 && values[6]==42,"actual existing parser/display fixed units");
            Need(raw.SequenceEqual(original),"SNS1 input preserved");Call(session,"Accept",handler,raw);Call(session,"Accept",handler,Snapshot(2));Call(session,"Accept",handler,Snapshot(3,0));
            Refuse(()=>Call(session,"Accept",handler,new byte[139]),"partial view cannot enter journal",typeof(IOException));
        }finally{((IDisposable)handler).Dispose();}
        string[] rows=File.ReadAllLines(Path.Combine(folder,"imu_native_samples.jsonl"));Need(rows.Length==3,"metadata plus two distinct valid samples only");
        Need(rows[0].Contains("001A0004434250062039314E") && rows[1].Contains("host_receipt_qpc"),"identity and host provenance retained");
        Need(!File.Exists(Path.Combine(folder,"analogin.dat")),"no held grid fabricated from sparse BLE views");
        Need(File.ReadAllBytes(sentinel).SequenceEqual(new byte[]{11,22,33}),"neural file untouched");
        Need(File.ReadAllText(Path.Combine(folder,"ble_imu_stream.txt")).Contains("maximum_poll_hz=1"),"sparse stream limitation saved");
        var invalid=Activator.CreateInstance(session,All,null,new object[]{null,null},null);try{var display=Call(session,"Accept",invalid,Snapshot(0,0));Need((uint)display.GetType().GetField("Valid",All).GetValue(display)==0,"unavailable IMU never fabricated");}finally{((IDisposable)invalid).Dispose();}
    }
    static void Priority(){
        var peer=Activator.CreateInstance(connection,true);
        var semaphore=(SemaphoreSlim)connection.GetField("transaction",All).GetValue(peer);semaphore.Wait();
        var optional=connection.GetMethods(All).Single(m=>m.Name=="ExchangeAsync" && m.GetParameters().Length==4);
        var foreground=connection.GetMethods(All).Single(m=>m.Name=="ExchangeAsync" && m.GetParameters().Length==3);
        try {
            var skip=(Task<byte[]>)optional.Invoke(peer,new object[]{(byte)0xb9,Request(0,18),CancellationToken.None,true});
            Need(skip.IsCompleted && skip.GetAwaiter().GetResult()==null,"actual low-priority lane never queues ahead of control");
            var control=(Task<byte[]>)foreground.Invoke(peer,new object[]{(byte)0xb9,Request(0,19),CancellationToken.None});
            Need(!control.IsCompleted,"existing normal command still waits for its lease");semaphore.Release();
            Refuse(()=>control.GetAwaiter().GetResult(),"disconnected control performs no radio call",typeof(IOException));
            connection.GetField("imuReaders",All).SetValue(peer,1);
            Need((bool)connection.GetProperty("ImuPending",All).GetValue(peer),"pending IMU is visible to preview");
            var previewType=connection.Assembly.GetType("CE32_console.NpV2PreviewBurst",true);
            var preview=(byte[])Call(previewType,"Request",null,19U,5U,1U,0U,0U,7U);
            var deferred=(Task<bool>)Call(connection,"TryWritePreview",peer,preview,1,CancellationToken.None);
            Need(!deferred.GetAwaiter().GetResult() && semaphore.CurrentCount==1,"actual preview yields to IMU without taking a control lease");
            connection.GetField("imuReaders",All).SetValue(peer,0);
            var headless=(Task<byte[]>)Call(connection,"ReadImuSnapshot",peer,CancellationToken.None);
            Need(headless.GetAwaiter().GetResult()==null,"production headless IMU API is read-only on disconnected instance");
            Need(!(bool)connection.GetProperty("ImuPending",All).GetValue(peer),"IMU priority released after skipped view");
        }finally{if(semaphore.CurrentCount==0)semaphore.Release();((IDisposable)peer).Dispose();}
    }
    static void Retry(){
        var type=connection.Assembly.GetType("USB512_interface.NpBleImuRetry",true);var retry=Activator.CreateInstance(type,true);
        long now=100;Need((bool)Call(type,"CanAttempt",retry,now),"initial IMU attempt allowed");
        foreach(int delay in new[]{1000,2000,4000,8000,8000,8000}){
            Need((int)Call(type,"Defer",retry,now)==delay,"bounded automatic IMU retry");
            Need(!(bool)Call(type,"CanAttempt",retry,now+delay-1),"IMU retry respects backoff");
            now+=delay;Need((bool)Call(type,"CanAttempt",retry,now),"transient pressure never permanently disables IMU");
        }
        Call(type,"Reset",retry);Need((bool)Call(type,"CanAttempt",retry,0L),"reconnect/success clears backoff");
        Need((int)Call(type,"Defer",retry,now)==1000,"recovered IMU starts with short retry");
    }
    [STAThread] static int Main(string[] args){try{
        var assembly=Assembly.LoadFrom(Path.GetFullPath(args[0]));protocol=assembly.GetType("USB512_interface.NpBleImu",true);connection=assembly.GetType("USB512_interface.NpBleConnection",true);session=assembly.GetType("USB512_interface.NpBleImuSession",true);wire=assembly.GetType("USB512_interface.NpBleWire",true);
        Wire();Client();Handler(Path.GetFullPath(args[1]));Priority();Retry();Console.WriteLine("PASS: "+checks+" actual-compiled BLE IMU wire/client/handler/priority/retry checks; no hardware access.");return 0;
    }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
