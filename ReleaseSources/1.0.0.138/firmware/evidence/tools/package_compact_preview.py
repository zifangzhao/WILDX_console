"""Directly package the checked compact-preview APP with unchanged checked USB/boot."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys

ROOT=Path(__file__).resolve().parents[3]
TASK=ROOT/'_artifacts/v1_ble_compact_preview_20261025'
PARENT=ROOT/'_artifacts/v1_lifecycle_release_20261025/bundle_final'
OUT=TASK/'bundle_final'
sys.path.insert(0,str(ROOT/'Tools/V1Capture/UnifiedUsb'))
from merge_verified_hex import read_intel_hex,CONFIG,APP_META
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest().upper()
def load(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def save(p,x):p.write_text(json.dumps(x,indent=2)+'\n',encoding='utf-8',newline='\n')
def cp(p,q):q.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(p,q)

def main():
    assert not OUT.exists()
    assert sha(PARENT/'bundle.json')=='B58F4806BD274418CE04968664ED4FE315A1189A0BCB24D64593278F180DFB29'
    parent=load(PARENT/'bundle.json');app=load(TASK/'app_sealed/identity.json')
    assert app['crc32']=='82E51DEA' and sha(Path(app['image']))==app['sha256']
    acceptance=load(TASK/'readback/preview_audit/result.json')
    assert acceptance['result'].startswith('PASS') and acceptance['preview_valid_samples']==27163
    assert acceptance['framed_crc_errors']==0 and acceptance['higher_uart_promoted'] is False
    host=Path('D:/Code/WILDX_console/WILDX_console/_artifacts/v1_ble_compact_20261025/build138/compile-bin/WILDX_console.exe')
    assert sha(host)=='6E1A2E88B98E6DC55A2733082BECD8496913F91DA99A5C4A86876BC195B8DB86'
    assert sha(TASK/'hil/app_plan/flash_after_install.bin')=='01CC90B06C44B2B52962DD214AB82C4F9600F06F13BB11D4BCCE7F21F2B66A75'
    assert sha(TASK/'hil/options_before.bin')==sha(TASK/'hil/app_plan/options_after_install.bin')
    restored=(TASK/'hil/post-restore-return/events.log').read_text()
    assert 'uid_verified=001D00065942500B20313354' in restored and 'USB_return_ack=true' in restored
    assert '"Hz":15000' in restored and '"Fault":0' in restored and 'observer_completed=true' in restored
    archive=load(TASK/'source_archive/verification.json')
    assert archive['result'].startswith('PASS') and sha(Path(archive['zip']))==archive['sha256']
    query=load(TASK/'uart_query/result.json')
    assert not query['module_configuration_written'] and not query['typed_module_response'] and query['debugger_disabled']
    OUT.mkdir()
    inherited=[]
    for marker in ('V1_DFU_boot_','V1_USB_service_'):
        item=next(x for x in parent['files'] if x['path'].startswith(marker))
        p=PARENT/item['path'];assert sha(p)==item['sha256'];cp(p,OUT/p.name);inherited.append(OUT/p.name)
    new_app=OUT/'V1_APP_82E51DEA_compact4_preview_20261025.hex';cp(Path(app['image']),new_app)
    fused=OUT/'V1_fused_BOOT_USB4F7B521A_APP82E51DEA_compact4_preview_20261025.hex'
    merge=ROOT/'Tools/V1Capture/UnifiedUsb/merge_verified_hex.py'
    subprocess.run([sys.executable,'-B',str(merge),str(inherited[0]),str(new_app),str(fused)],check=True)
    memory=read_intel_hex(fused);expected={**read_intel_hex(inherited[0]),**read_intel_hex(new_app)}
    assert not any(CONFIG<=a<APP_META for a in memory) and all(memory[a]==b for a,b in expected.items())
    assert b'\r' not in fused.read_bytes()
    cp(TASK/'app_sealed/identity.json',OUT/'app-identity.json');cp(PARENT/'usb-identity.json',OUT/'usb-identity.json')
    acceptance.update(host_sha256=sha(host),source_archive_sha256=archive['sha256'],
        profile_restored_and_confirmed_on_fresh_ble_connection=True,final_setup='stopped idle USB download; APP P12/15k restored; USB unchanged P12/20k',
        firmware_unchanged_boot_usb_configuration_options=True,firmware_rebuilt_during_packaging=False,published=False)
    acceptance['limits'] += ['USB streaming and speed evidence inherited unchanged from137/USB4F; not repeated for the APP-only preview change.',
        'Faster UART remains blocked on confirmed module configuration access/rollback semantics; no higher-rate claim.',
        'Source archive preserves admitted inputs and object identities; no full clean-rebuild claim.']
    save(OUT/'acceptance.json',acceptance)
    for folder in ('hil/compact-sd-r1','hil/compact-sd-r3','hil/post-restore-return','uart_query'):
        for p in (TASK/folder).iterdir():
            if p.is_file() and p.suffix in ('.json','.log','.png'):cp(p,OUT/'evidence'/p.relative_to(TASK))
    for path in ('readback/result.json','readback/export/capture_status.txt','readback/preview_audit/result.json',
                 'app_build/build.json','app_source/source_manifest.json','build_preparation.json'):
        cp(TASK/path,OUT/'evidence'/path)
    native=dict(result='PASS actual compact header and legacy regression',checks=12170,packets=121,
        fixture_sha256=sha(ROOT/'Tools/V1Capture/Ble/test_compact_preview.c'),header_sha256=sha(TASK/'app_source/Core/Inc/np_ble_preview.h'),
        vectors_sha256=sha(TASK/'native/vectors.bin'),compiler='MSVC /std:c11 /O2 /W4 /WX; execution transcript retained in originating chat',hardware_access=False)
    save(OUT/'evidence/native-result.json',native)
    for p in (TASK/'source_archive').iterdir():
        if p.is_file() and p.suffix in ('.json','.zip'):cp(p,OUT/p.name)
    for p in (Path(__file__),ROOT/'Tools/V1Capture/Ble/validate_compact_preview_hil.py',ROOT/'Tools/V1Capture/Timing/LifecycleSdReadback.cs'):
        cp(p,OUT/'evidence/tools'/p.name)
    files=[dict(path=p.relative_to(OUT).as_posix(),bytes=p.stat().st_size,sha256=sha(p)) for p in sorted(OUT.rglob('*')) if p.is_file()]
    save(OUT/'bundle.json',dict(release='V1_COMPACT4_PREVIEW_USB4F7B521A_APP82E51DEA_20261025',
        stage='BOUNDED_HARDWARE_ACCEPTED_DIRECT_FUSED_READY_FOR_WILDX_PUBLICATION',files=files,
        app_crc32='82E51DEA',usb_crc32='4F7B521A',app_version='build82E51DEA (compact four-channel BLE preview)',usb_version='1.0.108',host_version='1.0.0.138',
        merge_tool_sha256=sha(merge),fused_lf_normalized=True,configuration_excluded=True,
        hardware_access_during_packaging=False,rebuilt=False,resealed=False,published=False,limits=acceptance['limits']))
    print(json.dumps(dict(bundle=str(OUT),manifest_sha256=sha(OUT/'bundle.json'),fused=str(fused),fused_sha256=sha(fused),files=len(files)),indent=2))

if __name__=='__main__':main()
