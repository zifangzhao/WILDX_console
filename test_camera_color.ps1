param([string]$OutputDirectory="$PSScriptRoot/_artifacts/camera-color-tests",[string]$Ffmpeg)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$taskSource=Join-Path $PSScriptRoot 'USB512_interface/USB512_interface'
$taskExe=Join-Path $OutputDirectory 'NpCameraColorTests.exe'
& 'C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe' /nologo /optimize+ /r:System.Drawing.dll /r:System.Windows.Forms.dll "/out:$taskExe" "$PSScriptRoot/Tests/NpCameraColorTests.cs" "$taskSource/Ble/WILD_VideoDecoder.cs" "$taskSource/CameraPreviewForm.cs" "$taskSource/WildXTheme.cs"
if($LASTEXITCODE){throw 'Camera color check compilation failed'}
if($Ffmpeg){& $taskExe $OutputDirectory $Ffmpeg}else{& $taskExe $OutputDirectory}
if($LASTEXITCODE){throw 'Camera color checks failed'}
