param([string]$Compiler,[string]$OutputDirectory="$PSScriptRoot\_artifacts\neuroscope-xml")
$ErrorActionPreference='Stop'
if(!$Compiler){$locator=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe';$installation=& $locator -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath;$Compiler=Join-Path $installation 'MSBuild\Current\Bin\Roslyn\csc.exe'}
$console=Join-Path $PSScriptRoot '..\USB512_interface\USB512_interface'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$exe=Join-Path $OutputDirectory 'NpNeuroscopeXmlTests.exe'
& $Compiler /nologo /optimize+ /target:exe /r:System.Web.Extensions.dll /r:System.Xml.Linq.dll "/out:$exe" "$PSScriptRoot\Tests\TestNpNeuroscopeXml.cs" "$PSScriptRoot\SD\NpNeuroscopeXml.cs" "$PSScriptRoot\SD\NpMetadataFile.cs" "$console\NpRecordingSettings.cs","$console\NpProbeIdentity.cs" "$console\NpProbeManifest.cs" "$PSScriptRoot\V1Capture\Configuration\NpConfigClient.cs"
if($LASTEXITCODE){throw 'Neuroscope XML tests did not compile'}
& $exe ([IO.Path]::GetFullPath((Join-Path $OutputDirectory ('run-'+[Guid]::NewGuid().ToString('N')))))
if($LASTEXITCODE){throw 'Neuroscope XML tests failed'}
