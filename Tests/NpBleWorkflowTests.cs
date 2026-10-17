using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

static class NpBleWorkflowTests
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static Assembly app;static int checks;
    static Type T(string name){return app.GetType(name,true);}
    static object New(string name,params object[] args){return Activator.CreateInstance(T(name),BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,args,null);}
    static object Call(object target,string name,params object[] args){try{return (target as Type??target.GetType()).GetMethod(name,All).Invoke(target is Type?null:target,args);}catch(TargetInvocationException ex){throw ex.InnerException;}}
    static object Get(object target,string name){return target.GetType().GetField(name,All).GetValue(target);}
    static void Set(object target,string name,object value){target.GetType().GetField(name,All).SetValue(target,value);}
    static void Need(bool ok,string reason){checks++;if(!ok)throw new Exception(reason);}
    static void Reject(Action action,string reason){try{action();}catch(IOException){checks++;return;}throw new Exception(reason);}
    static object Await(object task){((Task)task).GetAwaiter().GetResult();return task.GetType().GetProperty("Result").GetValue(task);}
    static void Put(byte[] body,int at,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,body,at,4);}
    static byte[] Slice(byte[] data,int at,int size){var part=new byte[size];Buffer.BlockCopy(data,at,part,0,size);return part;}
    static byte[] Frame(byte command,byte[] body){var result=new byte[body.Length+4];result[0]=0xad;result[1]=command;body.CopyTo(result,2);result[result.Length-2]=result[result.Length-1]=0xda;return result;}
    static byte[] Preview(uint kind,uint band,uint channel,uint hz,uint offset,ulong tick,byte[] data){
        var body=new byte[224];uint stride=band==0?((hz+499)/500)*780:((hz+5999)/6000)*9360;
        uint[] words={0x3156504e,91,7,hz,(uint)tick,(uint)(tick>>32),offset|((kind==0?1560U:160U)<<16),1|(kind<<8)|(band<<12)|(channel<<16)};
        for(int i=0;i<words.Length;i++)Put(body,i*4,words[i]);data.CopyTo(body,32);Put(body,192,stride);return body;
    }
    static object Status(uint state,uint flags,uint start=8192){var s=New("CE32_console.NpSdStatus");Set(s,"State",state);Set(s,"Flags",flags);Set(s,"Mask",1U);Set(s,"Hz",7500U);Set(s,"Start",start);Set(s,"Capacity",1048576U);return s;}
    static void Wire(){
        foreach(var pair in new[]{Tuple.Create((byte)0xba,Preview(2,0,0,7500,65535,60,new byte[160])),Tuple.Create((byte)0x94,new byte[512]),Tuple.Create((byte)0x95,BitConverter.GetBytes(3U)),Tuple.Create((byte)0x90,new byte[512])}){
            var frame=Frame(pair.Item1,pair.Item2);
            for(int split=1;split<frame.Length;split++){
                var parser=New("USB512_interface.NpBleWire");Need(((IList)Call(parser,"Feed",Slice(frame,0,split))).Count==0,"Native/preview partial frame emitted");
                var replies=(IList)Call(parser,"Feed",Slice(frame,split,frame.Length-split));Need(replies.Count==1,"Fragmented native/NPV1 frame lost");
                Need((byte)replies[0].GetType().GetProperty("Item1").GetValue(replies[0])==pair.Item1,"Native reply misrouted");
            }
        }
        var native=new List<byte>();native.AddRange(Frame(0x95,new byte[4]));native.AddRange(Frame(0x90,new byte[512]));
        Need(((IList)Call(New("USB512_interface.NpBleWire"),"Feed",native.ToArray())).Count==2,"Coalesced delete/readback split lost");
    }
    static void Word(byte[] data,int at,int line,int word,uint value){for(int bit=0;bit<10;bit++)if((value&(1U<<(9-bit)))!=0)data[at+word*10+bit]|=(byte)(1<<line);}
    static void PreviewDecode(){
        foreach(uint hz in new uint[]{7500,15000,20000,30000})foreach(uint band in new uint[]{0,1})foreach(uint channel in new uint[]{0,127,383}){
            var raw=new byte[160];uint adc=(channel/24)*2+(channel&1),sample=(channel%24)/2,word=adc<30?adc%5:adc-30,line=adc<30?adc/5+1:7;
            for(int i=0;i<16;i++)Word(raw,i*10,(int)line,0,(uint)i*67);
            uint phase=37,offset=phase+(band==0?(sample+1)*60:sample*780)+word*10;
            var burst=New("CE32_console.NpV1PreviewBurst",91U);Need((bool)Call(burst,"Accept",Preview(2,band,channel,hz,32767,offset,raw)),"V1 rolling packet rejected");
            var values=(double[])Call(burst,"DecodeRolling",phase);for(int i=0;i<15;i++)Need(values[i]==i*67,"V1 ADC bit lane changed");Need(Double.IsNaN(values[15]),"Missing V1 sample fabricated");
            Reject(()=>Call(burst,"DecodeRolling",phase+1),"Wrong session-origin phase admitted");
        }
        var origin=new byte[1560];for(int f=0;f<13;f++){int at=37+f*60;Word(origin,at,0,0,f==0?0x330U:0xcfU);Word(origin,at,7,2,0x11);Word(origin,at,7,3,(uint)(900+f)>>10);Word(origin,at,7,4,(uint)(900+f)&1023);}
        var calibration=New("CE32_console.NpV1PreviewBurst",91U);
        for(int at=0;at<origin.Length;at+=160)Need((bool)Call(calibration,"Accept",Preview(0,1,383,7500,(uint)at,0,Slice(origin,at,Math.Min(160,origin.Length-at))))==(at+160>=origin.Length),"Origin fragment completion mismatch");
        Need((int)Call(calibration,"FindPhase")==37,"V1 origin phase not recovered");
        calibration=New("CE32_console.NpV1PreviewBurst",91U);Call(calibration,"Accept",Preview(0,0,0,7500,0,0,Slice(origin,0,160)));
        Reject(()=>Call(calibration,"Accept",Preview(0,0,0,7500,320,0,Slice(origin,320,160))),"Missing origin fragment accepted");
        Need((bool)Call(T("USB512_interface.NpV1BlePreviewForm"),"CanPreview",Status(1,128|4)),"Active flag incorrectly hides recording preview");
        foreach(uint flags in new uint[]{4,128|4|8,128|4|131072,128|4|262144})Need(!(bool)Call(T("USB512_interface.NpV1BlePreviewForm"),"CanPreview",Status(1,flags)),"Pending/unadvertised preview admitted");
        foreach(uint state in new uint[]{0,2,3,4})Need(!(bool)Call(T("USB512_interface.NpV1BlePreviewForm"),"CanPreview",Status(state,128)),"Stopped/draining preview admitted");
        using(var source=(IDisposable)New("USB512_interface.NpBleConnection")){
            var lease=(SemaphoreSlim)Get(source,"transaction");lease.Wait();
            try{var request=(byte[])Call(T("CE32_console.NpV1PreviewBurst"),"Request",91U,2U,1U,0U,0U,7U,0U);Need(!(bool)Await(Call(source,"TryWritePreview",request,0,CancellationToken.None)),"V1 preview bypassed control priority");}finally{lease.Release();}
            using(var form=(Form)New("USB512_interface.NpV1BlePreviewForm",source,null,(Func<bool>)(()=>false),null)){
                foreach(var pair in new[]{Tuple.Create("activeId",91U),Tuple.Create("activeProbe",1U),Tuple.Create("activeChannel",0U),Tuple.Create("activeBand",0U),Tuple.Create("session",7U),Tuple.Create("hz",7500U)})Set(form,pair.Item1,pair.Item2);
                Call(form,"Receive",1,Preview(2,0,0,7500,65535,60,new byte[160]));Need(((ICollection)Get(form,"pending")).Count==0,"Stale preview epoch accepted");
                for(int i=0;i<10;i++)Call(form,"Receive",0,Preview(2,0,0,7500,65535,60+(ulong)i*187200,new byte[160]));
                Need(((ICollection)Get(form,"pending")).Count==8 && (int)Get(form,"dropped")==2,"V1 preview receive queue unbounded");Need(!form.TopMost,"BLE preview always on top");
                form.Dispose();Need(Get(source,"PreviewReceived")==null,"Closed V1 preview retained its callback");
            }
        }
    }
    static byte[] Page(params uint[] ends){var data=new byte[512];Put(data,0,(uint)ends.Length);for(int i=0;i<ends.Length;i++){Put(data,4+4*i,ends[i]);Put(data,256+4*i,ends[i]);}return data;}
    static object Catalog(object status,Func<ushort,Task<byte[]>> read){return Await(Call(T("USB512_interface.NpBleCatalog"),"ReadV1",status,read));}
    static void Catalogs(){
        int reads=0;var page=Page(32768,65536);Func<ushort,Task<byte[]>> read=index=>{reads++;Need(index==0,"Unexpected page");return Task.FromResult(page);};
        var catalog=Catalog(Status(3,17,65536),read);Need(((IList)Get(catalog,"Records")).Count==2 && reads==1,"Catalog did not list two recordings");
        Need((bool)Call(catalog,"SameAs",Catalog(Status(3,17,65536),read)),"Identical confirmation snapshot differs");
        page[400]=1;Need(!(bool)Call(catalog,"SameAs",Catalog(Status(3,17,65536),read)),"Catalog snapshot was not immutable");
        foreach(var invalid in new[]{Page(8192),Page(1048577),Page(32768,16384),new byte[8]})Reject(()=>Catalog(Status(3,17,65536),index=>Task.FromResult(invalid)),"Corrupt catalog admitted");
        var mismatch=Page(32768);Put(mismatch,256,32769);Reject(()=>Catalog(Status(3,17,32768),index=>Task.FromResult(mismatch)),"Event extent mismatch accepted");
        Reject(()=>Catalog(Status(3,17,8192),index=>Task.FromResult(Page(32768))),"Changed catalog start accepted");
        foreach(var state in new[]{Status(1,21),Status(2,21),Status(0,17|8),Status(0,1),Status(0,17|131072)}){
            reads=0;Reject(()=>Catalog(state,index=>{reads++;return Task.FromResult(Page());}),"Active/unmounted catalog read admitted");Need(reads==0,"Refused catalog touched transport");
        }
        var ends=new uint[63];for(uint i=0;i<63;i++)ends[i]=32768+i*8192;
        reads=0;catalog=Catalog(Status(3,17,ends[62]),index=>{reads++;return Task.FromResult(index==0?Page(ends):Page());});Need(reads==2 && ((IList)Get(catalog,"Records")).Count==63,"63-entry page boundary lost");
    }
    static int storageReads;
    static Func<Task<TValue>> Read<TValue>(object value){return ()=>{storageReads++;return Task.FromResult((TValue)value);};}
    static Func<TValue> StatusGetter<TValue>(object value){return ()=>(TValue)value;}
    static Func<TValue,Task<TValue>> Delete<TValue>(){return value=>{throw new Exception("Unexpected deletion in UI check");};}
    static object Generic(string method,params object[] args){return typeof(NpBleWorkflowTests).GetMethod(method,All).MakeGenericMethod(T("USB512_interface.NpBleCatalog")).Invoke(null,args);}
    static void V1ActiveProbe(){
        using(var connection=(IDisposable)New("USB512_interface.NpBleConnection"))foreach(uint mask in new uint[]{1,2,4,6,7}){
            var status=Status(1,128|4);Set(status,"Mask",mask);
            var getter=typeof(NpBleWorkflowTests).GetMethod("StatusGetter",All).MakeGenericMethod(T("CE32_console.NpSdStatus")).Invoke(null,new[]{status});
            using(var form=(Form)New("USB512_interface.NpV1BlePreviewForm",connection,getter,(Func<bool>)(()=>false),null)){
                int selected=(int)((NumericUpDown)Get(form,"probe")).Value;
                Need((mask&(1U<<(selected-1)))!=0,"V1 preview opened on an inactive probe");
                Need(((CheckBox)Get(form,"enabled")).Checked,"Selecting the initial active probe paused preview");
            }
        }
    }
    static void V1StorageContract(){
        var status=Status(0,1|16384);Set(status,"LastId",0xf0000000U);
        var request=(byte[])Call(T("CE32_console.NpV1BleProtocol"),"Format",status,1048576U);
        Need(BitConverter.ToUInt32(request,8)==5 && BitConverter.ToUInt32(request,36)==0x46524d54U,"V1 FORMAT opcode/key differs from firmware contract");
        Need(BitConverter.ToUInt32(request,12)==0xf0000001U,"V1 FORMAT did not follow fresh device mutation ID");
        foreach(int at in new[]{16,20,24,28,32,40,44,48,52,56})Need(BitConverter.ToUInt32(request,at)==0,"V1 FORMAT ignored-argument/reserved field set");
        Need(BitConverter.ToUInt32(request,60)==(uint)Call(T("CE32_console.NpSdControl"),"Crc",request,60),"V1 FORMAT CRC invalid");
        Set(status,"LastId",UInt32.MaxValue);request=(byte[])Call(T("CE32_console.NpV1BleProtocol"),"Format",status,1048576U);Need(BitConverter.ToUInt32(request,12)==1,"V1 FORMAT wrap generated zero transaction");
        Reject(()=>Call(T("CE32_console.NpV1BleProtocol"),"Format",status,1048575U),"V1 FORMAT accepted changed card capacity");
        foreach(uint flags in new uint[]{0,1|4,1|8,1|131072,1|262144}){Set(status,"Flags",flags);Reject(()=>Call(T("CE32_console.NpV1BleProtocol"),"Format",status,1048576U),"V1 FORMAT admitted active/pending/unwritable storage");}
        // A catalog/mount fault is not an active writer. FORMAT is recovery,
        // and this firmware deliberately retains old recorder state/fault.
        foreach(uint state in new uint[]{0,3,4}){
            status=Status(state,1|16384,8192);Set(status,"Fault",7U);Set(status,"LastId",90U);
            request=(byte[])Call(T("CE32_console.NpV1BleProtocol"),"Format",status,1048576U);
            Need(BitConverter.ToUInt32(request,8)==5 && BitConverter.ToUInt32(request,12)==91,"Stopped fault/unmounted card cannot be formatted");
            Need(!(bool)Call(T("CE32_console.NpV1BleProtocol"),"CanStart",status),"Formatting eligibility enabled recording on faulty storage");
            Set(status,"Flags",17U|16384U);Set(status,"LastId",91U);
            Call(T("CE32_console.NpV1BleProtocol"),"RequireStorageCompleted",status,1048576U,91U,true);
            var empty=Catalog(status,index=>Task.FromResult(Page()));
            Need(((IList)Get(empty,"Records")).Count==0 && !(bool)empty.GetType().GetProperty("CanDeleteLast",All).GetValue(empty,null),"Formatted faulted catalog could not be read or exposed delete");
            Need((bool)Call(T("CE32_console.NpV1BleProtocol"),"CanManualStart",status),"Explicit manual START cannot rearm formatted finalized capture");
            Need((bool)Call(T("CE32_console.NpV1BleProtocol"),"CanSelectProfile",status,1U,7500U),"Manual profile selection blocked by historical fault");
            uint end=(uint)Call(T("CE32_console.NpV1BleProtocol"),"EndLba",status,1U,50U,true);
            Need(end>8192 && (end&16383)==0,"Manual restart reservation lost allocation-unit bounds");
            Set(status,"Flags",17U|16384U|8192U|1024U);
            Reject(()=>Call(T("CE32_console.NpV1BleProtocol"),"ScheduledStart",status,92U,end,1U,1700000000000000UL,false),"Scheduled START inherited manual rearm policy");
            Reject(()=>Call(T("CE32_console.NpV1BleProtocol"),"Mode",status,92U,2U),"MODE inherited manual rearm policy");
            Reject(()=>Call(T("CE32_console.NpV1BleProtocol"),"Mount",status,92U),"MOUNT inherited format recovery policy");
            Reject(()=>Call(T("CE32_console.NpV1BleProtocol"),"Recover",status,92U),"RECOVER inherited format recovery policy");
        }
        foreach(string invalid in new[]{"running","draining","active","pending","waiting","preparing","no-card","capacity","stale-id","error","not-empty"}){
            status=Status(4,17,8192);Set(status,"Fault",7U);Set(status,"LastId",91U);
            if(invalid=="running")Set(status,"State",1U);if(invalid=="draining")Set(status,"State",2U);
            if(invalid=="active")Set(status,"Flags",21U);if(invalid=="pending")Set(status,"Flags",25U);
            if(invalid=="waiting")Set(status,"Flags",17U|131072U);if(invalid=="preparing")Set(status,"Flags",17U|262144U);
            if(invalid=="no-card")Set(status,"Flags",1U);if(invalid=="capacity")Set(status,"Capacity",1048575U);
            if(invalid=="stale-id")Set(status,"LastId",90U);if(invalid=="error")Set(status,"Error",7U);if(invalid=="not-empty")Set(status,"Start",32768U);
            Reject(()=>Call(T("CE32_console.NpV1BleProtocol"),"RequireStorageCompleted",status,1048576U,91U,true),"V1 FORMAT completion accepted "+invalid);
            if(invalid=="running"||invalid=="draining"||invalid=="active"||invalid=="pending"||invalid=="waiting"||invalid=="preparing")
            {
                Reject(()=>Call(T("CE32_console.NpV1BleProtocol"),"Format",status,1048576U),"V1 FORMAT eligibility accepted "+invalid);
                Need(!(bool)Call(T("CE32_console.NpV1BleProtocol"),"CanManualStart",status),"Manual START admitted owned storage: "+invalid);
                Need(!(bool)Call(T("CE32_console.NpV1BleProtocol"),"CanSelectProfile",status,1U,7500U),"Manual SELECT admitted owned storage: "+invalid);
            }
        }
        status=Status(0,1|16384,8192);Set(status,"Capacity",8192U);
        Reject(()=>Call(T("CE32_console.NpV1BleProtocol"),"Format",status,8192U),"Missing/unusable card accepted");
        status=Status(0,1|16384);
        Set(status,"Flags",1U|8192U);
        foreach(uint destination in new uint[]{1,2,3,4}){
            request=(byte[])Call(T("CE32_console.NpV1BleProtocol"),"Mode",status,1U,destination);
            Need(BitConverter.ToUInt32(request,8)==7 && BitConverter.ToUInt32(request,16)==destination,"V1 NLD1 destination was remapped to a boot target");
            Need(BitConverter.ToUInt32(request,40)==0 && BitConverter.ToUInt32(request,60)==(uint)Call(T("CE32_console.NpSdControl"),"Crc",request,60),"V1 mode command has invalid mode field/CRC");
        }
    }
    static void V2CatalogWireAndUi(){
        const uint capability=0x00400000;
        Need(!(bool)Call(T("USB512_interface.NpV2BleCatalog"),"CanRead",Status(1,128|17)),"V2 catalog admitted without capability");
        Need((bool)Call(T("USB512_interface.NpV2BleCatalog"),"CanRead",Status(1,capability|128|17|65536)),"FULL V2 catalog listing blocked");
        Need((bool)Call(T("USB512_interface.NpV2BleCatalog"),"CanRead",Status(1,capability|128|17|131072)),"Mounted recovery catalog cannot be listed read-only");
        Need(!(bool)Call(T("USB512_interface.NpV2BleCatalog"),"CanRead",Status(1,capability|128|17|8)),"Pending V2 catalog admitted");
        byte[] request=(byte[])Call(T("USB512_interface.NpV2BleCatalog"),"InfoRequest",71U);
        var body=new byte[64];uint[] words={0x32524c4e,2,9,71,0,0xfffffffe,0,8192,1048576,0,0,UInt32.MaxValue,0,0,0};for(int i=0;i<words.Length;i++)Put(body,i*4,words[i]);Put(body,60,(uint)Call(T("CE32_console.NpSdControl"),"Crc",body,60));
        Need(((IList)Call(New("USB512_interface.NpBleWire"),"Feed",Frame(0xb9,body))).Count==1,"INFO token misinterpreted as STATUS state");
        Need((bool)Call(T("USB512_interface.NpBleConnection"),"Matches",(byte)0xb9,request,(byte)0xb9,body),"INFO receipt not correlated by shared BLE connection");
        var stale=(byte[])body.Clone();Put(stale,12,72);Put(stale,60,(uint)Call(T("CE32_console.NpSdControl"),"Crc",stale,60));Need(!(bool)Call(T("USB512_interface.NpBleConnection"),"Matches",(byte)0xb9,request,(byte)0xb9,stale),"Stale INFO transaction accepted");
        foreach(uint state in new uint[]{1,3}){
            Put(body,24,1);Put(body,28,32768);Put(body,44,0);Put(body,48,8192);Put(body,52,32768);Put(body,60,(uint)Call(T("CE32_console.NpSdControl"),"Crc",body,60));
            var info=Call(T("USB512_interface.NpV2BleCatalog"),"ParseInfo",body,71U);var catalog=New("USB512_interface.NpBleCatalog");Set(catalog,"V2",info);Set(catalog,"Capacity",1048576U);Set(catalog,"NextStart",32768U);
            var record=New("USB512_interface.NpBleRecord");Set(record,"Id",1U);Set(record,"Start",8192U);Set(record,"End",32768U);Set(record,"Flags",13U|(state<<8)|(1U<<16)|(1U<<18)|(state==3?1U<<17:0U));((IList)Get(catalog,"Records")).Add(record);
            using(var form=(Form)New("USB512_interface.NpBleStorageForm","V2 · TEST",1048576U,Generic("Read",catalog),Generic("Delete"),(Func<bool,Task>)(format=>{throw new Exception("Unexpected card mutation");}),true)){
                ((Task)Call(form,"Run",(Func<Task>)(()=>(Task)Call(form,"RefreshRecords")))).GetAwaiter().GetResult();
                Need(((Button)Get(form,"remove")).Enabled==(state==3),"V2 delete eligibility not reflected in UI");
                Need(((ListView)Get(form,"records")).Items[0].SubItems[4].Text==(state==3?"Complete":"Reserved"),"V2 file state missing from UI");
            }
        }
    }
    static void StorageUi(string output){
        var catalog=Catalog(Status(3,17,65536),index=>Task.FromResult(Page(32768,65536)));
        using(var recovery=(Form)New("USB512_interface.NpBleStorageForm","V1 · TEST",1048576U,Generic("Read",catalog),Generic("Delete"),(Func<bool,Task>)(format=>{throw new Exception("Unexpected recovery format");}),true)){
            int before=storageReads;recovery.StartPosition=FormStartPosition.Manual;recovery.Location=new Point(-20000,-20000);recovery.Show();Application.DoEvents();
            Need(storageReads==before && ((Label)Get(recovery,"info")).Text.Contains("Format does not require"),"Opening recovery dialog issued a prerequisite catalog read");
            ((Task)Call(recovery,"Run",(Func<Task>)(()=>{throw new IOException("Outdated catalog; cannot list recordings");}))).GetAwaiter().GetResult();
            var controls=(FlowLayoutPanel)Get(recovery,"actions");
            Need(controls.Enabled && controls.Controls.Cast<Control>().Single(c=>c.Text=="Format card").Enabled,"Unreadable catalog disabled the recovery Format control");
            Need(!((Button)Get(recovery,"remove")).Enabled,"Failed catalog read left delete enabled");
        }
        foreach(bool canList in new[]{true,false})using(var form=(Form)New("USB512_interface.NpBleStorageForm","V1 · TEST",1048576U,Generic("Read",catalog),Generic("Delete"),(Func<bool,Task>)(format=>{throw new Exception("Unexpected card mutation in UI check");}),canList)){
            Need(form.Text.Contains("V1 · TEST") && !form.TopMost,"Storage target missing or window always on top");
            var actions=(FlowLayoutPanel)Get(form,"actions");Need(actions.Controls.Count==4,"Storage actions missing");Need(actions.Controls[0].Enabled==canList,"Unsupported listing can send a request");
            if(!canList)continue;
            ((Task)Call(form,"RefreshRecords")).GetAwaiter().GetResult();var list=(ListView)Get(form,"records");
            Need(list.Columns.Count==5 && list.Items.Count==2 && list.Items[1].SubItems[0].Text=="2","Recording table did not display IDs and extents");
            var waiting=new TaskCompletionSource<bool>();var running=(Task)Call(form,"Run",(Func<Task>)(()=>waiting.Task));
            Need(!actions.Enabled,"Pending file operation left destructive buttons enabled");var close=new FormClosingEventArgs(CloseReason.UserClosing,false);Call(form,"OnFormClosing",close);Need(close.Cancel,"Closed a pending file operation");
            waiting.SetResult(true);while(!running.IsCompleted){Application.DoEvents();Thread.Sleep(1);}running.GetAwaiter().GetResult();Need(actions.Enabled && ((Button)Get(form,"remove")).Enabled,"Completed operation stranded storage controls");
            using(var host=new Form{ClientSize=form.ClientSize,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-20000,-20000),Padding=form.Padding,Font=form.Font,BackColor=form.BackColor}){
                var controls=new List<Control>();foreach(Control control in form.Controls)controls.Add(control);foreach(var control in controls)host.Controls.Add(control);
                host.Show();Application.DoEvents();Need(list.Bottom<=((Label)Get(form,"info")).Top,"Recording rows overlap summary");
                if(output!=null)using(var bitmap=new Bitmap(host.Width,host.Height)){host.DrawToBitmap(bitmap,new Rectangle(Point.Empty,host.Size));bitmap.Save(output);}
            }
        }
    }
    [STAThread]static int Main(string[] args){try{app=Assembly.LoadFrom(args[0]);Wire();PreviewDecode();Catalogs();StorageUi(args.Length>1?args[1]:null);V2CatalogWireAndUi();V1ActiveProbe();V1StorageContract();Console.WriteLine("PASS "+checks+" actual-assembly BLE V1/V2 preview/framing/catalog/UI checks; no hardware.");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
