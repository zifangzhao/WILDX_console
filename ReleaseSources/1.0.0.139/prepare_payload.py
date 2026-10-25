from pathlib import Path
import hashlib,json,shutil,zipfile
repo=Path(__file__).resolve().parents[2];root=Path(__file__).resolve().parent
build=root/'build139';release=repo/'ReleaseSources/1.0.0.139';payload=root/'release/app'
load=lambda p:json.loads(p.read_text(encoding='utf-8-sig'))
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest().upper()
def save(p,x):p.parent.mkdir(parents=True,exist_ok=True);p.write_text(json.dumps(x,indent=2)+'\n',encoding='utf-8')
def cp(a,b):b.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(a,b)
assert not payload.exists() and not release.exists()
prior=load(repo/'_artifacts/v1_ble_compact_20261025/build138-receipt.json')
files=[Path(s) for s in (build/'compile-inputs.txt').read_text(encoding='utf-8-sig').splitlines() if s.strip()]
before={Path(x['path']).name:x['sha256'] for x in prior['sources']};after={p.name:sha(p) for p in files}
assert len(after)==len(files) and set(after)==set(before)
changed=sorted(n for n in before if before[n]!=after[n]);assert changed==['AssemblyInfo.cs'],changed
exe=build/'compile-bin/WILDX_console.exe';sdk=build/'compile-bin/NpV2SdDatExport.dll'
assert sha(sdk)==prior['sdk_sha256']
receipt={'version':'1.0.0.139','baseline':'1.0.0.138','gui_sha256':sha(exe),'sdk_sha256':sha(sdk),'new_product_compilations':1,'sdk_compilations':0,'changed_sources':changed,'sources':[{'path':str(p),'sha256':sha(p)} for p in files],'references':[{'path':s,'sha256':sha(Path(s))} for s in (build/'reference-inputs.txt').read_text(encoding='utf-8-sig').splitlines() if s.strip()],'scope':'Version-only compilation from exact138 inputs. Latest accepted V2 firmware/catalog bundled; V1 and SDK unchanged. No new firmware build or hardware test.'}
save(root/'build139-receipt.json',receipt)
old=load(repo/'LATEST_INSTALLERS/release-manifest.json');assert old['version']=='1.0.0.138'
for item in old['payload']:
 if item['path']=='Firmware/releases.json' or item['path'].startswith('Firmware/V2/'):continue
 p=repo/'_artifacts/v1_ble_compact_20261025/release/app'/item['path'];assert sha(p)==item['sha256'];cp(p,payload/item['path'])
cp(exe,payload/'WILDX_console.exe');assert sha(payload/'NpV2SdDatExport.dll')==receipt['sdk_sha256']
catalog=load(repo/'Firmware/releases.json');v1=next(x for x in catalog['releases'] if x['family']==1);v2=next(x for x in catalog['releases'] if x['family']==2)
assert (v1['appCrc32'],v1['usbCrc32'])==('82E51DEA','4F7B521A')
assert (v2['appCrc32'],v2['usbCrc32'])==('8DE56637','1D3262FB')
for entry in catalog['releases']:
 for asset in entry['files']:assert sha(repo/'Firmware'/asset['path'])==asset['sha256']
# Archive the superseded standalone delivery; the new installer is self-contained.
updates=repo/'Firmware/Updates';archive=updates/'legacy/20261025_replaced_by_1.0.0.139';assert not archive.exists();archive.mkdir(parents=True)
assert archive.resolve().is_relative_to(updates.resolve())
for name in ['WILDX138_V2_1D3262FB_8DE56637_20261025.zip','SHA256SUMS.txt']:
 p=updates/name;assert p.resolve().parent==updates.resolve();shutil.move(str(p),str(archive/name))
old_note=repo/'Firmware/V2/STANDALONE_UPDATE.md'
if old_note.exists():shutil.move(str(old_note),str(archive/'STANDALONE_UPDATE.md'))
(updates/'README.md').write_text('# Firmware delivery\n\nUse the [latest WILD-X installer](../../LATEST_INSTALLERS/README.md). Version 1.0.0.139 includes V2 USB 1D3262FB / APP 8DE56637 and the matching catalog directly. No separate firmware ZIP is needed. The prior standalone update for138 is archived for recovery.\n',encoding='utf-8')
(repo/'Firmware/V2/README.md').write_text('# V2 USB 1D3262FB / APP 8DE56637\n\nIncluded with its matching firmware catalog in [WILD-X 1.0.0.139](../../LATEST_INSTALLERS/README.md). Install the console update, then use Firmware Update in WILD-X to update the device. No separate ZIP is required.\n\nThe accepted components and source receipts come from XE_NP commit3280df8fea774eb3fce1650a6fd1c84ce4383817 (branchV2). Bounded USB/SD/BLE/IMU checks passed with138;139 changes only the application version and bundles these exact firmware files.\n\nFirst-sample PC time is unavailable for legacyUTC1 recordings. One earlier USB return required cable reconnect; a later final-image return passed. The fused image was assembled from checked components, not separately flashed whole. These checks do not establish all-card/all-mode endurance. [Evidence](../../ReleaseSources/V2_IMU_1D3262FB_8DE56637_20261025/README.md).\n',encoding='utf-8')
cp(repo/'Firmware/releases.json',payload/'Firmware/releases.json')
for p in (repo/'Firmware/V2').iterdir():
 if p.is_file():cp(p,payload/'Firmware/V2'/p.name)
assert not list(payload.rglob('*.zip')) and not (payload/'Firmware/Updates').exists()
release.mkdir(parents=True);ledger=[]
with zipfile.ZipFile(release/'compiled-sources.zip','x',zipfile.ZIP_DEFLATED) as z:
 for i,item in enumerate(receipt['sources']):
  p=Path(item['path']);name='gui/'+str(i).zfill(3)+'_'+p.name;z.write(p,name);ledger.append({'archive':name,'compiler_input':str(p),'sha256':sha(p),'component':'GUI'})
 for item in load(repo/'ReleaseSources/1.0.0.138/source-inputs.json'):
  if item['component']=='SDK':
   p=Path(item['compiler_input']);assert sha(p)==item['sha256'];z.write(p,item['archive']);ledger.append(item)
 for p in [root/'prepare139.py',root/'prepare_payload.py',build/'ui-only.targets',repo/'BuildVersion.targets']:z.write(p,'build/'+p.name)
save(release/'source-inputs.json',ledger);cp(root/'build139-receipt.json',release/'build-receipt.json')
cp(build/'gui-build.log',release/'build.log');cp(build/'reference-inputs.txt',release/'reference-inputs.txt')
cp(repo/'ReleaseSources/1.0.0.138/installer.nsi',release/'installer.nsi')
cp(repo/'Firmware/releases.json',release/'firmware-catalog.json')
cp(repo/'Firmware/V2/package-receipt.json',release/'firmware/v2-package-receipt.json')
cp(repo/'ReleaseSources/V2_IMU_1D3262FB_8DE56637_20261025/integration.json',release/'firmware/v2-integration.json')
save(root/'release/preparation.json',{'version':'1.0.0.139','installer_created':False,'published':False,'v2App':'8DE56637','v2Usb':'1D3262FB','standaloneZipRequired':False})
print(json.dumps({'version':'1.0.0.139','changed_sources':changed,'gui_sha256':receipt['gui_sha256'],'sdk_sha256':receipt['sdk_sha256']}))
