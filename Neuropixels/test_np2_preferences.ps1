param(
    [string]$Compiler,
    [string]$OutputDirectory = "$PSScriptRoot\_artifacts\np2-preferences"
)
$ErrorActionPreference = 'Stop'
if (-not $Compiler) {
    $locator = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $locator)) { throw 'Pass -Compiler with the full path to Roslyn csc.exe.' }
    $installation = & $locator -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath
    $Compiler = Join-Path $installation 'MSBuild\Current\Bin\Roslyn\csc.exe'
}
if (-not (Test-Path -LiteralPath $Compiler)) { throw "C# compiler missing: $Compiler" }
$console = Join-Path $PSScriptRoot '..\USB512_interface\USB512_interface'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$exe = Join-Path $OutputDirectory 'TestNp2ConfigurationPreferences.exe'
& $Compiler /nologo /optimize+ /target:exe /r:System.Web.Extensions.dll "/out:$exe" "$console\Np2ConfigurationPreferences.cs" "$PSScriptRoot\Tests\TestNp2ConfigurationPreferences.cs"
if ($LASTEXITCODE) { throw 'NP2 preference test compilation failed.' }
& $exe
if ($LASTEXITCODE) { throw 'NP2 preference tests failed.' }
