using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

// File-only, actual delivered assemblies. No device enumeration or control call.
internal static class Np2PolarityTests
{
    const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static int checks;
    static readonly byte[] Sync={0xD3,0x6A,0x59,0xC5,0x3E,0xA7,0x1D,0xE4,0xB2,0x4F,0x78,0x96};
    static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static object Call(object target,string name,params object[] args){
        try{return target.GetType().GetMethod(name,Flags).Invoke(target,args);}
        catch(TargetInvocationException ex){throw ex.InnerException??ex;}
    }
    static object New(Type t,params object[] args){return t.GetConstructor(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,Array.ConvertAll(args,x=>x.GetType()),null).Invoke(args);}
    static void Put(byte[] b,int at,uint v){Buffer.BlockCopy(BitConverter.GetBytes(v),0,b,at,4);}
    static void Q(byte[] b,int at,ulong v){Buffer.BlockCopy(BitConverter.GetBytes(v),0,b,at,8);}
    static uint Crc(byte[] b,int length){uint c=~0U;for(int i=0;i<length;i++){c^=b[i];for(int n=0;n<8;n++)c=(c>>1)^((c&1)==0?0:0xedb88320U);}return c^~0U;}
    static int Raw(int row,int ch,int probe){return (row+ch*137+probe*71)&4095;}
    static byte Lane(int tick,int probe){
        int pos=(tick+137)%1600,frame=pos/100,bit=pos%100;
        if(bit==0)return (byte)(frame==0?7:0);
        if(bit==1)return 0;
        int value=0;
        for(int line=0;line<3;line++){
            int v;
            if(bit<4){int at=frame*6+line*2+bit-2;v=(Sync[at/8]>>(at%8))&1;}
            else {int slot=(bit-4)/12,ch=(slot/2)*32+(slot&1)+frame*2+line*128;v=(Raw((tick+137)/1600,ch,probe)>>(11-(bit-4)%12))&1;}
            value|=v<<line;
        }return (byte)value;
    }
    static byte[] Block(int seq,uint mask,bool packed,uint hz){
        int probes=mask==7?3:mask==3?2:1,codec=packed||probes==1?4:probes==2?5:6;
        int bits=codec==4?probes*3:codec==5?8:9,ticks=codec==4?46080*8/bits:codec==5?46080:40960;
        byte[] b=new byte[46592];Encoding.ASCII.GetBytes("NP2USB03").CopyTo(b,0);
        Put(b,8,3);Put(b,12,512);Put(b,16,46592);Put(b,20,46080);Q(b,24,(ulong)seq);Q(b,32,(ulong)(seq*ticks));
        Put(b,40,(uint)ticks);Put(b,44,hz*1600);Put(b,48,mask==7?511U:mask==3?63U:7U);Put(b,52,57);Put(b,60,hz);Sync.CopyTo(b,64);
        Put(b,76,mask);Put(b,80,mask);Put(b,84,1);Put(b,156,(uint)bits);Put(b,160,(uint)codec);
        for(int p=0;p<probes;p++){b[96+p*8]=(byte)('1'+p);b[120+p*8]=(byte)('A'+p);Put(b,144+p*4,(uint)(10+p));}
        for(int i=0;i<ticks;i++)for(int p=0;p<probes;p++){
            byte lane=Lane(seq*ticks+i,p);
            if(codec==4){int at=i*bits+p*3;for(int n=0;n<3;n++)if((lane&(1<<n))!=0)b[512+(at+n)/8]|=(byte)(1<<((at+n)%8));}
            else if(codec==5)b[512+i]|=(byte)(lane<<(p*3));
            else if(p<2)b[512+i]|=(byte)(lane<<(p*3));
            else {b[512+i]|=(byte)((lane&3)<<6);if((lane&4)!=0)b[512+ticks+i/8]|=(byte)(1<<(i%8));}
        }return b;
    }
    static byte[][] Decode(Assembly a,uint mask,bool packed,uint hz,string folder){
        var decoderType=a.GetType("USB512_interface.NpV2SampleDecoder",true);
        var verifier=New(a.GetType("USB512_interface.NpV2WireVerifier",true));
        int[] rows=new int[4];var bytes=new MemoryStream[4];for(int p=1;p<=3;p++)bytes[p]=new MemoryStream();
        Action<int,short[]> receive=(p,row)=>{
            for(int ch=0;ch<384;ch++){short expected=(short)(2048-Raw(rows[p]+1,ch,p-1));Check(row[ch]==expected,"Actual decoder polarity/channel order "+a.GetName().Name);bytes[p].WriteByte((byte)row[ch]);bytes[p].WriteByte((byte)(row[ch]>>8));}rows[p]++;
        };
        var decoder=New(decoderType,new Action<uint,uint>((m,h)=>Check(m==mask&&h==hz,"Profile changed")),receive);
        object recorder=null;int preview=0;
        if(folder!=null){Directory.CreateDirectory(folder);recorder=New(a.GetType("USB512_interface.NpV2DatRecorder",true),folder,new Action<int,short[]>((p,row)=>{Check(row[0]==2048-Raw(preview/(mask==3?2:1)+1,0,p-1)||mask!=1,"Preview row polarity");preview++;}));}
        int ticks=0;
        for(int seq=0;seq<3;seq++){
            byte[] block=Block(seq,mask,packed,hz),original=(byte[])block.Clone();ticks=(int)BitConverter.ToUInt32(block,40);
            Call(verifier,"Feed",block,0);Call(decoder,"Feed",block,0,verifier);
            if(recorder!=null)Call(recorder,"Enqueue",block,0,verifier);
            Check(Convert.ToBase64String(block)==Convert.ToBase64String(original),"Raw block changed");
        }Call(decoder,"Complete");
        for(int p=1;p<=3;p++)Check(rows[p]==(((mask>>(p-1))&1)==0?0:(3*ticks-1463)/1600),"Rows lost/padded");
        if(recorder!=null){Call(recorder,"Complete");((IDisposable)recorder).Dispose();for(int p=1;p<=3;p++)if(rows[p]>0){string path=Path.Combine(folder,mask==1?"":"probe"+p,"neuropixels_ap.dat");Check(Convert.ToBase64String(File.ReadAllBytes(path))==Convert.ToBase64String(bytes[p].ToArray()),"Actual recorder signed DAT differs");}Check(preview==rows[1]+rows[2]+rows[3],"Preview missed DAT rows");using(var raw=File.OpenRead(Path.Combine(folder,"neuropixels_np2usb.raw"))){for(int seq=0;seq<3;seq++){byte[] expected=Block(seq,mask,packed,hz),actual=new byte[expected.Length];Check(raw.Read(actual,0,actual.Length)==actual.Length&&Convert.ToBase64String(expected)==Convert.ToBase64String(actual),"Saved RAW changed");}}}
        byte[][] output=new byte[4][];for(int p=1;p<=3;p++){output[p]=bytes[p].ToArray();bytes[p].Dispose();}return output;
    }
    static void Sd(Assembly a,string root){
        byte[] d=new byte[512],f;Encoding.ASCII.GetBytes("N2SDLOG1").CopyTo(d,0);
        uint[] fields={11,7,20000,32000000,3,63,4,6,46592,61440,91,4,8192,16384,32764,32768,65536,0};
        for(int n=0;n<fields.Length;n++)Put(d,8+n*4,fields[n]);Put(d,96,15);Put(d,100,1);Put(d,508,Crc(d,508));
        f=(byte[])d.Clone();Encoding.ASCII.GetBytes("N2SDEND1").CopyTo(f,0);Put(f,100,3);Put(f,108,4);Put(f,112,1);Put(f,116,16384+364);Q(f,120,4UL*61440);Put(f,508,Crc(f,508));
        using(var image=new MemoryStream(new byte[(16384+364)*512])){
            image.Position=(8192+3L)*512;image.Write(d,0,512);image.Write(f,0,512);
            image.Position=16384L*512;for(int seq=0;seq<4;seq++){var block=Block(seq,3,true,20000);image.Write(block,0,block.Length);}
            byte[] original=image.ToArray();string folder=Path.Combine(root,"sd");
            a.GetType("USB512_interface.NpV2SdDatExport",true).GetMethod("Export").Invoke(null,new object[]{image,8192U,folder,0L,null,null});
            for(int p=1;p<=2;p++){byte[] dat=File.ReadAllBytes(Path.Combine(folder,"neuropixels_probe"+p+"_ap.dat"));int rows=(4*61440-1463)/1600;Check(dat.Length==rows*768,"SD extent changed");for(int row=0;row<rows;row++)for(int ch=0;ch<384;ch++)Check(BitConverter.ToInt16(dat,(row*384+ch)*2)==2048-Raw(row+1,ch,p-1),"Actual SD DAT sign/offset/order");}
            string report=File.ReadAllText(Path.Combine(folder,"np2_sd_export_status.txt"));Check(report.Contains("sample_encoding=np2_signed_nominal_v1")&&report.Contains("sample_transform=2048 - raw12")&&report.Contains("sample_range=-2047..2048")&&report.Contains("Legacy DAT"),"SD metadata/legacy marker missing");
            Check(Convert.ToBase64String(original)==Convert.ToBase64String(image.ToArray()),"SD input image changed");
        }
    }
    [STAThread] static int Main(string[] args){
        try{
            var gui=Assembly.LoadFrom(Path.GetFullPath(args[0]));var sd=Assembly.LoadFrom(Path.GetFullPath(args[1]));string root=Path.GetFullPath(args[2]);Directory.CreateDirectory(root);
            foreach(var a in new[]{gui,sd}){Type t=a.GetType("USB512_interface.NpV2SampleDecoder",true);var nominal=t.GetMethod("NominalCount",Flags);Check(nominal!=null,"Old uncorrected decoder (negative control)");for(int raw=0;raw<4096;raw++)Check((short)nominal.Invoke(null,new object[]{raw})==2048-raw,"All-code signed count mismatch");
                foreach(uint rate in new uint[]{15000,20000,30000})foreach(uint mask in new uint[]{1,3,7})Decode(a,mask,mask==3,rate,mask==1&&rate==20000?Path.Combine(root,a.GetName().Name+"-usb"):null);
                Decode(a,3,false,20000,null);
            }
            Sd(sd,root);
            Type preview=gui.GetType("USB512_interface.NeuropixelsPreviewForm",true);
            foreach(int bits in new[]{10,12}){var form=(Form)New(preview,20000,1,bits==12,bits);Check((float)preview.GetField("apAdcMidscale",Flags).GetValue(form)==0f,"Preview still uses unsigned pedestal");form.Dispose();}
            var point=preview.GetMethod("TracePoint",Flags);foreach(short sample in new short[]{-2047,-1,0,1,2048}){var row=new short[64];row[0]=sample;var p=(PointF)point.Invoke(null,new object[]{row,0,0,0f,1f,100f,0f,.01f});Check(Math.Abs(p.Y-(100-sample*.01f))<.001f,"Trace direction or double inversion");}
            Type plot=gui.GetType("USB512_interface.NpBlePreviewForm+Plot",true);var control=(Control)New(plot);var rawSamples=new ushort[,]{{0,2048,4095},{2047,2049,1}};Call(control,"Append",rawSamples,0UL,1UL,3U);var values=(short[,])plot.GetField("values",Flags).GetValue(control);for(int row=0;row<2;row++)for(int lane=0;lane<3;lane++)Check(values[row,lane]==2048-rawSamples[row,lane],"BLE display correction missing/double applied");Check(rawSamples[0,2]==4095,"BLE RAW modified");control.Dispose();
            string source=File.ReadAllText(args[3]);foreach(string key in new[]{"sample_encoding=","sample_transform=","sample_range=-2047..2048","sample_polarity=","legacy_dat_encoding="})Check(source.Contains(key),"USB metadata key missing");
            Console.WriteLine("PASS "+checks+" actual GUI/SD polarity, all4096 codes, all384 channels/all3 ports, packed3/6, PSSI8/split9, 15/20/30k offline replay, USB DAT/RAW/preview, SD export/metadata and BLE plot checks; no hardware accessed.");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
