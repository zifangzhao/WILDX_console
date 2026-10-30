using System;
using System.IO;
using System.Linq;
using USB512_interface;

static class NpFirstSyncSdGrid16AppendTests {
    const string Uid="001A0004434250062039314E";
    const ulong Token=0x1020304050607080UL;
    // Independently packed LE/zlib IEEE-CRC32 vectors, not BuildReply output.
    static readonly string[] Names={"caps","start","status","scheduled"};
    static readonly string[] Golden={
        "TlBTRAUAAABjAAAABAAaAAZQQkNOMTkgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA1WL14k5QU0EFAAAAYwAAAAQAGgAGUEJDTjE5IAAAAAAAAAAAAAAAAB0AAAAAAAAAAAAAAAAAAAAAAAAA/////zlhCgM=",
        "TlBTRAUAAQA5AAAABAAaAAZQQkNOMTkggHBgUEAwIBAHAAAAIE4AADkwAAAAAAAAAAAAAAAAAAAAAAAAYVuUE05QU0EFAAEAOQAAAAQAGgAGUEJDTjE5IIBwYFBAMCAQAAAAAB0AAAAZAAAAZAAAAAkAAAALAAAA/////xqHhOI=",
        "TlBTRAUAAgBkAAAABAAaAAZQQkNOMTkggHBgUEAwIBAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAaZjGWk5QU0EFAAIAZAAAAAQAGgAGUEJDTjE5IIBwYFBAMCAQAAAAAB0AAAAZAAAAZAAAAAkAAAALAAAA/////5M68I4=",
        "TlBTRAUAAwA5AAAABAAaAAZQQkNOMTkggHBgUEAwIBAHAAAAIE4AADkwAABAwgTp514GAAAAAAAAAAAAGNIgOU5QU0EFAAMAOQAAAAQAGgAGUEJDTjE5IIBwYFBAMCAQAAAAAB8AAAAZAAAAZAAAAAkAAAALAAAA/////3kY9Dg="};
    static int checks;
    static void Need(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static void Put(byte[] b,int at,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,at,4);}
    static void Seal(byte[] b){Put(b,60,NpFirstSyncWire.Crc(b,60));}
    static byte[] Alter(byte[] raw,int at,uint value){var b=(byte[])raw.Clone();Put(b,at,value);Seal(b);return b;}
    static void Bad(Action work,string why){bool failed=false;try{work();}catch(InvalidDataException){failed=true;}Need(failed,why);}
    static byte[] Legacy(bool scheduled){var b=new byte[64];Put(b,0,0x32444c4e);Put(b,4,2);Put(b,8,scheduled?4U:2U);Put(b,12,57);Put(b,16,7);Put(b,20,20000);Put(b,24,12345);if(scheduled)Buffer.BlockCopy(BitConverter.GetBytes(1793200000123456UL),0,b,28,8);Seal(b);return b;}
    static void Check(string output){
        Directory.CreateDirectory(output);
        for(int n=0;n<Golden.Length;n++){
            var pair=Convert.FromBase64String(Golden[n]);Need(pair.Length==128,"independent pair extent");var q=pair.Take(64).ToArray();var a=pair.Skip(64).ToArray();
            var actual=n==0?NpFirstSyncSd.QueryGrid16Append(NpFirstSyncSd.Caps,99,2,Uid):n==2?NpFirstSyncSd.QueryGrid16Append(NpFirstSyncSd.Status,100,2,Uid,Token):NpFirstSyncSd.WrapLegacyGrid16Append(Legacy(n==3),2,Uid,Token);
            Need(actual.SequenceEqual(q),"independent v5 "+Names[n]+" request golden");
            var parsed=NpFirstSyncSd.ParseReply(a,q,2);Need(parsed.Version==5&&parsed.Grid16Append==(n!=0)&&parsed.Grid16==(n!=0)&&parsed.FineCounter64==(n!=0),"v5 declaration/caps semantics");
            Need(NpFirstSyncSd.BuildReply(q,2,new NpFirstSyncSdReply{Supported=n==3?31U:29U,RecordFeatures=n==0?0U:25U,Session=n==0?0U:100U,Generation=n==0?0U:9U,SourceEpoch=n==0?0U:11U,ProfileCrc=UInt32.MaxValue}).SequenceEqual(a),"independent v5 reply golden");
            File.WriteAllBytes(Path.Combine(output,"v5-"+Names[n]+".bin"),pair);
            Bad(()=>NpFirstSyncSd.ParseRequest(q,1,Uid),"Family1 accepted v5");Bad(()=>NpFirstSyncSd.ParseReply(a,q,1),"Family1 accepted v5 reply");
            for(int i=0;i<64;i++){
                var bad=(byte[])q.Clone();bad[i]^=1;Bad(()=>NpFirstSyncSd.ParseRequest(bad,2,Uid),"request CRC mutation");
                bad=(byte[])a.Clone();bad[i]^=1;Bad(()=>NpFirstSyncSd.ParseReply(bad,q,2),"reply CRC mutation");
            }
            foreach(int at in new[]{4,6,8,12,24}){var b=(byte[])a.Clone();b[at]^=1;Seal(b);Bad(()=>NpFirstSyncSd.ParseReply(b,q,2),"wrong echoed version/op/id/UID/token");}
            Bad(()=>NpFirstSyncSd.ParseRequest(Alter(q,4,(BitConverter.ToUInt32(q,4)&0xffff0000U)|6U),2,Uid),"unknown version6 accepted");
            var oldq=Alter(q,4,(BitConverter.ToUInt32(q,4)&0xffff0000U)|4U);var olda=Alter(a,4,(BitConverter.ToUInt32(a,4)&0xffff0000U)|4U);var old=NpFirstSyncSd.ParseReply(olda,oldq,2);
            Need(old.Grid16==(n!=0)&&!old.Grid16Append,"v4 implicitly selects append");Bad(()=>NpFirstSyncSd.ParseReply(olda,q,2),"v4 response admitted v5");
            if(n==1||n==3){Need(NpFirstSyncSd.Unwrap(q,2,Uid).SequenceEqual(Legacy(n==3)),"legacy payload changed");Need(NpFirstSyncSd.FreshStart(q,2,Uid,Token-1)&&!NpFirstSyncSd.FreshStart(q,2,Uid,Token),"token replay admission");}
            if(n==0){foreach(int at in new[]{40,44,48,52,56})Bad(()=>NpFirstSyncSd.ParseReply(Alter(a,at,1),q,2),"CAPS leaked record context");}
            else foreach(uint feature in new uint[]{0,1,9,24,57})Bad(()=>NpFirstSyncSd.ParseReply(Alter(a,40,feature),q,2),"record25 not exact");
            foreach(uint support in new uint[]{0,1,5,13,16,25,28,32,61})Bad(()=>NpFirstSyncSd.ParseReply(Alter(a,36,support),q,2),"unsupported dependency mask");
        }
        Bad(()=>NpFirstSyncSd.QueryGrid16Append(NpFirstSyncSd.Caps,1,1,Uid),"Family1 builder emitted v5");
        var caps=NpFirstSyncSd.QueryGrid16Append(NpFirstSyncSd.Caps,1,2,Uid);
        for(uint error=1;error<=5;error++){
            var a=NpFirstSyncSd.BuildReply(caps,2,new NpFirstSyncSdReply{Result=error,Supported=0,ProfileCrc=UInt32.MaxValue});Need(NpFirstSyncSd.ParseReply(a,caps,2).Result==error,"unsupported/error result lost");Bad(()=>NpFirstSyncSd.ParseReply(Alter(a,44,1),caps,2),"error leaked record binding");
        }
        Console.WriteLine("PASS "+checks+" managed v5 append protocol checks; independent request/reply goldens, no transport.");
    }
    static int Main(string[] args){try{Check(args[0]);return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
