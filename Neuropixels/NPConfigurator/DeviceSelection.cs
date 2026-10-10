using System.Buffers.Binary;
namespace NPConfigurator;
public static class DeviceSelection {
 public const int PayloadBytes=1576;
 public static ProbeConfiguration LoadForPort(string path,int connector) {
  var c=StandardFormats.Load(path);c.Connector=connector;
  Compile(c);return c;
 }
 public static byte[] Compile(ProbeConfiguration c) {
  c.RequireValid();
  if(ProbeConfiguration.IsNp2(c.Profile))throw new NotSupportedException("V1 configuration command supports NP1 and NRIC1384 only.");
  if(c.Profile==ProbeProfile.Nric1384 && c.InternalReferenceBank!=0)throw new NotSupportedException("NRIC has no selectable internal reference bank; use bank 0.");
  byte[] p=new byte[PayloadBytes];"NPB1"u8.CopyTo(p);p[4]=(byte)(c.Profile==ProbeProfile.Np1?2:1);
  p[5]=(byte)(c.Connector-1);p[6]=(byte)c.InternalReferenceBank;
  Convert.FromHexString(c.Id).CopyTo(p,8);
  foreach(var channel in c.Channels) {
   int at=40+channel.Channel*4;
   BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(at),(ushort)(channel.Site??65535));
   p[at+2]=(byte)(Array.IndexOf(ProbeConfiguration.Gains,channel.ApGain)|Array.IndexOf(ProbeConfiguration.Gains,channel.LfpGain)<<3|
    (channel.HighPass?64:0)|(channel.Standby?128:0));
   p[at+3]=(byte)(c.Profile==ProbeProfile.Np1?Array.IndexOf(new[]{"External","Tip","Internal"},channel.Reference):
    Array.IndexOf(new[]{"All","Parity"},channel.Reference));
  }
  return p;
 }
 public static ProbeConfiguration FromPayload(byte[] payload) {
  if(payload.Length!=PayloadBytes || !payload.AsSpan(0,4).SequenceEqual("NPB1"u8) ||
     payload[4] is not (1 or 2) || payload[5]>2)
   throw new InvalidDataException("Unsupported device selection snapshot.");
  var c=ProbeConfiguration.Create(payload[4]==2?ProbeProfile.Np1:ProbeProfile.Nric1384);
  c.Connector=payload[5]+1;c.InternalReferenceBank=payload[6];c.HardwareState="DeviceSnapshotUnverified";
  var references=c.Profile==ProbeProfile.Np1?new[]{"External","Tip","Internal"}:new[]{"All","Parity"};
  foreach(var channel in c.Channels) {
   int at=40+channel.Channel*4,site=BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(at,2)),flags=payload[at+2],reference=payload[at+3];
   if(reference>=references.Length)throw new InvalidDataException("Invalid snapshot reference.");
   channel.Site=site==65535?null:site;channel.Standby=(flags&128)!=0;channel.Export=channel.Site!=null && !channel.Standby;
   channel.ApGain=ProbeConfiguration.Gains[flags&7];channel.LfpGain=ProbeConfiguration.Gains[(flags>>3)&7];
   channel.HighPass=(flags&64)!=0;channel.Reference=references[reference];
  }
  c.RequireValid();return c;
 }
}
