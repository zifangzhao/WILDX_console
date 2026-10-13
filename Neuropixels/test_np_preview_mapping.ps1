param([string]$Compiler,[string]$OutputDirectory="$PSScriptRoot\_artifacts\preview-mapping",[string]$Configurator)
$ErrorActionPreference='Stop'
if(!$Compiler){
 $locator=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
 $installation=& $locator -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath
 $Compiler=Join-Path $installation 'MSBuild\Current\Bin\Roslyn\csc.exe'
}
$console=Join-Path $PSScriptRoot '..\USB512_interface\USB512_interface'
if(!$Configurator){$Configurator=Join-Path $console 'bin\Release\NPConfigurator\NPConfigurator.exe'}
if(!(Test-Path -LiteralPath $Configurator)){throw 'Pass -Configurator pointing to the existing checked NPConfigurator.exe'}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$exe=Join-Path $OutputDirectory 'NpPreviewMappingTests.exe'
& $Compiler /nologo /optimize+ /target:exe /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll "/out:$exe" "/resource:$console\Resources\Waveform.ico,USB512_interface.Waveform.ico" "$PSScriptRoot\Tests\TestNpPreviewMapping.cs" "$console\NeuropixelsPreviewForm.cs" "$console\XeNpStreamDecoder.cs"
if($LASTEXITCODE){throw 'Preview mapping tests did not compile'}
& $exe ([IO.Path]::GetFullPath($OutputDirectory)) ([IO.Path]::GetFullPath((Join-Path $console 'mainForm.cs'))) ([IO.Path]::GetFullPath($Configurator)) ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'NPConfigurator\catalog.json'))) ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'USBConsoleV2\NpV2UsbConsole.cs')))
if($LASTEXITCODE){throw 'Preview mapping/render tests failed'}
