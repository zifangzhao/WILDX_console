param([string]$ManagementFixtureDirectory=(Join-Path $PSScriptRoot 'Neuropixels\_artifacts\inplace-update-tests'),[string]$OutputDirectory=(Join-Path $PSScriptRoot '_artifacts\v1-start-transition-tests'),[string]$Compiler='C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\Roslyn\csc.exe')
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force|Out-Null
# Reuse the checked management fake, adding per-handle policy state for reopen.
$source=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Neuropixels\Tests\TestNpInPlaceUpdate.cs') -Raw
$original='internal static bool WinUsbInitialize(SafeFileHandle f,out IntPtr usb){usb=new IntPtr(456);return true;}'
if($source.Split(@($original),[StringSplitOptions]::None).Length -ne 2){throw 'Expected one management fake initializer.'}
$replacement='internal static bool WinUsbInitialize(SafeFileHandle f,out IntPtr usb){PowerCalls=StatusReads=InTimeout=OutTimeout=0;usb=new IntPtr(456);return true;}'
$fake=Join-Path $OutputDirectory 'NativeReopen.mock.cs'
[IO.File]::WriteAllText($fake,$source.Replace($original,$replacement),[Text.UTF8Encoding]::new($false))
$exe=Join-Path $OutputDirectory 'NpV1StartTransitionTests.exe'
& $compiler /nologo /target:exe /main:NpV1StartTransitionTests "/out:$exe" (Join-Path $ManagementFixtureDirectory 'NpV1Management.mock.cs') $fake (Join-Path $PSScriptRoot 'Tests\NpV1StartTransitionTests.cs')
if($LASTEXITCODE){throw 'V1 Start-transition fixture compilation failed.'}
& $exe
if($LASTEXITCODE){throw 'V1 Start-transition checks failed.'}
