using NPConfigurator;
using WildNpConfiguration;

internal static class ImroDeviceTests
{
    public static async Task Run(string[] files)
    {
        int checks=0;
        void Check(bool condition,string message){checks++;if(!condition)throw new Exception(message);}
        void Reject(Action action,string message){try{action();}catch(IOException){checks++;return;}catch(InvalidDataException){checks++;return;}catch(NotSupportedException){checks++;return;}throw new Exception(message);}
        var requests=new Dictionary<byte,NpConfigRequest>();
        for(int i=0;i<files.Length;i++){
            int port=i==1?2:1;
            var config=DeviceSelection.LoadForPort(files[i],port);
            byte[] selection=DeviceSelection.Compile(config);
            Check(config.Profile==ProbeProfile.Np1 && config.ModelRevision=="0","Actual user IMRO must be NP1.");
            Check(config.Connector==port && selection[5]==port-1 && selection[4]==2,"Dropdown port and NP1 payload must agree.");
            Check(selection.Length==1576 && config.SamplingChannels.Length==383,"Complete NP1 payload and reserved reference channel.");
            foreach(var channel in config.Channels){
                int at=40+channel.Channel*4;
                int site=selection[at]|selection[at+1]<<8;
                Check(site==(channel.Site??65535),"IMRO routing must reach the device selection unchanged.");
                Check(channel.ApGain==500 && channel.LfpGain==250 && channel.Reference=="External" && channel.HighPass,"Actual file gains/reference/filter.");
                Check(selection[at+2]==(83|(channel.Standby?128:0)) && selection[at+3]==0,"Packed AP/LFP/filter/standby/reference settings.");
            }
            Check(ProbeConfiguration.FromJson(config.ToJson()).Id==config.Id,"Prepared JSON must preserve the configuration ID.");
            var request=new NpConfigRequest(config.ToJson(),selection);
            if(i<2)requests[request.Port]=request;
            Console.WriteLine("PASS import P"+port+": "+files[i]+"; ID="+config.Id+"; CRC32="+NpConfigClient.Crc(selection,selection.Length).ToString("X8"));
        }
        Check(requests.Count==2 && requests[0].Port==0 && requests[1].Port==1,"Per-port requests must preserve P1 and P2 simultaneously.");
        Check(ProbeConfiguration.FromJson(requests[0].Json).Id=="21e543e77ee25577da56b8285660fd88961fa9fd6d1dded23775bacea36e2b51","User P1 identity regression.");
        Check(ProbeConfiguration.FromJson(requests[1].Json).Id=="f004b1a71786eb4cc4ea19e460bf2e9cab9f997f660972450bfdb1f5bb7ba120","User P2 identity regression.");
        var p3=DeviceSelection.LoadForPort(files[0],3);
        requests[2]=new NpConfigRequest(p3.ToJson(),DeviceSelection.Compile(p3));
        Check(p3.Connector==3 && requests[2].Port==2 && p3.Channels[0].Site==ProbeConfiguration.FromJson(requests[0].Json).Channels[0].Site,"P3 uses the selected connector while preserving representative NP1 routing.");
        Check(p3.Id=="e42da634d166f8a5bc4a78fb76978013728d8323b256e260998725d684cd94a5","Representative P3 identity regression.");
        Check(requests.Count==3 && requests[0].Port==0 && requests[1].Port==1,"Adding P3 must preserve P1 and P2 requests.");
        Reject(()=>DeviceSelection.Compile(ProbeConfiguration.Create(ProbeProfile.Np2Single)),"A real NP2/V1 mismatch must remain rejected.");
        Reject(()=>DeviceSelection.Compile(ProbeConfiguration.Create(ProbeProfile.Np2Multi)),"A real four-shank NP2/V1 mismatch must remain rejected.");
        Reject(()=>DeviceSelection.LoadForPort(files[0],4),"Out-of-range connected port must remain rejected.");
        var transport=new MemoryTransport();var client=new NpConfigClient(transport);
        // Firmware 1.0.9's old mask restriction must produce its true error,
        // rather than being labelled an incompatible IMRO parser failure.
        transport.ActiveMask=2;
        try{await client.StageForCaptureAsync(new[]{requests[0]},1,CancellationToken.None);throw new Exception("Inactive-mask rejection was lost.");}
        catch(IOException ex){Check(ex.Message.Contains("P1") && ex.Message.Contains("code 7"),"Staging failure must preserve target port and firmware error.");}
        transport.Reset();transport.ActiveMask=7;
        await client.StageForCaptureAsync(requests.Values,7,CancellationToken.None);
        Check(transport.Active.Count==3,"Final-mode staging must retain all three ports.");
        foreach(var request in requests.Values){
            var reply=await client.StatusAsync(request.Port,CancellationToken.None);
            Reject(()=>NpConfigClient.RequireApplied(reply,request.Selection),"STAGED is not APPLIED.");
        }
        transport.Reset();transport.ActiveMask=7;
        foreach(var request in requests.Values){
            var reply=await client.StatusAsync(request.Port,CancellationToken.None);
            Reject(()=>NpConfigClient.RequireApplied(reply,request.Selection),"A reset to defaults must not release a configured recording.");
        }
        await client.StageForCaptureAsync(requests.Values,7,CancellationToken.None);
        transport.Apply();
        foreach(var request in requests.Values){
            var reply=await client.StatusAsync(request.Port,CancellationToken.None);
            NpConfigClient.RequireApplied(reply,request.Selection);checks++;
            reply.PayloadCrc^=1;Reject(()=>NpConfigClient.RequireApplied(reply,request.Selection),"Different applied CRC must reject capture.");
            reply=await client.StatusAsync(request.Port,CancellationToken.None);reply.ConfigurationId[0]^=1;
            Reject(()=>NpConfigClient.RequireApplied(reply,request.Selection),"Different applied ID must reject capture.");
            reply=await client.StatusAsync(request.Port,CancellationToken.None);reply.Verification=2;
            Reject(()=>NpConfigClient.RequireApplied(reply,request.Selection),"Missing NP1 base readback must reject capture.");
            reply=await client.StatusAsync(request.Port,CancellationToken.None);reply.PortState=4;
            Reject(()=>NpConfigClient.RequireApplied(reply,request.Selection),"Firmware apply failure must reject capture.");
        }
        transport.Reset();transport.ActiveMask=1;
        await client.StageForCaptureAsync(requests.Values,1,CancellationToken.None);
        Check(transport.Active.Count==1 && transport.Active.ContainsKey(0),"Disabled ports must not be staged for capture.");
        Check(requests.Count==3,"Capture-mask filtering must not erase another port's host request.");
        transport.Reset();transport.ActiveMask=4;
        await client.StageForCaptureAsync(requests.Values,4,CancellationToken.None);transport.Apply();
        Check(transport.Active.Count==1 && transport.Active.ContainsKey(2),"P3-only capture stages only P3.");
        NpConfigClient.RequireApplied(await client.StatusAsync(2,CancellationToken.None),requests[2].Selection);checks++;
        var nric=DeviceSelection.Compile(ProbeConfiguration.Create(ProbeProfile.Nric1384));
        var nricReply=new NpConfigReply{PortState=3,PayloadCrc=NpConfigClient.Crc(nric,nric.Length),ConfigurationId=nric[8..40],Verification=4};
        NpConfigClient.RequireApplied(nricReply,nric);checks++;
        Console.WriteLine("PASS: "+checks+" targeted IMRO/port/reset/applied-receipt checks; memory transport only, no devices.");
    }

    private sealed class MemoryTransport:INpConfigTransport
    {
        public int ActiveMask=3;
        public readonly Dictionary<byte,byte[]> Active=new();
        readonly Dictionary<byte,byte> states=new();
        byte[] upload=new byte[1576];uint received;
        public void Reset(){Active.Clear();states.Clear();upload=new byte[1576];received=0;}
        public void Apply(){foreach(var port in Active.Keys)states[port]=3;}
        public Task<byte[]> ExchangeAsync(byte[] q,CancellationToken cancel){
            cancel.ThrowIfCancellationRequested();byte op=q[5],port=q[6],error=0;
            uint offset=BitConverter.ToUInt32(q,12),length=BitConverter.ToUInt32(q,16);
            if(op==1){upload=new byte[1576];received=0;}
            if(op==2){Buffer.BlockCopy(q,24,upload,(int)offset,(int)length);received=offset+length;}
            if(op==3){
                if((ActiveMask&(1<<port))==0)error=7;
                else{Active[port]=(byte[])upload.Clone();states[port]=2;}
            }
            var r=new byte[64];System.Text.Encoding.ASCII.GetBytes("NPR1").CopyTo(r,0);r[4]=1;r[5]=op;r[6]=error;
            Buffer.BlockCopy(q,8,r,8,4);BitConverter.GetBytes(received).CopyTo(r,12);BitConverter.GetBytes(1576U).CopyTo(r,16);
            if(Active.TryGetValue(port,out var active)){
                r[20]=states[port];r[21]=(byte)(r[20]==3?(active[4]==2?3:4):0);
                BitConverter.GetBytes(NpConfigClient.Crc(active,active.Length)).CopyTo(r,24);Buffer.BlockCopy(active,8,r,28,32);
            }
            BitConverter.GetBytes(NpConfigClient.Crc(r,60)).CopyTo(r,60);return Task.FromResult(r);
        }
    }
}
