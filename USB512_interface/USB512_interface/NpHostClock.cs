using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace USB512_interface
{
    // One process-wide PC coordinate for NP, GPIO and post-recording correction.
    // Wall-clock changes are retained as observations, never applied mid-recording.
    internal static class NpHostClock
    {
        internal static readonly string Epoch=Guid.NewGuid().ToString("N");
        internal static readonly long OriginTicks,OriginUtcUs;
        internal static readonly double AnchorHalfWidthUs;
        internal static readonly string Api;
        [DllImport("kernel32.dll")] static extern void GetSystemTimePreciseAsFileTime(out long value);
        static NpHostClock()
        {
            long value,before=Stopwatch.GetTimestamp();
            try { GetSystemTimePreciseAsFileTime(out value);before=Stopwatch.GetTimestamp();GetSystemTimePreciseAsFileTime(out value);Api="GetSystemTimePreciseAsFileTime"; }
            catch(EntryPointNotFoundException) {value=DateTime.UtcNow.ToFileTimeUtc();Api="DateTime.UtcNow fallback";}
            long after=Stopwatch.GetTimestamp();
            OriginTicks=before+(after-before)/2;OriginUtcUs=(value-116444736000000000L)/10;
            AnchorHalfWidthUs=(after-before)*500000.0/Stopwatch.Frequency;
        }
        internal static double MonotonicUs(long ticks) {return (ticks-OriginTicks)*1000000.0/Stopwatch.Frequency;}
        internal static double UnixUs(long ticks) {return OriginUtcUs+MonotonicUs(ticks);}
        internal static long NowUs {get{return (long)UnixUs(Stopwatch.GetTimestamp());}}
        internal static object Receipt {get{return new {host_epoch=Epoch,qpc_frequency=Stopwatch.Frequency,origin_qpc=OriginTicks,origin_pc_unix_us=OriginUtcUs,anchor_half_width_us=AnchorHalfWidthUs,api=Api,network_correction=false};}}
    }
}
