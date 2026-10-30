param([Parameter(Mandatory=$true)][string]$App,[string]$OutputDirectory='C:/Frank/Code/USB_PCconsole/_artifacts/sd_usb_test_ui215/render215')
$ErrorActionPreference='Stop'
$taskRoot='D:/Code/WILDX_console/WILDX_console'
$taskSource=Join-Path $taskRoot 'Tests/NpSdUsbTestUiTests.cs'
$taskBefore=(Get-FileHash -LiteralPath $taskSource -Algorithm SHA256).Hash
$taskAppBefore=(Get-FileHash -LiteralPath $App -Algorithm SHA256).Hash
$taskCompiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
$taskExe=Join-Path $PSScriptRoot 'NpSdUsbTestUiTests.exe'
& $taskCompiler /nologo /warnaserror /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Configuration.dll /r:System.Web.Extensions.dll "/out:$taskExe" $taskSource
if($LASTEXITCODE){throw 'Actual-app UI fixture failed to compile'}
& $taskExe $App $OutputDirectory
$taskExit=$LASTEXITCODE
if((Get-FileHash -LiteralPath $taskSource -Algorithm SHA256).Hash -cne $taskBefore -or (Get-FileHash -LiteralPath $App -Algorithm SHA256).Hash -cne $taskAppBefore){throw 'Source/app identity changed during UI check'}
Write-Output ('UI fixture exit '+$taskExit+'; output '+$OutputDirectory)
exit $taskExit
