using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;

namespace USB512_interface {
    // Optional control/evidence owner. No read/rearm slot or neural buffer ownership.
    internal sealed class NpUsbTimingSession : IDisposable {
        sealed class Received {internal byte[] Bytes;internal ulong Sequence;internal long Qpc;}
        readonly string folder,uid;readonly int family;readonly ulong token;
        readonly BlockingCollection<Received> inbox=new BlockingCollection<Received>(32);
        readonly JavaScriptSerializer json=new JavaScriptSerializer();readonly object fileGate=new object();
        StreamWriter file;Thread worker;NpUsbTimingOut output;Action<byte[]> write;
        volatile bool stopQueries,disposed;uint nextId=1,generation,pendingId;long nextQuery,pendingSince;
        long dropped;internal string Error {get;private set;}
        internal bool Enabled {get;private set;}
        NpFirstSyncUsbInfo firstSyncInfo;NpFirstSyncUsbAssembler firstSync;
        byte[] firstSyncProbe,firstSyncNegotiation,firstSyncConfirmed,firstSyncDisable,firstSyncObservation,sourceAnchor,sourceCycles;
        bool firstSyncAttempted,firstSyncWritten,firstSyncInvalid,firstSyncPageContext;
        bool fineRootInvalid;string fineRootError;volatile NpFirstSyncUsbFineRoot fineRoot;
        NpRtcStartAssembler rtc;bool rtcInvalid,rtcWritten,rtcLocalQualified;string rtcError;int rtcPagesBeforeRoot;
        string firstSyncProbeError,firstSyncDisableError,firstSyncObservationError;
        internal string FirstSyncError {get;private set;}
        internal bool FirstSyncEnabled {get{return firstSyncInfo!=null;}}
        internal bool FirstSyncQualified {get{return firstSyncWritten && !firstSyncInvalid;}}
        internal byte FirstSyncMissingMask {get{return firstSync==null?(byte)127:firstSync.MissingMask;}}
        internal bool FineRootEnabled {get{return firstSyncInfo!=null && firstSyncInfo.FineAux;}}
        internal string FineRootError {get{return fineRootError;}}
        internal NpFirstSyncUsbFineRoot FineRoot {get{return FirstSyncQualified && !fineRootInvalid?fineRoot:null;}}
        internal bool RtcEnabled {get{return firstSyncInfo!=null&&firstSyncInfo.HardwareRtc;}}
        internal bool RtcBound {get{return RtcEnabled&&FirstSyncQualified&&FineRoot!=null&&!rtcInvalid&&rtcWritten;}}
        internal bool RtcQualified {get{return RtcBound&&rtcLocalQualified;}}
        internal string RtcError {get{return rtcError;}}
        internal byte RtcMissingMask {get{return rtc==null?(byte)7:rtc.MissingMask;}}
        internal ulong Token {get{return token;}}
        static string Hex(byte[] b){return b==null?null:BitConverter.ToString(b).Replace("-","");}
        static ulong NewToken(){ulong value;do{value=BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(),0);}while(value==0);return value;}
        NpUsbTimingSession(string directory,string identity,int version,ulong value=0){folder=directory;uid=(identity??"").ToUpperInvariant();family=version;token=value==0?NewToken():value;}
        void Log(object row){lock(fileGate){if(file!=null){file.WriteLine(json.Serialize(row));file.Flush();}}}
        internal static NpUsbTimingSession Prepare(string folder,string uid,int family,Func<byte[]> info,Action<byte[]> enable,Func<uint,byte[]> prearm,Func<NpUsbTimingOut> makeOutput,Func<byte[]> infoV2=null) {
            var session=new NpUsbTimingSession(folder,uid,family);bool negotiated=false;
            try {
                NpFirstSyncUsbInfo v2=null;uint preparedGeneration;
                if(infoV2!=null)try{
                    session.firstSyncProbe=infoV2();var probe=NpFirstSyncUsb.ParseInfo(session.firstSyncProbe,uid,family);
                    if((probe.Flags&5)==5 && (probe.Caps&3)==3 && (probe.Features==1 || probe.Features==3 || probe.Features==7 || probe.Features==15))v2=probe;
                    else session.firstSyncProbeError="First-sync v2 layout is unavailable; legacy timing retained.";
                }catch(Exception ex){session.firstSyncProbeError=ex.Message;}
                if(v2!=null){
                    session.firstSyncNegotiation=v2.HardwareRtc?NpFirstSyncUsb.NegotiateRtcStart(uid,session.token,lastToken:v2.Token):
                        v2.FineCounter64?NpFirstSyncUsb.NegotiateFineCounter64(uid,session.token,lastToken:v2.Token):
                        v2.FineAux?NpFirstSyncUsb.NegotiateFineAux(uid,session.token,lastToken:v2.Token):NpFirstSyncUsb.Negotiate(uid,session.token,lastToken:v2.Token);
                    session.firstSyncAttempted=true; // Delivery/ACK may fail after device mutation.
                    enable(session.firstSyncNegotiation);negotiated=true;
                    session.firstSyncConfirmed=infoV2();var ready=NpFirstSyncUsb.ParseInfo(session.firstSyncConfirmed,uid,family);
                    if(!ready.Ready || ready.Token!=session.token || ready.Features!=v2.Features)throw new IOException("First-sync v2 negotiation was not confirmed.");
                    session.firstSyncInfo=ready;preparedGeneration=ready.Generation;
                }else{
                    var first=NpUsbTimingWire.ParseInfo(info(),uid,family);
                    if((first.Flags&5)!=5||(first.Caps&3)!=3)return session;
                    enable(NpUsbTimingWire.Enable(uid,session.token));negotiated=true;
                    var ready=NpUsbTimingWire.ParseInfo(info(),uid,family);
                    if(!ready.Ready||ready.Token!=session.token)throw new IOException("Timing negotiation was not confirmed.");
                    preparedGeneration=ready.Generation;
                }
                session.file=new StreamWriter(new FileStream(Path.Combine(folder,NpUsbTimingEvidence.FileName),FileMode.CreateNew,FileAccess.Write,FileShare.Read,65536));
                if(session.firstSyncInfo==null)session.Log(new {kind="metadata",schema=2,device_uid=session.uid,device_family=family,token_hex=session.token.ToString("X16"),host_epoch=NpHostClock.Epoch,clock=NpHostClock.Receipt,
                    reference="Request-correlated MCU counters; USB completion is a causal bound, not sample arrival latency.",record_bytes=108});
                else session.Log(new {kind="metadata",schema=2,device_uid=session.uid,device_family=family,token_hex=session.token.ToString("X16"),host_epoch=NpHostClock.Epoch,clock=NpHostClock.Receipt,
                    reference="Request-correlated MCU counters; USB completion is a causal bound, not sample arrival latency.",record_bytes=108,
                    first_sync_transport=new {version=2,initial_info_hex=Hex(session.firstSyncProbe),negotiation_hex=Hex(session.firstSyncNegotiation),confirmed_info_hex=Hex(session.firstSyncConfirmed)}});
                long before=Stopwatch.GetTimestamp();uint id=session.nextId++;
                session.Log(new {kind="request",phase="prearm",request_id=id,generation=preparedGeneration,send_qpc=before,raw_hex=(string)null});
                byte[] raw=prearm(id);long after=Stopwatch.GetTimestamp();
                var record=NpUsbTimingWire.Parse(raw,uid,family,session.token,UInt64.MaxValue);
                if(record.Kind!=1||!record.Valid||!record.Prearm||record.Id!=id||record.Generation!=preparedGeneration)throw new IOException("Pre-arm timing reply is unbound.");
                session.Log(new {kind="record",outcome="accepted",receive_qpc=after,container_sequence=UInt64.MaxValue.ToString(CultureInfo.InvariantCulture),raw_hex=Hex(raw)});
                session.output=makeOutput();session.write=session.output.Write;session.Enabled=true;
            } catch(Exception ex) {
                session.Error=ex.Message;
                if(session.firstSyncAttempted){
                    session.FirstSyncError=ex.Message;session.firstSyncInvalid=true;session.firstSyncInfo=null;
                    // One version-correct stopped cleanup only, even after lost ACK.
                    try{session.firstSyncDisable=NpFirstSyncUsb.Negotiate(uid,0,false);enable(session.firstSyncDisable);}
                    catch(Exception disable){session.firstSyncDisableError=disable.Message;}
                    try{session.firstSyncObservation=infoV2();NpFirstSyncUsb.ParseInfo(session.firstSyncObservation,uid,family);}
                    catch(Exception observation){session.firstSyncObservationError=observation.Message;}
                }else if(negotiated)try{enable(NpUsbTimingWire.Enable(uid,0,false));}catch(Exception disable){session.Error+="; disable unavailable: "+disable.Message;}
                if(session.output!=null)try{session.output.Dispose();}catch{}session.output=null;
                if(session.file!=null){try{session.file.Dispose();}catch{}session.file=null;}
            }
            return session;
        }
        internal void Start() {
            if(!Enabled||worker!=null)return;
            try{worker=new Thread(Run){IsBackground=true,Name="NP optional USB timing"};worker.Start();}
            catch(Exception ex){Error=ex.Message;stopQueries=true;}
        }
        internal void FeedV2(byte[] block,int offset,long qpc) {
            if(!Enabled||disposed||block==null||offset<0||block.Length-offset<512)return;
            if((BitConverter.ToUInt32(block,offset+52)&512)==0)return;
            Feed(block,offset+396,BitConverter.ToUInt64(block,offset+24),qpc);
        }
        internal void FeedV1(byte[] b,int offset,int length,long qpc) {
            if(!Enabled||disposed||b==null||offset<0||length!=506||b.Length-offset<506)return;
            if((BitConverter.ToUInt32(b,offset+188)&8)==0)return;
            // This may run when misc saving is off, so validate the wrapper here too.
            if(System.Text.Encoding.ASCII.GetString(b,offset,8)!="NPSENS01"||System.Text.Encoding.ASCII.GetString(b,offset+164,8)!="NPUSB001"||
                BitConverter.ToUInt32(b,offset+160)!=NpUsbSensors.Crc(b,offset,160)||BitConverter.ToUInt32(b,offset+502)!=NpUsbSensors.Crc(b,offset,502)){
                Interlocked.Increment(ref dropped);return;
            }
            Feed(b,offset+192,BitConverter.ToUInt32(b,offset+8),qpc);
        }
        void Feed(byte[] bytes,int offset,ulong sequence,long qpc) {
            if(qpc<=0||NpUsbTimingWire.IsAbsent(bytes,offset))return;
            var copy=new byte[108];Buffer.BlockCopy(bytes,offset,copy,0,108);
            try{if(!inbox.TryAdd(new Received{Bytes=copy,Sequence=sequence,Qpc=qpc}))Interlocked.Increment(ref dropped);}
            catch(InvalidOperationException){Interlocked.Increment(ref dropped);}
        }
        void KeepSource(NpUsbTimingRecord record) {
            if(!FirstSyncEnabled)return;
            byte[] previous=record.Kind==2?sourceAnchor:sourceCycles;
            if(previous!=null && !previous.SequenceEqual(record.Body)){
                firstSyncInvalid=true;FirstSyncError="Conflicting immutable source receipts.";return;
            }
            if(record.Kind==2)sourceAnchor=(byte[])record.Body.Clone();else sourceCycles=(byte[])record.Body.Clone();
            if(firstSync==null && generation!=0)firstSync=new NpFirstSyncUsbAssembler(firstSyncInfo,token,generation);
            SaveFirstSync();
        }
        void SaveFirstSync() {
            if(firstSyncInvalid || firstSync==null || !firstSync.Complete)return;
            byte[] source=sourceAnchor;
            if(firstSyncInfo.HardwareRtc&&source==null){if(fineRoot==null)return;source=fineRoot.Anchor;}
            if(source==null)return;
            try{
                if(firstSyncInfo.HardwareRtc&&sourceAnchor==null&&firstSync.Receipt.Session!=generation)throw new InvalidDataException("USB final session/generation mismatch.");
                NpFirstSyncUsbEvidence.CheckSource(firstSync.Receipt,source,sourceCycles,uid,family,generation);
                if(firstSyncInfo.HardwareRtc&&sourceAnchor!=null)NpFirstSyncUsbEvidence.CheckSource(firstSync.Receipt,sourceAnchor,sourceCycles,uid,family,generation);
                if(firstSyncWritten)return; // Recheck a later source counter receipt; never rewrite body.
                using(var body=new FileStream(Path.Combine(folder,NpFirstSyncUsbEvidence.ReceiptFile),FileMode.CreateNew,FileAccess.Write,FileShare.Read))
                    body.Write(firstSync.Receipt.Raw,0,firstSync.Receipt.Raw.Length);
                firstSyncWritten=true;
                Log(new {kind="first_sync_receipt",outcome="transport_qualified",device_uid=uid,token_hex=token.ToString("X16"),generation,
                    raw_file=NpFirstSyncUsbEvidence.ReceiptFile,physical_source_clock_qualified=false,utc_qualified=false});
            }catch(Exception ex){firstSyncInvalid=true;FirstSyncError=ex.Message;}
        }
        void KeepPage(Received packet) {
            string outcome="rejected",why=null;
            if(FirstSyncEnabled&&firstSyncInfo.HardwareRtc&&generation==0&&!firstSyncInvalid)try{
                generation=NpFirstSyncUsb.CandidatePageGeneration(packet.Bytes,uid,token,packet.Sequence);firstSyncPageContext=true;
            }catch(Exception failure){firstSyncInvalid=true;FirstSyncError=failure.Message;}
            if(!FirstSyncEnabled)why="First-sync v2 was not confirmed.";
            else if(generation==0)why="No accepted active source generation yet.";
            else if(firstSyncInvalid)why=FirstSyncError??"First-sync evidence was invalidated.";
            else{
                if(firstSync==null)firstSync=new NpFirstSyncUsbAssembler(firstSyncInfo,token,generation);
                if(firstSync.Add(packet.Bytes,packet.Sequence)){outcome="accepted";SaveFirstSync();if(firstSyncInvalid){outcome="rejected";why=FirstSyncError;}}
                else{firstSyncInvalid=true;why=firstSync.Error;}
            }
            if(why!=null)FirstSyncError=why;
            // Never log kind4 as an accepted legacy record: old strict readers
            // do not understand it. This separate row kind remains ignorable.
            Log(new {kind="first_sync_page",outcome,error=why,receive_qpc=packet.Qpc,container_sequence=packet.Sequence.ToString(CultureInfo.InvariantCulture),
                raw_hex=Hex(packet.Bytes),missing_mask=FirstSyncMissingMask,assembly_failed=firstSync!=null && firstSync.Failed,binding_invalid=firstSyncInvalid});
        }
        void KeepFineRoot(Received packet) {
            string why=null;
            try{
                if(fineRootInvalid)why=fineRootError;
                else if(!FineRootEnabled)why="Fine-AUX was not negotiated.";
                else if(firstSyncInvalid || firstSync==null)why="No accepted first-sync source context.";
                else if(!firstSync.FeedAnchor(packet.Bytes,packet.Sequence))why=firstSync.FineError;
                else if(fineRoot==null){fineRoot=new NpFirstSyncUsbFineRoot(firstSyncConfirmed,firstSync,packet.Bytes,packet.Sequence);if(firstSyncInfo.HardwareRtc)SaveFirstSync();}
            }catch(Exception ex){why=ex.Message;}
            if(why!=null){fineRootInvalid=true;fineRootError=why;}
            var rawAnchor=new byte[64];Buffer.BlockCopy(packet.Bytes,40,rawAnchor,0,64);
            // Old evidence readers ignore this row; kind5 is never a legacy R2 record.
            Log(new {kind="first_sync_source_root",outcome=why==null?"accepted":"rejected",error=why,receive_qpc=packet.Qpc,
                container_sequence=packet.Sequence.ToString(CultureInfo.InvariantCulture),raw_hex=Hex(packet.Bytes),
                anchor_hex=Hex(rawAnchor),fine_root_qualified=FineRoot!=null});
        }
        void KeepRtc(Received packet) {
            string why=null;
            try{
                if(!RtcEnabled)why="Hardware RTC was not negotiated.";
                else if(rtcInvalid)why=rtcError;
                else if(!FirstSyncQualified||FineRoot==null){rtcPagesBeforeRoot++;why="Complete NPSTART and final fine source root must precede RTC pages.";}
                else{
                    if(packet.Sequence<=FineRoot.Sequence)throw new InvalidDataException("RTC page must follow the final source root.");
                    if(rtc==null)rtc=new NpRtcStartAssembler(NpRtcUsbArchive.Bind(FineRoot));
                    if(!rtc.Feed(packet.Bytes,packet.Sequence))throw new InvalidDataException(rtc.Error??"RTC page assembly failed.");
                    if(rtc.Complete&&!rtcWritten){
                        var binding=NpRtcUsbArchive.Bind(FineRoot);var raw=rtc.Raw;rtcLocalQualified=NpRtcStartWire.Map(raw,binding).Qualified;
                        NpRtcUsbArchive.Save(folder,raw,binding);rtcWritten=true;
                    }
                }
            }catch(Exception error){rtcInvalid=true;why=error.Message;}
            rtcError=why;
            // Kind6 must remain ignorable to strict legacy R2 evidence readers.
            Log(new {kind=NpRtcUsbArchive.RowKind,outcome=why==null?"accepted":"rejected",error=why,receive_qpc=packet.Qpc,
                container_sequence=packet.Sequence.ToString(CultureInfo.InvariantCulture),raw_hex=Hex(packet.Bytes),
                missing_mask=RtcMissingMask,assembly_failed=rtc!=null&&rtc.Failed,rtc_invalid=rtcInvalid});
        }
        void Run() {
            try {
                while(!inbox.IsCompleted) {
                    Received packet;
                    if(inbox.TryTake(out packet,100)) {
                        if(BitConverter.ToUInt16(packet.Bytes,6)==4)KeepPage(packet);
                        else if(BitConverter.ToUInt16(packet.Bytes,6)==5)KeepFineRoot(packet);
                        else if(BitConverter.ToUInt16(packet.Bytes,6)==6)KeepRtc(packet);
                        else{
                        string outcome="accepted",why=null;
                        try {
                            var r=NpUsbTimingWire.Parse(packet.Bytes,uid,family,token,packet.Sequence);
                            if(r.Kind!=1) {
                                if(generation!=0&&generation!=r.Generation){stopQueries=true;if(FirstSyncEnabled){firstSyncInvalid=true;FirstSyncError="Acquisition generation changed.";}throw new InvalidDataException("Acquisition generation changed.");}
                                if(r.Generation!=0)generation=r.Generation;
                                KeepSource(r);
                            } else {
                                if(r.Id!=pendingId||r.Prearm||r.Generation!=generation)throw new InvalidDataException("Clock reply has no matching active request.");
                                pendingId=0;
                            }
                        }catch(Exception ex){outcome="rejected";why=ex.Message;}
                        Log(new {kind="record",outcome,error=why,receive_qpc=packet.Qpc,container_sequence=packet.Sequence.ToString(CultureInfo.InvariantCulture),raw_hex=Hex(packet.Bytes)});
                        }
                    }
                    long now=Stopwatch.GetTimestamp();
                    if(pendingId!=0&&now-pendingSince>=10*Stopwatch.Frequency){Log(new {kind="unanswered",request_id=pendingId});pendingId=0;}
                    if(!stopQueries&&!firstSyncPageContext&&pendingId==0&&generation!=0&&now>=nextQuery) {
                        if(nextId==UInt32.MaxValue){stopQueries=true;Error="Timing ID lifetime exhausted.";continue;}
                        uint id=nextId++;byte[] request=NpUsbTimingWire.Query(id,token,generation);long send=Stopwatch.GetTimestamp();nextQuery=send+5*Stopwatch.Frequency;pendingId=id;pendingSince=send;
                        Log(new {kind="request",phase="active",request_id=id,generation,send_qpc=send,raw_hex=Hex(request)});
                        try{write(request);}catch(Exception ex){if(!stopQueries)Error=ex.Message;Log(new {kind=stopQueries?"request_cancelled":"write_error",request_id=id,error=ex.Message});stopQueries=true;}
                    }
                }
            }catch(Exception ex){Error=ex.Message;stopQueries=true;}
        }
        internal void StopQueries() {
            stopQueries=true;
            var writer=output;if(writer!=null){writer.Abort();writer.Dispose();output=null;}
        }
        public void Dispose() {
            if(disposed)return;StopQueries();disposed=true;inbox.CompleteAdding();if(worker!=null)worker.Join();
            try{Log(new {kind="close",error=Error,dropped_records=Interlocked.Read(ref dropped),unanswered_request_id=pendingId,
                first_sync_enabled=FirstSyncEnabled,first_sync_qualified=FirstSyncQualified,first_sync_error=FirstSyncError,first_sync_invalid=firstSyncInvalid,first_sync_missing_mask=FirstSyncMissingMask,
                fine_root_enabled=FineRootEnabled,fine_root_qualified=FineRoot!=null,fine_root_error=fineRootError,
                rtc_enabled=RtcEnabled,rtc_bound=RtcBound,rtc_qualified=RtcQualified,rtc_error=rtcError,rtc_invalid=rtcInvalid,rtc_missing_mask=RtcMissingMask,rtc_pages_before_root=rtcPagesBeforeRoot});}catch(Exception ex){Error=ex.Message;}
            if(file!=null){try{file.Dispose();}catch(Exception ex){Error=ex.Message;}file=null;}inbox.Dispose();
            if(!Enabled&&Error==null)return;
            try{File.WriteAllText(Path.Combine(folder,"usb_timing_status.json"),json.Serialize(new {schema=2,enabled=Enabled,error=Error,dropped_records=dropped,
                first_sync_enabled=FirstSyncEnabled,first_sync_qualified=FirstSyncQualified,first_sync_error=FirstSyncError,first_sync_invalid=firstSyncInvalid,first_sync_missing_mask=FirstSyncMissingMask,
                fine_root_enabled=FineRootEnabled,fine_root_qualified=FineRoot!=null,fine_root_error=fineRootError,
                rtc_enabled=RtcEnabled,rtc_bound=RtcBound,rtc_qualified=RtcQualified,rtc_error=rtcError,rtc_invalid=rtcInvalid,rtc_missing_mask=RtcMissingMask,rtc_pages_before_root=rtcPagesBeforeRoot,
                first_sync_negotiation=new {attempted=firstSyncAttempted,initial_info_hex=Hex(firstSyncProbe),probe_error=firstSyncProbeError,request_hex=Hex(firstSyncNegotiation),confirmed_info_hex=Hex(firstSyncConfirmed),
                    cleanup_disable_hex=Hex(firstSyncDisable),cleanup_error=firstSyncDisableError,cleanup_observation_hex=Hex(firstSyncObservation),observation_error=firstSyncObservationError},
                limitation="Optional request-correlated counter evidence only; no physical ADC/filter/trigger calibration or inferred missing interval."}));}catch{}
        }
    }
}
