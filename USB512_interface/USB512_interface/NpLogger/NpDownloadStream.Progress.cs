using System.Threading;
namespace CE32_console
{
    internal sealed partial class NpDownloadStream
    {
        // Count accepted SD payload once after validation, including burst reads.
        // Cache hits and speculative replies discarded without validation do not count.
        long downloadedBytes;
        internal long DownloadedBytes {get{return Interlocked.Read(ref downloadedBytes);}}
        void CountDownloadedPayload(int bytes){Interlocked.Add(ref downloadedBytes,bytes);}
    }
}
