param(
    [string]$Compiler,
    [string]$OutputDirectory = "$PSScriptRoot\_artifacts\wire-tests"
)
$ErrorActionPreference = 'Stop'
if (-not $Compiler) {
    $locator = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $locator)) { throw 'Pass -Compiler with the full path to Roslyn csc.exe.' }
    $installation = & $locator -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath
    $Compiler = Join-Path $installation 'MSBuild\Current\Bin\Roslyn\csc.exe'
}
if (-not (Test-Path -LiteralPath $Compiler)) { throw "C# compiler missing: $Compiler" }
$console = Join-Path $PSScriptRoot '..\USB512_interface\USB512_interface'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$exe = Join-Path $OutputDirectory 'UsbConsoleTests.exe'
$sources = @(
    "$PSScriptRoot\Tests\test_usb_console_v2.cs",
    "$console\XeNpWinUsb.cs", "$console\XeNpStreamDecoder.cs",
    "$console\NpCaptureModes.cs", "$console\NpProbeManifest.cs",
    "$PSScriptRoot\V1Capture\Configuration\NpConfigClient.cs",
    "$console\USB512WinUsb.cs", "$console\XeNpSdInfo.cs"
    "$console\NpLogger\NpV1Management.cs", "$console\NpLogger\NpDownloadNative.cs",
    "$console\NpLogger\NpDownloadStream.cs", "$console\NpLogger\RawBurst.cs"
)
& $Compiler /nologo /optimize+ /target:exe /r:System.Drawing.dll /r:System.Windows.Forms.dll "/out:$exe" @sources
if ($LASTEXITCODE) { throw 'NP1 test compilation failed.' }
& $exe
if ($LASTEXITCODE) { throw 'NP1 compatibility tests failed.' }
$exe = Join-Path $OutputDirectory 'Np1ResetPhaseTests.exe'
& $Compiler /nologo /optimize+ /target:exe "/out:$exe" "$PSScriptRoot\Tests\TestNp1ResetPhase.cs" "$console\XeNpStreamDecoder.cs"
if ($LASTEXITCODE) { throw 'NP1 reset/LFP phase test compilation failed.' }
& $exe
if ($LASTEXITCODE) { throw 'NP1 reset/LFP phase tests failed.' }
$exe = Join-Path $OutputDirectory 'Np1StartupTests.exe'
& $Compiler /nologo /optimize+ /target:exe "/out:$exe" "$PSScriptRoot\Tests\TestNp1Startup.cs" "$console\XeNpStreamDecoder.cs" "$console\NpLogger\NpCe64Decoder.cs"
if ($LASTEXITCODE) { throw 'NP1 startup qualification test compilation failed.' }
& $exe
if ($LASTEXITCODE) { throw 'NP1 live/SD startup qualification tests failed.' }
$exe = Join-Path $OutputDirectory 'NpV2UsbConsoleTests.exe'
$sources = @(
    "$PSScriptRoot\Tests\test_np_v2_usb_console.cs",
    "$PSScriptRoot\USBConsoleV2\NpV2UsbConsole.cs",
    "$console\Np2ConfigurationClient.cs",
    "$console\USB512WinUsb.cs", "$console\NeuropixelsPreviewForm.cs",
    "$console\XeNpStreamDecoder.cs"
)
& $Compiler /nologo /optimize+ /define:NP2_CONSOLE_STANDALONE /target:exe /r:System.Drawing.dll /r:System.Windows.Forms.dll "/out:$exe" @sources
if ($LASTEXITCODE) { throw 'NP2 test compilation failed.' }
& $exe
if ($LASTEXITCODE) { throw 'NP2 wire/status tests failed.' }
