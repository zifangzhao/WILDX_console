using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using USB512_interface;

internal static class TestNpFirmwareFlash
{
    const string Uid="001E002A5942500B20313354";
    static int checks;
    static void Check(bool value,string text){checks++;if(!value)throw new Exception(text);}
    static void Reject(Action action,string text){try{action();throw new Exception("Accepted "+text);}catch(Exception error){if(!(error is IOException) && !(error is InvalidDataException) && !(error is ArgumentException))throw;checks++;}}
    sealed class Model
    {
        internal byte[] Flash;
        internal bool Dfu,Multiple,WrongUid,MissingUid,Unverified,FailWrite,CorruptReadback,ReadOnlyFailure;
        internal bool ListFailure,CorruptUpperReadback;
        internal int WrongUidAt,ChangeSerialAt,ListCalls;
        internal int Writes,Starts,Requests,UidReads,FlashReads;
        internal readonly HashSet<int> ErasedPages=new HashSet<int>();
        string operationHex;
        internal readonly List<string[]> Commands=new List<string[]>();
        internal NpFirmwareFlash.CliResult Run(string[] args)
        {
            Commands.Add(args);
            if(args[0]=="-l"){
                ListCalls++;if(ListFailure)return new NpFirmwareFlash.CliResult{ExitCode=2,Output="Error: enumeration failed\nNo STM32 device in DFU mode connected"};
                string serial=ChangeSerialAt>0 && ListCalls>=ChangeSerialAt?"123456789ABD":"123456789ABC";
                return Result("Total number of available devices in DFU mode: "+(Multiple?2:Dfu?1:0)+"\n"+
                    (Dfu?"Device Index : USB1\nSerial number : "+serial+"\n":"")+(Multiple?"Device Index : USB2\nSerial number : 123456789ABC\n":""));
            }
            Check(args.Take(5).SequenceEqual(new[]{"-c","port=USB1","sn=123456789ABC","VID=0x0483","PID=0xDF11"}),"CLI is not serial-pinned");
            Check(!args.Any(a=>a=="-e" || a=="all" || a=="-ob" || a=="-rdu" || a=="--skipErase"),"Forbidden programmer option");
            if(ReadOnlyFailure)return new NpFirmwareFlash.CliResult{ExitCode=2,Output="Error: read failed"};
            string output="STM32CubeProgrammer v99.0.0\n";
            for(int at=5;at<args.Length;){
                if(args[at]=="-u"){
                    string address=args[at+1],path=args[at+3];
                    if(address=="0x0BFA0700"){
                        UidReads++;if(!MissingUid){var bytes=new byte[12];for(int i=0;i<3;i++)Buffer.BlockCopy(BitConverter.GetBytes(Convert.ToUInt32(Uid.Substring(i*8,8),16)),0,bytes,i*4,4);if(WrongUid || WrongUidAt>0 && UidReads>=WrongUidAt)bytes[0]^=1;File.WriteAllBytes(path,bytes);}
                    }else if(address=="0x0BFA07A0")File.WriteAllBytes(path,BitConverter.GetBytes((ushort)(Flash.Length/1024)));
                    else{
                        FlashReads++;var bytes=(byte[])Flash.Clone();
                        if(FlashReads>1){if(CorruptReadback)bytes[0x1c100]^=1;if(CorruptUpperReadback)bytes[0x200100]^=1;if(operationHex!=null)LockedHex(operationHex);}
                        File.WriteAllBytes(path,bytes);
                    }
                    at+=4;
                }else if(args[at]=="-d"){
                    Writes++;if(FailWrite)return new NpFirmwareFlash.CliResult{ExitCode=1,Output="Error: programming failed"};
                    operationHex=args[at+1];LockedHex(operationHex);ApplyHex(operationHex);output+=Unverified?"Download complete, 100%\n":"DOWNLOAD VERIFIED SUCCESSFULLY\n";at+=2;
                }else if(args[at]=="-v")at++;
                else if(args[at]=="-s"){Starts++;Dfu=false;at+=2;}
                else throw new Exception("Unexpected CLI command: "+args[at]);
            }
            return Result(output);
        }
        void ApplyHex(string path)
        {
            // Model real erase-page behavior before DATA, rather than copying a
            // generated expected image back to the workflow under test.
            uint upper=0;
            foreach(string line in File.ReadLines(path)){
                int n=Convert.ToInt32(line.Substring(1,2),16),kind=Convert.ToInt32(line.Substring(7,2),16);
                if(kind==4){upper=Convert.ToUInt32(line.Substring(9,4),16)<<16;continue;}
                if(kind!=0)continue;uint address=upper+Convert.ToUInt32(line.Substring(3,4),16);
                int index=(int)(address-NpFirmwareImage.FlashBase),page=index/8192;
                Check(page!=14 && page<256,"Configuration or upper physical flash erase page was emitted");
                if(ErasedPages.Add(page))for(int i=page*8192;i<(page+1)*8192;i++)Flash[i]=255;
                for(int i=0;i<n;i++)Flash[index+i]=Convert.ToByte(line.Substring(9+i*2,2),16);
            }
        }
        static void LockedHex(string path)
        {
            Reject(()=>{using(var write=new FileStream(path,FileMode.Open,FileAccess.Write,FileShare.ReadWrite)){}},"operation HEX mutation during programming/readback");
        }
        static NpFirmwareFlash.CliResult Result(string text){return new NpFirmwareFlash.CliResult{ExitCode=0,Output=text};}
        internal void Request(){Requests++;Dfu=true;}
    }
    static string NewDirectory(string root){string result=Path.Combine(root,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(result);return result;}
    static Model Make(byte[] before){return new Model{Flash=(byte[])before.Clone()};}
    static string Run(Model model,NpFirmwareImage image,string root,bool already=false,bool reconnect=true)
    {return new NpFirmwareFlash(model.Run,(s,p)=>{},NewDirectory(root)).Execute(image,Uid,already,model.Request,()=>reconnect);}
    static void Classification(string appPath,string fullPath,byte[] full)
    {
        var app=NpFirmwareImage.Load(appPath);var fused=NpFirmwareImage.Load(fullPath);
        Check(!app.Full && fused.Full,"Real released HEX classification");
        Check(app.AppPackage.Skip(128).SequenceEqual(full.Skip(0x20000).Take(app.AppPackage.Length-128)),"Real APP payload differs");
        byte[] raw=File.ReadAllBytes(appPath);string text=Encoding.ASCII.GetString(raw);
        string eof=":00000001FF";
        foreach(string bad in new[]{text.Replace(eof,""),text+eof+"\n",text.Replace(":02000004",":02000005"),text.Replace(eof,":020000040900F1\n:0100000000FF\n"+eof),text.Replace(eof,":020000040801F1\n:01C00000003F\n"+eof)})Reject(()=>NpFirmwareImage.Parse(Encoding.ASCII.GetBytes(bad)),"malformed/outside/config HEX");
        string firstData=text.Split('\n').First(l=>l.Length>8 && l.Substring(7,2)=="00").Trim();
        Reject(()=>NpFirmwareImage.Parse(Encoding.ASCII.GetBytes(text.Replace(firstData,firstData+"\n"+firstData))),"overlapping HEX");
        var damaged=(byte[])raw.Clone();damaged[30]=(byte)'Z';Reject(()=>NpFirmwareImage.Parse(damaged),"nonhex checksum");
        string[] fullLines=File.ReadAllLines(fullPath);string partial=String.Join("\n",fullLines.Where((l,i)=>i!=2));Reject(()=>NpFirmwareImage.Parse(Encoding.ASCII.GetBytes(partial)),"partial fused coverage");
        var wrong=(byte[])full.Clone();wrong[0x100]^=1;Reject(()=>NpFirmwareImage.ValidateRecovery(wrong),"foreign recovery loader");
        var renamed=Path.Combine(Path.GetDirectoryName(appPath),"filename-says-full.bin");File.Copy(appPath,renamed,true);Check(!NpFirmwareImage.Load(renamed).Full,"Filename determined image scope");
    }
    static void Workflow(NpFirmwareImage app,NpFirmwareImage fused,byte[] before,string root)
    {
        foreach(var image in new[]{app,fused}){
            var model=Make(before);string result=Run(model,image,root);
            int writes=image.ExpectedFlash(before).SequenceEqual(before)?0:1;
            Check(result.Contains("Verified") && model.Writes==writes && model.Starts==1 && model.Requests==1,"Normal update sequence");
            Check(model.UidReads>=4,"UID not revalidated before write/verify/exit");
            Check(model.Flash.Skip(0x1c000).Take(8192).SequenceEqual(before.Skip(0x1c000).Take(8192)),"Configuration changed");
            Check(model.Flash.Skip(0x20000).Take(app.AppPackage.Length-128).SequenceEqual(app.AppPackage.Skip(128)),"Selected APP data changed");
            if(!image.Full){Check(model.Flash.Take(0x1c000).SequenceEqual(before.Take(0x1c000)),"APP altered loader");Check(model.Flash.Skip(0x17e000).SequenceEqual(before.Skip(0x17e000)),"APP altered USB services");}
        }
        var resume=Make(before);resume.Dfu=true;Run(resume,app,root,true);Check(resume.Requests==0 && resume.Writes==1,"Bound already-DFU resume");
        foreach(string failure in new[]{"multiple","preexisting","uid","missing","unverified","write","readback","read","reconnect"}){
            var model=Make(before);model.Multiple=failure=="multiple";model.Dfu=failure=="multiple" || failure=="preexisting";model.WrongUid=failure=="uid";model.MissingUid=failure=="missing";model.Unverified=failure=="unverified";model.FailWrite=failure=="write";model.CorruptReadback=failure=="readback";model.ReadOnlyFailure=failure=="read";
            Reject(()=>Run(model,app,root,false,failure!="reconnect"),failure);
            if(failure=="multiple" || failure=="preexisting")Check(model.Requests==0 && model.Writes==0,"Preflight failure sent DFU/write");
            if(failure=="uid" || failure=="missing" || failure=="read")Check(model.Writes==0,"Identity/read failure wrote flash");
            Check(model.Writes<=1,"Automatic write retry");
            if(failure!="reconnect")Check(model.Starts==0,"Exited DFU after failure");
        }
        Reject(()=>NpFirmwareFlash.ParseList("unrecognized future output"),"unrecognized list");
        Reject(()=>NpFirmwareFlash.ParseList("Total number of available devices in DFU mode: 2\nDevice Index : USB1\nSerial number : 123456789ABC"),"inconsistent count");
        Check(NpFirmwareFlash.ReadUid(new byte[]{0x2a,0,0x1e,0,0x0b,0x50,0x42,0x59,0x54,0x33,0x31,0x20})==Uid,"Full 96-bit UID byte order");
        var foreign=Make(before);foreign.Flash[0x100]^=1;Reject(()=>Run(foreign,app,root),"unknown APP fallback loader");Check(foreign.Writes==0,"Unrecognized resident loader erased");
        var timeout=Make(before);var flow=new NpFirmwareFlash(a=>{throw new IOException("fake CLI timeout");},(s,p)=>{},NewDirectory(root));Reject(()=>flow.Execute(app,Uid,false,timeout.Request,()=>true),"CLI timeout");Check(timeout.Requests==0,"Timeout sent DFU request");
        var listFailure=Make(before);listFailure.ListFailure=true;Reject(()=>Run(listFailure,app,root),"preflight CLI failure");Check(listFailure.Requests==0 && listFailure.Writes==0,"Preflight CLI error sent DFU/write");
        var identityChange=Make(before);identityChange.WrongUidAt=2;Reject(()=>Run(identityChange,app,root),"UID changed immediately before write");Check(identityChange.Writes==0,"Changed prewrite UID received write");
        var serialChange=Make(before);serialChange.ChangeSerialAt=4;Reject(()=>Run(serialChange,app,root),"Serial changed before write");Check(serialChange.Writes==0,"Changed prewrite serial received write");
        var reconnectError=Make(before);flow=new NpFirmwareFlash(reconnectError.Run,(s,p)=>{},NewDirectory(root));Reject(()=>flow.Execute(app,Uid,false,reconnectError.Request,()=>{throw new IOException("reconnect status not ready");}),"reconnect IOException");Check(reconnectError.Starts==1,"Reconnect failure falsely completed or retried");
    }
    static void ChangedPages(NpFirmwareImage app,NpFirmwareImage fused,byte[] before,string root)
    {
        var matching=fused.ExpectedFlash(before);
        string emptyPath=Path.Combine(NewDirectory(root),"no-pages.hex");
        using(var empty=fused.WritePageHex(emptyPath,matching,(byte[])matching.Clone()))Check(empty==null,"Identical fused image produced an operation stream");
        Check(!File.Exists(emptyPath),"Identical fused image created empty HEX");
        var unchanged=Make(matching);string directory=NewDirectory(root);
        string result=new NpFirmwareFlash(unchanged.Run,(s,p)=>{},directory).Execute(fused,Uid,false,unchanged.Request,()=>true);
        Check(unchanged.Writes==0 && unchanged.ErasedPages.Count==0 && !unchanged.Commands.Any(a=>a.Contains("-d")),"Identical fused image programmed flash");
        Check(unchanged.Starts==1 && unchanged.Requests==1 && unchanged.UidReads==4 && unchanged.FlashReads==2 && result.Contains("Verified"),"No-op skipped verification/UID/exit/reconnect");
        Check(unchanged.Flash.SequenceEqual(matching),"No-op changed physical flash");
        Check(!File.Exists(Path.Combine(directory,"validated-pages.hex")) && File.ReadAllText(Path.Combine(directory,"operation.log")).Contains("no flash pages programmed"),"No-op operation not explicit");

        foreach(int page in new[]{16,250}){
            var single=Make(matching);single.Flash[page*8192+103]^=1;Run(single,fused,root);
            Check(single.Writes==1 && single.ErasedPages.SetEquals(new[]{page}),"One-byte change did not emit exactly its page");
            Check(single.Flash.SequenceEqual(matching),"Complete changed page/config/upper flash not preserved");
            Check(!single.ErasedPages.Any(p=>p<14),"Unchanged recovery loader was emitted");
        }
        var generation=Make(matching);Run(generation,app,root);
        Check(generation.ErasedPages.SetEquals(new[]{15}),"Same APP payload emitted pages beyond generation manifest");
        Check(NpFirmwareImage.U(generation.Flash,0x1e000+20)==unchecked(NpFirmwareImage.U(matching,0x1e000+20)+1),"APP generation was not updated");
        Check(generation.Flash.SequenceEqual(app.ExpectedFlash(matching)),"APP generation omitted-byte/config/upper-flash preservation");

        var twoMiB=matching.Take(0x200000).ToArray();var shortBackup=Make(twoMiB);
        // The CLI model reports physical geometry from its actual backup length.
        Run(shortBackup,fused,root);Check(shortBackup.Writes==0 && shortBackup.Flash.SequenceEqual(twoMiB),"Identical 2 MiB no-op changed flash");
        Reject(()=>fused.WritePageHex(emptyPath,new byte[0x300000],new byte[0x300000]),"unsupported backup size");
        Reject(()=>fused.WritePageHex(emptyPath,matching,twoMiB),"mismatched expected physical size");
        foreach(string failure in new[]{"uid-before","uid-readback","uid-exit","serial","config-readback","upper-readback","reconnect"}){
            var model=Make(matching);model.WrongUidAt=failure=="uid-before"?2:failure=="uid-readback"?3:failure=="uid-exit"?4:0;
            model.ChangeSerialAt=failure=="serial"?4:0;model.CorruptReadback=failure=="config-readback";model.CorruptUpperReadback=failure=="upper-readback";
            Reject(()=>Run(model,fused,root,false,failure!="reconnect"),"no-op "+failure);
            Check(model.Writes==0 && model.ErasedPages.Count==0,"No-op failure programmed or retried");
            Check(model.Starts==(failure=="reconnect"?1:0),"No-op verification failure exited DFU");
        }
    }
    static void FrozenResident(string path,string root)
    {
        string frozen=Path.Combine(NewDirectory(root),"selected file with spaces.hex");File.Copy(path,frozen);var image=NpFirmwareImage.Load(frozen);File.WriteAllText(frozen,"malformed replacement");
        CE32_console.NpDownloadNative.Reset();
        var target=new NpV1Target{Uid=Uid,Path=CE32_console.NpDownloadNative.Path(false,Uid)};
        using(var manager=new NpV1Management(target))Check(manager.UpdateBytes(image.AppPackage,null)==2,"Frozen resident update failed");
        Check(CE32_console.NpDownloadNative.Written.SequenceEqual(image.AppPackage.Skip(128)),"Original selected file mutation changed resident payload");
    }
    static void Process(string cli)
    {
        string[] args={"path with spaces & $(nothing).hex","quote\"inside","trailing\\", "", "snowman \u2603"};
        var result=NpFirmwareFlash.RunProcess(cli,args);string[] lines=result.Output.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries);
        // Empty arguments produce an empty Base64 line; assert separately.
        Check(result.ExitCode==0 && lines.Length==4,"Quoted process argument count");
        Check(lines.Select(l=>Encoding.UTF8.GetString(Convert.FromBase64String(l))).SequenceEqual(args.Where(a=>a.Length>0)),"Windows argument quoting/injection");
        result=NpFirmwareFlash.RunProcess(cli,new[]{"output"});Check(result.Output.Contains("stdout 1999") && result.Output.Contains("stderr 1999") && result.Output.Contains("FINAL OUTPUT COMPLETE"),"Async output not fully drained");
        Check(NpFirmwareFlash.RunProcess(cli,new[]{"fail"}).ExitCode==7,"Nonzero process exit ignored");
        Reject(()=>NpFirmwareFlash.RunProcess(cli,new[]{"-l","hang"}),"Real fake process timeout");
        Reject(()=>NpFirmwareFlash.Quote("invalid\nargument"),"Control character argument");
    }
    static int Main(string[] args)
    {
        try{
            Directory.CreateDirectory(args[3]);var full=File.ReadAllBytes(args[2]);Check(full.Length==0x200000,"Real matched flash fixture length");
            var before=new byte[0x400000];Buffer.BlockCopy(full,0,before,0,full.Length);
            for(int i=0x1c000;i<0x1e000;i++)before[i]=(byte)(i*37);
            for(int i=0x200000;i<before.Length;i++)before[i]=(byte)(i*13+17);
            var app=NpFirmwareImage.Load(args[0]);var fused=NpFirmwareImage.Load(args[1]);
            Classification(args[0],args[1],full);Workflow(app,fused,before,args[3]);ChangedPages(app,fused,before,args[3]);FrozenResident(args[0],args[3]);Process(args[4]);
            Console.WriteLine("PASS "+checks+" HEX classification, protected erase pages, serial/full-UID binding, fake CLI failure/readback/reconnect and real process quoting/exit/timeout/output checks; no hardware calls.");return 0;
        }catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
