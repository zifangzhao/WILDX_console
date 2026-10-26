using NPConfigurator;
int checks=0;void Need(bool value,string message){checks++;if(!value)throw new Exception(message);}
foreach(var profile in new[]{ProbeProfile.Np1,ProbeProfile.Np2Multi}){
 var source=ProbeConfiguration.Create(profile);source.SerialNumber="22420015023";source.Connector=2;if(profile==ProbeProfile.Np2Multi)source.ModelRevision="2013";
 source.OriginalImro=System.Text.Encoding.UTF8.GetBytes(StandardFormats.ToImro(source));string original=source.Id;
 if(args.Length==1){Directory.CreateDirectory(args[0]);source.Save(Path.Combine(args[0],profile+".json"));}
 var windows=ProbeBankScan.Windows(source);Need(windows.Length==(profile==ProbeProfile.Np1?3:16),"Wrong bank count");var covered=new HashSet<(int,int)>();
 foreach(var window in windows){
  var c=ProbeBankScan.Select(source,window,2);c.RequireValid();Need(c.OriginalImro==null&&c.SerialNumber==source.SerialNumber&&c.Connector==2,"Identity/IMRO provenance lost");
  var active=c.Channels.Where(x=>!x.Standby&&x.Site!=null).ToArray();Need(active.Length==(profile==ProbeProfile.Np1?383:384),"Scan must keep ordinary channels connected");
  foreach(var channel in active){Need(channel.Site>=window.FirstSite&&channel.Site<=window.LastSite&&channel.Shank==window.Shank,"Selection escaped window");Need(channel.Channel==Catalog.Channel(profile,window.Shank,channel.Site!.Value),"Wrong physical routing");Need(channel.ApGain==source.Channels[channel.Channel].ApGain&&channel.Reference==source.Channels[channel.Channel].Reference,"Gains/references changed");covered.Add((channel.Shank,channel.Site.Value));}
  Need(StandardFormats.FromImro(StandardFormats.ToImro(c)).SamplingChannels.Length==active.Length,"IMRO roundtrip lost channels");
  if(profile==ProbeProfile.Np2Multi){var payload=Np2DeviceSelection.Compile(c);Need(payload.Length==876&&payload[4]==4&&payload[5]==1,"N2B1 model/port/size changed");}
  else {var payload=DeviceSelection.Compile(c);foreach(var ch in c.Channels)Need(System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(40+ch.Channel*4,2))==(ch.Site??65535),"NPB1 changed scanned sites");}
 }
 Need(covered.Count==(profile==ProbeProfile.Np1?957:5120),"Not every usable site covered");Need(source.Id==original,"Template was mutated");Need(windows.Last().LastSite==source.SitesPerShank-1,"Tail not covered");
 string root=Path.Combine(Path.GetTempPath(),"WILDX_ScanTest_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
 try{string input=Path.Combine(root,"input.json"),output=Path.Combine(root,"plan");source.Save(input);ProbeBankScan.Write(input,output,2);Need(File.Exists(Path.Combine(output,"plan.json")),"Manifest missing");Need(Directory.GetFiles(output,"*.imro").Length==windows.Length,"IMRO missing");bool refused=false;try{ProbeBankScan.Write(input,output,2);}catch(IOException){refused=true;}Need(refused,"Existing outputs overwritten");}
 finally{if(Path.GetFullPath(root).StartsWith(Path.Combine(Path.GetTempPath(),"WILDX_ScanTest_"),StringComparison.OrdinalIgnoreCase))Directory.Delete(root,true);}
}
Console.WriteLine("PASS "+checks+" scan coverage/routing/IMRO/device-payload/identity tests; no hardware");
