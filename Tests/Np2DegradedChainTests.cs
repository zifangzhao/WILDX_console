using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using USB512_interface;

// Canonical source only. No devices, Forms, discovery or transport is created.
internal static class Np2DegradedChainTests
{
    static int checks;
    static void Need(bool value,string why){checks++;if(!value)throw new Exception(why);}
    static void Put(byte[] data,int offset,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,data,offset,4);}
    static Np2ConfigRequest Request(byte port){
        var bytes=new byte[876];bytes[0]=(byte)'N';bytes[1]=(byte)'2';bytes[2]=(byte)'B';bytes[3]=(byte)'1';bytes[4]=4;bytes[5]=port;
        for(int i=0;i<32;i++)bytes[8+i]=(byte)(31+i+port);
        return new Np2ConfigRequest("{}",bytes);
    }
    static Np2ConfigReply Reply(Np2ConfigRequest request,byte verification){
        var bytes=new byte[64];bytes[0]=(byte)'N';bytes[1]=(byte)'P';bytes[2]=(byte)'R';bytes[3]=(byte)'2';bytes[4]=2;
        Put(bytes,8,71);Put(bytes,16,876);bytes[20]=3;bytes[21]=verification;bytes[22]=request.Port;bytes[23]=4;
        Put(bytes,24,Np2ConfigClient.Crc(request.Selection,request.Selection.Length));Buffer.BlockCopy(request.Selection,8,bytes,28,32);
        Put(bytes,60,Np2ConfigClient.Crc(bytes,60));return Np2ConfigClient.Parse(bytes,Np2ConfigClient.Status,71);
    }
    static bool Accepted(Np2ConfigReply reply,Np2ConfigRequest request){try{Np2ConfigClient.RequireApplied(reply,request);return true;}catch(IOException){return false;}}
    static void ReceiptMatrix(){
        for(byte port=0;port<3;port++){
            var request=Request(port);var original=(byte[])request.Selection.Clone();
            for(int value=0;value<256;value++){
                var reply=Reply(request,(byte)value);int mask=value>>4;
                bool unused=(value&15)==2 && mask>=1 && mask<=14;
                bool unverified=(value&15)==3 && mask>=1 && mask<=15;
                bool expected=value==1 || unused || unverified;
                Need(Accepted(reply,request)==expected,"Unexpected verification admission: "+value);
                if(expected){
                    string warning=Np2ConfigClient.AppliedWarning(reply);
                    Need((warning==null)==(value==1),"Full and partial verification warning mismatch");
                    if(warning!=null){
                        Need(unverified==warning.Contains("Mapping/SR unverified"),"Wrong degraded verification warning");
                        for(int shank=0;shank<4;shank++)Need(warning.Contains("P"+(port+1)+" shank "+shank)==((mask&(1<<shank))!=0),"Wrong physical probe/shank warning");
                    }
                }
            }
            foreach(Action<Np2ConfigReply> mutate in new Action<Np2ConfigReply>[] {
                r=>r.Error=1,r=>r.PortState=2,r=>r.Model=3,r=>r.Port=(byte)((port+1)%3),r=>r.PayloadCrc^=1,r=>r.ConfigurationId[7]^=1
            }){var reply=Reply(request,0xA3);mutate(reply);Need(!Accepted(reply,request),"Degraded receipt bypassed required identity/error checks");}
            for(byte error=1;error<=12;error++){var reply=Reply(request,0x13);reply.Error=error;Need(!Accepted(reply,request),"Firmware setup/I2C/timeout/base error was accepted");}
            Need(request.Selection.SequenceEqual(original),"Admission or diagnostics remapped the request");
        }
    }
    static void SourceFlags(){
        for(int port=0;port<3;port++)for(int shank=0;shank<4;shank++){
            var bytes=new byte[144];Put(bytes,0,NpV2SourceStatus.MagicValue);Put(bytes,4,1);Put(bytes,8,11);
            Put(bytes,16,16);Put(bytes,84,1U<<(port*6+shank));
            var source=NpV2SourceStatus.Parse(bytes);string warning=source.DegradedWarning;
            Need(warning!=null && warning.Contains("P"+(port+1)+" shank "+shank) && warning.Contains("failed shank SR_OUT_OK") && warning.Contains("Consult the applied configuration receipt") && warning.Contains("SR/mapping may be unverified") && !warning.Contains("unused-shank") && !warning.Contains("requested mapping retained"),"DEGRADED flag/report mismatch");
            Put(bytes,16,1U<<16);Need(NpV2SourceStatus.Parse(bytes).DegradedWarning==null,"DEGRADED interpreted as bit 16");
            Put(bytes,16,2);Need(NpV2SourceStatus.Parse(bytes).DegradedWarning==null,"Full CHAINS verification marked degraded");
            Put(bytes,16,16);Put(bytes,12,8);Need(NpV2SourceStatus.Parse(bytes).DegradedWarning==null,"Fatal source failure described as accepted degradation");
        }
    }
    static void FatalContext(){
        var bytes=new byte[144];Put(bytes,0,NpV2SourceStatus.MagicValue);Put(bytes,4,1);Put(bytes,8,11);
        Put(bytes,16,16);Put(bytes,84,1); // Earlier tolerated P1 unused shank 0.
        var source=NpV2SourceStatus.Parse(bytes);
        Need(source.DegradedWarning.Contains("P1 shank 0"),"Initial unused-shank warning missing");
        var capture=new NpV2CaptureStatus{Words=new uint[32]};
        capture.Words[0]=NpV2CaptureStatus.MagicValue;capture.Words[1]=1;capture.Words[2]=255;capture.Words[3]=7;
        capture.Words[21]=3;capture.Words[24]=3;capture.Words[25]=3;capture.Words[26]=20000;
        source.Words[2]=255;source.Words[3]=capture.Words[23]=5;
        source.Words[4]=16U|0x100U|(1U<<9)|(7U<<16)|(14U<<24);
        source.Words[7]=4;source.Words[18]=3;source.Words[19]=1;source.Words[20]=17;
        string report=NpV2RecordingDiagnostics.Explain("Preparation",capture,source,null,"modeled P2 NACK");
        Need(report.Contains("Stage: Probe setup / I2C transfer/readback failed / Probe configuration / shift-chain programming"),"Fatal source stage was lost");
        Need(report.Contains("Probe: P2 (I2C3) / shank 1, pass 1, byte 17.") && !report.Contains("Probe: P1"),"Historical P1 comparator failure replaced fatal P2 context");
        Need(report.Contains("Comparator failure history: P1 shank 0.") && report.Contains("I2C=0x00000004 (NACK)") && report.Contains("Detail: modeled P2 NACK"),"Fatal report lost comparator history or actual cause");
        Need(source.DegradedWarning==null,"Fatal I2C failure was certified as tolerated degradation");
        source.Words[4]=0;
        report=NpV2RecordingDiagnostics.Explain("Preparation",capture,source,null,null);
        Need(report.Contains("Probe: not identified;") && report.Contains("Comparator failure history: P1 shank 0."),"Unidentified I2C failure falsely blamed historical comparator probe");
        source.Words[3]=capture.Words[23]=8;source.Words[7]=0;
        source.Words[4]=0x100U|(1U<<9)|(7U<<16)|(15U<<24);
        source.Words[18]=5;source.Words[19]=0;source.Words[20]=160;source.Words[21]=1U|(1U<<9);
        report=NpV2RecordingDiagnostics.Explain("Preparation",capture,source,null,null);
        Need(report.Contains("shift-register verification failed / Probe configuration / shift-chain status verification") && report.Contains("Probe: P2 (I2C3) / shank 3, pass 0, byte 160."),"Standalone SHIFT failure lost actual context");
        Need(report.Contains("Comparator failure history: P1 shank 0, P2 shank 3."),"SHIFT failure history incomplete");
        source.Words[4]=0;source.Words[21]=1U<<9;
        report=NpV2RecordingDiagnostics.Explain("Preparation",capture,source,null,null);
        Need(report.Contains("shift-register verification failed.") && report.Contains("Probe: P2 shank 3.") && report.Contains("Comparator failure history: P2 shank 3."),"SHIFT bitmap fallback lost probe/shank naming");
    }
    static void Ble(){
        for(byte port=0;port<3;port++)foreach(byte verification in new byte[]{1,0x12,0x62,0xE2,0x13,0x63,0xF3,2,0xF2,3}){
            var request=Request(port);var receipts=new NpBleConfigurationReceipts();receipts.Expect(new[]{request});
            var reply=Reply(request,verification);string report=receipts.Observe(4,(p,c)=>Task.FromResult(reply),CancellationToken.None).GetAwaiter().GetResult();
            int mask=verification>>4;bool expected=verification==1 || ((verification&15)==2 && mask>=1 && mask<=14) || ((verification&15)==3 && mask>=1 && mask<=15);
            Need(report.Contains("NOT confirmed applied")==!expected && receipts.Pending==0,"BLE claimed unknown/staged receipt applied");
            if(expected)Need(report.Contains("DEGRADED")== (verification!=1),"BLE accepted a partial receipt without warning");
            if((verification&15)==3 && expected)Need(report.Contains("Mapping/SR unverified") && report.Contains("P"+(port+1)+" shank "),"BLE degraded map/SR warning lost probe/shank context");
        }
    }
    static int Main(){try{ReceiptMatrix();SourceFlags();FatalContext();Ble();Console.WriteLine("PASS "+checks+" canonical source degraded-chain checks: all 256 receipt values/all 3 ports, exact ID/CRC/model/error/state gates, no remap, source flag16/shank reports, fatal-context precedence/comparator history and BLE warnings. No app/dependency/exporter build or hardware.");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
