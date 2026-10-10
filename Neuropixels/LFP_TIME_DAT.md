# NP USB LFP host-arrival timeline

After recording workers drain and DAT files close, each NP1 USB output directory
containing `neuropixels_lfp.dat` receives `time.dat`: one headerless little-endian
`uint32` millisecond-of-local-day value per complete saved LFP row. Individual
multi-probe output uses `probeN/time.dat`; merged output uses root `time.dat`.
Empty LFP files produce empty time files. Failed recordings retain a timeline for
the saved complete-row prefix. Trailing partial DAT-row bytes are reported and
are not changed. AP upsampling does not alter the LFP time spacing.

The anchor is the host UTC read-completion observation carried through both host
queues to the block containing the last successfully accepted LFP row. It is not
the later decoder/foreground processing clock. For merged rows, arrival order
selects the latest read among the matched probe rows; unmatched tails do not move
the merged anchor. Stopped native reads use their completion-harvest observation
time. Windows/USB scheduling and buffering may delay observations: this does not
measure exact wire arrival or hardware sample time.

Rows lie on a uniform derived grid with exact rate `AP Hz / 12`. At 20 kHz AP,
spacing is exactly 0.6 ms, not an integer 1666 Hz approximation. Values are floored
to milliseconds, so sub-ms rows repeat; they wrap modulo 86,400,000. The local
timezone and offset at the anchor are recorded. This grid holds that offset and
does not reconstruct daylight-saving transitions or host clock corrections.

`recording_settings.json` contains `lfp_time_output` receipts with per-output row
count, byte count, UTC/local anchor, logical anchor-row index, timezone, trailing
DAT bytes and semantics. A uniform prefix is derived from that logical anchor
index if the saved file is shorter than the writer's accepted rows. The recording
status text also identifies the format and anchor. No sensor ticks are treated
as PC time, and no hardware synchronization is implied.

Time files are generated after drain, using actual DAT file lengths. They are
first written as `time.dat.partial` and published only after length/flush checks;
existing time files are never replaced. Startup-only probe reconfiguration has
no time file to retire because generation happens only after recording closes.
Historical SD downloads have no host recording-arrival anchor and are unchanged.
NP2 USB produces no LFP DAT and receives no fabricated LFP timeline.
