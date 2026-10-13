using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

// Software-only clock contract checks against the actual delivered GUI.
// Passing does not establish sensor availability or physical clock accuracy.
class TestImuSyncReadiness
{
    static Assembly gui;
    static int checks, observations, commits;
    static uint id, state = 1;
    static readonly BindingFlags Flags = BindingFlags.Static | BindingFlags.Instance |
        BindingFlags.Public | BindingFlags.NonPublic;
    static Type Clock { get { return gui.GetType("CE32_console.NpV2OnlineClock", true); } }
    static void Check(bool value) { checks++; if (!value) throw new Exception("Clock contract failed at " + checks); }
    static object Call(Type type, object instance, string name, params object[] values)
    {
        try { return type.GetMethod(name, Flags).Invoke(instance, values); }
        catch (TargetInvocationException ex) { throw ex.InnerException; }
    }
    static uint U(byte[] b, int at) { return BitConverter.ToUInt32(b, at); }
    static void Put(byte[] b, int at, uint value) { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, b, at, 4); }
    static uint Crc(byte[] b)
    {
        uint c = ~0U;
        for (int i = 0; i < 60; i++) { c ^= b[i]; for (int bit = 0; bit < 8; bit++) c = (c >> 1) ^ (0xedb88320U & (0U - (c & 1))); }
        return ~c;
    }
    static Task<byte[]> Exchange(byte[] request)
    {
        Check(U(request, 60) == Crc(request));
        uint op = U(request, 8);
        if (op == 0) observations++; else commits++;
        var b = new byte[64];
        uint[] words = { 0x3252504e, 1, op, U(request, 12), 0, 123, 123,
            op == 1 ? 1U : 0U, op == 1 ? U(request, 28) : 0U, op == 1 ? 1U : 0U,
            op == 1 ? U(request, 20) : 0U, op == 1 ? U(request, 24) : 0U, state, 1000, 0 };
        for (int i = 0; i < words.Length; i++) Put(b, i * 4, words[i]);
        Put(b, 60, Crc(b)); return Task.FromResult(b);
    }
    static void Main(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("TestImuSyncReadiness <checked GUI.exe>");
        gui = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        Console.WriteLine("Actual GUI: " + gui.GetName().Version);
        object[] timing = { 1000000L, 1020000L, 100U, 104U, 0L };
        Check((long)Call(Clock, null, "MidpointUtc", timing) == 1012000L && (long)timing[4] == 9000L);
        timing[2] = 0xfffffffeU; timing[3] = 2U;
        Check((long)Call(Clock, null, "MidpointUtc", timing) == 1012000L);
        timing = new object[] { 1000000L, 1000000L, 0U, 0U, 0L };
        Call(Clock, null, "MidpointUtc", timing); Check((long)timing[4] >= 1000);
        timing[1] = 999999L;
        bool rejected = false;
        try { Call(Clock, null, "MidpointUtc", timing); } catch (IOException) { rejected = true; }
        Check(rejected);
        foreach (uint next in new uint[] { 0, 1, 5, 7, 2, 3, 4, 6, 255 })
        {
            state = next; observations = commits = 0;
            bool stopped = next == 0 || next == 1 || next == 5 || next == 7;
            Check((bool)Call(Clock, null, "CanCalibrate", next) == stopped);
            rejected = false;
            try
            {
                var task = (Task)Call(Clock, null, "Synchronize", (Func<byte[], Task<byte[]>>)Exchange,
                    (Func<uint>)(() => ++id), 64U);
                task.GetAwaiter().GetResult();
                object result = task.GetType().GetProperty("Result").GetValue(task, null);
                Check((uint)result.GetType().GetField("Samples", Flags).GetValue(result) == 64);
                Check((double)result.GetType().GetField("UncertaintySeconds", Flags).GetValue(result) >= .001);
            }
            catch (IOException) { rejected = true; }
            Check(stopped ? !rejected && observations == 64 && commits == 1 : rejected && commits == 0);
        }
        Console.WriteLine("PASS " + checks + " delivered-clock checks: bounds, tick wrap, stopped-only commit; NOT physical precision qualification.");
    }
}
