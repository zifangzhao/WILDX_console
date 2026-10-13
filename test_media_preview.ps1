param([string]$Compiler,[string]$ConsoleExecutable="$PSScriptRoot\USB512_interface\USB512_interface\bin\Release\WILDX_console.exe",[string]$OutputDirectory="$PSScriptRoot\_artifacts\media-preview-tests")
$ErrorActionPreference='Stop'
if(!$Compiler){$locator=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe';$installation=& $locator -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath;$Compiler=Join-Path $installation 'MSBuild\Current\Bin\Roslyn\csc.exe'}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$exe=Join-Path $OutputDirectory 'NpMediaPreviewTests.exe'
& $Compiler /nologo /optimize+ /r:System.Drawing.dll /r:System.Windows.Forms.dll "/out:$exe" "$PSScriptRoot\Tests\NpMediaPreviewTests.cs"
if($LASTEXITCODE){throw 'Media preview fixture did not compile.'}
& $exe ([IO.Path]::GetFullPath($ConsoleExecutable)) ([IO.Path]::GetFullPath($OutputDirectory))
if($LASTEXITCODE){throw 'Media preview fixture failed.'}
