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
        internal bool Enabled, HasEstimate, IsFresh, Automatic;
        internal string Server, ConfiguredServer, Endpoint, Status, Error;
        internal string[] ServerResults;
        internal DateTime Utc, LastSyncUtc;
        internal double OffsetMilliseconds, DelayMilliseconds, UncertaintyMilliseconds, AgeSeconds, FrequencyPpm;
        internal int Stratum, Samples;
        internal bool MeetsAccuracyTarget { get { return IsFresh && UncertaintyMilliseconds <= NetworkTimeClock.TargetAccuracyMilliseconds; } }
        internal string TargetStatus { get { return (MeetsAccuracyTarget ? "within " : "above ") + NetworkTimeClock.TargetAccuracyMilliseconds.ToString("0") + " ms target"; } }
        internal static DateTimeOffset LocalTime(DateTime utc, TimeZoneInfo zone = null)
        {
            return TimeZoneInfo.ConvertTime(new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)), zone ?? TimeZoneInfo.Local);
        }
    }

    internal sealed class NetworkTimeClock : IDisposable
    {
        internal const string DefaultServer = "time.cloudflare.com";
        internal const double TargetAccuracyMilliseconds = 10;
        internal static readonly string[] AutomaticServers = { DefaultServer, "time.nist.gov", "time.windows.com" };
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
        private bool automatic;
        private sealed class Source
        {
            internal string Host, Error;
            internal readonly List<NtpSample> Samples = new List<NtpSample>();
            internal IPEndPoint Endpoint;
            internal int AddressIndex, PollSeconds = 16, MinimumPollSeconds = 16;
            internal long NextPoll;
            internal bool Denied;
        }
        private Source[] sources = new Source[0];
        private Source activeSource;
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
            Configure(enable, hostname, false);
        }
        internal void Configure(bool enable, string hostname, bool automaticSelection)
        {
            hostname = ValidateServer(hostname);
            lock (gate) {
                if (disposed) throw new ObjectDisposedException("NetworkTimeClock");
                enabled = enable; server = hostname; generation++; denied = false; error = null;
                automatic = automaticSelection; activeSource = null;
                sources = (automatic ? AutomaticServers : new[] { hostname }).Select(host => new Source { Host = host }).ToArray();
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
        private double SourceScore(Source source, long timestamp)
        {
            var recent = source.Samples.Where(s => Seconds(timestamp, s.Timestamp) <= MaximumAgeSeconds).ToArray();
            if (source.Denied || source.Error != null || recent.Length == 0) return Double.PositiveInfinity;
            // Keep source ranking separate from holdover age; polling phase must not
            // make otherwise stable peers swap every few seconds.
            return recent.Min(s => s.UncertaintyMilliseconds) + recent.Max(s => s.OffsetMilliseconds) - recent.Min(s => s.OffsetMilliseconds);
        }
        internal void ObserveServer(string hostname, NtpSample observation)
        {
            lock (gate) {
                var source = sources.Single(s => String.Equals(s.Host, hostname, StringComparison.OrdinalIgnoreCase));
                if (source.Samples.Count != 0 && source.Samples.Last().Endpoint != observation.Endpoint) source.Samples.Clear();
                source.Samples.Add(observation);
                if (source.Samples.Count > 8) source.Samples.RemoveAt(0);
                source.Error = null; source.PollSeconds = source.MinimumPollSeconds;
                var best = sources.OrderBy(s => SourceScore(s, observation.Timestamp)).First();
                // Retain an equally good source, avoiding needless phase corrections.
                if (activeSource != null && SourceScore(activeSource, observation.Timestamp) <= SourceScore(best, observation.Timestamp) + 0.1) best = activeSource;
                if (best != source) return;
                if (activeSource != source || latest != null && latest.Endpoint != observation.Endpoint) {
                    samples.Clear(); latest = null;
                }
                activeSource = source;
                // Observe retains the running QPC anchor and slews toward a new
                // source; it never fits a frequency slope across different peers.
                Observe(observation);
            }
        }
        internal void RecordServerFailure(string hostname, Exception exception)
        {
            lock (gate) {
                var source = sources.Single(s => String.Equals(s.Host, hostname, StringComparison.OrdinalIgnoreCase));
                var kiss = exception as NtpServerException;
                if (kiss != null && (kiss.KissCode == "DENY" || kiss.KissCode == "RSTR")) source.Denied = true;
                if (kiss != null && kiss.KissCode == "RATE") {
                    if (source.PollSeconds >= 1024) source.Denied = true;
                    else source.MinimumPollSeconds = source.PollSeconds = Math.Min(1024, source.PollSeconds * 2);
                }
                else if (!source.Denied) source.PollSeconds = Math.Min(1024, source.PollSeconds * 2);
                source.Error = exception is SocketException && ((SocketException)exception).SocketErrorCode == SocketError.TimedOut
                    ? "No NTP response (UDP port 123)." : exception.GetBaseException().Message;
                source.Endpoint = null;
                denied = sources.All(s => s.Denied);
                if (activeSource == source || activeSource == null) error = source.Host + ": " + source.Error;
                if (denied) error = "All servers paused polling; reapply settings to retry.";
            }
        }
        private string DescribeSource(Source source, long timestamp)
        {
            if (source.Denied) return source.Host + ": paused by server";
            if (source.Error != null) return source.Host + ": " + source.Error;
            if (source.Samples.Count == 0) return source.Host + ": waiting";
            var last = source.Samples.Last();
            double age = Math.Max(0, Seconds(timestamp, last.Timestamp));
            return source.Host + ": " + (age > MaximumAgeSeconds ? "stale" : String.Format("RTT {0:F2} ms · quality {1:F2} ms · {2}/8 samples",
                last.DelayMilliseconds, SourceScore(source, timestamp), source.Samples.Count));
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
                    Enabled = enabled, Automatic = automatic, ConfiguredServer = server,
                    Server = activeSource == null ? automatic ? "Automatic selection" : server : activeSource.Host,
                    ServerResults = sources.Select(source => DescribeSource(source, timestamp)).ToArray(),
                    HasEstimate = has, IsFresh = fresh, Utc = utc,
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
            while (true) {
                Source source; int currentGeneration, waitMilliseconds;
                lock (gate) {
                    if (disposed) return;
                    currentGeneration = generation;
                    source = enabled ? sources.Where(s => !s.Denied).OrderBy(s => s.NextPoll).FirstOrDefault() : null;
                    waitMilliseconds = source == null ? Timeout.Infinite : (int)Math.Min(Int32.MaxValue, Math.Max(0, Seconds(source.NextPoll, Stopwatch.GetTimestamp()) * 1000));
                }
                if (source == null || waitMilliseconds > 0) { wake.WaitOne(waitMilliseconds); continue; }
                try {
                    if (source.Endpoint == null) {
                        var dns = Dns.GetHostAddressesAsync(source.Host);
                        if (!dns.Wait(3000)) throw new TimeoutException("NTP hostname lookup timed out.");
                        IPAddress[] addresses = dns.GetAwaiter().GetResult().Where(a => a.AddressFamily == AddressFamily.InterNetwork || a.AddressFamily == AddressFamily.InterNetworkV6)
                            .OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1).ToArray();
                        if (addresses.Length == 0) throw new InvalidOperationException("No NTP server address found.");
                        source.Endpoint = new IPEndPoint(addresses[source.AddressIndex++ % addresses.Length], 123);
                    }
                    NtpSample observation = Query(source.Endpoint, currentGeneration);
                    lock (gate) { if (!disposed && currentGeneration == generation) ObserveServer(source.Host, observation); }
                }
                catch (Exception ex) {
                    lock (gate) {
                        if (disposed) return;
                        if (currentGeneration != generation) continue;
                        RecordServerFailure(source.Host, ex);
                    }
                }
                lock (gate) {
                    if (disposed) return; if (currentGeneration != generation) continue;
                    source.NextPoll = Stopwatch.GetTimestamp() + (long)(source.PollSeconds * frequency);
                }
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
