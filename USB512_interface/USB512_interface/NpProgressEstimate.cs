using System;
using System.Diagnostics;
using System.Globalization;

namespace USB512_interface
{
    // One estimate per phase: USB, decoding, merging and encoding have different rates.
    internal sealed class NpProgressEstimate
    {
        readonly Stopwatch watch=Stopwatch.StartNew();
        internal string Elapsed {get{return "Elapsed "+Duration(watch.Elapsed.TotalSeconds);}}
        internal string Describe(double done,double total,bool known=true){return Elapsed+" · "+Remaining(done,total,watch.Elapsed.TotalSeconds,known);}
        internal string Percent(int percent,bool known=true){return Describe(percent,100,known);}
        internal static string TransferRate(long bytes,double seconds)
        {
            if(bytes<=0||seconds<0.25||Double.IsNaN(seconds)||Double.IsInfinity(seconds))return "Measuring speed…";
            return (bytes/seconds/1e6).ToString("F1",CultureInfo.InvariantCulture)+" MB/s";
        }
        internal static string Duration(double seconds)
        {
            if(Double.IsNaN(seconds)||Double.IsInfinity(seconds)||seconds<0||seconds>=TimeSpan.MaxValue.TotalSeconds)return "--:--:--";
            long s=(long)seconds;return (s/3600).ToString("00",CultureInfo.InvariantCulture)+":"+(s/60%60).ToString("00",CultureInfo.InvariantCulture)+":"+(s%60).ToString("00",CultureInfo.InvariantCulture);
        }
        internal static string Remaining(double done,double total,double elapsedSeconds,bool known=true)
        {
            if(!known||total<=0||Double.IsNaN(total)||Double.IsInfinity(total))return "Time remaining unknown (size unknown)";
            if(done>=total)return "Finalizing…";
            if(done<=0||Double.IsNaN(done)||Double.IsInfinity(done)||elapsedSeconds<3||Double.IsNaN(elapsedSeconds)||Double.IsInfinity(elapsedSeconds))return "Estimating time…";
            double seconds=Math.Ceiling(elapsedSeconds*((total-done)/done));
            return Double.IsNaN(seconds)||Double.IsInfinity(seconds)||seconds>=TimeSpan.MaxValue.TotalSeconds?"Estimating time…":"~"+Duration(seconds)+" remaining";
        }
    }
}
