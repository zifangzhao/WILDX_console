# V2 manual Start GUI fix — 2026-10-19

V2 manual Start is available on a usable connection when no host command is
running. The GUI no longer blocks it using PC-clock synchronization or cached
recorder readiness. Profile selection still requires its device confirmation;
the handler sends one immediate continuous START (blocks0/UTC0). Firmware
accepts or rejects it, and its operation/error/fault is shown. There is no
automatic recovery, overwrite, reset or retry. V1 behavior is unchanged.

The checked GUI Compile plus one version-only Compile to1.0.0.45 and the owner's focused actual-app/handler checks were
used; see the input ledger and regression log. No device was accessed for this
GUI fix. Firmware and all other runtime files are byte-identical to ca55be1:
V1USB0E542BBD/APP654824DE, V2APP136172A5 and V1Camera remain unchanged. The private
V2 BLE-catalog firmware candidate is excluded. Existing firmware evidence and
its limitations remain historical and are not new device qualification here.

Validation: 23 actual-app settings checks and 75 actual-source button/Apply/Start checks passed with simulated transport. The initial profile fixture lacked an unused V1 binding dependency; its isolated repair did not change the app. A version-only Compile then changed only AssemblyInfo to1.0.0.45; functional checks were reused. The normal build entry point now increments the version before compilation. No new firmware or hardware testing.
