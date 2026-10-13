using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using USB512_interface;
using CE32_console;

internal static class Np1PolarityTests
{
    static int checks;
    static void Check(bool value,string reason){checks++;if(!value)throw new Exception(reason);}
    static int Channel(int adc,int slot){return (adc/2)*24+(adc&1)+slot*2;}
    static int Raw(int cycle,int adc,int slot){return (cycle+adc*137+slot*17)&1023;}
    static void Word(byte[] b,int at,int line,int word,uint value){
        for(int bit=0;bit<10;bit++)if((value&(1U<<(9-bit)))!=0)b[at+word*10+bit]|=(byte)(1<<line);
    }
    static byte[] Fixture(int layout,int rows){
        var b=new byte[rows*780];
        for(int sf=0;sf<rows;sf++)for(int f=0;f<13;f++){
            int at=(sf*13+f)*60;uint c=(uint)(sf*13+f)&0xFFFFF;
            Word(b,at,0,0,f==0?0x330U:0xCFU);Word(b,at,7,2,0x11);
            Word(b,at,7,3,c>>10);Word(b,at,7,4,c&1023);
            for(int adc=0;adc<32;adc++){
                int line=adc>=30?7:layout==3?adc/5+1:adc%6+1;
                int word=adc>=30?adc-30:layout==3?adc%5:adc/6;
                Word(b,at,line,word,(uint)Raw(sf/12,adc,f==0?sf%12:f-1));
            }
        }
        if(layout==2)for(int i=0;i<b.Length;i++)b[i]=(byte)((b[i]>>1)|(b[i]<<7));
        return b;
    }
    static void AllCodes(int layout,string folder){
        const int rows=1024*12;byte[] wire=Fixture(layout,rows),original=(byte[])wire.Clone();
        var live=new XeNpStreamDecoder((XeNpLaneLayout)layout);live.DeclareRecordingReset();
        var sd=new NpCe64Decoder((NpCe64LaneLayout)layout);sd.DeclareRecordingReset();
        short[] expectedAp=new short[rows*384],expectedLfp=new short[1024*384];
        int ap=0,lfp=0;
        Action<short[],int,short[],int> receive=(a,na,l,nl)=>{
            Check(na==1 && nl==(ap%12==11?1:0),"AP/LFP row boundary changed");
            for(int adc=0;adc<32;adc++)for(int slot=0;slot<12;slot++){
                int ch=Channel(adc,slot);
                Check(a[ch]==512-Raw(ap/12,adc,slot),"All-code AP sign/offset/mapping");
                if(nl>0)Check(l[ch]==512-Raw(lfp,adc,slot),"All-code LFP sign/offset/mapping");
            }
            Array.Copy(a,0,expectedAp,ap*384,384);if(nl>0)Array.Copy(l,0,expectedLfp,lfp*384,384);
            ap+=na;lfp+=nl;
        };
        for(int at=0;at<wire.Length;at+=131072)live.Feed(wire,at,Math.Min(131072,wire.Length-at),receive);
        live.Complete();Check(ap==rows && lfp==1024,"All-code live row count");
        ap=0;lfp=0;
        for(int at=0;at<wire.Length;at+=1973)sd.Feed(wire,at,Math.Min(1973,wire.Length-at),(a,na,l,nl)=>{
            for(int ch=0;ch<384;ch++){
                Check(a[ch]==expectedAp[ap*384+ch],"USB/SD AP output differs");
                if(nl>0)Check(l[ch]==expectedLfp[lfp*384+ch],"USB/SD LFP output differs");
            }
            ap+=na;lfp+=nl;
        });sd.Complete();
        Check(ap==rows && lfp==1024 && live.FramesChecked==rows*13 && sd.FramesChecked==live.FramesChecked,"SD counts/framing changed");
        Check(live.FrameErrors+live.CounterErrors+live.LfpPhaseErrors+sd.FrameErrors+sd.CounterErrors+sd.LfpPhaseErrors==0,"Sign conversion affected framing or phase");
        for(int i=0;i<wire.Length;i++)Check(wire[i]==original[i],"Raw transport was transformed");
        if(layout==3){
            string path=Path.Combine(folder,"all-code-ap.dat");
            using(var writer=new NpDatWriter(path,384,false))writer.Write(expectedAp,rows);
            byte[] dat=File.ReadAllBytes(path);Check(dat.Length==expectedAp.Length*2,"DAT byte count changed");
            for(int i=0;i<expectedAp.Length;i++){
                ushort bits=unchecked((ushort)expectedAp[i]);
                Check(dat[i*2]==(byte)bits && dat[i*2+1]==(byte)(bits>>8),"DAT is not signed little-endian int16");
            }
        }
    }
    static void Preview(){
        Type type=typeof(NeuropixelsPreviewForm);const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Static;
        Check((float)type.GetField("NpAdcMidscale",flags).GetRawConstantValue()==0f,"NP1 AP preview still uses unsigned midscale");
        Check((float)type.GetField("NpLfpMidscale",flags).GetRawConstantValue()==0f,"NP1 LFP fallback still uses raw pedestal");
        MethodInfo point=type.GetMethod("TracePoint",flags);
        foreach(short sample in new short[]{-511,-1,0,1,512}){
            var row=new short[64];row[0]=sample;
            var p=(PointF)point.Invoke(null,new object[]{row,0,0,0f,1f,100f,0f,0.1f});
            Check(Math.Abs(p.Y-(100f-sample*0.1f))<0.001f,"Preview polarity/zero baseline");
            Check(sample==0?p.Y==100f:sample>0?p.Y<100f:p.Y>100f,"Positive/negative trace direction");
        }
        var sweep=new NpSweepBuffer(4,4);var a=new short[64];var b=new short[64];
        a[0]=-11;b[0]=9;sweep.AddRow(a,0,0);sweep.AddRow(b,0,1);
        float[] refs=NeuropixelsPreviewForm.CaptureSweepReferences(sweep);
        Check(refs[0]==-1f && refs[1]==0f,"Frozen LFP reference did not use signed extrema");
        // Constructor reference selection is checked without showing a window.
        foreach(int bits in new[]{10,12})using(var form=new NeuropixelsPreviewForm(15000,1,bits==12,bits)){
            float reference=(float)type.GetField("apAdcMidscale",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form);
            Check(reference==0f,"NP1/NP2 signed preview reference selection");
            type.GetMethod("OnFormClosing",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(form,new object[]{new FormClosingEventArgs(CloseReason.ApplicationExitCall,false)});
        }
    }
    static short[] Row(short value){var a=new short[384];for(int ch=0;ch<a.Length;ch++)a[ch]=value;return a;}
    static void UpsamplingAndMerge(string folder){
        short[] inputs={-2,0,2,0,-2,511,-511};
        string path=Path.Combine(folder,"signed-upsample.dat");
        using(var writer=new NpDatWriter(path,1,true))foreach(short value in inputs)writer.Write(new[]{value},1);
        byte[] bytes=File.ReadAllBytes(path);
        for(int outRow=0;outRow<bytes.Length/2;outRow++){
            int quarter=outRow*3,index=quarter/4,fraction=quarter%4;
            double exact=fraction==0?inputs[index]:((4-fraction)*inputs[index]+fraction*inputs[index+1])/4.0;
            short expected=(short)Math.Round(exact,MidpointRounding.AwayFromZero);
            Check(BitConverter.ToInt16(bytes,outRow*2)==expected,"Signed resampler rounding or double transform");
        }
        Check(BitConverter.ToInt16(bytes,2)==-1 && BitConverter.ToInt16(bytes,4)==1,"Negative/positive half-boundary rounding");
        string merged=Path.Combine(folder,"merged");Directory.CreateDirectory(merged);
        using(var writer=new NpMergedDatWriter(merged,new[]{1,2},true)){
            writer.Add(2,12,Row(-511),1,Row(1),1);writer.Add(1,12,Row(512),1,Row(-1),1);
            writer.Add(1,25,Row(0),1,null,0);writer.Add(2,25,Row(-1),1,null,0);writer.Complete();
        }
        bytes=File.ReadAllBytes(Path.Combine(merged,"neuropixels_ap.dat"));
        Check(bytes.Length==2*768*2,"Merged signed upsample row count");
        for(int ch=0;ch<384;ch++){
            Check(BitConverter.ToInt16(bytes,ch*2)==512 && BitConverter.ToInt16(bytes,(384+ch)*2)==-511,"Merged probe order or double transform");
            Check(BitConverter.ToInt16(bytes,(768+ch)*2)==128 && BitConverter.ToInt16(bytes,(1152+ch)*2)==-129,"Merged signed interpolation");
        }
        bytes=File.ReadAllBytes(Path.Combine(merged,"neuropixels_lfp.dat"));
        Check(bytes.Length==768*2 && BitConverter.ToInt16(bytes,0)==-1 && BitConverter.ToInt16(bytes,384*2)==1,"Merged LFP double transform");
    }
    static void LiveMetadata(string folder){
        string output=Path.Combine(folder,"metadata");Directory.CreateDirectory(output);
        using(var session=new NpCaptureSession())session.WriteStatus(output,false);
        string text=File.ReadAllText(Path.Combine(output,"neuropixels_probes.txt"));
        Check(text.Contains("sample_transform_version=1") && text.Contains("sample_transform=512 - raw10") &&
            text.Contains("sample_range=-511..512") && text.Contains("sample_polarity=") && text.Contains("legacy_dat_encoding="),"Live sign/version/legacy metadata missing");
        using(var session=new NpCaptureSession()){session.SampleCorrection=(p,a,n,l,m)=>{};session.WriteStatus(output,false);}
        text=File.ReadAllText(Path.Combine(output,"neuropixels_probes.txt"));
        Check(text.Contains("signed calibrated nominal ADC counts") && text.Contains("see adc_calibration.json") && !text.Contains("sample_range=-511..512"),"Calibrated live metadata claims uncorrected counts");
    }
    static void Put(byte[] b,int at,uint n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,at,4);}
    static uint Crc(byte[] b,int count){uint c=~0U;for(int i=0;i<count;i++){c^=b[i];for(int bit=0;bit<8;bit++)c=(c>>1)^((0-(c&1))&0xedb88320U);}return ~c;}
    static void SdExport(string folder){
        const int chunks=8,hs=8192,end=hs+chunks*128;
        byte[] wire=Fixture(3,631);var h=new byte[512];var f=new byte[512];
        foreach(var b in new[]{h,f}){
            Put(b,8,3);Put(b,12,77);Put(b,16,1);Put(b,20,1);Put(b,24,15000);Put(b,28,1250);
            Put(b,32,15360);Put(b,36,128);Put(b,40,0);Put(b,44,end);Put(b,48,hs);Put(b,80,1);
        }
        Buffer.BlockCopy(System.Text.Encoding.ASCII.GetBytes("NPSDLOG1"),0,h,0,8);
        Buffer.BlockCopy(System.Text.Encoding.ASCII.GetBytes("NPSDEND1"),0,f,0,8);
        Put(f,60,3);Put(f,68,chunks);Put(h,508,Crc(h,508));Put(f,508,Crc(f,508));
        string output=Path.Combine(folder,"sd-export"),calibratedOutput=Path.Combine(folder,"sd-calibrated");
        var gainCsv=new System.Text.StringBuilder("1234\n");var adcCsv=new System.Text.StringBuilder("1234\n");
        for(int ch=0;ch<384;ch++){gainCsv.Append(ch);for(int column=1;column<=16;column++)gainCsv.Append(column<=8?",1.25":",0.75");gainCsv.Append('\n');}
        for(int adc=0;adc<32;adc++)adcCsv.Append(adc).Append(",0,0,0,0,0,0,").Append(adc%4).Append(",512\n");
        string gainPath=Path.Combine(folder,"np1_gain.csv"),adcPath=Path.Combine(folder,"np1_adc.csv");
        File.WriteAllText(gainPath,gainCsv.ToString());File.WriteAllText(adcPath,adcCsv.ToString());
        var calibration=new NpCalibrationSet();var profile=new NpAdcCalibration(gainPath,adcPath,"1234",1000,50);calibration.Set(1,profile);
        using(var card=new MemoryStream()){
            card.SetLength(end*512L);card.Position=3*512;card.Write(h,0,512);card.Write(f,0,512);
            card.Position=hs*512L;
            for(int chunk=0;chunk<chunks;chunk++){
                var raw=new byte[65024];var misc=new byte[512];
                for(int i=0;i<15360;i++)for(int lane=0;lane<4;lane++)raw[lane*15360+i]=wire[chunk*61440+i*4+lane];
                for(int row=0;row<16;row++){
                    Buffer.BlockCopy(BitConverter.GetBytes((ushort)(chunk*16+row)),0,misc,row*32+22,2);Put(misc,row*32+28,(uint)chunk);
                }
                card.Write(raw,0,raw.Length);card.Write(misc,0,misc.Length);
            }
            NpCe64Export.Export(card,0,end,output,null,null);
            NpCe64Export.ExportCalibrated(card,0,end,calibratedOutput,null,null,calibration);
        }
        byte[] ap=File.ReadAllBytes(Path.Combine(output,"np1_ap.dat")),lfp=File.ReadAllBytes(Path.Combine(output,"np1_lfp.dat"));
        Check(ap.Length==630*768 && lfp.Length==52*768,"SD export row counts changed");
        for(int row=0;row<630;row++)for(int adc=0;adc<32;adc++)for(int slot=0;slot<12;slot++)
            Check(BitConverter.ToInt16(ap,row*768+Channel(adc,slot)*2)==512-Raw(row/12,adc,slot),"Downloaded AP DAT sign/offset/byte order");
        for(int row=0;row<52;row++)for(int adc=0;adc<32;adc++)for(int slot=0;slot<12;slot++)
            Check(BitConverter.ToInt16(lfp,row*768+Channel(adc,slot)*2)==512-Raw(row,adc,slot),"Downloaded LFP DAT sign/offset/byte order");
        byte[] rawLanes=File.ReadAllBytes(Path.Combine(output,"np1.lane8"));
        Check(rawLanes.Length==chunks*61440,"Downloaded raw lane byte count changed");
        for(int i=0;i<rawLanes.Length;i++)Check(rawLanes[i]==wire[i],"SD raw transport was transformed");
        string text=File.ReadAllText(Path.Combine(output,"capture_status.txt"));
        Check(text.Contains("complete=True") && text.Contains("sample_transform_version=1") && text.Contains("sample_transform=512 - raw10") &&
            text.Contains("sample_range=-511..512") && text.Contains("sample_polarity=") && text.Contains("legacy_dat_encoding="),"SD sign/version/legacy metadata missing");
        byte[] correctedAp=File.ReadAllBytes(Path.Combine(calibratedOutput,"np1_ap.dat")),correctedLfp=File.ReadAllBytes(Path.Combine(calibratedOutput,"np1_lfp.dat"));
        Check(correctedAp.Length==ap.Length && correctedLfp.Length==lfp.Length,"Calibrated NP1 SD row count changed");
        for(int i=0;i<ap.Length/2;i++)Check(BitConverter.ToInt16(correctedAp,i*2)==profile.Correct(BitConverter.ToInt16(ap,i*2),i%384),"NP1 SD AP bypassed factory correction");
        for(int i=0;i<lfp.Length/2;i++)Check(BitConverter.ToInt16(correctedLfp,i*2)==profile.Correct(BitConverter.ToInt16(lfp,i*2),i%384,true),"NP1 SD LFP bypassed factory correction");
        byte[] calibratedRaw=File.ReadAllBytes(Path.Combine(calibratedOutput,"np1.lane8"));Check(calibratedRaw.Length==rawLanes.Length,"Calibrated RAW extent changed");
        for(int i=0;i<rawLanes.Length;i++)Check(rawLanes[i]==calibratedRaw[i],"Calibration changed native NP1 SD RAW");
        text=File.ReadAllText(Path.Combine(calibratedOutput,"capture_status.txt"));
        Check(text.Contains("complete=True") && text.Contains("factory host correction") && File.Exists(Path.Combine(calibratedOutput,NpCalibrationSet.ManifestName)),"NP1 calibrated SD provenance missing");
    }
    [STAThread] static int Main(string[] args){
        string folder=Path.Combine(args[0],"polarity-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        foreach(int layout in new[]{1,2,3})AllCodes(layout,folder);
        Preview();UpsamplingAndMerge(folder);LiveMetadata(folder);SdExport(folder);
        Console.WriteLine("PASS: "+checks+" NP1 all-code/ADC/AP/LFP/live-SD/DAT/preview/signed-resampler/merge/metadata checks");
        Console.WriteLine("Nominal transform: 512 - raw10; 0=>+512, 511=>+1, 512=>0, 513=>-1, 1023=>-511; no calibration claim");
        return 0;
    }
}
