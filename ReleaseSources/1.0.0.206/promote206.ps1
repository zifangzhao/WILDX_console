param([switch]$Stage)
$ErrorActionPreference='Stop'
$taskRoot='D:/Code/WILDX_console/WILDX_console'
$taskPrep=Join-Path $taskRoot '_artifacts/release206-prep'
$taskPlan=Get-Content -LiteralPath (Join-Path $taskPrep 'promotion-plan.json') -Raw|ConvertFrom-Json
function Assert-Pin($p,$h,$bytes){
 if(!(Test-Path -LiteralPath $p) -or (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash -cne $h -or ($null -ne $bytes -and (Get-Item -LiteralPath $p).Length -ne $bytes)){throw ('Identity differs: '+$p)}
}
function Json-File($p,$x){[IO.File]::WriteAllText($p,($x|ConvertTo-Json -Depth 30)+"`n",[Text.UTF8Encoding]::new($false))}
function Copy-Pinned($source,$target,$hash,$bytes){
 Assert-Pin $source $hash $bytes
 [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))|Out-Null
 Copy-Item -LiteralPath $source -Destination $target
 Assert-Pin $target $hash $bytes
}
$taskBranch=(& git -C $taskRoot branch --show-current).Trim()
$taskHead=(& git -C $taskRoot rev-parse HEAD).Trim()
if($taskBranch -ne 'main' -or $taskHead -ne $taskPlan.expected_git_baseline){throw 'Intended main baseline changed; review before promotion.'}
$taskAlreadyStaged=@(& git -C $taskRoot diff --cached --name-only)
if($taskAlreadyStaged.Count){throw 'Index is not empty; preserve existing work and review it before explicit release staging.'}
$taskPackagePath=Join-Path $taskRoot '_artifacts/release206/package-verification.json'
$taskPackage=Get-Content -LiteralPath $taskPackagePath -Raw|ConvertFrom-Json
if($taskPackage.status -ne 'payload_identity_verified' -or $taskPackage.version -ne '1.0.0.206' -or !$taskPackage.all_expected_files_match -or $taskPackage.expected_payload_files -ne 57 -or $taskPackage.unexpected_runtime_files -ne 0 -or $taskPackage.installer_executed -or $taskPackage.package_invocations -ne 1 -or $taskPackage.fresh_extractions -ne 1){throw 'Completed exact206 package verification is required.'}
$taskFinal=Get-Content -LiteralPath (Join-Path $taskPrep 'final-payload.json') -Raw|ConvertFrom-Json
if($taskFinal.status -ne 'ready' -or $taskFinal.version -ne '1.0.0.206' -or $taskFinal.files.Count -ne 57){throw 'Final206 allowlist is not ready.'}
Assert-Pin (Join-Path $taskPrep 'qualification.json') $taskPlan.qualification_sha256 $null
$taskQualification=Get-Content -LiteralPath (Join-Path $taskPrep 'qualification.json') -Raw|ConvertFrom-Json
if($taskQualification.status -ne 'qualified_host_sparse_v2_update_fix' -or $taskQualification.gui_sha256 -ne $taskPlan.gui_sha256 -or $taskQualification.firmware_changed -or $taskQualification.catalog_changed){throw '206 qualification scope changed.'}
$taskBaseline=Get-Content -LiteralPath (Join-Path $taskRoot '_artifacts/release204-prep/final-payload.json') -Raw|ConvertFrom-Json
foreach($taskFile in $taskFinal.files){
 $taskVerified=@($taskPackage.files|Where-Object path -CEQ $taskFile.path)
 if($taskVerified.Count -ne 1 -or $taskVerified[0].sha256 -cne $taskFile.sha256 -or $taskVerified[0].bytes -ne $taskFile.bytes){throw ('Package receipt differs: '+$taskFile.path)}
 Assert-Pin $taskFile.source $taskFile.sha256 $taskFile.bytes
 if($taskFile.path -ne 'WILDX_console.exe'){
  $taskOld=@($taskBaseline.files|Where-Object path -CEQ $taskFile.path)
  if($taskOld.Count -ne 1 -or $taskOld[0].sha256 -cne $taskFile.sha256 -or $taskOld[0].bytes -ne $taskFile.bytes){throw ('Non-GUI payload changed: '+$taskFile.path)}
 }
}
$taskInstaller=Join-Path $taskRoot '_artifacts/release206/payload/WILDX_console-Setup-1.0.0.206.exe'
Assert-Pin $taskInstaller $taskPackage.installer_sha256 $taskPackage.installer_bytes
$taskLatest=Join-Path $taskRoot 'LATEST_INSTALLERS'
$taskCurrent=@(Get-ChildItem -LiteralPath $taskLatest -File -Filter '*Setup*.exe')
if($taskCurrent.Count -ne 1 -or $taskCurrent[0].Name -ne 'WILDX_console-Setup-1.0.0.204.exe'){throw 'Expected one current204 installer.'}
foreach($taskOld in $taskPlan.baseline_latest){Assert-Pin (Join-Path $taskRoot $taskOld.path) $taskOld.sha256 $taskOld.bytes}
foreach($taskFile in $taskPlan.source_files){Assert-Pin $taskFile.source $taskFile.sha256 $taskFile.bytes}
$taskLegacy=[IO.Path]::GetFullPath((Join-Path $taskLatest 'legacy/1.0.0.204'))
if(!$taskLegacy.StartsWith(([IO.Path]::GetFullPath($taskLatest)+[IO.Path]::DirectorySeparatorChar),[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $taskLegacy)){throw 'Legacy archive target must be new and inside latest.'}
$taskRelease=Join-Path $taskRoot 'ReleaseSources/1.0.0.206'
if(Test-Path -LiteralPath $taskRelease){throw 'ReleaseSources206 already exists; do not overwrite promotion.'}

# Mutations start only after all source, package and baseline checks above.
$taskAttr=Join-Path $taskRoot '.gitattributes'
foreach($taskLine in @('/ReleaseSources/1.0.0.206/** -text','/LATEST_INSTALLERS/legacy/1.0.0.204/** -text')){
 if(!(Get-Content -LiteralPath $taskAttr|Where-Object {$_ -ceq $taskLine})){[IO.File]::AppendAllText($taskAttr,"`n"+$taskLine+"`n",[Text.UTF8Encoding]::new($false))}
}
[IO.Directory]::CreateDirectory($taskLegacy)|Out-Null
foreach($taskOld in $taskPlan.baseline_latest){
 $taskSource=Join-Path $taskRoot $taskOld.path;$taskTarget=Join-Path $taskLegacy ([IO.Path]::GetFileName($taskSource))
 if($taskSource.EndsWith('.exe')){Move-Item -LiteralPath $taskSource -Destination $taskTarget}else{Copy-Item -LiteralPath $taskSource -Destination $taskTarget}
 Assert-Pin $taskTarget $taskOld.sha256 $taskOld.bytes
}
foreach($taskFile in $taskPlan.source_files){
 $taskTarget=Join-Path $taskRoot $taskFile.path
 if($taskFile.path.StartsWith('ReleaseSources/')){Copy-Pinned $taskFile.source $taskTarget $taskFile.sha256 $taskFile.bytes}
 else{Assert-Pin $taskTarget $taskFile.sha256 $taskFile.bytes}
}
Copy-Pinned $taskInstaller (Join-Path $taskLatest 'WILDX_console-Setup-1.0.0.206.exe') $taskPackage.installer_sha256 $taskPackage.installer_bytes
Copy-Item -LiteralPath $taskPackagePath -Destination (Join-Path $taskRelease 'package-verification.json')
Copy-Item -LiteralPath (Join-Path $taskPrep 'qualification.json') -Destination (Join-Path $taskRelease 'qualification.json')
Copy-Item -LiteralPath (Join-Path $taskPrep 'promote206.ps1') -Destination (Join-Path $taskRelease 'promote206.ps1')
Copy-Item -LiteralPath (Join-Path $taskPrep 'promotion-plan.json') -Destination (Join-Path $taskRelease 'promotion-plan.json')
$taskValidation=$taskQualification
$taskValidation|Add-Member -NotePropertyName package_verification -NotePropertyValue 'package-verification.json'
$taskValidation|Add-Member -NotePropertyName installer_sha256 -NotePropertyValue $taskPackage.installer_sha256
Json-File (Join-Path $taskRelease 'validation-summary.json') $taskValidation
$taskManifest=Get-Content -LiteralPath (Join-Path $taskLegacy 'release-manifest.json') -Raw|ConvertFrom-Json
$taskManifest.version='1.0.0.206';$taskManifest.releaseId='WILDX206_SPARSE_V2_FULL_BUNDLE_FIX_20261030';$taskManifest.baselineVersion='1.0.0.204'
$taskManifest.installer=[ordered]@{bytes=$taskPackage.installer_bytes;file='WILDX_console-Setup-1.0.0.206.exe';sha256=$taskPackage.installer_sha256}
$taskManifest.guiSha256=$taskPlan.gui_sha256;$taskManifest.sdkSha256=$taskQualification.sdk_sha256
$taskManifest.sourceArchiveSha256=$taskPlan.source_archive_sha256;$taskManifest.sourcePublicationCommit=$null
$taskManifest.sourceProvenance='../ReleaseSources/1.0.0.206/source-freeze.json';$taskManifest.validation='../ReleaseSources/1.0.0.206/validation-summary.json'
$taskManifest|Add-Member -NotePropertyName sourceGitBaseline -NotePropertyValue $taskPlan.expected_git_baseline
$taskManifest|Add-Member -NotePropertyName sourceIdentityRole -NotePropertyValue 'git_baseline_plus_exact_compiled_source_overlay'
$taskManifest.payload=@($taskFinal.files|Select-Object path,bytes,sha256)
Json-File (Join-Path $taskLatest 'release-manifest.json') $taskManifest
Copy-Item -LiteralPath (Join-Path $taskLatest 'release-manifest.json') -Destination (Join-Path $taskLatest 'WILDX_release-manifest.json')
[IO.File]::WriteAllText((Join-Path $taskLatest 'SHA256SUMS.txt'),$taskPackage.installer_sha256+'  WILDX_console-Setup-1.0.0.206.exe'+"`n",[Text.UTF8Encoding]::new($false))
$taskLatestReadme=@"
# WILD-X1.0.0.206

[Download the installer](WILDX_console-Setup-1.0.0.206.exe).

Fixes the V2 Full bundle parser error before programming at0x080074C0. Untouched flash and configuration are preserved from the exact backup. Select Advanced -> Firmware update -> the exact device/family -> Full bundle: APP + USB + boot -> Update bundled firmware.

All firmware and companions are unchanged from204: V1APPFFF283D9/USB1E73A743, V2APP194D9B94/USB74A9B98B/recovery6408DBB8; V1Camera unchanged. New V2 SD experiments are excluded. This is an independently checked host updater fix; no new physical flash or recording qualification is claimed. Source and validation records are in ReleaseSources/1.0.0.206. The predecessor is archived under legacy/1.0.0.204; one current installer remains here.
"@
[IO.File]::WriteAllText((Join-Path $taskLatest 'README.md'),$taskLatestReadme+"`n",[Text.UTF8Encoding]::new($false))
$taskPaths=@($taskPlan.git_paths)
[IO.File]::WriteAllLines((Join-Path $taskPrep 'git-paths206.txt'),$taskPaths,[Text.UTF8Encoding]::new($false))
if($Stage){
 & git -C $taskRoot add -- $taskPaths
 if($LASTEXITCODE){throw 'Explicit git add failed.'}
 $taskStaged=@(& git -C $taskRoot diff --cached --name-only)
 $taskOutside=@($taskStaged|Where-Object {$_ -notin $taskPaths})
 if($taskOutside.Count){throw ('Unexpected staged paths: '+($taskOutside -join ', '))}
 & git -C $taskRoot diff --cached --check
 if($LASTEXITCODE){throw 'Staged whitespace check failed; review without broad cleanup.'}
 foreach($taskPath in $taskPaths){
  $taskDisk=Join-Path $taskRoot $taskPath
  if((Test-Path -LiteralPath $taskDisk) -and ($taskPath.StartsWith('ReleaseSources/') -or $taskPath.StartsWith('LATEST_INSTALLERS/legacy/') -or $taskPath -in @('LATEST_INSTALLERS/release-manifest.json','LATEST_INSTALLERS/WILDX_release-manifest.json','LATEST_INSTALLERS/SHA256SUMS.txt'))){
   $taskRaw=(& git -C $taskRoot hash-object --no-filters -- $taskPath).Trim()
   $taskBlob=(& git -C $taskRoot rev-parse (':'+$taskPath)).Trim()
   if($LASTEXITCODE -or $taskRaw -ne $taskBlob){throw ('Staged bytes were normalized: '+$taskPath)}
  }
 }
}
Write-Output '206 promoted with exact allowlist; no commit or push performed.'
