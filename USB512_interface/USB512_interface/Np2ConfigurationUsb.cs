using System;
using System.Threading;
using System.Threading.Tasks;

namespace USB512_interface
{
    internal sealed class Np2ConfigurationUsb : INp2ConfigTransport
    {
        private readonly NpV2UsbDevice device;

        internal Np2ConfigurationUsb(NpV2UsbDevice device)
        {
            this.device = device ?? throw new ArgumentNullException("device");
        }

        public Task<byte[]> ExchangeAsync(byte[] request, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            return Task.FromResult(device.ExchangeConfiguration(request));
        }

        internal static Np2ConfigReply StageForCapture(NpV2UsbDevice device, Np2ConfigRequest request)
        {
            return new Np2ConfigClient(new Np2ConfigurationUsb(device)).StageAsync(request, CancellationToken.None, true).GetAwaiter().GetResult();
        }

        internal static Np2ConfigReply ConfirmApplied(NpV2UsbDevice device, Np2ConfigRequest request)
        {
            var client = new Np2ConfigClient(new Np2ConfigurationUsb(device));
            var reply = client.StatusAsync(request.Port, CancellationToken.None).GetAwaiter().GetResult();
            Np2ConfigClient.RequireApplied(reply, request);
            return reply;
        }
    }
}
