$ErrorActionPreference='Stop'
$taskVersionFile=Join-Path $PSScriptRoot 'USB512_interface/USB512_interface/Properties/AssemblyInfo.cs'
$taskVersionSource=[IO.File]::ReadAllText($taskVersionFile)
$taskVersionPattern='Assembly(?:File)?Version\("(\d+\.\d+\.\d+\.\d+)"\)'
$taskVersionMatches=[regex]::Matches($taskVersionSource,$taskVersionPattern)
if($taskVersionMatches.Count -ne 2 -or $taskVersionMatches[0].Groups[1].Value -ne $taskVersionMatches[1].Groups[1].Value){throw 'Assembly and file versions must match.'}
$taskCurrentVersion=[version]$taskVersionMatches[0].Groups[1].Value
$taskNextVersion=[version]::new($taskCurrentVersion.Major,$taskCurrentVersion.Minor,$taskCurrentVersion.Build,$taskCurrentVersion.Revision+1)
$taskVersionSource=[regex]::Replace($taskVersionSource,$taskVersionPattern,{param($taskMatch) $taskMatch.Value.Replace($taskCurrentVersion.ToString(),$taskNextVersion.ToString())})
[IO.File]::WriteAllText($taskVersionFile,$taskVersionSource,[Text.UTF8Encoding]::new($false))
$taskNextVersion.ToString()
