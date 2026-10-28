from pathlib import Path
import hashlib,json,shutil,struct,zipfile,zlib
repo=Path(__file__).resolve().parents[2]
release=repo/'ReleaseSources/1.0.0.158/firmware'
load=lambda p:json.loads(p.read_text(encoding='utf-8-sig'))
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest().upper()
def cp(a,b):b.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(a,b)
def save(p,x):p.write_text(json.dumps(x,indent=2)+'\n',encoding='utf-8')
catalog=load(repo/'Firmware/releases.json')
assert [x['appCrc32'] for x in catalog['releases']]==['82E51DEA','C250DF48']
v1root=Path('C:/Frank/Code/XE_NP/_artifacts/v1_imu_fast_release_20261028')
v2root=Path('D:/Code/XE_NP_V2/_artifacts/v2_imu_stop_fast_20261028')
v1=load(v1root/'package/catalog_entry.json')
v2=load(v2root/'package/candidate.json')
assert v1['appCrc32']=='B1758369' and v2['app_crc32']=='EC5C2198'
for name,f in v2['files'].items():
    p=v2root/'package'/name;assert sha(p)==f['sha256'];cp(p,repo/'Firmware/V2'/name)
image=Path(v2['app_update']).read_bytes();h,body=image[:128],image[128:]
u=lambda at:struct.unpack_from('<I',h,at)[0]
assert u(124)==zlib.crc32(h[:124]) and u(32)==zlib.crc32(body)
assert len(body)==u(28)==v2['app_bytes'] and sha(Path(v2['app_update']))==v2['files'][Path(v2['app_update']).name]['sha256']
assert u(16)==0x41505031 and u(24)==0x08020000 and u(80)==4 and u(20)==4
entry=catalog['releases'][1]
entry['previousApps']=[entry['appIdentity']]+entry['previousApps']
entry.update(release='V2_IMU_STOP_FAST_APPEC5C2198_USBA71BDECC_20261028',stage=v2['stage'],
             appVersion='build EC5C2198',appCrc32='EC5C2198')
entry['appIdentity']={'imageId':u(16),'baseAddress':u(24),'bytes':u(28),'codeCrc32':f'{u(32):08X}',
                      'buildIdHex':h[44:76].hex().upper(),'manifestFlags':u(76),'minimumLoaderAbi':u(80),'generation':u(20)}
asset=lambda p:{'path':'V2/'+Path(p).name,'sha256':sha(Path(p))}
entry['app']=asset(v2['app_update']);entry['full']=asset(v2['fused_hex'])
entry['files']=[{'path':'V2/'+name,'sha256':f['sha256']} for name,f in v2['files'].items()]
entry['limits']='APP IMU/SD finalization software-checked update; no new hardware test. APP AG200Hz/MAG100Hz with sparse SNS2 SD snapshots. Essential drain/footer/catalog retained; automatic full postrecord payload reread removed, PC verification retained. USB/boot unchanged (USB MAG25Hz). Prearm/NPSTART/closed-loop firmware excluded.'
for family,root,files in [(1,v1root,['build.json','package/handoff.json','package/imu_candidate_acceptance.json','package/catalog_entry.json']),
                          (2,v2root,['source-manifest.json','app/memory-audit.json','app.diff','writer.diff','package/candidate.json'])]:
    dest=release/f'V{family}';dest.mkdir(parents=True,exist_ok=True)
    for name in files:cp(root/name,dest/Path(name).name)
    source=root/('source' if family==1 else 'sources')
    with zipfile.ZipFile(dest/'source-overlay.zip','w',zipfile.ZIP_DEFLATED) as z:
        for p in sorted(source.rglob('*')):
            if p.is_file():z.write(p,p.relative_to(source).as_posix())
    current=v1 if family==1 else entry
    current['sourceCommitScope']='Retained production baseline commit; exact hotfix overlay and retained-object provenance are bound by sourceInputs.'
    current['sourceInputs']={'baselineRelease':'APP82E51DEA' if family==1 else 'APPC250DF48',
                            'sourceOverlaySha256':sha(dest/'source-overlay.zip'),
                            'evidence':f'ReleaseSources/1.0.0.158/firmware/V{family}'}
v1['appVersion']='build B1758369'
catalog['releases']=[v1,entry]
save(repo/'Firmware/releases.json',catalog)
save(release/'catalog.json',catalog)
print(json.dumps({'V1':v1['appCrc32'],'V2':entry['appCrc32'],'firmware_files':sum(len(x['files']) for x in catalog['releases'])}))
