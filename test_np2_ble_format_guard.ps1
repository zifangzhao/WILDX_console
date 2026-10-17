param([Parameter(Mandatory=$true)][string]$ConsoleExecutable,[string]$OutputDirectory="$PSScriptRoot/_artifacts/np2-ble-format-guard")
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$source=Get-Content -LiteralPath "$PSScriptRoot/Tests/Np2BleHardware.cs" -Raw
$format=$source.IndexOf('if (action == "format")',[StringComparison]::Ordinal)
$begin=$source.IndexOf('RequireHealthy(lastStatus);',$format,[StringComparison]::Ordinal)
$end=$source.IndexOf('uint capacity=',$begin,[StringComparison]::Ordinal)
if($format -lt 0 -or $begin -lt 0 -or $end -le $begin){throw 'Actual format admission block not found.'}
$guard=$source.Substring($begin,$end-$begin)
$begin=$source.IndexOf('static void RequireHealthy(object status)',[StringComparison]::Ordinal)
$end=$source.IndexOf('static void LogStatus(', $begin,[StringComparison]::Ordinal)
if($begin -lt 0 -or $end -le $begin){throw 'Actual health guard not found.'}
$healthy=$source.Substring($begin,$end-$begin)
$program=@'
using System;
using System.IO;
using System.Reflection;
static class GuardCheck {
 const BindingFlags All=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static Assembly app;static int checks;
 static Type T(string name){return app.GetType(name,true);}
 static object Get(object x,string name){var f=x.GetType().GetField(name,All);return f!=null?f.GetValue(x):x.GetType().GetProperty(name,All).GetValue(x,null);}
 static uint U(object x){return Convert.ToUInt32(x);}
 static object InvokeStatic(Type type,string method,object arg){return type.GetMethod(method,All).Invoke(null,new[]{arg});}
 static void Set(object x,string name,uint value){x.GetType().GetField(name,All).SetValue(x,value);}
 __HEALTHY__
 static void Admit(object lastStatus){__GUARD__}
 static void Check(bool accept,uint state,uint flags,uint capacity=1001390080,uint fault=0,uint error=0){
  var status=Activator.CreateInstance(T("CE32_console.NpSdStatus"),true);
  Set(status,"State",state);Set(status,"Flags",flags);Set(status,"Capacity",capacity);Set(status,"Fault",fault);Set(status,"Error",error);
  bool accepted=true;try{Admit(status);}catch(IOException){accepted=false;}
  checks++;if(accepted!=accept)throw new Exception("Wrong actual HIL format admission: state="+state+" flags="+flags+" capacity="+capacity);
 }
 static int Main(string[] args){try{
  app=Assembly.LoadFrom(Path.GetFullPath(args[0]));const uint ready=16384|1;
  Check(true,0,ready);Check(true,1,ready);Check(true,1,ready|16);
  foreach(uint state in new uint[]{2,3,4,5,6,7,255})Check(false,state,ready);
  foreach(uint flags in new uint[]{0,1,16384,ready|4,ready|8,ready|12})Check(false,1,flags);
  Check(false,1,ready,0);Check(false,1,ready,8192);Check(false,1,ready,1001390080,18);Check(false,1,ready,1001390080,0,2);
  Console.WriteLine("PASS "+checks+" extracted actual HIL format admission checks against production GUI; no radio or card access.");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
'@
$program=$program.Replace('__HEALTHY__',$healthy).Replace('__GUARD__',$guard)
$code=Join-Path $OutputDirectory 'GuardCheck.cs'
$exe=Join-Path $OutputDirectory 'GuardCheck.exe'
[IO.File]::WriteAllText($code,$program)
& 'C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe' /nologo /warnaserror "/out:$exe" $code
if($LASTEXITCODE){throw 'Format guard check did not compile.'}
& $exe ([IO.Path]::GetFullPath($ConsoleExecutable))
if($LASTEXITCODE){throw 'Format guard regression failed.'}
