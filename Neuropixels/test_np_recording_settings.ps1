param([string]$Compiler,[string]$OutputDirectory="$PSScriptRoot\_artifacts\recording-settings-tests",[Parameter(Mandatory=$true)][string]$ConfiguratorExecutable)
$ErrorActionPreference='Stop'
if(!$Compiler){$locator=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe';$installation=& $locator -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath;$Compiler=Join-Path $installation 'MSBuild\Current\Bin\Roslyn\csc.exe'}
$console=Join-Path $PSScriptRoot '..\USB512_interface\USB512_interface'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$exe=Join-Path $OutputDirectory 'NpRecordingSettingsTests.exe'
$sdAssembly=Join-Path $console 'bin\Release\NpV2SdDatExport.dll'
Copy-Item -LiteralPath $sdAssembly -Destination $OutputDirectory
& $Compiler /nologo /optimize+ /target:exe /r:System.Web.Extensions.dll "/r:$sdAssembly" "/out:$exe" "$PSScriptRoot\Tests\TestNpRecordingSettings.cs" "$console\NpRecordingSettings.cs" "$console\NpProbeManifest.cs" "$PSScriptRoot\V1Capture\Configuration\NpConfigClient.cs" "$console\NpLogger\NpCe64Export.cs" "$console\NpLogger\NpCe64Decoder.cs" "$console\NpLogger\NpSdGeometry.cs"
if($LASTEXITCODE){throw 'Recording settings regression did not compile'}
& $exe ([IO.Path]::GetFullPath($ConfiguratorExecutable)) (Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) ('run-'+[Guid]::NewGuid().ToString('N')))
if($LASTEXITCODE){throw 'Recording settings regression failed'}
