param([string]$Compiler,[string]$OutputDirectory="$PSScriptRoot\_artifacts\camera-tests")
$ErrorActionPreference='Stop'
if(!$Compiler){$locator=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe';$installation=& $locator -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath;$Compiler=Join-Path $installation 'MSBuild\Current\Bin\Roslyn\csc.exe'}
$console=Join-Path $PSScriptRoot '..\USB512_interface\USB512_interface'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$exe=Join-Path $OutputDirectory 'NpCameraTests.exe'
$sources=@("$PSScriptRoot\Tests\TestNpCamera.cs","$console\XeNpWinUsb.cs","$console\XeNpStreamDecoder.cs","$console\NpCaptureModes.cs","$console\NpProbeManifest.cs","$PSScriptRoot\V1Capture\Configuration\NpConfigClient.cs","$console\USB512WinUsb.cs", "$console\UsbHealth.cs", "$console\UsbHealthNative.cs","$console\XeNpSdInfo.cs","$console\NpLogger\NpV1Management.cs","$console\NpLogger\NpDownloadNative.cs","$console\NpLogger\NpDownloadStream.cs","$console\NpLogger\RawBurst.cs")
$sources+="$console\CameraPreviewForm.cs"
$sources+="$console\WildXTheme.cs"
& $Compiler /nologo /optimize+ /target:exe /r:System.Drawing.dll /r:System.Windows.Forms.dll "/out:$exe" @sources
if($LASTEXITCODE){throw 'NP camera tests did not compile'}
& $exe (Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) ('run-'+[Guid]::NewGuid().ToString('N')))
if($LASTEXITCODE){throw 'NP camera tests failed'}
