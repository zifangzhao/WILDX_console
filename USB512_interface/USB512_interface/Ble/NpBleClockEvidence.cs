using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using CE32_console;

namespace USB512_interface {
    internal sealed class NpBleClockContext {
        public string device_uid,host_recording_receipt,sd_record_token_hex;
        public int device_family;
        public uint? status_state,status_start_lba,status_sample_hz,status_probe_mask;
        public bool on_card_recording_association_verified=false;
    }
    internal sealed class NpBleClockAttempt {
        internal uint Id;internal long SendQpc;internal byte[] Request;internal NpBleClockContext Context;
        internal NpBleClockTransport Transport;internal bool StartedWritten;
    }
    // Per read-only exchange only. The production reassembler supplies accepted
    // byte ranges; no second scanner and no raw notification copies are needed.
    internal sealed class NpBleClockTransport {
        struct Notification {internal long Start,End,Qpc;internal int Number;}
        struct Write {internal int Offset,Bytes;internal long Before,After;internal bool Success;}
        readonly object gate=new object();readonly byte[] request;
        readonly Notification[] notifications=new Notification[128];readonly Write[] writes=new Write[4];
        int first,count,received,dropped,writeCount;long nextByte,lastNotificationQpc;
        long startQpc,endQpc,startByte,endByte;int startNumber,endNumber;bool correlated;string error;
        internal NpBleClockTransport(byte[] body){request=body;}
        internal bool IsWrite(byte[] frame){
            if(frame==null||frame.Length!=67||frame[0]!=0x3c||frame[1]!=0xb9||frame[66]!=0x3e)return false;
            for(int i=0;i<64;i++)if(frame[i+2]!=request[i])return false;return true;
        }
        internal void WriteStarted(int offset,int bytes,long qpc){lock(gate){
            if(writeCount==writes.Length||offset<0||bytes<1||offset+bytes>67||qpc<=0){error="Invalid GATT write-fragment boundary.";return;}
            writes[writeCount++]=new Write {Offset=offset,Bytes=bytes,Before=qpc};}}
        internal void WriteCompleted(int offset,long qpc,bool success){lock(gate){
            if(writeCount==0||writes[writeCount-1].Offset!=offset||qpc<writes[writeCount-1].Before){error="Conflicting GATT completion boundary.";return;}
            var w=writes[writeCount-1];w.After=qpc;w.Success=success;writes[writeCount-1]=w;}}
        internal void NotificationReceived(long at,int bytes,long qpc){lock(gate){
            if(correlated||bytes==0)return;
            if(at<0||bytes<0||at>Int64.MaxValue-bytes||qpc<=0||received!=0&&(at!=nextByte||qpc<lastNotificationQpc)){error="Non-contiguous notification bytes or reversed callback QPC.";return;}
            nextByte=at+bytes;lastNotificationQpc=qpc;received++;
            if(count==notifications.Length){first=(first+1)%notifications.Length;count--;dropped++;}
            notifications[(first+count)%notifications.Length]=new Notification {Start=at,End=nextByte,Qpc=qpc,Number=received};count++;
        }}
        internal void Accepted(byte command,byte[] body,long start,long end){lock(gate){
            if(correlated||error!=null||command!=0xb9||end<=start)return;
            try{
                if(NpFineClockWire.IsRequest(request)){
                    if(body==null||body.Length!=80)return;
                    NpFineClockWire.ParseReply(body,request,BitConverter.ToUInt16(body,6)>>8);
                }else NpV2OnlineClock.Parse(body,0,BitConverter.ToUInt32(request,12));
            }catch{return;}
            Notification a=default(Notification),b=default(Notification);bool haveA=false,haveB=false;
            for(int i=0;i<count;i++){var n=notifications[(first+i)%notifications.Length];
                if(start>=n.Start&&start<n.End){a=n;haveA=true;}if(end-1>=n.Start&&end-1<n.End){b=n;haveB=true;}}
            if(!haveA||!haveB||a.Qpc>b.Qpc){error="Accepted reply range is outside retained notification segments.";return;}
            startQpc=a.Qpc;endQpc=b.Qpc;startByte=start;endByte=end;startNumber=a.Number;endNumber=b.Number;correlated=true;
        }}
        internal object Evidence(long fullSend,long continuation){lock(gate){
            var sent=new List<object>();for(int i=0;i<writeCount;i++){var w=writes[i];sent.Add(new {offset=w.Offset,bytes=w.Bytes,before_write_qpc=w.Before,completed_qpc=w.After==0?(long?)null:w.After,success=w.Success});}
            var parts=new List<object>();for(int i=0;i<count;i++){var n=notifications[(first+i)%notifications.Length];parts.Add(new {number=n.Number,start_byte=n.Start,end_byte=n.End,callback_qpc=n.Qpc});}
            long last=writeCount==0?0:writes[writeCount-1].Before;
            return new {schema=1,reply_id_crc_correlated=correlated,boundary_order_valid=correlated&&error==null&&last>=fullSend&&startQpc>=last&&endQpc<=continuation,
                last_gatt_fragment_send_qpc=last==0?(long?)null:last,reply_start_notification_qpc=correlated?(long?)startQpc:null,
                reply_complete_notification_qpc=correlated?(long?)endQpc:null,reply_start_byte=correlated?(long?)startByte:null,reply_end_byte=correlated?(long?)endByte:null,
                reply_start_notification=correlated?(int?)startNumber:null,reply_complete_notification=correlated?(int?)endNumber:null,
                full_exchange_start_qpc=fullSend,continuation_qpc=continuation,write_fragments=sent,notification_segments=parts,
                notifications_seen=received,notification_segments_dropped=dropped,error,clock_bounds_applied=false,
                scope="Host software boundaries only: before last GATT fragment write and callback entry for the first segment of the CRC/ID-correlated accepted reply. Not UART wire, radio or hardware notification timestamps. Legacy clock bounds are unchanged."};
        }}
    }
    // Evidence only: no fitting, AP timestamps, device writes or clock commits.
    internal sealed class NpBleClockEvidence : IDisposable {
        readonly object gate=new object();readonly JavaScriptSerializer json=new JavaScriptSerializer();
        readonly string transport,folder,session=Guid.NewGuid().ToString("N");readonly int epoch;readonly long openedQpc=Stopwatch.GetTimestamp();
        readonly HashSet<uint> pending=new HashSet<uint>();
        readonly List<NpBleClockAttempt> unfinished=new List<NpBleClockAttempt>();
        StreamWriter writer,mirror;string mirrorPath;bool closed,openingWritten;
        internal string Error {get;private set;}
        internal NpBleClockEvidence(string folder,string transportIdentity,int connectionEpoch) {
            this.folder=folder;transport=transportIdentity;epoch=connectionEpoch;
        }
        static StreamWriter Open(string path,bool append){return new StreamWriter(new FileStream(path,append?FileMode.Append:FileMode.CreateNew,FileAccess.Write,FileShare.Read,16384),new UTF8Encoding(false));}
        Dictionary<string,object> Row(string kind,long qpc) {
            return new Dictionary<string,object>{{"schema",1},{"event",kind},{"transport_identity",transport},{"connection_epoch",epoch},
                {"journal_session",session},{"host_epoch",NpHostClock.Epoch},{"host_qpc",qpc},{"host_pc_unix_us",NpHostClock.UnixUs(qpc)},
                {"pc_clock",NpHostClock.Receipt},{"reference","shared_PC_monotonic_anchor; network correction separate"},
                {"ap_source_pair",null},{"device_boot_domain",null},{"acquisition_generation",null}};
        }
        void Write(NpBleClockContext context,Dictionary<string,object> row) {
            if(closed)return;
            if(writer==null){try{Directory.CreateDirectory(folder);writer=Open(Path.Combine(folder,"ble_clock_exchange_evidence.jsonl"),false);}
                catch(Exception ex){Error=ex.Message;return;}}
            if(!openingWritten){openingWritten=true;Write(null,Row("connection_opened",openedQpc));}
            row["context"]=context;if(!row.ContainsKey("identity_scope"))row["identity_scope"]=context!=null && context.device_uid!=null?"exact_MCU_UID":"BLE_transport_identity_only";
            try {
                string line=json.Serialize(row);writer.WriteLine(line);writer.Flush();
                string path=context==null?mirrorPath:String.IsNullOrEmpty(context.host_recording_receipt)?null:
                    Path.Combine(context.host_recording_receipt,"ble_clock_"+session+".jsonl");
                if(path!=null && context!=null && String.Equals(Path.GetFullPath(context.host_recording_receipt).TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase))path=null;
                if(path!=mirrorPath){if(mirror!=null)mirror.Dispose();mirror=null;mirrorPath=path;
                    if(path!=null)mirror=Open(path,File.Exists(path));}
                if(mirror!=null){mirror.WriteLine(line);mirror.Flush();}
            }catch(Exception ex){Error=ex.Message;} // Metadata I/O cannot fail a command.
        }
        internal NpBleClockAttempt Begin(byte[] request,long sendQpc,NpBleClockContext context) {
            lock(gate) {
                if(closed || !NpBleConnection.IsReadOnlyClockRequest(0xb9,request))return null;
                var captured=context==null?null:new NpBleClockContext {device_uid=context.device_uid,host_recording_receipt=context.host_recording_receipt,device_family=context.device_family,sd_record_token_hex=context.sd_record_token_hex,
                    status_state=context.status_state,status_start_lba=context.status_start_lba,status_sample_hz=context.status_sample_hz,status_probe_mask=context.status_probe_mask};
                var attempt=new NpBleClockAttempt {Id=BitConverter.ToUInt32(request,NpFineClockWire.IsRequest(request)?8:12),SendQpc=sendQpc,Request=(byte[])request.Clone(),Context=captured};
                attempt.Transport=new NpBleClockTransport(attempt.Request);pending.Add(attempt.Id);unfinished.Add(attempt);return attempt;
            }
        }
        void Started(NpBleClockAttempt attempt){
            if(attempt.StartedWritten)return;attempt.StartedWritten=true;
            var row=Row("exchange_started",attempt.SendQpc);row["request_id"]=attempt.Id;row["request_hex"]=Hex(attempt.Request);
            row["host_send_qpc"]=attempt.SendQpc;Write(attempt.Context,row);
        }
        internal void Finish(NpBleClockAttempt attempt,byte[] response,long receiveQpc,Exception error) {
            lock(gate) {
                if(attempt==null || closed)return;
                pending.Remove(attempt.Id);unfinished.Remove(attempt);Started(attempt);var row=Row("exchange_finished",receiveQpc);
                row["request_id"]=attempt.Id;row["request_hex"]=Hex(attempt.Request);row["reply_hex"]=response==null?null:Hex(response);
                row["host_send_qpc"]=attempt.SendQpc;row["host_receive_qpc"]=receiveQpc;
                row["host_send_pc_unix_us"]=NpHostClock.UnixUs(attempt.SendQpc);row["host_receive_pc_unix_us"]=NpHostClock.UnixUs(receiveQpc);
                row["receive_scope"]="QPC observed after matching receipt; includes host scheduling, not radio packet timing";
                row["transport_boundaries"]=attempt.Transport.Evidence(attempt.SendQpc,receiveQpc);
                row["outcome"]=error==null?"reply_unqualified":error is OperationCanceledException?"cancelled":error is TimeoutException?"timeout":"transport_error";
                row["error"]=error==null?null:error.Message;
                if(response!=null)try {
                    if(NpFineClockWire.IsRequest(attempt.Request)){
                        int family=response.Length==80?BitConverter.ToUInt16(response,6)>>8:0;
                        var fine=NpFineClockWire.ParseReply(response,attempt.Request,family);
                        row["clock_protocol"]=fine.Version==2?"NPRF2":"NPRF1";row["requested_hold_us"]=fine.RequestedHoldUs;
                        row["device_uid"]=BitConverter.ToUInt32(response,12).ToString("X8")+BitConverter.ToUInt32(response,16).ToString("X8")+BitConverter.ToUInt32(response,20).ToString("X8");
                        row["device_family"]=family;row["identity_scope"]="exact_MCU_UID_request_and_reply";
                        row["fine_clock_qualified"]=fine.Qualified;row["fine_clock_flags"]=fine.Flags;
                        row["fine_clock_domain"]=fine.Domain;row["mcu_counter_hz"]=fine.Hz;
                        row["mcu_rx_ms"]=fine.RxMs;row["mcu_tx_ms"]=fine.TxMs;row["mcu_rx_counter"]=fine.RxCounter;row["mcu_tx_counter"]=fine.TxCounter;
                        row["acquisition_generation"]=(fine.Flags&NpFineClockWire.CaptureBound)!=0?(object)fine.Generation:null;
                        row["source_epoch"]=(fine.Flags&NpFineClockWire.CaptureBound)!=0?(object)fine.SourceEpoch:null;
                        row["uart_baud_upper_bound"]=(fine.Flags&NpFineClockWire.UartBaudBound)!=0?(object)fine.UartBaud:null;
                        if(error==null)row["outcome"]=receiveQpc>=attempt.SendQpc?"reply_validated":"host_counter_conflict";
                    }else{
                    var reply=NpV2OnlineClock.Parse(response,0,attempt.Id);
                    row["mcu_rx_ms"]=reply.Rx;row["mcu_tx_ms"]=reply.Tx;row["mcu_counter_hz"]=1000;
                    row["clock_mapping_generation"]=reply.Generation;row["device_reported_mapping_valid"]=reply.Valid;
                    row["device_state"]=reply.State;row["device_reported_mapping_uncertainty_us"]=reply.Uncertainty;
                    row["device_reported_mapping_pc_us"]=reply.Utc;
                    if(error==null)row["outcome"]=receiveQpc>=attempt.SendQpc?"reply_validated":"host_counter_conflict";
                    }
                }catch(Exception ex){row["receipt_error"]=ex.Message;}
                Write(attempt.Context,row);
            }
        }
        internal void Gap(NpBleClockContext context,string reason,long qpc) {
            lock(gate){var row=Row("coverage_gap",qpc);row["reason"]=reason;Write(context,row);}
        }
        internal void Close(long qpc,string reason) {
            lock(gate) {
                if(closed)return;
                foreach(var attempt in unfinished)Started(attempt);
                var row=Row("connection_closed",qpc);row["reason"]=reason;row["unfinished_request_ids"]=new List<uint>(pending);
                Write(null,row);closed=true;try{if(writer!=null)writer.Dispose();if(mirror!=null)mirror.Dispose();}catch(Exception ex){Error=ex.Message;}
                writer=mirror=null;
            }
        }
        public void Dispose(){Close(Stopwatch.GetTimestamp(),"journal_disposed; reconnect requires a new transport epoch");}
        static string Hex(byte[] bytes){return BitConverter.ToString(bytes).Replace("-","");}
    }
    internal sealed class NpBleRecordingClockSchedule {
        long lastStatus=Int64.MinValue,next;bool recording,supported;
        internal static bool Confirmed(int family,uint state,uint error,uint fault){return (family==1 || family==2) && error==0 && fault==0 && state==(family==1?1U:3U);}
        internal static bool Capable(int family,uint flags){return (family==1 || family==2) && (flags&256U)!=0;}
        internal void Status(bool active,bool capability,long now){recording=active;supported=capability;lastStatus=now;}
        internal bool Due(long now){return recording && supported && now>=lastStatus && now-lastStatus<=5000 && now>=next;}
        internal void Attempt(long now){next=now+5000;}
        internal void Failed(long now){next=now+15000;}
        internal void Reset(){lastStatus=Int64.MinValue;next=0;recording=supported=false;}
    }
}
