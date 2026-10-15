param([string]$Compiler,[string]$OutputDirectory="$PSScriptRoot\_artifacts\start-ready")
$ErrorActionPreference='Stop'
if(!$Compiler){$locator=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe';$installation=& $locator -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath;$Compiler=Join-Path $installation 'MSBuild\Current\Bin\Roslyn\csc.exe'}
$console=Join-Path $PSScriptRoot '..\USB512_interface\USB512_interface'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$exe=Join-Path $OutputDirectory 'NpStartReadyTests.exe'
$sources=@("$PSScriptRoot\Tests\TestNpStartReady.cs","$console\XeNpWinUsb.cs","$console\XeNpStreamDecoder.cs","$console\NpCaptureModes.cs","$console\NpProbeManifest.cs","$PSScriptRoot\V1Capture\Configuration\NpConfigClient.cs","$console\USB512WinUsb.cs", "$console\UsbHealth.cs", "$console\UsbHealthNative.cs","$console\XeNpSdInfo.cs","$console\NpLogger\NpV1Management.cs","$console\NpLogger\NpDownloadNative.cs","$console\NpLogger\NpDownloadStream.cs","$console\NpLogger\RawBurst.cs")
& $Compiler /nologo /optimize+ /target:exe /r:System.Drawing.dll /r:System.Windows.Forms.dll "/out:$exe" @sources
if($LASTEXITCODE){throw 'START READY tests did not compile'}
& $exe ([IO.Path]::GetFullPath($OutputDirectory)) ([IO.Path]::GetFullPath($console))
if($LASTEXITCODE){throw 'START READY tests failed'}
