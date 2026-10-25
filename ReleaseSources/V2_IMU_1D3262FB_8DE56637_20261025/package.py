from pathlib import Path
import hashlib,json,shutil,zipfile
repo=Path(__file__).resolve().parents[2];root=Path(__file__).resolve().parent
release=repo/'ReleaseSources/V2_IMU_1D3262FB_8DE56637_20261025';bundle=root/'standalone'
load=lambda p:json.loads(p.read_text(encoding='utf-8-sig'))
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest().upper()
def cp(a,b):b.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(a,b)
checks=load(root/'checks-r3/result.json');assert checks['result'].startswith('PASS')
installer=repo/'LATEST_INSTALLERS/WILDX_console-Setup-1.0.0.138.exe'
assert sha(installer)=='CABD0D9810DA4FC0DF4C3373E0EFB1C7CC9AF33222E7D24B16CCB0727DC90779'
assert sha(repo/'Tools/Update-WILDX138V2Firmware.ps1')==sha(bundle/'Install-V2Firmware.ps1')
updates=repo/'Firmware/Updates';updates.mkdir(exist_ok=True)
archive=updates/'WILDX138_V2_1D3262FB_8DE56637_20261025.zip';assert not archive.exists()
expected={p.relative_to(bundle).as_posix():sha(p) for p in bundle.rglob('*') if p.is_file()}
with zipfile.ZipFile(archive,'x',zipfile.ZIP_DEFLATED) as z:
 for name in sorted(expected):z.write(bundle/name,name)
with zipfile.ZipFile(archive) as z:
 assert z.testzip() is None and set(z.namelist())==set(expected)
 for name,h in expected.items():assert hashlib.sha256(z.read(name)).hexdigest().upper()==h
for a,b in [(root/'integration.json',release/'integration.json'),(root/'checks-r3/result.json',release/'checks/standalone-update-result.json'),(root/'standalone-checks-r3.log',release/'checks/standalone-update.log'),(repo/'Tools/Update-WILDX138V2Firmware.ps1',release/'Update-WILDX138V2Firmware.ps1'),(repo/'test_v2_firmware_standalone.ps1',release/'test_v2_firmware_standalone.ps1'),(root/'integrate.py',release/'integrate.py')]:cp(a,b)
receipt={'file':archive.name,'bytes':archive.stat().st_size,'sha256':sha(archive),'zipFilesVerified':len(expected),'payload':expected,'applicationVersion':'1.0.0.138','installerSha256Unchanged':sha(installer),'appRebuilt':False,'sdkRebuilt':False,'hardwareAccessed':False,'upstreamCommit':'3280df8fea774eb3fce1650a6fd1c84ce4383817','upstreamBranch':'V2','v1Preserved':True,'compatibilityResult':checks['result']}
(release/'standalone-package.json').write_text(json.dumps(receipt,indent=2)+'\n',encoding='utf-8')
(updates/'SHA256SUMS.txt').write_text(sha(archive)+'  '+archive.name+'\n',encoding='utf-8')
(updates/'README.md').write_text('# Standalone firmware catalogs\n\n[V2 update for WILD-X 1.0.0.138]('+archive.name+') installs USB 1D3262FB / APP 8DE56637 files and their matching local catalog. Extract the ZIP, close WILD-X, then run Install-V2Firmware.cmd. Open WILD-X afterward and use Firmware Update to flash the device separately. V1 is preserved; the application and SDK are unchanged.\n\nThe original 138 installer retains its older V2 catalog; reapply this standalone update after reinstalling it. [Validation and limits](../../ReleaseSources/V2_IMU_1D3262FB_8DE56637_20261025/README.md).\n',encoding='utf-8')
p=repo/'LATEST_INSTALLERS/README.md';s=p.read_text(encoding='utf-8');s=s.replace('USB firmware, buffered downloads and V2 are unchanged.','USB download improvements remain included. The installer retains its original V2 bundle; use the [standalone V2 firmware/catalog update](../Firmware/Updates/WILDX138_V2_1D3262FB_8DE56637_20261025.zip) for USB 1D3262FB / APP 8DE56637.');p.write_text(s,encoding='utf-8')
(repo/'LATEST_INSTALLERS/WILDX_V2_README.md').write_text('# WILD-X / V2 firmware\n\nUse the [unchanged WILD-X 1.0.0.138 installer](WILDX_console-Setup-1.0.0.138.exe), then apply the [standalone V2 firmware/catalog update](../Firmware/Updates/WILDX138_V2_1D3262FB_8DE56637_20261025.zip). The updater installs the matching local catalog and firmware files; flashing the device remains a separate Firmware Update action in WILD-X.\n\n[Validation and limits](../ReleaseSources/V2_IMU_1D3262FB_8DE56637_20261025/README.md).\n',encoding='utf-8')
p=repo/'README.md';s=p.read_text(encoding='utf-8');old='USB firmware, buffered downloads and V2 are unchanged.'
assert s.count(old)==1;s=s.replace(old,'USB firmware and buffered downloads are unchanged. A [standalone V2 firmware/catalog update](Firmware/Updates/WILDX138_V2_1D3262FB_8DE56637_20261025.zip) provides USB 1D3262FB / APP 8DE56637 for the existing application.');p.write_text(s,encoding='utf-8')
p=repo/'.gitattributes';b=p.read_bytes();line=b'/ReleaseSources/V2_IMU_1D3262FB_8DE56637_20261025/** -text'
if line not in b:p.write_bytes(b.rstrip(b'\r\n')+b'\n'+line+b'\n')
assert sha(installer)==receipt['installerSha256Unchanged'] and len(list((repo/'LATEST_INSTALLERS').glob('*.exe')))==1
print(json.dumps({k:receipt[k] for k in ['file','bytes','sha256','zipFilesVerified','installerSha256Unchanged']}))
