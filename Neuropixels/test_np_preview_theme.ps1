param([string]$Compiler,[string]$OutputDirectory="$PSScriptRoot\_artifacts\preview-theme")
$ErrorActionPreference='Stop'
if(!$Compiler){$Compiler='C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\Roslyn\csc.exe'}
$console=Join-Path $PSScriptRoot '..\USB512_interface\USB512_interface'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$exe=Join-Path $OutputDirectory 'NpPreviewThemeTests.exe'
& $Compiler /nologo /optimize+ /target:exe /r:System.Drawing.dll /r:System.Windows.Forms.dll "/out:$exe" "/resource:$console\Resources\Waveform.ico,USB512_interface.Waveform.ico" "$PSScriptRoot\Tests\TestNpPreviewTheme.cs" "$console\NeuropixelsPreviewForm.cs" "$console\XeNpStreamDecoder.cs"
if($LASTEXITCODE){throw 'Preview theme tests did not compile'}
& $exe ([IO.Path]::GetFullPath($OutputDirectory))
if($LASTEXITCODE){throw 'Preview theme contrast/render checks failed'}
