param([string]$OutputDirectory="$PSScriptRoot\_artifacts\frame-diagnostics-tests")
$ErrorActionPreference='Stop'
$compiler='C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$console=[IO.Path]::GetFullPath("$PSScriptRoot\..\USB512_interface\USB512_interface")
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$sources=@("$console\XeNpStreamDecoder.cs","$console\NpLogger\NpCe64Decoder.cs","$PSScriptRoot\Tests\TestNp1Startup.cs")
foreach($entry in @(@{name='TestNpFrameDiagnostics';source="$PSScriptRoot\Tests\TestNpFrameDiagnostics.cs"},@{name='Np1StartupTests';source=$null})){
    $exe=Join-Path $OutputDirectory ($entry.name+'.exe');$inputs=$sources;if($entry.source){$inputs+=@($entry.source)}
    & $compiler /nologo /warnaserror /optimize+ ('/main:'+$entry.name) ('/out:'+$exe) @inputs
    if($LASTEXITCODE){throw 'Frame diagnostic/startup test compilation failed'}
    & $exe
    if($LASTEXITCODE){throw 'Frame diagnostic/startup checks failed'}
}
