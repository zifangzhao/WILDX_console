from pathlib import Path
import hashlib,json,shutil,subprocess,sys,zipfile,xml.etree.ElementTree as ET

repo=Path(__file__).resolve().parents[2]
root=Path(__file__).resolve().parent
app=repo/'_artifacts/preparation_20261028/host'
payload=root/'payload'
release=repo/'ReleaseSources/1.0.0.158'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest().upper()
load=lambda p:json.loads(p.read_text(encoding='utf-8-sig'))
def save(p,x):
    p.parent.mkdir(parents=True,exist_ok=True)
    p.write_text(json.dumps(x,indent=2)+'\n',encoding='utf-8')
def cp(a,b):
    b.parent.mkdir(parents=True,exist_ok=True)
    shutil.copy2(a,b)

if sys.argv[1]=='prepare':
    files=['WILDX_console.exe','WILDX_console.exe.config','Correct-RecordingTime.ps1',
           'NpV2SdDatExport.dll','OpenTK.dll','OpenTK.GLControl.dll',
           'System.Runtime.WindowsRuntime.dll','System.Runtime.InteropServices.WindowsRuntime.dll']
    files=[p for p in files if (app/p).is_file()]
    for folder,name in [('NPConfigurator','NPConfigurator'),('FirmwareUpdater','XeNpFirmwareUpdater')]:
        files += [folder+'/'+name+ext for ext in ['.exe','.dll','.deps.json','.runtimeconfig.json']]
    for name in files:cp(app/name,payload/name)
    for name in ['recording-time-correction.md','imu-time-correction.md']:cp(repo/'docs'/name,payload/'docs'/name)
    ns={'p':'http://schemas.microsoft.com/developer/msbuild/2003'}
    sources=set()
    for name in ['USB512_interface/USB512_interface/WILDX_console.csproj','Neuropixels/SD/NpV2SdDatExport.csproj']:
        project=repo/name;sources.add(project)
        for tag in ['Compile','EmbeddedResource','None']:
            for item in ET.parse(project).findall('.//p:'+tag,ns):
                for inc in item.attrib.get('Include','').split(';'):
                    path=(project.parent/inc.replace('\\','/')).resolve()
                    if path.is_file() and path.is_relative_to(repo):sources.add(path)
    for folder in ['Neuropixels/NPConfigurator','Neuropixels/FirmwareUpdater']:
        for p in (repo/folder).rglob('*'):
            if p.is_file() and not {'bin','obj','_artifacts','Tests'}&set(p.relative_to(repo/folder).parts) and p.suffix in ['.cs','.csproj','.resx','.json']:sources.add(p)
    for name in ['BuildVersion.targets','bump_version.ps1','USB512_interface/WILDX_console.nsi','build.ps1','Tools/Correct-RecordingTime.ps1']:
        sources.add(repo/name)
    ledger=[]
    for p in sorted(sources):
        rel=p.relative_to(repo).as_posix();cp(p,root/'source-freeze'/rel)
        ledger.append({'path':rel,'sha256':sha(p)})
    receipt={'version':'1.0.0.158','gui_sha256':sha(payload/'WILDX_console.exe'),'sdk_sha256':sha(payload/'NpV2SdDatExport.dll'),
             'preparation_enabled':False,'first_sync_enabled':False,'sources':ledger,'hardware_tested':False}
    save(release/'build-receipt.json',receipt)
    print(json.dumps({'prepared_payload_files':len(files)+2,'source_files':len(ledger),'receipt':str(release/'build-receipt.json')}))
elif sys.argv[1]=='package':
    receipt=load(release/'build-receipt.json')
    assert sha(payload/'WILDX_console.exe')==receipt['gui_sha256']
    assert sha(payload/'NpV2SdDatExport.dll')==receipt['sdk_sha256']
    catalog=load(repo/'Firmware/releases.json')
    cp(repo/'Firmware/releases.json',payload/'Firmware/releases.json')
    cp(repo/'Firmware/README.md',payload/'Firmware/README.md')
    for item in catalog['releases']:
        cp(repo/f"Firmware/V{item['family']}/README.md",payload/f"Firmware/V{item['family']}/README.md")
        for f in item['files']:
            source=repo/'Firmware'/f['path'];assert sha(source)==f['sha256'];cp(source,payload/'Firmware'/f['path'])
    for p in (repo/'Firmware/V1Camera').iterdir():
        if p.is_file() and (p.suffix=='.hex' or p.name in ['README.md','release.json']):cp(p,payload/'Firmware/V1Camera'/p.name)
    with zipfile.ZipFile(release/'compiled-sources.zip','w',zipfile.ZIP_DEFLATED) as z:
        for item in receipt['sources']:
            p=root/'source-freeze'/item['path'];assert sha(p)==item['sha256'];z.write(p,item['path'])
    installer=payload/'WILDX_console-Setup-1.0.0.158.exe'
    expected=[{'path':p.relative_to(payload).as_posix(),'bytes':p.stat().st_size,'sha256':sha(p)} for p in sorted(payload.rglob('*')) if p.is_file() and p!=installer]
    assert not any(p['path'].endswith(('.pdb','.zip')) or 'legacy/' in p['path'] for p in expected)
    cp(repo/'USB512_interface/WILDX_console.nsi',release/'installer.nsi')
    if not installer.exists():
        with (root/'nsis.log').open('wb') as log:
            subprocess.run(['C:/Program Files (x86)/NSIS/makensis.exe','/NOCD','/V2','/DAPP_PAYLOAD_DIR='+str(payload),str(release/'installer.nsi')],cwd=repo/'USB512_interface',stdout=log,stderr=subprocess.STDOUT,check=True)
    extracted=root/'extracted158'
    if not extracted.exists():
        with (root/'extract.log').open('wb') as log:
            subprocess.run(['C:/ProgramData/NVIDIA Corporation/NVIDIA App/UpdateFramework/ota-artifacts/nvapp/post-processing/241407d0d505673fd4d654200345ed76/NvApp/7z.exe','x',str(installer),'-o'+str(extracted),'-y'],stdout=log,stderr=subprocess.STDOUT,check=True)
    for item in expected:assert sha(extracted/item['path'])==item['sha256'],item['path']
    manifest={'version':'1.0.0.158','baselineVersion':'1.0.0.151','releaseId':'WILDX158_IMU_FINALIZATION_20261028',
              'guiSha256':receipt['gui_sha256'],'sdkSha256':receipt['sdk_sha256'],'sourceArchiveSha256':sha(release/'compiled-sources.zip'),
              'installerExecuted':False,'hardwareTestedThisUpdate':False,'preparationEnabled':False,'firstSyncEnabled':False,
              'installerPayloadExtractedAndVerified':True,'extractedPayloadFilesVerified':len(expected),
              'standaloneUpdateRequired':False,'separateZipIncluded':False,'allModeOrEnduranceQualified':False,
              'firmware':[{'family':x['family'],'app':x['appCrc32'],'usb':x['usbCrc32'],'stage':x['stage']} for x in catalog['releases']],
              'installer':{'file':installer.name,'bytes':installer.stat().st_size,'sha256':sha(installer)},'payload':expected}
    save(release/'package-verification.json',manifest);cp(repo/'Firmware/releases.json',release/'firmware-catalog.json')
    print(json.dumps({'installer':manifest['installer'],'payload_files_verified':len(expected)}))
else:raise SystemExit('prepare or package required')
