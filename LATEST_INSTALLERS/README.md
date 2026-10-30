# WILD-X1.0.0.204

[Download the installer](WILDX_console-Setup-1.0.0.204.exe).

This update includes the checked application and new V1 firmware (APPFFF283D9 / USB1E73A743). V1 two-probe20kHz USB60 and BLE-to-SD30 tests passed with saved1250Hz scheduled IMU copies and no reported midstream NP errors. Local RTC-to-first-NP interval widths were112us/111us; PC calibration uncertainty is separate. One initial missing IMU slot is explicit, and physical sensor conversion phase and SD terminal coverage remain unqualified.

V2 retains the checked1.0.0.199 firmware (APP194D9B94 / USB74A9B98B / recovery6408DBB8). Its new deterministic SD scheduler is still being corrected and is excluded from this installer. V1Camera assets are unchanged.

Firmware update uses the shared Advanced window. Select the exact device and matching bundled family, choose Full bundle: APP + USB + boot, then Update bundled firmware. Detailed scope and source records are in ReleaseSources/1.0.0.204. Earlier installers remain under legacy; this root contains one current installer and no separate firmware update ZIP.