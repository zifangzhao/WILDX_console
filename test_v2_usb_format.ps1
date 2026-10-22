param([Parameter(Mandatory=$true)][string]$ConsoleExecutable,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force|Out-Null
$compiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
foreach($name in @('NpDownloadSourceTests','NpV2UsbFormatTests')){
    $test=Join-Path $OutputDirectory "$name.exe"
    & $compiler /nologo /warnaserror /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll "/out:$test" "$PSScriptRoot/Tests/$name.cs"
    if($LASTEXITCODE){throw "$name did not compile"}
    & $test ([IO.Path]::GetFullPath($ConsoleExecutable))
    if($LASTEXITCODE){throw "$name failed"}
}
$firmwareTest=Join-Path $OutputDirectory 'NpV2FormatFirmwareTests.exe'
& $compiler /nologo /warnaserror /r:System.Core.dll "/out:$firmwareTest" "$PSScriptRoot/Tests/NpV2FormatFirmwareTests.cs"
if($LASTEXITCODE){throw 'Firmware preservation checks did not compile'}
& $firmwareTest ([IO.Path]::GetFullPath($ConsoleExecutable)) "$PSScriptRoot/Firmware/legacy/V2/20261022_before_USB_FORMAT/V2_fused_RECOVERY_6408DBB8_USB_E323A06A_APP_660A6DA4_DUAL30_20261021.hex" "$PSScriptRoot/Firmware/V2/V2_fused_RECOVERY_6408DBB8_USB_B6099A0E_APP_660A6DA4_GUARDED_FORMAT_20261022.hex" "$PSScriptRoot/Firmware/V2/V2_USB_update_B6099A0E_GUARDED_FORMAT_20261022.xenp"
if($LASTEXITCODE){throw 'Firmware preservation checks failed'}
