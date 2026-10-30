using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using USB512_interface;

static class NpFirstSyncSdClientTests {
    const string Uid="001D00065942500B20313354",OtherUid="001A0004434250062039314E";
    const int Epoch=7;
    static int checks;
    static void Need(bool value,string message){Interlocked.Increment(ref checks);if(!value)throw new Exception(message);}
    static void Put(byte[] b,int at,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,at,4);}
    static void Put64(byte[] b,int at,ulong value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,at,8);}
    static void Seal(byte[] b){Put(b,b.Length-4,NpFirstSyncWire.Crc(b,b.Length-4));}
    static byte[] Legacy(int family,uint id=57,bool scheduled=false){
        var b=new byte[64];Put(b,0,family==1?0x31444c4eU:0x32444c4eU);Put(b,4,(uint)family);Put(b,8,scheduled?(family==1?6U:4U):2U);Put(b,12,id);Put(b,16,family==1?3U:7U);Put(b,20,20000);
        if(family==1){Put(b,24,8192);Put(b,28,1048576);Put(b,32,100);Put(b,36,1);if(scheduled)Put64(b,44,1793200000123456UL);}
        else{Put(b,24,400);if(scheduled)Put64(b,28,1793200000123456UL);}
        Seal(b);return b;
    }
    static async Task Fails<T>(Func<Task> action,string message) where T:Exception {
        bool failed=false;try{await action();}catch(T){failed=true;}Need(failed,message);
    }
    static void Fails<T>(Action action,string message) where T:Exception {
        bool failed=false;try{action();}catch(T){failed=true;}Need(failed,message);
    }
    sealed class Fake {
        internal readonly int Family;
        internal int CurrentEpoch=Epoch;
        internal string CurrentUid=Uid;
        internal bool Lease=true;
        internal uint Supported,FineAuxSupported,FineCounter64Supported,Grid16Supported;
        internal readonly List<byte[]> Calls=new List<byte[]>();
        internal Func<byte[],CancellationToken,Task<byte[]>> Handler;
        internal Fake(int family){Family=family;Supported=family==1?1U:3U;FineAuxSupported=family==1?13U:15U;FineCounter64Supported=family==1?29U:31U;Grid16Supported=31U;Handler=(q,c)=>Task.FromResult(Reply(q));}
        internal NpFirstSyncSdClient Client(){return new NpFirstSyncSdClient(Uid,Family,Epoch,()=>Lease&&CurrentEpoch==Epoch&&CurrentUid==Uid,Exchange);}
        Task<byte[]> Exchange(byte[] q,CancellationToken c){
            Need(BitConverter.ToUInt32(q,0)==NpFirstSyncSd.RequestMagic,"Helper sent a legacy/non-NPSD command");
            Calls.Add((byte[])q.Clone());return Handler(q,c);
        }
        internal int Starts {get{return Calls.Count(q=>BitConverter.ToUInt16(q,6)==1||BitConverter.ToUInt16(q,6)==3);}}
        internal byte[] Reply(byte[] q,uint result=0,bool found=false){
            ushort op=BitConverter.ToUInt16(q,6),version=BitConverter.ToUInt16(q,4);bool record=result==0&&op!=NpFirstSyncSd.Caps;
            return NpFirstSyncSd.BuildReply(q,Family,new NpFirstSyncSdReply{Result=result,Supported=version==4?Grid16Supported:version==3?FineCounter64Supported:version==2?FineAuxSupported:Supported,RecordFeatures=record?(version>=3?25U:version==2?9U:1U):0,
                Session=record&&found?100U:0,Generation=record&&found?9U:0,SourceEpoch=record&&found?11U:0,ProfileCrc=UInt32.MaxValue});
        }
    }
    static async Task OldFirmware(){
        for(int kind=0;kind<8;kind++){
            int failure=kind;var fake=new Fake(1);
            fake.Handler=(q,c)=>{
                if(failure==0)throw new TimeoutException("Old firmware has no NPSD reply");
                if(failure==1)throw new IOException("Unavailable");
                if(failure==2)throw new NotSupportedException();
                if(failure==3)return Task.FromResult(new byte[64]);
                if(failure==4){var r=fake.Reply(q);r[12]^=1;Seal(r);return Task.FromResult(r);}
                if(failure==5)return Task.FromResult(fake.Reply(q,1));
                if(failure==7){q[12]^=1;Seal(q);return Task.FromResult(fake.Reply(q));}
                fake.Supported=0;return Task.FromResult(fake.Reply(q));
            };
            var client=fake.Client();Need(await client.StartAsync(Legacy(1),CancellationToken.None)==null,"Unsupported discovery attempted Start");
            Need(client.Attempt==null&&fake.Starts==0&&fake.Calls.Count==1&&client.FallbackReason!=null,"Fallback lost pre-delivery distinction");
            Need(!await client.ReadCapabilitiesAsync(CancellationToken.None)&&fake.Calls.Count==1,"CAPS was retried within the client/epoch");
            Need(await client.StartAsync(Legacy(1),CancellationToken.None)==null&&fake.Calls.Count==1,"Unsupported fallback later replayed CAPS or Start");
        }
        foreach(string missing in new[]{(string)null,"",new string('0',24),new string('F',24)}){
            int calls=0;var client=new NpFirstSyncSdClient(missing,1,Epoch,()=>true,(q,c)=>{calls++;throw new Exception("Missing UID sent a command");});
            Need(await client.StartAsync(Legacy(1),CancellationToken.None)==null&&calls==0&&client.Attempt==null,"Missing exact UID did not fall back before delivery");
        }
    }
    static async Task QueuedAndStatus(int family){
        var fake=new Fake(family);var client=fake.Client();
        Need(await client.ReadCapabilitiesAsync(CancellationToken.None)&&await client.ReadCapabilitiesAsync(CancellationToken.None)&&fake.Calls.Count==1,"CAPS was not cached once");
        var original=Legacy(family);var before=(byte[])original.Clone();var attempt=await client.StartAsync(original,CancellationToken.None);
        Need(fake.Starts==1&&Object.ReferenceEquals(attempt,client.Attempt)&&!attempt.OutcomeUnknown&&!attempt.Rejected,"Valid queued ACK did not retain exactly one attempt");
        Need(attempt.Acknowledgement.Associated&&attempt.Acknowledgement.Session==0&&attempt.Acknowledgement.Generation==0&&attempt.Acknowledgement.SourceEpoch==0,"Queued association fabricated running/session proof");
        Need(attempt.Uid==Uid&&attempt.Family==family&&attempt.ConnectionEpoch==Epoch&&attempt.Token!=0&&attempt.Id==57,"Attempt journal identity lost");
        Need(original.SequenceEqual(before)&&attempt.Legacy.SequenceEqual(before)&&NpFirstSyncSd.Unwrap(attempt.Request,family,Uid).SequenceEqual(before),"Existing legacy Start changed while wrapping");
        var exposed=attempt.Request;exposed[12]^=1;Need(attempt.Request[12]!=exposed[12],"Journal request is externally mutable");
        int count=fake.Calls.Count;await Fails<InvalidOperationException>(async()=>{await client.StartAsync(Legacy(family,58),CancellationToken.None);},"Queued Start silently allowed another Start");
        Need(fake.Calls.Count==count,"Second Start replayed a mutation");
        await client.StatusAsync(CancellationToken.None);await client.StatusAsync(CancellationToken.None);
        var statuses=fake.Calls.Where(q=>BitConverter.ToUInt16(q,6)==NpFirstSyncSd.Status).ToArray();
        Need(statuses.Length==2&&statuses.All(q=>BitConverter.ToUInt64(q,24)==attempt.Token)&&statuses.Select(q=>BitConverter.ToUInt32(q,8)).Distinct().Count()==2&&statuses.All(q=>BitConverter.ToUInt32(q,8)>57),"STATUS did not use the same token and fresh IDs");
        fake.Handler=(q,c)=>Task.FromResult(fake.Reply(q,4));var missing=await client.StatusAsync(CancellationToken.None);
        Need(missing.Result==4&&!missing.Associated&&Object.ReferenceEquals(client.Attempt,attempt),"NOT_FOUND was treated as stopped or another record");
        Fails<InvalidOperationException>(()=>client.RetireAfterConfirmedStop(Uid,family,Epoch,false),"Missing fresh Stop proof retired context");
        Fails<InvalidOperationException>(()=>client.RetireAfterConfirmedStop(OtherUid,family,Epoch,true),"Other UID retired context");
        Fails<InvalidOperationException>(()=>client.RetireAfterConfirmedStop(Uid,3-family,Epoch,true),"Other family retired context");
        Fails<InvalidOperationException>(()=>client.RetireAfterConfirmedStop(Uid,family,Epoch+1,true),"Other epoch retired context");
        Need(Object.ReferenceEquals(client.Attempt,attempt),"Rejected Stop proof lost the attempt");
        count=fake.Calls.Count;client.RetireAfterConfirmedStop(Uid,family,Epoch,true);
        Need(client.Attempt==null&&fake.Calls.Count==count,"Explicit confirmed Stop issued a helper command");
        fake.Handler=(q,c)=>Task.FromResult(fake.Reply(q));var next=await client.StartAsync(Legacy(family,58),CancellationToken.None);
        Need(next.Token!=attempt.Token&&fake.Starts==2&&next.Acknowledgement.Session==0&&next.LastStatus==null,"Retired context/token leaked into the next record");
    }
    static async Task LostAndMalformedAck(){
        for(int kind=0;kind<7;kind++){
            int failure=kind;var fake=new Fake(2);var client=fake.Client();
            fake.Handler=(q,c)=>{
                if(BitConverter.ToUInt16(q,6)==0)return Task.FromResult(fake.Reply(q));
                Need(client.Attempt!=null&&client.Attempt.OutcomeUnknown,"Attempt context was not published before delivery");
                if(failure==0)throw new TimeoutException("Lost ACK");
                if(failure==1)throw new OperationCanceledException("Canceled after delivery");
                var r=fake.Reply(q);r[new[]{0,0,6,8,12,24,60}[failure]]^=1;if(failure!=6)Seal(r);return Task.FromResult(r);
            };
            await Fails<Exception>(async()=>{await client.StartAsync(Legacy(2),CancellationToken.None);},"Uncertain/bad ACK was accepted");
            var attempt=client.Attempt;Need(attempt!=null&&attempt.OutcomeUnknown&&attempt.Acknowledgement==null&&attempt.Error!=null&&fake.Starts==1,"Unknown delivery attempt was lost");
            int count=fake.Calls.Count;await Fails<InvalidOperationException>(async()=>{await client.StartAsync(Legacy(2,58),CancellationToken.None);},"Unknown attempt allowed another Start");
            Need(fake.Calls.Count==count,"Unknown Start was retried or fell back to legacy");
            fake.Handler=(q,c)=>Task.FromResult(fake.Reply(q,4));await client.StatusAsync(CancellationToken.None);
            Need(Object.ReferenceEquals(client.Attempt,attempt)&&attempt.OutcomeUnknown&&fake.Starts==1,"Read-only NOT_FOUND cleared unknown delivery");
            if(failure==0){
                Fails<InvalidOperationException>(()=>client.RetireAfterConfirmedStop(Uid,2,Epoch,false),"Unknown Start retired without ordered Stop proof");
                client.RetireAfterConfirmedStop(Uid,2,Epoch,true);fake.Handler=(q,c)=>Task.FromResult(fake.Reply(q));
                var next=await client.StartAsync(Legacy(2,59),CancellationToken.None);
                Need(next.Token!=attempt.Token&&fake.Starts==2&&next.LastStatus==null,"Ordered Stop did not retire unknown context without token reuse");
            }
        }
    }
    static async Task CancellationAndBinding(){
        var fake=new Fake(1);var client=fake.Client();using(var cancel=new CancellationTokenSource()){
            cancel.Cancel();await Fails<OperationCanceledException>(async()=>{await client.StartAsync(Legacy(1),cancel.Token);},"Canceled preflight sent Start");
            Need(fake.Calls.Count==0&&client.Attempt==null,"Preflight cancellation retained a nonexistent delivery");
        }
        fake=new Fake(1);client=fake.Client();using(var cancel=new CancellationTokenSource()){
            fake.Handler=(q,c)=>{cancel.Cancel();return Task.FromResult(fake.Reply(q));};
            await Fails<OperationCanceledException>(async()=>{await client.StartAsync(Legacy(1),cancel.Token);},"Cancellation after CAPS still sent Start");
            Need(fake.Starts==0&&client.Attempt==null,"Canceled CAPS became a Start attempt");
        }
        fake=new Fake(1);client=fake.Client();using(var cancel=new CancellationTokenSource()){
            fake.Handler=(q,c)=>{if(BitConverter.ToUInt16(q,6)!=0)cancel.Cancel();return Task.FromResult(fake.Reply(q));};
            await Fails<OperationCanceledException>(async()=>{await client.StartAsync(Legacy(1),cancel.Token);},"Cancellation during Start ACK was ignored");
            Need(fake.Starts==1&&client.Attempt.OutcomeUnknown,"Post-delivery cancellation lost unknown context");
        }
        for(int phase=0;phase<3;phase++){
            int change=phase;fake=new Fake(2);client=fake.Client();
            if(change==0)fake.CurrentEpoch++;
            fake.Handler=(q,c)=>{if(change==1||change==2&&BitConverter.ToUInt16(q,6)!=0)fake.CurrentEpoch++;return Task.FromResult(fake.Reply(q));};
            await Fails<InvalidOperationException>(async()=>{await client.StartAsync(Legacy(2),CancellationToken.None);},"Changed connection epoch was accepted");
            Need(fake.Starts==(change==2?1:0)&&(change==2?client.Attempt!=null&&client.Attempt.OutcomeUnknown:client.Attempt==null),"Epoch change crossed the delivery boundary incorrectly");
            int calls=fake.Calls.Count;await Fails<InvalidOperationException>(async()=>{await client.StatusAsync(CancellationToken.None);},"Status substituted a changed connection");
            Fails<InvalidOperationException>(()=>client.RetireAfterConfirmedStop(Uid,2,fake.CurrentEpoch,true),"Reconnect retired old context");
            Need(fake.Calls.Count==calls,"Changed epoch sent another command");
        }
        fake=new Fake(2);client=fake.Client();fake.Handler=(q,c)=>{if(BitConverter.ToUInt16(q,6)!=0)fake.CurrentUid=OtherUid;return Task.FromResult(fake.Reply(q));};
        await Fails<InvalidOperationException>(async()=>{await client.StartAsync(Legacy(2),CancellationToken.None);},"UID change accepted the old ACK");
        Need(client.Attempt.Uid==Uid&&client.Attempt.OutcomeUnknown&&fake.Starts==1,"UID substitution rewrote attempt identity");
        fake=new Fake(2);client=fake.Client();fake.Handler=(q,c)=>{
            if(BitConverter.ToUInt16(q,6)!=0){q[12]^=1;Seal(q);}return Task.FromResult(fake.Reply(q));
        };
        await Fails<InvalidDataException>(async()=>{await client.StartAsync(Legacy(2),CancellationToken.None);},"Transport mutation replaced the expected Start UID");
        Need(client.Attempt.Uid==Uid&&client.Attempt.OutcomeUnknown&&fake.Starts==1,"Transport mutation changed the journal binding");
    }
    static async Task ScheduleAndIsolation(){
        var fake=new Fake(2);fake.Supported=1;var client=fake.Client();var scheduled=Legacy(2,57,true);var original=(byte[])scheduled.Clone();
        Need(await client.StartAsync(scheduled,CancellationToken.None)==null&&client.Attempt==null&&fake.Starts==0&&fake.Calls.Count==1,"Missing scheduled capability did not fall back before write");
        fake=new Fake(1);client=fake.Client();Need(await client.StartAsync(Legacy(1,57,true),CancellationToken.None)==null&&fake.Starts==0,"V1 schedule was truncated into the wrapper");
        fake=new Fake(2);client=fake.Client();var attempt=await client.StartAsync(scheduled,CancellationToken.None);
        Need(attempt.Operation==NpFirstSyncSd.Scheduled&&fake.Starts==1&&scheduled.SequenceEqual(original)&&NpFirstSyncSd.Unwrap(attempt.Request,2,Uid).SequenceEqual(original),"V2 op4 scheduled UTC/payload was not preserved byte-for-byte");
        fake=new Fake(1);client=fake.Client();fake.Handler=(q,c)=>Task.FromResult(fake.Reply(q,BitConverter.ToUInt16(q,6)==0?0U:3U));
        attempt=await client.StartAsync(Legacy(1),CancellationToken.None);
        Need(attempt.Rejected&&!attempt.OutcomeUnknown&&attempt.Acknowledgement.Session==0&&attempt.Acknowledgement.RecordFeatures==0&&attempt.LastStatus==null,"Explicit rejection leaked record association or became fallback");
        int count=fake.Calls.Count;await Fails<InvalidOperationException>(async()=>{await client.StartAsync(Legacy(1,58),CancellationToken.None);},"Rejected attempt automatically allowed another Start");
        Need(fake.Calls.Count==count,"Rejected Start replayed a command");
        fake.Handler=(q,c)=>{var r=fake.Reply(q,4);Put(r,44,987);Seal(r);return Task.FromResult(r);};
        await Fails<InvalidDataException>(async()=>{await client.StatusAsync(CancellationToken.None);},"NOT_FOUND leaked another recording's session");
        Need(attempt.LastStatus==null&&attempt.Acknowledgement.Session==0&&Object.ReferenceEquals(client.Attempt,attempt),"Malformed error context replaced the rejected attempt");
        foreach(int at in new[]{6,8,12,24}){
            fake.Handler=(q,c)=>{var r=fake.Reply(q);r[at]^=1;Seal(r);return Task.FromResult(r);};
            await Fails<InvalidDataException>(async()=>{await client.StatusAsync(CancellationToken.None);},"Mismatched Status identity was accepted");
            Need(attempt.LastStatus==null,"Bad Status leaked an association");
        }
    }
    static async Task InFlight(){
        var fake=new Fake(2);var client=fake.Client();var delivered=new TaskCompletionSource<byte[]>();byte[] start=null;
        fake.Handler=(q,c)=>{if(BitConverter.ToUInt16(q,6)==0)return Task.FromResult(fake.Reply(q));start=q;return delivered.Task;};
        var pending=client.StartAsync(Legacy(2),CancellationToken.None);
        Need(start!=null&&client.Attempt!=null&&client.Attempt.OutcomeUnknown&&fake.Starts==1,"Pending delivery has no inspectable attempt");
        await Fails<InvalidOperationException>(async()=>{await client.StartAsync(Legacy(2,58),CancellationToken.None);},"Concurrent Start was permitted");
        await Fails<InvalidOperationException>(async()=>{await client.StatusAsync(CancellationToken.None);},"Concurrent Status stole the lease");
        Fails<InvalidOperationException>(()=>client.RetireAfterConfirmedStop(Uid,2,Epoch,true),"Pending Start was retired before its ACK boundary");
        delivered.SetResult(fake.Reply(start));var attempt=await pending;
        Need(!attempt.OutcomeUnknown&&fake.Starts==1,"Pending operation caused replay");
    }
    static async Task ConcurrentStart(){
        var fake=new Fake(2);var client=fake.Client();var caps=new TaskCompletionSource<byte[]>();var entered=new TaskCompletionSource<bool>();byte[] capsRequest=null;
        fake.Handler=(q,c)=>{
            if(BitConverter.ToUInt16(q,6)==0){capsRequest=q;entered.SetResult(true);return caps.Task;}
            return Task.FromResult(fake.Reply(q));
        };
        using(var release=new ManualResetEventSlim(false)){
            var first=Task.Run(async()=>{release.Wait();return await client.StartAsync(Legacy(2),CancellationToken.None);});
            var second=Task.Run(async()=>{release.Wait();return await client.StartAsync(Legacy(2,58),CancellationToken.None);});
            release.Set();await entered.Task;var rejected=await Task.WhenAny(first,second);
            await Fails<InvalidOperationException>(async()=>{await rejected;},"Two concurrent Starts both acquired the client");
            Need(fake.Calls.Count==1&&fake.Starts==0&&client.Attempt==null,"Concurrent Starts both awaited CAPS or changed context");
            Fails<InvalidOperationException>(()=>client.RetireAfterConfirmedStop(Uid,2,Epoch,true),"Concurrent retirement bypassed the pending client lease");
            caps.SetResult(fake.Reply(capsRequest));var accepted=await(Object.ReferenceEquals(rejected,first)?second:first);
            Need(Object.ReferenceEquals(client.Attempt,accepted)&&fake.Starts==1&&fake.Calls.Count==2,"Concurrent Start replayed or overwrote the accepted attempt");
        }
    }
    static async Task FineCapabilities(){
        for(int family=1;family<=2;family++)foreach(uint support in family==1?new uint[]{1,5}:new uint[]{1,3,5,7}){
            var fake=new Fake(family);fake.Supported=support;var client=fake.Client();
            Need(!client.CapabilitiesChecked&&client.SupportedFeatures==0&&fake.Calls.Count==0,"Reading unprobed capabilities caused I/O or fabricated support");
            Need(await client.ReadCapabilitiesAsync(CancellationToken.None)&&client.SupportedFeatures==support&&fake.Calls.Count==1,"Exact legacy/fine CAPS mask was not retained");
            Need(await client.ReadCapabilitiesAsync(CancellationToken.None)&&fake.Calls.Count==1,"Repeated capability read retried transport");
            var attempt=await client.StartAsync(Legacy(family),CancellationToken.None);
            Need(fake.Starts==1&&fake.Calls.Count==2&&attempt.Acknowledgement.RecordFeatures==1&&client.SupportedFeatures==support,"Optional FineClock support changed recording delivery/features");
        }
        foreach(uint result in new uint[]{1,3,5}){
            var fake=new Fake(1);fake.Supported=5;fake.Handler=(q,c)=>Task.FromResult(fake.Reply(q,result));var client=fake.Client();
            Need(!await client.ReadCapabilitiesAsync(CancellationToken.None)&&client.SupportedFeatures==0&&fake.Starts==0,"Rejected CAPS enabled optional FineClock queries");
        }
        var noSchedule=new Fake(2);noSchedule.Supported=5;var scoped=noSchedule.Client();
        Need(await scoped.StartAsync(Legacy(2,57,true),CancellationToken.None)==null&&scoped.SupportedFeatures==5&&noSchedule.Starts==0,"FineClock implied unsupported scheduled recording");
    }
    static async Task FineAuxCapabilities(){
        for(int family=1;family<=2;family++){
            var old=new Fake(family);var legacyClient=old.Client();
            Need(!await legacyClient.ReadFineAuxCapabilitiesAsync(CancellationToken.None)&&legacyClient.FineAuxCapabilitiesChecked&&old.Calls.Count==1&&old.Starts==0,"Old peer received optional v2 query or Start");
            Need(!await legacyClient.ReadFineAuxCapabilitiesAsync(CancellationToken.None)&&old.Calls.Count==1,"Old peer capability result was retried");
            var legacyAttempt=await legacyClient.StartAsync(Legacy(family),CancellationToken.None);
            Need(legacyAttempt.Version==1&&old.Calls.Count==2&&old.Starts==1,"Old peer cached v1 Start changed");
            var fake=new Fake(family);fake.Supported=family==1?5U:7U;var client=fake.Client();
            Need(await client.ReadCapabilitiesAsync(CancellationToken.None)&&client.FineAuxSupportedFeatures==0&&!client.FineAuxCapabilitiesChecked,"Fine AUX support was invented before explicit discovery");
            Need(await client.ReadFineAuxCapabilitiesAsync(CancellationToken.None)&&await client.ReadFineAuxCapabilitiesAsync(CancellationToken.None)&&fake.Calls.Count==2,"Fine AUX CAPS was not exactly once/cached");
            Need(client.SupportedFeatures==fake.Supported&&client.FineAuxSupportedFeatures==fake.FineAuxSupported&&client.Attempt==null,"Fine discovery removed v1 support or made a Start attempt");
            var attempt=await client.StartAsync(Legacy(family),CancellationToken.None);
            Need(attempt.Version==2&&attempt.Acknowledgement.Version==2&&attempt.Acknowledgement.RecordFeatures==9&&attempt.Acknowledgement.Associated&&fake.Calls.Count==3&&fake.Starts==1,"Cached fine AUX Start added a query or used wrong version/features");
            Need(!await client.ReadFineAuxCapabilitiesAsync(CancellationToken.None)&&fake.Calls.Count==3&&ReferenceEquals(client.Attempt,attempt),"Fine capability call retargeted an existing attempt");
            var status=await client.StatusAsync(CancellationToken.None);
            Need(status.Version==2&&status.RecordFeatures==9&&BitConverter.ToUInt16(attempt.StatusRequest,4)==2&&BitConverter.ToUInt64(attempt.StatusRequest,24)==attempt.Token,"Fine AUX STATUS version/token changed");
        }
        // Even a fine-capable peer uses v1 until the caller explicitly warms v2.
        var unprobed=new Fake(2);unprobed.Supported=7;var unprobedClient=unprobed.Client();
        var ordinary=await unprobedClient.StartAsync(Legacy(2),CancellationToken.None);
        Need(ordinary.Version==1&&unprobed.Calls.Count==2&&unprobed.Calls.All(q=>BitConverter.ToUInt16(q,4)==1),"Start automatically probed optional v2 support");
        int before=unprobed.Calls.Count;Need(!await unprobedClient.ReadFineAuxCapabilitiesAsync(CancellationToken.None)&&unprobed.Calls.Count==before,"Discovery after v1 Start upgraded/retargeted the record");
        var schedule=new Fake(2);schedule.Supported=7;schedule.FineAuxSupported=13;var scheduledClient=schedule.Client();
        Need(await scheduledClient.ReadFineAuxCapabilitiesAsync(CancellationToken.None),"V2 optional scheduled bit became mandatory for fine CAPS");
        var scheduled=Legacy(2,57,true);var scheduledAttempt=await scheduledClient.StartAsync(scheduled,CancellationToken.None);
        Need(scheduledAttempt.Version==1&&schedule.Starts==1&&schedule.Calls.Count==3&&NpFirstSyncSd.Unwrap(scheduledAttempt.Request,2,Uid).SequenceEqual(scheduled),"Unsupported fine schedule did not choose cached legacy path before mutation");
        Need((await scheduledClient.StatusAsync(CancellationToken.None)).Version==1,"Legacy scheduled attempt switched STATUS to v2");
    }
    static async Task FineAuxFailures(){
        for(int kind=0;kind<7;kind++){
            int failure=kind;var fake=new Fake(1);fake.Supported=5;var client=fake.Client();await client.ReadCapabilitiesAsync(CancellationToken.None);
            fake.Handler=(q,c)=>{
                if(BitConverter.ToUInt16(q,4)==1)return Task.FromResult(fake.Reply(q));
                if(failure==0)throw new TimeoutException("Unknown v2 CAPS");
                if(failure==1)throw new IOException("Unavailable v2 CAPS");
                if(failure==2)throw new NotSupportedException();
                if(failure==3){fake.FineAuxSupported=5;return Task.FromResult(fake.Reply(q,1));}
                var raw=fake.Reply(q);if(failure==4){raw[12]^=1;Seal(raw);}if(failure==5)raw[60]^=1;if(failure==6){Put(raw,44,100);Seal(raw);}return Task.FromResult(raw);
            };
            Need(!await client.ReadFineAuxCapabilitiesAsync(CancellationToken.None)&&client.FineAuxCapabilitiesChecked&&client.SupportedFeatures==5&&client.FineAuxSupportedFeatures==0&&client.Attempt==null,"Optional failure changed v1/start state");
            Need(!await client.ReadFineAuxCapabilitiesAsync(CancellationToken.None)&&fake.Calls.Count==2,"Failed optional query was retried");
            var attempt=await client.StartAsync(Legacy(1),CancellationToken.None);
            Need(attempt.Version==1&&fake.Starts==1&&fake.Calls.Count==3,"Ordinary Start queried/retried after failed optional discovery");
        }
        for(int phase=0;phase<2;phase++)using(var cancel=new CancellationTokenSource()){
            int when=phase;var fake=new Fake(1);fake.Supported=5;var client=fake.Client();await client.ReadCapabilitiesAsync(CancellationToken.None);
            if(when==0)cancel.Cancel();else fake.Handler=(q,c)=>{cancel.Cancel();return Task.FromResult(fake.Reply(q));};
            await Fails<OperationCanceledException>(async()=>{await client.ReadFineAuxCapabilitiesAsync(cancel.Token);},"Optional discovery cancellation was ignored");
            Need(client.Attempt==null&&client.SupportedFeatures==5&&client.FineAuxSupportedFeatures==0&&fake.Starts==0&&fake.Calls.Count==(when==0?1:2),"Canceled optional query created record context or lost v1 support");
            fake.Handler=(q,c)=>Task.FromResult(fake.Reply(q));int calls=fake.Calls.Count;
            Need((await client.StartAsync(Legacy(1),CancellationToken.None)).Version==1&&fake.Calls.Count==calls+1,"Start retried canceled optional query");
        }
        foreach(bool uidChange in new[]{false,true}){
            var fake=new Fake(2);fake.Supported=7;var client=fake.Client();await client.ReadCapabilitiesAsync(CancellationToken.None);
            fake.Handler=(q,c)=>{if(uidChange)fake.CurrentUid=OtherUid;else fake.CurrentEpoch++;return Task.FromResult(fake.Reply(q));};
            await Fails<InvalidOperationException>(async()=>{await client.ReadFineAuxCapabilitiesAsync(CancellationToken.None);},"Changed UID/epoch accepted optional capability reply");
            Need(client.Attempt==null&&client.SupportedFeatures==7&&client.FineAuxSupportedFeatures==0&&fake.Starts==0,"Changed optional connection created a Start attempt");int calls=fake.Calls.Count;
            await Fails<InvalidOperationException>(async()=>{await client.StartAsync(Legacy(2),CancellationToken.None);},"Changed connection was substituted for Start");
            Need(fake.Calls.Count==calls,"Changed binding sent another command");
        }
        var lost=new Fake(2);lost.Supported=7;var lostClient=lost.Client();await lostClient.ReadFineAuxCapabilitiesAsync(CancellationToken.None);
        lost.Handler=(q,c)=>{throw new TimeoutException("Lost fine AUX Start ACK");};
        await Fails<TimeoutException>(async()=>{await lostClient.StartAsync(Legacy(2),CancellationToken.None);},"Lost fine AUX Start ACK was accepted");
        var unknown=lostClient.Attempt;Need(unknown!=null&&unknown.Version==2&&unknown.OutcomeUnknown&&lost.Starts==1,"Unknown v2 Start context lost");int prior=lost.Calls.Count;
        await Fails<InvalidOperationException>(async()=>{await lostClient.StartAsync(Legacy(2,58),CancellationToken.None);},"Unknown fine AUX Start was replayed");
        Need(!await lostClient.ReadFineAuxCapabilitiesAsync(CancellationToken.None)&&lost.Calls.Count==prior,"Unknown Start re-entered fine capability transport");
        lost.Handler=(q,c)=>Task.FromResult(lost.Reply(q,4));Need((await lostClient.StatusAsync(CancellationToken.None)).Version==2&&ReferenceEquals(lostClient.Attempt,unknown)&&lost.Starts==1,"Version2 NOT_FOUND retired unknown Start or used v1 STATUS");
        var rejected=new Fake(1);rejected.Supported=5;var rejectedClient=rejected.Client();await rejectedClient.ReadFineAuxCapabilitiesAsync(CancellationToken.None);
        rejected.Handler=(q,c)=>Task.FromResult(rejected.Reply(q,3));var rejectedAttempt=await rejectedClient.StartAsync(Legacy(1),CancellationToken.None);
        Need(rejectedAttempt.Version==2&&rejectedAttempt.Rejected&&!rejectedAttempt.OutcomeUnknown&&!rejectedAttempt.Acknowledgement.Associated&&rejectedAttempt.Acknowledgement.RecordFeatures==0,"Rejected v2 Start leaked an association or became legacy fallback");
        prior=rejected.Calls.Count;await Fails<InvalidOperationException>(async()=>{await rejectedClient.StartAsync(Legacy(1,58),CancellationToken.None);},"Rejected v2 Start allowed automatic replay");Need(rejected.Calls.Count==prior,"Rejected v2 Start sent a fallback mutation");
    }
    static async Task FineAuxInFlight(){
        var fake=new Fake(2);fake.Supported=7;var client=fake.Client();await client.ReadCapabilitiesAsync(CancellationToken.None);
        var pending=new TaskCompletionSource<byte[]>();byte[] query=null;fake.Handler=(q,c)=>{query=(byte[])q.Clone();return pending.Task;};
        var discovery=client.ReadFineAuxCapabilitiesAsync(CancellationToken.None);
        Need(query!=null&&BitConverter.ToUInt16(query,4)==2&&client.Attempt==null&&fake.Starts==0,"Optional query published a Start attempt");
        await Fails<InvalidOperationException>(async()=>{await client.StartAsync(Legacy(2),CancellationToken.None);},"Concurrent Start stole optional query ownership");
        await Fails<InvalidOperationException>(async()=>{await client.ReadFineAuxCapabilitiesAsync(CancellationToken.None);},"Concurrent optional discovery replayed query");
        Need(fake.Calls.Count==2&&client.Attempt==null,"Pending optional query permitted additional transport");
        pending.SetResult(fake.Reply(query));Need(await discovery,"Valid optional query was not cached");fake.Handler=(q,c)=>Task.FromResult(fake.Reply(q));
        Need((await client.StartAsync(Legacy(2),CancellationToken.None)).Version==2&&fake.Starts==1&&fake.Calls.Count==3,"Start after completed warm-up added discovery transport");
    }
    static Fake Continuous(int family){var fake=new Fake(family);fake.Supported=family==1?5U:7U;fake.FineAuxSupported=family==1?29U:31U;return fake;}
    static async Task FineCounter64Capabilities(){
        for(int family=1;family<=2;family++){
            var fake=Continuous(family);var client=fake.Client();Need(!await client.ReadFineCounter64CapabilitiesAsync(CancellationToken.None)&&!client.FineCounter64CapabilitiesChecked&&fake.Calls.Count==0,"Unwarmed v3 query created transport/cache");
            Need(await client.ReadFineAuxCapabilitiesAsync(CancellationToken.None)&&client.FineAuxSupportedFeatures==fake.FineAuxSupported&&fake.Calls.Count==2,"v2 discovery did not retain advertised16");
            Need(await client.ReadFineCounter64CapabilitiesAsync(CancellationToken.None)&&await client.ReadFineCounter64CapabilitiesAsync(CancellationToken.None)&&client.FineCounter64CapabilitiesChecked&&client.FineCounter64SupportedFeatures==fake.FineCounter64Supported&&fake.Calls.Count==3,"v3 discovery was not cached once");
            var attempt=await client.StartAsync(Legacy(family),CancellationToken.None);Need(attempt.Version==3&&attempt.Acknowledgement.RecordFeatures==25&&attempt.Acknowledgement.Associated&&fake.Starts==1&&fake.Calls.Count==4,"Cached v3 Start queried/fell back to9");
            Need(!await client.ReadFineCounter64CapabilitiesAsync(CancellationToken.None)&&fake.Calls.Count==4,"v3 discovery retargeted an attempted record");
            var status=await client.StatusAsync(CancellationToken.None);Need(status.Version==3&&status.RecordFeatures==25&&BitConverter.ToUInt16(attempt.StatusRequest,4)==3&&BitConverter.ToUInt64(attempt.StatusRequest,24)==attempt.Token,"Status lost version3/token");
            int calls=fake.Calls.Count;client.RetireAfterConfirmedStop(Uid,family,Epoch,true);var next=await client.StartAsync(Legacy(family,58),CancellationToken.None);Need(next.Version==3&&next.Token!=attempt.Token&&fake.Calls.Count==calls+1,"Confirmed Stop lost cache/reused token");
            var unprobed=Continuous(family);var unprobedClient=unprobed.Client();await unprobedClient.ReadFineAuxCapabilitiesAsync(CancellationToken.None);calls=unprobed.Calls.Count;
            Need((await unprobedClient.StartAsync(Legacy(family),CancellationToken.None)).Version==1&&unprobed.Calls.Count==calls+1,"Start probed v3 or selected unsupported v2record9 when16 advertised");
            var old=new Fake(family);old.Supported=family==1?5U:7U;var oldClient=old.Client();await oldClient.ReadFineAuxCapabilitiesAsync(CancellationToken.None);calls=old.Calls.Count;
            Need(!await oldClient.ReadFineCounter64CapabilitiesAsync(CancellationToken.None)&&old.Calls.Count==calls&&(await oldClient.StartAsync(Legacy(family),CancellationToken.None)).Version==2,"Old explicit v2 behavior changed");
        }
        var schedule=Continuous(2);schedule.FineCounter64Supported=29;var scheduledClient=schedule.Client();await scheduledClient.ReadFineAuxCapabilitiesAsync(CancellationToken.None);await scheduledClient.ReadFineCounter64CapabilitiesAsync(CancellationToken.None);int count=schedule.Calls.Count;
        var a=await scheduledClient.StartAsync(Legacy(2,57,true),CancellationToken.None);Need(a.Version==1&&schedule.Calls.Count==count+1&&NpFirstSyncSd.Unwrap(a.Request,2,Uid).SequenceEqual(Legacy(2,57,true)),"v3 unsupported scheduling did not choose v1 before mutation");
    }
    static async Task FineCounter64Failures(){
        for(int mode=0;mode<8;mode++){
            int failure=mode;var fake=Continuous(1);var client=fake.Client();await client.ReadFineAuxCapabilitiesAsync(CancellationToken.None);
            fake.Handler=(q,c)=>{if(BitConverter.ToUInt16(q,4)!=3)return Task.FromResult(fake.Reply(q));if(failure==0)throw new TimeoutException();if(failure==1)throw new IOException();if(failure==2)throw new NotSupportedException();if(failure==3)return Task.FromResult(fake.Reply(q,1));var r=fake.Reply(q);if(failure==4)r[12]^=1;if(failure==5)r[60]^=1;if(failure==6)Put(r,4,2);if(failure==7)Put(r,44,100);if(failure!=5)Seal(r);return Task.FromResult(r);};
            Need(!await client.ReadFineCounter64CapabilitiesAsync(CancellationToken.None)&&client.FineCounter64CapabilitiesChecked&&client.FineCounter64SupportedFeatures==0&&client.SupportedFeatures==5&&client.Attempt==null,"Failed v3 discovery removed v1 or created record");
            int calls=fake.Calls.Count;Need(!await client.ReadFineCounter64CapabilitiesAsync(CancellationToken.None)&&fake.Calls.Count==calls,"Failed v3 capability query replayed");
            Need((await client.StartAsync(Legacy(1),CancellationToken.None)).Version==1&&fake.Calls.Count==calls+1&&fake.Starts==1,"v3 failure fell back to unsupported v2record9 or queried from Start");
        }
        foreach(bool after in new[]{false,true})using(var cancel=new CancellationTokenSource()){
            var fake=Continuous(2);var client=fake.Client();await client.ReadFineAuxCapabilitiesAsync(CancellationToken.None);if(!after)cancel.Cancel();else fake.Handler=(q,c)=>{cancel.Cancel();return Task.FromResult(fake.Reply(q));};
            await Fails<OperationCanceledException>(async()=>{await client.ReadFineCounter64CapabilitiesAsync(cancel.Token);},"Canceled v3 capability discovery was accepted");
            Need(client.Attempt==null&&client.FineCounter64SupportedFeatures==0&&client.SupportedFeatures==7&&fake.Starts==0,"Canceled v3 created record/lost v1");fake.Handler=(q,c)=>Task.FromResult(fake.Reply(q));int calls=fake.Calls.Count;
            Need((await client.StartAsync(Legacy(2),CancellationToken.None)).Version==1&&fake.Calls.Count==calls+1,"Start retried canceled v3 or selected9");
        }
        foreach(bool uid in new[]{false,true}){
            var fake=Continuous(2);var client=fake.Client();await client.ReadFineAuxCapabilitiesAsync(CancellationToken.None);fake.Handler=(q,c)=>{if(uid)fake.CurrentUid=OtherUid;else fake.CurrentEpoch++;return Task.FromResult(fake.Reply(q));};
            await Fails<InvalidOperationException>(async()=>{await client.ReadFineCounter64CapabilitiesAsync(CancellationToken.None);},"Changed binding accepted v3 support");int calls=fake.Calls.Count;
            await Fails<InvalidOperationException>(async()=>{await client.StartAsync(Legacy(2),CancellationToken.None);},"Changed binding sent Start");Need(fake.Calls.Count==calls&&client.Attempt==null&&client.FineCounter64SupportedFeatures==0,"Changed UID/epoch leaked record or capability");
        }
        var pending=Continuous(2);var pendingClient=pending.Client();await pendingClient.ReadFineAuxCapabilitiesAsync(CancellationToken.None);var completion=new TaskCompletionSource<byte[]>();byte[] saved=null;pending.Handler=(q,c)=>{saved=(byte[])q.Clone();return completion.Task;};var discovery=pendingClient.ReadFineCounter64CapabilitiesAsync(CancellationToken.None);
        await Fails<InvalidOperationException>(async()=>{await pendingClient.StartAsync(Legacy(2),CancellationToken.None);},"Concurrent Start bypassed v3 owner");Need(pending.Calls.Count==3&&pendingClient.Attempt==null,"v3 query queued a mutation");completion.SetResult(pending.Reply(saved));Need(await discovery,"Completed v3 discovery lost");
        foreach(bool rejected in new[]{false,true}){
            var fake=Continuous(2);var client=fake.Client();await client.ReadFineAuxCapabilitiesAsync(CancellationToken.None);await client.ReadFineCounter64CapabilitiesAsync(CancellationToken.None);fake.Handler=(q,c)=>{if(rejected)return Task.FromResult(fake.Reply(q,3));throw new TimeoutException("Lost v3 Start ACK");};
            if(rejected){var a=await client.StartAsync(Legacy(2),CancellationToken.None);Need(a.Rejected&&!a.OutcomeUnknown,"Definite v3 rejection lost");}else await Fails<TimeoutException>(async()=>{await client.StartAsync(Legacy(2),CancellationToken.None);},"Lost v3 Start ACK accepted");
            var attempt=client.Attempt;Need(attempt.Version==3&&fake.Starts==1&&(rejected?!attempt.Acknowledgement.Associated:attempt.OutcomeUnknown),"v3 attempt outcome/context lost");int calls=fake.Calls.Count;
            await Fails<InvalidOperationException>(async()=>{await client.StartAsync(Legacy(2,58),CancellationToken.None);},"v3 unknown/rejected Start replayed");Need(!await client.ReadFineCounter64CapabilitiesAsync(CancellationToken.None)&&fake.Calls.Count==calls,"v3 attempted record rediscovered capabilities");
            fake.Handler=(q,c)=>Task.FromResult(fake.Reply(q,4));Need((await client.StatusAsync(CancellationToken.None)).Version==3&&ReferenceEquals(client.Attempt,attempt)&&fake.Starts==1,"v3 NOT_FOUND retired/replayed unknown Start");
        }
        foreach(bool after in new[]{false,true})using(var cancel=new CancellationTokenSource()){
            var fake=Continuous(1);var client=fake.Client();await client.ReadFineAuxCapabilitiesAsync(CancellationToken.None);await client.ReadFineCounter64CapabilitiesAsync(CancellationToken.None);int calls=fake.Calls.Count;
            if(after)fake.Handler=(q,c)=>{cancel.Cancel();return Task.FromResult(fake.Reply(q));};else cancel.Cancel();
            await Fails<OperationCanceledException>(async()=>{await client.StartAsync(Legacy(1),cancel.Token);},"v3 Start cancellation was ignored");
            if(after){Need(client.Attempt!=null&&client.Attempt.Version==3&&client.Attempt.OutcomeUnknown&&client.Attempt.StartTransportEntered&&fake.Starts==1,"Canceled delivered v3 Start lost unknown context");await Fails<InvalidOperationException>(async()=>{await client.StartAsync(Legacy(1,58),CancellationToken.None);},"Canceled delivered v3 Start replayed");Need(fake.Calls.Count==calls+1,"Canceled v3 Start sent fallback/query");}
            else{Need(client.Attempt==null&&fake.Starts==0&&fake.Calls.Count==calls,"Pre-canceled v3 Start sent mutation");Need((await client.StartAsync(Legacy(1),CancellationToken.None)).Version==3&&fake.Calls.Count==calls+1,"Pre-cancel removed valid v3 cache or added discovery");}
        }
    }
    static async Task Grid16Capabilities(){
        var v1=Continuous(1);var c1=v1.Client();await c1.ReadFineAuxCapabilitiesAsync(CancellationToken.None);await c1.ReadFineCounter64CapabilitiesAsync(CancellationToken.None);int before=v1.Calls.Count;
        Need(!await c1.ReadGrid16CapabilitiesAsync(CancellationToken.None)&&v1.Calls.Count==before&&!c1.Grid16CapabilitiesChecked,"V1 queried/cached unsupported v4");Need((await c1.StartAsync(Legacy(1),CancellationToken.None)).Version==3,"V1 v3 legacy behavior changed");
        var peer=Continuous(2);var client=peer.Client();Need(!await client.ReadGrid16CapabilitiesAsync(CancellationToken.None)&&peer.Calls.Count==0,"v4 probed before v3 confirmation");
        await client.ReadFineAuxCapabilitiesAsync(CancellationToken.None);await client.ReadFineCounter64CapabilitiesAsync(CancellationToken.None);Need(await client.ReadGrid16CapabilitiesAsync(CancellationToken.None)&&client.Grid16CapabilitiesChecked&&client.Grid16SupportedFeatures==31&&peer.Calls.Count==4,"Explicit v4 warmup missing");
        before=peer.Calls.Count;Need(await client.ReadGrid16CapabilitiesAsync(CancellationToken.None)&&peer.Calls.Count==before,"v4 CAPS repeated");var attempt=await client.StartAsync(Legacy(2),CancellationToken.None);Need(attempt.Version==4&&attempt.Acknowledgement.Grid16&&attempt.Acknowledgement.FineCounter64&&peer.Calls.Count==before+1&&peer.Starts==1,"Cached v4 Start added a query or wrong declaration");
        Need((await client.StatusAsync(CancellationToken.None)).Version==4&&BitConverter.ToUInt64(peer.Calls.Last(),24)==attempt.Token,"STATUS did not retain v4/token");before=peer.Calls.Count;Need(!await client.ReadGrid16CapabilitiesAsync(CancellationToken.None)&&peer.Calls.Count==before,"An attempted record was rediscovered");
        var old=Continuous(2);var c=old.Client();await c.ReadFineAuxCapabilitiesAsync(CancellationToken.None);await c.ReadFineCounter64CapabilitiesAsync(CancellationToken.None);before=old.Calls.Count;Need((await c.StartAsync(Legacy(2),CancellationToken.None)).Version==3&&old.Calls.Count==before+1,"Start secretly probes/selects unwarmed v4");
        foreach(uint supported in new[]{29U,31U}){
            var scheduled=Continuous(2);scheduled.Grid16Supported=supported;var sc=scheduled.Client();await sc.ReadFineAuxCapabilitiesAsync(CancellationToken.None);await sc.ReadFineCounter64CapabilitiesAsync(CancellationToken.None);await sc.ReadGrid16CapabilitiesAsync(CancellationToken.None);var a=await sc.StartAsync(Legacy(2,57,true),CancellationToken.None);
            Need(a.Version==(supported==31?4:3)&&BitConverter.ToUInt16(a.Request,6)==3,"Scheduled selection ignored actual advertised support");
        }
        for(int mode=0;mode<7;mode++){
            int fault=mode;var f=Continuous(2);var fc=f.Client();await fc.ReadFineAuxCapabilitiesAsync(CancellationToken.None);await fc.ReadFineCounter64CapabilitiesAsync(CancellationToken.None);
            f.Handler=(q,token)=>{if(fault==0)throw new TimeoutException();if(fault==1)throw new IOException();if(fault==2)throw new NotSupportedException();if(fault==3)return Task.FromResult(new byte[64]);if(fault==4){var b=f.Reply(q);b[12]^=1;Seal(b);return Task.FromResult(b);}if(fault==5)return Task.FromResult(f.Reply(q,1));var b3=f.Reply(q);b3[4]=3;Seal(b3);return Task.FromResult(b3);};
            Need(!await fc.ReadGrid16CapabilitiesAsync(CancellationToken.None)&&fc.Grid16CapabilitiesChecked&&fc.Grid16SupportedFeatures==0&&fc.FineCounter64SupportedFeatures==31,"Optional v4 failure removed valid v3");int calls=f.Calls.Count;Need(!await fc.ReadGrid16CapabilitiesAsync(CancellationToken.None)&&f.Calls.Count==calls,"Optional failed v4 was retried");f.Handler=(q,token)=>Task.FromResult(f.Reply(q));Need((await fc.StartAsync(Legacy(2),CancellationToken.None)).Version==3&&f.Calls.Count==calls+1,"v4 failure gated or probed Start");
        }
        foreach(bool after in new[]{false,true})using(var cancel=new CancellationTokenSource()){
            var f=Continuous(2);var fc=f.Client();await fc.ReadFineAuxCapabilitiesAsync(CancellationToken.None);await fc.ReadFineCounter64CapabilitiesAsync(CancellationToken.None);if(after)f.Handler=(q,ct)=>{cancel.Cancel();return Task.FromResult(f.Reply(q));};else cancel.Cancel();
            await Fails<OperationCanceledException>(async()=>{await fc.ReadGrid16CapabilitiesAsync(cancel.Token);},"Canceled v4 discovery accepted");Need(fc.Grid16SupportedFeatures==0&&fc.FineCounter64SupportedFeatures==31&&fc.Attempt==null,"Canceled v4 polluted cached proof");int calls=f.Calls.Count;f.Handler=(q,ct)=>Task.FromResult(f.Reply(q));Need((await fc.StartAsync(Legacy(2),CancellationToken.None)).Version==3&&f.Calls.Count==calls+1,"Start retried canceled v4");
        }
        foreach(bool uid in new[]{false,true}){
            var f=Continuous(2);var fc=f.Client();await fc.ReadFineAuxCapabilitiesAsync(CancellationToken.None);await fc.ReadFineCounter64CapabilitiesAsync(CancellationToken.None);f.Handler=(q,ct)=>{var raw=f.Reply(q);if(uid)f.CurrentUid=OtherUid;else f.CurrentEpoch++;return Task.FromResult(raw);};
            await Fails<InvalidOperationException>(async()=>{await fc.ReadGrid16CapabilitiesAsync(CancellationToken.None);},"V4 accepted changed UID/epoch");int calls=f.Calls.Count;await Fails<InvalidOperationException>(async()=>{await fc.StartAsync(Legacy(2),CancellationToken.None);},"Changed binding started v4");Need(fc.Attempt==null&&fc.Grid16SupportedFeatures==0&&f.Calls.Count==calls,"Changed binding leaked support/delivery");
        }
        foreach(bool rejected in new[]{false,true}){
            var f=Continuous(2);var fc=f.Client();await fc.ReadFineAuxCapabilitiesAsync(CancellationToken.None);await fc.ReadFineCounter64CapabilitiesAsync(CancellationToken.None);await fc.ReadGrid16CapabilitiesAsync(CancellationToken.None);f.Handler=(q,ct)=>{if(rejected)return Task.FromResult(f.Reply(q,3));throw new TimeoutException("lost v4 ACK");};
            if(rejected)Need((await fc.StartAsync(Legacy(2),CancellationToken.None)).Rejected,"V4 rejection lost");else await Fails<TimeoutException>(async()=>{await fc.StartAsync(Legacy(2),CancellationToken.None);},"Lost v4 ACK accepted");var a=fc.Attempt;int calls=f.Calls.Count;Need(a.Version==4&&f.Starts==1,"V4 attempt context lost");await Fails<InvalidOperationException>(async()=>{await fc.StartAsync(Legacy(2,58),CancellationToken.None);},"V4 Start replay/downgrade");Need(f.Calls.Count==calls,"Unknown v4 sent fallback");f.Handler=(q,ct)=>Task.FromResult(f.Reply(q,4));Need((await fc.StatusAsync(CancellationToken.None)).Version==4&&ReferenceEquals(a,fc.Attempt),"V4 NOT_FOUND retired/replayed attempt");
        }
        var pending=Continuous(2);var pc=pending.Client();await pc.ReadFineAuxCapabilitiesAsync(CancellationToken.None);await pc.ReadFineCounter64CapabilitiesAsync(CancellationToken.None);var complete=new TaskCompletionSource<byte[]>();byte[] saved=null;pending.Handler=(q,ct)=>{saved=(byte[])q.Clone();return complete.Task;};var warm=pc.ReadGrid16CapabilitiesAsync(CancellationToken.None);await Fails<InvalidOperationException>(async()=>{await pc.StartAsync(Legacy(2),CancellationToken.None);},"Concurrent Start bypassed v4 warmup owner");Need(pc.Attempt==null&&pending.Starts==0,"V4 CAPS created a mutation");complete.SetResult(pending.Reply(saved));Need(await warm,"Pending v4 capability lost");
    }
    static async Task Run(){await OldFirmware();await FineCapabilities();await QueuedAndStatus(1);await QueuedAndStatus(2);await LostAndMalformedAck();await CancellationAndBinding();await ScheduleAndIsolation();await InFlight();await ConcurrentStart();await FineAuxCapabilities();await FineAuxFailures();await FineAuxInFlight();await FineCounter64Capabilities();await FineCounter64Failures();await Grid16Capabilities();}
    static int Main(){try{Run().GetAwaiter().GetResult();Console.WriteLine("PASS "+checks+" isolated SD client checks; fake B8 transport only.");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
