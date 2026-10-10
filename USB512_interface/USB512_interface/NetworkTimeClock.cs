using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace USB512_interface
{
    internal sealed class NtpSample
    {
        internal long Timestamp;
        internal double OffsetMilliseconds, DelayMilliseconds, UncertaintyMilliseconds;
        internal int Stratum;
        internal string Endpoint;
    }

    internal sealed class NtpServerException : Exception
    {
        internal readonly string KissCode;
        internal NtpServerException(string code) : base("Time server response: " + code) { KissCode = code; }
    }

    // SNTP's four timestamps (RFC 5905 section 8), with client time measured
    // against one QPC anchor so Windows clock adjustments cannot alter an exchange.
    internal static class NtpPacket
    {
        private static readonly DateTime Epoch = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private const double EraSeconds = 4294967296.0;
        internal static byte[] Request(DateTime utc)
        {
            var packet = new byte[48];
            packet[0] = 0x23; // version 4, client mode
            WriteTimestamp(packet, 40, utc);
            return packet;
        }
        internal static void WriteTimestamp(byte[] packet, int offset, DateTime utc)
        {
            long ticks = utc.Ticks - Epoch.Ticks;
            if (ticks < 0) throw new ArgumentOutOfRangeException("utc");
            WriteUInt32(packet, offset, unchecked((uint)(ticks / TimeSpan.TicksPerSecond)));
            WriteUInt32(packet, offset + 4, (uint)(((ulong)(ticks % TimeSpan.TicksPerSecond) << 32) / TimeSpan.TicksPerSecond));
        }
        internal static DateTime ReadTimestamp(byte[] packet, int offset, DateTime nearbyUtc)
        {
            uint seconds = ReadUInt32(packet, offset), fraction = ReadUInt32(packet, offset + 4);
            double era = Math.Round(((nearbyUtc - Epoch).TotalSeconds - seconds) / EraSeconds);
            long ticks = checked(Epoch.Ticks + checked((long)(era * EraSeconds + seconds) * TimeSpan.TicksPerSecond)
                + (long)(((ulong)fraction * TimeSpan.TicksPerSecond) >> 32));
            return new DateTime(ticks, DateTimeKind.Utc);
        }
        internal static NtpSample Parse(byte[] request, byte[] response, DateTime sentUtc,
            double elapsedMilliseconds, long receivedTimestamp, double localPrecisionMilliseconds, string endpoint)
        {
            if (response == null || response.Length < 48) throw new InvalidOperationException("Truncated NTP reply.");
            int version = (response[0] >> 3) & 7;
            if ((response[0] & 7) != 4 || (version != 3 && version != 4)) throw new InvalidOperationException("Invalid NTP server mode/version.");
            for (int i = 0; i < 8; i++)
                if (response[24 + i] != request[40 + i]) throw new InvalidOperationException("NTP reply does not match this request.");
            if (response[1] == 0) throw new NtpServerException(Encoding.ASCII.GetString(response, 12, 4));
            // A pending leap cannot be modeled safely by this secondary clock.
            if ((response[0] >> 6) != 0 || response[1] >= 16) throw new InvalidOperationException("Time server reports a leap adjustment or an unsynchronized clock.");
            if (ReadUInt32(response, 32) == 0 && ReadUInt32(response, 36) == 0 ||
                ReadUInt32(response, 40) == 0 && ReadUInt32(response, 44) == 0)
                throw new InvalidOperationException("Missing NTP server timestamps.");
            int precisionExponent = unchecked((sbyte)response[3]);
            if (precisionExponent > 0) throw new InvalidOperationException("Time server precision is insufficient.");
            double precision = Math.Pow(2, precisionExponent) * 1000;
            DateTime t1 = ReadTimestamp(request, 40, sentUtc);
            DateTime t2 = ReadTimestamp(response, 32, sentUtc);
            DateTime t3 = ReadTimestamp(response, 40, sentUtc);
            double processing = (t3 - t2).TotalMilliseconds;
            if (elapsedMilliseconds < 0 || processing < 0 || processing > elapsedMilliseconds + Math.Max(1, precision * 2 + localPrecisionMilliseconds))
                throw new InvalidOperationException("Inconsistent NTP timing.");
            double delay = Math.Max(0, elapsedMilliseconds - processing);
            double rootDelay = Math.Max(0, unchecked((int)ReadUInt32(response, 4)) / 65536.0 * 1000);
            double rootDispersion = ReadUInt32(response, 8) / 65536.0 * 1000;
            double uncertainty = (delay + rootDelay) / 2 + rootDispersion + precision + localPrecisionMilliseconds;
            if (uncertainty > 1500) throw new InvalidOperationException("Time server synchronization distance exceeds 1.5 seconds.");
            return new NtpSample {
                Timestamp = receivedTimestamp, DelayMilliseconds = delay, UncertaintyMilliseconds = uncertainty,
                OffsetMilliseconds = ((t2 - t1).TotalMilliseconds + (t3 - t1).TotalMilliseconds - elapsedMilliseconds) / 2,
                Stratum = response[1], Endpoint = endpoint
            };
        }
        private static uint ReadUInt32(byte[] data, int offset)
        {
            return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];
        }
        private static void WriteUInt32(byte[] data, int offset, uint value)
        {
            for (int i = 3; i >= 0; i--) { data[offset + i] = (byte)value; value >>= 8; }
        }
    }

    internal sealed class NetworkTimeSnapshot
    {
        internal bool Enabled, HasEstimate, IsFresh;
        internal string Server, Endpoint, Status, Error;
        internal DateTime Utc, LastSyncUtc;
        internal double OffsetMilliseconds, DelayMilliseconds, UncertaintyMilliseconds, AgeSeconds, FrequencyPpm;
        internal int Stratum, Samples;
        internal bool MeetsOneMillisecondTarget { get { return IsFresh && UncertaintyMilliseconds <= 1; } }
    }

    internal sealed class NetworkTimeClock : IDisposable
    {
        internal const string DefaultServer = "time.cloudflare.com";
        internal const double MaximumAgeSeconds = 120;
        private const double DriftBoundPpm = 100, SlewMillisecondsPerSecond = 0.5;
        private readonly object gate = new object();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly List<NtpSample> samples = new List<NtpSample>();
        private readonly DateTime baseUtc;
        private readonly long baseTimestamp;
        private readonly double frequency, localPrecisionMilliseconds;
        private Thread worker;
        private UdpClient activeSocket;
        private bool enabled, disposed, denied;
        private int generation;
        private string server = DefaultServer, error;
        private NtpSample selected, latest;
        private long anchorTimestamp;
        private DateTime anchorUtc;
        private double frequencyPpm, phaseMilliseconds, jitterMilliseconds;

        [DllImport("kernel32.dll")]
        private static extern void GetSystemTimePreciseAsFileTime(out long fileTime);
        internal static DateTime PreciseUtcNow()
        {
            long fileTime;
            GetSystemTimePreciseAsFileTime(out fileTime);
            return DateTime.FromFileTimeUtc(fileTime);
        }
        internal NetworkTimeClock()
        {
            frequency = Stopwatch.Frequency;
            long before = Stopwatch.GetTimestamp();
            try { baseUtc = PreciseUtcNow(); localPrecisionMilliseconds = 0.001; }
            catch (EntryPointNotFoundException) { baseUtc = DateTime.UtcNow; localPrecisionMilliseconds = 16; }
            long after = Stopwatch.GetTimestamp();
            baseTimestamp = before + (after - before) / 2;
            localPrecisionMilliseconds += (after - before) / frequency * 500 + 1000 / frequency;
        }
        // Deterministic clock tests use synthetic QPC ticks; no network or device is opened.
        internal NetworkTimeClock(DateTime utc, long timestamp, double ticksPerSecond)
        {
            baseUtc = utc; baseTimestamp = timestamp; frequency = ticksPerSecond; localPrecisionMilliseconds = 0.001;
        }
        internal static string ValidateServer(string value)
        {
            value = (value ?? "").Trim();
            if (value.Length == 0 || value.Length > 253 || Uri.CheckHostName(value) == UriHostNameType.Unknown)
                throw new ArgumentException("Enter an NTP hostname or IP address without a URL or port.");
            return value;
        }
        internal void Configure(bool enable, string hostname)
        {
            hostname = ValidateServer(hostname);
            lock (gate) {
                if (disposed) throw new ObjectDisposedException("NetworkTimeClock");
                enabled = enable; server = hostname; generation++; denied = false; error = null;
                samples.Clear(); selected = latest = null; frequencyPpm = phaseMilliseconds = jitterMilliseconds = 0;
                if (activeSocket != null) activeSocket.Close();
            }
            wake.Set();
        }
        internal void Start()
        {
            lock (gate) {
                if (disposed) throw new ObjectDisposedException("NetworkTimeClock");
                if (worker != null) return;
                worker = new Thread(Run) { IsBackground = true, Name = "Secondary NTP clock", Priority = ThreadPriority.BelowNormal };
                worker.Start();
            }
        }
        private double Seconds(long timestamp, long origin) { return (timestamp - origin) / frequency; }
        private DateTime BaselineUtc(long timestamp) { return baseUtc.AddTicks((long)(Seconds(timestamp, baseTimestamp) * TimeSpan.TicksPerSecond)); }
        private DateTime EstimateUtc(long timestamp)
        {
            double seconds = Math.Max(0, Seconds(timestamp, anchorTimestamp));
            double correction = Math.Sign(phaseMilliseconds) * Math.Min(Math.Abs(phaseMilliseconds), seconds * SlewMillisecondsPerSecond);
            return anchorUtc.AddTicks((long)((seconds * 1000 * (1 + frequencyPpm / 1000000) + correction) * TimeSpan.TicksPerMillisecond));
        }
        internal void Observe(NtpSample sample)
        {
            lock (gate) {
                if (latest != null && sample.Timestamp <= latest.Timestamp) throw new ArgumentException("Samples must arrive in QPC order.");
                samples.Add(sample);
                if (samples.Count > 8) samples.RemoveAt(0);
                double rate = 0;
                if (samples.Count >= 4 && Seconds(sample.Timestamp, samples[0].Timestamp) >= 64) {
                    double meanX = samples.Average(s => Seconds(s.Timestamp, baseTimestamp));
                    double meanY = samples.Average(s => s.OffsetMilliseconds);
                    double numerator = 0, denominator = 0;
                    foreach (var s in samples) {
                        double x = Seconds(s.Timestamp, baseTimestamp) - meanX;
                        numerator += x * (s.OffsetMilliseconds - meanY); denominator += x * x;
                    }
                    rate = Math.Max(-DriftBoundPpm, Math.Min(DriftBoundPpm, numerator / denominator * 1000));
                }
                var best = samples.OrderBy(s => s.UncertaintyMilliseconds + Seconds(sample.Timestamp, s.Timestamp) * DriftBoundPpm / 1000).First();
                double targetOffset = best.OffsetMilliseconds + Seconds(sample.Timestamp, best.Timestamp) * rate / 1000;
                DateTime target = BaselineUtc(sample.Timestamp).AddTicks((long)(targetOffset * TimeSpan.TicksPerMillisecond));
                DateTime current = selected == null ? target : EstimateUtc(sample.Timestamp);
                jitterMilliseconds = samples.Max(s => Math.Abs(s.OffsetMilliseconds + Seconds(best.Timestamp, s.Timestamp) * rate / 1000 - best.OffsetMilliseconds));
                anchorTimestamp = sample.Timestamp; anchorUtc = current; phaseMilliseconds = (target - current).TotalMilliseconds;
                frequencyPpm = rate; selected = best; latest = sample; error = null;
            }
        }
        private double Uncertainty(long timestamp)
        {
            double remaining = Math.Max(0, Math.Abs(phaseMilliseconds) - Math.Max(0, Seconds(timestamp, anchorTimestamp)) * SlewMillisecondsPerSecond);
            return selected.UncertaintyMilliseconds + jitterMilliseconds + Math.Max(0, Seconds(timestamp, selected.Timestamp)) * DriftBoundPpm / 1000 + remaining;
        }
        internal bool TryGetUtc(out DateTime utc, out double uncertaintyMilliseconds)
        {
            lock (gate) {
                long timestamp = Stopwatch.GetTimestamp();
                bool fresh = enabled && !disposed && !denied && latest != null && Seconds(timestamp, latest.Timestamp) <= MaximumAgeSeconds;
                utc = fresh ? EstimateUtc(timestamp) : DateTime.MinValue;
                uncertaintyMilliseconds = fresh ? Uncertainty(timestamp) : Double.PositiveInfinity;
                return fresh;
            }
        }
        internal NetworkTimeSnapshot GetSnapshot()
        {
            DateTime wall;
            try { wall = PreciseUtcNow(); } catch (EntryPointNotFoundException) { wall = DateTime.UtcNow; }
            return GetSnapshot(Stopwatch.GetTimestamp(), wall);
        }
        internal NetworkTimeSnapshot GetSnapshot(long timestamp, DateTime systemUtc)
        {
            lock (gate) {
                bool has = selected != null;
                double age = has ? Math.Max(0, Seconds(timestamp, latest.Timestamp)) : 0;
                bool fresh = enabled && !disposed && !denied && has && age <= MaximumAgeSeconds;
                DateTime utc = has ? EstimateUtc(timestamp) : DateTime.MinValue;
                return new NetworkTimeSnapshot {
                    Enabled = enabled, Server = server, HasEstimate = has, IsFresh = fresh, Utc = utc,
                    Status = disposed ? "Stopped" : !enabled ? "Disabled" : denied ? "Polling paused by server" : !has ? "Waiting for time server" : !fresh ? "Stale holdover" : error != null ? "Holdover" : "Tracking",
                    Error = error, AgeSeconds = age, Endpoint = has ? selected.Endpoint : null,
                    LastSyncUtc = has ? BaselineUtc(latest.Timestamp).AddTicks((long)(latest.OffsetMilliseconds * TimeSpan.TicksPerMillisecond)) : DateTime.MinValue,
                    OffsetMilliseconds = has ? (utc - systemUtc).TotalMilliseconds : 0,
                    DelayMilliseconds = has ? selected.DelayMilliseconds : 0,
                    UncertaintyMilliseconds = has ? Uncertainty(timestamp) : Double.PositiveInfinity,
                    FrequencyPpm = frequencyPpm, Samples = samples.Count, Stratum = has ? selected.Stratum : 0
                };
            }
        }
        private void Run()
        {
            int seenGeneration = -1, addressIndex = 0, pollSeconds = 16, ratePollSeconds = 16;
            while (true) {
                string hostname; int currentGeneration; bool canPoll;
                lock (gate) {
                    if (disposed) return;
                    hostname = server; currentGeneration = generation; canPoll = enabled && !denied;
                }
                if (seenGeneration != currentGeneration) { seenGeneration = currentGeneration; pollSeconds = ratePollSeconds = 16; addressIndex = 0; wake.WaitOne(0); }
                if (!canPoll) { wake.WaitOne(); continue; }
                try {
                    var dns = Dns.GetHostAddressesAsync(hostname);
                    if (!dns.Wait(3000)) throw new TimeoutException("NTP hostname lookup timed out.");
                    IPAddress[] addresses = dns.GetAwaiter().GetResult().Where(a => a.AddressFamily == AddressFamily.InterNetwork || a.AddressFamily == AddressFamily.InterNetworkV6)
                        .OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1).ToArray();
                    if (addresses.Length == 0) throw new InvalidOperationException("No NTP server address found.");
                    NtpSample observation = Query(new IPEndPoint(addresses[addressIndex++ % addresses.Length], 123), currentGeneration);
                    lock (gate) { if (!disposed && currentGeneration == generation) { Observe(observation); pollSeconds = ratePollSeconds; } }
                }
                catch (Exception ex) {
                    lock (gate) {
                        if (disposed) return;
                        if (currentGeneration != generation) continue;
                        var kiss = ex as NtpServerException;
                        if (kiss != null && (kiss.KissCode == "DENY" || kiss.KissCode == "RSTR")) denied = true;
                        if (kiss != null && kiss.KissCode == "RATE") {
                            if (pollSeconds >= 1024) denied = true;
                            else pollSeconds = ratePollSeconds = Math.Min(1024, pollSeconds * 2);
                        }
                        else if (!denied) pollSeconds = Math.Max(ratePollSeconds, Math.Min(256, pollSeconds * 2));
                        error = ex is SocketException && ((SocketException)ex).SocketErrorCode == SocketError.TimedOut ? "No NTP response (UDP port 123)." : ex.GetBaseException().Message;
                        if (denied) error += ". Polling stopped; reapply settings to retry.";
                    }
                }
                lock (gate) { if (disposed) return; if (currentGeneration != generation) continue; }
                wake.WaitOne(pollSeconds * 1000);
            }
        }
        internal NtpSample Query(IPEndPoint endpoint, int expectedGeneration)
        {
            using (var socket = new UdpClient(endpoint.AddressFamily)) {
                lock (gate) {
                    if (disposed || !enabled || denied || expectedGeneration != generation) throw new OperationCanceledException();
                    activeSocket = socket;
                }
                try {
                    socket.Client.ReceiveTimeout = 2000;
                    socket.Client.SendTimeout = 2000;
                    socket.Connect(endpoint);
                    long sent = Stopwatch.GetTimestamp();
                    DateTime utc = BaselineUtc(sent);
                    byte[] request = NtpPacket.Request(utc);
                    socket.Send(request, request.Length);
                    IPEndPoint remote = null;
                    byte[] response = socket.Receive(ref remote);
                    long received = Stopwatch.GetTimestamp();
                    return NtpPacket.Parse(request, response, utc, Seconds(received, sent) * 1000, received, localPrecisionMilliseconds, endpoint.ToString());
                }
                finally { lock (gate) { if (activeSocket == socket) activeSocket = null; } }
            }
        }
        public void Dispose()
        {
            Thread thread;
            lock (gate) {
                if (disposed) return;
                disposed = true; if (activeSocket != null) activeSocket.Close(); thread = worker;
            }
            wake.Set();
            if (thread != null) thread.Join(); // DNS and socket waits are bounded; not part of the capture path.
            wake.Dispose();
        }
    }
}
