using System;
using System.Drawing;
using System.IO;
using System.Text;

namespace USB512_interface
{
    internal static class NpAudioPreview
    {
        const int ListeningHz=48000,ClipSeconds=10;
        // A bounded listening copy. The native high-rate microphone recording
        // is never rewritten. Fractional box averaging prevents simple sample
        // skipping; this is a listening preview, not an analysis conversion.
        internal static MemoryStream ReadClip(string path)
        {
            using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
            using(var reader=new BinaryReader(input)) {
                if(input.Length<44 || Encoding.ASCII.GetString(reader.ReadBytes(4))!="RIFF")throw new IOException("Expected a PCM WAV file.");
                uint riff=reader.ReadUInt32();if((ulong)riff+8>(ulong)input.Length || Encoding.ASCII.GetString(reader.ReadBytes(4))!="WAVE")throw new IOException("Truncated WAV header.");
                long limit=(long)riff+8,dataAt=0;uint dataBytes=0,hz=0;bool format=false;
                while(input.Position+8<=limit) {
                    string kind=Encoding.ASCII.GetString(reader.ReadBytes(4));uint count=reader.ReadUInt32();long at=input.Position;
                    if(at+count>limit)throw new IOException("Truncated WAV chunk.");
                    if(kind=="fmt ") {
                        if(count<16 || reader.ReadUInt16()!=1 || reader.ReadUInt16()!=1)throw new IOException("Use mono 16-bit PCM audio.");
                        hz=reader.ReadUInt32();uint bytesPerSecond=reader.ReadUInt32();ushort alignment=reader.ReadUInt16(),bits=reader.ReadUInt16();
                        if(hz<1000 || hz>240000 || bits!=16 || alignment!=2 || bytesPerSecond!=hz*2)throw new IOException("Unsupported PCM rate/encoding.");
                        format=true;
                    } else if(kind=="data"){dataAt=at;dataBytes=count;}
                    input.Position=at+count+(count&1);
                }
                if(!format || dataAt==0 || dataBytes==0 || (dataBytes&1)!=0)throw new IOException("WAV samples are missing or truncated.");
                int samples=(int)Math.Min(dataBytes/2,hz*ClipSeconds);input.Position=dataAt;
                var source=new short[samples];for(int i=0;i<samples;i++)source[i]=reader.ReadInt16();
                int outputSamples=(int)((long)samples*ListeningHz/hz);var output=new MemoryStream(44+outputSamples*2);
                using(var writer=new BinaryWriter(output,Encoding.ASCII,true)) {
                    writer.Write(Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+outputSamples*2);writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                    writer.Write(16);writer.Write((ushort)1);writer.Write((ushort)1);writer.Write(ListeningHz);writer.Write(ListeningHz*2);writer.Write((ushort)2);writer.Write((ushort)16);
                    writer.Write(Encoding.ASCII.GetBytes("data"));writer.Write(outputSamples*2);
                    for(int i=0;i<outputSamples;i++) {
                        long from=(long)i*hz,to=(long)(i+1)*hz,sum=0;
                        for(int sample=(int)(from/ListeningHz);sample<samples && (long)sample*ListeningHz<to;sample++)
                            sum+=source[sample]*(Math.Min(to,(long)(sample+1)*ListeningHz)-Math.Max(from,(long)sample*ListeningHz));
                        writer.Write((short)(sum/hz));
                    }
                }
                output.Position=0;return output;
            }
        }
        internal static Bitmap Waveform(MemoryStream clip)
        {
            var image=new Bitmap(800,240);byte[] data=clip.GetBuffer();int samples=((int)clip.Length-44)/2;
            using(var graphics=Graphics.FromImage(image))using(var pen=new Pen(WildXTheme.Accent)) {
                graphics.Clear(WildXTheme.Surface);
                for(int x=0;x<image.Width && samples>0;x++) {
                    int first=x*samples/image.Width,last=Math.Max(first+1,(x+1)*samples/image.Width);short low=short.MaxValue,high=short.MinValue;
                    for(int sample=first;sample<last && sample<samples;sample++){short value=BitConverter.ToInt16(data,44+sample*2);low=Math.Min(low,value);high=Math.Max(high,value);}
                    graphics.DrawLine(pen,x,120-high*110f/32768,x,120-low*110f/32768);
                }
            }
            return image;
        }
    }
}
