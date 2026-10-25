from pathlib import Path
import copy,hashlib,json,shutil,struct,zlib,zipfile
repo=Path(__file__).resolve().parents[2];root=Path(__file__).resolve().parent;source=root/'published'
owner_commit='3280df8fea774eb3fce1650a6fd1c84ce4383817'
release=repo/'ReleaseSources/V2_IMU_1D3262FB_8DE56637_20261025'
bundle=root/'standalone';firmware=repo/'Firmware';v2=firmware/'V2'
load=lambda p:json.loads(p.read_text(encoding='utf-8-sig'))
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest().upper()
def save(p,x):p.parent.mkdir(parents=True,exist_ok=True);p.write_text(json.dumps(x,indent=2)+'\n',encoding='utf-8')
def cp(a,b):b.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(a,b)
assert not release.exists() and not bundle.exists()
receipt=load(source/'package-receipt.json');assert receipt['pair_hardware_validated'] and receipt['app_crc32']=='8DE56637' and receipt['usb_crc32']=='1D3262FB'
assert sha(source/'package-receipt.json')=='5B8BF10BBDEB0EFC688025DD6A7664E9A2DE3EF2F2092B3CE2C922E11B72B90C'
for name,item in receipt['files'].items():assert sha(source/name)==item['sha256'] and (source/name).stat().st_size==item['bytes']
catalog=load(firmware/'releases.json');before=copy.deepcopy(catalog)
v1_before=json.dumps(next(x for x in catalog['releases'] if x['family']==1),sort_keys=True)
v1_files={str(p.relative_to(firmware/'V1')):sha(p) for p in (firmware/'V1').rglob('*') if p.is_file()}
entry=next(x for x in catalog['releases'] if x['family']==2);prior=copy.deepcopy(entry)
unchanged={n for n,item in receipt['files'].items() if (v2/n).exists() and sha(v2/n)==item['sha256']}
archive=v2/'legacy/20261025_before_1D3262FB_8DE56637';assert not archive.exists();archive.mkdir(parents=True)
assert archive.resolve().is_relative_to(v2.resolve())
for p in list(v2.iterdir()):
 if p.is_file() and p.name not in unchanged:
  assert p.resolve().parent==v2.resolve();shutil.move(str(p),str(archive/p.name))
for p in source.iterdir():
 if p.is_file():cp(p,v2/p.name)
def identity(pattern,crc):
 p=next(source.glob(pattern));b=p.read_bytes();u=lambda n:struct.unpack_from('<I',b,n)[0]
 assert b[:8]==b'XENPMNF1' and len(b)==128+u(28) and f'{zlib.crc32(b[128:]):08X}'==crc==f'{u(32):08X}'
 assert b[36:44]==b[128:136]
 return {'imageId':u(16),'baseAddress':u(24),'bytes':u(28),'codeCrc32':crc,'buildIdHex':b[44:76].hex().upper(),'manifestFlags':u(76),'minimumLoaderAbi':u(80),'generation':u(20)}
def asset(name):return {'path':'V2/'+name,'sha256':receipt['files'][name]['sha256']}
app=next(n for n in receipt['files'] if 'APP_update' in n);full=next(n for n in receipt['files'] if '_fused_' in n)
entry.update(release='V2_IMU_STARTUP_USB1D3262FB_APP8DE56637_20261025',sourceCommit=owner_commit,stage='BOUNDED_USB_BLE_SD_IMU_READBACK_CHECKED',appVersion='build 8DE56637 (IMU startup)',usbVersion='build 1D3262FB (IMU startup)',recoveryVersion='build 6408DBB8',appCrc32='8DE56637',usbCrc32='1D3262FB',recoveryCrc32='6408DBB8',app=asset(app),full=asset(full),files=[asset(n) for n in receipt['files']],appIdentity=identity('*APP_update*.xenp','8DE56637'),usbIdentity=identity('*USB_update*.xenp','1D3262FB'),previousApps=[prior['appIdentity']]+prior.get('previousApps',[]),previousUsb=[prior['usbIdentity']]+prior.get('previousUsb',[]),limits=receipt['validation']['limits'],sourceProvenance={'publishedRepository':'zifangzhao/XE_NP','publishedBranch':'V2','publishedCommit':owner_commit,'ownerManifest':'V2/source-manifest.json','ownerManifestSha256':sha(source/'source-manifest.json'),'packageReceiptSha256':sha(source/'package-receipt.json'),'sourceArchive':'ReleaseSources/V2_IMU_1D3262FB_8DE56637_20261025/compiled-sources.zip','sourceArchiveSha256':receipt['firmware_source_archive_sha256'],'canonicalSourceEquivalenceClaimed':False})
assert json.dumps(next(x for x in catalog['releases'] if x['family']==1),sort_keys=True)==v1_before
save(firmware/'releases.json',catalog)
assert {str(p.relative_to(firmware/'V1')):sha(p) for p in (firmware/'V1').rglob('*') if p.is_file()}==v1_files
release.mkdir(parents=True);cp(root/'compiled-sources.zip',release/'compiled-sources.zip')
for name in ['package-receipt.json','source-manifest.json','README.md','SHA256SUMS.txt']:cp(source/name,release/'upstream'/name)
cp(repo/'_artifacts/v2_ble_imu_diagnosis_20261025/result.json',release/'checks/ble-imu-observer-diagnosis.json')
cp(repo/'_artifacts/v2_ble_imu_diagnosis_20261025/replay.log',release/'checks/actual138-imu-replay.log')
save(release/'previous-catalog.json',before)
bundle.mkdir();save(bundle/'Firmware/releases.json',catalog)
for name in receipt['files']:cp(source/name,bundle/'Firmware/V2'/name)
cp(repo/'Tools/Update-WILDX138V2Firmware.ps1',bundle/'Install-V2Firmware.ps1')
(bundle/'Install-V2Firmware.cmd').write_bytes(b'@echo off\r\n"%SystemRoot%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-V2Firmware.ps1"\r\nif errorlevel 1 pause\r\n')
readme='''# V2 firmware catalog update for WILD-X 1.0.0.138

1. Close WILD-X and extract this ZIP.
2. Run `Install-V2Firmware.cmd`. It finds the existing per-user installation,
   verifies the exact published 1.0.0.138 application and firmware hashes,
   preserves its V1 catalog entry, and backs up the previous catalog.
3. Reopen WILD-X. The bundled firmware controls now use V2 USB 1D3262FB /
   APP 8DE56637. Use Firmware Update to install firmware on the device.

This update installs local firmware files and their matching catalog. It does
not flash a device or replace the application/SDK. The original 1.0.0.138
installer is unchanged and contains its older V2 bundle; reapply this update
after reinstalling that installer. A custom installation can run
`Install-V2Firmware.ps1 -InstallDirectory "C:\\path\\to\\WILDX_console"`
from Windows PowerShell.

Bounded USB/SD/BLE/IMU checks passed on the connected V2. The fused image was
assembled from the checked components and was not separately flashed whole.
First-sample PC time is unavailable for legacy UTC1 recordings. One earlier
USB return required reconnecting the data cable; a later final-image return
passed. These results do not establish all-card/all-mode endurance.
'''
(bundle/'README.md').write_text(readme,encoding='utf-8')
files=[{'path':p.relative_to(bundle/'Firmware').as_posix(),'sha256':sha(p)} for p in sorted((bundle/'Firmware').rglob('*')) if p.is_file()]
save(bundle/'bundle-manifest.json',{'forApplicationVersion':'1.0.0.138','applicationSha256':'6E1A2E88B98E6DC55A2733082BECD8496913F91DA99A5C4A86876BC195B8DB86','v2AppCrc32':'8DE56637','v2UsbCrc32':'1D3262FB','files':files})
cp(bundle/'README.md',v2/'STANDALONE_UPDATE.md')
(v2/'README.md').write_text('# V2 USB 1D3262FB / APP 8DE56637\n\n'+receipt['validation']['USB']+'\n\n'+receipt['validation']['BLE_SD']+'\n\n'+receipt['validation']['limits']+'\n\nFor installed WILD-X 1.0.0.138 use [the standalone catalog update](../Updates/WILDX138_V2_1D3262FB_8DE56637_20261025.zip). The unchanged 138 installer contains its earlier V2 bundle. Exact source/validation receipts are in [ReleaseSources](../../ReleaseSources/V2_IMU_1D3262FB_8DE56637_20261025/README.md).\n',encoding='utf-8')
save(root/'integration.json',{'upstreamCommit':owner_commit,'upstreamBranch':'V2','catalogBeforeSha256':sha(release/'previous-catalog.json'),'catalogAfterSha256':sha(firmware/'releases.json'),'v1Preserved':True,'hostRebuilt':False,'sdkRebuilt':False,'hardwareAccessed':False,'installerUnchangedSha256':'CABD0D9810DA4FC0DF4C3373E0EFB1C7CC9AF33222E7D24B16CCB0727DC90779','firmware':receipt['files']})
print('Canonical V2 catalog and standalone payload prepared; V1 and app/installer unchanged. Test before creating the ZIP.')
