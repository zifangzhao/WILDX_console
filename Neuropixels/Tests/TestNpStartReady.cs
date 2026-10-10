using System;
using System.IO;
using USB512_interface;

internal static class NpStartReadyTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static void Reject(Action action,string message){checks++;try{action();}catch(IOException){return;}throw new Exception(message);}
    static void Put(byte[] b,int at,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,at,4);}
    static byte[] Status(int rate,bool streaming=false,uint session=2,uint version=5,uint divider=2)
    {
        var b=new byte[344];Put(b,0,0x55534253);Put(b,4,version);Put(b,20,streaming?1U:0U);
        Put(b,260,divider);Put(b,268,(uint)rate*780);Put(b,304,0x31525356);Put(b,308,1);Put(b,312,session);
        Put(b,336,1);Put(b,340,(uint)rate);return b;
    }
    static XeNpStatus Parse(byte[] b){return XeNpStatus.Parse(b,b.Length);}
    static NpModeCapabilities Caps(int mask,int rate,bool admission=true)
    {
        var b=new byte[48];Put(b,0,0x314d504e);Put(b,4,1);Put(b,8,7);
        Put(b,12,15);Put(b,16,15);Put(b,20,3);Put(b,24,(uint)mask);Put(b,28,(uint)rate);
        Put(b,32,0x76);Put(b,36,32);Put(b,40,admission?5U:1U);return NpModeCapabilities.Parse(b,b.Length);
    }
    static NpStartupAdmission Receipt(int requested,int accepted,int rate,uint session=2,uint state=2)
    {
        var b=new byte[64];Put(b,0,0x3141504e);Put(b,4,1);Put(b,8,64);Put(b,12,state);
        Put(b,16,(uint)requested);Put(b,20,(uint)accepted);Put(b,24,(uint)(requested&~accepted));
        Put(b,28,session);Put(b,32,(uint)rate);return NpStartupAdmission.Parse(b,b.Length);
    }
    static void DiagnosticTransition()
    {
        byte[] stopped=Status(10000,false,1,5,4);var initial=Parse(stopped);
        Check(initial.ApSampleRateHz==10000 && initial.IsTransientStartupClock,"Stopped native20 MCO4 diagnostic rejected or rewritten");
        Check(NpCaptureMode.RateBit(10000)==0 && !NpCaptureMode.Valid(3,10000),"10 kHz became a recording mode");
        var requested=Caps(3,20000);var readyReceipt=Receipt(3,3,20000);
        var bringingUp=Parse(Status(10000,false,2,5,4));
        Check(bringingUp.SessionId==2 && bringingUp.IsTransientStartupClock,"New-session transient diagnostic lost");
        Reject(()=>XeNpWinUsbDevice.RequireReadyProfile(bringingUp,20000,XeNpLaneLayout.Auto,2,requested,readyReceipt),"10 kHz accepted at READY");
        Reject(()=>bringingUp.RequireCaptureProfile(10000,XeNpLaneLayout.Auto),"Diagnostic rate admitted as recording rate");
        foreach(bool streaming in new[]{false,true}) {
            var ready=Parse(Status(20000,streaming));
            XeNpWinUsbDevice.RequireReadyProfile(ready,20000,XeNpLaneLayout.Auto,2,requested,readyReceipt);checks++;
            Check(ready.ApSampleRateHz==20000 && !ready.IsTransientStartupClock,"READY rate overwritten");
        }
        foreach(uint version in new uint[]{5,6,7}) {
            var b=Status(10000,false,2,version,4);Check(Parse(b).IsTransientStartupClock,"Stopped V1 diagnostic transition unavailable");
            Put(b,20,1);Reject(()=>Parse(b),"Unsupported streaming 10 kHz accepted");
        }
        Reject(()=>Parse(Status(10000,false,2,5,2)),"Arbitrary 10 kHz clock tuple allowed");
        Reject(()=>Parse(Status(12000,false,2,5,4)),"Arbitrary unsupported stopped rate allowed");
        Reject(()=>Parse(Status(10000,false,2,8,4)),"NP1 V2 explicit 10 kHz profile allowed");
        var malformed=Status(10000,false,2,5,4);Put(malformed,268,7800001);
        Reject(()=>Parse(malformed),"Nonintegral PSB clock accepted");
        Reject(()=>XeNpStatus.Parse(stopped,75),"Truncated diagnostics accepted");
        Reject(()=>XeNpStatus.Parse(Status(30000,false,2,8),340),"Truncated explicit capture profile accepted");
        malformed=Status(20000,true,2,8);Put(malformed,268,7800000);
        Reject(()=>Parse(malformed),"Streaming explicit rate concealed contradictory physical clock");
    }
    static void ReadyContracts()
    {
        foreach(int rate in new[]{7500,15000,20000,30000})for(int mask=1;mask<=7;mask++) {
            if(!NpCaptureMode.Valid(mask,rate))continue;
            var requested=Caps(mask,rate);var status=Parse(Status(rate));
            for(int accepted=1;accepted<=7;accepted++) {
                if((accepted&~mask)!=0 || !NpCaptureMode.Valid(accepted,rate))continue;
                var receipt=Receipt(mask,accepted,rate);
                XeNpWinUsbDevice.RequireReadyProfile(status,rate,XeNpLaneLayout.Auto,2,requested,receipt);checks++;
                Reject(()=>XeNpWinUsbDevice.RequireReadyProfile(status,rate,XeNpLaneLayout.Auto,3,requested,receipt),"Changed session admitted");
                Reject(()=>XeNpWinUsbDevice.RequireReadyProfile(status,rate,XeNpLaneLayout.Gpio,2,requested,receipt),"Changed layout admitted");
                var wrongRate=Parse(Status(rate==15000?30000:15000));
                Reject(()=>XeNpWinUsbDevice.RequireReadyProfile(wrongRate,rate,XeNpLaneLayout.Auto,2,requested,receipt),"Wrong supported actual rate admitted");
            }
            var active=Parse(Status(rate,true));
            XeNpWinUsbDevice.RequireReadyProfile(active,rate,XeNpLaneLayout.Auto,2,Caps(mask,rate,false),null);checks++;
            Reject(()=>XeNpWinUsbDevice.RequireReadyProfile(status,rate,XeNpLaneLayout.Auto,2,requested,null),"Fresh no-admission path returned pre-streaming status");
        }
        var caps=Caps(3,20000);var ready=Parse(Status(20000));
        Reject(()=>XeNpWinUsbDevice.RequireReadyProfile(ready,20000,XeNpLaneLayout.Auto,2,caps,Receipt(3,3,15000)),"Changed admission rate accepted");
        Reject(()=>XeNpWinUsbDevice.RequireReadyProfile(ready,20000,XeNpLaneLayout.Auto,2,caps,Receipt(3,3,20000,3)),"Changed receipt session accepted");
        Reject(()=>XeNpWinUsbDevice.RequireReadyProfile(ready,20000,XeNpLaneLayout.Auto,2,caps,Receipt(1,1,20000)),"Changed requested mask accepted");
        Reject(()=>XeNpWinUsbDevice.RequireReadyProfile(ready,20000,XeNpLaneLayout.Auto,2,caps,Receipt(3,3,20000,2,1)),"Starting receipt accepted as READY");
        var fault=Status(20000);Put(fault,324,0xBADA0001);
        Reject(()=>XeNpWinUsbDevice.RequireReadyProfile(Parse(fault),20000,XeNpLaneLayout.Auto,2,caps,Receipt(3,3,20000)),"Fresh continuity fault ignored");
        var legacy=Parse(Status(30000,true,0,8));
        XeNpWinUsbDevice.RequireReadyProfile(legacy,30000,XeNpLaneLayout.Gpio,0,Caps(1,30000,false),null);checks++;
        Reject(()=>XeNpWinUsbDevice.RequireReadyProfile(Parse(Status(30000,false,0,8)),30000,XeNpLaneLayout.Gpio,0,null,null),"Legacy pre-START profile returned");
        Reject(()=>XeNpWinUsbDevice.RequireReadyProfile(legacy,30000,XeNpLaneLayout.Gpio,0,Caps(1,15000,false),null),"Changed legacy capability rate accepted");
        var pssi=Status(30000,true,0,8);Put(pssi,336,2);
        XeNpWinUsbDevice.RequireReadyProfile(Parse(pssi),30000,XeNpLaneLayout.Pssi,0,null,null);checks++;
        pssi=Status(20000,true,0,8);Put(pssi,336,2);
        Reject(()=>XeNpWinUsbDevice.RequireReadyProfile(Parse(pssi),20000,XeNpLaneLayout.Pssi,0,null,null),"Unsupported PSSI rate accepted");
    }
    static void ParserPlanAndSubset(string output)
    {
        string folder=Path.Combine(output,"planned-profile-"+Guid.NewGuid().ToString("N"));
        using(var parser=new NpCaptureSession()) {
            var diagnostic=Parse(Status(10000,false,1,5,4));
            parser.Configure(folder,diagnostic,Caps(3,20000),new NpCaptureOptions(false,false));
            Check(parser.ApHz==20000 && parser.ProbeMask==3 && diagnostic.ApSampleRateHz==10000,"Parser plan erased diagnostics or used transient rate");
            var ready=Parse(Status(20000));
            XeNpWinUsbDevice.RequireReadyProfile(ready,20000,XeNpLaneLayout.Auto,2,Caps(3,20000),Receipt(3,2,20000));
            parser.ReconfigureBeforeFirstData(folder,ready,Caps(2,20000),new NpCaptureOptions(false,false));
            Check(parser.ApHz==ready.ApSampleRateHz && parser.ProbeMask==2 && parser.Probes.Count==1 && parser.Probes[0].Probe==2,"Accepted-mask reconfiguration changed rate/port");
            Check(parser.Packets==0 && parser.Probes[0].ApRows==0 && parser.Probes[0].LfpRows==0,"Planning consumed sample data");
        }
    }
    static void SourceFlow(string root)
    {
        string device=File.ReadAllText(Path.Combine(root,"XeNpWinUsb.cs")),main=File.ReadAllText(Path.Combine(root,"mainForm.cs"));
        int begin=device.IndexOf("internal XeNpStatus Start(",StringComparison.Ordinal),end=device.IndexOf("internal static void RequireReadyProfile",begin,StringComparison.Ordinal);
        string start=device.Substring(begin,end-begin);
        Check(!start.Contains("return status;"),"START retained stale pre-START return path");
        Check(start.Contains("if(!current.Streaming)continue;") && start.Contains("RequireReadyProfile(current,expectedHz,status.Layout,expectedSession,startCaps,null)"),"Legacy/fresh no-admission START bypasses READY");
        int complete=device.IndexOf("internal void CompleteStartupAdmission",StringComparison.Ordinal);
        Check(device.IndexOf("RequireReadyProfile(GetStatus()",complete,StringComparison.Ordinal)<device.IndexOf("VendorOut(0x78",complete,StringComparison.Ordinal),"Release bypasses final actual-clock/session validation");
        Check(!main.Contains("profile.ApSampleRateHz=modeCaps.ApHz"),"Actual diagnostic rate still overwritten by requested rate");
        Check(main.Contains("int captureRateHz=modeCaps==null?profile.ApSampleRateHz:modeCaps.ApHz;"),"Parser/preview plan rate is not distinct from actual diagnostics");
        int startup=main.IndexOf("profile=device.Start(XeNpMcoDivider)",StringComparison.Ordinal);
        Check(main.IndexOf("if(profile.ApSampleRateHz!=parser.ApHz)",startup,StringComparison.Ordinal)<main.IndexOf("device.CompleteStartupAdmission()",startup,StringComparison.Ordinal),"Parser rate not verified before data release");
    }
    static int Main(string[] args)
    {
        var stopped=new XeNpStatus {Streaming=false};stopped.RequireStopped();checks++;
        Reject(()=>new XeNpStatus {Streaming=true}.RequireStopped(),"STOP ACK without stopped status accepted");
        Reject(()=>new XeNpStatus {Streaming=false,RecordingFault=0xBADA0013}.RequireStopped(),"Stopped status hid the firmware fault");
        Directory.CreateDirectory(args[0]);DiagnosticTransition();ReadyContracts();ParserPlanAndSubset(args[0]);SourceFlow(args[1]);
        Console.WriteLine("PASS: "+checks+" stopped native20 diagnostic-to-READY, rate/layout/session/mask/fault/legacy/parser/subset/release checks");
        Console.WriteLine("10 kHz remains unsupported for capture; only stopped V1 MCO4/7.8MHz diagnostics are allowed. No hardware access.");return 0;
    }
}
