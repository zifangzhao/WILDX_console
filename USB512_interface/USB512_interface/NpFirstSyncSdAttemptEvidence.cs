using System;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;

namespace USB512_interface
{
    // One atomically replaced file per immutable attempt, never a global
    // recording matcher. A declared request is not proof of an on-card record.
    internal static class NpFirstSyncSdAttemptEvidence
    {
        static string Hex(byte[] b){return b==null?null:BitConverter.ToString(b).Replace("-","");}
        static object Reply(NpFirstSyncSdReply r){return r==null?null:new {result=r.Result,supported=r.Supported,record_features=r.RecordFeatures,
            session_id=r.Session==0?(uint?)null:r.Session,capture_generation=r.Generation==0?(uint?)null:r.Generation,
            source_epoch=r.SourceEpoch==0?(uint?)null:r.SourceEpoch,profile_crc=r.ProfileCrc==UInt32.MaxValue?(uint?)null:r.ProfileCrc,
            scope="Validated control reply association only; queued/historical acceptance does not prove recording, written data, source sync or timing."};}
        internal static string Write(string folder,NpFirstSyncSdAttempt attempt)
        {
            if(attempt==null||String.IsNullOrWhiteSpace(folder))throw new IOException("A host recording metadata folder is required for the SD timing receipt.");
            byte[] request=attempt.Request,legacy=attempt.Legacy,ackRaw=attempt.AcknowledgementRaw,statusRequest=attempt.StatusRequest,statusRaw=attempt.StatusRaw;
            var parsed=NpFirstSyncSd.ParseRequest(request,attempt.Family,attempt.Uid);
            if(parsed.Token!=attempt.Token||parsed.Id!=attempt.Id)throw new InvalidDataException("Immutable SD timing attempt identity changed.");
            NpFirstSyncSdReply ack=null,status=null;
            if(attempt.Acknowledgement!=null)ack=NpFirstSyncSd.ParseReply(ackRaw,request,attempt.Family);
            if(attempt.LastStatus!=null)status=NpFirstSyncSd.ParseReply(statusRaw,statusRequest,attempt.Family);
            uint mask=BitConverter.ToUInt32(legacy,16),hz=BitConverter.ToUInt32(legacy,20);
            string path=Path.Combine(folder,"sd_start_"+attempt.Token.ToString("X16",CultureInfo.InvariantCulture)+"_"+attempt.EvidenceId+".json");
            var document=new {schema=1,format="neuropixels.sd_first_sync_attempt",attempt_id=attempt.EvidenceId,device_uid=attempt.Uid,device_family=attempt.Family,
                connection_epoch=attempt.ConnectionEpoch,token_hex=attempt.Token.ToString("X16",CultureInfo.InvariantCulture),request_id=attempt.Id,operation=attempt.Operation,
                phase=attempt.EvidencePhase,created_utc_host=attempt.CreatedUtc,evidence_updated_utc_host=attempt.EvidenceUpdatedUtc,
                start_ack_outcome=attempt.Acknowledgement==null?"unconfirmed":attempt.Rejected?"validated_rejection":"validated_queued_or_historical_acceptance",
                start_transport_entered=attempt.StartTransportEntered,status_transport_entered=attempt.StatusTransportEntered,
                delivery_scope=!attempt.StartTransportEntered?"Prepared host request only; Start transport was never entered; no delivery or recording is asserted":"ACK and status fields describe control evidence only; transport exceptions may leave delivery unknown",
                recording_state="Not established by this receipt; use independent normal device status and on-card record evidence",
                declaration_persisted=true,before_delivery_evidence_persisted=attempt.BeforeDeliveryEvidencePersisted||attempt.EvidencePhase=="before_delivery",
                delivery_outcome_unknown=attempt.OutcomeUnknown,rejected=attempt.Rejected,start_error=attempt.Error,status_error=attempt.StatusError,
                prior_evidence_save_error=attempt.EvidenceError,request_hex=Hex(request),legacy_request_hex=Hex(legacy),ack_raw_hex=Hex(ackRaw),ack_validated=ack!=null,ack=Reply(ack),
                status_request_hex=Hex(statusRequest),status_raw_hex=Hex(statusRaw),status_validated=status!=null,status=Reply(status),
                intended=new {probe_mask=mask,ap_hz=hz,request_id=attempt.Id,
                    start_lba=attempt.Family==1?(uint?)BitConverter.ToUInt32(legacy,24):null,end_lba=attempt.Family==1?(uint?)BitConverter.ToUInt32(legacy,28):null,
                    session_id=attempt.Family==1?(uint?)BitConverter.ToUInt32(legacy,32):null,
                    requested_blocks=attempt.Family==2?(uint?)BitConverter.ToUInt32(legacy,24):null},
                record_association_confirmed=false,export_autoassociation_allowed=false,
                limitation="Host attempt evidence only. Intended IDs/LBA/session are not confirmed stored record identities. No approximate match by time, profile, BLE address, LBA or capacity. Import and independently bind this receipt, or use an on-card declaration. A missing NPSTART receipt cannot be identified from an otherwise legacy sector on another PC.",
                timing_scope="All host times are audit provenance, never acquisition T0. Invalid/unowned raw replies are diagnostic only. No source sync, native IMU or first-sample time is fabricated."};
            NpMetadataFile.WriteText(path,new JavaScriptSerializer().Serialize(document)+"\n");attempt.EvidenceStored(path);return path;
        }
    }
}
