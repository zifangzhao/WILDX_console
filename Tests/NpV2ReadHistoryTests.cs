using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using USB512_interface;

internal static class NpV2ReadHistoryTests
{
    private static int checks;
    private static void Need(bool value,string why){checks++;if(!value)throw new Exception(why);}
    private static NpV2ReadHistory.Entry[] Rows(NpV2ReadHistory h,out long total,out long lost,out long late,out long bad)
    {return h.Snapshot(out total,out lost,out late,out bad);}
    private static byte[] Block(ulong sequence)
    {
        // Reuse the existing actual-verifier NP2USB03 live 3-probe fixture.
        return (byte[])typeof(NpV2UsbConsoleTests).GetMethod("MakeV3Block",BindingFlags.NonPublic|BindingFlags.Static)
            .Invoke(null,new object[]{sequence,sequence*40960UL,7U,7U,20000U,false,UInt32.MaxValue});
    }
    private static byte[] Join(params byte[][] blocks)
    {var all=new byte[blocks.Sum(b=>b.Length)];int at=0;foreach(var b in blocks){Buffer.BlockCopy(b,0,all,at,b.Length);at+=b.Length;}return all;}
    private static long Complete(NpV2ReadHistory h,long issue,int slot,long qpc,int requested,int received)
    {h.Issue(issue,slot,qpc-2,requested);h.QueueReturned(issue,slot,qpc-1,0);return h.Completion(issue,slot,qpc,requested,received);}
    private static byte[] Decode(byte[] wire,NpV2ReadHistory history)
    {
        var verifier=new NpV2WireVerifier();
        using(var output=new MemoryStream())using(var writer=new BinaryWriter(output))
        {
            var decoder=new NpV2SampleDecoder((mask,rate)=>{},(probe,row)=>{writer.Write(probe);foreach(short value in row)writer.Write(value);});
            long ticket=history==null?0:Complete(history,0,3,100,wire.Length,wire.Length);
            long before=verifier.Blocks;
            for(int at=0;at<wire.Length;at+=NpV2UsbDevice.BlockBytes){verifier.Feed(wire,at);decoder.Feed(wire,at,verifier);}
            if(history!=null)history.Verified(ticket,wire,wire.Length,before,verifier.Blocks,150);
            Need(verifier.Blocks==4&&verifier.SyncPositionsChecked>0,"Real verifier did not verify four live blocks");
            return output.ToArray();
        }
    }
    private static void Checks(string root,string output)
    {
        string prior=Environment.GetEnvironmentVariable("WILDX_V2_USB_HISTORY_LAB");
        try
        {
            foreach(string value in new[]{null,"","true","0","01"}){Environment.SetEnvironmentVariable("WILDX_V2_USB_HISTORY_LAB",value);Need(NpV2ReadHistory.ForLab()==null,"History enabled without exact lab opt-in");}
            Environment.SetEnvironmentVariable("WILDX_V2_USB_HISTORY_LAB","1");Need(NpV2ReadHistory.ForLab()!=null,"Explicit lab opt-in unavailable");
        }
        finally{Environment.SetEnvironmentVariable("WILDX_V2_USB_HISTORY_LAB",prior);}

        byte[] wire=Join(Block(0),Block(1),Block(2),Block(3));byte[] original=(byte[])wire.Clone();
        var history=new NpV2ReadHistory();byte[] baseline=Decode(wire,null),observed=Decode(wire,history);
        Need(baseline.Length>0&&baseline.SequenceEqual(observed),"History changed actual decoded DAT rows/bytes");
        Need(wire.SequenceEqual(original),"History mutated actual neural wire bytes");
        long total,lost,late,bad;var rows=Rows(history,out total,out lost,out late,out bad);
        Need(total==1&&lost==0&&late==0&&bad==0&&rows.Length==1,"Completion accounting changed");
        Need(rows[0].VerifiedBlocks==4&&rows[0].FirstSequence==0&&rows[0].LastSequence==3&&rows[0].AllVerified,"Verified header+24 IDs not preserved");
        Need(rows[0].IssueId==0&&rows[0].Slot==3&&rows[0].CompletionQpc==100&&rows[0].VerificationQpc==150,"Completion/verifier clocks confused");
        Need(!File.Exists(Path.Combine(output,NpV2ReadHistory.FileName)),"Active-memory history wrote a file");

        // Real verifier rejection accepts only the already verified prefix.
        var partial=new NpV2ReadHistory();var verifier=new NpV2WireVerifier();byte[] damaged=(byte[])wire.Clone();
        damaged[NpV2UsbDevice.BlockBytes+8]=99;
        long id=Complete(partial,9,7,200,wire.Length,damaged.Length),before=verifier.Blocks;bool rejected=false;
        try{for(int at=0;at<damaged.Length;at+=NpV2UsbDevice.BlockBytes)verifier.Feed(damaged,at);}
        catch(InvalidDataException){rejected=true;}
        finally{partial.Verified(id,damaged,damaged.Length,before,verifier.Blocks,250);}
        rows=Rows(partial,out total,out lost,out late,out bad);
        Need(rejected&&rows[0].VerifiedBlocks==1&&rows[0].FirstSequence==0&&rows[0].LastSequence==0&&!rows[0].AllVerified,"Failure was given fabricated later IDs");
        partial.Verified(id,damaged,damaged.Length,0,4,260);
        rows=Rows(partial,out total,out lost,out late,out bad);Need(bad==1&&rows[0].VerifiedBlocks==1,"Duplicate verifier update rewrote a completed mapping");

        var unknown=new NpV2ReadHistory();
        long pending=Complete(unknown,2,1,300,wire.Length,wire.Length);
        long split=Complete(unknown,1,2,301,wire.Length,wire.Length-1);
        unknown.Verified(split,wire,wire.Length-1,0,1,302);
        long failed=Complete(unknown,3,4,303,wire.Length,wire.Length);
        unknown.Verified(failed,wire,wire.Length,4,4,304);
        rows=Rows(unknown,out total,out lost,out late,out bad);
        Need(rows[0].CompletionId==pending&&rows[0].IssueId==2&&rows[1].IssueId==1,"Observed completion order replaced with submission order");
        Need(!rows[0].VerifiedBlocks.HasValue&&!rows[0].FirstSequence.HasValue&&rows[0].VerificationQpc==0,"Pending worker fabricated mapping");
        Need(!rows[1].VerifiedBlocks.HasValue&&bad==1,"Split completion given an unverified mapping");
        Need(rows[2].VerifiedBlocks==0&&!rows[2].FirstSequence.HasValue&&!rows[2].AllVerified,"Zero accepted blocks invented a wire ID");
        var snapshot=rows;Complete(unknown,4,0,305,0,0);Need(snapshot.Length==3&&snapshot[0].CompletionId==pending,"Snapshot was not immutable");
        // IDs come from the actual header, not from verifier totals or issue IDs.
        byte[] other=(byte[])wire.Clone();Buffer.BlockCopy(BitConverter.GetBytes(987654321UL),0,other,24,8);
        var direct=new NpV2ReadHistory();long d=Complete(direct,77,2,400,other.Length,other.Length);direct.Verified(d,other,other.Length,120,121,401);
        rows=Rows(direct,out total,out lost,out late,out bad);Need(rows[0].FirstSequence==987654321UL,"Header ID was synthesized from verifier count");

        var wrap=new NpV2ReadHistory();long first=Complete(wrap,0,0,500,wire.Length,wire.Length);
        for(int i=1;i<=NpV2ReadHistory.Capacity;i++)Complete(wrap,i,i%64,500+i,wire.Length,wire.Length);
        wrap.Verified(first,wire,wire.Length,0,4,5000);
        rows=Rows(wrap,out total,out lost,out late,out bad);
        Need(rows.Length==1024&&total==1025&&lost==1&&late==1&&bad==0,"Bounded wrap/late-update counters wrong");
        Need(rows[0].CompletionId==2&&rows[1023].CompletionId==1025&&!rows[1023].VerifiedBlocks.HasValue,"Late update mutated overwritten slot");

        var life=new NpV2ReadHistory();
        for(int i=0;i<4;i++){life.Issue(i,i,600+i*2,wire.Length);life.QueueReturned(i,i,601+i*2,0);}
        rows=Rows(life,out total,out lost,out late,out bad);
        for(int i=0;i<4;i++)Need(rows[i].IssueOutstanding==i&&rows[i].QueueOutstanding==i+1&&rows[i].CompletionQpc==0&&!rows[i].HarvestStatus.HasValue,"Issue/accepted outstanding depth or pending mapping wrong");
        long outOfOrder=life.Completion(2,2,620,wire.Length,wire.Length);
        life.Verified(outOfOrder,wire,wire.Length,0,4,650);
        life.Issue(4,2,625,wire.Length);life.QueueReturned(4,2,626,0);
        // Failed queue is an attempt, not accepted native ownership or requeue.
        life.Issue(5,5,630,wire.Length);life.QueueReturned(5,5,631,998);
        // Failed harvest retains unknown ownership until native retirement is proven.
        life.Harvest(1,1,640,995,null,false,wire.Length);
        life.Stopped(1,1,700,true);
        life.Stopped(0,0,700,false);
        rows=Rows(life,out total,out lost,out late,out bad);
        Need(rows[2].CompletionId==1&&rows[2].IssueId==2&&rows[2].HarvestOutstanding==3&&rows[2].VerificationQpc==650,"Out-of-order harvest/worker processing conflated");
        Need(rows[2].RequeueQpc==626&&rows[2].RequeueIssueId==4&&rows[4].IssueOutstanding==3&&rows[4].QueueOutstanding==4,"Successful per-slot requeue/depth wrong");
        Need(rows[5].QueueStatus==998&&rows[5].QueueOutstanding==4&&rows[5].CompletionQpc==0,"Rejected queue counted as accepted");
        Need(rows[1].HarvestStatus==995&&rows[1].HarvestRetired==false&&rows[1].HarvestOutstanding==4&&!rows[1].ReceivedBytes.HasValue&&rows[1].StopObservedRetired==false,"Failed read invented retirement/bytes");
        Need(rows[0].StopObservedRetired==true&&rows[0].CompletionQpc==0&&!rows[0].FirstSequence.HasValue,"Abort retirement invented a native harvest or NN mapping");
        life.Stopped(1,1,701,false);
        life.Issue(6,1,710,wire.Length);life.QueueReturned(6,1,711,0);
        rows=Rows(life,out total,out lost,out late,out bad);
        Need(rows[6].IssueOutstanding==2&&rows[6].QueueOutstanding==3,"Observed retirement did not release the host depth");
        long badBefore=bad;life.Completion(999,1,712,wire.Length,wire.Length);
        rows=Rows(life,out total,out lost,out late,out bad);Need(bad==badBefore+1&&rows[6].CompletionQpc==0,"Wrong issue identity changed current slot");

        // A long-lived pending IRP may outlive the bounded history window.
        var slow=new NpV2ReadHistory();slow.Issue(0,63,800,wire.Length);slow.QueueReturned(0,63,801,0);
        for(int i=1;i<=NpV2ReadHistory.Capacity;i++)Complete(slow,i,0,810+i*3,wire.Length,wire.Length);
        Need(slow.Completion(0,63,5000,wire.Length,wire.Length)==0,"Overwritten pending issue fabricated an old row");
        slow.Issue(2000,63,5001,wire.Length);slow.QueueReturned(2000,63,5002,0);
        rows=Rows(slow,out total,out lost,out late,out bad);
        Need(rows[rows.Length-1].IssueOutstanding==0&&rows[rows.Length-1].QueueOutstanding==1&&late>0&&bad==0,"History overwrite corrupted independent outstanding accounting");

        history.Save(output);string csv=File.ReadAllText(Path.Combine(output,NpV2ReadHistory.FileName));
        Need(csv.Contains("schema=2")&&csv.Contains("qpc_frequency="+Stopwatch.Frequency)&&csv.Contains("device_clock_mapping=unknown")&&csv.Contains("not HCD/bus pending depth"),"Clock/depth scope/schema missing");
        Need(csv.Contains("1,1,0,3,98,0,99,0,1,100,0,true,0,"+wire.Length+","+wire.Length+",150,4,0,3,true"),"Serialized actual mapping changed");
        Need(!csv.Contains(root)&&!csv.Contains("UTC20")&&!csv.Contains("001A")&&!csv.Contains("sample_value"),"History leaks unrelated identity/path/payload");
        bool collision=false;try{history.Save(output);}catch(IOException){collision=true;}Need(collision&&File.ReadAllText(Path.Combine(output,NpV2ReadHistory.FileName))==csv,"Save overwrote existing evidence");

        string source=File.ReadAllText(Path.Combine(root,"Neuropixels/USBConsoleV2/NpV2UsbConsole.cs"));
        Need(source.IndexOf("readHistory.Completion(done.Issue",StringComparison.Ordinal)<source.IndexOf("if (bytesDone != requested[done.Index])",StringComparison.Ordinal),"Completion missing before malformed extent/backlog failure");
        Need(source.Contains("finally { if(readHistory!=null)readHistory.Verified(current.HistoryTicket"),"Worker failure loses verified prefix");
        Need(source.Contains("QueueReadTracked(device,slot,bytes,readHistory)")&&source.Contains("QueueReadTracked(device,slot,NpV2UsbDevice.BlockBytes,readHistory)"),"Normal or Stop read queue bypasses lifecycle observer");
        Need(source.IndexOf("readHistory.Harvest(done.Issue",StringComparison.Ordinal)<source.IndexOf("NpV2CaptureStatus atFailure",StringComparison.Ordinal),"Failure EP0 diagnostic hides harvest time");
        Need(source.IndexOf("if (worker != null) worker.Join();",StringComparison.Ordinal)<source.IndexOf("readHistory.Save(folder)",StringComparison.Ordinal),"History saved before worker retirement");
        Need(source.Contains("catch(IOException ex) { warnings.Enqueue(\"Lab USB read history")&&source.Contains("catch(UnauthorizedAccessException ex) { warnings.Enqueue(\"Lab USB read history"),"Optional history save error gates neural completion");
        // Bounded runtime smoke, measured only; not a USB throughput claim.
        var bench=new NpV2ReadHistory();var clock=Stopwatch.StartNew();
        for(int i=0;i<100000;i++){bench.Issue(i,i%64,Stopwatch.GetTimestamp(),wire.Length);bench.QueueReturned(i,i%64,Stopwatch.GetTimestamp(),0);long ticket=bench.Completion(i,i%64,Stopwatch.GetTimestamp(),wire.Length,wire.Length);bench.Verified(ticket,wire,wire.Length,0,4,Stopwatch.GetTimestamp());}
        clock.Stop();Console.WriteLine("100000 issue+queue+harvest+verification updates including4QPCreads: "+clock.Elapsed.TotalMilliseconds.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+" ms (offline PC fixture, no USB throughput claim)");
        Console.WriteLine("Entry bytes="+System.Runtime.InteropServices.Marshal.SizeOf(typeof(NpV2ReadHistory.Entry))+"; fixed entry capacity="+NpV2ReadHistory.Capacity);
    }
    public static int Main(string[] args)
    {try{Checks(args[0],args[1]);Console.WriteLine("PASS "+checks+" bounded V2 host read-history checks; actual verifier/decoder, no device/app build");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
