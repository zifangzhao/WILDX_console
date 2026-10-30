using System;using System.IO;using System.Collections.Generic;using System.Threading;using System.Threading.Tasks;using System.Windows.Forms;using WildNpConfiguration;
namespace USB512_interface {
 internal sealed class NpConfigurationUsb:INpConfigTransport {
  readonly XeNpWinUsbDevice device;
  NpConfigurationUsb(XeNpWinUsbDevice device){this.device=device;}
  public Task<byte[]> ExchangeAsync(byte[] request,CancellationToken cancel){return Task.Run(()=>device.ExchangeNpConfiguration(request,cancel),cancel);}
  internal static string Show(IWin32Window owner,string path,string initial,IDictionary<byte,NpConfigRequest> requests=null){
   int connectedMask=7;
   try {using(var probe=XeNpWinUsbDevice.OpenForControl(path)) {
    NpModeCapabilities caps=probe.GetModeCapabilities();
    if(caps!=null)connectedMask=caps.AvailableMask;
   }} catch { /* legacy firmware: retain all three choices for manual selection */ }
   using(var form=new NpConfigurationDialog(initial,availableMask:connectedMask,retainedV1Requests:requests,
    receipt:async(port,json)=>{using(var device=XeNpWinUsbDevice.OpenForControl(path)){
     var reply=await new NpConfigClient(new NpConfigurationUsb(device)).StatusAsync((byte)(port-1),CancellationToken.None);
     NpConfigRequest request=json==null?null:NpConfigurationDialog.PrepareV1Request(json,port);
     return NpConfigurationDialog.Receipt(reply,request);}})){
    WildXTheme.Apply(form);return form.ShowDialog(owner)==DialogResult.OK?form.RequestedJson:null;
   }
  }
  internal static void StageForCapture(XeNpWinUsbDevice device,IList<NpConfigRequest> requests,int mask){
   new NpConfigClient(new NpConfigurationUsb(device)).StageForCaptureAsync(requests,mask,CancellationToken.None,true).GetAwaiter().GetResult();
  }
  internal static void ConfirmApplied(XeNpWinUsbDevice device,IList<NpConfigRequest> requests,int mask,string folder){
   var client=new NpConfigClient(new NpConfigurationUsb(device));var lines=new List<string>();
   foreach(var request in requests)if((mask&(1<<request.Port))!=0){
    var reply=client.StatusAsync(request.Port,CancellationToken.None).GetAwaiter().GetResult();
    NpConfigClient.RequireApplied(reply,request.Selection);
    lines.Add("P"+(request.Port+1)+": applied; ID="+BitConverter.ToString(reply.ConfigurationId).Replace("-","")+"; CRC32="+reply.PayloadCrc.ToString("X8")+"; verification="+reply.Verification);
   }
   lines.Add("Firmware receipt only: NP1 base readback / shank ACK; NRIC ready indication. Analog signals not verified.");
   File.WriteAllLines(Path.Combine(folder,"requested-configuration-applied-receipts.txt"),lines);
  }
 }
}
