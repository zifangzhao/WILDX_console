# Installer Finish launch fix

The per-user installer runs with `RequestExecutionLevel user`, while WILD-X's
application manifest requests `requireAdministrator`. The default NSIS MUI2
Finish-page launch uses `Exec` (CreateProcess), which cannot request elevation.
This explains why the checked Run WILD-X option failed after installation.

The Finish page now calls `LaunchWildX`, using `ExecShell "open"` so Windows can
honor the application's elevation manifest. The working directory is the install
folder. The checkbox remains checked by default; unchecking it skips launch.
If shell launch fails or elevation is declined, setup displays a launch failure
message with the Start-menu alternative.

Validation used the actual preprocessed production MUI Finish callback and launch
function in a small silent NSIS harness, with a dummy marker executable:

- Checked: executable launched from an install path containing spaces, with the
  install folder as its working directory.
- Unchecked: executable did not launch.
- Missing executable: handler returned without hanging or starting another app.
- Preprocessing confirmed the checked default and the callback's checkbox guard.

All three execution cases passed. No application compilation, firmware change,
real WILD-X launch, device access, or UAC-consent interaction was performed.
The release owner separately compiles/packages the final installer from the
already checked 1.0.0.48 payload. This note does not claim installer publication.

Private evidence: `_artifacts/installer_finish_launch_20261019/verification.json`
and `check-launch.ps1` in the same directory. Frozen installer-script SHA256:
`CB43535C91EB63740853553E4C76D0E40E66E282EBD66F202973DF767DC738CB`.

References: [NSIS MUI2 Finish-page source](https://github.com/NSIS-Dev/nsis/blob/master/Contrib/Modern%20UI%202/Pages/Finish.nsh),
[Microsoft CreateProcess and UAC behavior](https://learn.microsoft.com/en-us/windows/win32/dxtecharts/user-account-control-for-game-developers).
