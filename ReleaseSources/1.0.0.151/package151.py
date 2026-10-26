from pathlib import Path
import hashlib,json,shutil,subprocess,zipfile
repo=Path(__file__).resolve().parents[2];root=Path(__file__).resolve().parent
payload=root/'app151';release=repo/'ReleaseSources/1.0.0.151';latest=repo/'LATEST_INSTALLERS'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest().upper()
load=lambda p:json.loads(p.read_text(encoding='utf-8-sig'))
def save(p,x):p.parent.mkdir(parents=True,exist_ok=True);p.write_text(json.dumps(x,indent=2)+'\n',encoding='utf-8')
def cp(a,b):b.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(a,b)
old=load(latest/'release-manifest.json');assert old['version']=='1.0.0.150'
receipt=load(root/'build151-receipt.json')
assert sha(payload/'WILDX_console.exe')==receipt['gui_sha256']
assert sha(payload/'NpV2SdDatExport.dll')==receipt['sdk_sha256']==old['sdkSha256']
assert sha(payload/'NPConfigurator/NPConfigurator.dll')==old['configuratorSha256']
removed=load(root/'removed-payload-files.json');changed=[]
for x in old['payload']:
 p=payload/x['path']
 if not p.exists():assert x['path'] in removed;continue
 if sha(p)!=x['sha256']:changed.append(x['path'])
assert changed==['WILDX_console.exe'],changed
assert not any('legacy' in p.parts or p.suffix.lower() in ('.pdb','.patch','.xml') for p in payload.rglob('*') if p.is_file())
catalog=load(payload/'Firmware/releases.json');v1=next(x for x in catalog['releases'] if x['family']==1);v2=next(x for x in catalog['releases'] if x['family']==2)
assert (v1['appCrc32'],v1['usbCrc32'],v2['appCrc32'],v2['usbCrc32'])==('82E51DEA','4F7B521A','C250DF48','A71BDECC')
for e in catalog['releases']:
 for x in e['files']:assert sha(payload/'Firmware'/x['path'])==x['sha256']
assert not (release/'package-verification.json').exists();release.mkdir(parents=True,exist_ok=True)
cp(repo/'ReleaseSources/1.0.0.150/installer.nsi',release/'installer.nsi')
cp(repo/'Note/DOWNLOAD_BURST_HEADROOM_20261026.md',release/'README.md')
cp(root/'build151-receipt.json',release/'build-receipt.json');cp(root/'removed-payload-files.json',release/'removed-payload-files.json')
checks=['test_download_speed.ps1','test_download_burst.ps1','Tests/NpDownloadSpeedTests.cs','Tests/NpDownloadBurstTests.cs','Tests/NpDownloadEtaTests.cs']
with zipfile.ZipFile(release/'compiled-sources.zip','w',zipfile.ZIP_DEFLATED) as z:
 for i,item in enumerate(receipt['sources']):
  p=Path(item['path']);assert sha(p)==item['sha256'];z.write(p,'gui/'+str(i).zfill(3)+'_'+p.name)
 with zipfile.ZipFile(repo/'ReleaseSources/1.0.0.150/compiled-sources.zip') as prior:
  for name in prior.namelist():
   if name.startswith(('sdk/','configurator/')):z.writestr(name,prior.read(name))
 for name in checks:z.write(repo/name,'checks/'+name)
 z.write(root/'build151/ui-only.targets','checks/ui-only.targets');z.write(repo/'BuildVersion.targets','checks/BuildVersion.targets')
cp(root/'final-tests.log',release/'checks/download-speed-tests.log')
for folder,action in [('v1-ab-r2','transport'),('v1-export','export'),('v1-cancel','cancel'),('v2-ab-r2','transport'),('v2-export','export'),('v2-cancel','cancel')]:
 report=load(root/folder/(action+'.json'));assert report['result']=='pass' and report['catalog_preserved']
 cp(root/folder/(action+'.json'),release/'checks'/folder/(action+'.json'))
for name in ['summary.json','headroom.json','DownloadHeadroom.cs','prepare_observer.py','build_inputs.py']:
 cp(root/name,release/'checks'/name)

expected=[{'path':p.relative_to(payload).as_posix(),'bytes':p.stat().st_size,'sha256':sha(p)} for p in sorted(payload.rglob('*')) if p.is_file()]
assert len(expected)==34 and not list(payload.rglob('*.zip')) and not list(payload.glob('*Setup*.exe'))
with (root/'nsis151.log').open('wb') as log:subprocess.run(['C:/Program Files (x86)/NSIS/makensis.exe','/NOCD','/V2','/DAPP_PAYLOAD_DIR='+str(payload),str(release/'installer.nsi')],cwd=repo/'USB512_interface',stdout=log,stderr=subprocess.STDOUT,check=True)
installer=payload/'WILDX_console-Setup-1.0.0.151.exe';extracted=root/'extracted151';assert not extracted.exists()
with (root/'extract151.log').open('wb') as log:subprocess.run(['C:/ProgramData/NVIDIA Corporation/NVIDIA App/UpdateFramework/ota-artifacts/nvapp/post-processing/241407d0d505673fd4d654200345ed76/NvApp/7z.exe','x',str(installer),'-o'+str(extracted),'-y'],stdout=log,stderr=subprocess.STDOUT,check=True)
for item in expected:assert sha(extracted/item['path'])==item['sha256'],item['path']
actual={p.relative_to(extracted).as_posix() for p in extracted.rglob('*') if p.is_file()}
assert actual-{x['path'] for x in expected}=={'$PLUGINSDIR/System.dll','$PLUGINSDIR/nsDialogs.dll','$PLUGINSDIR/modern-wizard.bmp'}
smoke=root/'catalog-smoke151.ps1'
smoke.write_text("""$ErrorActionPreference='Stop'
$root='"""+extracted.as_posix()+"""'
$app=[Reflection.Assembly]::LoadFrom(\"$root/WILDX_console.exe\")
$flags=[Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
$type=$app.GetType('USB512_interface.NpFirmwareCatalog',$true)
$catalog=$type.GetProperty('Bundled',$flags).GetValue($null,$null)
$errorText=$type.GetField('Error',$flags).GetValue($catalog);if($errorText){throw $errorText}
$actualRoot=[string]$type.GetField('Root',$flags).GetValue($catalog)
if([IO.Path]::GetFullPath($actualRoot) -ne [IO.Path]::GetFullPath((Join-Path $root 'Firmware'))){throw 'Unexpected bundled root'}
if($app.GetName().Version.ToString() -ne '1.0.0.151'){throw 'Wrong app version'}
foreach($family in 1,2){$release=$type.GetMethod('For',$flags).Invoke($catalog,@([uint32]$family));$want=if($family -eq 1){'4F7B521A'}else{'A71BDECC'};if($release.usbCrc32 -ne $want){throw 'Firmware mismatch'};\"PASS extracted151 automatic Bundled catalog: V$family APP$($release.appCrc32) / USB$want\"}
""",encoding='utf-8')
with (root/'catalog-smoke151.log').open('wb') as log:subprocess.run(['powershell','-NoProfile','-ExecutionPolicy','Bypass','-File',str(smoke)],stdout=log,stderr=subprocess.STDOUT,check=True)
cp(root/'catalog-smoke151.log',release/'checks/catalog-smoke.log');cp(smoke,release/'checks/catalog-smoke.ps1')
acceptance={'result':'software_checks_passed','production_transport_checks':790,'built_app_rate_checks':254,'built_app_eta_checks':14,'hardware_tested_this_fix':True,'production_build_warnings':0}
manifest={'version':'1.0.0.151','releaseId':'WILDX151_USB_BURST_20261026','baselineVersion':'1.0.0.150','localGuiCompilationsThisTask':1,'localHelperCompilationsThisTask':0,'applicationRebuiltDuringPackaging':False,'guiSha256':receipt['gui_sha256'],'sdkSha256':receipt['sdk_sha256'],'configuratorSha256':old['configuratorSha256'],'sourceArchiveSha256':sha(release/'compiled-sources.zip'),'changedGuiSources':receipt['changed_gui_sources'],'v1FirmwareChanged':False,'v2FirmwareChanged':False,'v2AppCrc32':v2['appCrc32'],'v2UsbCrc32':v2['usbCrc32'],'helpersChanged':False,'installerExecuted':False,'installerPayloadExtractedAndVerified':True,'extractedPayloadFilesVerified':len(expected),'obsoletePayloadFilesRemoved':len(removed),'standaloneUpdateRequired':False,'separateZipIncluded':False,'allModeOrEnduranceQualified':False,'acceptance':acceptance,'installer':{'file':installer.name,'bytes':installer.stat().st_size,'sha256':sha(installer)},'payload':expected}
save(release/'package-verification.json',manifest);cp(payload/'Firmware/releases.json',release/'firmware-catalog.json');cp(Path(__file__),release/'package151.py')
archive=latest/'legacy/20261026_before_1.0.0.151';assert not archive.exists();archive.mkdir(parents=True);assert archive.resolve().is_relative_to(latest.resolve())
for p in list(latest.iterdir()):
 if p.is_file():assert p.resolve().parent==latest.resolve();shutil.move(str(p),str(archive/p.name))
cp(installer,latest/installer.name)
for name in ['release-manifest.json','WILDX_release-manifest.json']:save(latest/name,manifest)
(latest/'SHA256SUMS.txt').write_text(sha(installer)+'  '+installer.name+'\n',encoding='utf-8')
(latest/'README.md').write_text('# WILD-X 1.0.0.151\n\n[Download installer]('+installer.name+'). Reduces command pauses with larger negotiated V1/V2 USB download batches. Same recordings, unchanged firmware and all integrity checks retained. The clean payload, shared updater, recording, exploration and BLE preview are retained.\n\n[Usage and validation limits](../ReleaseSources/1.0.0.151/README.md). Matched existing-recording V1/V2 comparisons, early retirement/reopen integrity, unchanged catalogs and focused software checks passed. See the measured results and limits; no firmware upgrade is needed.\n',encoding='utf-8')
(latest/'WILDX_V2_README.md').write_text('# WILD-X / V2\n\n[Install WILD-X 1.0.0.151]('+installer.name+'). V2 APP '+v2['appCrc32']+' / USB '+v2['usbCrc32']+' and the matching catalog are bundled. No separate update ZIP is required.\n\n[USB download throughput](../ReleaseSources/1.0.0.151/README.md).\n',encoding='utf-8')
p=repo/'README.md';s=p.read_text(encoding='utf-8');a=s.index('Independent Windows console');b=s.index('The application uses',a);s=s[:a]+'Independent Windows console for Neuropixels devices. Current published version **1.0.0.151**.\n\n[Latest installer](LATEST_INSTALLERS/README.md) uses larger negotiated batches to reduce V1/V2 SD-USB download command overhead. Recording, exploration, shared firmware updating and BLE preview are retained. [Usage and validation](ReleaseSources/1.0.0.151/README.md).\n\n'+s[b:];p.write_text(s,encoding='utf-8')
p=repo/'.gitattributes';data=p.read_bytes().rstrip(b'\r\n')
for line in [b'/LATEST_INSTALLERS/legacy/20261026_before_1.0.0.151/** -text',b'/ReleaseSources/1.0.0.151/** -text']:
 if line not in data:data+=b'\n'+line
p.write_bytes(data+b'\n');assert len(list(latest.glob('*.exe')))==1
print(json.dumps({'installer':manifest['installer'],'payloadFilesVerified':len(expected),'obsoleteFilesExcluded':len(removed)}))
