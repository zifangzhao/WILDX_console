param([string]$Compiler,[string]$OutputDirectory="$PSScriptRoot\_artifacts\preview-mapping")
$ErrorActionPreference='Stop'
if(!$Compiler){
 $locator=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
 $installation=& $locator -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath
 $Compiler=Join-Path $installation 'MSBuild\Current\Bin\Roslyn\csc.exe'
}
$console=Join-Path $PSScriptRoot '..\USB512_interface\USB512_interface'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$exe=Join-Path $OutputDirectory 'NpPreviewMappingTests.exe'
& $Compiler /nologo /optimize+ /target:exe /r:System.Drawing.dll /r:System.Windows.Forms.dll "/out:$exe" "/resource:$console\Resources\Waveform.ico,USB512_interface.Waveform.ico" "$PSScriptRoot\Tests\TestNpPreviewMapping.cs" "$console\NeuropixelsPreviewForm.cs" "$console\XeNpStreamDecoder.cs"
if($LASTEXITCODE){throw 'Preview mapping tests did not compile'}
& $exe ([IO.Path]::GetFullPath($OutputDirectory)) ([IO.Path]::GetFullPath((Join-Path $console 'mainForm.cs')))
if($LASTEXITCODE){throw 'Preview mapping/render tests failed'}
