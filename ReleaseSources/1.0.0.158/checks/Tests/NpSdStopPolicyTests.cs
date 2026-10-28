using System;
using System.Threading.Tasks;
using CE32_console;

// Writer/catalog completion is independent of an optional full payload reread.
// Compile against the actual source helpers; no radio, USB or SD is opened.
static class NpSdStopPolicyTests
{
    static int checks;
    static void Need(bool value,string why){checks++;if(!value)throw new Exception(why);}
    static NpSdStatus Status(uint state,uint flags=0,uint fault=0)
    {return new NpSdStatus{State=state,Flags=flags,Fault=fault,Mask=3,Hz=20000,Start=8192,Capacity=1048576};}
    static void Completion()
    {
        foreach(uint state in new uint[]{0,1,5,7,255}){
            foreach(uint flags in new uint[]{0,1,2,64,128,512})
                Need(NpV2SdControl.StopAlreadySatisfied(Status(state,flags)),"V2 terminal receipt acquired a validation/capability prerequisite");
            foreach(uint busy in new uint[]{4,8,12})
                Need(!NpV2SdControl.StopAlreadySatisfied(Status(state,busy)),"V2 terminal state ignored live/pending ownership");
        }
        foreach(uint state in new uint[]{2,3,4,6})
            Need(!NpV2SdControl.StopAlreadySatisfied(Status(state)),"V2 nonterminal phase was inferred complete");
        foreach(uint flags in new uint[]{0,4,8,12}){
            var legacy=Status(4,flags);
            Need(NpV2SdControl.StopIsDraining(legacy),"Legacy finalization no longer observed");
            Need(!NpV2SdControl.StopAlreadySatisfied(legacy),"Legacy validation/drain phase was bypassed");
        }
        Need(!NpV2SdControl.StopAlreadySatisfied(null)&&!NpV2SdControl.StopIsDraining(null),"Missing V2 receipt invented completion");
        Need(NpV2SdControl.StopAlreadySatisfied(Status(255,0,9)),"Stopped V2 failure was forced to Complete");
        Need(NpV2SdControl.Describe(Status(255,0,9)).Contains("Stopped after recording failure"),"V2 terminal fault was hidden");
        Need(NpV2SdControl.Describe(Status(4)).Contains("Finalizing SD"),"V2 finalization label changed");
        Need(!NpV2SdControl.Describe(Status(4)).Contains("verif")&&!NpV2SdControl.OnlineState(Status(4)).Contains("verif"),"V2 normal Stop text promises payload verification");
        foreach(uint state in new uint[]{0,3,4}){
            foreach(uint flags in new uint[]{0,1,2,128,512})
                Need(NpV1BleProtocol.StopAlreadySatisfied(Status(state,flags)),"V1 terminal receipt acquired a validation prerequisite");
            foreach(uint owner in new uint[]{4,8,12,NpV1BleProtocol.Waiting,NpV1BleProtocol.Preparing})
                Need(!NpV1BleProtocol.StopAlreadySatisfied(Status(state,owner)),"V1 terminal state ignored active preparation/write ownership");
        }
        foreach(uint state in new uint[]{1,2})Need(!NpV1BleProtocol.StopAlreadySatisfied(Status(state)),"V1 recording/draining was inferred stopped");
        Need(!NpV1BleProtocol.StopAlreadySatisfied(null),"Missing V1 receipt invented completion");
        Need(NpV1BleProtocol.StateText(Status(2)).Contains("draining SD"),"V1 still needs essential drain phase");
    }
    static async Task NoReplay()
    {
        int sends=0;
        var lost=new NpSdControl(q=>{sends++;throw new TimeoutException("Missing Stop acknowledgement");});
        bool failed=false;
        try{await lost.Send(NpSdControl.Request(3,41));}catch(TimeoutException){failed=true;}
        Need(failed&&sends==1,"Missing Stop receipt was replayed");
        var v2=NpV2SdControl.Request(3,41);
        Need(BitConverter.ToUInt32(v2,8)==3&&BitConverter.ToUInt32(v2,12)==41,"V2 Stop wire changed");
        for(int at=16;at<60;at++)Need(v2[at]==0,"V2 Stop acquired a validation or format command payload");
    }
    static int Main(){try{Completion();NoReplay().GetAwaiter().GetResult();Console.WriteLine("PASS "+checks+" SD finalization/ownership/no-replay checks; no hardware.");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
