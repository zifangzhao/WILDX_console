param([string]$ManagementFixtureDirectory=(Join-Path $PSScriptRoot 'Neuropixels\_artifacts\inplace-update-tests'),[string]$OutputDirectory=(Join-Path $PSScriptRoot '_artifacts\v1-start-transition-tests'),[string]$Compiler='C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\Roslyn\csc.exe')
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force|Out-Null
$manager=Get-Content -LiteralPath "$PSScriptRoot/USB512_interface/USB512_interface/NpLogger/NpV1Management.cs" -Raw
$manager=[regex]::Replace($manager,'\[DllImport\("winusb.dll",EntryPoint="WinUsb_WritePipe",SetLastError=true\)\]\s*\[return:MarshalAs\(UnmanagedType.Bool\)\]\s*private static extern bool WritePipe\(IntPtr h,byte pipe,byte\[\] b,uint count,out uint sent,IntPtr overlapped\);','private static bool WritePipe(IntPtr h,byte pipe,byte[] b,uint count,out uint sent,IntPtr overlapped){return NpDownloadNative.WritePipe(h,pipe,b,count,out sent,overlapped);}')
if($manager.Contains('extern bool WritePipe')){throw 'Native WritePipe substitution failed'}
$management=Join-Path $OutputDirectory 'NpV1Management.mock.cs'
[IO.File]::WriteAllText($management,$manager,[Text.UTF8Encoding]::new($false))
# Reuse the checked management fake, adding per-handle policy state for reopen.
$source=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Neuropixels\Tests\TestNpInPlaceUpdate.cs') -Raw
$original='internal static bool WinUsbInitialize(SafeFileHandle f,out IntPtr usb){usb=new IntPtr(456);return true;}'
if($source.Split(@($original),[StringSplitOptions]::None).Length -ne 2){throw 'Expected one management fake initializer.'}
$replacement='internal static bool WinUsbInitialize(SafeFileHandle f,out IntPtr usb){PowerCalls=StatusReads=InTimeout=OutTimeout=0;usb=new IntPtr(456);return true;}'
$fake=Join-Path $OutputDirectory 'NativeReopen.mock.cs'
[IO.File]::WriteAllText($fake,$source.Replace($original,$replacement),[Text.UTF8Encoding]::new($false))
$exe=Join-Path $OutputDirectory 'NpV1StartTransitionTests.exe'
& $compiler /nologo /target:exe /main:NpV1StartTransitionTests "/out:$exe" $management $fake (Join-Path $PSScriptRoot 'Tests\NpV1StartTransitionTests.cs')
if($LASTEXITCODE){throw 'V1 Start-transition fixture compilation failed.'}
& $exe
if($LASTEXITCODE){throw 'V1 Start-transition checks failed.'}
