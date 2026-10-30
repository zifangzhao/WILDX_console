using System;
using System.IO;
using System.Linq;
using USB512_interface;

// A real compiled-SDK boundary check. The main fixture creates/validates the
// source cards; this program links only the resulting SDK, not its sources.
internal static class NpV2SdGrid16AssemblyTests {
    sealed class Card:Stream {
        readonly FileStream source;
        internal Card(string path){source=File.OpenRead(path);}
        public override bool CanRead{get{return true;}}public override bool CanSeek{get{return true;}}public override bool CanWrite{get{return false;}}
        public override long Length{get{return 65536L*512;}}public override long Position{get{return source.Position;}set{source.Position=value;}}
        public override int Read(byte[] b,int a,int n){return source.Read(b,a,n);}public override long Seek(long n,SeekOrigin o){return source.Seek(n,o);}
        public override void Flush(){}public override void Write(byte[] b,int a,int n){throw new NotSupportedException();}public override void SetLength(long n){throw new NotSupportedException();}
        protected override void Dispose(bool disposing){if(disposing)source.Dispose();base.Dispose(disposing);}
    }
    static int Main(string[] args){
        try{
            string fixtures=args[0],output=args[1];if(Directory.Exists(output))throw new IOException("Fresh boundary output required.");Directory.CreateDirectory(output);int checks=0;
            foreach(string name in new[]{"g16_20000_3","g16_20000_7","g16_cl.img"}){
                bool cl=name.EndsWith(".img");string image=Path.Combine(fixtures,name+(cl?"new":"_new.img")),baseline=Path.Combine(fixtures,name+(cl?"base":"_old")),dest=Path.Combine(output,name);
                NpV2SdDatExport.Result r;using(var card=new Card(image))r=NpV2SdDatExport.Export(card,8192,dest);
                if(!r.RecordingComplete||!r.ScheduledImuGrid||r.CommittedBlocks!=8||!r.ImuAnalogGridNpLocked||!r.ImuGridSourceWindowVerified)throw new Exception("SDK binding/phase result: "+name);checks++;
                for(int p=0;p<r.Rows.Length;p++){string dat="neuropixels_probe"+(p+1)+"_ap.dat";if(!File.ReadAllBytes(Path.Combine(dest,dat)).SequenceEqual(File.ReadAllBytes(Path.Combine(baseline,dat))))throw new Exception("SDK NN byte parity: "+name);checks++;}
            }
            string bad=Path.Combine(fixtures,"g16_20000_3_new.imgcontext_absent");
            using(var card=new Card(bad)){var r=NpV2SdDatExport.Export(card,8192,Path.Combine(output,"optional"));if(!r.RecordingComplete||r.ImuAnalogGridNpLocked||r.OptionalImuError==null)throw new Exception("SDK optional context isolation");checks++;}
            bool rejected=false;using(var card=new Card(Path.Combine(fixtures,"g16_20000_3_new.imgnn")))try{NpV2SdDatExport.Export(card,8192,Path.Combine(output,"critical"));}catch(InvalidDataException){rejected=true;}
            if(!rejected)throw new Exception("SDK neural corruption admission");checks++;
            Console.WriteLine("PASS "+checks+" actual SDK assembly-boundary checks; "+typeof(NpV2SdDatExport).Assembly.Location);return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
