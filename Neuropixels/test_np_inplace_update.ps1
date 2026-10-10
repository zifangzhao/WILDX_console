param([string]$OutputDirectory="$PSScriptRoot\_artifacts\inplace-update-tests",[string]$Compiler)
$ErrorActionPreference='Stop'
if(-not $Compiler){
    $locator=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $installation=& $locator -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath
    $Compiler=Join-Path $installation 'MSBuild\Current\Bin\Roslyn\csc.exe'
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$source=Get-Content -LiteralPath "$PSScriptRoot\..\USB512_interface\USB512_interface\NpLogger\NpV1Management.cs" -Raw
$native=@'
        [DllImport("winusb.dll",EntryPoint="WinUsb_WritePipe",SetLastError=true)]
        [return:MarshalAs(UnmanagedType.Bool)]
        private static extern bool WritePipe(IntPtr h,byte pipe,byte[] b,uint count,out uint sent,IntPtr overlapped);
'@
$source=$source.Replace("`r`n","`n");$native=$native.Replace("`r`n","`n")
if($source.Split(@($native),[StringSplitOptions]::None).Length -ne 2){throw 'Expected one unchanged native WritePipe declaration'}
$mock='        private static bool WritePipe(IntPtr h,byte pipe,byte[] b,uint count,out uint sent,IntPtr overlapped){return NpDownloadNative.WritePipe(h,pipe,b,count,out sent,overlapped);}'
$manager=Join-Path $OutputDirectory 'NpV1Management.mock.cs'
[IO.File]::WriteAllText($manager,$source.Replace($native,$mock),[Text.UTF8Encoding]::new($false))
$exe=Join-Path $OutputDirectory 'TestNpInPlaceUpdate.exe'
& $compiler /nologo /warnaserror /optimize+ "/out:$exe" $manager "$PSScriptRoot\Tests\TestNpInPlaceUpdate.cs"
if($LASTEXITCODE){throw 'Offline APP update test compilation failed'}
& $exe ([IO.Path]::GetFullPath($OutputDirectory))
if($LASTEXITCODE){throw 'Offline APP update checks failed'}
