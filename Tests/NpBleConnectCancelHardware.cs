using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

// Headless local cancellation only. No firmware/status/configuration/reset writes.
// Exact peer/type are the witnessed004B radio from PROGRESS_PLAN section278.
internal static class NpBleConnectCancelHardware
{
    const BindingFlags All=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    [MTAThread] static int Main(string[] args)
    {try{Run(args).GetAwaiter().GetResult();return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
    static object Call(object target,string name)
    {try{return target.GetType().GetMethod(name,All).Invoke(target,null);}catch(TargetInvocationException e){throw e.InnerException??e;}}
    static object Property(object target,string name){return target.GetType().GetProperty(name,All).GetValue(target,null);}
    static async Task Run(string[] args)
    {
        if(args.Length!=2)throw new ArgumentException("<built WILDX_console.exe> <fresh events.log>");
        string appPath=Path.GetFullPath(args[0]),logPath=Path.GetFullPath(args[1]);
        if(File.Exists(logPath))throw new IOException("Fresh evidence log required.");
        string directory=Path.GetDirectoryName(appPath);
        AppDomain.CurrentDomain.AssemblyResolve+=(sender,e)=>{string path=Path.Combine(directory,new AssemblyName(e.Name).Name+".dll");return File.Exists(path)?Assembly.LoadFrom(path):null;};
        var type=Assembly.LoadFrom(appPath).GetType("USB512_interface.NpBleConnection",true);
        object connection=Activator.CreateInstance(type,true);Task pending=null;
        using(var log=new StreamWriter(logPath)){
            Action<string> record=text=>{Console.WriteLine(text);log.WriteLine(DateTime.UtcNow.ToString("O")+" "+text);log.Flush();};
            try{
                var connect=type.GetMethods(All).Single(m=>m.Name=="ConnectAsync" && m.GetParameters().Length==2);
                var addressType=Nullable.GetUnderlyingType(connect.GetParameters()[1].ParameterType);
                record("peer=F5D68A6BCC7C; type=Random; prior witnessed type; local cancellation only");
                pending=(Task)connect.Invoke(connection,new[]{(object)0xF5D68A6BCC7CUL,Enum.Parse(addressType,"Random")});
                var start=Stopwatch.StartNew();string trace;
                do{
                    trace=(string)Property(connection,"LastConnectDiagnostic");
                    if(trace.Contains("FFF0 service discovery") && !trace.Contains("service discovery result="))break;
                    if(pending.IsCompleted || start.ElapsedMilliseconds>3000)throw new IOException("Did not witness a pending discovery; no cancellation timing claim.");
                    await Task.Delay(10);
                }while(true);
                record("before="+trace);
                var canceled=Stopwatch.StartNew();Call(connection,"Disconnect");
                record("disconnect_call_ms="+canceled.ElapsedMilliseconds);
                if(await Task.WhenAny(pending,Task.Delay(10000))!=pending)throw new TimeoutException("Pending connection has not retired after local cancellation.");
                Exception failure=null;try{await pending;}catch(Exception e){failure=e;}
                record("retirement_ms="+canceled.ElapsedMilliseconds+"; outcome="+(failure==null?"success":failure.GetType().Name+": "+failure.Message));
                record("after="+Property(connection,"LastConnectDiagnostic"));
                var lease=(SemaphoreSlim)type.GetField("transaction",All).GetValue(connection);
                bool free=lease.Wait(0);if(free)lease.Release();
                if((bool)Property(connection,"Connected") || !free || failure==null || !failure.Message.Contains("connection attempt canceled") || canceled.ElapsedMilliseconds>2000)
                    throw new IOException("Local cancellation/retirement contract failed; no radio-cancellation claim.");
                record("PASS pending discovery canceled; disconnected and transaction lease free; no recorder command sent");
            }finally{
                Call(connection,"Dispose");
                if(pending!=null && !pending.IsCompleted){
                    await Task.WhenAny(pending,Task.Delay(30000));
                    record("cleanup.pending_terminal="+pending.IsCompleted);
                    if(pending.IsFaulted){var observed=pending.Exception;}
                }
            }
        }
    }
}
