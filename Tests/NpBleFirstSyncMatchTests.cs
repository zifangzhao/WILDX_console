using System;
using System.IO;
using USB512_interface;

static class NpBleFirstSyncMatchTests {
    const string Uid="001D00065942500B20313354";
    static int checks;
    static void Need(bool value,string why){checks++;if(!value)throw new Exception(why);}
    static void Put(byte[] b,int at,uint n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,at,4);}
    static void Seal(byte[] b){Put(b,60,NpFirstSyncWire.Crc(b,60));}
    // This runner extracts the actual guarded dispatch block. The surrounding
    // fixed-body length/CRC/command checks mirror its preceding transport gates.
    static bool Matches(byte command,byte[] request,byte receivedCommand,byte[] response){
        if(response==null||response.Length!=64||BitConverter.ToUInt32(response,60)!=NpFirstSyncWire.Crc(response,60))return false;
        if(command!=receivedCommand||request==null||request.Length!=64)return false;
        uint magic=BitConverter.ToUInt32(request,0);
ACTUAL_NPSD_MATCH
        return false;
    }
    static int Main(){try{
#if NP_FIRST_SYNC_ENABLE
        bool enabled=true;
#else
        bool enabled=false;
#endif
        for(int family=1;family<=2;family++)foreach(bool scheduled in new[]{false,true}){
            if(family==1&&scheduled)continue;
            var legacy=new byte[64];Put(legacy,0,family==1?0x31444c4eU:0x32444c4eU);Put(legacy,4,(uint)family);Put(legacy,8,scheduled?4U:2U);Put(legacy,12,57);Put(legacy,16,3);Put(legacy,20,20000);
            if(family==1){Put(legacy,24,8192);Put(legacy,28,1048576);Put(legacy,32,100);}else{Put(legacy,24,400);if(scheduled){Put(legacy,28,1234);Put(legacy,32,5678);}}Seal(legacy);
            foreach(ushort version in family==1?new ushort[]{1,2,3}:new ushort[]{1,2,3,4,5})foreach(int operation in new[]{0,1,2}){
                var q=operation==1?(version==5?NpFirstSyncSd.WrapLegacyGrid16Append(legacy,family,Uid,123456789UL):version==4?NpFirstSyncSd.WrapLegacyGrid16(legacy,family,Uid,123456789UL):version==3?NpFirstSyncSd.WrapLegacyFineCounter64(legacy,family,Uid,123456789UL):version==2?NpFirstSyncSd.WrapLegacyFineAux(legacy,family,Uid,123456789UL):NpFirstSyncSd.WrapLegacy(legacy,family,Uid,123456789UL)):
                    version==5?NpFirstSyncSd.QueryGrid16Append(operation==0?NpFirstSyncSd.Caps:NpFirstSyncSd.Status,5,family,Uid,operation==0?0:123456789UL):
                    version==4?NpFirstSyncSd.QueryGrid16(operation==0?NpFirstSyncSd.Caps:NpFirstSyncSd.Status,5,family,Uid,operation==0?0:123456789UL):
                    version==3?NpFirstSyncSd.QueryFineCounter64(operation==0?NpFirstSyncSd.Caps:NpFirstSyncSd.Status,5,family,Uid,operation==0?0:123456789UL):
                    version==2?NpFirstSyncSd.QueryFineAux(operation==0?NpFirstSyncSd.Caps:NpFirstSyncSd.Status,5,family,Uid,operation==0?0:123456789UL):NpFirstSyncSd.Query(operation==0?NpFirstSyncSd.Caps:NpFirstSyncSd.Status,5,family,Uid,operation==0?0:123456789UL);
                var r=NpFirstSyncSd.BuildReply(q,family,new NpFirstSyncSdReply{Supported=version>=3?(family==1?29U:31U):version==2?(family==1?13U:15U):family==1?1U:3U,RecordFeatures=operation==0?0U:version>=3?25U:version==2?9U:1U,ProfileCrc=UInt32.MaxValue});
                Need(Matches(0xb8,q,0xb8,r)==enabled,"Guarded NPSD valid family/scheduled/CAPS dispatch changed");
                Need(!Matches(0xb8,q,0xb9,r),"Wrong outer command accepted NPSA");
                foreach(int at in new[]{4,6,8,12,24,60}){
                    var bad=(byte[])r.Clone();bad[at]^=1;if(at!=60)Seal(bad);
                    Need(!Matches(0xb8,q,0xb8,bad),"NPSD wrong op/ID/UID/token/CRC accepted");
                }
                var requestBad=(byte[])q.Clone();requestBad[60]^=1;Need(!Matches(0xb8,requestBad,0xb8,r),"Invalid request CRC accepted by correlation");
            }
        }
        Console.WriteLine("PASS "+checks+" actual-source NPSD Matches checks; opt-in "+enabled+", no radio.");return 0;
    }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
