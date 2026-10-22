using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

static class NpV2FormatFirmwareTests
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static int checks;
    static void Need(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static string Hash(byte[] bytes){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(bytes)).Replace("-","");}
    static object Get(object x,string name){return x.GetType().GetField(name,All).GetValue(x);}
    static object Load(Type type,string path,string hash){try{return type.GetMethod("LoadBundledV2",All).Invoke(null,new object[]{path,hash});}catch(TargetInvocationException ex){throw ex.InnerException;}}
    static int Main(string[] args){try{
        var app=Assembly.LoadFrom(Path.GetFullPath(args[0]));var type=app.GetType("USB512_interface.NpFirmwareImage",true);
        var old=Load(type,args[1],"0BFECE90D3D0B098C645DB5370052C806EC6FB21FE0F1947F1718DFCE61EEFB1");
        var current=Load(type,args[2],"E0683376C7DAB7F36ACBB8D5E67EAD31840C9258402A9FEA7A0D57451B88193A");
        Need((bool)Get(current,"Full")&&(uint)Get(current,"family")==2,"New fused image not admitted as full V2");
        var before=(byte[])Get(old,"flash");var after=(byte[])Get(current,"flash");var present=(BitArray)Get(current,"present");
        Need(before.Take(0x17e000).SequenceEqual(after.Take(0x17e000)),"Non-USB flash bytes changed");
        Need(present.Cast<bool>().Skip(0x1c000).Take(8192).All(p=>!p),"Retained settings included in image");
        Need((int)Get(old,"count")== (int)Get(current,"count"),"Fused coverage changed");
        var package=File.ReadAllBytes(args[3]);Need(package.Length==176736,"USB package size changed");
        Need(Hash(package)=="6CF99E583545F43308B62042EE3F69FCFC7AABCDF46C3B0AEF0F6EFB3D5AFEB2","USB package hash mismatch");
        Need(Hash(package.Skip(128).ToArray())=="F69B5458A7CAE90E3C28DF7FFFD17B60DD87438721D5E8F610A530B7E2A6C74C","USB body changed");
        Need(after.Skip(0x17e000).Take(128).SequenceEqual(package.Take(128)),"USB seal differs between package and fused image");
        Need(after.Skip(0x180000).Take(176608).SequenceEqual(package.Skip(128)),"USB code differs between package and fused image");
        Func<int,uint> word=p=>BitConverter.ToUInt32(package,p);
        Need(word(16)==0x55534231&&word(20)==4&&word(24)==0x08180000&&word(28)==176608&&word(32)==0xb6099a0e&&word(76)==3&&word(80)==4,"USB descriptor mismatch");
        Console.WriteLine("PASS "+checks+" actual-app V2 format firmware seal, coverage, APP/recovery/config preservation checks; no hardware.");return 0;
    }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
