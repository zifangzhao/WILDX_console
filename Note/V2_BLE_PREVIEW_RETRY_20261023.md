# V2 BLE preview setup/recovery — release 1.0.0.105

The reported symptom was Preview automatically unchecking before any waveform
arrived during three-probe V2 SD recording at 20 kHz. The host had three relevant
failure paths: it disabled Preview after four snapshot pressure refusals; a
confirmed unsent race with another BLE control was treated as a generic error;
and control ownership after phase preparation discarded that phase and repeated
calibration instead of completing preview startup.

The preview now preserves the requested checkbox during typed disposable
refusals/no-data timeouts, using 1/2/4/8-second backoff capped at 8 seconds. Fresh
phase and stream request IDs are used after retirement. Same connection epoch,
selection generation, start LBA, SD command generation, mask, rate and healthy
recording state are required. Rolling recovery stops renewing the old ID and
waits at least 2.25 seconds after any in-flight old GATT write retires. Delayed
device dispatch can extend actual lease lifetime; a new request can still be
silently ignored, so silence remains a bounded recovery condition, never proof
of request acceptance. Old fragments are discarded rather than mixed.

A confirmed unsent control race is a distinct host-only reason 129. A completed
valid phase is retained while yielding to controls, or when the rolling request
was confirmed not submitted. No acquisition, SD Start/Stop, configuration, clock,
MTU or firmware operation is retried. Generic transport failures, malformed
metadata, explicit unsupported calibration and cancellation retain terminal
handling. Legacy fallback still requires an explicit compact-kind refusal.

The V2 developer confirmed the exact bundled preview source/object admits
P1/P2 preview while recording mask 7 at 20 kHz; P3 preview is unsupported and is
not enabled by this fix. The existing recording-active flag distinction is
preserved: flag bit 4 is allowed; pending SD command bit 8 blocks admission.
Persistent firmware pressure may leave Preview checked and visibly waiting.

Validation: `test_ble_preview_display.ps1` passed 6,106 actual-source checks,
including mask 7/20 kHz transient-to-waveform recovery, repeated pressure beyond
the old budget, no immediate replay, recording/selection changes, control
priority, old lease-write retirement, prepared-phase handoff, malformed packets,
legacy compatibility, source-tick gaps and existing display behavior. Only the
radio was doubled. No BLE hardware, SD recording or firmware action was taken;
live triple20 delivery is not qualified by these tests.

The final release uses the frozen published96 compiler graph, admitting only
the three preview source changes and the normal version hook. A prior local104
package attempt was superseded to exclude unrelated unpublished recording
changes. The final app and installer are105. Exporter DLL, firmware, helpers
and NSIS recipe are unchanged from96. All40 installer payload files were
extracted and hash-verified. Frozen compiler inputs, checks and package manifest
are in `ReleaseSources/1.0.0.105/`. No installer execution or hardware test was
performed for this release.
