using System;
using System.IO;
using System.Linq;
using WildNpClosedLoop;

static class NpClosedLoopSpiTapTests
{
    static int checks;
    static void Put(byte[] b,int at,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,at,4);}
    static uint U(byte[] b,int at){return BitConverter.ToUInt32(b,at);}
    static void Need(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static byte[] Configure(byte[] golden,uint tap,uint hz,NpClosedLoopChannel[] channels,out byte[] descriptor)
    {
        var config=golden.Take(2048).ToArray();
        var p=config.Skip(1888).Take(160).ToArray();
        byte[] request=NpClosedLoopControl.Request(1,11,hz,0,1000,channels);
        Buffer.BlockCopy(request,0,p,32,64);Put(p,16,U(request,60));Put(p,24,hz);Put(p,28,hz*1600U);
        Put(p,104,(22U*(hz/2500U)-2U)*1600U);Put(p,116,tap);Put(p,128,(hz/1250U)*1600U);
        if(channels.Length==1){Put(p,112,0);Put(p,120,U(p,120)&~8U);}
        Put(p,156,NpClosedLoopEvent.Crc(p,156));Buffer.BlockCopy(p,0,config,1888,160);
        descriptor=new byte[512];Put(descriptor,8,16);Put(descriptor,12,U(p,20));Put(descriptor,16,hz);
        Put(descriptor,24,7);Put(descriptor,96,239);Put(descriptor,132,U(request,60));
        return config;
    }
    static NpClosedLoopChannel Channel(byte probe,ushort channel){return new NpClosedLoopChannel{Probe=probe,Channel=channel};}
    static void Reject(byte[] config,byte[] descriptor,string why)
    {
        try{NpClosedLoopCarrier.ReadV2Configuration(config,descriptor);}
        catch(InvalidDataException){checks++;return;}
        throw new Exception(why);
    }
    static int Main(string[] args)
    {
        try{
            byte[] golden=File.ReadAllBytes(args[0]);
            if(args.Length>1){
                byte[] native=File.ReadAllBytes(args[1]),descriptor=new byte[512];
                Put(descriptor,8,16);Put(descriptor,12,U(native,1908));Put(descriptor,16,U(native,1912));
                Put(descriptor,24,7);Put(descriptor,96,239);Put(descriptor,132,U(native,1904));
                var q=NpClosedLoopCarrier.ReadV2Configuration(native,descriptor);
                Need(U(native,2004)==2&&q.Channels.Length==2&&q.Channels.All(c=>c.Probe==2)&&
                    q.Channels[0].Channel==0&&q.Channels[1].Channel==1,"Actual firmware P2 golden metadata did not preserve source identity");
            }
            foreach(uint hz in new[]{7500U,15000U,20000U,30000U})foreach(uint tap in new[]{2U,0x203U})foreach(int count in new[]{1,2}){
                var channels=tap==2?new[]{Channel(2,0),Channel(2,127)}:new[]{Channel(3,256),Channel(3,383)};
                byte[] d;var config=Configure(golden,tap,hz,channels.Take(count).ToArray(),out d);
                foreach(uint version in new[]{15U,16U}){
                    Put(d,8,version);var q=NpClosedLoopCarrier.ReadV2Configuration(config,d);
                    Need(q.ApHz==hz&&q.Channels.Length==count&&q.Channels[count-1].Probe==channels[count-1].Probe&&
                        q.Channels[count-1].Channel==channels[count-1].Channel,"SPI tap identity or legacy envelope changed");
                }
                var damaged=(byte[])config.Clone();damaged[1904]^=1;Reject(damaged,d,"Damaged source seal accepted");
                var wrongMask=(byte[])d.Clone();Put(wrongMask,24,3);Reject(config,wrongMask,"Wrong recorded probe mask accepted");
            }
            foreach(var channels in new[]{new[]{Channel(1,0)},new[]{Channel(3,0)},new[]{Channel(2,128)},new[]{Channel(2,0),Channel(3,256)}}){
                byte[] d;var config=Configure(golden,2,20000,channels,out d);Reject(config,d,"P2 tap accepted another probe/bank");
            }
            foreach(uint tap in new[]{0U,1U,3U,0x103U,0x202U,0x303U}){
                byte[] d;var config=Configure(golden,tap,20000,new[]{Channel(2,0)},out d);Reject(config,d,"Unknown SPI tap accepted");
            }
            byte[] legacy;var mismatch=Configure(golden,0x203,20000,new[]{Channel(2,0)},out legacy);
            Reject(mismatch,legacy,"Legacy P3 tap silently relabeled P2 data");
            Console.WriteLine("PASS "+checks+" actual SDK SPI tap, legacy, CRC and source-identity checks; no hardware");return 0;
        }catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
