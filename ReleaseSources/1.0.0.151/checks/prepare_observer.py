from pathlib import Path

root = Path(__file__).resolve().parent
repo = root.parent.parent
source = (repo / '_artifacts/v1_v2_download_parity_20261025/DownloadParity141.cs').read_text(encoding='utf-8-sig')
start = source.index('    static void Transport(')
end = source.index('    static void Benchmark(', start)
source = source[:start] + '''    static void CpuCost(){
        byte[] data=new byte[1020*512+32];new Random(19).NextBytes(data);
        var method=T("CE32_console.NpDownloadStream").GetMethod("Crc",All);
        var crc=(Func<byte[],int,int,uint>)Delegate.CreateDelegate(typeof(Func<byte[],int,int,uint>),method);
        uint value=crc(data,32,data.Length-32);var trials=new List<object>();
        for(int n=0;n<5;n++){var watch=Stopwatch.StartNew();uint check=0;for(int i=0;i<1024;i++)check^=crc(data,32,data.Length-32);watch.Stop();Need(check==0,"CRC repeat mismatch");trials.Add(new{MB_s=(data.Length-32)*1024.0/watch.Elapsed.TotalSeconds/1e6,seconds=watch.Elapsed.TotalSeconds});}
        report["crc_trials"]=trials;report["crc_value"]=value;
        report["scope"]="Actual published 150 host CRC delegate; no device access";
    }
    static void Transport(string path,object record){
        const long bytes=256L*1024*1024;long first=N(record,"Start")*512,last=N(record,"End")*512;
        Need(first+bytes+3L*1020*512<=last,"Selected recording too short for bounded prefix plus legacy read-ahead allowance");
        var trials=new List<object>();report["transport_trials"]=trials;string reference=null;
        uint[] batches={32,256,256,32,32,256};
        for(int n=0;n<batches.Length;n++)using(var card=Open(path)){
            string before=(string)Call(card,"Diagnostics");
            var input=(Stream)New("CE32_console.NpDownloadPrefetchStream",card,first,first+bytes,(Func<bool>)(()=>false),1024*1024);
            string hashText;double seconds;long done=0;var timer=new Stopwatch();var cleanup=new Stopwatch();
            try{
                var maximum=T("CE32_console.NpDownloadStream").GetField("burstMaximum",All);Need((uint)maximum.GetValue(card)==32,"Expected released bounded batch policy");
                // Test-only runtime change, bounded by the exact advertised maximum.
                byte[] caps=(byte[])Call(card,"Control",(byte)0xc0,(byte)0x67,(ushort)0,(ushort)0,(ushort)32);Need(BitConverter.ToUInt32(caps,0)==0x3143424e&&BitConverter.ToUInt32(caps,8)>=batches[n],"Requested batch exceeds negotiated capability");maximum.SetValue(card,batches[n]);
                using(var hash=SHA256.Create()){
                    byte[] buffer=new byte[1024*1024];timer.Start();
                    while(done<bytes){int got=input.Read(buffer,0,(int)Math.Min(buffer.Length,bytes-done));Need(got>0,"Truncated transport prefix");hash.TransformBlock(buffer,0,got,buffer,0);done+=got;}
                    hash.TransformFinalBlock(new byte[0],0,0);timer.Stop();seconds=timer.Elapsed.TotalSeconds;hashText=BitConverter.ToString(hash.Hash).Replace("-","");
                }
            }finally{timer.Stop();cleanup.Start();input.Dispose();cleanup.Stop();}
            string after=(string)Call(card,"Diagnostics");Need(Counter(after,"errors")==Counter(before,"errors"),"Firmware errors increased");
            if(reference==null)reference=hashText;Need(reference==hashText,"Transport prefix SHA256 changed between trials");
            var trial=new{index=n,batch=batches[n],consumed_physical_sd_bytes=done,seconds,MB_per_second=done/seconds/1e6,source_read_seconds=N(input,"SourceReadTicks")/(double)Stopwatch.Frequency,source_bytes=N(input,"SourceBytes"),sha256=hashText,cleanup_ms=cleanup.Elapsed.TotalMilliseconds,firmware_before=before,firmware_after=after};
            trials.Add(trial);Console.WriteLine("Transport batch"+batches[n]+": "+trial.MB_per_second.ToString("F3")+" MB/s; CRC and SHA validated");
            File.WriteAllText(Path.Combine(root,"progress.json"),new JavaScriptSerializer{MaxJsonLength=Int32.MaxValue}.Serialize(report));
        }
    }
''' + source[end:]
source = source.replace('args[2]!="inventory"&&args[2]!="transport"&&args[2]!="parity"', 'args[2]!="cpu"&&args[2]!="inventory"&&args[2]!="transport"')
source = source.replace('args[2]!="transport"))return 2;', 'args[2]!="transport"&&args[2]!="export"&&args[2]!="cancel"))return 2;')
source = source.replace('"1.0.0.141"&&Hash(exe)=="37A35A19B29D9F348D4D59BAEDAFB8E92270B89A4FB115F30F0E5E8A5E0E5268"', '"1.0.0.150"&&Hash(exe)=="BE4C320DEECC1E28C4DACACAD6C2F2F65220B375B60A4E1080C43C1B3F118CF3"')
source = source.replace('"Published141 required"', '"Exact published150 required"')
source = source.replace('Need(app.GetName().Version.ToString()=="1.0.0.150"&&Hash(exe)=="BE4C320DEECC1E28C4DACACAD6C2F2F65220B375B60A4E1080C43C1B3F118CF3","Exact published150 required");', 'Need((app.GetName().Version.ToString()=="1.0.0.150"&&Hash(exe)=="BE4C320DEECC1E28C4DACACAD6C2F2F65220B375B60A4E1080C43C1B3F118CF3")||(app.GetName().Version.ToString()=="1.0.0.151"&&Hash(exe)=="023D19C256109965E97C0E0787FD53E271D169C8F6C033AF6A97157EA0DC5EFE"),"Exact checked150 or151 required");')
source = source.replace('Need((uint)maximum.GetValue(card)==32,"Expected released bounded batch policy");', 'Need((uint)maximum.GetValue(card)==(app.GetName().Version.Revision==151?256U:32U),"Expected compiled bounded batch policy");')
source = source.replace('session=args.Length>4?UInt32.Parse(args[4]):0;Run(args[2],args.Length>3?args[3]:null).GetAwaiter().GetResult();', 'session=args.Length>4?UInt32.Parse(args[4]):0;if(args[2]=="cpu")CpuCost();else Run(args[2],args[3]).GetAwaiter().GetResult();')
source = source.replace('uid=="001A0004434250062039314E"||uid=="001D00065942500B20313354"','uid=="001A0004434250062039314E"||uid=="001D00065942500B20313354"||uid=="001E002A5942500B20313354"')
source = source.replace('V1(paths[0])==(uid=="001D00065942500B20313354")','V1(paths[0])==(uid!="001A0004434250062039314E")')
a=source.index('            object chosen=records.Single(')
b=source.index('            report["selected_record"]',a)
source=source[:a]+'''            var eligible=records.Where(p=>(bool)V(p,"Downloadable")&&(bool)V(p,"Complete")&&N(p,"Chunks")>0&&(N(p,"End")-N(p,"Start"))*512>=256L*1024*1024+3L*1020*512).ToArray();
            Need(eligible.Length>0,"No sufficiently long complete existing recording; no recording or card change attempted");
            object chosen=session==0?eligible.OrderBy(p=>N(p,"End")-N(p,"Start")).First():eligible.Single(p=>N(p,"Session")==session);
            if(uid=="001D00065942500B20313354"&&session==1792385836)Need(N(chosen,"Start")==62128128&&N(chosen,"End")==62799872&&N(chosen,"Chunks")==5120&&N(chosen,"ProbeMask")==3,"Preserved V1 fixture tuple changed");
''' +source[b:]
source=source.replace('report["selected_record"]=Fields(chosen);Transport(path,chosen);','report["selected_record"]=Fields(chosen);if(action=="transport")Transport(path,chosen);if(action=="cancel")CancelCheck(path,chosen);')
source=source.replace('if(action=="parity")Benchmark(path,chosen);','if(action=="export")Benchmark(path,chosen);')
source=source.replace('bool buffered=n==1||n==2||n==5;', 'uint batch=(n==1||n==2||n==5)?256U:32U;bool buffered=true;')
source=source.replace('try{Call(T("CE32_console.NpCe64Export"),"ExportCalibrated",prefetch??card,', 'try{T("CE32_console.NpDownloadStream").GetField("burstMaximum",All).SetValue(card,batch);Call(T("CE32_console.NpCe64Export"),"ExportCalibrated",prefetch??card,')
source=source.replace('var trial=new{index=n,prefetch=buffered,logical_source_bytes=', 'Need(Counter(after,"errors")==Counter(before,"errors"),"Firmware errors increased during export");var trial=new{index=n,batch,prefetch=buffered,logical_source_bytes=')
source=source.replace('Console.WriteLine("Trial "+n+" "+(buffered?"prefetch":"serial")', 'Console.WriteLine("Trial "+n+" batch"+batch')
source=source.replace('    static async Task Run(','''    static void CancelCheck(string path,object record){
        long first=N(record,"Start")*512,last=first+256L*1024*1024;byte[] prefix=new byte[1024*1024];string expected=null;var trials=new List<object>();
        foreach(uint batch in new uint[]{32,256}){
            using(var card=Open(path)){
                string before=(string)Call(card,"Diagnostics");var input=(Stream)New("CE32_console.NpDownloadPrefetchStream",card,first,last,(Func<bool>)(()=>false),1024*1024);
                var cleanup=new Stopwatch();uint pending;
                try{T("CE32_console.NpDownloadStream").GetField("burstMaximum",All).SetValue(card,batch);Need(input.Read(prefix,0,prefix.Length)==prefix.Length,"Short cancellation prefix");using(var sha=SHA256.Create()){string actual=BitConverter.ToString(sha.ComputeHash(prefix)).Replace("-","");if(expected==null)expected=actual;Need(actual==expected,"Prefix differs across batch cancellation checks");}}
                finally{cleanup.Start();input.Dispose();cleanup.Stop();}
                byte[] caps=(byte[])Call(card,"Control",(byte)0xc0,(byte)0x67,(ushort)0,(ushort)0,(ushort)32);pending=BitConverter.ToUInt32(caps,16);Need(pending==0,"Burst remained active after cleanup");
                string after=(string)Call(card,"Diagnostics");Need(Counter(after,"errors")==Counter(before,"errors"),"Firmware errors increased during retirement");trials.Add(new{batch,retirement_ms=cleanup.Elapsed.TotalMilliseconds,pending,sha256=expected});
            }
            using(var reopened=Open(path)){reopened.Position=first;Need(reopened.Read(prefix,0,prefix.Length)==prefix.Length,"Short reopen prefix");using(var sha=SHA256.Create())Need(BitConverter.ToString(sha.ComputeHash(prefix)).Replace("-","")==expected,"Reopened prefix changed after retirement");}
        }
        report["cancel_trials"]=trials;report["reopened_prefix_hashes_match"]=true;
    }
    static async Task Run(''')
(root / 'DownloadHeadroom.cs').write_text(source,encoding='utf-8')
print('Prepared observer; CPU action never enumerates or opens devices.')
