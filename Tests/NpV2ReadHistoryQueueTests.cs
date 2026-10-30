using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using USB512_interface;

namespace USB512_interface
{
    // Only native boundary is replaced; ActualQueue contains the exact production wrapper.
    internal sealed class NpV2ReadSlot {internal int Index;internal long Issue;}
    internal sealed class NpV2UsbDevice
    {
        internal const int BlockBytes=46592;
        internal int Calls;internal Exception Failure;
        internal void QueueRead(NpV2ReadSlot slot,int bytes){Calls++;if(Failure!=null)throw Failure;}
    }
}
internal static class NpV2ReadHistoryQueueTests
{
    private static int checks;
    private static void Need(bool yes,string why){checks++;if(!yes)throw new Exception(why);}
    private static Exception Invoke(NpV2UsbDevice device,NpV2ReadSlot slot,NpV2ReadHistory history)
    {try{typeof(ActualQueue).GetMethod("QueueReadTracked",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{device,slot,46592,history});return null;}catch(TargetInvocationException ex){return ex.InnerException;}}
    public static int Main()
    {try{
        var device=new NpV2UsbDevice();var slot=new NpV2ReadSlot{Index=7,Issue=12};
        Need(Invoke(device,slot,null)==null&&device.Calls==1,"Default-off native call changed");
        var h=new NpV2ReadHistory();Need(Invoke(device,slot,h)==null&&device.Calls==2,"Accepted native queue replayed");
        long total,lost,late,bad;var rows=h.Snapshot(out total,out lost,out late,out bad);
        Need(rows.Length==1&&rows[0].IssueQpc>0&&rows[0].QueueReturnQpc>=rows[0].IssueQpc&&rows[0].QueueStatus==0&&rows[0].QueueOutstanding==1,"Actual wrapper omitted queue QPC/status/depth");
        foreach(Exception failure in new Exception[]{new Win32Exception(998,"native queue failure"),new IOException("IO failure"),new InvalidOperationException("unexpected failure")})
        {
            device.Failure=failure;var failed=new NpV2ReadHistory();int before=device.Calls;
            Need(Object.ReferenceEquals(Invoke(device,slot,failed),failure)&&device.Calls==before+1,"Wrapper masked/replayed original queue failure");
            rows=failed.Snapshot(out total,out lost,out late,out bad);
            Need(rows.Length==1&&rows[0].QueueStatus==(failure is Win32Exception?998:-1)&&rows[0].QueueOutstanding==0&&rows[0].CompletionQpc==0,"Queue failure acquired ownership or invented harvest");
            Need(Object.ReferenceEquals(Invoke(device,slot,null),failure),"Default-off exception changed");
        }
        Console.WriteLine("PASS "+checks+" exact production queue-wrapper failure/one-shot/default-off checks; native boundary stub only");return 0;
    }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
