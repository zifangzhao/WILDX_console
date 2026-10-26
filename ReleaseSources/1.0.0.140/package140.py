from pathlib import Path
import hashlib,json,shutil,subprocess
repo=Path(__file__).resolve().parents[2];root=Path(__file__).resolve().parent
payload=root/'release/app';release=repo/'ReleaseSources/1.0.0.140';latest=repo/'LATEST_INSTALLERS'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest().upper()
load=lambda p:json.loads(p.read_text(encoding='utf-8-sig'))
def save(p,x):p.write_text(json.dumps(x,indent=2)+'\n',encoding='utf-8')
def cp(a,b):b.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(a,b)
old=load(latest/'release-manifest.json');assert old['version']=='1.0.0.139'
receipt=load(root/'build140-receipt.json');assert receipt['changed_sources']==['AssemblyInfo.cs','NpBleConnection.cs']
assert sha(payload/'WILDX_console.exe')==receipt['gui_sha256'] and sha(payload/'NpV2SdDatExport.dll')==receipt['sdk_sha256']
acceptance=load(root/'final-acceptance.json');assert acceptance['preview140_pass'] and acceptance['hardware_readers_closed']
catalog=load(payload/'Firmware/releases.json');v1=next(x for x in catalog['releases'] if x['family']==1);v2=next(x for x in catalog['releases'] if x['family']==2)
assert (v1['appCrc32'],v1['usbCrc32'])==('82E51DEA','4F7B521A')
assert v2['usbCrc32']!='9C6B72A1' and v2['usbCrc32']==acceptance['bundled_v2_usb']
for entry in catalog['releases']:
 for asset in entry['files']:assert sha(payload/'Firmware'/asset['path'])==asset['sha256']
assert not list(payload.rglob('*.zip')) and not list(payload.glob('*Setup*.exe')) and not list(payload.glob('*Preview*.exe'))
expected=[{'path':p.relative_to(payload).as_posix(),'bytes':p.stat().st_size,'sha256':sha(p)} for p in sorted(payload.rglob('*')) if p.is_file()]
with (root/'release/nsis.log').open('wb') as log:subprocess.run(['C:/Program Files (x86)/NSIS/makensis.exe','/NOCD','/V2','/DAPP_PAYLOAD_DIR='+str(payload),str(release/'installer.nsi')],cwd=repo/'USB512_interface',stdout=log,stderr=subprocess.STDOUT,check=True)
installer=payload/'WILDX_console-Setup-1.0.0.140.exe';extracted=root/'release/extracted';assert not extracted.exists()
with (root/'release/extract.log').open('wb') as log:subprocess.run(['C:/ProgramData/NVIDIA Corporation/NVIDIA App/UpdateFramework/ota-artifacts/nvapp/post-processing/241407d0d505673fd4d654200345ed76/NvApp/7z.exe','x',str(installer),'-o'+str(extracted),'-y'],stdout=log,stderr=subprocess.STDOUT,check=True)
for item in expected:assert sha(extracted/item['path'])==item['sha256'],item['path']
actual={p.relative_to(extracted).as_posix() for p in extracted.rglob('*') if p.is_file()}
assert actual-{x['path'] for x in expected}=={'$PLUGINSDIR/System.dll','$PLUGINSDIR/nsDialogs.dll','$PLUGINSDIR/modern-wizard.bmp'}
manifest={'version':'1.0.0.140','releaseId':'WILDX140_V2_PREVIEW_DOWNLOAD_20261025','baselineVersion':'1.0.0.139','applicationRebuiltDuringPackaging':False,'guiSha256':receipt['gui_sha256'],'sdkSha256':receipt['sdk_sha256'],'changedGuiSources':receipt['changed_sources'],'sourceArchiveSha256':sha(release/'compiled-sources.zip'),'v1FirmwareChanged':False,'v2FirmwareChanged':v2['usbCrc32']!='1D3262FB','v2AppCrc32':v2['appCrc32'],'v2UsbCrc32':v2['usbCrc32'],'helpersChanged':False,'installerExecuted':False,'installerPayloadExtractedAndVerified':True,'extractedPayloadFilesVerified':len(expected),'standaloneUpdateRequired':False,'separateZipIncluded':False,'allModeOrEnduranceQualified':False,'acceptance':acceptance,'installer':{'file':installer.name,'bytes':installer.stat().st_size,'sha256':sha(installer)},'payload':expected}
save(release/'package-verification.json',manifest);cp(payload/'Firmware/releases.json',release/'firmware-catalog.json');cp(root/'final-acceptance.json',release/'checks/final-acceptance.json');cp(root/'catalog-smoke140.log',release/'checks/catalog-smoke.log');cp(root/'package140.py',release/'package140.py')
archive=latest/'legacy/20261025_before_1.0.0.140';assert not archive.exists();archive.mkdir(parents=True);assert archive.resolve().is_relative_to(latest.resolve())
for p in list(latest.iterdir()):
 if p.is_file():assert p.resolve().parent==latest.resolve();shutil.move(str(p),str(archive/p.name))
cp(installer,latest/installer.name)
for name in ['release-manifest.json','WILDX_release-manifest.json']:save(latest/name,manifest)
(latest/'SHA256SUMS.txt').write_text(sha(installer)+'  '+installer.name+'\n',encoding='utf-8')
(latest/'README.md').write_text('# WILD-X 1.0.0.140\n\n[Download installer]('+installer.name+'). Includes the BLE preview timeout fix and the accepted V1/V2 firmware with matching catalog directly. No separate update ZIP is required.\n\n[Measurements and limits](../ReleaseSources/1.0.0.140/README.md).\n',encoding='utf-8')
(latest/'WILDX_V2_README.md').write_text('# WILD-X / V2\n\n[Install WILD-X 1.0.0.140]('+installer.name+'). V2 APP '+v2['appCrc32']+' / USB '+v2['usbCrc32']+' and their matching catalog are bundled.\n\n[Validation](../ReleaseSources/1.0.0.140/README.md).\n',encoding='utf-8')
p=repo/'README.md';s=p.read_text(encoding='utf-8');a=s.index('Independent Windows console');b=s.index('The application uses',a);s=s[:a]+'Independent Windows console for Neuropixels devices. Current published version **1.0.0.140**.\n\n[Latest installer](LATEST_INSTALLERS/README.md) includes the BLE preview timeout fix and matching V1/V2 firmware catalogs. No separate ZIP is needed. [Release details](ReleaseSources/1.0.0.140/README.md).\n\n'+s[b:];p.write_text(s,encoding='utf-8')
p=repo/'.gitattributes';data=p.read_bytes().rstrip(b'\r\n')
for line in [b'/LATEST_INSTALLERS/legacy/20261025_before_1.0.0.140/** -text',b'/ReleaseSources/1.0.0.140/** -text']:
 if line not in data:data+=b'\n'+line
p.write_bytes(data+b'\n');assert len(list(latest.glob('*.exe')))==1
print(json.dumps({'installer':manifest['installer'],'payloadFilesVerified':len(expected),'v2Usb':v2['usbCrc32']}))
