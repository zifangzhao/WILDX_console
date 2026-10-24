using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

static class NpBleClockEvidenceTests {
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static Assembly app;static int checks;static JavaScriptSerializer json=new JavaScriptSerializer();
    static Type T(string name){return app.GetType("USB512_interface."+name,true);}
    static object New(string name,params object[] args){return args.Length==0?Activator.CreateInstance(T(name),true):Activator.CreateInstance(T(name),All,null,args,null);}
    static object Call(object target,string name,params object[] args){
        var type=target as Type??target.GetType();try{return type.GetMethods(All).Single(m=>m.Name==name && m.GetParameters().Length==args.Length).Invoke(target is Type?null:target,args);}catch(TargetInvocationException ex){throw ex.InnerException;}
    }
    static void Set(object item,string name,object value){item.GetType().GetField(name,All).SetValue(item,value);}
    static void Need(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static void Put(byte[] b,int at,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,at,4);}
    static uint Crc(byte[] b){uint c=0xffffffff;for(int i=0;i<60;i++){c^=b[i];for(int k=0;k<8;k++)c=c>>1^((0U-(c&1))&0xedb88320);}return ~c;}
    static byte[] Request(uint id,bool commit=false){var b=new byte[64];Put(b,0,0x3254504e);Put(b,4,1);Put(b,8,commit?1U:0U);Put(b,12,id);Put(b,60,Crc(b));return b;}
    static byte[] Reply(uint id,uint rx=100,uint tx=103){var b=new byte[64];Put(b,0,0x3252504e);Put(b,4,1);Put(b,12,id);Put(b,20,rx);Put(b,24,tx);Put(b,28,7);Put(b,48,3);Put(b,52,1000);Put(b,60,Crc(b));return b;}
    static Dictionary<string,object>[] Rows(string path){return File.ReadAllLines(path).Select(x=>json.Deserialize<Dictionary<string,object>>(x)).ToArray();}
    static long L(object value){return Convert.ToInt64(value);}
    static void Evidence(string root){
        string folder=Path.Combine(root,"connection1"),record=Path.Combine(root,"host-receipt");Directory.CreateDirectory(record);
        string sentinel=Path.Combine(record,"neural.dat");File.WriteAllBytes(sentinel,new byte[]{2,7,9});
        var context=New("NpBleClockContext");Set(context,"device_uid","001A0004434250062039314E");Set(context,"host_recording_receipt",record);Set(context,"status_state",(uint?)3);Set(context,"status_start_lba",(uint?)40960);
        var journal=New("NpBleClockEvidence",folder,"BLE-112233445566",10);long send=Stopwatch.GetTimestamp(),receive=send+Stopwatch.Frequency/100;
        var attempt=Call(journal,"Begin",Request(91),send,context);Set(context,"device_uid","changed-after-send");
        Call(journal,"Finish",attempt,Reply(91),receive,null);
        Need(Call(journal,"Begin",Request(92,true),send,context)==null,"clock SET/commit excluded from evidence collector");
        Call(journal,"Gap",context,"IMU priority",receive);
        var cancelled=Call(journal,"Begin",Request(93),receive,context);Call(journal,"Finish",cancelled,null,receive+1,new OperationCanceledException("foreground control priority"));
        var corrupt=Call(journal,"Begin",Request(94),receive+2,context);var bad=Reply(94);bad[60]^=1;Call(journal,"Finish",corrupt,bad,receive+3,null);
        var wrap=Call(journal,"Begin",Request(95),receive+4,context);Call(journal,"Finish",wrap,Reply(95,UInt32.MaxValue,2),receive+5,null);
        var unfinished=Call(journal,"Begin",Request(96),receive+6,context);Call(journal,"Close",receive+7,"connection ended");Call(journal,"Finish",unfinished,Reply(96),receive+8,null);
        var rows=Rows(Path.Combine(folder,"ble_clock_exchange_evidence.jsonl"));var first=rows.Single(r=>(string)r["event"]=="exchange_finished" && L(r["request_id"])==91);
        Need(L(first["host_send_qpc"])==send && L(first["host_receive_qpc"])==receive,"exact PC QPC brackets retained");
        Need(L(first["mcu_rx_ms"])==100 && L(first["mcu_tx_ms"])==103 && L(first["mcu_counter_hz"])==1000,"full MCU Rx/Tx fields retained");
        Need(L(first["clock_mapping_generation"])==7 && !(bool)first["device_reported_mapping_valid"],"raw observation survives absent device time mapping");
        Need((string)first["outcome"]=="reply_validated" && ((string)first["request_hex"]).Length==128 && ((string)first["reply_hex"]).Length==128,"raw64B request/reply retained");
        var captured=(Dictionary<string,object>)first["context"];
        Need((string)captured["device_uid"]=="001A0004434250062039314E" && !(bool)captured["on_card_recording_association_verified"],"exact identity is captured at send; no synthetic SD identity");
        Need(first["ap_source_pair"]==null && first["device_boot_domain"]==null && first["acquisition_generation"]==null,"unavailable AP/boot fields explicitly absent");
        Need(!String.IsNullOrEmpty((string)first["host_epoch"]) && L(first["connection_epoch"])==10,"host and connection epochs retained");
        Need(rows.Any(r=>(string)r["event"]=="coverage_gap" && (string)r["reason"]=="IMU priority"),"missing observation reason explicit");
        Need(rows.Any(r=>(string)r["event"]=="exchange_finished" && (string)r["outcome"]=="cancelled"),"foreground cancellation explicit");
        Need(rows.Any(r=>r.ContainsKey("receipt_error")),"corrupt receipt retained as unqualified evidence");
        var wrapping=rows.Single(r=>(string)r["event"]=="exchange_finished" && L(r["request_id"])==95);
        Need(L(wrapping["mcu_rx_ms"])==UInt32.MaxValue && L(wrapping["mcu_tx_ms"])==2,"counter wrap preserved raw, not guessed");
        var close=rows.Last();Need((string)close["event"]=="connection_closed" && ((ArrayList)close["unfinished_request_ids"]).Cast<object>().Any(x=>L(x)==96),"reconnect boundary identifies unfinished requests");
        string mirror=Directory.GetFiles(record,"ble_clock_*.jsonl").Single();Need(Rows(mirror).Last()["event"].ToString()=="connection_closed","recording receipt keeps its evidence and boundary together");
        Need(File.ReadAllBytes(sentinel).SequenceEqual(new byte[]{2,7,9}),"neural bytes unchanged");
        Need(!Directory.GetFiles(record).Any(p=>p.Contains("timestamps.calibrated")),"no unsupported AP correction generated");
        ((IDisposable)journal).Dispose();
        var second=New("NpBleClockEvidence",Path.Combine(root,"connection2"),"BLE-112233445566",11);((IDisposable)second).Dispose();
        var open2=Rows(Path.Combine(root,"connection2/ble_clock_exchange_evidence.jsonl"))[0];
        Need(L(open2["connection_epoch"])==11 && (string)open2["host_epoch"]==(string)first["host_epoch"] && (string)open2["journal_session"]!=(string)first["journal_session"],"reconnect keeps shared PC epoch but splits transport journals");
        var failed=New("NpBleClockEvidence",sentinel,"BLE-failure",12);Call(failed,"Begin",Request(4),send,null);((IDisposable)failed).Dispose();
        Need(failed.GetType().GetProperty("Error",All).GetValue(failed)!=null,"metadata I/O failure remains nonfatal and visible to report");
    }
    static void Schedule(){
        var policy=T("NpBleRecordingClockSchedule");
        foreach(int family in new[]{1,2}){
            uint active=family==1?1U:3U;
            Need((bool)Call(policy,"Confirmed",family,active,0U,0U),"family-specific confirmed recording");
            Need(!(bool)Call(policy,"Confirmed",family,active,1U,0U) && !(bool)Call(policy,"Confirmed",family,active,0U,2U),"error/fault never admits timing collector");
            Need(!(bool)Call(policy,"Confirmed",family,0U,0U,0U),"idle recorder excluded");
            Need((bool)Call(policy,"Capable",family,256U) && !(bool)Call(policy,"Capable",family,0U),"optional clock capability for both families");
        }
        Need(!(bool)Call(policy,"Confirmed",0,1U,0U,0U) && !(bool)Call(policy,"Capable",0,256U),"unknown family excluded");
        var schedule=New("NpBleRecordingClockSchedule");
        Need(!(bool)Call(schedule,"Due",0L),"no query before confirmed capture");
        Call(schedule,"Status",true,false,100L);Need(!(bool)Call(schedule,"Due",100L),"legacy unsupported capability nonfatal/no query");
        Call(schedule,"Status",true,true,100L);Need((bool)Call(schedule,"Due",100L),"supported confirmed capture eligible");Call(schedule,"Attempt",100L);
        Need(!(bool)Call(schedule,"Due",5099L),"at most one periodic request per5s");Need((bool)Call(schedule,"Due",5100L),"bounded periodic schedule");
        Need(!(bool)Call(schedule,"Due",5101L),"stale recording state cannot keep collector running");
        Call(schedule,"Status",false,true,5200L);Need(!(bool)Call(schedule,"Due",5200L),"stopped recorder never polled by periodic collector");
        Call(schedule,"Status",true,true,6000L);Call(schedule,"Failed",6000L);Call(schedule,"Status",true,true,20000L);
        Need(!(bool)Call(schedule,"Due",20000L),"missing response backs off");Call(schedule,"Status",true,true,21000L);Need((bool)Call(schedule,"Due",21000L),"missing response recovers without recording replay");
        Call(schedule,"Reset");Need(!(bool)Call(schedule,"Due",22000L),"new connection requires fresh confirmation");
    }
    static void Priority(){
        var peer=New("NpBleConnection");Set(peer,"imuReaders",1);
        var skipped=(Task<byte[]>)Call(peer,"ReadRecordingClock",9U,CancellationToken.None);Need(skipped.GetAwaiter().GetResult()==null,"clock observation yields to IMU without radio");
        Set(peer,"imuReaders",0);var old=Task.FromResult(new byte[]{1});Set(peer,"recordingClockTask",old);
        Call(peer,"RetireRecordingClock").GetType();Need(!(bool)T("NpBleConnection").GetProperty("ClockPending",All).GetValue(peer),"completed clock task owns no preview priority");
        var pending=new TaskCompletionSource<byte[]>();Set(peer,"recordingClockTask",pending.Task);
        Need((bool)T("NpBleConnection").GetProperty("PreviewLowerPriorityBusy",All).GetValue(peer),"waveform yields to pending clock evidence");
        var preview=new byte[64];Put(preview,0,0x3256504e);Put(preview,4,1);Put(preview,8,5);Put(preview,12,11);Put(preview,16,1);Put(preview,60,Crc(preview));
        Need(!((Task<bool>)Call(peer,"TryWritePreview",preview,1,CancellationToken.None)).GetAwaiter().GetResult(),"actual waveform transport defers without opening radio");pending.SetResult(null);
        Call(peer,"SetClockContext","bad",null,null);var context=T("NpBleConnection").GetField("clockContext",All).GetValue(peer);
        Need(context.GetType().GetField("device_uid",All).GetValue(context)==null,"invalid identity cannot be promoted to exact UID");
        Call(peer,"SetClockContext","001a0004434250062039314e",null,null);context=T("NpBleConnection").GetField("clockContext",All).GetValue(peer);
        Need((string)context.GetType().GetField("device_uid",All).GetValue(context)=="001A0004434250062039314E","available exact UID canonicalized independently of transport address");
        ((IDisposable)peer).Dispose();
    }
    [STAThread]static int Main(string[] args){try{app=Assembly.LoadFrom(Path.GetFullPath(args[0]));Directory.CreateDirectory(args[1]);Evidence(Path.GetFullPath(args[1]));Schedule();Priority();Console.WriteLine("PASS: "+checks+" actual-compiled raw BLE clock evidence/schedule/priority checks; no hardware access.");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
