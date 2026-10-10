param([string]$Compiler,[string]$OutputDirectory="$PSScriptRoot\_artifacts\queued-reader-tests",[Parameter(Mandatory=$true)][string]$ConsoleExecutable)
$ErrorActionPreference='Stop'
if(!$Compiler){$locator=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe';$installation=& $locator -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath;$Compiler=Join-Path $installation 'MSBuild\Current\Bin\Roslyn\csc.exe'}
$console=Join-Path $PSScriptRoot '..\USB512_interface\USB512_interface'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$reader=[IO.File]::ReadAllText((Join-Path $console 'USB512WinUsb.cs'))
$begin=$reader.IndexOf('    internal sealed class USB512WinUsbQueuedReader');$end=$reader.IndexOf('    internal static class USB512WinUsbNative',$begin)
if($begin -lt 0 -or $end -le $begin){throw 'Actual queued reader source boundaries missing'}
$actual=Join-Path $OutputDirectory 'ActualQueuedReader.cs'
[IO.File]::WriteAllText($actual,"using System; using System.IO; using System.ComponentModel; using System.Runtime.InteropServices; namespace USB512_interface {`n"+$reader.Substring($begin,$end-$begin)+"`n}",[Text.UTF8Encoding]::new($false))
$device=[IO.File]::ReadAllText((Join-Path $console 'XeNpWinUsb.cs'))
$begin=$device.IndexOf('        internal void Stop()');$end=$device.IndexOf('        internal int Read(',$begin)
if($begin -lt 0 -or $end -le $begin){throw 'Actual NP STOP method source boundaries missing'}
$actualStop=Join-Path $OutputDirectory 'ActualNpStop.cs'
[IO.File]::WriteAllText($actualStop,"using System; using System.IO; using System.ComponentModel; using System.Runtime.InteropServices; namespace USB512_interface {partial class XeNpWinUsbDevice {`n"+$device.Substring($begin,$end-$begin)+"`n}}",[Text.UTF8Encoding]::new($false))
$exe=Join-Path $OutputDirectory 'NpQueuedReaderTests.exe'
& $Compiler /nologo /optimize+ /target:exe "/out:$exe" $actual $actualStop "$PSScriptRoot\Tests\TestNpQueuedReader.cs"
if($LASTEXITCODE){throw 'Queued reader regression did not compile'}
& $exe ([IO.Path]::GetFullPath($ConsoleExecutable))
if($LASTEXITCODE){throw 'Queued reader regression failed'}
