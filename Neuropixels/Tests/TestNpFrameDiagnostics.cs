using System;
using System.IO;
using System.Reflection;
using USB512_interface;

internal static class TestNpFrameDiagnostics
{
    static int checks;
    static void Check(bool value,string reason){checks++;if(!value)throw new Exception(reason);}
    static byte[] Fixture(int layout)
    {return (byte[])typeof(Np1StartupTests).GetMethod("Fixture",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{layout,0,4,(ushort)0x11,null});}
    static void Flip(byte[] b,int at,byte mask,int layout){b[at]^=layout==2?(byte)((mask>>1)|(mask<<7)):mask;}
    static XeNpStreamDecoder Decoder(int layout)
    {var decoder=new XeNpStreamDecoder((XeNpLaneLayout)layout);decoder.DeclareRecordingReset();decoder.DeclareFixedWord(0x11);return decoder;}
    static void Broken(int layout,string field)
    {
        byte[] b=Fixture(layout);int at=27*60;
        if(field=="sync")Flip(b,at+3,1,layout);
        if(field=="fixed")Flip(b,at+29,128,layout);
        if(field=="fill")Flip(b,at+50,2,layout);
        if(field=="heartbeat")Flip(b,at+20,1,layout);
        var decoder=Decoder(layout);string message=null;
        try{decoder.Feed(b,(a,na,l,nl)=>{});}catch(IOException error){message=error.Message;}
        Check(message!=null && decoder.FramesChecked==27 && decoder.FrameErrors==1 && decoder.CounterErrors==0,"Frame admission changed: "+field);
        Check(message.Contains("at frame 27 (raw byte 1620, counter 0x0001B)"),"Failure frame/offset/counter details");
        Check(message.Contains("sync expected 0x0CF received 0x"+(field=="sync"?"08F":"0CF")),"Sync diagnostic");
        Check(message.Contains("fixed expected 0x011 received 0x"+(field=="fixed"?"010":"011")),"Fixed-word diagnostic");
        Check(message.Contains("fill nonzero ticks="+(field=="fill"?"1 mask=0x02":"0 mask=0x00")),"Fill diagnostic");
        Check(message.Contains("heartbeat transitions="+(field=="heartbeat"?"3":"1")+" (maximum 1)"),"Heartbeat diagnostic");
        Check(message.EndsWith("recording was not resynchronized."),"Failure policy text");
        bool closed=false;try{decoder.Feed(b,(a,na,l,nl)=>{});}catch(InvalidOperationException){closed=true;}Check(closed,"Failed decoder resumed");
    }
    static int Main()
    {
        try{
            foreach(int layout in new[]{1,2,3}){
                var clean=Decoder(layout);clean.Feed(Fixture(layout),(a,na,l,nl)=>{});clean.Complete();Check(clean.FramesChecked==52 && clean.FrameErrors==0 && clean.CounterErrors==0,"Clean fixture changed");
                foreach(string field in new[]{"sync","fixed","fill","heartbeat"})Broken(layout,field);
                var b=Fixture(layout);Flip(b,27*60+39,128,layout);var counter=Decoder(layout);string message=null;
                try{counter.Feed(b,(a,na,l,nl)=>{});}catch(IOException error){message=error.Message;}
                Check(message!=null && message.StartsWith("NP1 frame-counter discontinuity:") && counter.CounterErrors==1 && counter.FrameErrors==0,"Counter failure admission or diagnostic changed");
            }
            Console.WriteLine("PASS "+checks+" failure-only sync/fixed/fill/heartbeat/frame-index diagnostics, GPIO/PSSI/V1 formats, unchanged counter admission and closed-on-error checks.");return 0;
        }catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
