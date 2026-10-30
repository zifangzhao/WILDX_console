using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CE32_console;
using USB512_interface;

static class NpSdUsbTestControllerTests {
    const string Uid1="001D00065942500B20313354",Uid2="001A0004434250062039314E";
    const uint Session=0x12345,Frontier=65536,Capacity=1000000000;
    const ulong Token=0x1020304050607080;
    static int checks;
    static void Need(bool b,string why){checks++;if(!b)throw new Exception(why);}
    static void Reject(Action a,string why){bool rejected=false;try{a();}catch(IOException){rejected=true;}catch(InvalidDataException){rejected=true;}Need(rejected,why);}
    static string Uid(int family){return family==1?Uid1:Uid2;}
    static void Put(byte[] b,int at,uint n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,at,4);}
    static void Put64(byte[] b,int at,ulong n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,at,8);}
    static void Seal(byte[] b){Put(b,b.Length-4,NpFirstSyncWire.Crc(b,b.Length-4));}
    static byte[] Idle(int family){
        var b=Enumerable.Repeat((byte)255,256).ToArray();Put(b,0,NpSdUsbTestWire.ReportMagic);Put(b,4,1);Put(b,8,256);Put(b,12,NpSdUsbTestWire.Capability);
        Put(b,16,0);Put(b,20,0);Put(b,24,47);Put(b,28,0);Put64(b,32,0);Put(b,48,(uint)family);
        for(int n=0;n<3;n++)Put(b,68+n*4,Convert.ToUInt32(Uid(family).Substring(n*8,8),16));Put(b,80,Capacity);
        for(int n=0;n<4;n++)Put(b,84+n*4,0x12345678U+(uint)n);Put(b,100,Frontier);Put(b,104,0);Put(b,108,0xfffffff0);Put(b,112,120);Put(b,120,family==1?3U:7U);Put(b,124,20000);Seal(b);return b;
    }
    static NpSdUsbTestReport Parse(byte[] b,int family){return NpSdUsbTestWire.ParseReport(b,family,Uid(family));}
    static byte[] Inner(int family,ulong token=Token,uint id=57){
        var legacy=family==1?NpSdControl.Request(2,id,3,20000,Frontier,Frontier+1000000,Session,1):NpV2SdControl.Request(2,id,20000,128,0,7);
        return family==1?NpFirstSyncSd.WrapLegacyFineCounter64(legacy,family,Uid(family),token):NpFirstSyncSd.WrapLegacyGrid16Append(legacy,family,Uid(family),token);
    }
    static NpSdUsbTestReport Terminal(byte[] plan,int family){
        var b=Idle(family);Put(b,16,4);Put(b,20,2);Put(b,24,47|16|1024|16384);Put64(b,32,BitConverter.ToUInt64(plan,8));Put(b,40,BitConverter.ToUInt32(plan,60));Put(b,44,BitConverter.ToUInt32(plan,72));
        Put(b,52,BitConverter.ToUInt32(plan,96));Put(b,56,BitConverter.ToUInt32(plan,100));Put(b,60,BitConverter.ToUInt32(plan,52));Put(b,64,128);Put(b,116,Frontier);
        Put(b,128,Session);Put(b,132,Frontier);Put(b,136,Frontier+1000000);Put(b,140,Frontier+8192);Put(b,144,Frontier+20000);Put(b,148,128);Put(b,156,128);Put(b,160,128);Seal(b);return Parse(b,family);
    }
    static void Admission(){foreach(int family in new[]{1,2}){
        var idle=Parse(Idle(family),family);NpSdUsbTestController.RequireReady(idle,true);checks++;
        if(family==1){Need(NpSdUsbTestController.V1EndLba(idle,3,20000,10)==737280,"V1 USB test did not reuse actual20k/8MiB allocation geometry");var small=Idle(family);Put(small,80,Frontier+32768);Seal(small);Reject(()=>NpSdUsbTestController.V1EndLba(Parse(small,family),3,20000,10),"V1 test exceeded fresh card capacity");}
        Reject(()=>NpSdUsbTestController.RequireReady(idle,false),"Automatic fallback accepted virgin history");
        var plan=NpSdUsbTestController.PlanForProfile(idle,Inner(family),30,true,null);Need(BitConverter.ToUInt32(plan,56)==1,"First explicit profile lost reconcile flag");
        var unknown=Idle(family);Put(unknown,16,6);Seal(unknown);var u=Parse(unknown,family);NpSdUsbTestController.RequireReady(u,true);checks++;
        Reject(()=>NpSdUsbTestController.PlanForProfile(u,Inner(family),30,false,plan),"Unknown-delivery history authorized automatic fallback");
        foreach(uint phase in new uint[]{1,2,3}){var active=Idle(family);Put(active,16,phase);Seal(active);var r=Parse(active,family);Reject(()=>NpSdUsbTestController.PlanForProfile(r,Inner(family),30,true,null),"Explicit reconciliation stole active owner");}
        foreach(uint bit in new uint[]{1,2,4,8,32}){var bad=Idle(family);Put(bad,24,47U&~bit);Seal(bad);var r=Parse(bad,family);Reject(()=>NpSdUsbTestController.RequireReady(r,true),"Missing fresh ready proof admitted");}
        var dirty=Idle(family);Put(dirty,104,1);Seal(dirty);Reject(()=>NpSdUsbTestController.RequireReady(Parse(dirty,family),true),"Nonwritable catalog admitted");
        foreach(uint phase in new uint[]{0,6}){
            var missing=Idle(family);Put(missing,16,phase);Put(missing,120,UInt32.MaxValue);Put(missing,124,UInt32.MaxValue);Seal(missing);var initial=Parse(missing,family);
            NpSdUsbTestController.RequireReady(initial,true);checks++;
            Need(initial.PriorMask==null&&initial.PriorHz==null,"Initial admission fabricated APP prior profile");
            var first=NpSdUsbTestController.PlanForProfile(initial,Inner(family),30,true,null);Need(BitConverter.ToUInt32(first,56)==1,"Unknown initial APP profile blocked explicit plan");
            Reject(()=>NpSdUsbTestController.RequireReady(initial,false),"Unknown initial APP profile allowed automatic reconciliation");
            foreach(uint bit in new uint[]{1,2,4,8,32}){var bad=(byte[])missing.Clone();Put(bad,24,47U&~bit);Seal(bad);Reject(()=>NpSdUsbTestController.RequireReady(Parse(bad,family),true),"Unknown profile bypassed fresh readiness bit "+bit);}
            var badCatalog=(byte[])missing.Clone();Put(badCatalog,104,1);Seal(badCatalog);Reject(()=>NpSdUsbTestController.RequireReady(Parse(badCatalog,family),true),"Unknown profile bypassed writable catalog");
        }
        var terminal=Terminal(plan,family);Need(NpSdUsbTestController.SafeTerminal(terminal),"Exact closed/restored terminal rejected");
        var next=NpSdUsbTestController.PlanForProfile(terminal,Inner(family,Token+1,58),30,false,plan);Need(BitConverter.ToUInt32(next,56)==0,"Automatic next rate acquired explicit reconciliation");
        Reject(()=>NpSdUsbTestController.PlanForProfile(terminal,Inner(family,Token+1,58),30,false,null),"Fallback without previous plan admitted");
        var foreign=terminal.Raw;Put64(foreign,32,Token+2);Seal(foreign);Reject(()=>NpSdUsbTestController.PlanForProfile(Parse(foreign,family),Inner(family,Token+1,58),30,false,plan),"Fallback accepted another ticket");
        foreach(uint bit in new uint[]{16,1024}){var bad=terminal.Raw;Put(bad,24,BitConverter.ToUInt32(bad,24)&~bit);if(bit==1024){Put(bad,120,UInt32.MaxValue);Put(bad,124,UInt32.MaxValue);}Seal(bad);var unconfirmed=Parse(bad,family);Need(!NpSdUsbTestController.SafeTerminal(unconfirmed),"Terminal readiness invented missing proof");
            Reject(()=>NpSdUsbTestController.PlanForProfile(unconfirmed,Inner(family,Token+1,58),30,false,plan),"Follow-on bypassed terminal restoration proof");
            Reject(()=>NpSdUsbTestController.RequireReady(unconfirmed,true),"Manual reconciliation bypassed actual terminal restoration proof");}
    }}
    static void Delivery(){
        var terminal=Parse(Idle(2),2);
        foreach(bool entered in new[]{false,true})foreach(int failure in new[]{0,1,2,3}){
            var p=new NpSdUsbProfileResult();int outs=0,reads=0;Exception expected=failure==1?(Exception)new IOException("lost native receipt"):failure==2?new Win32Exception(31):new OperationCanceledException("cancelled");
            Func<Task> submit=()=>{outs++;p.SubmitEntered=entered;if(failure!=0)throw expected;return Task.CompletedTask;};
            bool failed=false;try{var observed=NpSdUsbTestController.SubmitOnce(p,submit,()=>{reads++;return Task.FromResult(terminal);}).GetAwaiter().GetResult();Need(ReferenceEquals(observed,terminal),"Observed receipt replaced");}catch(Exception e){failed=true;Need(ReferenceEquals(e,expected),"Pre-delivery failure identity changed");}
            Need(outs==1,"Start submission was replayed");Need(reads==((failure!=0&&!entered)?0:1),"Wrong post-delivery observation count");
            Need(failed==(failure!=0&&!entered),"Possible-delivery failure escaped without observation");Need(p.SubmitAcknowledged==(failure==0),"Native acceptance fact fabricated");
            if(entered&&failure!=0)Need(p.Error!=null,"Unknown delivery evidence discarded");
        }
        var unknown=new NpSdUsbProfileResult();int calls=0;bool timedOut=false;
        try{NpSdUsbTestController.SubmitOnce(unknown,()=>{calls++;unknown.SubmitEntered=true;return Task.CompletedTask;},()=>{throw new IOException("terminal still unknown");}).GetAwaiter().GetResult();}catch(IOException){timedOut=true;}
        Need(timedOut&&calls==1&&unknown.SubmitEntered,"Unknown terminal triggered retry or lost attempt context");
        var native=new NpSdUsbProfileResult();int saves=0,observedCount=0;bool lateFailed=false;
        try{NpSdUsbTestController.SubmitOnce(native,()=>{native.SubmitEntered=true;throw new Win32Exception(31,"Actual A4 STALL");},()=>{observedCount++;Need(saves==1&&native.SubmissionNativeErrorCode==31&&native.SubmissionError.Contains("STALL"),"Initial submission failure not saved before observe");throw new IOException("later terminal timeout");},()=>{saves++;}).GetAwaiter().GetResult();}catch(IOException){lateFailed=true;native.Error="later terminal timeout";}
        Need(lateFailed&&observedCount==1&&native.SubmissionNativeErrorCode==31&&native.SubmissionError.Contains("STALL"),"Later wait overwrote actual native submission error");
        var evidenceFailure=new NpSdUsbProfileResult();int stillObserved=0;
        NpSdUsbTestController.SubmitOnce(evidenceFailure,()=>{evidenceFailure.SubmitEntered=true;throw new IOException("lost OUT receipt");},()=>{stillObserved++;return Task.FromResult(terminal);},()=>{throw new IOException("PC report disk unavailable");}).GetAwaiter().GetResult();Need(stillObserved==1,"PC evidence failure skipped finite-owner observation");
    }
    static NpUsbCatalogRepair.Receipt Inspection(NpSdUsbTestReport report){return new NpUsbCatalogRepair.Receipt {Generation=report.Family,Operation=0xa1,Phase=3,Error=0,Nonce=17,InspectedNonce=17,Capacity=report.Capacity.Value,NextLba=report.Frontier.Value,Flags=report.CatalogFlags.Value,Cid=String.Concat(report.Cid.Select(n=>n.ToString("X8")))};}
    static byte[] DownloadStatus(uint epoch,uint state=0,uint sequence=55){var b=new byte[32];Put(b,0,0x3144504e);Put(b,4,1);Put(b,8,Capacity);Put(b,12,1020);Put(b,16,state);Put(b,20,sequence);Put(b,24,epoch);Put(b,28,1);return b;}
    static void ReadRetirement(){
        bool rangeActive=false,prefetchValid=true;int cancels=0,polls=0;uint epoch=7;
        var retired=NpSdUsbTestController.RetireCatalogRead(()=>{if(cancels==0)return DownloadStatus(epoch);polls++;if(polls==2)prefetchValid=false;return DownloadStatus(epoch,prefetchValid?4U:0U);},()=>{Need(!rangeActive&&prefetchValid,"Random-read inactive-range cache reproduction missing");cancels++;epoch++;},Capacity,3);
        Need(cancels==1&&polls==2&&!prefetchValid&&BitConverter.ToUInt32(retired,24)==8&&BitConverter.ToUInt32(retired,16)==0,"Random-read cache was not retired once to next-epoch idle");
        foreach(int at in new[]{0,4,8,12,28}){int sent=0;var bad=DownloadStatus(7);Put(bad,at,at==12?1021U:0);Reject(()=>NpSdUsbTestController.RetireCatalogRead(()=>bad,()=>sent++,Capacity,1),"Invalid initial NPD1 permitted cancel");Need(sent==0,"Initial malformed ownership caused cancel");}
        int busyCancel=0;Reject(()=>NpSdUsbTestController.RetireCatalogRead(()=>DownloadStatus(7,3),()=>busyCancel++,Capacity,1),"Busy unrelated read owner cancelled");Need(busyCancel==0,"Non-idle read received cancel");
        foreach(int fault in Enumerable.Range(0,5)){int sent=0;var after=DownloadStatus(8);if(fault==0)Put(after,24,7);if(fault==1)Put(after,20,56);if(fault==2)Put(after,12,1000);if(fault==3)Put(after,16,1);if(fault==4)Put(after,8,Capacity-1);
            Reject(()=>NpSdUsbTestController.RetireCatalogRead(()=>sent==0?DownloadStatus(7):after,()=>sent++,Capacity,1),"Unmatched retirement allowed inspection: "+fault);Need(sent==1,"Retirement cancel replayed after mismatch");}
        int timeoutCancel=0,inspects=0,outs=0;var idle=Parse(Idle(2),2);
        Reject(()=>{var r=NpSdUsbTestController.RefreshAfterCatalog(()=>Task.CompletedTask,()=>{NpSdUsbTestController.RetireCatalogRead(()=>DownloadStatus(timeoutCancel==0?7U:8U,timeoutCancel==0?0U:4U),()=>timeoutCancel++,Capacity,2);return Task.CompletedTask;},()=>{inspects++;return Task.FromResult(idle);},idle,true).GetAwaiter().GetResult();NpSdUsbTestController.PlanForProfile(r,Inner(2),30,true,null);outs++;},"Retirement timeout reached INSPECT/A4");
        Need(timeoutCancel==1&&inspects==0&&outs==0,"Timeout replayed cancel or permitted a new owner");
        int wrapped=0;var wrap=NpSdUsbTestController.RetireCatalogRead(()=>DownloadStatus(wrapped==0?UInt32.MaxValue:0),()=>wrapped++,Capacity,1);Need(wrapped==1&&BitConverter.ToUInt32(wrap,24)==0,"Exact uint32 retirement epoch wrap mishandled");
    }
    static void FreshSequence(){foreach(int family in new[]{1,2}){
        var idle=Parse(Idle(family),family);var events=new List<string>();bool closed=false;
        var fresh=NpSdUsbTestController.RefreshAfterCatalog(()=>{events.Add("catalog-read");closed=true;events.Add("catalog-close");return Task.CompletedTask;},()=>{events.Add("cancel62");events.Add("idle-next-epoch");return Task.CompletedTask;},()=>{Need(closed,"INSPECT ran before bulk catalog stream closed");events.Add("INSPECT");events.Add("fresh-A3");return Task.FromResult(idle);},idle,true).GetAwaiter().GetResult();
        events.Add("plan");var plan=NpSdUsbTestController.PlanForProfile(fresh,Inner(family),30,true,null);var profile=new NpSdUsbProfileResult();
        NpSdUsbTestController.SubmitOnce(profile,()=>{events.Add("OUT");profile.SubmitEntered=true;return Task.CompletedTask;},()=>Task.FromResult(Terminal(plan,family))).GetAwaiter().GetResult();
        Need(String.Join(",",events)=="catalog-read,catalog-close,cancel62,idle-next-epoch,INSPECT,fresh-A3,plan,OUT","Actual submission preparation sequence changed");
        foreach(uint bit in new uint[]{1,2,4,8,32}){var b=Idle(family);Put(b,24,47&~bit);Seal(b);var bad=Parse(b,family);Reject(()=>NpSdUsbTestController.RefreshAfterCatalog(()=>Task.CompletedTask,()=>Task.CompletedTask,()=>Task.FromResult(bad),idle,true).GetAwaiter().GetResult(),"Missing post-inspection readiness admitted plan");}
        foreach(int at in new[]{84,100}){var b=Idle(family);Put(b,at,BitConverter.ToUInt32(b,at)+1);Seal(b);var changed=Parse(b,family);Reject(()=>NpSdUsbTestController.RefreshAfterCatalog(()=>Task.CompletedTask,()=>Task.CompletedTask,()=>Task.FromResult(changed),idle,true).GetAwaiter().GetResult(),"Changed card/frontier after catalog admitted");}
        Need(NpSdUsbTestController.InspectionMatches(Inspection(idle),idle),"Matching read-only INSPECT/A3 rejected");
        foreach(string field in new[]{"Generation","Operation","Phase","Error","Nonce","InspectedNonce","Capacity","NextLba","Flags","Cid"}){var mismatch=Inspection(idle);var f=typeof(NpUsbCatalogRepair.Receipt).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic);f.SetValue(mismatch,field=="Cid"?(object)new string('0',32):field=="Generation"?(object)0:(uint)0);if(field=="Error"||field=="Flags")f.SetValue(mismatch,1U);Need(!NpSdUsbTestController.InspectionMatches(mismatch,idle),"Stale/malformed inspection accepted: "+field);}
    }
        Need(NpSdUsbTestController.CatalogInspectSupported(1,0x20)&&!NpSdUsbTestController.CatalogInspectSupported(1,0x10),"V1 DFU bit confused with INSPECT capability");
        Need(NpSdUsbTestController.CatalogInspectSupported(2,0x10)&&!NpSdUsbTestController.CatalogInspectSupported(2,0x20),"V2 format bit confused with INSPECT capability");
        Need(!NpSdUsbTestController.CatalogInspectSupported(1,0x10000)&&!NpSdUsbTestController.CatalogInspectSupported(2,0x10000),"Test capability alone triggered unsupported INSPECT");
    }
    static void TerminalFreshness(){foreach(int family in new[]{1,2}){
        var before=Parse(Idle(family),family);var plan=NpSdUsbTestController.PlanForProfile(before,Inner(family),30,true,null);
        var freshRaw=Terminal(plan,family).Raw;Put(freshRaw,100,Frontier+1000000);Seal(freshRaw);var fresh=Parse(freshRaw,family);
        var firstRaw=(byte[])freshRaw.Clone();Put(firstRaw,24,BitConverter.ToUInt32(firstRaw,24)&~47U);for(int at=80;at<=104;at+=4)Put(firstRaw,at,UInt32.MaxValue);Seal(firstRaw);var first=Parse(firstRaw,family);var original=(byte[])firstRaw.Clone();int inspections=0,reads=0;
        Need(!NpSdUsbTestController.SafeTerminal(first)&&first.Capacity==null,"Postboot missing-facts fixture did not reproduce first cached terminal");
        var result=NpSdUsbTestController.RefreshTerminal(plan,first,before,()=>{inspections++;return Task.FromResult(first);},()=>{reads++;return Task.FromResult(fresh);},2).GetAwaiter().GetResult();
        Need(inspections==1&&reads==1&&ReferenceEquals(result,fresh)&&NpSdUsbTestController.SafeTerminal(result),"Missing terminal facts did not refresh once before decision");
        Need(first.Raw.SequenceEqual(original),"Original terminal raw report was changed");
        int unnecessary=0;var ready=NpSdUsbTestController.RefreshTerminal(plan,fresh,before,()=>{unnecessary++;return Task.FromResult(fresh);},()=>{unnecessary++;return Task.FromResult(fresh);},1).GetAwaiter().GetResult();Need(ReferenceEquals(ready,fresh)&&unnecessary==0,"Already-fresh terminal got extra maintenance");
        foreach(int at in new[]{32,84,136,180,24}){var changed=(byte[])freshRaw.Clone();if(at==24)Put(changed,at,BitConverter.ToUInt32(changed,at)|256U);else Put(changed,at,BitConverter.ToUInt32(changed,at)+1);if(at==24){Put(changed,216,1);Put64(changed,220,1000);Put(changed,228,1000);Put(changed,232,1000);Put(changed,236,108000000);Put(changed,240,128000000);}Seal(changed);var mismatch=Parse(changed,family);
            Reject(()=>NpSdUsbTestController.RefreshTerminal(plan,first,before,()=>Task.FromResult(mismatch),()=>Task.FromResult(mismatch),1).GetAwaiter().GetResult(),"Foreign card/ticket/changed immutable terminal accepted: "+at);}
        int unavailable=0,outs=0;Reject(()=>{NpSdUsbTestController.RefreshTerminal(plan,first,before,()=>{unavailable++;return Task.FromResult(first);},()=>Task.FromResult(first),1).GetAwaiter().GetResult();outs++;},"Unavailable freshness permitted a lower-rate Start");Need(unavailable==1&&outs==0,"Unavailable terminal replayed Start or inspection");
        var interruptedRaw=(byte[])firstRaw.Clone();Put(interruptedRaw,16,6);Seal(interruptedRaw);var interrupted=Parse(interruptedRaw,family);int queried=0;
        var unresolved=NpSdUsbTestController.RefreshTerminal(plan,interrupted,before,()=>{queried++;return Task.FromResult(fresh);},()=>Task.FromResult(fresh),1).GetAwaiter().GetResult();Need(ReferenceEquals(unresolved,interrupted)&&queried==0&&!NpSdUsbTestController.SafeTerminal(unresolved),"InterruptedUnknown promoted by physical refresh");
    }}
    static void ReadbackTransition(){foreach(int family in new[]{1,2}){
        var before=Parse(Idle(family),family);var oldPlan=NpSdUsbTestController.PlanForProfile(before,Inner(family),30,true,null);var terminal=Terminal(oldPlan,family);
        var profile=new NpSdUsbProfileResult {SampleHz=30000,Status="Stored readback checked",ReadbackCompleted=true,SafeToContinue=true,Evidence=NpSdSweepEvidence.VerifiedThroughputLoss,Decision=NpSdSweepDecision.TryLower};
        var events=new List<string>();events.Add("readback-Dispose");int cancels=0;
        NpSdUsbTestController.RetireOwnedReadback(profile,true,()=>{NpSdUsbTestController.RetireCatalogRead(()=>DownloadStatus(cancels==0?7U:8U),()=>{cancels++;events.Add("readback-62");},Capacity,1);events.Add("readback-idle");});
        Need(cancels==1&&profile.ReadbackReadRetired&&profile.SafeToContinue,"Readback stream not explicitly retired exactly once");
        events.Add("next-INSPECT");int baselineCancel=0;
        var fresh=NpSdUsbTestController.RefreshAfterCatalog(()=>{events.Add("baseline-read");events.Add("baseline-Dispose");return Task.CompletedTask;},()=>{NpSdUsbTestController.RetireCatalogRead(()=>DownloadStatus(baselineCancel==0?8U:9U),()=>{baselineCancel++;events.Add("baseline-62");},Capacity,1);events.Add("baseline-idle");return Task.CompletedTask;},()=>{events.Add("final-INSPECT");return Task.FromResult(terminal);},terminal,false).GetAwaiter().GetResult();
        events.Add("plan");var newPlan=NpSdUsbTestController.PlanForProfile(fresh,Inner(family,Token+1,58),30,false,oldPlan);
        NpSdUsbTestController.SubmitOnce(new NpSdUsbProfileResult(),()=>{events.Add("A4");return Task.CompletedTask;},()=>Task.FromResult(Terminal(newPlan,family))).GetAwaiter().GetResult();
        Need(String.Join(",",events)=="readback-Dispose,readback-62,readback-idle,next-INSPECT,baseline-read,baseline-Dispose,baseline-62,baseline-idle,final-INSPECT,plan,A4","Real shared next-rate transition ordering changed");
        var sweep=new NpSdUsbSweepResult();sweep.Profiles.Add(profile);NpSdUsbTestController.RecordRunFailure(sweep,null,new IOException("INSPECT ACK lost nonce2"));
        Need(profile.Evidence==NpSdSweepEvidence.VerifiedThroughputLoss&&profile.Decision==NpSdSweepDecision.TryLower&&profile.Status=="Stored readback checked"&&profile.Error==null,"Next preparation clobbered completed verified profile");
        Need(sweep.Error.StartsWith("Next-profile preparation failed:")&&sweep.Error.Contains("nonce2"),"Next-profile error scope lost");
        var next=new NpSdUsbProfileResult {SampleHz=20000};sweep.Profiles.Add(next);NpSdUsbTestController.RecordRunFailure(sweep,next,new IOException("new profile preflight"));Need(next.Decision==NpSdSweepDecision.Inconclusive&&profile.Decision==NpSdSweepDecision.TryLower,"Active failure rewrote earlier profile");
        var timeout=new NpSdUsbProfileResult {SafeToContinue=true,ReadbackCompleted=true,Evidence=NpSdSweepEvidence.VerifiedThroughputLoss};int sent=0,nextInspects=0,outs=0;
        NpSdUsbTestController.RetireOwnedReadback(timeout,true,()=>NpSdUsbTestController.RetireCatalogRead(()=>DownloadStatus(sent==0?1U:2U,sent==0?0U:4U),()=>sent++,Capacity,2));
        timeout.Decision=NpSdSweepPolicy.Decide(timeout.Evidence,timeout.SafeToContinue);if(timeout.Decision==NpSdSweepDecision.TryLower){nextInspects++;outs++;}
        Need(sent==1&&nextInspects==0&&outs==0&&!timeout.ReadbackReadRetired&&timeout.ReadbackRetirementError!=null&&timeout.Evidence==NpSdSweepEvidence.VerifiedThroughputLoss,"Retirement timeout permitted next owner or erased readback evidence");
    }
        foreach(var evidence in new[]{NpSdSweepEvidence.Cancelled,NpSdSweepEvidence.VerifiedIntegrityFailure}){int retired=0;var p=new NpSdUsbProfileResult {Evidence=evidence};NpSdUsbTestController.RetireOwnedReadback(p,true,()=>retired++);Need(retired==1&&p.ReadbackReadRetired&&p.Evidence==evidence,"Failed/cancelled verification skipped retirement or changed its evidence");}
        int unowned=0;NpSdUsbTestController.RetireOwnedReadback(new NpSdUsbProfileResult(),false,()=>unowned++);Need(unowned==0,"Unopened readback sent unowned cancel");
    }
    static void CapabilityReenumeration(){foreach(int family in new[]{1,2}){
        var idle=Parse(Idle(family),family);var plan=NpSdUsbTestController.PlanForProfile(idle,Inner(family),30,true,null);var terminal=Terminal(plan,family);
        var foreignRaw=terminal.Raw;Put64(foreignRaw,32,Token+7);Seal(foreignRaw);var foreign=Parse(foreignRaw,family);
        int outs=0,polls=0,transients=0;var profile=new NpSdUsbProfileResult();
        var result=NpSdUsbTestController.SubmitOnce(profile,()=>{outs++;profile.SubmitEntered=true;return Task.CompletedTask;},async()=>{
            for(int i=0;i<5;i++){
                var report=await NpSdUsbTestController.ObserveAcceptedTicket(plan,()=>{int step=polls++;if(step==0)throw new NpSdUsbTestController.CapabilityUnavailableException();if(step==1)throw new Win32Exception(31);if(step==2)throw new IOException("USB boot window");return Task.FromResult(step==3?foreign:terminal);},ex=>transients++);
                if(report!=null)return report;
            }
            throw new IOException("bounded test observation exhausted");
        }).GetAwaiter().GetResult();
        Need(outs==1&&polls==5&&transients==3&&profile.SubmitAcknowledged&&ReferenceEquals(result,terminal),"Re-enumeration capability window aborted or replayed accepted test");
        int unavailable=0;for(int i=0;i<2;i++){var r=NpSdUsbTestController.ObserveAcceptedTicket(plan,()=>{throw new NpSdUsbTestController.CapabilityUnavailableException();},ex=>unavailable++).GetAwaiter().GetResult();Need(r==null,"Absent capability invented terminal evidence");}Need(unavailable==2&&outs==1,"Unavailable capability created new Start");
        bool strict=false;try{NpSdUsbTestController.ObserveAcceptedTicket(plan,()=>{throw new NotSupportedException("unrelated unsupported protocol");},ex=>transients++).GetAwaiter().GetResult();}catch(NotSupportedException){strict=true;}Need(strict&&transients==3,"Unrelated unsupported condition was swallowed");
        Reject(()=>NpSdUsbTestController.ObserveAcceptedTicket(plan,()=>{throw new InvalidDataException("wrong UID or corrupt result");},ex=>transients++).GetAwaiter().GetResult(),"Invalid identity/report was ignored as capability warmup");
        Need(transients==3,"Malformed identity increased transient capability count");
    }
        Need(new NpSdUsbTestController.CapabilityUnavailableException() is NotSupportedException,"Initial unsupported admission message/type changed");
    }
    static NpDownloadRecord Record(int family){return new NpDownloadRecord {Start=Frontier,End=Frontier+20000,Session=Session,Chunks=128,ApHz=20000,LfpHz=family==1?1666U:0U,ProbeMask=family==1?3U:7U,FormatVersion=family==1?6U:16U,State=3};}
    static void Catalog(){
        var r=Record(2);var prior=new[]{NpSdUsbTestController.CatalogIdentity(r)};NpSdUsbTestController.RequirePreviousCatalog(prior,new[]{r,Record(2)});checks++;
        foreach(var f in typeof(NpDownloadRecord).GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)){
            if(f.Name=="RecoveryNotice")continue;
            var changed=Record(2);object value=f.GetValue(changed);f.SetValue(changed,f.FieldType==typeof(uint)?(object)(Convert.ToUInt32(value)+1):f.FieldType==typeof(long)?(object)(Convert.ToInt64(value)+1):f.FieldType==typeof(ulong)?(object)(Convert.ToUInt64(value)+1):f.FieldType==typeof(bool)?(object)!Convert.ToBoolean(value):"changed");
            Reject(()=>NpSdUsbTestController.RequirePreviousCatalog(prior,new[]{changed,Record(2)}),"Previous catalog field changed: "+f.Name);
        }
        Reject(()=>NpSdUsbTestController.RequirePreviousCatalog(prior,new[]{r}),"Missing new record accepted");Reject(()=>NpSdUsbTestController.RequirePreviousCatalog(prior,new[]{r,r,r}),"Two new records accepted");
        var oldV1=Record(1);oldV1.RecoveryNotice="filesystem2 checkpoint generation746, allocation frontier89178112";
        var newV1=Record(1);newV1.RecoveryNotice="filesystem2 checkpoint generation747, allocation frontier89833472";
        var oldFingerprint=new[]{NpSdUsbTestController.CatalogIdentity(oldV1)};
        NpSdUsbTestController.RequirePreviousCatalog(oldFingerprint,new[]{newV1,Record(1)});checks++;
        Need(oldV1.RecoveryNotice!=newV1.RecoveryNotice&&NpSdUsbTestController.CatalogIdentity(oldV1)==NpSdUsbTestController.CatalogIdentity(newV1),"Global checkpoint prose remained a record mutation");
        newV1.End++;Reject(()=>NpSdUsbTestController.RequirePreviousCatalog(oldFingerprint,new[]{newV1,Record(1)}),"Advisory update masked actual prior extent change");
    }
    static byte[] Sector(int family){var b=new byte[512];System.Text.Encoding.ASCII.GetBytes(family==1?"N1SDUTC2":"N2SDUTC2").CopyTo(b,0);Put(b,8,2);Put(b,12,Session);Put(b,72,20000);Put(b,76,1000);Seal(b);return NpFirstSyncSd.BindSector(b,NpFirstSyncWireTests.Fixture(family),family,Uid(family),Token);}
    static void Binding(){foreach(int family in new[]{1,2}){
        var plan=NpSdUsbTestController.PlanForProfile(Parse(Idle(family),family),Inner(family),30,true,null);var report=Terminal(plan,family);var record=Record(family);var sector=Sector(family);
        Need(NpSdUsbTestController.ValidateStoredTicket(sector,report,record,family,Uid(family)).Token==Token,"Valid stored ticket rejected");
        foreach(int at in new[]{160,484,488,500,508}){var bad=(byte[])sector.Clone();bad[at]^=1;if(at!=508)Seal(bad);Reject(()=>NpSdUsbTestController.ValidateStoredTicket(bad,report,record,family,Uid(family)),"Corrupt/wrong on-card binding admitted");}
        foreach(string field in new[]{"Start","End","Session","ProbeMask","ApHz","FormatVersion"}){var bad=Record(family);var f=typeof(NpDownloadRecord).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic);object n=f.GetValue(bad);if(field=="End")f.SetValue(bad,(long)Frontier+1000001);else if(field=="FormatVersion")f.SetValue(bad,family==1?16U:6U);else f.SetValue(bad,f.FieldType==typeof(long)?(object)(Convert.ToInt64(n)+1):(uint)(Convert.ToUInt32(n)+1));Reject(()=>NpSdUsbTestController.ValidateStoredTicket(sector,report,bad,family,Uid(family)),"Mismatched stored record admitted: "+field);}
        var wrongMask=new byte[512];System.Text.Encoding.ASCII.GetBytes(family==1?"N1SDUTC2":"N2SDUTC2").CopyTo(wrongMask,0);Put(wrongMask,8,2);Put(wrongMask,12,Session);Put(wrongMask,72,20000);Put(wrongMask,76,1000);Seal(wrongMask);wrongMask=NpFirstSyncSd.BindSector(wrongMask,NpFirstSyncWireTests.Fixture(family,false,20000,1),family,Uid(family),Token);
        Reject(()=>NpSdUsbTestController.ValidateStoredTicket(wrongMask,report,record,family,Uid(family)),"Valid other-mask receipt admitted");
    }}
    static void V1Loss(){var b=new byte[512];System.Text.Encoding.ASCII.GetBytes("NPLOSS01").CopyTo(b,320);Put(b,328,1);Seal(b);Need(NpSdUsbTestController.V1CleanLossCounters(b),"Known zero V1 loss statistics rejected");
        foreach(int at in new[]{332,336,340,344,348,384,360,368,376}){var bad=(byte[])b.Clone();if(at>=360&&at<=376)Put64(bad,at,1UL<<40);else Put(bad,at,1);Seal(bad);Need(!NpSdUsbTestController.V1CleanLossCounters(bad),"Nonzero V1 loss/skipped highword passed: "+at);}
        b[508]^=1;Need(!NpSdUsbTestController.V1CleanLossCounters(b),"Unsealed V1 counters passed");Need(!NpSdUsbTestController.V1CleanLossCounters(new byte[512]),"Absent V1 counters became zero proof");
    }
    static NpV2SdDatExport.Segment Segment(uint first,uint end,ulong start,ulong stop){return new NpV2SdDatExport.Segment(first,start,new long[3]){FirstLogicalBlock=first,LogicalEndExclusive=end,SourceEndTick=stop};}
    static NpV2SdDatExport.Result Export(uint hz,params ulong[] gaps){
        var r=new NpV2SdDatExport.Result {SampleHz=hz,SourceGapCount=(uint)gaps.Length};ulong tick=1234;uint logical=0;
        r.Segments.Add(Segment(logical,++logical,tick,tick+100));tick+=100;
        foreach(ulong gap in gaps){tick=checked(tick+gap);r.MissingSourceTicks=checked(r.MissingSourceTicks+gap);r.Segments.Add(Segment(logical,++logical,tick,tick+100));tick+=100;}return r;
    }
    static NpSdUsbGapStats Stats(NpV2SdDatExport.Result r){var s=new NpSdUsbGapStats();s.V2Readback(r);return s;}
    static void GapStats(){
        Need(new NpSdUsbProfileResult().Gaps!=null,"Preparing callback lacks stats object");
        var absent=Stats(null);Need(!absent.NeuralDistributionKnown&&absent.NeuralMaximumGapMs==null&&absent.NeuralGapError!=null,"Missing export became known zero");
        Need(!Stats(new NpV2SdDatExport.Result {SampleHz=20000}).NeuralDistributionKnown,"No segments became lossless proof");
        foreach(uint hz in new uint[]{7500,15000,20000,30000}){
            ulong ms=(ulong)hz*1600/1000;var r=Export(hz,ms,ms*2,ms*3,ms*4,ms*5,ms*100);var s=Stats(r);
            Need(s.NeuralDistributionKnown&&s.NeuralGapCount==6&&s.NeuralMissingTicks==ms*115,"Known gaps failed reconciliation");
            Need(s.NeuralSourceHz==hz*1600&&s.NeuralTotalMissingMs==115&&s.NeuralMaximumGapMs==100&&s.NeuralMeanGapMs==115m/6,"Source tick conversion wrong");
            Need(s.NeuralP95GapMs==100&&s.NeuralP99GapMs==100&&s.NeuralGapTicks.SequenceEqual(new[]{ms,ms*2,ms*3,ms*4,ms*5,ms*100}),"Gap order or nearest-rank percentile wrong");
            var zero=Stats(Export(hz));Need(zero.NeuralDistributionKnown&&zero.NeuralGapCount==0&&zero.NeuralTotalMissingMs==0&&zero.NeuralMaximumGapMs==0&&zero.NeuralMeanGapMs==0&&zero.NeuralP95GapMs==0&&zero.NeuralP99GapMs==0,"Known zero gaps displayed unknown");
        }
        var ranked=Stats(Export(20000,Enumerable.Range(1,100).Reverse().Select(n=>(ulong)n*32000).ToArray()));
        Need(ranked.NeuralP95GapMs==95&&ranked.NeuralP99GapMs==99&&ranked.NeuralMeanGapMs==50.5m&&ranked.NeuralTotalMissingMs==5050,"Percentile is not nearest-rank over all gaps");
        Need(Stats(Export(20000,1)).NeuralMaximumGapMs==1m/32000,"Single tick rounded or used AP rather than source clock");
        var contiguous=Export(20000);contiguous.Segments.Add(Segment(1,2,1334,1400));Need(Stats(contiguous).NeuralDistributionKnown,"Touching segments invented gap");
        var prefix=Export(20000);prefix.IndexedGridAppend=true;prefix.MissingStoredBlocks=4;prefix.Segments[0].FirstLogicalBlock=4;prefix.Segments[0].LogicalEndExclusive=5;
        var p=Stats(prefix);Need(p.NeuralDistributionKnown&&p.NeuralGapCount==0&&p.MissingStoredBlocks==4&&p.StorageBreaks==0,"Initial missing storage became source gap");
        var hole=Export(20000,32000,64000);hole.IndexedGridAppend=true;hole.MissingStoredBlocks=2;hole.Segments[1].FirstLogicalBlock=3;hole.Segments[1].LogicalEndExclusive=4;hole.Segments[2].FirstLogicalBlock=4;hole.Segments[2].LogicalEndExclusive=5;hole.SourceGapCount=1;hole.MissingSourceTicks=64000;
        var h=Stats(hole);Need(h.NeuralDistributionKnown&&h.StorageBreaks==1&&h.MissingStoredBlocks==2&&h.NeuralGapCount==1&&h.NeuralMaximumGapMs==2,"Storage hole was attributed to source loss");
        hole.IndexedGridAppend=false;Need(!Stats(hole).NeuralDistributionKnown,"Unindexed logical discontinuity admitted");
        foreach(int fault in Enumerable.Range(0,9)){
            var r=Export(20000,32000);switch(fault){
                case 0:r.Segments[1].SourceStartTick=r.Segments[0].SourceEndTick-1;break;
                case 1:r.Segments[1].LogicalEndExclusive=0;break;
                case 2:r.Segments[1].FirstLogicalBlock=0;break;
                case 3:r.SourceGapCount++;break;
                case 4:r.MissingSourceTicks++;break;
                case 5:r.SampleHz=0;break;
                case 6:r.Segments[1]=null;break;
                case 7:r.Segments[1].SourceStartTick=UInt64.MaxValue;r.Segments[1].SourceEndTick=1;break;
                case 8:r.Segments[0].SourceStartTick=r.Segments[0].SourceEndTick;break;
            }
            var bad=Stats(r);Need(!bad.NeuralDistributionKnown&&bad.NeuralMaximumGapMs==null&&bad.NeuralGapError!=null,"Invalid segment stats qualified: "+fault);
            Need(bad.ReadbackSourceGaps==r.SourceGapCount&&bad.ReadbackMissingTicks==r.MissingSourceTicks,"Malformed distribution erased known readback counters");
        }
        var huge=new NpV2SdDatExport.Result {SampleHz=20000,SourceGapCount=1,MissingSourceTicks=UInt64.MaxValue-3};huge.Segments.Add(Segment(0,1,0,1));huge.Segments.Add(Segment(1,2,UInt64.MaxValue-2,UInt64.MaxValue));
        Need(Stats(huge).NeuralDistributionKnown,"Representable huge gap overflowed time conversion");huge.Segments.Add(Segment(2,3,0,10));Need(!Stats(huge).NeuralDistributionKnown,"Unsigned counter wrap became positive gap");
        var saved=Export(20000);saved.OverflowEvents=8;saved.ProducerDiscardedTicks=9;saved.DroppedBlocks=10;
        Need(Stats(saved).ReadbackOverflows==null&&Stats(saved).ReadbackDroppedBlocks==10,"Unqualified stopped counters became known");saved.StoppedLossStatsPresent=true;
        Need(Stats(saved).ReadbackOverflows==8&&Stats(saved).ReadbackDiscardedTicks==9,"Known stopped readback counters discarded");
    }
    static void TimingStats(){
        var s=new NpSdUsbGapStats();s.Device(Parse(Idle(2),2));Need(s.CardMaximumWriteMs==null&&s.CardMeanWriteMs==null&&s.MaximumServiceMs==null&&s.DeviceMissingTicks==null,"Unknown device metrics invented zero");
        var b=Idle(2);Put(b,24,47|256|64|4096|8192);Put(b,180,7);Put(b,184,8);Put(b,188,9);Put64(b,192,10);Put64(b,200,11);Put(b,216,4);Put64(b,220,18000);Put(b,228,9000);Put(b,232,13000);Put(b,236,108000000);Put(b,240,128000000);Seal(b);
        s.Device(Parse(b,2));Need(s.CardMaximumWriteMs==9&&s.CardMeanWriteMs==4.5&&s.MaximumServiceMs==13,"Device write/service conversion wrong");Need(s.DeviceDroppedBlocks==7&&s.DeviceOverflows==8&&s.DeviceSourceGaps==9&&s.DeviceMissingTicks==10&&s.DeviceDiscardedTicks==11,"Device counters conflated");
        var f=new byte[512];System.Text.Encoding.ASCII.GetBytes("NPLOSS01").CopyTo(f,320);Put(f,328,1);Put(f,76,123);Put(f,332,4);Put64(f,360,1UL<<40);Put64(f,368,17);Put64(f,376,33);Seal(f);
        s.V1Footer(f);Need(s.CardMaximumWriteMs==9,"V1 fallback overwrote device write measurement");
        var v1=new NpSdUsbGapStats();v1.V1Footer(f);Need(v1.CardMaximumWriteMs==123&&v1.V1LossEventsLowerBound==4&&v1.V1SkippedLaneBytesLowerBound.SequenceEqual(new[]{1UL<<40,17UL,33UL}),"V1 measured fallback/lowerbounds lost");Need(!v1.NeuralDistributionKnown&&v1.NeuralMaximumGapMs==null&&v1.NeuralP95GapMs==null,"V1 lower bounds invented exact duration");
        f[508]^=1;var corrupt=new NpSdUsbGapStats();corrupt.V1Footer(f);Need(corrupt.CardMaximumWriteMs==null&&corrupt.V1LossEventsLowerBound==null,"Corrupt footer accepted stats");
        Put(b,216,0);Put64(b,220,0);Put(b,228,0);Put(b,232,0);Seal(b);var zero=new NpSdUsbGapStats();zero.Device(Parse(b,2));Need(zero.CardMaximumWriteMs==0&&zero.MaximumServiceMs==0&&zero.CardMeanWriteMs==null,"Zero-write device timing misrepresented");
        // Readback failure/invalid segments must preserve independently measured device facts.
        s.V2Readback(new NpV2SdDatExport.Result {SampleHz=20000});Need(s.CardMaximumWriteMs==9&&s.DeviceMissingTicks==10&&!s.NeuralDistributionKnown,"Readback failure erased device evidence");
    }
    static byte[] CleanV1Footer(){var footer=new byte[512];System.Text.Encoding.ASCII.GetBytes("NPLOSS01").CopyTo(footer,320);Put(footer,328,1);Seal(footer);return footer;}
    static string ImuFixture(string root,string name,int initial=2){string folder=Path.Combine(root,name);Directory.CreateDirectory(folder);int rows=initial+3;
        File.WriteAllLines(Path.Combine(folder,"imu_grid_status.txt"),new[]{"selected=true","carrier=NSG1","final_parent_verified=True","enforced_schedule_verified=True","grid_phase_qualified=False","source_window_verified=False","first_slot_phase_valid=False","rows="+rows,"error=none"});
        File.WriteAllLines(Path.Combine(folder,"analogin_encoding.txt"),new[]{"schema=NP_IMU_NSG1_GRID_V1","output_grid_hz=1250; period_us=800; first_slot=0","rows="+rows,"observed=3","missing="+initial,"first_observed_slot="+initial,"slot_extent="+rows,"terminal_tail_coverage=unknown","error=Saved first NP byte/counter or phase interval unavailable."});
        File.WriteAllBytes(Path.Combine(folder,"analogin.dat"),new byte[rows*32]);var flags=new byte[rows];flags[initial]=7;flags[initial+1]=31;flags[initial+2]=7;File.WriteAllBytes(Path.Combine(folder,"analogin_validity.dat"),flags);
        File.WriteAllLines(Path.Combine(folder,"imu_grid_segments.csv"),new[]{"first_slot,row_count,first_file_row","0,"+rows+",0"});
        File.WriteAllLines(Path.Combine(folder,"imu_grid_gaps.csv"),initial==0?new[]{"first_slot,slot_count,reason"}:new[]{"first_slot,slot_count,reason","0,"+initial+",transport_gap"});return folder;
    }
    static void FileReject(Action action,string why){bool rejected=false;try{action();}catch(IOException){rejected=true;}catch(InvalidDataException){rejected=true;}Need(rejected,why);}
    static void V1Completion(string root){
        var unknown=Parse(Idle(1),1);var footer=CleanV1Footer();var profile=new NpSdUsbProfileResult();var good=ImuFixture(root,"good");
        NpSdUsbTestController.CheckCleanCompletion(1,unknown,true,footer,good,profile);checks++;
        Need(profile.SavedImuRows==5&&profile.SavedImuObservedRows==3&&profile.SavedImuInitialUnavailableSlots==2,"Initial unavailable V1 IMU coverage not retained");
        Need(profile.SavedImuScope.Contains("unknown")&&!profile.Gaps.DeviceDroppedBlocks.HasValue&&!profile.Gaps.DeviceDiscardedTicks.HasValue,"V1 unavailable firmware counters fabricated");
        NpSdUsbTestController.CheckCleanCompletion(1,unknown,true,footer,ImuFixture(root,"no_prefix",0),new NpSdUsbProfileResult());checks++;
        FileReject(()=>NpSdUsbTestController.CheckCleanCompletion(1,unknown,false,footer,good,new NpSdUsbProfileResult()),"Incomplete recording accepted");
        var badFooter=(byte[])footer.Clone();Put64(badFooter,376,1UL<<40);Seal(badFooter);FileReject(()=>NpSdUsbTestController.CheckCleanCompletion(1,unknown,true,badFooter,good,new NpSdUsbProfileResult()),"V1 skipped lane highword accepted");
        var corruptFooter=(byte[])footer.Clone();corruptFooter[508]^=1;FileReject(()=>NpSdUsbTestController.CheckCleanCompletion(1,unknown,true,corruptFooter,good,new NpSdUsbProfileResult()),"Corrupt V1 footer accepted");
        foreach(int fault in Enumerable.Range(0,15)){
            string folder=ImuFixture(root,"bad_"+fault);string validity=Path.Combine(folder,"analogin_validity.dat"),status=Path.Combine(folder,"imu_grid_status.txt"),encoding=Path.Combine(folder,"analogin_encoding.txt");
            switch(fault){
                case 0:File.Delete(Path.Combine(folder,"analogin.dat"));break;
                case 1:File.Delete(status);break;
                case 2:var flags=File.ReadAllBytes(validity);flags[3]=0;File.WriteAllBytes(validity,flags);break;
                case 3:var errors=File.ReadAllBytes(validity);errors[3]|=32;File.WriteAllBytes(validity,errors);break;
                case 4:File.AppendAllText(status,"rows=5\n");break;
                case 5:File.WriteAllText(status,File.ReadAllText(status).Replace("final_parent_verified=True","final_parent_verified=False"));break;
                case 6:File.WriteAllText(status,File.ReadAllText(status).Replace("error=none","error=Indexed grid CRC mismatch"));break;
                case 7:File.WriteAllText(encoding,File.ReadAllText(encoding).Replace("observed=3","observed=4"));break;
                case 8:File.WriteAllText(Path.Combine(folder,"imu_grid_segments.csv"),"first_slot,row_count,first_file_row\n0,3,0\n4,2,3\n");break;
                case 9:File.WriteAllText(Path.Combine(folder,"imu_grid_gaps.csv"),"first_slot,slot_count,reason\n0,2,transport_gap\n3,1,producer_missing\n");break;
                case 10:File.WriteAllBytes(validity,new byte[4]);break;
                case 11:File.WriteAllBytes(validity,new byte[]{0,0,3,3,3});break;
                case 12:File.WriteAllText(encoding,File.ReadAllText(encoding).Replace("first_observed_slot=2","first_observed_slot=3"));break;
                case 13:File.WriteAllText(encoding,File.ReadAllText(encoding).Replace("rows=5","rows=bogus"));break;
                case 14:File.WriteAllText(Path.Combine(folder,"imu_grid_gaps.csv"),"first_slot,slot_count,reason\n1,2,transport_gap\n");break;
            }
            FileReject(()=>NpSdUsbTestController.CheckCleanCompletion(1,unknown,true,footer,folder,new NpSdUsbProfileResult()),"Missing/corrupt/observed-gap IMU evidence passed: "+fault);
        }
        var v2=Idle(2);FileReject(()=>NpSdUsbTestController.CheckCleanCompletion(2,Parse(v2,2),true,null,null,new NpSdUsbProfileResult()),"V2 unknown firmware groups accepted");
        Put(v2,24,47|64|512);Put(v2,180,0);Put(v2,184,0);Put64(v2,200,0);Put(v2,244,0);Put(v2,248,0);Seal(v2);NpSdUsbTestController.CheckCleanCompletion(2,Parse(v2,2),true,null,null,new NpSdUsbProfileResult());checks++;
        foreach(uint missing in new uint[]{64,512}){var b=(byte[])v2.Clone();Put(b,24,(47|64|512)&~missing);Seal(b);FileReject(()=>NpSdUsbTestController.CheckCleanCompletion(2,Parse(b,2),true,null,null,new NpSdUsbProfileResult()),"V2 missing known-counter group accepted");}
        foreach(int at in new[]{180,184,200,244,248}){var b=(byte[])v2.Clone();Put(b,at,1);Seal(b);FileReject(()=>NpSdUsbTestController.CheckCleanCompletion(2,Parse(b,2),true,null,null,new NpSdUsbProfileResult()),"V2 nonzero required counter accepted");}
    }
    static int Main(string[] args){try{Admission();Delivery();ReadRetirement();FreshSequence();TerminalFreshness();ReadbackTransition();CapabilityReenumeration();Catalog();Binding();V1Loss();GapStats();TimingStats();V1Completion(args[1]);string source=File.ReadAllText(args[0]);int verify=source.IndexOf("        void Verify(",StringComparison.Ordinal),bound=source.IndexOf("ValidateStoredTicket(",verify,StringComparison.Ordinal),export=source.IndexOf("NpCe64Export.ExportCalibrated(",verify,StringComparison.Ordinal);Need(verify>=0&&bound>verify&&bound<export,"Actual exporter bypasses bound ticket validation");Need(source.IndexOf("profile.Gaps.Device(terminal)",StringComparison.Ordinal)<verify,"Device metrics not retained before readback");Need(source.IndexOf("profile.Gaps.V1Footer(footer)",verify,StringComparison.Ordinal)<export,"V1 metrics not retained before exporter");Need(source.IndexOf("profile.Gaps.V2Readback(exported)",export,StringComparison.Ordinal)<source.IndexOf("!exported.StoppedLossStatsPresent",export,StringComparison.Ordinal),"V2 stats lost before failure classification");Need(source.IndexOf("CheckCleanCompletion(Family,terminal",export,StringComparison.Ordinal)>source.IndexOf("NpSdLoggerTest.CheckV1Readback(",export,StringComparison.Ordinal),"V1 completion skipped strict neural readback");int prepare=source.IndexOf("current=await RefreshAfterCatalog(",StringComparison.Ordinal),makePlan=source.IndexOf("byte[] inner=Inner(current",prepare,StringComparison.Ordinal),outCall=source.IndexOf("usb.Submit(plan)",makePlan,StringComparison.Ordinal);Need(prepare>=0&&makePlan>prepare&&outCall>makePlan&&!source.Substring(makePlan,outCall-makePlan).Contains("new NpDownloadStream"),"Actual Run opened bulk catalog after final INSPECT/plan");int originalFile=source.IndexOf("profile.OriginalTerminalRawFile=",outCall,StringComparison.Ordinal),refreshTerminal=source.IndexOf("await RefreshTerminal(",originalFile,StringComparison.Ordinal),safe=source.IndexOf("profile.SafeToContinue=",refreshTerminal,StringComparison.Ordinal);Need(originalFile>outCall&&refreshTerminal>originalFile&&safe>refreshTerminal,"Terminal refresh/decision lost original raw ordering");Need(source.Contains("while(elapsed.Elapsed.TotalSeconds<seconds+240)")&&source.Contains("await ObserveAcceptedTicket(plan,"),"Accepted-ticket timeout or actual observation route changed");Console.WriteLine("PASS "+checks+" actual USB SD controller admission/delivery/catalog/ticket/loss/gap/V1-IMU/inspection-terminal-freshness decisions; actual exporter DTOs; native/export boundaries inert; no hardware.");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}

// Deliberately inert unused I/O boundaries. Any accidental invocation fails;
// tests exercise the actual controller's shared decisions and one-OUT method.
namespace CE32_console {
 internal sealed class NpDownloadStream:MemoryStream {internal NpDownloadStream(string p){throw new InvalidOperationException("No native card in controller fixture");}internal string TransportDescription{get{return "High-speed USB fixture";}}}
 internal static class NpDownloadCatalog {internal static IEnumerable<NpDownloadRecord> Read(Stream s){throw new InvalidOperationException("No catalog I/O fixture");}internal static byte[] Sector(Stream s,long lba){throw new InvalidOperationException("No card I/O fixture");}}
 internal static class NpDownloadNative {
  internal enum USBD_PIPE_TYPE {Bulk} internal struct USB_INTERFACE_DESCRIPTOR {internal byte bNumEndpoints;} internal struct WINUSB_PIPE_INFORMATION {internal USBD_PIPE_TYPE PipeType;internal ushort MaximumPacketSize;} internal struct WINUSB_SETUP_PACKET {internal byte RequestType,Request;internal ushort Length;}
  internal static Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(string p,uint a,uint b,IntPtr c,uint d,uint e,IntPtr f){throw new InvalidOperationException("No native calls");}
  internal static bool WinUsbInitialize(Microsoft.Win32.SafeHandles.SafeFileHandle f,out IntPtr u){throw new InvalidOperationException("No native calls");}
  internal static bool WinUsbQueryInterfaceSettings(IntPtr u,byte a,out USB_INTERFACE_DESCRIPTOR d){throw new InvalidOperationException("No native calls");}
  internal static bool WinUsbQueryPipe(IntPtr u,byte a,byte b,out WINUSB_PIPE_INFORMATION p){throw new InvalidOperationException("No native calls");}
  internal static bool WinUsbControlTransfer(IntPtr u,WINUSB_SETUP_PACKET s,byte[] b,uint l,out uint n,IntPtr o){throw new InvalidOperationException("No native calls");}
  internal static void WinUsbFree(IntPtr u){throw new InvalidOperationException("No native calls");}internal static string[] EnumerateNpPaths(){throw new InvalidOperationException("No device enumeration");}
 }
 internal static class NpV1Management {internal sealed class Status {internal uint Capabilities;}internal static Status ParseSplitStatus(byte[] b,string u){throw new InvalidOperationException("No native status");}internal static string UidFromPath(string p){throw new InvalidOperationException("No native identity");}internal static string EnsureDownload(string p,Func<bool> c){throw new InvalidOperationException("No mode switch");}}
 internal static class NpV2FirmwareForm {internal static string Uid(string p){throw new InvalidOperationException("No native identity");}internal static Task<string> EnsureDownload(string p,Func<bool> c){throw new InvalidOperationException("No mode switch");}}
 internal static class NpCe64Export {internal static USB512_interface.NpV2SdDatExport.Result ExportCalibrated(Stream s,long a,long b,string o,Func<bool> c,Action<int> p,object x){throw new InvalidOperationException("No payload exporter");}}
}
namespace USB512_interface {
 internal sealed class NpUsbCatalogRepair:IDisposable {internal const uint V1Capability=0x20,V2Capability=0x10;internal const byte InspectRequest=0xa1;internal sealed class Receipt {internal int Generation;internal uint Operation,Phase,Error,Nonce,InspectedNonce,Capacity,NextLba,Flags;internal string Cid;}internal static NpUsbCatalogRepair Open(string path,Func<bool> available,Func<bool> cancel){throw new InvalidOperationException("No real catalog control in fixture");}internal Receipt Inspect(){throw new InvalidOperationException("No real catalog control in fixture");}public void Dispose(){throw new InvalidOperationException("No real catalog control in fixture");}}
 internal sealed class NpV2ServiceStatus {internal uint Capabilities;internal static NpV2ServiceStatus Parse(byte[] b){throw new InvalidOperationException("No native status");}internal void RequireUid(string u){throw new InvalidOperationException("No native identity");}}
 internal static class NpProgressEstimate {internal static string Duration(double s){throw new InvalidOperationException("No wait loop in fixture");}}
 internal static class NpFolderNaming {internal static string CreateFresh(string r,string n){throw new InvalidOperationException("No Run I/O");}internal static string DownloadName(string n,DateTime? d,string f){throw new InvalidOperationException("No Run I/O");}}
 internal static class NpSdLoggerTest {internal static uint Blocks(uint m,uint h,uint s){throw new InvalidOperationException("No Run I/O");}internal static void CheckV1Readback(NpSdTestResult r,byte[] h,byte[] f,string[] l){throw new InvalidOperationException("No payload checks");}internal static void CheckV2Readback(NpSdTestResult r,byte[] h,NpV2SdDatExport.Result x){throw new InvalidOperationException("No payload checks");}}
}
