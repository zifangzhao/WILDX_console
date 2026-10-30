using System;
using System.Threading.Tasks;
namespace CE32_console { internal sealed class NpSdStatus {} }
namespace USB512_interface {
    internal sealed partial class NpBleForm {
        static int sends;
        Task<CE32_console.NpSdStatus> Send(byte[] request){sends++;return Task.FromResult<CE32_console.NpSdStatus>(null);}
        static int Main(){new NpBleForm().WarmBleFirstSyncCapabilities().GetAwaiter().GetResult();if(sends!=0)throw new Exception("Default-off warm-up performed I/O");Console.WriteLine("PASS default-off actual warm-up stub; no connection/query/Form constructed.");return 0;}
    }
}
