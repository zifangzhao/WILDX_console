param(
    [string]$ConsoleExecutable = "$PSScriptRoot\USB512_interface\USB512_interface\bin\Release\WILDX_console.exe",
    [string]$Compiler,
    [string]$OutputDirectory = "$PSScriptRoot\_artifacts\adc-calibration-tests"
)
$ErrorActionPreference = 'Stop'
if (-not $Compiler) {
    $locator = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $installation = & $locator -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath
    $Compiler = Join-Path $installation 'MSBuild\Current\Bin\Roslyn\csc.exe'
}
$ConsoleExecutable = [IO.Path]::GetFullPath($ConsoleExecutable)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$library = Join-Path (Split-Path -Parent $ConsoleExecutable) 'NpV2SdDatExport.dll'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$fixtureDirectory = Join-Path $OutputDirectory 'fixtures'
& (Join-Path $PSScriptRoot 'Neuropixels\test_sd_dat.ps1') -Compiler $Compiler -OutputDirectory $fixtureDirectory -CompileOnly
$localLibrary = Join-Path $OutputDirectory 'NpV2SdDatExport.dll'
if ($library -ne $localLibrary) { Copy-Item -LiteralPath $library -Destination $localLibrary -Force }
$test = Join-Path $OutputDirectory 'NpAdcCalibrationTests.exe'
& $Compiler /nologo /optimize+ /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll "/r:$localLibrary" "/out:$test" (Join-Path $PSScriptRoot 'Tests\NpAdcCalibrationTests.cs')
if ($LASTEXITCODE) { throw 'Factory calibration checks did not compile.' }
& $test $ConsoleExecutable (Join-Path $fixtureDirectory 'NpV2SdDatExportTests.exe') $OutputDirectory
if ($LASTEXITCODE) { throw 'Factory calibration checks failed.' }
