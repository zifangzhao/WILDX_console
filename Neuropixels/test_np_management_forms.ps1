param([Parameter(Mandatory=$true)][string]$ConsoleExecutable,[string]$OutputDirectory="$PSScriptRoot\_artifacts\management-forms-tests",[string]$Compiler)
$ErrorActionPreference='Stop'
if(-not $Compiler){
    $locator=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $installation=& $locator -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath
    $Compiler=Join-Path $installation 'MSBuild\Current\Bin\Roslyn\csc.exe'
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$exe=Join-Path $OutputDirectory 'TestNpManagementForms.exe'
& $compiler /nologo /warnaserror /optimize+ /r:System.Drawing.dll /r:System.Windows.Forms.dll "/out:$exe" "$PSScriptRoot\Tests\TestNpManagementForms.cs"
if($LASTEXITCODE){throw 'Offline management forms test compilation failed'}
& $exe ([IO.Path]::GetFullPath($ConsoleExecutable)) ([IO.Path]::GetFullPath($OutputDirectory))
if($LASTEXITCODE){throw 'Offline management forms checks failed'}
