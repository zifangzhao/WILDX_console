using System;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

// Exercises the built receipt helper only.  No Form, discovery, GATT, USB or hardware is created.
internal static class Np2BleConfigurationTests
{
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Assembly app; static int checks;
    static Type T(string name) { return app.GetType(name, true); }
    static object New(string name, params object[] args) { return Activator.CreateInstance(T(name), All, null, args, null); }
    static object Call(object target, string name, params object[] args) { return (target as Type ?? target.GetType()).GetMethod(name, All).Invoke(target is Type ? null : target, args); }
    static object Field(object target, string name) { return target.GetType().GetField(name, All).GetValue(target); }
    static void Set(object target, string name, object value) { target.GetType().GetField(name, All).SetValue(target, value); }
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static void Await(Task task) { task.GetAwaiter().GetResult(); }
    static string Observe(object receipts, uint state, Delegate reader)
    {
        Task task = (Task)Call(receipts, "Observe", state, reader, CancellationToken.None); Await(task);
        return (string)task.GetType().GetProperty("Result").GetValue(task, null);
    }
    static object Request(byte port, byte seed)
    {
        var selection = new byte[876]; selection[0] = (byte)'N'; selection[1] = (byte)'2'; selection[2] = (byte)'B'; selection[3] = (byte)'1'; selection[4] = 4; selection[5] = port;
        for (int i = 0; i < 32; i++) selection[8 + i] = (byte)(seed + i);
        return New("USB512_interface.Np2ConfigRequest", "{}", selection);
    }
    static object Reply(object request, byte portState, byte verification, byte error, int portDelta = 0, int model = 4, bool crc = true, bool id = true)
    {
        byte[] selection = (byte[])Field(request, "Selection"); byte[] identity = new byte[32]; Buffer.BlockCopy(selection, 8, identity, 0, identity.Length);
        if (!id) identity[0] ^= 1;
        var reply = New("USB512_interface.Np2ConfigReply");
        Set(reply, "Error", error); Set(reply, "PortState", portState); Set(reply, "Verification", verification);
        Set(reply, "Port", (byte)(selection[5] + portDelta)); Set(reply, "Model", (byte)model);
        uint payloadCrc = (uint)Call(T("USB512_interface.Np2ConfigClient"), "Crc", selection, selection.Length);
        Set(reply, "PayloadCrc", crc ? payloadCrc : payloadCrc ^ 1U); Set(reply, "ConfigurationId", identity);
        return reply;
    }
    static object Completed(Type replyType, object reply)
    {
        return typeof(Task).GetMethod("FromResult").MakeGenericMethod(replyType).Invoke(null, new[] { reply });
    }
    static object Pending(Type replyType, out object completion)
    {
        Type tcsType = typeof(TaskCompletionSource<>).MakeGenericType(replyType); completion = Activator.CreateInstance(tcsType);
        return tcsType.GetProperty("Task").GetValue(completion, null);
    }
    static Delegate Reader(Type replyType, Func<byte, object> response)
    {
        Type taskType = typeof(Task<>).MakeGenericType(replyType);
        Type funcType = typeof(Func<,,>).MakeGenericType(typeof(byte), typeof(CancellationToken), taskType);
        var port = Expression.Parameter(typeof(byte), "port"); var cancel = Expression.Parameter(typeof(CancellationToken), "cancel");
        var body = Expression.Convert(Expression.Invoke(Expression.Constant(response), port), taskType);
        return Expression.Lambda(funcType, body, port, cancel).Compile();
    }
    static void Expect(object receipts, params object[] requests)
    {
        Array typed = Array.CreateInstance(T("USB512_interface.Np2ConfigRequest"), requests.Length);
        for (int i = 0; i < requests.Length; i++) typed.SetValue(requests[i], i);
        Call(receipts, "Expect", typed);
    }
    static int PendingCount(object receipts) { return (int)receipts.GetType().GetProperty("Pending", All).GetValue(receipts, null); }
    static void Receipts()
    {
        Type replyType = T("USB512_interface.Np2ConfigReply");
        foreach (uint state in new uint[] { 0, 1, 2 })
        {
            var receipts = New("USB512_interface.NpBleConfigurationReceipts"); object request = Request(0, 10); Expect(receipts, request); int calls = 0;
            string result = Observe(receipts, state, Reader(replyType, port => { calls++; return Completed(replyType, Reply(request, 3, 1, 0)); }));
            Check(result == null && calls == 0 && PendingCount(receipts) == 1, "Idle/preparing status queried or consumed an NP2 receipt");
        }
        foreach (uint state in new uint[] { 3, 4, 5, 6, 7, 255 })
        {
            var receipts = New("USB512_interface.NpBleConfigurationReceipts"); object first = Request(0, 20), second = Request(1, 40); Expect(receipts, first, second);
            var ports = new List<byte>(); var reader = Reader(replyType, port => { ports.Add(port); return Completed(replyType, Reply(port == 0 ? first : second, 3, 1, 0)); });
            Check(Observe(receipts, state, reader).Contains("applied; matching ID/CRC") && PendingCount(receipts) == 1 && ports.Count == 1 && ports[0] == 0, "First allowed poll did not read exactly one matching port");
            Check(Observe(receipts, state, reader).Contains("applied; matching ID/CRC") && PendingCount(receipts) == 0 && ports.Count == 2 && ports[1] == 1, "Second allowed poll did not read exactly one matching port");
            Check(Observe(receipts, state, reader) == null && ports.Count == 2, "Confirmed receipts issued extra status reads");
        }
        object valid = Request(0, 80);
        foreach (object bad in new[] { Reply(valid, 2, 1, 0), Reply(valid, 3, 1, 1), Reply(valid, 3, 1, 0, portDelta: 1), Reply(valid, 3, 1, 0, model: 3), Reply(valid, 3, 1, 0, crc: false), Reply(valid, 3, 1, 0, id: false), Reply(valid, 3, 0, 0) })
        {
            var receipts = New("USB512_interface.NpBleConfigurationReceipts"); Expect(receipts, valid);
            string result = Observe(receipts, 4, Reader(replyType, port => Completed(replyType, bad)));
            Check(result.Contains("NOT confirmed applied") && PendingCount(receipts) == 0, "Staged/failed/identity-mismatched receipt claimed application");
        }
        {
            var receipts = New("USB512_interface.NpBleConfigurationReceipts"); Expect(receipts, valid); bool timeout = false;
            try { Observe(receipts, 3, Reader(replyType, port => { throw new TimeoutException(); })); } catch (TimeoutException) { timeout = true; }
            Check(timeout && PendingCount(receipts) == 1, "Timeout discarded pending NP2 receipt");
            Check(Observe(receipts, 3, Reader(replyType, port => Completed(replyType, Reply(valid, 3, 1, 0)))).Contains("applied;") && PendingCount(receipts) == 0, "Timed-out receipt could not be retried read-only");
        }
        {
            var receipts = New("USB512_interface.NpBleConfigurationReceipts"); object oldRequest = Request(0, 100), newRequest = Request(1, 120); Expect(receipts, oldRequest);
            object completion; object pending = Pending(replyType, out completion); Task old = (Task)Call(receipts, "Observe", 3U, Reader(replyType, port => pending), CancellationToken.None);
            Expect(receipts, newRequest); completion.GetType().GetMethod("SetResult").Invoke(completion, new[] { Reply(oldRequest, 3, 1, 0) }); Await(old);
            Check(PendingCount(receipts) == 1, "Stale awaited receipt removed a newer expectation");
            Check(Observe(receipts, 3, Reader(replyType, port => Completed(replyType, Reply(newRequest, 3, 1, 0)))).Contains("applied;") && PendingCount(receipts) == 0, "New expectation was not retained after stale await");
        }
    }
    static int Main(string[] args)
    {
        try { if (args.Length != 1) throw new ArgumentException("Pass the built WILDX_console.exe path."); app = Assembly.LoadFrom(Path.GetFullPath(args[0])); Receipts(); Console.WriteLine("PASS " + checks + " built-assembly NP2 BLE configuration receipt checks; no UI, discovery, GATT, USB, or hardware."); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
