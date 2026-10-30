using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace USB512_interface
{
    // Lab-only host observations, never a device clock or USB packet timestamp.
    // No payload copies, per-block callbacks or active-capture file writes.
    internal sealed class NpV2ReadHistory
    {
        internal const int Capacity = 1024;
        internal const string FileName = "usb_read_history_lab.csv";
        internal struct Entry
        {
            internal long RecordId, CompletionId, IssueId, IssueQpc, QueueReturnQpc, CompletionQpc, VerificationQpc, RequeueQpc, StopQpc;
            internal int Slot, RequestedBytes;
            internal int? ReceivedBytes, IssueOutstanding, QueueOutstanding, HarvestOutstanding, QueueStatus, HarvestStatus;
            internal long? RequeueIssueId;
            internal bool? HarvestRetired, StopObservedRetired;
            internal long? VerifiedBlocks;
            internal ulong? FirstSequence, LastSequence;
            internal bool AllVerified;
        }
        private readonly object gate = new object();
        private readonly Entry[] entries = new Entry[Capacity];
        private readonly long[] slotTickets = new long[64], slotIssues = new long[64], lastAccepted = new long[64];
        private readonly bool[] owned = new bool[64];
        private long total, completions, overwritten, lateUpdates, invalidMappings;
        private int outstanding;

        internal static NpV2ReadHistory ForLab()
        { return Environment.GetEnvironmentVariable("WILDX_V2_USB_HISTORY_LAB") == "1" ? new NpV2ReadHistory() : null; }

        internal void Issue(long issue,int slot,long qpc,int requested)
        {
            lock (gate)
            {
                if(slot<0||slot>=slotTickets.Length||owned[slot]){invalidMappings++;return;}
                long id = ++total;
                if (id > Capacity) overwritten++;
                slotTickets[slot]=id;slotIssues[slot]=issue;
                entries[(int)((id - 1) % Capacity)] = new Entry { RecordId=id, IssueId=issue,
                    Slot=slot, IssueQpc=qpc, IssueOutstanding=outstanding, RequestedBytes=requested };
            }
        }

        internal void QueueReturned(long issue,int slot,long qpc,int status)
        {
            lock(gate)
            {
                int index;if(!Current(issue,slot,out index))return;
                Entry e=entries[index];
                if(e.QueueReturnQpc!=0||qpc<e.IssueQpc){invalidMappings++;return;}
                e.QueueReturnQpc=qpc;e.QueueStatus=status;
                if(status==0)
                {
                    owned[slot]=true;outstanding++;
                    long prior=lastAccepted[slot];
                    if(prior!=0)
                    {
                        int at=(int)((prior-1)%Capacity);Entry old=entries[at];
                        if(old.RecordId==prior){old.RequeueQpc=qpc;old.RequeueIssueId=issue;entries[at]=old;}
                        else lateUpdates++;
                    }
                    lastAccepted[slot]=e.RecordId;
                }
                e.QueueOutstanding=outstanding;entries[index]=e;
            }
        }

        internal long Completion(long issue,int slot,long qpc,int requested,int received)
        {return Harvest(issue,slot,qpc,0,received,true,requested);}

        internal long Harvest(long issue,int slot,long qpc,int status,int? received,bool retired,int requested)
        {
            lock(gate)
            {
                if(slot<0||slot>=slotTickets.Length||slotTickets[slot]==0||slotIssues[slot]!=issue){invalidMappings++;return 0;}
                // Even an overwritten history row cannot leave the independent
                // 64-slot ownership/depth observation stuck at an old issue.
                if(entries[(int)((slotTickets[slot]-1)%Capacity)].RecordId!=slotTickets[slot])
                {if(retired&&owned[slot]){owned[slot]=false;outstanding--;}lateUpdates++;return 0;}
                int index;if(!Current(issue,slot,out index))return 0;
                Entry e=entries[index];
                if(e.CompletionQpc!=0||!e.QueueStatus.HasValue||e.QueueStatus!=0||qpc<e.QueueReturnQpc||requested!=e.RequestedBytes)
                {invalidMappings++;return 0;}
                e.CompletionId=++completions;e.CompletionQpc=qpc;e.HarvestStatus=status;
                e.ReceivedBytes=received;e.HarvestRetired=retired;
                if(retired&&owned[slot]){owned[slot]=false;outstanding--;}
                e.HarvestOutstanding=outstanding;entries[index]=e;return e.RecordId;
            }
        }

        // Active=false is existing native-retirement evidence, not a timestamp
        // of physical completion. No synthetic received bytes or block IDs.
        internal void Stopped(long issue,int slot,long qpc,bool active)
        {
            lock(gate)
            {
                int index;if(slot<0||slot>=slotTickets.Length||slotTickets[slot]==0)return;
                if(slotIssues[slot]!=issue){invalidMappings++;return;}
                if(!active&&owned[slot]){owned[slot]=false;outstanding--;}
                if(!Current(issue,slot,out index))return;
                Entry e=entries[index];e.StopQpc=qpc;e.StopObservedRetired=!active;
                entries[index]=e;
            }
        }

        private bool Current(long issue,int slot,out int index)
        {
            index=0;
            if(slot<0||slot>=slotTickets.Length||slotTickets[slot]==0){invalidMappings++;return false;}
            long id=slotTickets[slot];index=(int)((id-1)%Capacity);
            if(entries[index].RecordId!=id){lateUpdates++;return false;}
            if(entries[index].IssueId!=issue){invalidMappings++;return false;}
            return true;
        }

        // The single ordered verifier supplies its accepted prefix count. A
        // later DAT/sensor error must not erase blocks it already verified.
        internal void Verified(long id, byte[] bytes, int received, long before, long after, long qpc)
        {
            lock (gate)
            {
                if (id <= 0 || id > total) { invalidMappings++; return; }
                int index = (int)((id - 1) % Capacity);
                Entry e = entries[index];
                if (e.RecordId != id) { lateUpdates++; return; }
                if (e.VerificationQpc != 0) { invalidMappings++; return; }
                long count = after - before;
                if (e.HarvestStatus!=0 || e.HarvestRetired!=true || bytes == null || received < 0 || received > bytes.Length || received != e.ReceivedBytes ||
                    received % NpV2UsbDevice.BlockBytes != 0 || before < 0 || after < before ||
                    count > received / NpV2UsbDevice.BlockBytes || qpc < e.CompletionQpc)
                { invalidMappings++; return; }
                e.VerificationQpc=qpc; e.VerifiedBlocks=count;
                if (count > 0)
                {
                    int last = checked(((int)count - 1) * NpV2UsbDevice.BlockBytes);
                    // Only expose IDs from actual verified NP2USB03 headers.
                    if (V3(bytes,0) && V3(bytes,last))
                    {
                        e.FirstSequence=BitConverter.ToUInt64(bytes,24);
                        e.LastSequence=BitConverter.ToUInt64(bytes,last+24);
                        e.AllVerified=count == received / NpV2UsbDevice.BlockBytes;
                    }
                }
                entries[index]=e;
            }
        }

        private static bool V3(byte[] b,int at)
        { return b[at]=='N'&&b[at+1]=='P'&&b[at+2]=='2'&&b[at+3]=='U'&&b[at+4]=='S'&&b[at+5]=='B'&&b[at+6]=='0'&&b[at+7]=='3'; }

        internal Entry[] Snapshot(out long completions, out long lost, out long late, out long invalid)
        {
            lock (gate)
            {
                completions=total; lost=overwritten; late=lateUpdates; invalid=invalidMappings;
                var copy=new Entry[(int)Math.Min(total,Capacity)];
                long first=total-copy.Length+1;
                for(int i=0;i<copy.Length;i++)copy[i]=entries[(int)((first+i-1)%Capacity)];
                return copy;
            }
        }

        internal void Save(string folder)
        {
            long completions,lost,late,invalid;
            Entry[] copy=Snapshot(out completions,out lost,out late,out invalid);
            using(var stream=new FileStream(Path.Combine(folder,FileName),FileMode.CreateNew,FileAccess.Write,FileShare.Read))
            using(var output=new StreamWriter(stream,new UTF8Encoding(false)))
            {
                output.WriteLine("# schema=2; lab_only=true; host_observation=api_issue_and_harvest; device_clock_mapping=unknown; utc_mapping=none");
                output.WriteLine("# qpc_frequency="+N(Stopwatch.Frequency)+"; capacity="+N(Capacity)+"; total_issues="+N(completions)+"; total_harvests="+N(this.completions)+
                    "; history_overwritten="+N(lost)+"; late_updates="+N(late)+"; invalid_mapping_updates="+N(invalid)+"; final_outstanding_observed="+N(outstanding));
                output.WriteLine("# Verified IDs are NP2USB03 header+24 wire-block IDs, not DAT persistence or physical USB timing; blank=unknown.");
                output.WriteLine("# Outstanding=API-accepted but not yet proven retired; includes physically completed unharvested requests; not HCD/bus pending depth. Status0=API success,positive=Win32 error,-1=other error. Stop fields are stopped observations only.");
                output.WriteLine("record_id,completion_id,read_issue_id,native_slot,issue_qpc,issue_outstanding,queue_return_qpc,queue_status,queue_outstanding,host_completion_qpc,harvest_status,harvest_retired,harvest_outstanding,requested_bytes,received_bytes,verification_done_qpc,verified_block_count,first_verified_NP_seq,last_verified_NP_seq,all_received_blocks_verified,next_successful_requeue_qpc,next_requeue_issue_id,stop_observed_qpc,stop_observed_retired");
                foreach(Entry e in copy)output.WriteLine(String.Join(",",new[]{N(e.RecordId),Q(e.CompletionId),N(e.IssueId),N(e.Slot),Q(e.IssueQpc),I(e.IssueOutstanding),
                    Q(e.QueueReturnQpc),I(e.QueueStatus),I(e.QueueOutstanding),Q(e.CompletionQpc),I(e.HarvestStatus),B(e.HarvestRetired),I(e.HarvestOutstanding),
                    N(e.RequestedBytes),I(e.ReceivedBytes),Q(e.VerificationQpc),
                    e.VerifiedBlocks.HasValue?N(e.VerifiedBlocks.Value):"",e.FirstSequence.HasValue?U(e.FirstSequence.Value):"",
                    e.LastSequence.HasValue?U(e.LastSequence.Value):"",e.VerifiedBlocks.HasValue?(e.AllVerified?"true":"false"):"",
                    Q(e.RequeueQpc),e.RequeueIssueId.HasValue?N(e.RequeueIssueId.Value):"",Q(e.StopQpc),B(e.StopObservedRetired)}));
            }
        }
        private static string N(long n){return n.ToString(CultureInfo.InvariantCulture);}
        private static string U(ulong n){return n.ToString(CultureInfo.InvariantCulture);}
        private static string Q(long n){return n==0?"":N(n);}
        private static string I(int? n){return n.HasValue?N(n.Value):"";}
        private static string B(bool? n){return n.HasValue?(n.Value?"true":"false"):"";}
    }
}
