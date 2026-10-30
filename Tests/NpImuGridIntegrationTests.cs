using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using USB512_interface;
using CE32_console;

internal static class NpImuGridIntegrationTests {
    const string Uid="001D00065942500B20313354";const ulong Token=0x1020304050607080UL;const uint Sid=1792272185;
    static int checks;static string root;
    static void Need(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static void Reject(Action f,string why){try{f();}catch(Exception e) when(e is IOException||e is InvalidDataException||e is ArgumentException){Need(true,why);return;}throw new Exception("Expected rejection: "+why);}
    static void Put(byte[] b,int o,uint n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,o,4);}
    static void Put64(byte[] b,int o,ulong n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,o,8);}
    static void Seal(byte[] b){Put(b,b.Length-4,NpFirstSyncWire.Crc(b,b.Length-4));}
    static string Folder(string name){string p=Path.Combine(root,name);Directory.CreateDirectory(p);return p;}
    static byte[] Frame(ulong first=0,uint gen=9,ushort mask=5){var b=new NpImuGridBatch{FirstSlot=first,CaptureGeneration=gen,SlotCount=3,ObservedMask=mask,AgValid=mask,MagValid=mask,AgRepeat=(ushort)(mask&4),MagRepeat=(ushort)(mask&4),ErrorMask=(ushort)(mask&1)};b.Rows=Enumerable.Range(0,3).Where(i=>(mask&(1<<i))!=0).Select(i=>new[]{1000000,-1000000,int.MaxValue,1000,-1000,0,300,-300,0}).ToArray();return NpImuGridWire.Pack(b);}
    static byte[] Packet(byte[] frame,uint sequence=0,uint sid=Sid){var b=new byte[506];Encoding.ASCII.GetBytes("NPGRID01").CopyTo(b,0);Put(b,8,sequence);Put(b,12,sid);Put(b,16,(uint)frame.Length);frame.CopyTo(b,20);Put(b,502,NpImuGridCarrier.Crc(b,0,502));return b;}
    static byte[] Control(uint flags=1,uint rev=1){var b=new byte[32];Put(b,0,0x4347534e);Put(b,4,1);Put(b,8,flags);var u=NpFirstSyncUsb.UidWords(Uid);for(int i=0;i<3;i++)Put(b,12+i*4,u[i]);Put(b,24,rev);Seal(b);return b;}
    static void Controls(){
        int reads=0,writes=0;Need(!NpImuGridControl.PrepareUsb(()=>{reads++;throw new Win32Exception(31);},n=>writes++,Uid,true)&&reads==1&&writes==0,"Unsupported probe did not preserve legacy");
        reads=writes=0;Need(NpImuGridControl.PrepareUsb(()=>++reads==1?Control():Control(3,2),n=>{writes++;throw new IOException("Lost OUT completion");},Uid,true)&&reads==2&&writes==1,"Lost SELECT did not reconcile once");
        reads=writes=0;Need(!NpImuGridControl.PrepareUsb(()=>{reads++;return Control();},n=>writes++,Uid,true)&&writes==1&&reads==2,"Confirmed legacy fallback");
        reads=writes=0;Need(!NpImuGridControl.PrepareUsb(()=>{reads++;return Control(0);},n=>writes++,Uid,true)&&reads==1&&writes==0,"Valid unsupported NSGC gated legacy recording or mutated selection");
        reads=writes=0;Need(!NpImuGridControl.PrepareUsb(()=>++reads==1?Control(2,1):Control(0,2),n=>{Need(!n,"Unsupported stale mode selected grid");writes++;},Uid,true)&&reads==2&&writes==1,"Unsupported stale selected state was not reconciled to idle legacy");
        reads=writes=0;Reject(()=>NpImuGridControl.PrepareUsb(()=>++reads==1?Control():Control(2,2),n=>writes++,Uid,true),"Readiness loss qualified selected grid");Need(writes==1&&reads==2,"Readiness loss replayed mutation");
        writes=0;var bad=Control();bad[28]^=1;Reject(()=>NpImuGridControl.PrepareUsb(()=>bad,n=>writes++,Uid,true),"Invalid CRC was called unsupported");Need(writes==0,"Bad identity caused mutation");
        Reject(()=>NpImuGridControl.ParseUsb(Control(),"001A0004434250062039314E"),"Wrong UID");Reject(()=>NpImuGridControl.ParseUsb(Control(5),Uid),"Frozen control");
        reads=writes=0;Reject(()=>NpImuGridControl.PrepareUsb(()=>{if(++reads==1)return Control();throw new IOException("readback lost");},n=>writes++,Uid,true),"Unknown mutation result started capture");Need(writes==1&&reads==2,"Uncertain mutation replayed");
        var request=NpImuGridControl.BleRequest(123,true);Need(BitConverter.ToUInt32(request,8)==10&&BitConverter.ToUInt32(request,16)==1&&request.Skip(20).Take(40).All(n=>n==0)&&BitConverter.ToUInt32(request,60)==NpSdControl.Crc(request,60),"BLE op10 geometry");
        var before=new NpSdStatus{State=0,Flags=17|NpImuGridControl.BleSupported,LastId=7};var after=new NpSdStatus{State=0,Flags=17|NpImuGridControl.BleSupported|NpImuGridControl.BleSelected,LastId=8};writes=reads=0;
        var selected=NpImuGridControl.PrepareBle(before,b=>{writes++;throw new TimeoutException();},()=>{reads++;return Task.FromResult(after);},true).GetAwaiter().GetResult();Need(selected==after&&writes==1&&reads==1,"BLE lost ACK not reconciled read-only");
        before.Flags=17;writes=reads=0;Need(NpImuGridControl.PrepareBle(before,b=>{writes++;return Task.FromResult(after);},()=>{reads++;return Task.FromResult(after);},true).Result==before&&writes==0&&reads==0,"Legacy BLE changed");
    }
    static void Carriers(){
        var raw=Packet(Frame());uint sequence;Need(NpImuGridCarrier.Usb(raw,0,506,Sid,out sequence).SequenceEqual(Frame())&&sequence==0,"USB exact frame retention");var original=(byte[])raw.Clone();
        Reject(()=>NpImuGridCarrier.Usb(raw,0,506,Sid+1,out sequence),"USB wrong session");raw[501]=1;Put(raw,502,NpImuGridCarrier.Crc(raw,0,502));Reject(()=>NpImuGridCarrier.Usb(raw,0,506,Sid,out sequence),"USB padding");raw=original;raw[50]^=1;Reject(()=>NpImuGridCarrier.Usb(raw,0,506,Sid,out sequence),"USB outer CRC");
        var h=new byte[512];Put(h,8,6);var f=(byte[])h.Clone();Need(!NpImuGridCarrier.SdMetadata(h,f),"Legacy declaration changed");Put(h,104,NpImuGridWire.Magic);Put(h,108,1);f=(byte[])h.Clone();Need(NpImuGridCarrier.SdMetadata(h,f),"SD declaration");var d=(byte[])h.Clone();var b=new byte[65024];var frame=Frame();frame.CopyTo(b,62144);frame.CopyTo(b,62144+frame.Length);Put(d,124,(uint)(2*frame.Length));int n=NpImuGridCarrier.SdSpan(h,d);NpImuGridCarrier.SdPadding(b,n);Need(NpImuGridCarrier.SdFrames(b,n).Count()==2,"SD concatenated inner frames");
        b[65023]=1;Reject(()=>NpImuGridCarrier.SdPadding(b,n),"SD padding");b[65023]=0;b[62144+44]^=1;Reject(()=>NpImuGridCarrier.SdFrames(b,n).ToArray(),"SD inner CRC optional rejection");Put(d,124,2881);Reject(()=>NpImuGridCarrier.SdSpan(h,d),"Global SD span");Put(h,8,5);Reject(()=>NpImuGridCarrier.SdDeclared(h),"Grid admitted outside format6");
    }
    static void Writers(){
        string p=Folder("writer");using(var w=new NpImuGridWriter(p,9)){w.Feed(Frame(2));Need(w.Rows==5&&w.Missing==3&&w.Observed==2,"Missing prefix/inner ordinal accounting");var mark=w.Mark();w.Feed(Frame(5));w.Restore(mark);Need(w.Rows==5&&w.Missing==3,"Prefix rollback");Reject(()=>w.Feed(Frame(5,10)),"Generation mismatch");Reject(()=>w.Feed(Frame(4)),"Overlap");w.Complete(7,1250,0,true);Need(w.Rows==5,"Unknown phase fabricated a zero-phase tail");}
        var data=File.ReadAllBytes(Path.Combine(p,"analogin.dat"));var flags=File.ReadAllBytes(Path.Combine(p,"analogin_validity.dat"));Need(data.Length==5*32&&flags.SequenceEqual(new byte[]{0,0,39,0,31}),"Exact missing/valid/repeat/error flags");Need(BitConverter.ToInt16(data,2)==short.MinValue&&BitConverter.ToInt16(data,2*32+2)==4096&&BitConverter.ToInt16(data,2*32+4)==-4096&&BitConverter.ToInt16(data,2*32+6)==short.MaxValue&&BitConverter.ToInt16(data,2*32+8)==16&&BitConverter.ToInt16(data,2*32+14)==1,"WILD scaling and unavailable sentinels");Need(File.ReadAllText(Path.Combine(p,"analogin_encoding.txt")).Contains("phase_offset_us=unknown"),"Nominal slot0 became exact phase");
        p=Folder("sparse");using(var w=new NpImuGridWriter(p,9)){w.Feed(Frame());w.Feed(Frame(1000000000));Need(w.Rows==6&&w.Slots==1000000003&&w.Missing==999999999,"Long gaps expanded/rejected or ordinals lost");w.Complete(1000000003,1250,0,true);}Need(new FileInfo(Path.Combine(p,"analogin.dat")).Length==192&&File.ReadAllText(Path.Combine(p,"imu_grid_segments.csv")).Contains("1000000000,3,3"),"Sparse segment row join");
    }
    static byte[] Receipt(uint session=Sid){var r=NpFirstSyncWireTests.Fixture(1,false,15000,3);Put(r,16,session);Put(r,12,BitConverter.ToUInt32(r,12)&~NpFirstSyncWire.AuxGated);r[133]=69;r[134]=64;Put(r,136,UInt32.MaxValue);Put(r,140,UInt32.MaxValue);Put64(r,152,3);Put(r,160,1250);Put64(r,168,0x8000000000000000UL);Put(r,176,0);Seal(r);return r;}
    static byte[] Anchor(){var a=new byte[64];uint[] w={0x5241504e,1,77,0x11f,9,1000,1000,1000000,1000012,128000000,11700000,0,0,15000,1000};for(int i=0;i<w.Length;i++)Put(a,i*4,w[i]);Seal(a);return a;}
    static byte[] Phase(ulong first=3,uint flags=3,uint session=Sid){var b=NpImuGridWriter.PhaseBinding(Uid,Token,NpFirstSyncWire.Parse(Receipt(session)));return NpImuGridPhase.Pack(new NpImuGridPhaseRecord{Uid=b.Uid,Session=b.Session,CaptureGeneration=b.CaptureGeneration,SourceEpoch=b.SourceEpoch,CounterDomain=b.CounterDomain,Token=b.Token,FirstAcceptedSlot=first,LowerNumerator=(flags&1)!=0?15000:Int64.MinValue,UpperNumerator=(flags&1)!=0?15010:Int64.MinValue,Denominator=(flags&1)!=0?1U:0,MaximumJitter=(flags&2)!=0?10U:UInt32.MaxValue,Flags=flags});}
    static void Phases(){
        foreach(string mode in new[]{"valid","lost_first","missing_interval","missing_jitter","bad_crc","wrong_token","wrong_generation","conflict","all_missing_prefix","aux_counter","aux_rate","aux_bits","aux_kind","aux_point"}){
            string p=Folder("phase_"+mode);var expected=NpImuGridWriter.PhaseBinding(Uid,Token,NpFirstSyncWire.Parse(Receipt()));
            using(var w=new NpImuGridWriter(p,9)){
                byte[] raw=Phase(3,mode=="missing_interval"?2U:mode=="missing_jitter"?1U:3U);if(mode=="bad_crc")raw[80]^=1;if(mode=="wrong_token"){Put64(raw,36,Token+1);Seal(raw);}if(mode=="wrong_generation"){Put(raw,24,10);Seal(raw);}
                byte[] rr=Receipt();if(mode=="aux_counter")Put64(rr,152,4);if(mode=="aux_rate")Put(rr,160,200);if(mode=="aux_bits")rr[134]=32;if(mode=="aux_kind")rr[135]=2;if(mode=="aux_point"){rr[133]&=191;rr[135]=0;Put(rr,144,UInt32.MaxValue);Put(rr,148,UInt32.MaxValue);}Seal(rr);
                var parent=NpFirstSyncWire.Parse(rr);bool accepted=w.AcceptPhase(raw,expected,11700000,parent);Need(accepted!=(mode=="bad_crc"||mode=="wrong_token"||mode=="wrong_generation"||mode.StartsWith("aux_")),"NSGP exact context/CRC/NPSTART admission");
                if(mode=="conflict"){var c=Phase(4);Need(!w.AcceptPhase(c,expected,11700000,parent),"Conflicting once-only phase admitted");}
                if(mode=="all_missing_prefix")w.Feed(Frame(0,9,0));w.Feed(Frame(mode=="lost_first"?4UL:3UL));w.Complete(780+100000,11700000,780,true);
                Need(w.PhaseQualified==(mode=="valid"||mode=="all_missing_prefix"),"Stored first observed ordinal/phase/jitter qualification: "+mode);
            }
            Need(File.ReadAllText(Path.Combine(p,"analogin_encoding.txt")).Contains("np_sample_grid_locked=false"),"NSGP became sample0 lock");Need(new FileInfo(Path.Combine(p,"analogin.dat")).Length>0,"Bad optional phase discarded actual grid data");
        }
    }
    static void UnknownPhases(){
        for(int mode=0;mode<10;mode++){
            string p=Folder("unknown_phase_"+mode);var rr=Receipt();rr[133]=5;rr[135]=0;Put(rr,144,UInt32.MaxValue);Put(rr,148,UInt32.MaxValue);
            if(mode==1){rr[133]=7;Put(rr,136,1000);Put(rr,140,128000000);}
            if(mode==2)Put64(rr,152,4);if(mode==3)Put(rr,160,200);if(mode==4)rr[134]=32;
            Seal(rr);var parent=NpFirstSyncWire.Parse(rr);var expected=NpImuGridWriter.PhaseBinding(Uid,Token,parent);
            byte[] raw=Phase(mode==5?4UL:3UL,mode==6?1U:0U);if(mode==7){Put64(raw,36,Token+1);Seal(raw);}if(mode==8)raw[80]^=1;
            using(var w=new NpImuGridWriter(p,9)){
                bool accepted=w.AcceptPhase(raw,expected,mode==9?11700001U:11700000U,parent);Need(accepted==(mode<2||mode==6),"Unknown/start-only interval lost strict context/native ordinal/rate checks");
                w.Feed(Frame(3));w.Complete(100000,11700000,780,true);Need(!w.PhaseQualified,"Unknown interval gained phase/physical/RTC qualification");
            }
            string archive=Path.Combine(p,"imu_grid_phase.bin");Need(File.Exists(archive)==(mode<2||mode==6),"Invalid phase archived or bound unknown/start-only evidence dropped");
            if(mode<2){Need(File.ReadAllBytes(archive).SequenceEqual(raw),"Unknown NSGP bytes changed");var text=File.ReadAllText(Path.Combine(p,"analogin_encoding.txt"));Need(text.Contains("NP_reference_phase_offset_us=unknown")&&text.Contains("np_sample_grid_locked=false")&&!text.Contains("contradict"),"Unknown interval became mismatch or sample lock");}
            Need(new FileInfo(Path.Combine(p,"analogin.dat")).Length==192,"Optional unknown/invalid phase changed actual grid values");
        }
    }
    static NpFirstSyncUsbFineRoot Root(string uid=Uid,ulong token=Token,uint generation=9,ulong sourceSequence=8,bool sourcePoint=true,byte firstPointKind=1){var r=Receipt(generation);Put(r,20,generation);r[135]=firstPointKind;if(!sourcePoint){r[133]&=191;r[135]=0;Put(r,144,UInt32.MaxValue);Put(r,148,UInt32.MaxValue);}Seal(r);var info=new byte[64];Put(info,0,NpFirstSyncUsb.InfoMagic);Put(info,4,0x70002);Put64(info,8,token);var u=NpFirstSyncUsb.UidWords(uid);for(int i=0;i<3;i++)Put(info,16+i*4,u[i]);Put(info,28,1000);Put(info,32,generation);Put(info,36,7);Put(info,40,108);Put(info,44,1);Put(info,48,192);Put(info,52,1);Put(info,56,7);Seal(info);var a=new NpFirstSyncUsbAssembler(NpFirstSyncUsb.ParseInfo(info,uid,1),token,generation);
        for(int i=0;i<7;i++){var p=new byte[108];Put(p,0,NpFirstSyncUsb.EnvelopeMagic);Put(p,4,0x40001);Put64(p,8,token);Put(p,16,generation);for(int j=0;j<3;j++)Put(p,20+j*4,u[j]);Put64(p,32,(ulong)i);Put(p,40,NpFirstSyncUsb.PageMagic);p[44]=1;p[45]=(byte)i;p[46]=7;Put(p,48,BitConverter.ToUInt32(r,324));Buffer.BlockCopy(r,i*48,p,52,i==6?40:48);Put(p,100,NpFirstSyncWire.Crc(p.Skip(40).Take(64).ToArray(),60));Seal(p);Need(a.Add(p,(ulong)i),"Root receipt pages");}
        var anchor=Anchor();Put(anchor,16,generation);Seal(anchor);var env=NpFirstSyncUsb.Anchor(anchor,r,uid,token,generation,sourceSequence);Need(a.FeedAnchor(env,sourceSequence),"Actual kind5 source root");return new NpFirstSyncUsbFineRoot(info,a,env,sourceSequence);}
    static NpImuGridSourceWindow Window(NpFirstSyncUsbFineRoot root){var r=NpFirstSyncWire.Parse(root.Receipt);var selected=Enumerable.Range(0,3).Where(i=>(r.Mask&(1<<i))!=0).ToArray();long raw=selected.Max(i=>(long)r.Probes[i].FirstTick)+200*780+1;return new NpImuGridSourceWindow{Finalized=true,StartFinalized=true,OuterLossless=true,Mask=r.Mask,ApHz=r.ApHz,Probes=selected.Select(i=>new NpImuGridSourceWindow.Probe{Port=i+1,FirstTick=(long)r.Probes[i].FirstTick,FirstCounter=r.Probes[i].Counter,RawBytes=raw,ApRows=200,FramesChecked=2600}).ToArray()};}
    static void SourceWindows(){var source=Root();var good=Window(source);ulong end;string why;Need(good.Probes.All(p=>p.FirstTick>0)&&good.TryBound(source,out end,out why)&&end==156780,"Bound startup-discard prefix failed despite exact final first byte/counter");
        foreach(Action<NpImuGridSourceWindow> bad in new Action<NpImuGridSourceWindow>[] {
            p=>p.Finalized=false,p=>p.OuterLossless=false,p=>p.Mask=1,p=>p.ApHz=20000,p=>p.Probes=p.Probes.Take(1).ToArray(),p=>p.Probes[1].Port=1,
            p=>p.Probes[0].FirstTick++,p=>p.Probes[0].FirstCounter++,p=>p.Probes[0].RawBytes++,p=>p.Probes[0].FrameErrors=1,p=>p.Probes[0].CounterErrors=1,
            p=>p.Probes[0].GapEvents=1,p=>p.Probes[0].Relocks=1,p=>p.Probes[0].RecoveryBytes=1,p=>p.Probes[0].MissingApRows=1,p=>p.Probes[0].MissingLfpRows=1,
            p=>p.Probes[0].LfpPhaseErrors=1,p=>p.Probes[0].HostDroppedBytes=1,p=>p.Probes[0].HostDropEvents=1,p=>p.Probes[0].ApRows++,p=>p.Probes[0].FramesChecked=0,
            p=>{foreach(var probe in p.Probes)probe.RawBytes+=60;}
        }){var altered=Window(source);bad(altered);Need(!altered.TryBound(source,out end,out why)&&end==0&&!String.IsNullOrEmpty(why),"Bad continuity/first-position/source extent admitted");}
    }
    static void Usb(){string p=Folder("usb");NpFirstSyncUsbFineRoot r=null;using(var w=new NpImuGridUsb(p,Uid,()=>Sid,()=>r)){w.Feed(Packet(Phase(3,3,9)),0,506);Need(!File.Exists(Path.Combine(p,"analogin.dat")),"Unbound parent emitted grid");r=Root();Need(NpFirstSyncWire.Parse(r.Receipt).Session==9&&r.Generation==9&&Sid!=9,"Fixture equates wrapper and NPSTART sessions");w.Feed(Packet(Frame(3),1),0,506);w.Complete(Window(r));Need(w.Error==null,"Distinct wrapper/NPSTART session grid failed: "+w.Error);}Need(new FileInfo(Path.Combine(p,"analogin.dat")).Length==192,"Pending batches not replayed once");string encoding=File.ReadAllText(Path.Combine(p,"analogin_encoding.txt"));Need(encoding.Contains("grid_phase_qualified=True"),"Bound pending USB phase did not qualify");Need(encoding.Contains("terminal_tail_coverage=unknown"),"Retained-row phase proof became terminal completeness");
        p=Folder("usb_missing");using(var w=new NpImuGridUsb(p,Uid,()=>Sid,()=>null)){for(int i=0;i<33;i++)w.Feed(Packet(Frame((ulong)i*3),(uint)i),0,506);Need(w.Error!=null&&!File.Exists(Path.Combine(p,"analogin.dat")),"Unbounded/missing parent admitted");}
        p=Folder("usb_badgen");using(var w=new NpImuGridUsb(p,Uid,()=>Sid,()=>Root())){w.Feed(Packet(Frame(0,10)),0,506);Need(w.Error!=null,"Wrong parent generation admitted");}
        p=Folder("usb_io");Directory.CreateDirectory(Path.Combine(p,"analogin_encoding.txt"));using(var w=new NpImuGridUsb(p,Uid,()=>Sid,()=>Root())){w.Feed(Packet(Frame(3)),0,506);Need(w.Error!=null,"Optional grid IO error escaped or was lost");}
        p=Folder("usb_wrong_outer_session");using(var w=new NpImuGridUsb(p,Uid,()=>Sid,()=>Root())){w.Feed(Packet(Phase(3,3,9),0,9),0,506);Need(w.Error!=null&&!File.Exists(Path.Combine(p,"analogin.dat")),"NPSTART session was accepted as USB wrapper session");}
        p=Folder("usb_wrong_phase_session");var parent=Root();using(var w=new NpImuGridUsb(p,Uid,()=>Sid,()=>parent)){w.Feed(Packet(Phase()),0,506);w.Feed(Packet(Frame(3),1),0,506);w.Complete(Window(parent));Need(w.Error==null,"Wrong optional phase session rejected NN/data");}Need(File.ReadAllText(Path.Combine(p,"analogin_encoding.txt")).Contains("grid_phase_qualified=False"),"Wrapper session was accepted as NPSTART/NSGP session");
    }
    static void UsbRecoveryRetention(){
        string p=Folder("usb_np_recovery_retention");var parent=Root();var phase=Phase(3,0,9);var source=Window(parent);
        using(var w=new NpImuGridUsb(p,Uid,()=>Sid,()=>parent)){
            w.Feed(Packet(phase),0,506);w.Feed(Packet(Frame(3),1),0,506);
            // The real caller supplies NP integrity at finalization, separately
            // from ordered grid delivery. A same-lifetime NP recovery cannot
            // withdraw the already-published root or relabel its initial phase.
            source.Probes[0].FrameErrors=1;
            w.Feed(Packet(Frame(9),2),0,506);w.Feed(Packet(Frame(12),3),0,506);
            Need(w.Error==null,"NP decoder failure gated later valid same-context grid delivery");
            w.Complete(source);Need(w.Error!=null&&w.Error.StartsWith("Final NP source window unavailable:"),"NP integrity loss gained phase qualification");
        }
        Need(new FileInfo(Path.Combine(p,"analogin.dat")).Length==15*32,"Later recovery grid rows were discarded or expanded incorrectly");
        var flags=File.ReadAllBytes(Path.Combine(p,"analogin_validity.dat"));Need(flags.Length==15&&flags.Count(n=>(n&1)!=0)==6&&flags.Skip(6).Take(3).All(n=>n==0)&&flags[12]==39&&flags[14]==31,"Observed recovery rows or explicit missing ordinals changed");
        Need(File.ReadAllBytes(Path.Combine(p,"imu_grid_phase.bin")).SequenceEqual(phase),"Historical once-only NSGP was rewritten after NP recovery");
        var text=File.ReadAllText(Path.Combine(p,"analogin_encoding.txt"));Need(text.Contains("grid_phase_qualified=False")&&text.Contains("NP_reference_phase_offset_us=unknown")&&text.Contains("terminal_tail_coverage=unknown"),"Recovery invented NP alignment or terminal coverage");
        Need(File.ReadAllText(Path.Combine(p,"imu_grid_gaps.csv")).Contains("6,3,transport_gap"),"Recovery gap lacks explicit ordinal metadata");
        foreach(string mode in new[]{"withdrawn","uid","token","generation","root_sequence","wrapper_session"}){
            p=Folder("usb_recovery_changed_"+mode);parent=Root();uint sid=Sid;
            using(var w=new NpImuGridUsb(p,Uid,()=>sid,()=>parent)){
                w.Feed(Packet(Frame(3)),0,506);
                if(mode=="withdrawn")parent=null;if(mode=="uid")parent=Root(uid:"001A0004434250062039314E");
                if(mode=="token")parent=Root(token:Token+1);if(mode=="generation")parent=Root(generation:10);
                if(mode=="root_sequence")parent=Root(sourceSequence:9);
                w.Feed(Packet(Frame(9),1,mode=="wrapper_session"?Sid+1:sid),0,506);Need(w.Error!=null,"Changed recovery context admitted: "+mode);
                parent=Root();sid=Sid;w.Feed(Packet(Frame(12),2),0,506);
            }
            Need(new FileInfo(Path.Combine(p,"analogin.dat")).Length==6*32,"Changed context discarded original rows or reopened rejected association: "+mode);
        }
    }
    static void StartOnly(){
        var rounding=typeof(NpImuGridWriter).GetMethod("FirstOffsetUs",BindingFlags.Static|BindingFlags.NonPublic);
        foreach(var c in new[]{Tuple.Create(Int64.MaxValue,1U,788322396312374000m,788322396312374001m),Tuple.Create(Int64.MaxValue,UInt32.MaxValue,183545610m,183545611m),Tuple.Create(11700000L,1U,1000000m,1000000m),Tuple.Create(11699999L,1U,999999m,1000000m),Tuple.Create(1L,UInt32.MaxValue,0m,1m)}){
            // Expected integer bounds were computed independently with Python
            // integer division of numerator*1e6/(denominator*sourceHz).
            Need((decimal)rounding.Invoke(null,new object[]{c.Item1,c.Item2,11700000U,false})==c.Item3&&(decimal)rounding.Invoke(null,new object[]{c.Item1,c.Item2,11700000U,true})==c.Item4,"First-slot microsecond bounds rounded inward/overflowed");
        }
        var parent=Root();string why;var source=Window(parent);Need(source.TryStart(parent,out why),"Saved source start did not bind");
        source.Probes[0].FrameErrors=1;source.Finalized=false;Need(source.TryStart(parent,out why),"Later NP framing failure erased independent saved startup proof");
        foreach(Action<NpImuGridSourceWindow> change in new Action<NpImuGridSourceWindow>[]{
            s=>s.StartFinalized=false,s=>s.OuterLossless=false,s=>s.Probes[0].HostDroppedBytes=1,s=>s.Probes[0].HostDropEvents=1,
            s=>s.Probes[0].FirstTick++,s=>s.Probes[0].FirstCounter++,s=>s.Probes[0].ApRows=0,s=>s.Probes[0].FramesChecked=0,
            s=>s.Probes=s.Probes.Take(1).ToArray(),s=>s.Probes[1].Port=1,s=>s.Mask=1,s=>s.ApHz=20000}){
            var bad=Window(parent);change(bad);Need(!bad.TryStart(parent,out why),"Unbound/unknown pre-start loss admitted as saved first source");
        }
        foreach(string mode in new[]{"start_only","later_np_error","lost_first","no_saved_start","missing_interval","wrong_counter"}){
            string p=Folder("usb_"+mode);source=Window(parent);byte[] phase=Phase(3,mode=="missing_interval"?0U:1U,9);
            if(mode=="later_np_error"){source.Probes[0].FrameErrors=1;source.Finalized=false;}
            if(mode=="no_saved_start")source.StartFinalized=false;if(mode=="wrong_counter")source.Probes[0].FirstCounter++;
            using(var w=new NpImuGridUsb(p,Uid,()=>Sid,()=>parent)){
                w.Feed(Packet(phase),0,506);w.Feed(Packet(Frame(mode=="lost_first"?4UL:3UL),1),0,506);w.Complete(source);
            }
            bool valid=mode=="start_only"||mode=="later_np_error";var text=File.ReadAllText(Path.Combine(p,"analogin_encoding.txt"));
            Need(text.Contains("first_slot_phase_valid="+valid)&&text.Contains("grid_phase_qualified=False")&&text.Contains("terminal_tail_coverage=unknown"),"Start-only and full lifetime qualification were conflated: "+mode);
            Need(text.Contains("NP_reference_phase_offset_us=unknown")&&text.Contains("np_sample_grid_locked=false"),"New start-only bound changed legacy whole-grid/sample-lock semantics");
            if(valid)Need(text.Contains("first_slot_offset_lower_us=1282")&&text.Contains("first_slot_offset_upper_us=1283")&&text.Contains("first_slot_phase_scope=start_only"),"Measured startup interval was hidden or rounded inward because jitter was unknown");
            Need(File.ReadAllBytes(Path.Combine(p,"imu_grid_phase.bin")).SequenceEqual(phase),"Start-only processing rewrote raw NSGP");
            Need(new FileInfo(Path.Combine(p,"analogin.dat")).Length==(mode=="lost_first"?7:6)*32,"Startup proof changed stored data extent");
        }
    }
    static void StartWithoutPoint(){
        foreach(string mode in new[]{"valid","missing_saved_start","lost_first","wrong_ordinal","bad_token","bad_crc","full_grid","foreground_point"}){
            string p=Folder("start_without_point_"+mode);var parent=mode=="foreground_point"?Root(firstPointKind:2):Root(sourcePoint:false);var source=Window(parent);
            var raw=Phase(mode=="wrong_ordinal"?4UL:3UL,mode=="full_grid"?3U:1U,9);if(mode=="bad_token"){Put64(raw,36,Token+1);Seal(raw);}if(mode=="bad_crc")raw[80]^=1;
            if(mode=="missing_saved_start")source.StartFinalized=false;
            using(var w=new NpImuGridUsb(p,Uid,()=>Sid,()=>parent)){w.Feed(Packet(raw),0,506);w.Feed(Packet(Frame(mode=="lost_first"?4UL:3UL),1),0,506);w.Complete(source);}
            var text=File.ReadAllText(Path.Combine(p,"analogin_encoding.txt"));Need(text.Contains("first_slot_phase_valid="+(mode=="valid"))&&text.Contains("grid_phase_qualified=False"),"Independent measured first interval/full-grid separation failed without POINT: "+mode);
            bool archived=mode=="valid"||mode=="missing_saved_start"||mode=="lost_first";
            Need(File.Exists(Path.Combine(p,"imu_grid_phase.bin"))==archived,"Missing POINT bypassed strict phase context/CRC/full-grid gate");
            Need(new FileInfo(Path.Combine(p,"analogin.dat")).Length==(mode=="lost_first"?7:6)*32,"POINT interoperability changed stored observations");
            var a=NpFirstSyncWire.Parse(parent.Receipt).Aux[0];if(mode!="foreground_point")Need((a.Flags&64)==0&&a.StampKind==0&&a.FirstMs==UInt32.MaxValue&&a.FirstCycles==UInt32.MaxValue,"Host fabricated legacy POINT/HALms evidence");
        }
        foreach(string mode in new[]{"counter","rate","width","source_clock","domain"}){
            string p=Folder("start_without_point_bad_"+mode);var raw=Receipt(9);raw[133]&=191;raw[135]=0;Put(raw,144,UInt32.MaxValue);Put(raw,148,UInt32.MaxValue);
            if(mode=="counter")Put64(raw,152,4);if(mode=="rate")Put(raw,160,200);if(mode=="width")raw[134]=32;if(mode=="source_clock")Put(raw,12,BitConverter.ToUInt32(raw,12)&~NpFirstSyncWire.SourceClock);if(mode=="domain")Put(raw,28,18);Seal(raw);
            var expected=NpImuGridWriter.PhaseBinding(Uid,Token,NpFirstSyncWire.Parse(Receipt(9)));
            using(var w=new NpImuGridWriter(p,9)){Need(!w.AcceptPhase(Phase(3,1,9),expected,11700000,NpFirstSyncWire.Parse(raw)),"Startup-only POINT exception weakened native/rate/source binding: "+mode);w.Feed(Frame(3));w.Complete(0,11700000,780,false,true);Need(!w.FirstSlotPhaseValid&&!w.PhaseQualified,"Bad native/source evidence acquired startup or full-grid confidence");}
        }
    }
    static void CallerOrder(string repository){
        string main=File.ReadAllText(Path.Combine(repository,"USB512_interface/USB512_interface/mainForm.cs")).Replace("\r\n","\n");
        string transport=File.ReadAllText(Path.Combine(repository,"USB512_interface/USB512_interface/XeNpWinUsb.cs"));
        int start=main.IndexOf("profile=device.StartPrepared(XeNpMcoDivider,delegate",StringComparison.Ordinal),end=main.IndexOf("\n                });",start,StringComparison.Ordinal);
        int timing=main.IndexOf("usbTiming=NpUsbTimingSession.Prepare",start,StringComparison.Ordinal),select=main.IndexOf("imuGridSelected=NpImuGridControl.PrepareUsb",StringComparison.Ordinal);
        int output=main.IndexOf("sensors=new NpUsbSensors(runFolder,true,devicePath,null,0,0,!imuGridSelected)",StringComparison.Ordinal);
        Need(start>=0&&timing>start&&select>timing&&output>select&&output<end,"NSGC selection/sensor ownership preceded stopped timing negotiation");
        Need(main.LastIndexOf("imuGridSelected=NpImuGridControl.PrepareUsb",StringComparison.Ordinal)==select,"Duplicate grid SELECT callsite");
        string callback=main.Substring(timing,end-timing);Need(callback.Contains("npRecordMiscSignals&&usbTiming!=null&&usbTiming.Enabled&&usbTiming.RtcEnabled"),"Grid requested without confirmed feature15 timing");
        Need(callback.IndexOf("if(!running)",StringComparison.Ordinal)<callback.IndexOf("imuGridSelected=",StringComparison.Ordinal)&&callback.LastIndexOf("if(!running)",StringComparison.Ordinal)>callback.IndexOf("sensors=new",StringComparison.Ordinal),"Stop cancellation does not bracket optional grid setup");
        Need(main.Contains("#if !NP_IMU_GRID_ENABLE || !NP_FIRST_SYNC_ENABLE\n                if(npRecordMiscSignals)sensors=new NpUsbSensors(runFolder,true,devicePath);"),"Default-off legacy sensor path changed");
        int invoke=transport.IndexOf("if(beforeStart!=null)beforeStart();",StringComparison.Ordinal),command=transport.IndexOf("VendorOut(RequestStart, 0, 0);",invoke,StringComparison.Ordinal);Need(invoke>=0&&command>invoke,"beforeStart callback executes after Start command");
        Need(transport.Contains("if(current.SessionId==previousSession)continue;")&&transport.Contains("expectedSession=current.SessionId;")&&transport.Contains("s.SessionId=BitConverter.ToUInt32(data,312);"),"Fresh status session source changed");
        int admission=main.IndexOf("device.CompleteStartupAdmission();",end,StringComparison.Ordinal),read=main.IndexOf("reader.ReadInto(buffer)",admission,StringComparison.Ordinal),enqueue=main.IndexOf("completed.Add(new ReceiveBlock(buffer,bytesRead",read,StringComparison.Ordinal);Need(admission>end&&read>admission&&enqueue>read,"Worker can process before StartPrepared fresh profile assignment/admission");
        Need(main.Contains("()=>profile.SessionId,()=>usbTiming==null?null:usbTiming.FineRoot"),"Grid wrapper bound to pre-Start/root generation instead of fresh firmware session");
        Need(main.Contains("imuGrid.Complete(new NpImuGridSourceWindow")&&main.Contains("FirstCounter=p.Decoder.FirstSuperframeCounter")&&main.Contains("FirstTick=p.Decoder.StartupDiscardedBytes")&&!main.Contains("p.Lossless&&p.RawBytes==gridEnd"),"Grid source-window caller regressed to global byte-zero Lossless or lacks first-position proof");
    }
    static Dictionary<string,string> TextFields(string path){return File.ReadAllLines(path).Where(s=>s.Contains("=")).Select(s=>s.Split(new[]{'='},2)).GroupBy(s=>s[0]).ToDictionary(g=>g.Key,g=>g.Last()[1]);}
    static long Number(Dictionary<string,string> d,string key){return Int64.Parse(d[key],CultureInfo.InvariantCulture);}
    static string Hash(string path){using(var hash=SHA256.Create())using(var input=File.OpenRead(path))return BitConverter.ToString(hash.ComputeHash(input)).Replace("-","");}
    static void SavedWindow(string folder){
        string[] files={"usb_timing_evidence.jsonl","usb_first_sync_receipt.bin","neuropixels_probes.txt","neuropixels_capture_status.txt","imu_grid_phase.bin","analogin_validity.dat","recording_start_utc.json"};var hashes=files.ToDictionary(s=>s,s=>Hash(Path.Combine(folder,s)));
        var history=NpFirstSyncUsbEvidence.Read(folder);Need(history!=null&&history.FineQualified&&history.Error==null,"Saved r4 source root not qualified from raw journal");var receipt=NpFirstSyncWire.Parse(history.FineRoot.Receipt);
        var capture=TextFields(Path.Combine(folder,"neuropixels_capture_status.txt"));var probes=TextFields(Path.Combine(folder,"neuropixels_probes.txt"));
        var window=new NpImuGridSourceWindow{Finalized=capture["complete"]=="True"&&capture["failure"]=="none",OuterLossless=probes["outer_lossless"]=="True",Mask=(uint)Number(probes,"probe_mask"),ApHz=(uint)Number(probes,"native_ap_sample_rate_hz_per_probe")};
        window.Probes=Enumerable.Range(1,3).Where(p=>(window.Mask&(1U<<(p-1)))!=0).Select(p=>{string pre="probe"+p+".";return new NpImuGridSourceWindow.Probe{Port=p,RawBytes=Number(probes,pre+"raw_bytes"),ApRows=Number(probes,pre+"ap_rows"),FramesChecked=Number(probes,pre+"validated_frames"),FirstTick=Number(probes,pre+"startup_discarded_bytes"),FirstCounter=(uint)Number(probes,pre+"first_superframe_counter"),HostDroppedBytes=Number(probes,pre+"host_dropped_lane_bytes"),HostDropEvents=Number(probes,pre+"host_drop_events"),FrameErrors=Number(probes,pre+"frame_errors"),CounterErrors=Number(probes,pre+"counter_errors"),GapEvents=Number(probes,pre+"gap_events"),Relocks=Number(probes,pre+"relocks"),RecoveryBytes=Number(probes,pre+"recovery_discarded_bytes"),MissingApRows=Number(probes,pre+"missing_ap_rows"),MissingLfpRows=Number(probes,pre+"missing_lfp_rows"),LfpPhaseErrors=Number(probes,pre+"lfp_phase_errors")};}).ToArray();
        ulong end;string error;Need(window.TryBound(history.FineRoot,out end,out error),"Actual r4 saved prefix was rejected: "+error);Need(end==938639831UL,"Actual r4 saved prefix end changed");
        var raw=File.ReadAllBytes(Path.Combine(folder,"imu_grid_phase.bin"));var phase=NpImuGridPhase.Read(raw,NpImuGridWriter.PhaseBinding(history.FineRoot.Uid,history.FineRoot.Token,receipt));var flags=File.ReadAllBytes(Path.Combine(folder,"analogin_validity.dat"));
        int first=Array.FindIndex(flags,b=>(b&1)!=0),last=Array.FindLastIndex(flags,b=>(b&1)!=0);Need(first>=0&&first==(long)phase.FirstAcceptedSlot&&last>=first,"Actual r4 first stored observation differs from NSGP");Need((phase.Flags&3)==3,"Actual r4 phase/jitter invalid");
        decimal lastUpper=(decimal)phase.UpperNumerator/phase.Denominator+(last-first)*(decimal)receipt.SourceHz/1250m+phase.MaximumJitter;Need(lastUpper<=end-receipt.ReferenceTick,"Actual r4 grid lies outside verified saved AP window");
        Need(files.All(s=>Hash(Path.Combine(folder,s))==hashes[s]),"Actual saved evidence changed");
        File.WriteAllText(Path.Combine(root,"saved-r4-window-replay.json"),new JavaScriptSerializer().Serialize(new{source=folder,source_window_qualified=true,common_saved_ap_end_tick=end,reference_tick=receipt.ReferenceTick,last_observed_slot=last,last_grid_upper_np_relative_tick=lastUpper,margin_source_ticks=(decimal)end-receipt.ReferenceTick-lastUpper,terminal_tail_coverage="unknown",raw_grid_carrier_replay=false,nn_payload_rescan=false,originals_unchanged=true,hashes,scope="Strict original source journal + per-port finalized decoder receipts + original NSGP and saved validity indices. No original NSG1 carrier archive or new hardware."}));
    }
    sealed class CardOverlay:Stream {
        readonly NpV1PowerCutRecoveryTests.SparseCard card;readonly Dictionary<long,byte[]> sectors;readonly string output,fault;bool injected;
        internal CardOverlay(NpV1PowerCutRecoveryTests.SparseCard card,bool proof,string output=null,string fault=null,byte[] receipt=null){this.card=card;this.output=output;this.fault=fault;sectors=new Dictionary<long,byte[]>();if(!proof)return;var c=new byte[512];Encoding.ASCII.GetBytes("N1SDUTC2").CopyTo(c,0);Put(c,8,2);Put(c,12,Sid);Put(c,72,15000);Put(c,76,1000);Seal(c);c=NpFirstSyncSd.BindSector(c,receipt??Receipt(),1,Uid,Token);sectors.Add((8192+5)*512L,c);sectors.Add((8192+6)*512L,NpFineClockWire.Companion(c,Anchor(),1,Sid,15000,Uid,Token));}
        public override int Read(byte[] b,int o,int n){if(fault!=null&&!injected&&Directory.Exists(output)){Directory.CreateDirectory(Path.Combine(output,fault));injected=true;}long at=card.Position;int count=card.Read(b,o,n);foreach(var s in sectors){long lo=Math.Max(at,s.Key),hi=Math.Min(at+count,s.Key+512);if(hi>lo)Buffer.BlockCopy(s.Value,(int)(lo-s.Key),b,o+(int)(lo-at),(int)(hi-lo));}return count;}
        public override bool CanRead{get{return true;}}public override bool CanSeek{get{return true;}}public override bool CanWrite{get{return false;}}public override long Length{get{return card.Length;}}public override long Position{get{return card.Position;}set{card.Position=value;}}public override long Seek(long n,SeekOrigin s){return card.Seek(n,s);}public override void Flush(){}public override void Write(byte[]b,int o,int n){throw new Exception("Source write");}public override void SetLength(long n){throw new Exception("Source write");}
    }
    static void Sd(){
        foreach(string mode in new[]{"valid","bad_inner","bad_phase","lost_first","bad_span","undeclared","no_parent","recovery","encoding_io","status_io","phase_io"}){
            string p=Path.Combine(root,"sd_"+mode);using(var c=new NpV1PowerCutRecoveryTests.SparseCard(8)){if(mode=="recovery")c.IncompleteFooter(8);else c.FinalizeFooter(8);Put(c.Header,104,NpImuGridWire.Magic);Put(c.Header,108,1);Put(c.Footer,104,NpImuGridWire.Magic);Put(c.Footer,108,1);Seal(c.Header);Seal(c.Footer);
                foreach(var item in c.Chunks){var d=item.Value;Put(d,61632+104,NpImuGridWire.Magic);Put(d,61632+108,1);var f=Frame((ulong)(item.Key+1)*3,9,mode=="lost_first"&&item.Key==0?(ushort)4:(ushort)5);int offset=0;if(item.Key==0){var ph=Phase();if(mode=="bad_phase")ph[80]^=1;ph.CopyTo(d,62144);offset=84;}f.CopyTo(d,62144+offset);Put(d,61632+124,(uint)(offset+f.Length));Put(d,61632+508,NpImuGridCarrier.Crc(d,61632,508));}
                if(mode=="bad_inner")c.Chunks[2][62144+44]^=1;if(mode=="bad_span")c.Descriptor(2,124,2881);if(mode=="undeclared")c.Chunks[2][65023]=1;
                var before=c.Chunks.Values.SelectMany(n=>n).ToArray();using(var input=new CardOverlay(c,mode!="no_parent",p,mode=="encoding_io"?"analogin_encoding.txt":mode=="status_io"?"imu_grid_status.txt":mode=="phase_io"?"imu_grid_phase.bin":null)){
                    Action export=()=>{if(mode=="recovery")NpCe64Export.ExportPowerInterruptedPrefix(input,8192,c.ReservedEnd,p,null,null);else NpCe64Export.Export(input,8192,c.ReservedEnd,p,null,null);};
                    if(mode=="bad_span"||mode=="undeclared"){Reject(export,"Global SD geometry/padding was relaxed");continue;}export();
                }
                Need(c.Chunks.Values.SelectMany(n=>n).SequenceEqual(before),"SD source mutation");Need(new FileInfo(Path.Combine(p,"analogin.dat.legacy_chunk")).Length==4096,"Legacy misc was overwritten/lost");
                if(mode=="no_parent")Need(!File.Exists(Path.Combine(p,"analogin.dat")),"Missing parent generated locked grid");else Need(File.Exists(Path.Combine(p,"analogin.dat")),"Declared bound grid missing");
                string status=File.ReadAllText(Path.Combine(p,"capture_status.txt"));if(mode!="recovery")Need(status.Contains("complete=True"),"Optional inner grid failure rejected NN");Need(status.Contains("np1_ap_rows=")&&status.Contains("np2_ap_rows="),"NN decode path not exercised");
                Need(!File.Exists(Path.Combine(p,"sensors_fine.bin")),"New no-SIF recording made empty fine archive");
                if(mode!="no_parent"&&mode!="encoding_io")Need(File.ReadAllText(Path.Combine(p,"analogin_encoding.txt")).Contains("grid_phase_qualified="+(mode=="valid"||mode=="status_io")),"Actual SD phase/stored first ordinal qualification: "+mode);
            }
        }
    }
    static void SdStartOnly(){foreach(string mode in new[]{"valid","later_np_error","wrong_counter","loss_order_unknown","lost_first"}){
        string p=Path.Combine(root,"sd_start_only_"+mode);using(var c=new NpV1PowerCutRecoveryTests.SparseCard(8)){
            c.FinalizeFooter(8);Put(c.Header,104,NpImuGridWire.Magic);Put(c.Header,108,1);Put(c.Footer,104,NpImuGridWire.Magic);Put(c.Footer,108,1);
            if(mode=="loss_order_unknown"){Encoding.ASCII.GetBytes("NPLOSS01").CopyTo(c.Footer,320);Put(c.Footer,328,1);Put(c.Footer,332,1);}Seal(c.Header);Seal(c.Footer);
            foreach(var item in c.Chunks){var d=item.Value;Put(d,61632+104,NpImuGridWire.Magic);Put(d,61632+108,1);int n=0;if(item.Key==0){var ph=Phase(3,1);ph.CopyTo(d,62144);n=ph.Length;}var f=Frame((ulong)(item.Key+1)*3,9,mode=="lost_first"&&item.Key==0?(ushort)4:(ushort)5);f.CopyTo(d,62144+n);Put(d,61632+124,(uint)(n+f.Length));Put(d,61632+508,NpImuGridCarrier.Crc(d,61632,508));}
            if(mode=="later_np_error")c.BadSync(4,10);
            var r=Receipt();Put64(r,52,0);for(int port=0;port<2;port++){int at=64+port*20;Put64(r,at,0);Put(r,at+8,mode=="wrong_counter"?13U:0U);r[at+12]=r[at+13]=0;}Seal(r);
            var before=c.Chunks.Values.SelectMany(n=>n).ToArray();using(var input=new CardOverlay(c,true,receipt:r))NpCe64Export.Export(input,8192,c.ReservedEnd,p,null,null);
            Need(c.Chunks.Values.SelectMany(n=>n).SequenceEqual(before),"Startup-only SD export mutated original NN/header bytes");
            string text=File.ReadAllText(Path.Combine(p,"analogin_encoding.txt"));bool valid=mode=="valid"||mode=="later_np_error";
            Need(text.Contains("first_slot_phase_valid="+valid)&&text.Contains("grid_phase_qualified=False")&&text.Contains("terminal_tail_coverage=unknown"),"SD initial binding or startup-vs-lifetime split failed: "+mode);
            Need(File.ReadAllText(Path.Combine(p,"capture_status.txt")).Contains("complete=True")&&new FileInfo(Path.Combine(p,"analogin.dat")).Length>0,"Optional startup qualification rejected neural/grid data");
        }
    }}
    static int Main(string[] args){try{root=Path.Combine(args[0],"grid-"+Guid.NewGuid().ToString("N").Substring(0,8));Directory.CreateDirectory(root);Controls();Carriers();Writers();Phases();UnknownPhases();SourceWindows();Usb();UsbRecoveryRetention();StartOnly();StartWithoutPoint();Sd();SdStartOnly();CallerOrder(args[1]);if(args.Length>2)SavedWindow(args[2]);var legacy=typeof(NpV1SdOptionalSensorTests).GetMethod("Main",BindingFlags.Static|BindingFlags.NonPublic);Need((int)legacy.Invoke(null,new object[]{new[]{root}})==0,"Legacy optional sensor regressions");Console.WriteLine("PASS "+checks+" NSG1/NSGP actual V1 control/carrier/writer/USB parent/SD export and caller-order checks; no hardware.");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
