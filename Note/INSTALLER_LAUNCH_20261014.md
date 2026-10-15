# Run after installation — WILDX 1.0.0.29

The installer finish page offers **Run WILD-X**, checked by default. Clicking
Finish launches the installed `$INSTDIR\WILDX_console.exe` when the box is
checked; users can uncheck it to close the installer without launching.

This uses the native NSIS Modern UI finish-page option. The expanded installer
code was checked for the checkbox label, default state, checked-state condition
and quoted executable path. NSIS compilation succeeded and independent
extraction verified all 13 runtime files. The installer was not executed.

The GUI was rebuilt from the checked release 28 snapshot with only its assembly
version changed. All 80 other source documents and the 12 runtime dependency
files retain their checked identities. Release 28 application validation carries
forward; no application workflow or firmware changes were added.

Private verification: `_artifacts/installer_launch_20261014/`.
