param(
    [string]$Compiler,
    [string]$OutputDirectory = "$PSScriptRoot\_artifacts\np1-lfp-phase",
    [string]$SdDecoderSource,
    [string]$ConsoleExecutable,
    [string]$AuditedExport
)
$ErrorActionPreference='Stop'
if(!$Compiler) {
    $locator=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $installation=& $locator -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath
    $Compiler=Join-Path $installation 'MSBuild\Current\Bin\Roslyn\csc.exe'
}
if(!(Test-Path -LiteralPath $Compiler)){throw 'Roslyn C# compiler is required'}
$console=Join-Path $PSScriptRoot '..\USB512_interface\USB512_interface'
if(!$SdDecoderSource){$SdDecoderSource=Join-Path $console 'NpLogger\NpCe64Decoder.cs'}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$exe=Join-Path $OutputDirectory 'Np1LfpPhaseTests.exe'
& $Compiler /nologo /warnaserror /optimize+ /target:exe "/out:$exe" "$PSScriptRoot\Tests\TestNp1LfpPhase.cs" "$console\XeNpStreamDecoder.cs" $SdDecoderSource
if($LASTEXITCODE){throw 'Actual NP1 phase test build failed'}
if($ConsoleExecutable -or $AuditedExport) {
    if(!$ConsoleExecutable -or !$AuditedExport){throw 'Both actual console executable and audited export folder are required'}
    & $exe ([IO.Path]::GetFullPath($ConsoleExecutable)) ([IO.Path]::GetFullPath($AuditedExport))
} else { & $exe }
if($LASTEXITCODE){throw 'Actual NP1 phase tests failed'}
'PASS offline production NP1 phase tests; no hardware access'
