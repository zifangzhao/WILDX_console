using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using USB512_interface;

internal static class TestNetworkTime
{
    private static int checks;
    private static readonly DateTime Origin = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
    private static void Check(bool condition, string reason) { checks++; if (!condition) throw new Exception(reason); }
    private static void Near(double actual, double expected, double tolerance, string reason) { Check(Math.Abs(actual - expected) <= tolerance, reason + ": " + actual + " vs " + expected); }
    private static void Reject(Action action, string reason)
    {
        bool rejected = false;
        try { action(); } catch (InvalidOperationException) { rejected = true; } catch (ArgumentException) { rejected = true; }
        Check(rejected, "Accepted " + reason);
    }
    private static byte[] Reply(byte[] request, DateTime received, DateTime sent)
    {
        var response = new byte[48]; response[0] = 0x24; response[1] = 2; response[3] = unchecked((byte)-20);
        Buffer.BlockCopy(request, 40, response, 24, 8);
        NtpPacket.WriteTimestamp(response, 32, received); NtpPacket.WriteTimestamp(response, 40, sent);
        return response;
    }
    private static NtpSample Parse(byte[] request, byte[] reply, double elapsed = 22)
    {
        return NtpPacket.Parse(request, reply, Origin, elapsed, 22000, 0.001, "127.0.0.1:123");
    }
    private static NtpSample Sample(double seconds, double offset = 25, double error = 0.05)
    {
        return new NtpSample { Timestamp = (long)(seconds * 1000000), OffsetMilliseconds = offset,
            DelayMilliseconds = error * 2, UncertaintyMilliseconds = error, Stratum = 1, Endpoint = "test" };
    }
    private static void Protocol()
    {
        byte[] request = NtpPacket.Request(Origin);
        Check(request.Length == 48 && request[0] == 0x23, "Standard NTPv4 client header");
        byte[] reply = Reply(request, Origin.AddMilliseconds(260), Origin.AddMilliseconds(262));
        var sample = Parse(request, reply);
        Near(sample.OffsetMilliseconds, 250, 0.001, "Four-timestamp offset");
        Near(sample.DelayMilliseconds, 20, 0.001, "Server processing excluded from RTT");
        Near(sample.UncertaintyMilliseconds, 10.001953674, 0.001, "Path uncertainty and precision");
        var asymmetric = Parse(request, Reply(request, Origin.AddMilliseconds(254), Origin.AddMilliseconds(256)));
        Near(asymmetric.OffsetMilliseconds, 244, 0.001, "Asymmetric path changes estimated offset");
        Check(Math.Abs(asymmetric.OffsetMilliseconds - 250) <= asymmetric.UncertaintyMilliseconds, "Path uncertainty includes asymmetric delay error");
        reply[0] = 0x1c; Check(Parse(request, reply).Stratum == 2, "NTPv3 interoperability"); reply[0] = 0x24;
        foreach (int index in new[] { 24, 31 }) { byte saved = reply[index]; reply[index] ^= 1; Reject(() => Parse(request, reply), "origin mismatch"); reply[index] = saved; }
        Reject(() => Parse(request, new byte[47]), "short packet");
        foreach (byte header in new byte[] { 0x23, 0x2c, 0x64, 0xa4, 0xe4 }) { reply[0] = header; Reject(() => Parse(request, reply), "mode/version/leap"); }
        reply[0] = 0x24; reply[1] = 16; Reject(() => Parse(request, reply), "unsynchronized stratum");
        foreach (string code in new[] { "RATE", "DENY", "RSTR" }) {
            reply[1] = 0; Buffer.BlockCopy(Encoding.ASCII.GetBytes(code), 0, reply, 12, 4);
            try { Parse(request, reply); throw new Exception("Accepted KoD"); }
            catch (NtpServerException ex) { Check(ex.KissCode == code, "KoD code retained"); }
        }
        reply = Reply(request, Origin.AddMilliseconds(260), Origin.AddMilliseconds(262));
        Array.Clear(reply, 32, 8); Reject(() => Parse(request, reply), "zero receive timestamp");
        reply = Reply(request, Origin.AddMilliseconds(260), Origin.AddMilliseconds(259)); Reject(() => Parse(request, reply), "reversed server timestamps");
        reply = Reply(request, Origin.AddMilliseconds(260), Origin.AddMilliseconds(360)); Reject(() => Parse(request, reply), "processing longer than exchange");
        reply = Reply(request, Origin.AddMilliseconds(260), Origin.AddMilliseconds(262));
        reply[8] = 0; reply[9] = 2; Reject(() => Parse(request, reply), "excessive dispersion");
        reply = Reply(request, Origin.AddMilliseconds(260), Origin.AddMilliseconds(262)); reply[4] = 0xff; reply[5] = 0xff;
        Near(Parse(request, reply).UncertaintyMilliseconds, sample.UncertaintyMilliseconds, 0.001, "Signed negative root delay does not reduce uncertainty");
        reply[4] = 0; reply[5] = 0; reply[6] = 0x80; reply[8] = 0; reply[9] = 0; reply[10] = 0x40;
        Near(Parse(request, reply).UncertaintyMilliseconds, 510.001953674, 0.001, "Root delay and dispersion included");
        foreach (DateTime utc in new[] { Origin.AddTicks(1234567), new DateTime(2036, 2, 7, 6, 28, 15, DateTimeKind.Utc).AddTicks(9999999),
            new DateTime(2036, 2, 7, 6, 28, 16, DateTimeKind.Utc).AddTicks(12345), new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc) }) {
            var encoded = NtpPacket.Request(utc); DateTime decoded = NtpPacket.ReadTimestamp(encoded, 40, utc);
            Check(Math.Abs((decoded - utc).Ticks) <= 1, "NTP fraction and era roundtrip " + utc);
        }
        DateTime boundary = new DateTime(2036, 2, 7, 6, 28, 15, DateTimeKind.Utc).AddMilliseconds(990);
        request = NtpPacket.Request(boundary); reply = Reply(request, boundary.AddMilliseconds(12), boundary.AddMilliseconds(14));
        sample = NtpPacket.Parse(request, reply, boundary, 24, 0, 0.001, "era test");
        Near(sample.OffsetMilliseconds, 1, 0.001, "Exchange crossing NTP era rollover");
        foreach (string hostname in new[] { "time.cloudflare.com", "192.0.2.1", "::1", "localhost" }) Check(NetworkTimeClock.ValidateServer(hostname) == hostname, "Valid server");
        foreach (string hostname in new[] { "", "https://time.cloudflare.com", "server:123", "bad host", "a/b", new string('x', 254) }) Reject(() => NetworkTimeClock.ValidateServer(hostname), "invalid server");
    }
    private static void Discipline()
    {
        using (var clock = new NetworkTimeClock(Origin, 0, 1000000)) {
            clock.Configure(true, NetworkTimeClock.DefaultServer);
            Check(!clock.GetSnapshot(0, Origin).HasEstimate, "No estimate before first valid reply");
            clock.Observe(Sample(0));
            var first = clock.GetSnapshot(0, Origin);
            Near((first.Utc - Origin).TotalMilliseconds, 25, 0.001, "Initial UTC anchor");
            Check(first.IsFresh && first.MeetsAccuracyTarget, "Initial low-uncertainty result");
            var held = clock.GetSnapshot(100000000, Origin.AddSeconds(100));
            Near(held.UncertaintyMilliseconds, 10.05, 0.001, "100 ppm holdover bound");
            Check(held.IsFresh && !held.MeetsAccuracyTarget, "Holdover loses 10 ms target before expiration");
            Check(!clock.GetSnapshot(120001000, Origin.AddSeconds(120.001)).IsFresh, "Stale after 120 seconds");
            var steppedWindows = clock.GetSnapshot(1000000, Origin.AddHours(1));
            Near((steppedWindows.Utc - Origin).TotalMilliseconds, 1025, 0.001, "Windows step does not alter network clock");
            clock.Observe(Sample(16, 24.5));
            DateTime prior = clock.GetSnapshot(16000000, Origin).Utc;
            for (int i = 1; i <= 20000; i++) {
                DateTime now = clock.GetSnapshot(16000000 + i * 100, Origin).Utc;
                Check(now > prior, "Negative phase correction is monotonic"); prior = now;
            }
            Near((clock.GetSnapshot(18000000, Origin).Utc - Origin.AddSeconds(18)).TotalMilliseconds, 24.5, 0.001, "Phase correction finishes without overshoot");
            clock.Observe(Sample(32, 24.5, 80));
            Check(clock.GetSnapshot(32000000, Origin).DelayMilliseconds < 160, "Noisy high-delay sample filtered");
            Reject(() => clock.Observe(Sample(32)), "reordered sample");
            clock.Configure(false, "localhost");
            var disabled = clock.GetSnapshot(32000000, Origin);
            Check(!disabled.Enabled && !disabled.HasEstimate && !disabled.IsFresh, "Disable discards old estimate");
            clock.Configure(true, "192.0.2.1"); Check(!clock.GetSnapshot(32000000, Origin).HasEstimate, "Server change starts fresh association");
        }
        using (var clock = new NetworkTimeClock(Origin, 0, 1000000)) {
            clock.Configure(true, "localhost");
            for (int i = 0; i < 12; i++) clock.Observe(Sample(i * 16, 25 + i * 16 * 0.01));
            var fitted = clock.GetSnapshot(176000000, Origin.AddSeconds(176));
            Near(fitted.FrequencyPpm, 10, 0.001, "Frequency learned from NTP/QPC slope");
            Check(fitted.Samples == 8, "Filter bounded to eight samples");
            Near((clock.GetSnapshot(177000000, Origin).Utc - Origin.AddSeconds(177)).TotalMilliseconds, 26.77, 0.002, "Learned drift applied between samples");
        }
        using (var clock = new NetworkTimeClock(Origin, 0, 1000000)) {
            clock.Configure(true, "localhost"); clock.Observe(Sample(0)); clock.Observe(Sample(16, 28));
            Check(clock.GetSnapshot(16000000, Origin).UncertaintyMilliseconds >= 6, "Jitter and pending correction included in error estimate");
        }
        using (var clock = new NetworkTimeClock(Origin, 0, 1000000)) {
            clock.Configure(true, "localhost");
            for (int i = 0; i < 8; i++) clock.Observe(Sample(i * 16, 25 + i * 160));
            Check(Math.Abs(clock.GetSnapshot(112000000, Origin).FrequencyPpm) <= 100, "Frequency correction bounded");
        }
    }
    private static void UdpExchange()
    {
        using (var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
        using (var clock = new NetworkTimeClock()) {
            clock.Configure(true, "localhost");
            server.Client.ReceiveTimeout = 3000;
            Exception failure = null;
            var thread = new Thread(() => { try {
                IPEndPoint peer = null; byte[] request = server.Receive(ref peer);
                DateTime receiveUtc = NetworkTimeClock.PreciseUtcNow().AddMilliseconds(100);
                byte[] reply = Reply(request, receiveUtc, NetworkTimeClock.PreciseUtcNow().AddMilliseconds(100));
                server.Send(reply, reply.Length, peer);
            } catch (Exception ex) { failure = ex; } });
            thread.Start();
            var sample = clock.Query((IPEndPoint)server.Client.LocalEndPoint, 1);
            Check(thread.Join(4000) && failure == null, "Loopback UDP server completed");
            Near(sample.OffsetMilliseconds, 100, 10, "Actual UDP exchange offset");
            Check(sample.DelayMilliseconds >= 0 && sample.UncertaintyMilliseconds > 0, "Actual UDP measurement quality");
            clock.Observe(sample); DateTime utc; double uncertainty;
            Check(clock.TryGetUtc(out utc, out uncertainty) && utc.Kind == DateTimeKind.Utc, "Future UTC API requires fresh sample");
            Exception cancelled = null;
            var waiting = new Thread(() => { try { clock.Query((IPEndPoint)server.Client.LocalEndPoint, 1); } catch (Exception ex) { cancelled = ex; } });
            waiting.Start(); IPEndPoint client = null; server.Receive(ref client);
            clock.Configure(false, "localhost");
            Check(waiting.Join(1000) && cancelled != null, "Configuration cancels pending receive");
            Check(!clock.TryGetUtc(out utc, out uncertainty) && utc == DateTime.MinValue && Double.IsPositiveInfinity(uncertainty), "Disabled API cannot return qualified UTC");
            clock.Start(); clock.Start(); var timer = Stopwatch.StartNew(); clock.Dispose();
            Check(timer.ElapsedMilliseconds < 1000, "Disabled worker exits promptly"); clock.Dispose();
            Reject(() => clock.Configure(true, "localhost"), "disposed clock reconfiguration");
        }
    }
    private static void Live(int durationSeconds)
    {
        using (var clock = new NetworkTimeClock()) {
            clock.Configure(true, NetworkTimeClock.DefaultServer, true); clock.Start();
            var timer = Stopwatch.StartNew(); NetworkTimeSnapshot snapshot; DateTime lastSync = DateTime.MinValue;
            do {
                Thread.Sleep(100); snapshot = clock.GetSnapshot();
                if (snapshot.HasEstimate && snapshot.LastSyncUtc != lastSync || !snapshot.HasEstimate && snapshot.Error != null) {
                    Console.WriteLine("LIVE: elapsed_s={0:F1}; status={1}; server={2}; endpoint={3}; offset_ms={4:F3}; delay_ms={5:F3}; estimated_error_ms={6:F3}; frequency_ppm={7:F2}; samples={8}; error={9}; target_status={10}",
                        timer.Elapsed.TotalSeconds, snapshot.Status, snapshot.Server, snapshot.Endpoint, snapshot.OffsetMilliseconds, snapshot.DelayMilliseconds, snapshot.UncertaintyMilliseconds, snapshot.FrequencyPpm, snapshot.Samples, snapshot.Error,snapshot.TargetStatus);
                    lastSync = snapshot.LastSyncUtc;
                }
            } while (timer.Elapsed.TotalSeconds < durationSeconds);
            foreach(string result in snapshot.ServerResults)Console.WriteLine("SOURCE: "+result);
            if (!snapshot.IsFresh) throw new Exception("Live server synchronization unavailable: " + snapshot.Error);
        }
    }
    private static void AutomaticSelection()
    {
        var threshold=new NetworkTimeSnapshot { IsFresh=true,UncertaintyMilliseconds=10 };
        Check(threshold.MeetsAccuracyTarget && threshold.TargetStatus=="within 10 ms target","10 ms target boundary rejected");
        threshold.UncertaintyMilliseconds=10.001;Check(!threshold.MeetsAccuracyTarget && threshold.TargetStatus=="above 10 ms target","Above-target estimate accepted");
        threshold.UncertaintyMilliseconds=0.1;threshold.IsFresh=false;Check(!threshold.MeetsAccuracyTarget,"Stale clock met the target");
        string cloud=NetworkTimeClock.AutomaticServers[0],nist=NetworkTimeClock.AutomaticServers[1],windows=NetworkTimeClock.AutomaticServers[2];
        using(var clock=new NetworkTimeClock(Origin,0,1000000)) {
            clock.Configure(true,cloud,true);
            var initial=clock.GetSnapshot(0,Origin);Check(initial.Automatic && initial.ServerResults.Length==3 && !initial.HasEstimate,"Automatic candidates not initialized");
            clock.ObserveServer(cloud,Sample(0,25,10));clock.ObserveServer(nist,Sample(1,26,2));
            var best=clock.GetSnapshot(1000000,Origin.AddSeconds(1));
            Check(best.Server==nist && best.Samples==1 && best.FrequencyPpm==0,"Lower-uncertainty source not selected independently");
            clock.ObserveServer(windows,Sample(2,26,20));
            Check(clock.GetSnapshot(2000000,Origin).Server==nist,"Higher-error source replaced the better one");
            DateTime before=clock.GetSnapshot(16000000,Origin).Utc;
            clock.ObserveServer(cloud,Sample(16,25,1));
            var switched=clock.GetSnapshot(16000000,Origin);
            Check(switched.Server==cloud && switched.Utc==before && switched.FrequencyPpm==0 && switched.Samples==1,"Source switch stepped the running clock or mixed drift histories");
            clock.RecordServerFailure(cloud,new TimeoutException("fixture timeout"));clock.ObserveServer(nist,Sample(17,26,2));
            Check(clock.GetSnapshot(17000000,Origin).Server==nist,"Failed source blocked healthy fallback");
            clock.RecordServerFailure(nist,new NtpServerException("DENY"));clock.ObserveServer(windows,Sample(18,26,20));
            var fallback=clock.GetSnapshot(18000000,Origin);
            Check(fallback.Server==windows && fallback.IsFresh && fallback.ServerResults[1].Contains("paused"),"One denied source paused all candidates");
            clock.RecordServerFailure(windows,new NtpServerException("RATE"));
            var sources=(Array)typeof(NetworkTimeClock).GetField("sources",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(clock);
            var source=sources.GetValue(2);var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            Check((int)source.GetType().GetField("PollSeconds",flags).GetValue(source)==32,"RATE did not back off this source");
            clock.ObserveServer(windows,Sample(50,26,20));
            Check((int)source.GetType().GetField("PollSeconds",flags).GetValue(source)==32,"Successful reply discarded RATE minimum interval");
            clock.RecordServerFailure(cloud,new NtpServerException("DENY"));clock.RecordServerFailure(windows,new NtpServerException("RSTR"));
            Check(!clock.GetSnapshot(50000000,Origin).IsFresh,"All paused sources still qualified time");
            clock.Configure(true,"localhost");
            var manual=clock.GetSnapshot(51000000,Origin);Check(!manual.Automatic && manual.ServerResults.Length==1 && manual.ConfiguredServer=="localhost" && !manual.HasEstimate,"Manual override did not reset automatic state");
        }
        using(var clock=new NetworkTimeClock(Origin,0,1000000)) {
            clock.Configure(true,cloud,true);clock.ObserveServer(cloud,Sample(0,25,1));clock.ObserveServer(nist,Sample(1,25,0.95));
            Check(clock.GetSnapshot(1000000,Origin).Server==cloud,"Nearly equal scores caused needless source flapping");
            clock.ObserveServer(nist,Sample(2,25,0.5));Check(clock.GetSnapshot(2000000,Origin).Server==nist,"Materially better source not selected");
            clock.ObserveServer(windows,Sample(130,25,5));Check(clock.GetSnapshot(130000000,Origin).Server==windows,"Stale low-error source blocked a fresh source");
        }
        var eastern=TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var summer=NetworkTimeSnapshot.LocalTime(new DateTime(2026,7,1,12,0,0,DateTimeKind.Utc),eastern);
        var winter=NetworkTimeSnapshot.LocalTime(new DateTime(2026,1,1,12,0,0,DateTimeKind.Utc),eastern);
        Check(summer.Hour==8 && summer.Offset==TimeSpan.FromHours(-4) && winter.Hour==7 && winter.Offset==TimeSpan.FromHours(-5),"Local display ignored seasonal UTC offsets");
        var first=NetworkTimeSnapshot.LocalTime(new DateTime(2026,11,1,5,30,0,DateTimeKind.Utc),eastern);
        var second=NetworkTimeSnapshot.LocalTime(new DateTime(2026,11,1,6,30,0,DateTimeKind.Utc),eastern);
        Check(first.Hour==second.Hour && first.Offset!=second.Offset && second.UtcDateTime-first.UtcDateTime==TimeSpan.FromHours(1),"Repeated local hour lost its UTC offset or changed the timestamp");
    }
    private static int Main(string[] args)
    {
        try { if (args.Length > 0 && args[0] == "--live") Live(args.Length > 1 ? Int32.Parse(args[1]) : 40); else { Protocol(); Discipline(); UdpExchange(); AutomaticSelection(); Console.WriteLine("PASS: " + checks + " network-time checks"); } return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
