$ErrorActionPreference='Stop'
$taskRoot=$PSScriptRoot
$taskRepo=Split-Path (Split-Path $taskRoot -Parent) -Parent
$taskApp=Join-Path $taskRoot 'build137/compile-bin/WILDX_console.exe'
$taskSource=Get-Content -LiteralPath (Join-Path $taskRepo 'Tests/NpDownloadPrefetchTests.cs') -Raw
$taskSource=$taskSource.Replace('using CE32_console;','using System.Reflection;')
$taskSource=$taskSource.Replace('new NpDownloadPrefetchStream(','Create(')
$taskFactory=@'
    static readonly Assembly app=Assembly.LoadFrom(@"APP_PATH");
    static Stream Create(Stream source,long first,long last,Func<bool> cancelled,int bufferBytes=1024*1024) {
        try { return (Stream)Activator.CreateInstance(app.GetType("CE32_console.NpDownloadPrefetchStream",true),
            BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,new object[]{source,first,last,cancelled,bufferBytes},null); }
        catch(TargetInvocationException e){throw e.InnerException;}
    }
'@
$taskFactory=$taskFactory.Replace('APP_PATH',$taskApp)
$taskSource=$taskSource.Replace('    static int checks;',$taskFactory+"`r`n    static int checks;")
$taskTestSource=Join-Path $taskRoot 'ActualPrefetchTests.cs'
[IO.File]::WriteAllText($taskTestSource,$taskSource)
$taskCompiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
$taskExe=Join-Path $taskRoot 'ActualPrefetchTests.exe'
& $taskCompiler /nologo /optimize+ /warnaserror "/out:$taskExe" $taskTestSource
if($LASTEXITCODE){throw 'Actual assembly prefetch checks did not compile'}
& $taskExe 2>&1 | Tee-Object -FilePath (Join-Path $taskRoot 'actual-prefetch-tests.log')
if($LASTEXITCODE){throw 'Actual137 prefetch checks failed'}
