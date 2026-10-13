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
using System.Windows.Forms;

// Exercise the built application's pure contracts. No discovery, GATT, USB or disk access.
internal static class BleFeaturesTests
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static Assembly app;static int checks;
    static Type T(string name){return app.GetType(name,true);}
    static object New(string name,params object[] args){return Activator.CreateInstance(T(name),BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,args,null);}
    static object Call(object target,string method,params object[] args){
        try{return (target as Type??target.GetType()).GetMethod(method,All).Invoke(target is Type?null:target,args);}
        catch(TargetInvocationException ex){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();throw;}
    }
    static object Field(object target,string name){return target.GetType().GetField(name,All).GetValue(target);}
    static object Property(object target,string name){return target.GetType().GetProperty(name,All).GetValue(target,null);}
    static void Check(bool condition,string message){checks++;if(!condition)throw new Exception(message);}
    static void Reject(Action action,string message){try{action();}catch(IOException){checks++;return;}throw new Exception(message);}
    static void Await(object task){((Task)task).GetAwaiter().GetResult();}
    static void Put(byte[] b,int offset,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,offset,4);}
    static uint U(byte[] b,int offset){return BitConverter.ToUInt32(b,offset);}
    static void Seal(byte[] b){Put(b,60,(uint)Call(T("CE32_console.NpSdControl"),"Crc",b,60));}
    static object Status(uint state,uint flags=256){var s=New("CE32_console.NpSdStatus");s.GetType().GetField("State",All).SetValue(s,state);s.GetType().GetField("Flags",All).SetValue(s,flags);return s;}
    sealed class ClockDevice
    {
        internal uint State=1,Generation=7;internal bool Valid=true,LostCommit;internal string Damage;
        internal int Reads,Commits,ReadTimeouts;internal long Offset;readonly Stopwatch elapsed=Stopwatch.StartNew();
        ulong anchor;uint reference,uncertainty=1000;
        internal Task<byte[]> Exchange(byte[] request){
            uint op=U(request,8),id=U(request,12),tick=1000+(uint)elapsed.ElapsedMilliseconds;
            Check(U(request,0)==0x3254504e && U(request,60)==(uint)Call(T("CE32_console.NpSdControl"),"Crc",request,60),"Clock request contract changed");
            if(op==0){Reads++;if(ReadTimeouts>0){ReadTimeouts--;throw new TimeoutException();}}
            else{Commits++;Check(op==1 && State==1,"Active recorder clock was committed");if(LostCommit)throw new TimeoutException("Lost commit");
                Check(U(request,32)==Generation,"Commit used a stale clock generation");reference=U(request,16);anchor=U(request,20)|((ulong)U(request,24)<<32);uncertainty=U(request,28);Generation++;}
            ulong utc=anchor==0?(ulong)((DateTime.UtcNow.Ticks-621355968000000000L)/10):anchor+(ulong)unchecked(tick-reference)*1000;
            utc=(ulong)((long)utc+Offset);
            var body=new byte[64];uint[] words={0x3252504e,1,op,id,0,tick,tick,Generation,uncertainty,Valid?1U:0U,(uint)utc,(uint)(utc>>32),State,1000};
            for(int i=0;i<words.Length;i++)Put(body,i*4,words[i]);
            if(Damage=="id")Put(body,12,id+1);if(Damage=="abi")Put(body,4,99);if(Damage=="state")Put(body,48,99);
            Seal(body);if(Damage=="crc")body[60]^=1;return Task.FromResult(body);
        }
    }
    static void Clocks()
    {
        uint id=90;Func<uint> next=()=>++id;
        var clock=New("USB512_interface.NpBleClock");var device=new ClockDevice{ReadTimeouts=1};
        Await(Call(clock,"Initialize",Status(1),(Func<byte[],Task<byte[]>>)device.Exchange,next));
        Check(device.Reads==17 && device.Commits==1 && (bool)Property(clock,"Synchronized"),"Stopped initialization did not collect 16 valid observations and one commit after a read timeout");
        Check((uint)Property(clock,"Generation")==8,"Acknowledged clock generation not retained");
        Await(Call(clock,"Observe",(Func<byte[],Task<byte[]>>)device.Exchange,next));
        Check(device.Commits==1 && device.Reads==18,"Periodic clock check changed the device clock");
        device.Offset=30000000;
        Reject(()=>Await(Call(clock,"Observe",(Func<byte[],Task<byte[]>>)device.Exchange,next)),"Large PC-time offset accepted");
        Check(!(bool)Property(clock,"Synchronized") && device.Commits==1,"Clock check failure retimed/replayed a mutation");
        device.Offset=0;Await(Call(clock,"Observe",(Func<byte[],Task<byte[]>>)device.Exchange,next));
        Check((bool)Property(clock,"Synchronized"),"Read-only check could not confirm the retained mapping");
        device.Generation++;
        Reject(()=>Await(Call(clock,"Observe",(Func<byte[],Task<byte[]>>)device.Exchange,next)),"Changed clock generation accepted");
        Check((uint)Property(clock,"Generation")==0,"Changed clock generation was retained");
        foreach(uint state in new uint[]{2,3,4,6,255}){
            clock=New("USB512_interface.NpBleClock");device=new ClockDevice{State=state};
            Await(Call(clock,"Initialize",Status(state),(Func<byte[],Task<byte[]>>)device.Exchange,next));
            Check(device.Reads==1 && device.Commits==0 && (bool)Property(clock,"Synchronized"),"Active attach did not adopt a valid clock using observation only");
        }
        foreach(string damage in new[]{"id","abi","crc","state","invalid","generation","offset"}){
            clock=New("USB512_interface.NpBleClock");device=new ClockDevice{State=3,Damage=damage,Valid=damage!="invalid",Generation=damage=="generation"?0U:7U,Offset=damage=="offset"?30000000:0};
            Reject(()=>Await(Call(clock,"Initialize",Status(3),(Func<byte[],Task<byte[]>>)device.Exchange,next)),"Invalid active clock accepted: "+damage);
            Check(device.Reads==1 && device.Commits==0,"Failed active attach sent extra clock commands");
        }
        clock=New("USB512_interface.NpBleClock");device=new ClockDevice{LostCommit=true};bool timeout=false;
        try{Await(Call(clock,"Initialize",Status(1),(Func<byte[],Task<byte[]>>)device.Exchange,next));}catch(TimeoutException){timeout=true;}
        Check(timeout && device.Commits==1 && !(bool)Property(clock,"Synchronized"),"Ambiguous clock commit was replayed/confirmed");
        clock=New("USB512_interface.NpBleClock");device=new ClockDevice();
        Reject(()=>Await(Call(clock,"Initialize",Status(1,0),(Func<byte[],Task<byte[]>>)device.Exchange,next)),"Missing clock capability accepted");
        Check(device.Reads+device.Commits==0,"Unsupported clock sent a request");
        device=new ClockDevice{State=1};
        Reject(()=>Await(Call(clock,"Initialize",Status(1,264),(Func<byte[],Task<byte[]>>)device.Exchange,next)),"Queued recorder was calibrated");
        Check(device.Commits==0,"Queued recorder received a clock commit");
    }
    static byte[] Preview(uint id=91,uint probe=1,uint channel=0,uint session=7,uint hz=20000,uint kind=2,uint offset=65535,uint total=192,ulong first=4,bool marked=true,byte[] payload=null){
        var body=new byte[224];uint[] words={0x3256504e,id,session,hz,(uint)first,(uint)(first>>32),offset|(total<<16),probe|((kind|(marked?64U:0U))<<8)|(channel<<16)};
        for(int i=0;i<words.Length;i++)Put(body,i*4,words[i]);if(payload!=null)Buffer.BlockCopy(payload,0,body,32,Math.Min(192,payload.Length));return body;
    }
    static byte[] Frame(byte command,byte[] body,bool legacy=true){var result=new byte[body.Length+(legacy?4:3)];result[0]=legacy?(byte)0xad:(byte)0x3c;result[1]=command;body.CopyTo(result,2);result[body.Length+2]=legacy?(byte)0xda:(byte)0x3e;if(legacy)result[body.Length+3]=0xda;return result;}
    static byte[] Join(params byte[][] pieces){var list=new List<byte>();foreach(var piece in pieces)list.AddRange(piece);return list.ToArray();}
    static byte[] Slice(byte[] bytes,int at,int count){var copy=new byte[count];Buffer.BlockCopy(bytes,at,copy,0,count);return copy;}
    static IList Feed(object wire,byte[] bytes){return (IList)Call(wire,"Feed",bytes);}
    static void PreviewWire()
    {
        var control=new byte[64];Put(control,0,0x32524c4e);Put(control,4,2);Put(control,12,91);Seal(control);
        var controlFrame=Frame(0xb9,control);var payload=Preview();
        foreach(bool legacy in new[]{false,true}){
            var frame=Frame(0xba,payload,legacy);
            for(int split=1;split<frame.Length;split++){
                var wire=New("USB512_interface.NpBleWire");Check(Feed(wire,Slice(frame,0,split)).Count==0,"Partial preview emitted");
                var frames=Feed(wire,Join(Slice(frame,split,frame.Length-split),controlFrame));
                Check(frames.Count==2 && (byte)FieldTuple(frames[0],"Item1")==0xba && (byte)FieldTuple(frames[1],"Item1")==0xb9,"Fragmented preview blocked/lost control receipt");
            }
        }
        var damaged=(byte[])controlFrame.Clone();damaged[20]^=1;
        var parser=New("USB512_interface.NpBleWire");
        Check(Feed(parser,Join(new byte[]{5,0xad,0xba,8,9},damaged,Frame(0xba,payload),controlFrame)).Count==2,"Noisy/corrupt mixed traffic damaged controls");
        parser=New("USB512_interface.NpBleWire");
        Check(Feed(parser,Join(Slice(Frame(0xba,payload),0,70),controlFrame)).Count==1,"Truncated marked preview held a complete control receipt");
        Check(((IList)Field(parser,"pending")).Count<=228,"Parser retained unbounded data");
        payload[32]=0xad;parser=New("USB512_interface.NpBleWire");
        Check(Feed(parser,Join(Frame(0xba,payload),controlFrame)).Count==1,"Impossible marked payload accepted");
        // Older unmarked firmware can retain the unused two high PSSI bits.
        payload=Preview(marked:false);payload[32]=0xad;parser=New("USB512_interface.NpBleWire");
        Check(Feed(parser,Frame(0xba,payload)).Count==1,"Legacy raw-eight-bit preview framing rejected");
        Check(!(bool)Call(T("USB512_interface.NpBleConnection"),"Matches",(byte)0xb9,control,(byte)0xba,payload),"Preview satisfied a control transaction");
    }
    static object FieldTuple(object value,string property){return value.GetType().GetProperty(property).GetValue(value,null);}
    static void PreviewDecode()
    {
        foreach(uint probe in new uint[]{1,2})foreach(uint hz in new uint[]{15000,20000,30000}){
            var raw=new byte[192];var expected=new ushort[16,3];
            for(int row=0;row<16;row++)for(int lane=0;lane<3;lane++)expected[row,lane]=(ushort)((row*271+lane*1013)&4095);
            expected[0,0]=0;expected[0,1]=4095;expected[0,2]=2048;
            for(int row=0;row<16;row++)for(int bit=0;bit<12;bit++)for(int lane=0;lane<3;lane++)raw[row*12+bit]|=(byte)(((expected[row,lane]>>(11-bit))&1)<<(lane+(int)(probe-1)*3));
            var burst=New("CE32_console.NpV2PreviewBurst",91U);
            Check((bool)Call(burst,"Accept",Preview(probe:probe,hz:hz,payload:raw)),"Complete rolling packet not accepted");
            var actual=(ushort[,])Call(burst,"DecodeRolling",0);
            for(int row=0;row<16;row++)for(int lane=0;lane<3;lane++)Check(actual[row,lane]==expected[row,lane],"Unsigned 12-bit ADC lane changed");
            Reject(()=>Call(burst,"DecodeRolling",1),"Wrong rolling phase accepted");
        }
        var rolling=New("CE32_console.NpV2PreviewBurst",91U);
        Check(!(bool)Call(rolling,"Accept",Preview(id:90)),"Stale preview ID accepted");
        foreach(byte[] invalid in new[]{Preview(session:0),Preview(hz:10000),Preview(hz:0),Preview(probe:3),Preview(channel:384),Preview(offset:0),Preview(total:193),Preview(kind:128,marked:false)})
            Reject(()=>Call(New("CE32_console.NpV2PreviewBurst",91U),"Accept",invalid),"Invalid preview metadata accepted");
        // Complete, phase-zero SuperSync for both probes; no assumed test-pattern edits.
        byte[] sync={0xd3,0x6a,0x59,0xc5,0x3e,0xa7,0x1d,0xe4,0xb2,0x4f,0x78,0x96};
        foreach(uint probe in new uint[]{1,2})foreach(uint hz in new uint[]{15000,20000,30000}){
            var raw=new byte[3200];
            for(int i=0;i<raw.Length;i++){int tick=i%100,frame=i%1600/100,value=0;
                if(tick==0)value=frame==0?7:0;else if(tick==2 || tick==3)for(int lane=0;lane<3;lane++){int bit=frame*6+lane*2+tick-2;value|=((sync[bit/8]>>(bit&7))&1)<<lane;}
                raw[i]=(byte)(value<<((int)(probe-1)*3));}
            var burst=New("CE32_console.NpV2PreviewBurst",91U);
            for(int offset=0;offset<3200;offset+=192)Check((bool)Call(burst,"Accept",Preview(probe:probe,hz:hz,kind:0,offset:(uint)offset,total:3200,first:0,payload:Slice(raw,offset,Math.Min(192,3200-offset))))==(offset+192>=3200),"Partial calibration accepted/lost");
            Check((int)Call(burst,"FindPhase")==0,"SuperSync phase not preserved");
        }
        var incomplete=New("CE32_console.NpV2PreviewBurst",91U);Call(incomplete,"Accept",Preview(kind:0,offset:0,total:3200,first:0));
        Reject(()=>Call(incomplete,"FindPhase"),"Incomplete calibration produced a waveform");
        Reject(()=>Call(incomplete,"Accept",Preview(kind:0,offset:384,total:3200,first:0)),"Calibration fragment gap accepted");
        incomplete=New("CE32_console.NpV2PreviewBurst",91U);Call(incomplete,"Accept",Preview(kind:0,offset:0,total:3200,first:0));
        Reject(()=>Call(incomplete,"Accept",Preview(kind:0,offset:192,total:3200,first:0,session:8)),"Mixed calibration session accepted");
        incomplete=New("CE32_console.NpV2PreviewBurst",91U);Call(incomplete,"Accept",Preview(kind:0,offset:0,total:3200,first:0,hz:15000));
        Reject(()=>Call(incomplete,"Accept",Preview(kind:0,offset:192,total:3200,first:0,hz:20000)),"Mixed calibration rate accepted");
    }
    static void PreviewLifetime()
    {
        var connection=New("USB512_interface.NpBleConnection");int epoch=(int)Property(connection,"Epoch");
        using(var stream=(IDisposable)New("USB512_interface.NpBlePreviewStream",connection,91U,1U,0U,7U,20000U)){
            Call(stream,"Receive",epoch+1,Preview());Call(stream,"Receive",epoch,Preview(id:90));Check(((IList)Call(stream,"Drain")).Count==0,"Stale connection/request reached preview consumer");
            for(int i=0;i<10;i++)Call(stream,"Receive",epoch,Preview(first:4+(ulong)i*1024000));
            Check(((IList)Call(stream,"Drain")).Count==8 && (int)Property(stream,"Dropped")==2,"Preview queue not bounded");
            stream.Dispose();Call(stream,"Receive",epoch,Preview());Check(((IList)Call(stream,"Drain")).Count==0,"Disposed consumer accepted a packet");
            Check(Field(connection,"PreviewReceived")==null,"Preview subscription survived disposal");
        }
        foreach(byte[] changed in new[]{Preview(session:8),Preview(hz:30000),Preview(probe:2),Preview(channel:1)})
            using(var stream=(IDisposable)New("USB512_interface.NpBlePreviewStream",connection,91U,1U,0U,7U,20000U)){
                Call(stream,"Receive",epoch,changed);Reject(()=>Call(stream,"Drain"),"Changed session/rate/selection reached display");
            }
        using(var stream=(IDisposable)New("USB512_interface.NpBlePreviewStream",connection,91U,1U,0U,7U,20000U)){
            Call(stream,"Receive",epoch,Preview(kind:1,total:3072,offset:0));Check(((IList)Call(stream,"Drain")).Count==0,"Incomplete gathered burst reached rolling display");
        }
        var request=(byte[])Call(T("CE32_console.NpV2PreviewBurst"),"Request",91U,2U,1U,0U,0U,7U);
        var transaction=(SemaphoreSlim)Field(connection,"transaction");transaction.Wait();
        try{var pending=(Task)Call(connection,"TryWritePreview",request,epoch,CancellationToken.None);Await(pending);Check(!(bool)Property(pending,"Result"),"Preview queued ahead of a control operation");}
        finally{transaction.Release();}
        Reject(()=>Await(Call(connection,"TryWritePreview",request,epoch+1,CancellationToken.None)),"Stale/disconnected preview attempted a native write");
        using(var cancellation=new CancellationTokenSource()){cancellation.Cancel();bool cancelled=false;
            try{Await(Call(connection,"TryWritePreview",request,epoch,cancellation.Token));}catch(OperationCanceledException){cancelled=true;}
            Check(cancelled,"Cancelled preview attempted delivery");}
        ((IDisposable)connection).Dispose();
    }
    static void Snapshot(string output)
    {
        var connection=New("USB512_interface.NpBleConnection");
        using(var form=(Form)New("USB512_interface.NpBlePreviewForm",connection,null,(Func<bool>)(()=>false)))
        using(var host=new Form{ClientSize=form.ClientSize,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-20000,-20000),Font=form.Font,BackColor=form.BackColor}){
            var children=new List<Control>();foreach(Control child in form.Controls)children.Add(child);foreach(var child in children)host.Controls.Add(child);
            host.Show();Application.DoEvents();using(var bitmap=new Bitmap(host.Width,host.Height)){host.DrawToBitmap(bitmap,new Rectangle(Point.Empty,host.Size));bitmap.Save(Path.Combine(output,"ble-preview.png"),ImageFormat.Png);}
            host.Hide();foreach(var child in children)form.Controls.Add(child);
        }
        Check(Field(connection,"PreviewReceived")==null,"Preview form retained its subscription");((IDisposable)connection).Dispose();
    }
    static void TraceReplay(string root)
    {
        foreach(string run in new[]{"run20","run30"}){
            var parser=New("USB512_interface.NpBleWire");var bursts=new Dictionary<uint,object>();var phases=new Dictionary<string,int>();
            int previews=0,calibrations=0,rolling=0,decoded=0,discarded=0;
            using(var reader=new BinaryReader(File.OpenRead(Path.Combine(root,run,"notify.bin")))){
                Check(System.Text.Encoding.ASCII.GetString(reader.ReadBytes(8))=="NP2RX001" && reader.ReadInt64()>0,"Wrong saved BLE trace format");
                while(reader.BaseStream.Position<reader.BaseStream.Length){
                    reader.ReadInt64();int size=reader.ReadInt32();Check(size>0 && size<=65536 && size<=reader.BaseStream.Length-reader.BaseStream.Position,"Truncated saved BLE trace");
                    foreach(var frame in Feed(parser,reader.ReadBytes(size))){
                        if((byte)FieldTuple(frame,"Item1")!=0xba)continue;previews++;
                        var body=(byte[])FieldTuple(frame,"Item2");uint id=U(body,4),kind=(U(body,28)>>8)&63;
                        object burst;if(kind==2 || !bursts.TryGetValue(id,out burst))burst=New("CE32_console.NpV2PreviewBurst",id);
                        bursts[id]=burst;
                        try{
                            if(!(bool)Call(burst,"Accept",body))continue;
                            string key=Field(burst,"Probe")+":"+Field(burst,"Session")+":"+Field(burst,"Hz");
                            if(kind==0){phases[key]=(int)Call(burst,"FindPhase");calibrations++;}
                            if(kind==2){rolling++;int phase;if(phases.TryGetValue(key,out phase)){
                                var samples=(ushort[,])Call(burst,"DecodeRolling",phase);Check(samples.GetLength(0)==16 && samples.GetLength(1)==3,"Saved rolling geometry changed");
                                foreach(ushort value in samples)Check(value<=4095,"Saved ADC code changed range");decoded++;
                            }}
                        }catch(IOException){discarded++;bursts.Remove(id);} // Disposable gaps/errors never produce partial data.
                    }
                }
            }
            Check(previews>0 && rolling>0 && decoded>0,"Saved firmware preview did not pass the bundled decoder");
            Console.WriteLine("PASS "+run+" preview replay: "+previews+" BA frames, "+calibrations+" calibrated bursts, "+rolling+" rolling packets, "+decoded+" phase-validated decodes, "+discarded+" disposable errors/gaps; no analog-quality claim.");
        }
    }
    static void ConnectTrace(string source=null)
    {
        if(source!=null){
            string text=File.ReadAllText(source);
            const string discovery="GetGattServicesForUuidAsync(ServiceId,BluetoothCacheMode.Uncached)";
            int first=text.IndexOf(discovery,StringComparison.Ordinal),second=first<0?-1:text.IndexOf(discovery,first+discovery.Length,StringComparison.Ordinal);
            Check(first>=0 && second>first && text.IndexOf(discovery,second+discovery.Length,StringComparison.Ordinal)<0,"FFF0 discovery/retry is not exactly UUID-specific and uncached.");
            Check(!text.Contains("GetGattServicesAsync(BluetoothCacheMode.Uncached)"),"Full-service uncached discovery remains in the connection path.");
            Check(text.Contains("if(allowWarm && warmType.HasValue)") && text.Contains("lastResolvedWarm=true"),"Only an exact retained address/type may take the warm route.");
            Check(text.Contains("if(!warm || freshFallback)") && text.Contains("ResolveAddress(address,timeout.Token,value=>{stage=value;TraceConnect(elapsed,value);},false)") && text.Contains("catch (OperationCanceledException error)"),"Warm discovery failure lacks one bounded cancellable fresh-observation fallback.");
        }
        using(var connection=(IDisposable)New("USB512_interface.NpBleConnection")){
            Check(!String.IsNullOrEmpty((string)Property(connection,"LastConnectDiagnostic")),"Missing initial connect diagnostic");
            Call(connection,"ResetConnectDiagnostic",0xF5D68A6BCC7CUL);
            string reset=(string)Property(connection,"LastConnectDiagnostic");
            Check(reset.Contains("address=F5D68A6BCC7C") && reset.Contains("0ms begin"),"Connect trace lost exact selected address");
            var elapsed=Stopwatch.StartNew();
            Call(connection,"TraceConnect",elapsed,"advertisement observed; observed_type=Random");
            Call(connection,"TraceConnect",elapsed,"FFF0 service discovery");
            string trace=(string)Property(connection,"LastConnectDiagnostic");
            Check(trace.Contains("ms advertisement observed; observed_type=Random") && trace.Contains("ms FFF0 service discovery"),"Connect trace lost stage/type timing");
            Call(connection,"Disconnect");
            Check((string)Property(connection,"LastConnectDiagnostic")==trace,"Disconnect erased diagnostic evidence");
            Call(connection,"ResetConnectDiagnostic",0xF5D68A6BCC7DUL);
            reset=(string)Property(connection,"LastConnectDiagnostic");
            Check(reset.Contains("address=F5D68A6BCC7D") && !reset.Contains("observed_type"),"New connect did not reset old diagnostic stages");
            Check(T("USB512_interface.NpBleConnection").GetProperty("LastConnectDiagnostic",All).GetSetMethod(true)==null,"Connect diagnostic is externally writable");
        }
    }
    [STAThread] static int Main(string[] args){
        try{app=Assembly.LoadFrom(Path.GetFullPath(args[0]));
            if((args.Length==2 || args.Length==3) && args[1]=="--connect-trace"){
                ConnectTrace(args.Length==3?args[2]:null);Console.WriteLine("PASS "+checks+" connection-diagnostic/source checks; no GUI, BLE discovery or device commands.");return 0;
            }
            Application.EnableVisualStyles();Clocks();PreviewWire();PreviewDecode();PreviewLifetime();Snapshot(args[1]);
            if(args.Length==3)TraceReplay(args[2]);
            Console.WriteLine("PASS "+checks+" BLE feature checks: stopped-only automatic clock commit, read-only active/periodic checks, correlated preview framing/ADC/session/lease lifecycle and bounded queues; no hardware discovery or commands.");return 0;}
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
