param([Parameter(Mandatory=$true)][string]$ConsoleExecutable,[string]$OutputDirectory="$PSScriptRoot/_artifacts/v2-bundled-update-tests")
$ErrorActionPreference='Stop';New-Item -ItemType Directory -Path $OutputDirectory -Force|Out-Null
$compiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
& $compiler /nologo /warnaserror /nowarn:0649 /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll "/out:$OutputDirectory/NpFirmwareMismatchTests.exe" "$PSScriptRoot/Tests/NpFirmwareMismatchTests.cs" "$PSScriptRoot/USB512_interface/USB512_interface/NpLogger/NpFirmwareCatalog.cs"
if($LASTEXITCODE){throw 'Mismatch checks compilation failed'}
& "$OutputDirectory/NpFirmwareMismatchTests.exe" (Join-Path (Split-Path ([IO.Path]::GetFullPath($ConsoleExecutable))) 'Firmware')
if($LASTEXITCODE){throw 'Mismatch checks failed'}
& $compiler /nologo /warnaserror /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll "/out:$OutputDirectory/NpV2BundledUpdateTests.exe" "$PSScriptRoot/Tests/NpV2BundledUpdateTests.cs"
if($LASTEXITCODE){throw 'V2 bundle checks compilation failed'}
& "$OutputDirectory/NpV2BundledUpdateTests.exe" ([IO.Path]::GetFullPath($ConsoleExecutable)) ([IO.Path]::GetFullPath("$OutputDirectory/results"))
if($LASTEXITCODE){throw 'V2 bundle checks failed'}
