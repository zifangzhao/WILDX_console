using System;
using System.IO;
using System.Reflection;
using USB512_interface;
using CE32_console;

// Test the actual live and SD decoders, not a replacement codec. The serial
// fixture follows NP1 Table 15/16 and tracks an absolute reset-epoch counter.
internal static class Np1LfpPhaseTests
{
    static int checks;
    static void Check(bool ok,string why) { checks++;if(!ok)throw new Exception(why); }
    static int Channel(int adc,int slot) { return (adc/2)*24+(adc&1)+slot*2; }
    static int Value(ulong cycle,int adc,int slot) { return (int)((cycle*17+(ulong)(adc*3+slot*7))&1023); }
    static void Word(byte[] b,int at,int line,int word,uint value) {
        for(int bit=0;bit<10;bit++)if((value&(1U<<(9-bit)))!=0)b[at+word*10+bit]|=(byte)(1<<line);
    }
    static byte[] Fixture(ulong first,int count,int fixedWord) {
        var b=new byte[count*780];
        for(int sf=0;sf<count;sf++)for(int f=0;f<13;f++) {
            int at=sf*780+f*60;ulong absolute=first+(ulong)(sf*13+f);
            uint c=(uint)(absolute&0xFFFFF);
            Word(b,at,0,0,f==0?0x330U:0xCFU);Word(b,at,7,2,(uint)fixedWord);
            Word(b,at,7,3,c>>10);Word(b,at,7,4,c&1023);
            int slot=f==0?(int)((first/13+(ulong)sf)%12):f-1;
            ulong cycle=f==0?(first/13+(ulong)sf)/12:first/13+(ulong)sf;
            for(int adc=0;adc<32;adc++)Word(b,at,adc<30?adc/5+1:7,adc<30?adc%5:adc-30,(uint)Value(cycle,adc,slot));
        }
        return b;
    }
    static void Valid(ulong first,int count,int fragment,int fixedWord,bool sd,int prefix) {
        Check(first%13==0 && first<0x100000,"reset fixture epoch");
        byte[] source=Fixture(first,count,fixedWord),bytes=new byte[source.Length+prefix];
        for(int i=0;i<prefix;i++)bytes[i]=0x55;
        Buffer.BlockCopy(source,0,bytes,prefix,source.Length);
        var live=new XeNpStreamDecoder(XeNpLaneLayout.GpioV1Quad);
        var saved=new NpCe64Decoder(NpCe64LaneLayout.GpioV1Quad);
        live.DeclareRecordingReset();saved.DeclareRecordingReset();
        int ap=0,lfp=0,initial=(int)((first/13)%12);
        int discard=initial==0?0:12-initial;
        Action<short[],int,short[],int> sink=(a,na,l,nl)=> {
            ulong sf=first/13+(ulong)ap;
            Check(na==1,"AP callback row count");
            for(int adc=0;adc<32;adc++)for(int slot=0;slot<12;slot++)
                Check(a[Channel(adc,slot)]==512-Value(sf,adc,slot),"AP channel/value changed");
            bool ready=ap>=discard && sf%12==11;
            Check(nl==(ready?1:0),"partial LFP row emitted or complete row lost");
            if(ready) {
                for(int adc=0;adc<32;adc++)for(int slot=0;slot<12;slot++)
                    Check(l[Channel(adc,slot)]==512-Value(sf/12,adc,slot),"LFP reset phase/channel/value");
                lfp++;
            } else Check(l==null,"partial LFP borrowed buffer exposed");
            ap++;
        };
        for(int at=0;at<bytes.Length;at+=fragment) {
            int n=Math.Min(fragment,bytes.Length-at);
            if(sd)saved.Feed(bytes,at,n,sink);else live.Feed(bytes,at,n,sink);
        }
        if(sd)saved.Complete();else live.Complete();
        Check(ap==count && lfp==Math.Max(0,count-discard)/12,"complete row totals");
        Check((sd?saved.InitialLfpSlot:live.InitialLfpSlot)==initial,"initial slot receipt");
        Check((sd?saved.StartupDiscardedLfpSuperframes:live.StartupDiscardedLfpSuperframes)==(count>=discard?discard:0),"partial-cycle discard receipt");
        Check((sd?saved.FramesChecked:live.FramesChecked)==count*13,"all frames checked");
        Check((sd?saved.CounterErrors:live.CounterErrors)==0,"continuous counter wrap rejected");
        Check((sd?saved.StartupDiscardedBytes:live.StartupDiscardedBytes)==prefix,"startup byte discard");
        bool refused=false;try {if(sd)saved.DeclareRecordingReset();else live.DeclareRecordingReset();}catch(IOException){refused=true;}
        Check(refused,"midstream phase epoch change accepted");
    }
    static void Reject(bool sd,bool declare,ulong first,bool gap) {
        var live=new XeNpStreamDecoder(XeNpLaneLayout.GpioV1Quad);
        var saved=new NpCe64Decoder(NpCe64LaneLayout.GpioV1Quad);
        if(declare){live.DeclareRecordingReset();saved.DeclareRecordingReset();}
        var bytes=Fixture(first,36,0x11);if(gap)bytes[1560+49]^=0x80; // Corrupt after two-superframe startup qualification.
        int callbacks=0;bool rejected=false;
        try {
            Action<short[],int,short[],int> sink=(a,na,l,nl)=>{callbacks++;};
            if(sd){saved.Feed(bytes,sink);saved.Complete();}else{live.Feed(bytes,sink);live.Complete();}
        }catch(IOException){rejected=true;}
        Check(rejected,"unsafe phase or counter gap accepted");
        if(!gap)Check(callbacks==0,"unqualified phase emitted samples");
        Check((sd?saved.Resynchronizations:live.Resynchronizations)==0,"silent stream repair");
    }
    static void ActualLive(string executable,string exported) {
        var assembly=Assembly.LoadFrom(Path.GetFullPath(executable));
        const BindingFlags instance=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        Type type=assembly.GetType("USB512_interface.XeNpStreamDecoder",true);
        Type layout=assembly.GetType("USB512_interface.XeNpLaneLayout",true);
        MethodInfo feed=type.GetMethod("Feed",instance,null,new[]{typeof(byte[]),typeof(int),typeof(int),typeof(Action<short[],int,short[],int>)},null);
        int probes=0;
        for(int probe=1;probe<=3;probe++) {
            string stem=Path.Combine(exported,"np"+probe);
            if(!File.Exists(stem+".lane8"))continue;
            probes++;
            object decoder=Activator.CreateInstance(type,instance,null,new[]{Enum.ToObject(layout,3)},null);
            type.GetMethod("DeclareRecordingReset",instance).Invoke(decoder,null);
            using(var raw=File.OpenRead(stem+".lane8"))
            using(var ap=File.OpenRead(stem+"_ap.dat"))
            using(var lfp=File.OpenRead(stem+"_lfp.dat")) {
                Check(raw.Length<=64*1024*1024,"bounded actual decoder input");
                var wanted=new byte[768];var got=new byte[768];
                Action<short[],int,short[],int> sink=(a,na,l,nl)=> {
                    for(int band=0;band<2;band++) {
                        if((band==0?na:nl)==0)continue;
                        Check((band==0?na:nl)==1,"actual decoder row count");
                        Stream dat=band==0?ap:lfp;int at=0,n;
                        while(at<768 && (n=dat.Read(wanted,at,768-at))!=0)at+=n;
                        Check(at==768,"actual live decoder emitted extra row");
                        Buffer.BlockCopy(band==0?a:l,0,got,0,768);
                        for(int i=0;i<768;i++)Check(wanted[i]==got[i],"actual live vs audited SD DAT byte mismatch");
                    }
                };
                var buffer=new byte[32768];int length;
                while((length=raw.Read(buffer,0,buffer.Length))!=0)feed.Invoke(decoder,new object[]{buffer,0,length,sink});
                type.GetMethod("Complete",instance).Invoke(decoder,null);
                Check(ap.ReadByte()==-1 && lfp.ReadByte()==-1,"actual live decoder dropped audited SD rows");
            }
        }
        Check(probes>0,"no bounded actual lane files");
        Console.WriteLine("PASS actual Release live decoder matches every AP/LFP DAT byte for "+probes+" saved real probes");
    }
    static int Main(string[] args) {
        foreach(bool sd in new[]{false,true}) {
            foreach(int slot in new[]{0,1,2,3,4,5,6,7,8,9,10,11})
                foreach(int fragment in new[]{1,59,1973,65536})
                    foreach(int fixedWord in new[]{0x11,0x22})Valid((ulong)(13*(192+slot)),37,fragment,fixedWord,sd,51);
            // Several slots at the first natural 20-bit wrap. Never recompute
            // multiplexing from the modulo counter after it wraps to zero.
            for(int before=1;before<=12;before++)Valid((ulong)((0x100000/13-before)*13),39,1973,0x11,sd,0);
            Valid(2600,3,17,0x11,sd,0); // No complete LFP cycle yet.
            Reject(sd,false,2600,false);Reject(sd,true,2601,false);Reject(sd,true,2600,true);
        }
        Console.WriteLine("PASS "+checks+" actual NP1 live/SD checks: 12 initial slots, both model words, fragmentation, partial startup, counter wrap, unknown epoch and counter fault");
        if(args.Length==2)ActualLive(args[0],args[1]);
        else if(args.Length!=0)throw new ArgumentException("Optional arguments: actual USB console executable and audited real-export folder");
        return 0;
    }
}
