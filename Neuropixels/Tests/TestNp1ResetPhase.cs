using System;
using System.IO;
using USB512_interface;

internal static class Np1ResetPhaseTests
{
    static int checks;
    static void Check(bool value,string reason) { checks++;if(!value)throw new Exception(reason); }
    static void Word(byte[] bytes,int at,int line,int word,uint value)
    {
        for(int bit=0;bit<10;bit++)if((value&(1U<<(9-bit)))!=0)
            bytes[at+word*10+bit]|=(byte)(1<<line);
    }
    static byte[] Fixture(int firstSuperframe,int count)
    {
        var bytes=new byte[count*780];
        for(int row=0;row<count;row++)for(int frame=0;frame<13;frame++) {
            int sf=firstSuperframe+row,at=(row*13+frame)*60;
            uint counter=(uint)(sf*13+frame)&0xFFFFF;
            Word(bytes,at,0,0,frame==0?0x330U:0xCFU);
            Word(bytes,at,7,2,0x11);Word(bytes,at,7,3,counter>>10);Word(bytes,at,7,4,counter&1023);
            for(int adc=0;adc<32;adc++) {
                int slot=frame==0?sf%12:frame-1;
                Word(bytes,at,adc<30?adc/5+1:7,adc<30?adc%5:adc-30,(uint)(adc*12+slot));
            }
        }
        return bytes;
    }
    static void Aligned(int first,int rows,int fragment)
    {
        var decoder=new XeNpStreamDecoder(XeNpLaneLayout.GpioV1Quad);
        decoder.DeclareRecordingReset();var bytes=Fixture(first,rows);int ap=0,lfp=0;
        Action<short[],int,short[],int> receive=(a,na,l,nl)=> {
            ap+=na;lfp+=nl;
            for(int adc=0;adc<32;adc++)for(int slot=0;slot<12;slot++) {
                int channel=(adc/2)*24+(adc&1)+slot*2;
                Check(a[channel]==512-(adc*12+slot),"AP channel changed");
                if(nl!=0)Check(l[channel]==512-(adc*12+slot),"LFP slot/channel rotated");
            }
        };
        for(int at=0;at<bytes.Length;at+=fragment)
            decoder.Feed(bytes,at,Math.Min(fragment,bytes.Length-at),receive);
        decoder.Complete();
        int discarded=first%12==0?0:12-first%12;
        Check(ap==rows && lfp==(rows-discarded)/12,"Complete native AP/LFP row count");
        Check(decoder.InitialLfpSlot==first%12 && decoder.StartupDiscardedLfpSuperframes==discarded,"Initial partial LFP epoch");
        Check(decoder.FrameErrors==0 && decoder.CounterErrors==0 && decoder.LfpPhaseErrors==0,"Integrity counters");
        bool refused=false;try{decoder.DeclareRecordingReset();}catch(IOException){refused=true;}
        Check(refused,"Cannot redeclare reset after any data");
    }
    static void RejectUnknownEpoch(bool declared,int first)
    {
        var d=new XeNpStreamDecoder(XeNpLaneLayout.GpioV1Quad);if(declared)d.DeclareRecordingReset();
        int rows=0;bool rejected=false;
        try{d.Feed(Fixture(first,24),(a,na,l,nl)=>rows+=na+nl);}
        catch(IOException){rejected=true;}
        Check(rejected && rows==0 && d.LfpPhaseErrors==1,"Unknown reset/first-wrap epoch must not emit shifted LFP");
    }
    static void CounterDiagnostic()
    {
        var d=new XeNpStreamDecoder(XeNpLaneLayout.GpioV1Quad);d.DeclareRecordingReset();var bytes=Fixture(0,24);
        d.Feed(bytes,0,1560,(a,na,l,nl)=>{});bytes[1560+30]^=0x80;
        string message=null;try{d.Feed(bytes,1560,780,(a,na,l,nl)=>{});}catch(IOException e){message=e.Message;}
        Check(message!=null && message.Contains("expected 0x0001A") && message.Contains("received 0x8001A") &&
              d.CounterErrors==1,"Failure-only exact counter diagnostic");
    }
    static int Main()
    {
        foreach(int first in new[]{0,1,2,3,4,5,6,7,8,9,10,11,37,80650})foreach(int fragment in new[]{1,511,1973,32768})
            Aligned(first,48,fragment);
        RejectUnknownEpoch(false,0);RejectUnknownEpoch(true,80660);CounterDiagnostic();
        Console.WriteLine("PASS: "+checks+" NP1 reset-epoch/LFP-slot/partial-start/counter-wrap checks");return 0;
    }
}
