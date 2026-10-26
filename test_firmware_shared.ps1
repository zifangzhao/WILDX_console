param([Parameter(Mandatory=$true)][string]$ConsoleExecutable,[string]$OutputDirectory="$PSScriptRoot/_artifacts/firmware_window_checks")
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$compiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
foreach($name in 'NpFirmwareSharedWindowTests','NpV2BundledUpdateTests'){
 $test=Join-Path $OutputDirectory ($name+'.exe')
 & $compiler /nologo /warnaserror /r:System.Windows.Forms.dll /r:System.Drawing.dll "/out:$test" "$PSScriptRoot/Tests/$name.cs"
 if($LASTEXITCODE){throw "$name did not compile"}
 & $test ([IO.Path]::GetFullPath($ConsoleExecutable)) (Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) $name)
 if($LASTEXITCODE){throw "$name failed"}
}
