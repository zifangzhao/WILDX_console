using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

// Executes the maintained updater inside the actual built Release executable.
// No USB enumeration, control transfer, BEGIN/erase, or programming is invoked.
internal static class TestNpV1AppHex
{
    static Type manager;
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static byte[] Validate(string path){return (byte[])manager.GetMethod("ValidatePackage",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{path});}
    static void Reject(string path)
    {
        try{Validate(path);throw new Exception("Accepted unsafe/corrupt image: "+path);}
        catch(TargetInvocationException e){Check(e.InnerException is InvalidDataException,"Expected explicit image rejection: "+e.InnerException);}
    }
    static uint Crc(byte[] data,int count)
    {
        uint value=0xffffffff;
        for(int i=0;i<count;i++){value^=data[i];for(int b=0;b<8;b++)value=(value>>1)^((value&1)!=0?0xedb88320U:0);}
        return ~value;
    }
    static string Record(byte kind,ushort address,byte[] data)
    {
        var result=new StringBuilder(":");int sum=data.Length+(address>>8)+(address&255)+kind;
        result.Append(data.Length.ToString("X2")).Append(address.ToString("X4")).Append(kind.ToString("X2"));
        foreach(byte value in data){sum+=value;result.Append(value.ToString("X2"));}
        return result.Append(((byte)-sum).ToString("X2")).ToString();
    }
    static void Compatible(byte[] image,uint abi,bool expected)
    {
        try{manager.GetMethod("RequireCompatibleLoader",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{image,abi});Check(expected,"Unexpected loader admission "+abi);}
        catch(TargetInvocationException e){Check(!expected && e.InnerException is InvalidDataException,"Wrong loader rejection "+abi);}
    }
    static int Main(string[] args)
    {
        try{
            if(args.Length!=5)throw new ArgumentException("console.exe APP.hex fused.hex legacy-test-package output");
            manager=Assembly.LoadFrom(Path.GetFullPath(args[0])).GetType("USB512_interface.NpV1Management",true);
            byte[] expected=File.ReadAllBytes(args[3]);
            Check(Validate(args[1]).SequenceEqual(expected),"HEX does not reconstruct the exact validated APP package");
            Check(Validate(args[3]).SequenceEqual(expected),"Legacy input compatibility regressed");
            Reject(args[2]);
            Compatible(expected,258,true);Compatible(expected,257,false);
            Compatible(expected,0,false);Compatible(expected,259,false);
            string output=Path.GetFullPath(args[4]);Directory.CreateDirectory(output);
            string[] original=File.ReadAllLines(args[1]);
            Action<string,string[]> negative=(name,lines)=>{string file=Path.Combine(output,name+".hex");File.WriteAllLines(file,lines);byte[] before=File.ReadAllBytes(file);Reject(file);Check(before.SequenceEqual(File.ReadAllBytes(file)),"Validator modified input");};
            string[] usbRecords={Record(4,0,new byte[]{8,0x17}),Record(0,0xe000,new byte[]{1,2,3,4}),Record(1,0,new byte[0])};
            negative("usb_only",usbRecords);
            negative("app_plus_usb",original.Take(original.Length-1).Concat(usbRecords).ToArray());
            negative("missing_eof",original.Take(original.Length-1).ToArray());
            negative("after_eof",original.Concat(new[]{original[1]}).ToArray());
            negative("duplicate_address",original.Take(original.Length-1).Concat(new[]{original[1],original.Last()}).ToArray());
            string[] bad=(string[])original.Clone();bad[1]=bad[1].Substring(0,bad[1].Length-1)+(bad[1].Last()=='0'?"1":"0");negative("checksum",bad);
            bad=(string[])original.Clone();bad[1]=bad[1].Substring(0,bad[1].Length-1);negative("length",bad);
            bad=(string[])original.Clone();bad[1]=bad[1].Substring(0,9)+"Z"+bad[1].Substring(10);negative("non_hex",bad);
            negative("missing_payload",original.Where((line,index)=>index!=10).ToArray());
            negative("configuration_write",original.Take(original.Length-1).Concat(new[]{Record(4,0,new byte[]{8,1}),Record(0,0xc000,new byte[]{0}),original.Last()}).ToArray());
            negative("unknown_type",original.Take(original.Length-1).Concat(new[]{Record(6,0,new byte[0]),original.Last()}).ToArray());
            negative("wrong_start",original.Take(original.Length-1).Concat(new[]{Record(5,0,new byte[]{8,0,0,1}),original.Last()}).ToArray());
            uint pc=BitConverter.ToUInt32(expected,40);
            string goodStart=Path.Combine(output,"matching_start.hex");
            File.WriteAllLines(goodStart,original.Take(original.Length-1).Concat(new[]{Record(5,0,new byte[]{(byte)(pc>>24),(byte)(pc>>16),(byte)(pc>>8),(byte)pc}),original.Last()}));
            Check(Validate(goodStart).SequenceEqual(expected),"Matching linear start record rejected");
            foreach(uint abi in new uint[]{0,256,257,258,259}) {
                byte[] changed=(byte[])expected.Clone();Buffer.BlockCopy(BitConverter.GetBytes(abi),0,changed,80,4);
                Buffer.BlockCopy(BitConverter.GetBytes(Crc(changed,124)),0,changed,124,4);
                string file=Path.Combine(output,"abi"+abi+".xenp");File.WriteAllBytes(file,changed);
                if(abi==257 || abi==258) {
                    Check(Validate(file).SequenceEqual(changed),"Supported minimum ABI rejected");
                    Compatible(changed,257,abi==257);Compatible(changed,258,true);
                } else Reject(file);
            }
            Console.WriteLine("PASS actual Release APP-only HEX updater: "+checks+" checks; exact package, ABI 257/258, factory/USB/combined rejection, malformed/duplicate/missing records. No programming.");
            return 0;
        }catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
