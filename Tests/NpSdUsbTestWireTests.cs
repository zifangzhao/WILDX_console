using System;
using System.IO;
using System.Linq;
using USB512_interface;

internal static class NpSdUsbTestWireTests {
    const string Uid1="001D00065942500B20313354",Uid2="001A0004434250062039314E";
    const ulong Ticket=0x1020304050607080UL;
    const uint Capacity=1000000000,Frontier=65536,Blocks=93752;
    static int checks;
    static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static void Reject(Action a,string why){try{a();}catch(InvalidDataException){checks++;return;}throw new Exception(why);}
    static void Put(byte[] b,int at,uint x){Buffer.BlockCopy(BitConverter.GetBytes(x),0,b,at,4);}
    static void Put64(byte[] b,int at,ulong x){Buffer.BlockCopy(BitConverter.GetBytes(x),0,b,at,8);}
    static uint U(byte[] b,int at){return BitConverter.ToUInt32(b,at);}
    static uint Crc(byte[] b,int count){uint c=0xffffffff;for(int n=0;n<count;n++){c^=b[n];for(int k=0;k<8;k++)c=(c>>1)^((c&1)!=0?0xedb88320U:0U);}return ~c;}
    static void Seal(byte[] b){Put(b,b.Length-4,Crc(b,b.Length-4));}
    static void SealPlan(byte[] b){var d=b.Take(60).Concat(b.Skip(64)).ToArray();Put(b,60,Crc(d,d.Length));}
    static byte[] Change(byte[] b,int at,uint n){var c=(byte[])b.Clone();Put(c,at,n);Seal(c);return c;}
    static string Uid(int family){return family==1?Uid1:Uid2;}
    static void PutUid(byte[] b,int at,string uid){for(int n=0;n<3;n++)Put(b,at+n*4,Convert.ToUInt32(uid.Substring(n*8,8),16));}
    static byte[] Inner(int family){
        var l=new byte[64];Put(l,0,family==1?0x31444c4eU:0x32444c4eU);Put(l,4,(uint)family);Put(l,8,2);Put(l,12,57);
        Put(l,16,family==1?3U:7U);Put(l,20,20000);Put(l,24,family==1?Frontier:Blocks);
        if(family==1){Put(l,28,Frontier+1000000);Put(l,32,0x55667788);Put(l,36,1);}
        Seal(l);return family==1?NpFirstSyncSd.WrapLegacyFineCounter64(l,family,Uid(family),Ticket):NpFirstSyncSd.WrapLegacyGrid16Append(l,family,Uid(family),Ticket);
    }
    static byte[] Report(int family){
        var b=Enumerable.Repeat((byte)255,256).ToArray();Put(b,0,0x3152514e);Put(b,4,1);Put(b,8,256);Put(b,12,NpSdUsbTestWire.Capability);Put(b,16,0);Put(b,20,0);
        Put(b,24,0x2f);Put(b,28,0);Put64(b,32,0);Put(b,48,(uint)family);PutUid(b,68,Uid(family));Put(b,80,Capacity);
        for(int n=0;n<4;n++)Put(b,84+n*4,0x12345678+(uint)n*0x11111111);
        Put(b,100,Frontier);Put(b,104,0);Put(b,108,family==1?0x0000fff0U:0xfffffff0U);Put(b,112,120);Put(b,120,family==1?3U:7U);Put(b,124,20000);
        Seal(b);return b;
    }
    static byte[] Finished(byte[] plan,int family){
        var b=Report(family);Put(b,16,4);Put(b,20,1);Put(b,24,0x7fff);Put64(b,32,Ticket);Put(b,40,U(plan,60));Put(b,44,57);
        Put(b,52,U(plan,96));Put(b,56,U(plan,100));Put(b,60,U(plan,52));Put(b,64,Blocks);Put(b,116,Frontier);
        Put(b,128,0x55667788);Put(b,132,Frontier);Put(b,136,Frontier+2000000);Put(b,140,Frontier+8);Put(b,144,Frontier+1000000);
        Put(b,148,Blocks);Put(b,152,Blocks);Put(b,156,Blocks);Put(b,160,Blocks);
        for(int n=41;n<=54;n++)Put(b,n*4,0);
        Put(b,164,5);Put(b,208,120000);Put(b,212,20);Put(b,216,23438);Put64(b,220,20000000);Put(b,228,2000);Put(b,232,9000);Put(b,236,48000000);Put(b,240,128000000);Put(b,244,0);Put(b,248,0);Seal(b);return b;
    }
    static byte[] ExpectedPlan(byte[] r,byte[] inner,uint seconds){
        var b=new byte[128];Put(b,0,0x3154514e);Put(b,4,1);Put64(b,8,Ticket);Buffer.BlockCopy(r,68,b,16,12);
        Put(b,28,U(r,80));Buffer.BlockCopy(r,84,b,32,16);Put(b,48,U(r,100));Put(b,52,seconds);Buffer.BlockCopy(inner,0,b,64,64);SealPlan(b);return b;
    }
    static void Native(string input,string output){
        var plan=File.ReadAllBytes(Path.Combine(input,"plan128.bin"));var raw=File.ReadAllBytes(Path.Combine(input,"report256.bin"));
        Check(plan.Length==128 && raw.Length==256,"native fixed extents");var r=NpSdUsbTestWire.ParseReport(raw,2,Uid2);
        Check(r.Raw.SequenceEqual(raw),"native256 exact preserved bytes");Check(NpSdUsbTestWire.ValidateMatch(plan,r),"native report matches valid finite plan");
        var idle=Report(2);Buffer.BlockCopy(plan,16,idle,68,12);Put(idle,80,U(plan,28));Buffer.BlockCopy(plan,32,idle,84,16);Put(idle,100,U(plan,48));Seal(idle);
        var inner=plan.Skip(64).ToArray();var built=NpSdUsbTestWire.BuildPlan(NpSdUsbTestWire.ParseReport(idle,2,Uid2),inner,U(plan,52),(U(plan,56)&1)!=0);
        Check(built.SequenceEqual(plan),"native128 byte-exact managed BuildPlan");
        Check(r.TargetNeuralBlocks==U(inner,40) && r.Mask==7 && r.Hz==20000 && r.Seconds==120,"native immutable plan named fields");
        Check(r.Uid==Uid2 && r.Capacity==U(plan,28) && r.Cid.SequenceEqual(new[]{U(plan,32),U(plan,36),U(plan,40),U(plan,44)}),"native exact card tuple");
        var positional=new byte[256];for(int n=0;n<63;n++)Put(positional,n*4,0x01000000U+(uint)n);Put(positional,0,0x3152514e);Put(positional,4,1);Put(positional,8,256);Put(positional,16,4);Put(positional,20,1);Put(positional,24,0xffff);Seal(positional);
        Reject(()=>NpSdUsbTestWire.ParseReport(positional,2,Uid2),"structural positional report is not device identity evidence");
        File.WriteAllBytes(Path.Combine(output,"managed-native-plan.bin"),built);File.WriteAllBytes(Path.Combine(output,"managed-native-report.bin"),r.Raw);
    }
    static void Family(int family,string folder){
        string uid=Uid(family);var raw=Report(family);var r=NpSdUsbTestWire.ParseReport(raw,family,uid.ToLowerInvariant());var inner=Inner(family);
        Check(r.Family==family && r.Uid==uid && r.Capacity==Capacity && r.Frontier==Frontier,"named identity fields");
        Check(r.RecordMilliseconds==null && r.StoredNeuralBlocks==null && r.MissingTicks==null && r.DiscardedTicks==null,"unknown counters remain unknown");
        Check(r.PriorMask==(family==1?3U:7U) && !r.Has(NpSdUsbTestValidity.PriorProfileRestored),"prior profile is separate from restoration claim");
        foreach(uint hz in new[]{7500U,15000U,20000U,30000U})Check(r.Supports(family==1?3U:7U,hz),"nibble profile support");
        Check(!r.Supports(0,20000) && !r.Supports(8,20000) && !r.Supports(3,10000),"bad profile does not shift outside nibble");
        var legacyCapability=NpSdUsbTestWire.ParseReport(Change(raw,12,0x40),family,uid);
        Check(!legacyCapability.Supports(family==1?3U:7U,20000),"legacy APP-update capability is not SD-test support");
        Reject(()=>NpSdUsbTestWire.BuildPlan(legacyCapability,inner,30),"legacy APP-update capability cannot build SD-test plan");
        var plan=NpSdUsbTestWire.BuildPlan(r,inner,120);Check(plan.SequenceEqual(ExpectedPlan(raw,inner,120)),"independent exact128-byte plan");
        Check(plan.Skip(64).SequenceEqual(inner),"inner frame byte-identical");
        Check(U(plan,60)!=Crc(plan,60),"outer CRC covers inner64 after skipping CRC word");
        var complete=Finished(plan,family);var c=NpSdUsbTestWire.ParseReport(complete,family,uid);
        Check(NpSdUsbTestWire.ValidateMatch(plan,c),"matched complete report");
        Check(c.Outcome==NpSdUsbTestOutcome.DeviceCompleteReadbackRequired && c.StoredNeuralBlocks==Blocks && c.LogicalPrefix==Blocks && c.StoredHighWater==Blocks,"device-complete still requires readback");
        Check(c.TotalWriteMicroseconds==20000000 && c.MaximumServiceMicroseconds==9000 && c.RecordMilliseconds==120000 && c.SdHz==48000000,"performance fields and units");
        Check(c.AuxQueueDrops==0 && c.MissingTicks==0 && c.SourceGaps==0 && c.DiscardedTicks==0,"known genuine zeros");
        var removed=(byte[])complete.Clone();Put(removed,24,U(removed,24)&~3U);Put(removed,20,7);for(int n=20;n<=26;n++)Put(removed,n*4,0);Seal(removed);
        var removedCard=NpSdUsbTestWire.ParseReport(removed,family,uid);
        Check(NpSdUsbTestWire.ValidateMatch(plan,removedCard) && removedCard.Capacity==null && removedCard.Cid==null && removedCard.Frontier==null && removedCard.Outcome==NpSdUsbTestOutcome.StorageIo,"unknown card still correlates terminal storage failure without SameCard claim");
        for(int n=0;n<raw.Length;n++){var bad=(byte[])raw.Clone();bad[n]^=1;Reject(()=>NpSdUsbTestWire.ParseReport(bad,family,uid),"report corruption "+n);}
        for(int n=0;n<plan.Length;n++){var bad=(byte[])plan.Clone();bad[n]^=1;Check(!NpSdUsbTestWire.ValidateMatch(bad,c),"plan corruption "+n);}
        foreach(int at in new[]{0,4,8,48,68,72,76,112})Reject(()=>NpSdUsbTestWire.ParseReport(Change(raw,at,0xffffffff),family,uid),"resealed header/identity "+at);
        Reject(()=>NpSdUsbTestWire.ParseReport(Change(raw,16,7),family,uid),"unknown phase");Reject(()=>NpSdUsbTestWire.ParseReport(Change(raw,20,11),family,uid),"unknown outcome");
        Reject(()=>NpSdUsbTestWire.ParseReport(Change(raw,24,0x10000),family,uid),"unknown validity bit");
        Reject(()=>NpSdUsbTestWire.ParseReport(raw,3,uid),"unsupported family");Reject(()=>NpSdUsbTestWire.ParseReport(raw,family,Uid(family==1?2:1)),"wrong expected UID");
        Reject(()=>NpSdUsbTestWire.ParseReport(new byte[255],family,uid),"short report");Reject(()=>NpSdUsbTestWire.ParseReport(new byte[257],family,uid),"long report");
        Reject(()=>NpSdUsbTestWire.ParseReport(raw,family,new string('0',24)),"zero UID");Reject(()=>NpSdUsbTestWire.ParseReport(raw,family,new string('F',24)),"ones UID");
        Reject(()=>NpSdUsbTestWire.BuildPlan(r,inner,0),"zero seconds");Reject(()=>NpSdUsbTestWire.BuildPlan(r,inner,121),"too long");
        Reject(()=>NpSdUsbTestWire.BuildPlan(null,inner,30),"missing report");Reject(()=>NpSdUsbTestWire.BuildPlan(r,null,30),"missing inner");
        foreach(uint flag in new[]{1U,2U,32U}){
            var b=Change(raw,24,U(raw,24)&~flag);var q=NpSdUsbTestWire.ParseReport(b,family,uid);Reject(()=>NpSdUsbTestWire.BuildPlan(q,inner,30),"required card/catalog/availability bit");
        }
        foreach(uint phase in new[]{1U,2U,3U,6U}){
            var b=Change(raw,16,phase);var q=NpSdUsbTestWire.ParseReport(b,family,uid);Reject(()=>NpSdUsbTestWire.BuildPlan(q,inner,30),"active/unresolved attempt");
        }
        foreach(uint phase in new[]{1U,2U,3U}){
            var q=NpSdUsbTestWire.ParseReport(Change(raw,16,phase),family,uid);Reject(()=>NpSdUsbTestWire.BuildPlan(q,inner,30,true),"manual reconcile never replaces active attempt");
        }
        var virgin=Change(Change(raw,16,6),20,10);var interrupted=NpSdUsbTestWire.ParseReport(virgin,family,uid);
        var explicitPlan=NpSdUsbTestWire.BuildPlan(interrupted,inner,120,true);var expectedExplicit=ExpectedPlan(raw,inner,120);Put(expectedExplicit,56,1);SealPlan(expectedExplicit);
        Check(explicitPlan.SequenceEqual(expectedExplicit) && !interrupted.Has(NpSdUsbTestValidity.ClosedTerminal) && !interrupted.Has(NpSdUsbTestValidity.PriorProfileRestored),"explicit virgin reconciliation is new fresh readiness, not old terminal/restoration proof");
        Check(NpSdUsbTestWire.ValidateMatch(explicitPlan,NpSdUsbTestWire.ParseReport(Finished(explicitPlan,family),family,uid)),"explicit flag1 accepted in correlated plan");
        foreach(uint quiet in new[]{4U,8U}){
            var q=NpSdUsbTestWire.ParseReport(Change(virgin,24,U(virgin,24)&~quiet),family,uid);Reject(()=>NpSdUsbTestWire.BuildPlan(q,inner,120,true),"unknown reconciliation requires source and SD quiet");
        }
        var notWritable=NpSdUsbTestWire.ParseReport(Change(virgin,104,1),family,uid);Reject(()=>NpSdUsbTestWire.BuildPlan(notWritable,inner,120,true),"unknown reconciliation requires clean writable catalog");
        Reject(()=>NpSdUsbTestWire.BuildPlan(c,inner,120,true),"explicit request cannot reuse old ticket");
        var freshInner=(byte[])inner.Clone();Put64(freshInner,24,Ticket+1);Seal(freshInner);Check(NpSdUsbTestWire.BuildPlan(c,freshInner,120).Length==128,"fresh completed-attempt successor uses a distinct ticket");
        var invalidFlags=(byte[])explicitPlan.Clone();Put(invalidFlags,56,2);SealPlan(invalidFlags);var invalidFlagsReport=Finished(invalidFlags,family);
        Check(!NpSdUsbTestWire.ValidateMatch(invalidFlags,NpSdUsbTestWire.ParseReport(invalidFlagsReport,family,uid)),"unknown envelope flag rejected even with matching plan CRC");
        var smallMax=NpSdUsbTestWire.ParseReport(Change(raw,112,30),family,uid);Check(NpSdUsbTestWire.BuildPlan(smallMax,inner,30).Length==128,"advertised smaller maximum");Reject(()=>NpSdUsbTestWire.BuildPlan(smallMax,inner,31),"advertised maximum enforced");
        var noCard=Change(raw,24,U(raw,24)&~1U);for(int n=20;n<=24;n++)Put(noCard,n*4,0);Seal(noCard);var absent=NpSdUsbTestWire.ParseReport(noCard,family,uid);
        Check(absent.Uid==uid && absent.Capacity==null && absent.Cid==null,"MCU identity known when card placeholders are zero");
        Reject(()=>NpSdUsbTestWire.ParseReport(Change(noCard,68,0),family,uid),"MCU UID remains required without card");
        var unknown=(byte[])raw.Clone();for(int n=37;n<=62;n++)Put(unknown,n*4,0);Seal(unknown);var zero=NpSdUsbTestWire.ParseReport(unknown,family,uid);
        Check(zero.StoredNeuralBlocks==null && zero.AdmittedHighWater==null && zero.Drops==null && zero.Overflows==null && zero.SourceGaps==null && zero.MissingTicks==null && zero.DiscardedTicks==null && zero.WriteCount==null && zero.TotalWriteMicroseconds==null && zero.AuxSchedulerMisses==null,"unknown group zero placeholders not fabricated data");
        Check(zero.RecordMilliseconds==0 && zero.DrainMilliseconds==0 && zero.AppState==0,"individual nullable fields are independent");
        foreach(int at in new[]{148,152,156,160,180,184,188,208+8,228,232,236,240,244,248})Reject(()=>NpSdUsbTestWire.ParseReport(Change(complete,at,0xffffffff),family,uid),"declared known sentinel "+at);
        var bad64=(byte[])complete.Clone();Put64(bad64,192,UInt64.MaxValue);Seal(bad64);Reject(()=>NpSdUsbTestWire.ParseReport(bad64,family,uid),"known missing64 sentinel");
        bad64=(byte[])complete.Clone();Put64(bad64,200,UInt64.MaxValue);Seal(bad64);Reject(()=>NpSdUsbTestWire.ParseReport(bad64,family,uid),"known discarded64 sentinel");
        bad64=(byte[])complete.Clone();Put64(bad64,220,UInt64.MaxValue);Seal(bad64);Reject(()=>NpSdUsbTestWire.ParseReport(bad64,family,uid),"known performance64 sentinel");
        var lowMax=(byte[])complete.Clone();Put64(lowMax,192,UInt32.MaxValue);Seal(lowMax);Check(NpSdUsbTestWire.ParseReport(lowMax,family,uid).MissingTicks==UInt32.MaxValue,"valid64 counter low word all ones");
        Reject(()=>NpSdUsbTestWire.ParseReport(Change(complete,156,Blocks+1),family,uid),"P greater than N");Reject(()=>NpSdUsbTestWire.ParseReport(Change(complete,160,Blocks-1),family,uid),"H less than N");Reject(()=>NpSdUsbTestWire.ParseReport(Change(complete,152,Blocks-1),family,uid),"A less than H");
        Reject(()=>NpSdUsbTestWire.ParseReport(Change(complete,120,0),family,uid),"false restored profile");
        var badInner=(byte[])inner.Clone();badInner[40]^=1;Reject(()=>NpSdUsbTestWire.BuildPlan(r,badInner,30),"bad inner CRC");
        foreach(int at in new[]{8,12}){var wrong=Change(inner,at,0);Reject(()=>NpSdUsbTestWire.BuildPlan(r,wrong,30),"inner id/UID "+at);}
        var zeroToken=(byte[])inner.Clone();Put64(zeroToken,24,0);Seal(zeroToken);Reject(()=>NpSdUsbTestWire.BuildPlan(r,zeroToken,30),"zero64 inner token");
        var wrongVersion=Change(inner,4,0x00010000U+(family==1?2U:4U));Reject(()=>NpSdUsbTestWire.BuildPlan(r,wrongVersion,30),"wrong negotiated version");
        var notStart=Change(inner,4,(family==1?3U:5U));Reject(()=>NpSdUsbTestWire.BuildPlan(r,notStart,30),"query is not Start");
        var scheduled=Change(inner,4,0x00030000U+(family==1?3U:5U));Reject(()=>NpSdUsbTestWire.BuildPlan(r,scheduled,30),"scheduled Start not accepted");
        Reject(()=>NpSdUsbTestWire.BuildPlan(r,Change(inner,32,0),30),"empty mask");Reject(()=>NpSdUsbTestWire.BuildPlan(r,Change(inner,36,1250),30),"bad AP Hz");
        Reject(()=>NpSdUsbTestWire.BuildPlan(NpSdUsbTestWire.ParseReport(Change(raw,108,0),family,uid),inner,30),"unsupported profile nibble");
        if(family==1){Reject(()=>NpSdUsbTestWire.BuildPlan(r,Change(inner,44,0xffffffff),30),"V1 unbounded EndLBA");Reject(()=>NpSdUsbTestWire.BuildPlan(r,Change(inner,44,Frontier),30),"V1 empty range");Reject(()=>NpSdUsbTestWire.BuildPlan(r,Change(inner,44,Capacity+1),30),"V1 beyond card");}
        else {Reject(()=>NpSdUsbTestWire.BuildPlan(r,Change(inner,40,0),30),"V2 continuous blocks not accepted");Reject(()=>NpSdUsbTestWire.BuildPlan(r,Change(inner,40,0xffffffff),30),"V2 unbounded blocks");}
        foreach(int at in new[]{32,40,44,52,56,60,80,84,116}){
            var mismatch=Change(complete,at,U(complete,at)^1);NpSdUsbTestReport x;
            try{x=NpSdUsbTestWire.ParseReport(mismatch,family,uid);}catch(InvalidDataException){checks++;continue;}
            Check(!NpSdUsbTestWire.ValidateMatch(plan,x),"valid resealed wrong report binding "+at);
        }
        foreach(int at in new[]{0,4,8,16,28,32,48,52,56}){var changed=(byte[])plan.Clone();Put(changed,at,U(changed,at)^1);SealPlan(changed);Check(!NpSdUsbTestWire.ValidateMatch(changed,c),"resealed outer mismatch "+at);}
        var copy=c.Raw;copy[24]=0;var cid=c.Cid;cid[0]^=1;complete[24]=0;Check(NpSdUsbTestWire.ValidateMatch(plan,c),"immutable raw/CID/original input");
        File.WriteAllBytes(Path.Combine(folder,"managed-v"+family+"-plan.bin"),plan);File.WriteAllBytes(Path.Combine(folder,"managed-v"+family+"-report.bin"),c.Raw);
    }
    static int Main(string[] args){
        try{
            if(args.Length==5 && args[0]=="native"){
                int f=Int32.Parse(args[3]);var r=NpSdUsbTestWire.ParseReport(File.ReadAllBytes(args[1]),f,args[4]);Check(NpSdUsbTestWire.ValidateMatch(File.ReadAllBytes(args[2]),r),"native report/plan exact match");
            }else{
                string folder=args[0];Directory.CreateDirectory(folder);Check(NpSdUsbTestWire.GetReportRequest==0xa3 && NpSdUsbTestWire.SubmitRequest==0xa4 && NpSdUsbTestWire.Capability==0x10000,"allocated request directions and noncolliding capability");
                Family(1,folder);Family(2,folder);
                if(args.Length>1)Native(args[1],folder);
                File.WriteAllText(Path.Combine(folder,"checks.json"),"{\"status\":\"PASS\",\"checks\":"+checks+",\"hardware_access\":false}");
            }
            Console.WriteLine("PASS "+checks+" SD USB test wire checks");return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
