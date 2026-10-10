using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using USB512_interface;
using CE32_console;

// File-only adversarial fixtures for both universal NP1 decoder entry points.
internal static class Np1StartupTests
{
    static int checks;
    static void Check(bool value,string reason) { checks++;if(!value)throw new Exception(reason); }
    static void Reject(Action action,string reason) {
        try { action(); } catch(IOException) { checks++;return; }
        throw new Exception(reason);
    }
    sealed class Decoder
    {
        readonly XeNpStreamDecoder live;
        readonly NpCe64Decoder sd;
        internal Decoder(bool disk,int layout) {
            if(disk)sd=new NpCe64Decoder((NpCe64LaneLayout)layout);
            else live=new XeNpStreamDecoder((XeNpLaneLayout)layout);
        }
        internal long Frames { get {return live!=null?live.FramesChecked:sd.FramesChecked;} }
        internal long Discarded { get {return live!=null?live.StartupDiscardedBytes:sd.StartupDiscardedBytes;} }
        internal long Errors { get {return live!=null?live.FrameErrors+live.CounterErrors:sd.FrameErrors+sd.CounterErrors;} }
        internal long PhaseErrors { get {return live!=null?live.LfpPhaseErrors:sd.LfpPhaseErrors;} }
        internal long PartialLfp { get {return live!=null?live.StartupDiscardedLfpSuperframes:sd.StartupDiscardedLfpSuperframes;} }
        internal int Slot { get {return live!=null?live.InitialLfpSlot:sd.InitialLfpSlot;} }
        internal ushort Model { get {return live!=null?live.ExpectedFixedWord:sd.ExpectedFixedWord;} }
        internal void Reset() {if(live!=null)live.DeclareRecordingReset();else sd.DeclareRecordingReset();}
        internal void Fixed(ushort value) {if(live!=null)live.DeclareFixedWord(value);else sd.DeclareFixedWord(value);}
        internal void Quad() {if(live!=null)live.UseV1QuadFormat();else sd.UseV1QuadFormat();}
        internal void Complete() {if(live!=null)live.Complete();else sd.Complete();}
        internal void Feed(byte[] b,int at,int n,Action<short[],int,short[],int> receive) {
            if(live!=null)live.Feed(b,at,n,receive);else sd.Feed(b,at,n,receive);
        }
        internal void Feed(byte[] b,Action<short[],int,short[],int> receive) {Feed(b,0,b.Length,receive);}
    }
    static void Word(byte[] b,int at,int line,int word,uint value) {
        for(int bit=0;bit<10;bit++)if((value&(1U<<(9-bit)))!=0)b[at+word*10+bit]|=(byte)(1<<line);
    }
    static int Channel(int adc,int slot) {return (adc/2)*24+(adc&1)+slot*2;}
    static byte[] Fixture(int layout,int first,int rows,ushort fixedWord=0x11,uint? counter=null) {
        var b=new byte[rows*780];
        for(int row=0;row<rows;row++)for(int f=0;f<13;f++) {
            int at=(row*13+f)*60;
            uint c=((counter??(uint)(first*13))+(uint)(row*13+f))&0xFFFFF;
            Word(b,at,0,0,f==0?0x330U:0xCFU);
            Word(b,at,7,2,fixedWord);Word(b,at,7,3,c>>10);Word(b,at,7,4,c&1023);
            // Exercise both legal heartbeat levels and one transition.
            if((row+f)%2==1)for(int t=31;t<60;t++)b[at+t]|=1;
            for(int adc=0;adc<32;adc++) {
                int slot=f==0?(first+row)%12:f-1;
                int line=adc>=30?7:layout==3?adc/5+1:adc%6+1;
                int word=adc>=30?adc-30:layout==3?adc%5:adc/6;
                Word(b,at,line,word,(uint)(adc*17+slot));
            }
        }
        if(layout==2)for(int i=0;i<b.Length;i++)b[i]=(byte)((b[i]>>1)|(b[i]<<7));
        return b;
    }
    static void FragmentedStarts(bool disk,int layout,int skip,int fragment) {
        var d=new Decoder(disk,layout);d.Reset();d.Fixed(0x11);
        byte[] b=Fixture(layout,2,36);int ap=0,lfp=0;
        Action<short[],int,short[],int> receive=(a,na,l,nl)=> {
            ap+=na;lfp+=nl;
            for(int adc=0;adc<32;adc++)for(int slot=0;slot<12;slot++) {
                Check(a[Channel(adc,slot)]==512-(adc*17+slot),"AP bit/word/channel mapping");
                if(nl>0)Check(l[Channel(adc,slot)]==512-(adc*17+slot),"LFP slot/word/channel mapping");
            }
        };
        for(int at=skip;at<b.Length;at+=fragment)d.Feed(b,at,Math.Min(fragment,b.Length-at),receive);
        d.Complete();
        int lost=(skip+779)/780,first=2+lost,partial=(12-first%12)%12;
        Check(ap==36-lost && lfp==(36-lost-partial)/12,"AP/LFP partial-start row counts");
        Check(d.Discarded==(780-skip%780)%780 && d.Slot==first%12 && d.PartialLfp==partial,"Startup byte and reset phase");
        Check(d.Errors==0 && d.PhaseErrors==0,"Valid fragmented recording rejected");
    }
    static void FalseSingleSuperframe(bool disk) {
        var d=new Decoder(disk,3);d.Reset();int ap=0;
        Action<short[],int,short[],int> receive=(a,na,l,nl)=>ap+=na;
        d.Feed(Fixture(3,0,1),receive);
        Check(ap==0 && d.Frames==0,"A single startup superframe emitted samples");
        byte[] good=Fixture(3,5,4);d.Feed(good,0,780,receive);
        Check(ap==0 && d.Frames==0,"Discontinuous startup superframes locked");
        d.Feed(good,780,good.Length-780,receive);d.Complete();
        Check(ap==4 && d.Discarded==780 && d.Slot==5,"False startup candidate was not discarded before lock");
        d=new Decoder(disk,3);d.Reset();d.Feed(Fixture(3,0,1),receive);
        Reject(d.Complete,"Short recording claimed qualification");
    }
    static void CorruptedTransport(bool disk) {
        foreach(int change in new[]{1,2,3,4,5}) {
            byte[] b=Fixture(3,0,12),bad=(byte[])b.Clone();
            if(change==1)for(int i=0;i<bad.Length;i+=2){byte t=bad[i];bad[i]=bad[i+1];bad[i+1]=t;}
            if(change==2)for(int i=0;i<bad.Length;i++)bad[i]=(byte)((bad[i]<<1)|(bad[i]>>7));
            if(change==3)for(int at=0;at<bad.Length;at+=60)for(int w=0;w<5;w++)
                for(int t=0;t<10;t++)bad[at+w*10+t]=b[at+w*10+(t+1)%10];
            if(change==4)for(int at=0;at<bad.Length;at+=60)for(int w=0;w<5;w++)
                Buffer.BlockCopy(b,at+((w+1)%5)*10,bad,at+w*10,10);
            if(change==5)for(int at=0;at<bad.Length;at+=60)bad[at+50]^=2;
            var d=new Decoder(disk,3);d.Reset();int emitted=0;
            d.Feed(bad,(a,na,l,nl)=>emitted+=na+nl);
            Check(emitted==0 && d.Frames==0,"Bad DDR/lane/bit/word/fill transport locked: "+change);
            Reject(d.Complete,"Bad transport qualified: "+change);
        }
    }
    static void PostLockContinuity(bool disk,int change) {
        byte[] b=Fixture(3,0,4);var d=new Decoder(disk,3);d.Reset();int ap=0;
        Action<short[],int,short[],int> receive=(a,na,l,nl)=>ap+=na;
        d.Feed(b,0,1560,receive);Check(ap==2,"Qualification did not preserve both good rows");
        b[1560+(change==0?30:change==1?50:0)]^=(byte)(change==0?0x80:change==1?2:1);
        Reject(()=>d.Feed(b,1560,780,receive),"Post-lock corruption was accepted");
        Check(ap==2 && d.Errors==1,"Corrupt row emitted or failure was hidden");
        Reject(d.Complete,"Failed decoder completed successfully");
        bool closed=false;try{d.Feed(Fixture(3,20,3),receive);}catch(InvalidOperationException){closed=true;}
        Check(closed,"Failed decoder silently relocked");
    }
    static void ModelAndEpoch(bool disk) {
        var d=new Decoder(disk,3);d.Reset();d.Fixed(0x22);int ap=0;
        d.Feed(Fixture(3,0,3,0x11),(a,na,l,nl)=>ap+=na);
        Check(ap==0,"Explicit NRIC model accepted NP1 fixed word");
        Reject(d.Complete,"Wrong model completed");
        d=new Decoder(disk,3);d.Reset();d.Fixed(0x22);
        d.Feed(Fixture(3,1,24,0x22),(a,na,l,nl)=>ap+=na);d.Complete();
        Check(ap==24 && d.Model==0x22 && d.Slot==1,"NRIC qualification changed");
        d.Fixed(0x22);Reject(()=>d.Fixed(0x11),"Model changed after lock");Reject(d.Reset,"Reset redeclared after lock");
        d=new Decoder(disk,1);d.Feed(Fixture(1,0,2),(a,na,l,nl)=>{});
        Reject(d.Quad,"Layout changed after lock");
        Reject(()=>d.Fixed(0x11),"Model declared retrospectively");
        d=new Decoder(disk,3);d.Feed(Fixture(3,0,1),(a,na,l,nl)=>{});
        Reject(d.Reset,"Reset declared after startup bytes");Reject(()=>d.Fixed(0x11),"Model declared after startup bytes");
        foreach(bool reset in new[]{false,true}) {
            d=new Decoder(disk,3);if(reset)d.Reset();ap=0;
            Reject(()=>d.Feed(Fixture(3,0,3,0x11,reset?(uint?)1:null),(a,na,l,nl)=>ap+=na),"Unknown/misaligned reset epoch accepted");
            Check(ap==0 && d.PhaseErrors==1,"Unknown epoch emitted samples");
        }
        bool invalid=false;try{new Decoder(disk,4);}catch(ArgumentOutOfRangeException){invalid=true;}
        Check(invalid,"Unknown explicit layout accepted");
        // A reconnect/new START receives a new decoder; previous epoch is never reused.
        d=new Decoder(disk,3);ap=0;
        Reject(()=>d.Feed(Fixture(3,0,3),(a,na,l,nl)=>ap+=na),"Reconnect inherited previous reset authority");
        d=new Decoder(disk,3);d.Reset();d.Feed(Fixture(3,7,24),(a,na,l,nl)=>ap+=na);
        Check(ap==24 && d.Slot==7,"Fresh reconnect did not establish its own phase");
    }
    static void CounterWrap(bool disk) {
        foreach(int layout in new[]{1,2,3}) {
            var d=new Decoder(disk,layout);d.Reset();int ap=0;
            d.Feed(Fixture(layout,80659,24),(a,na,l,nl)=>ap+=na);d.Complete();
            Check(ap==24 && d.Errors==0 && d.Slot==80659%12,"Continuous counter wrap rejected or changed phase");
        }
    }
    static void AdcOnlyLimit(bool disk) {
        byte[] original=Fixture(3,0,12),bad=(byte[])original.Clone();
        // Shift only ADC line 1 within its 10-bit words. All framing fields stay intact.
        for(int at=0;at<bad.Length;at+=60)for(int w=0;w<5;w++)for(int t=0;t<10;t++) {
            int i=at+w*10+t,j=at+w*10+(t+1)%10;
            bad[i]=(byte)((bad[i]&~2)|(original[j]&2));
        }
        var d=new Decoder(disk,3);d.Reset();int ap=0;bool changed=false;
        d.Feed(bad,(a,na,l,nl)=> {
            ap+=na;for(int adc=0;adc<5;adc++)for(int slot=0;slot<12;slot++)
                if(a[Channel(adc,slot)]!=512-(adc*17+slot))changed=true;
        });d.Complete();
        Check(ap==12 && changed && d.Errors==0,"ADC-only integrity limitation fixture invalid");
    }
    static void Throughput(bool disk) {
        byte[] b=Fixture(3,0,10000);var d=new Decoder(disk,3);d.Reset();int rows=0;
        var watch=Stopwatch.StartNew();
        for(int at=0;at<b.Length;at+=131072)d.Feed(b,at,Math.Min(131072,b.Length-at),(a,na,l,nl)=>rows+=na);
        d.Complete();watch.Stop();Check(rows==10000 && d.Errors==0,"Throughput fixture incomplete");
        Console.WriteLine((disk?"SD":"LIVE")+" qualified throughput MB/s="+(b.Length/watch.Elapsed.TotalSeconds/1e6).ToString("F2"));
    }
    static int Main() {
        foreach(bool disk in new[]{false,true}) {
            foreach(int layout in new[]{1,2,3})foreach(int skip in new[]{0,1,9,10,59,60,61,120,779,780,781,1559})
                foreach(int fragment in new[]{1,779,780,1559,1560,4093})FragmentedStarts(disk,layout,skip,fragment);
            FalseSingleSuperframe(disk);CorruptedTransport(disk);ModelAndEpoch(disk);CounterWrap(disk);
            for(int change=0;change<3;change++)PostLockContinuity(disk,change);
            AdcOnlyLimit(disk);Throughput(disk);
        }
        Console.WriteLine("PASS: "+checks+" live/SD startup, phase, transport, reset and post-lock checks");
        Console.WriteLine("LIMIT: framing cannot prove arbitrary ADC-only bit integrity; no sample correction is attempted.");
        return 0;
    }
}
