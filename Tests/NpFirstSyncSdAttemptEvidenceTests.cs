using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using USB512_interface;

static class NpFirstSyncSdAttemptEvidenceTests
{
    const string Uid="001D00065942500B20313354";static int checks;static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
    static void Need(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static void Put(byte[] b,int at,uint v){Buffer.BlockCopy(BitConverter.GetBytes(v),0,b,at,4);}
    static byte[] Legacy(int family,uint id=57){var b=new byte[64];Put(b,0,family==1?0x31444c4eU:0x32444c4eU);Put(b,4,family==1?1U:2U);Put(b,8,2);Put(b,12,id);Put(b,16,3);Put(b,20,20000);
        if(family==1){Put(b,24,8192);Put(b,28,1048576);Put(b,32,100);}Put(b,60,NpFirstSyncWire.Crc(b,60));return b;}
    static Dictionary<string,object> Read(NpFirstSyncSdAttempt a){return Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(a.EvidencePath));}
    sealed class Peer
    {
        internal int Family,Starts,Statuses;internal uint StartResult;internal bool Modern;internal bool DeviceStarted,RequireBefore=true,Unsupported,ThrowBefore,ThrowAfter,InvalidAck,InvalidStatus;internal CancellationTokenSource CancelDuringCaps,CancelAfterStart;
        internal NpFirstSyncSdClient Client;
        internal Task<byte[]> Exchange(byte[] request,CancellationToken cancel){
            ushort op=BitConverter.ToUInt16(request,6);bool start=op==1||op==3;
            if(op==0&&CancelDuringCaps!=null)CancelDuringCaps.Cancel();
            if(start){Starts++;if(RequireBefore){var before=Read(Client.Attempt);Need(Client.Attempt.BeforeDeliveryEvidencePersisted&&File.Exists(Client.Attempt.EvidencePath)&&Convert.ToString(before["phase"])=="before_delivery"&&
                    (bool)before["delivery_outcome_unknown"]&&!(bool)before["ack_validated"]&&Convert.ToString(before["start_ack_outcome"])=="unconfirmed","durable prepared-only attempt was not saved before Start delivery");}
                if(ThrowBefore)throw new IOException("transport failed before device dispatch");DeviceStarted=StartResult==0;if(ThrowAfter)throw new IOException("ACK lost after device dispatch");if(CancelAfterStart!=null)CancelAfterStart.Cancel();}
            if(op==2)Statuses++;
            uint result=Unsupported&&op==0?1U:start?StartResult:0U;
            ushort version=BitConverter.ToUInt16(request,4);
            var reply=new NpFirstSyncSdReply {Result=result,Supported=Modern?(version>=2?31U:7U):Family==1?1U:3U,RecordFeatures=op==0||result!=0?0U:version>=3?25U:version==2?9U:1U,ProfileCrc=uint.MaxValue};
            if(op==2){reply.Session=100;reply.Generation=9;reply.SourceEpoch=11;reply.ProfileCrc=0x12345678;}
            var raw=NpFirstSyncSd.BuildReply(request,Family,reply);if(start&&InvalidAck||op==2&&InvalidStatus)raw[60]^=1;return Task.FromResult(raw);
        }
    }
    static NpFirstSyncSdClient Client(Peer peer,string folder,Action<NpFirstSyncSdAttempt> callback=null){
        var client=new NpFirstSyncSdClient(Uid,peer.Family,7,()=>true,peer.Exchange);peer.Client=client;
        client.EvidenceChanged=callback??(attempt=>NpFirstSyncSdAttemptEvidence.Write(folder,attempt));return client;
    }
    static async Task Fails<T>(Func<Task> action,string why) where T:Exception {try{await action();}catch(T){checks++;return;}throw new Exception(why);}
    static async Task Run(string root)
    {
        foreach(int family in new[]{1,2}){
            string folder=Path.Combine(root,"family"+family);Directory.CreateDirectory(folder);var peer=new Peer {Family=family};var client=Client(peer,folder);byte[] source=Legacy(family),copy=(byte[])source.Clone();
            var attempt=await client.StartAsync(source,CancellationToken.None);var doc=Read(attempt);
            Need(peer.Starts==1&&!attempt.OutcomeUnknown&&attempt.BeforeDeliveryEvidencePersisted&&(bool)doc["ack_validated"]&&!(bool)doc["record_association_confirmed"]&&!(bool)doc["export_autoassociation_allowed"],"ACK/persistence status fabricated recording association");
            Need(source.SequenceEqual(copy)&&Convert.ToString(doc["legacy_request_hex"])==BitConverter.ToString(copy).Replace("-","")&&Convert.ToString(doc["request_hex"])==BitConverter.ToString(attempt.Request).Replace("-",""),"exact request bytes changed");
            var ack=(Dictionary<string,object>)doc["ack"];Need(ack["session_id"]==null&&ack["capture_generation"]==null,"pending zero ACK context became confirmed IDs");
            var intended=(Dictionary<string,object>)doc["intended"];Need(family==1?Convert.ToUInt32(intended["session_id"])==100&&Convert.ToUInt32(intended["start_lba"])==8192:intended["session_id"]==null&&intended["start_lba"]==null,"intended versus unknown IDs/LBA were confused");
            byte[] request=attempt.Request;request[0]^=1;byte[] rawAck=attempt.AcknowledgementRaw;rawAck[0]^=1;
            Need(attempt.Request[0]!=request[0]&&attempt.AcknowledgementRaw[0]!=rawAck[0],"retained protocol arrays were mutable");
            await client.StatusAsync(CancellationToken.None);doc=Read(attempt);Need((bool)doc["status_validated"]&&Convert.ToUInt32(((Dictionary<string,object>)doc["status"])["session_id"])==100&&!(bool)doc["record_association_confirmed"],"validated status did not remain a control association only");
            peer.InvalidStatus=true;await Fails<InvalidDataException>(()=>client.StatusAsync(CancellationToken.None),"bad status accepted");doc=Read(attempt);
            Need(!(bool)doc["status_validated"]&&doc["status"]==null&&doc["status_raw_hex"]!=null&&doc["status_error"]!=null,"invalid raw status became validated context");
            string first=attempt.EvidencePath;client.RetireAfterConfirmedStop(Uid,family,7,true);Need(Convert.ToString(Read(attempt)["phase"])=="retired_after_confirmed_stop"&&client.Attempt==null,"confirmed local retirement lost durable attempt");
            peer.InvalidStatus=false;var next=await client.StartAsync(Legacy(family,58),CancellationToken.None);
            Need(first!=next.EvidencePath&&attempt.Token!=next.Token&&File.Exists(first)&&Path.GetFileName(next.EvidencePath).Contains(next.Token.ToString("X16")),"another capture overwrote token/attempt evidence");
        }
        string gridFolder=Path.Combine(root,"v4-grid16");Directory.CreateDirectory(gridFolder);var gridPeer=new Peer {Family=2,Modern=true};var gridClient=Client(gridPeer,gridFolder);
        await gridClient.ReadFineAuxCapabilitiesAsync(CancellationToken.None);await gridClient.ReadFineCounter64CapabilitiesAsync(CancellationToken.None);await gridClient.ReadGrid16CapabilitiesAsync(CancellationToken.None);
        var grid=await gridClient.StartAsync(Legacy(2),CancellationToken.None);var gridDoc=Read(grid);
        Need(grid.Version==4&&Convert.ToUInt32(gridDoc["protocol_version"])==4&&gridPeer.Starts==1&&grid.BeforeDeliveryEvidencePersisted,"explicit v4 attempt/evidence version missing");
        Need(Convert.ToString(gridDoc["request_hex"])==BitConverter.ToString(grid.Request).Replace("-","")&&!(bool)gridDoc["record_association_confirmed"],"v4 raw request/association was changed");
        await gridClient.StatusAsync(CancellationToken.None);gridDoc=Read(grid);
        var statusRaw=grid.StatusRequest;Need(BitConverter.ToUInt16(statusRaw,4)==4&&BitConverter.ToUInt64(statusRaw,24)==grid.Token&&(bool)gridDoc["status_validated"]&&!(bool)gridDoc["record_association_confirmed"],"v4 exact STATUS token/version or control-only semantics lost");
        foreach(string fault in new[]{"before","after","invalid-ack","cancel-after"}){
            string folder=Path.Combine(root,fault);Directory.CreateDirectory(folder);using(var cancellation=new CancellationTokenSource()){
                var peer=new Peer {Family=1,ThrowBefore=fault=="before",ThrowAfter=fault=="after",InvalidAck=fault=="invalid-ack",CancelAfterStart=fault=="cancel-after"?cancellation:null};var client=Client(peer,folder);
                await Fails<Exception>(()=>client.StartAsync(Legacy(1),cancellation.Token),"uncertain/invalid Start unexpectedly succeeded");var attempt=client.Attempt;var doc=Read(attempt);
                Need(peer.Starts==1&&attempt.OutcomeUnknown&&doc["start_error"]!=null&&!(bool)doc["ack_validated"]&&doc["ack"]==null,"one-shot failure lost unknown outcome or validated bad ACK");
                if(fault=="invalid-ack"||fault=="cancel-after")Need(doc["ack_raw_hex"]!=null,"diagnostic reply after delivery was lost");
                await Fails<InvalidOperationException>(()=>client.StartAsync(Legacy(1,58),CancellationToken.None),"uncertain Start was replayed");Need(peer.Starts==1,"evidence update retried Start");
            }
        }
        string failed=Path.Combine(root,"evidence-failure");Directory.CreateDirectory(failed);var failurePeer=new Peer {Family=1,RequireBefore=false};
        var failureClient=Client(failurePeer,failed,a=>{if(a.EvidencePhase=="before_delivery")throw new IOException("generated metadata failure");NpFirstSyncSdAttemptEvidence.Write(failed,a);});
        var saved=await failureClient.StartAsync(Legacy(1),CancellationToken.None);var savedDoc=Read(saved);
        Need(failurePeer.Starts==1&&!saved.OutcomeUnknown&&!saved.BeforeDeliveryEvidencePersisted&&saved.EvidenceError!=null&&!(bool)savedDoc["before_delivery_evidence_persisted"]&&savedDoc["prior_evidence_save_error"]!=null,"metadata failure gated/replayed command or fabricated pre-delivery persistence");
        var allFailPeer=new Peer {Family=2,RequireBefore=false};var allFail=Client(allFailPeer,failed,a=>{throw new IOException("all evidence writes failed");});
        var live=await allFail.StartAsync(Legacy(2),CancellationToken.None);await allFail.StatusAsync(CancellationToken.None);allFail.RetireAfterConfirmedStop(Uid,2,7,true);
        Need(allFailPeer.Starts==1&&allFailPeer.Statuses==1&&live.EvidencePath==null&&live.EvidenceError!=null&&allFail.Attempt==null,"persistent metadata failure blocked normal start/status/stop ownership");
        string rejected=Path.Combine(root,"rejected");Directory.CreateDirectory(rejected);var rejectPeer=new Peer {Family=1,StartResult=2};var rejectClient=Client(rejectPeer,rejected);var reject=await rejectClient.StartAsync(Legacy(1),CancellationToken.None);var rejectedDoc=Read(reject);
        Need(reject.Rejected&&!rejectPeer.DeviceStarted&&(bool)rejectedDoc["ack_validated"]&&Convert.ToString(rejectedDoc["start_ack_outcome"])=="validated_rejection"&&!(bool)rejectedDoc["record_association_confirmed"],"validated rejection was confused with unknown/accepted recording");
        string beforeCancel=Path.Combine(root,"cancel-before");Directory.CreateDirectory(beforeCancel);using(var cancellation=new CancellationTokenSource()){
            var peer=new Peer {Family=1,CancelDuringCaps=cancellation};var client=Client(peer,beforeCancel);await Fails<OperationCanceledException>(()=>client.StartAsync(Legacy(1),cancellation.Token),"pre-delivery cancellation ignored");
            Need(peer.Starts==0&&client.Attempt==null&&Directory.GetFiles(beforeCancel).Length==0,"pre-delivery cancellation fabricated declaration");
        }
        string unsupported=Path.Combine(root,"legacy");Directory.CreateDirectory(unsupported);var oldPeer=new Peer {Family=1,Unsupported=true};var old=Client(oldPeer,unsupported);
        Need(await old.StartAsync(Legacy(1),CancellationToken.None)==null&&oldPeer.Starts==0&&Directory.GetFiles(unsupported).Length==0,"legacy fallback created NPSD evidence");
        foreach(bool cancellation in new[]{false,true})using(var cancel=new CancellationTokenSource()){
            string folder=Path.Combine(root,"recheck-start-"+cancellation);Directory.CreateDirectory(folder);bool owned=true;var peer=new Peer {Family=1};
            var client=new NpFirstSyncSdClient(Uid,1,7,()=>owned,peer.Exchange);peer.Client=client;
            client.EvidenceChanged=a=>{NpFirstSyncSdAttemptEvidence.Write(folder,a);if(a.EvidencePhase=="before_delivery"){if(cancellation)cancel.Cancel();else owned=false;}};
            await Fails<Exception>(()=>client.StartAsync(Legacy(1),cancel.Token),"ownership/cancel change during before-delivery evidence was ignored");
            var doc=Read(client.Attempt);Need(peer.Starts==0&&client.Attempt.OutcomeUnknown&&!client.Attempt.StartTransportEntered&&Convert.ToString(doc["phase"])=="prepared_not_delivered"&&!(bool)doc["start_transport_entered"],"prepared-only attempt entered Start after its evidence callback lost ownership");
        }
        foreach(bool cancellation in new[]{false,true})using(var cancel=new CancellationTokenSource()){
            string folder=Path.Combine(root,"recheck-status-"+cancellation);Directory.CreateDirectory(folder);bool owned=true;var peer=new Peer {Family=1};
            var client=new NpFirstSyncSdClient(Uid,1,7,()=>owned,peer.Exchange);peer.Client=client;client.EvidenceChanged=a=>NpFirstSyncSdAttemptEvidence.Write(folder,a);
            var attempt=await client.StartAsync(Legacy(1),CancellationToken.None);
            client.EvidenceChanged=a=>{NpFirstSyncSdAttemptEvidence.Write(folder,a);if(a.EvidencePhase=="status_query"){if(cancellation)cancel.Cancel();else owned=false;}};
            await Fails<Exception>(()=>client.StatusAsync(cancel.Token),"ownership/cancel change during status evidence was ignored");
            var doc=Read(attempt);Need(peer.Starts==1&&peer.Statuses==0&&!attempt.StatusTransportEntered&&Convert.ToString(doc["phase"])=="status_not_delivered"&&!(bool)doc["status_transport_entered"],"status exchange entered after its evidence callback lost ownership");
        }
    }
    static int Main(string[] args){try{string root=Path.Combine(args[0],Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);Run(root).GetAwaiter().GetResult();Console.WriteLine("PASS "+checks+" actual SD attempt/client/atomic persistence checks; source fixtures only, no hardware.");return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
