# Secondary network UTC clock

USB_PCconsole 1.0.3.34 starts an independent background SNTP clock by default,
using `time.cloudflare.com` on UDP port 123. The main window shows its quality;
**Time settings…** enables/disables it and selects a hostname or IPv4/IPv6 address.
Preferences persist per user and migrate to subsequent app versions.

This clock does not change Windows time, device time, recording timestamps,
`timestamp.dat`, IMU anchors, or the existing PC/device clock-sync statistics.
It is a separate time source available for a later explicitly reviewed switch.
No administrator rights, additional package, or device connection is required.

The client validates server mode/version, the echoed request timestamp, stratum,
timestamps and synchronization distance. It uses the four NTP timestamps to
remove server processing time and estimate offset/round-trip delay. A fixed
`Stopwatch`/QPC anchor prevents Windows clock steps from affecting measurements.
NTP eras are unfolded near the initial Windows date, including the 2036 rollover;
the initial Windows date must be within 68 years of actual UTC.

One sample is requested every 16 seconds. An eight-sample filter chooses the
lowest estimated error after accounting for age. After at least four samples
spanning 64 seconds, a bounded linear fit estimates clock frequency error.
The initial synchronization sets the alternate clock; subsequent corrections
slew at at most 0.5 ms/second, with frequency adjustment limited to ±100 ppm.
The clock therefore advances continuously within an association. Applying
settings resets the association and invalidates the previous estimate.

The displayed error estimate includes half the selected round-trip delay,
server root delay/dispersion and precision, local timestamp bracketing,
observed sample disagreement, remaining slew correction, and a conservative
100 ppm holdover allowance (0.1 ms per second). This is a quality estimate,
not a measured guarantee of absolute accuracy or protection against a faulty
server. The 1 ms target is marked satisfied only when the estimate is fresh
and the modeled error is at most 1 ms. Public Internet path asymmetry often
prevents this; use a nearby disciplined NTP server when that bound is required.
This is plain NTP, without NTS authentication or a multi-server consensus.

DNS and UDP waits are bounded. Failures retain the clock in holdover with
growing uncertainty and back off polling. After 120 seconds without an accepted
sample, the clock is stale. RATE replies slow polling; DENY/RSTR stop polling
until settings are reapplied. Repeated RATE replies also pause polling if the
interval has already reached 1024 seconds. Leap warnings are rejected because this initial
secondary clock does not implement leap-second transitions. Disabling the
service or closing the app cancels the active socket and stops the worker.

Future code can call `Program.NetworkClock.TryGetUtc(out DateTime utc,
out double uncertaintyMilliseconds)`. It returns false, `DateTime.MinValue`
and infinite uncertainty when disabled, denied, stopped, unsynchronized or stale.
A successful call still requires the caller to assess the reported uncertainty
against its own timing budget. No recording code currently calls this API.

Protocol reference: [RFC 5905](https://www.rfc-editor.org/rfc/rfc5905.html).
Default service: [Cloudflare NTP guide](https://developers.cloudflare.com/time-services/ntp/usage/).

Offline tests compile `Tests/TestNetworkTime.cs` together with
`USB512_interface/USB512_interface/NetworkTimeClock.cs`, using the .NET Framework
C# compiler and `System.dll`, `System.Core.dll`. Run the resulting executable
without arguments. `--live` separately probes the configured default service;
a live result is a connectivity/quality observation, not an absolute accuracy
qualification. Tests cover packet rejection, asymmetric delay, era rollover,
drift, slew continuity, holdover, configuration cancellation and actual loopback UDP.
