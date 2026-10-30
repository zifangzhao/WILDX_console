using System;
using System.IO;
using System.Linq;
using CE32_console;
using USB512_interface;

internal static partial class NpImuGridIntegrationTests {
    static void StartupWord(byte[] b,int at,int pin,int word,uint value){for(int bit=0;bit<10;bit++)if((value&(1U<<(9-bit)))!=0)b[at+word*10+bit]|=(byte)(1<<pin);}
    static void SdCertifiedStartup(){
        string reference=null;
        foreach(string mode in new[]{"valid","wrong_counter","wrong_tick","no_parent","legacy","mid_sync","counter_gap","source_index","truncated","loss_order"}){
            string p=Path.Combine(root,"sd_certified_startup_"+mode);
            using(var c=new NpV1PowerCutRecoveryTests.SparseCard(8)){
                c.FinalizeFooter(8);bool grid=mode!="legacy";
                if(grid){Put(c.Header,104,NpImuGridWire.Magic);Put(c.Header,108,2);Put(c.Footer,104,NpImuGridWire.Magic);Put(c.Footer,108,2);}
                if(mode=="loss_order"){System.Text.Encoding.ASCII.GetBytes("NPLOSS01").CopyTo(c.Footer,320);Put(c.Footer,328,1);Put(c.Footer,344,1);}
                Seal(c.Header);Seal(c.Footer);
                for(int port=0;port<2;port++){
                    int startup=port==0?505:493;var native=new byte[4*61440+60];
                    for(int frame=0;frame*60+60<=native.Length;frame++){
                        uint counter=16458U+(uint)frame+(mode=="counter_gap"&&port==0&&frame>=1500?13U:0U);int at=frame*60;
                        StartupWord(native,at,0,0,counter%13==0?0x330U:0xcfU);StartupWord(native,at,7,2,0x11);StartupWord(native,at,7,3,counter>>10);StartupWord(native,at,7,4,counter&1023);
                    }
                    if(mode=="mid_sync"&&port==0)native[1500*60+5]^=1;
                    for(int pair=0;pair<4;pair++){
                        var chunk=c.Chunks[pair*2+port];
                        for(int i=0;i<61440;i++){int source=pair*61440+i-startup;chunk[i%4*15360+i/4]=source<0?(byte)0:native[source];}
                    }
                }
                foreach(var item in c.Chunks)if(grid){var d=item.Value;Put(d,61632+104,NpImuGridWire.Magic);Put(d,61632+108,2);int offset=0;
                    if(item.Key==0){var phase=Phase();phase.CopyTo(d,62144);offset=phase.Length;}var frame=Frame((ulong)(item.Key+1)*3);frame.CopyTo(d,62144+offset);Put(d,61632+124,(uint)(offset+frame.Length));Put(d,61632+508,NpImuGridCarrier.Crc(d,61632,508));}
                if(mode=="source_index")c.Descriptor(4,92,99);if(mode=="truncated")c.Size--;
                var receipt=Receipt();Put64(receipt,52,mode=="wrong_tick"?506UL:505UL);
                for(int port=0;port<2;port++){
                    int at=64+port*20;ulong tick=port==0?(mode=="wrong_tick"?506UL:505UL):493UL;Put64(receipt,at,tick);Put(receipt,at+8,mode=="wrong_counter"&&port==0?16471U:16458U);
                    ushort phase=(ushort)((780-tick%780)%780);receipt[at+12]=(byte)phase;receipt[at+13]=(byte)(phase>>8);
                }Seal(receipt);
                var original=c.Chunks.Values.SelectMany(n=>n).ToArray();
                using(var input=new CardOverlay(c,mode!="no_parent"&&mode!="legacy",receipt:receipt)){
                    Action export=()=>NpCe64Export.Export(input,8192,c.ReservedEnd,p,null,null);
                    if(mode=="truncated"||mode=="source_index"){Reject(export,"Incomplete/incorrect stored extent acquired certified startup");continue;}export();
                }
                Need(c.Chunks.Values.SelectMany(n=>n).SequenceEqual(original),"Startup classification modified recorded bytes");
                string report=File.ReadAllText(Path.Combine(p,"capture_status.txt"));bool good=mode=="valid";
                Need(report.Contains("lossless="+good)&&report.Contains("np1_startup_alignment_certified="+good),"Unbound startup/counter/continuity was certified: "+mode);
                if(grid&&mode!="no_parent"){
                    string encoding=File.ReadAllText(Path.Combine(p,"analogin_encoding.txt"));Need(encoding.Contains("grid_phase_qualified="+good)&&encoding.Contains("source_window_verified="+good),"Startup proof incorrectly granted/lost full retained source interval: "+mode);
                }
                if(good){reference=p;var rows=File.ReadAllText(Path.Combine(p,"first_sample_timestamps.json"));Need(rows.Contains("505")&&rows.Contains("493"),"Canonical first rows lost certified initial positions");}
                else if(mode=="wrong_counter"||mode=="wrong_tick"||mode=="no_parent"||mode=="legacy")foreach(int port in new[]{1,2})foreach(string band in new[]{"ap","lfp"}){
                    string file="np"+port+"_"+band+".dat";Need(File.ReadAllBytes(Path.Combine(reference,file)).SequenceEqual(File.ReadAllBytes(Path.Combine(p,file))),"Startup evidence changed saved neural values");}
            }
        }
    }
}
