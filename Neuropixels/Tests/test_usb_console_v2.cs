using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using USB512_interface;

internal static class ConsoleV2Tests
{
    static int checks;
    static void Check(bool condition,string message) { checks++;if(!condition)throw new Exception(message); }
    static void Reject(Action action,string message) { try { action(); } catch(IOException) { checks++;return; } throw new Exception(message); }
    static int Channel(int adc,int slot) { return (adc/2)*24+(adc&1)+slot*2; }
    static ushort Sample(int sf,int frame,int adc) { return (ushort)((sf*5+frame*13+adc*7)&1023); }
    static void Word(byte[] data,int start,int word,int bitLine,uint value)
    { for(int bit=0;bit<10;bit++)if((value&(1U<<(9-bit)))!=0)data[start+word*10+bit]|=(byte)(1<<bitLine); }
    static byte[] Frames(int superframes,bool pssi,uint first=0xFFFF0)
    {
        byte[] bytes=new byte[superframes*780];
        for(int sf=0;sf<superframes;sf++)for(int f=0;f<13;f++) {
            int at=(sf*13+f)*60;Word(bytes,at,0,0,f==0?0x330U:0xCFU);
            // A single heartbeat transition inside a frame is valid.
            if(sf==1 && f==3)for(int b=31;b<60;b++)bytes[at+b]|=1;
            Word(bytes,at,2,7,0x11);uint counter=(first+(uint)(sf*13+f))&0xFFFFF;
            Word(bytes,at,3,7,counter>>10);Word(bytes,at,4,7,counter&1023);
            for(int adc=0;adc<30;adc++)Word(bytes,at,adc/6,1+adc%6,Sample(sf,f,adc));
            Word(bytes,at,0,7,Sample(sf,f,30));Word(bytes,at,1,7,Sample(sf,f,31));
        }
        if(pssi)for(int i=0;i<bytes.Length;i++)bytes[i]=(byte)((bytes[i]>>1)|(bytes[i]<<7));
        return bytes;
    }
    static byte[] Packet(byte[] data,int offset,int count,ushort sequence)
    {
        byte[] result=new byte[count+6];result[0]=0x12;result[1]=0xA5;result[2]=6;
        if(count!=506) { result[3]=4;result[4]=(byte)count;result[5]=(byte)(count>>8); }
        else { result[4]=(byte)sequence;result[5]=(byte)(sequence>>8); }
        Buffer.BlockCopy(data,offset,result,6,count);return result;
    }
    static byte[] Wire(byte[] raw)
    {
        using(var output=new MemoryStream()) {
            ushort sequence=65520;
            for(int offset=0;offset<raw.Length;offset+=506) {
                byte[] packet=Packet(raw,offset,Math.Min(506,raw.Length-offset),sequence++);
                output.Write(packet,0,packet.Length);
            }
            return output.ToArray();
        }
    }
    static void DecodeFixture(bool pssi,int chunk,XeNpLaneLayout hint)
    {
        byte[] raw=Frames(24,pssi),wire=Wire(raw);
        var decoder=new XeNpStreamDecoder(hint);var parser=new XeNpPacketParser();
        int apRows=0,lfpRows=0,rawBytes=0;
        Action<short[],int,short[],int> accept=delegate(short[] ap,int an,short[] lfp,int ln) {
            Check(an==1,"AP row count");
            for(int slot=0;slot<12;slot++)for(int adc=0;adc<32;adc++)
                Check(ap[Channel(adc,slot)]==512-Sample(apRows,slot+1,adc),"AP channel/word value");
            apRows++;
            if(ln>0) {
                Check(ln==1,"LFP row count");
                for(int slot=0;slot<12;slot++)for(int adc=0;adc<32;adc++)
                    Check(lfp[Channel(adc,slot)]==512-Sample(lfpRows*12+slot,0,adc),"LFP channel/word value");
                lfpRows++;
            }
        };
        for(int at=0;at<wire.Length;at+=chunk) {
            int length=Math.Min(chunk,wire.Length-at);byte[] fragment=new byte[length];Buffer.BlockCopy(wire,at,fragment,0,length);
            parser.Feed(fragment,length,delegate(byte[] bytes,int offset,int n) {
                for(int i=0;i<n;i++)Check(bytes[offset+i]==raw[rawBytes++],"raw payload changed");
                decoder.Feed(bytes,offset,n,accept);
            });
        }
        decoder.Complete();
        Check(apRows==24 && lfpRows==2,"sample totals");
        Check(decoder.FramesChecked==312 && decoder.CounterErrors==0 && decoder.FrameErrors==0,"integrity counters");
        Check(decoder.Layout==(pssi?XeNpLaneLayout.Pssi:XeNpLaneLayout.Gpio),"lane layout");
        Check(parser.PendingBytes==0 && parser.SequenceGaps==0,"packet continuity/wrap");
    }
    static void Faults()
    {
        var parser=new XeNpPacketParser();byte[] raw=Frames(2,true);
        byte[] p0=Packet(raw,0,506,100),p2=Packet(raw,506,506,102);
        parser.Feed(p0,p0.Length,delegate(byte[] p){});
        Reject(delegate{parser.Feed(p2,p2.Length,delegate(byte[] p){});},"packet gap accepted");
        Check(parser.SequenceGaps==1,"packet gap not latched");
        parser=new XeNpPacketParser();p0[0]=0;
        Reject(delegate{parser.Feed(p0,p0.Length,delegate(byte[] p){});},"bad magic accepted");
        parser=new XeNpPacketParser();p0=Packet(raw,0,506,0);p0[3]=0x20;
        Reject(delegate{parser.Feed(p0,p0.Length,delegate(byte[] p){});},"interleaved lanes accepted as NP1 single lane");
        foreach(bool pssi in new[]{false,true}) {
            var decoder=new XeNpStreamDecoder(pssi?XeNpLaneLayout.Pssi:XeNpLaneLayout.Gpio);
            byte[] data=Frames(3,pssi);decoder.Feed(data,0,1560,delegate(short[] a,int n,short[] l,int m){});
            // Counter corruption after lock keeps frame markers/fixed/fill valid.
            data[1560+30]^=(byte)(pssi?0x40:0x80);
            Reject(delegate{decoder.Feed(data,1560,780,delegate(short[] a,int n,short[] l,int m){});},"counter gap accepted");
            Check(decoder.CounterErrors==1,"counter error not exposed");
            decoder=new XeNpStreamDecoder(pssi?XeNpLaneLayout.Pssi:XeNpLaneLayout.Gpio);
            data=Frames(3,pssi);decoder.Feed(data,0,1560,delegate(short[] a,int n,short[] l,int m){});
            data[1560+50]^=(byte)(pssi?1:2);
            Reject(delegate{decoder.Feed(data,1560,780,delegate(short[] a,int n,short[] l,int m){});},"fill error accepted");
        }
    }
    static void Put32(byte[] data,int offset,uint value) { Buffer.BlockCopy(BitConverter.GetBytes(value),0,data,offset,4); }
    static void StatusTests()
    {
        byte[] s=new byte[344];Put32(s,0,0x55534253);Put32(s,4,8);Put32(s,260,1);
        Put32(s,268,23400000);Put32(s,336,2);Put32(s,340,30000);
        var status=XeNpStatus.Parse(s,s.Length);
        Check(status.Layout==XeNpLaneLayout.Pssi && status.ApSampleRateHz==30000,"v8 PSSI profile");
        Put32(s,332,1);status=XeNpStatus.Parse(s,s.Length);
        Reject(status.ThrowIfFault,"recording fault ignored");
        Put32(s,4,7);Put32(s,332,0);status=XeNpStatus.Parse(s,336);
        Check(status.Layout==XeNpLaneLayout.Auto && status.ApSampleRateHz==30000,"v7 PSSI fallback");
        Put32(s,4,6);Put32(s,268,11700000);status=XeNpStatus.Parse(s,332);
        Check(status.Layout==XeNpLaneLayout.Auto && status.ApSampleRateHz==15000,"legacy GPIO rate");
        Put32(s,268,15600000);status=XeNpStatus.Parse(s,332);
        Check(status.Layout==XeNpLaneLayout.Auto && status.ApSampleRateHz==20000,"native 20 kHz GPIO rate");
        Put32(s,4,8);Reject(delegate{XeNpStatus.Parse(s,340);},"truncated v8 status accepted");
        Check(XeNpWinUsbDevice.IsCapturePath("usb#VID_1209&PID_B386#serial"),"capture not found");
        Check(!XeNpWinUsbDevice.IsCapturePath("usb#vid_1209&pid_b387#serial"),"updater offered as capture");
        Check(!XeNpWinUsbDevice.IsCapturePath("usb#vid_1209&pid_b388#serial"),"MSC offered as capture");
    }
    static void Replay(string path)
    {
        var decoder=new XeNpStreamDecoder();var parser=new XeNpPacketParser();
        long bytes=0,ap=0,lfp=0;ushort sequence=65500;
        byte[] input=new byte[506],batch=new byte[32768];int batched=0;
        var watch=Stopwatch.StartNew();
        Action<byte[],int,int> consume=delegate(byte[] data,int offset,int n) {
            decoder.Feed(data,offset,n,delegate(short[] a,int an,short[] l,int ln){ap+=an;lfp+=ln;});
        };
        using(var file=File.OpenRead(path)) {
            int count;
            while((count=file.Read(input,0,input.Length))>0) {
                byte[] p=Packet(input,0,count,sequence++);bytes+=count;
                if(batched+p.Length>batch.Length) { parser.Feed(batch,batched,consume);batched=0; }
                Buffer.BlockCopy(p,0,batch,batched,p.Length);batched+=p.Length;
            }
        }
        if(batched>0)parser.Feed(batch,batched,consume);
        decoder.Complete();watch.Stop();
        Check(decoder.Layout==XeNpLaneLayout.Pssi,"real V2 recording not recognized");
        Check(decoder.FrameErrors==0 && decoder.CounterErrors==0 && parser.SequenceGaps==0,"replay failed integrity");
        Console.WriteLine("REPLAY bytes="+bytes+" frames="+decoder.FramesChecked+" AP="+ap+" LFP="+lfp+
            " layout="+decoder.Layout+" discarded_start="+decoder.StartupDiscardedBytes+
            " MB/s="+(bytes/watch.Elapsed.TotalSeconds/1000000).ToString("F2")+" file="+path);
    }
    public static int Main(string[] args)
    {
        foreach(bool pssi in new[]{false,true})foreach(int chunk in new[]{1,5,7,511,4093,32768})
            DecodeFixture(pssi,chunk,XeNpLaneLayout.Auto);
        DecodeFixture(true,4093,XeNpLaneLayout.Pssi);DecodeFixture(false,4093,XeNpLaneLayout.Gpio);
        Faults();StatusTests();
        for(int i=0;i<args.Length;i++) {
            if(args[i]=="--reject") {
                string bad=args[++i];Check(File.Exists(bad),"negative replay fixture missing");
                Reject(delegate{Replay(bad);},"corrupt recording silently accepted");
                Console.WriteLine("EXPECTED REJECTION: "+bad);
            } else Replay(args[i]);
        }
        Console.WriteLine("PASS: "+checks+" USB_console parser/decoder/profile checks (no hardware accessed).");
        return 0;
    }
}
