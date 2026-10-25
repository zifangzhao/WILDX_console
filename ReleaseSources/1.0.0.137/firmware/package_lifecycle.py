"""Freeze the accepted V1 lifecycle components; no rebuild, hardware or publishing."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[3]
TASK = ROOT / '_artifacts/v1_lifecycle_release_20261025'
OUT = TASK / 'bundle_final'
HOST = Path('D:/Code/WILDX_console/WILDX_console/_artifacts/download_restore_20261025/build137/compile-bin/WILDX_console.exe')
DFU = Path('D:/Code/WILDX_console/WILDX_console/Firmware/V1/V1_DFU_boot_USBF2D80C4F_usb30_startup_20261020.hex')
sys.path.insert(0, str(ROOT / 'Tools/V1Capture/UnifiedUsb'))
from merge_verified_hex import read_intel_hex, write_intel_hex, FLASH, CONFIG, APP_META, USB_META, END


def sha(path):
    h = hashlib.sha256()
    with path.open('rb') as f:
        for block in iter(lambda: f.read(1024*1024), b''):
            h.update(block)
    return h.hexdigest().upper()


def load(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def kv(path):
    return dict(line.split('=', 1) for line in path.read_text().splitlines() if '=' in line)


def save(path, value):
    path.write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8', newline='\n')


def check_usb(folder, hz, mask):
    root = TASK / folder
    r = load(root / 'experiment.json')
    s = kv(root / 'neuropixels_capture_status.txt')
    p = kv(root / 'neuropixels_probes.txt')
    assert r['result'] == 'pass' and r['version'] == '1.0.0.137'
    assert r['uid'] == '001D00065942500B20313354'
    assert r['requested_sample_rate_hz'] == hz and r['requested_probe_mask'] == mask
    assert r['v1_reader_priority'] == 'Highest'
    assert s['complete'] == 'True' and s['failure'] == 'none'
    for name in ('frame_errors', 'counter_errors', 'usb_sequence_gaps', 'raw_pool_drop_events',
                 'raw_pool_dropped_wire_bytes', 'np_packet_gap_events', 'misc_packet_gap_events',
                 'firmware_recording_fault', 'imu_sequence_gaps', 'imu_queue_overflow'):
        assert int(s[name]) == 0, (folder, name)
    assert r['usb_timing_status']['enabled'] and r['usb_timing_status']['error'] is None
    assert r['usb_timing_status']['dropped_records'] == 0 and r['usb_timing_clock_points'] > 0
    lengths = []
    for i in range(1, int(p['probe_count'])+1):
        prefix = f'probe{i}.'
        for key in ('frame_errors','counter_errors','relocks','lfp_phase_errors','missing_ap_rows','missing_lfp_rows','gap_events'):
            assert int(p[prefix+key]) == 0, (folder, prefix+key)
        directory = root / f'probe{i}' if int(p['probe_count']) > 1 else root
        for stem, key, width in (('neuropixels_ap.dat','ap_output_rows',768),
                                 ('neuropixels_lfp.dat','lfp_output_rows',768),
                                 ('time.dat','lfp_output_rows',4)):
            path = directory / stem
            expected = int(p[prefix+key])*width
            assert path.stat().st_size == expected, path
            lengths.append(dict(file=path.relative_to(root).as_posix(), bytes=expected))
    assert (root/'analogin.dat').stat().st_size == int(s['misc_1250_records'])*32
    return dict(folder=folder, rate_hz=hz, mask=mask, seconds=r['requested_seconds'],
                status='PASS bounded USB capture; no measured midstream gaps/errors',
                imu_samples=r['imu_retained_samples'], imu_max_gap_ms=r['imu_max_gap_ms'],
                timing_points=r['usb_timing_clock_points'], exact_dat_lengths=lengths,
                evidence_sha256=sha(root/'experiment.json'))


def main():
    assert not OUT.exists(), 'A frozen bundle must not be overwritten'
    assert sha(HOST) == '7BA5BF055E5A4C3C7CD624DF77306622B1A6A5BD1519894C7450F086EF808ABC'
    runs = [check_usb('usb108-20k',20000,3), check_usb('usb108-30k',30000,1)]
    sd = load(TASK/'app-readback137-final/anchor_validation.json')
    assert sd['result'].startswith('PASS') and sd['chunks'] == 264
    assert sd['prior_records_unchanged'] == 34 and sd['all_records'] == 35
    # Compare all actual saved data and binary recording metadata across the
    # earlier full readback and the final USB4F/host137 readback, not just counters.
    old, new = TASK/'app-readback/export', TASK/'app-readback137-final/export'
    old_files = {p.relative_to(old).as_posix():p for p in old.rglob('*') if p.is_file() and p.suffix in ('.dat','.bin')}
    new_files = {p.relative_to(new).as_posix():p for p in new.rglob('*') if p.is_file() and p.suffix in ('.dat','.bin')}
    assert old_files.keys() == new_files.keys() and old_files
    equivalent = []
    for name, path in new_files.items():
        digest = sha(path)
        assert digest == sha(old_files[name]), name
        equivalent.append(dict(file=name, bytes=path.stat().st_size, sha256=digest))
    usb_id = load(TASK/'usb_recovery_sealed/identity.json')
    app_id = load(TASK/'app_sealed/identity.json')
    assert usb_id['crc32'] == '4F7B521A' and app_id['crc32'] == 'CC50D8D2'
    for ident in (usb_id,app_id):
        assert sha(Path(ident['image'])) == ident['sha256']
        assert ident['known_stack_bytes'] < ident['stack_allocation_bytes']
        assert all(r['size'] <= r['maximum'] for r in ident['execution_regions'])
    assert sha(DFU) == 'BE9122F07CDA4169A7E8C49B0D40A6B033FB4648D362A5F3A299DE752C8C7E3B'
    original = read_intel_hex(DFU)
    boot = {a:b for a,b in original.items() if FLASH <= a < CONFIG}
    usb = read_intel_hex(Path(usb_id['image']))
    assert boot and usb and all(USB_META <= a < END for a in usb)
    assert not boot.keys() & usb.keys()
    archive = load(TASK/'source_archive/verification.json')
    assert archive['result'].startswith('PASS'), archive
    assert sha(Path(archive['zip'])) == archive['sha256']
    for receipt in (ROOT/'_artifacts/v1_ble_remaining_20261025/ce32_stop_final/result.json',
                    ROOT/'_artifacts/v1_sensor_lifecycle_20261025/after/result.json',
                    TASK/'app_native_check/r2/result.json'):
        check = load(receipt)
        assert check['compile_exit'] == check['test_exit'] == 0
    assert load(TASK/'usb_recovery_native/result.json')['result'].startswith('PASS')
    OUT.mkdir()
    u = OUT/'V1_USB_service_1.0.108_USB4F7B521A_lifecycle_20261025.hex'
    a = OUT/'V1_APP_CC50D8D2_lifecycle_20261025.hex'
    d = OUT/'V1_DFU_boot_USB4F7B521A_lifecycle_20261025.hex'
    f = OUT/'V1_fused_BOOT_USB4F7B521A_APPCC50D8D2_lifecycle_20261025.hex'
    shutil.copyfile(usb_id['image'],u)
    shutil.copyfile(app_id['image'],a)
    write_intel_hex(d,{**boot,**usb})
    d.write_bytes(d.read_bytes().replace(b'\r\n',b'\n'))
    merge_tool = ROOT/'Tools/V1Capture/UnifiedUsb/merge_verified_hex.py'
    subprocess.run([sys.executable,'-B',str(merge_tool),str(d),str(a),str(f)],check=True)
    fused = read_intel_hex(f)
    assert not any(CONFIG <= x < APP_META for x in fused)
    assert all(fused[x] == b for x,b in {**boot,**usb,**read_intel_hex(a)}.items())
    assert sha(u) == usb_id['sha256'] and sha(a) == app_id['sha256']
    limits = [
        'Bounded production-API tests, not full installed GUI, endurance, all-mode or physical ADC timing qualification.',
        'USB P12/20k70s and P1/30k60s passed on exact final components/host137. Other modes were not requalified.',
        'BLE/SD APPCC50 P12/15k short Start/Stop and full readback passed; final USB4F/host137 readback is byte-identical. No new 20k SD endurance claim.',
        'APP is a disclosed maintained private5D66-derived alternative; exact missing published654 control preimage was not recovered.',
        'Unclaimed native20k retention recovery remains best effort, with unknown-gap counters and host gap journaling; claimed-stage/DMA/ownership faults remain hard.',
        'Earlier USB412D BADA000A/BADA0013 failures retained; final4F short passes do not establish the general BADA000A cause or guaranteed lossless capture.',
        'Startup trimming and incomplete trailing bytes remain explicit. IMU is timestamped latest-value polling, not FIFO-lossless acquisition; environment error7 remains reported.',
        'R2/UTC2 are digital software clock evidence under stated bounds, not physical ADC/filter synchronization.',
        '43.5198 MB/s native payload export measured on USB412D/host137; final4F reuses unchanged download objects and passed small byte-identical readback, not another speed benchmark.',
        'No V2, camera, audio, power, cable-fault or interrupted-storage qualification in this request.',
        'Archive preserves admitted source bytes; unchanged linked objects are identified, not rebuilt; proprietary toolchain inputs are external references.'
    ]
    acceptance = dict(result='PASS bounded final V1 pair acceptance', uid='001D00065942500B20313354',
        host_version='1.0.0.137',host_sha256=sha(HOST),usb_crc32=usb_id['crc32'],app_crc32=app_id['crc32'],
        runs=runs,ble_sd=sd,final_sd_binary_equivalence=equivalent,
        final_setup='P12/20k selected, stopped idle USB download; APP original P12/15k restored/preserved',
        boot_configuration_options_preserved=True,full_installed_flash_sha256='C15739D30A83AF71DF891DF9DD709D73319A3A6BB07A5C1DAF29226F81AE1F5F',
        limits=limits,published=False)
    save(OUT/'acceptance.json',acceptance)
    for ident,name in ((usb_id,'usb-identity.json'),(app_id,'app-identity.json')):
        save(OUT/name,ident)  # original sealing snapshot; later HIL is in acceptance.json
    evidence=[]
    directories=['usb108-20k','usb108-30k','ble-app-check','app-readback137-final',
                 'usb20-first','usb20-diagnostic','usb20-baseline40ab-r2','usb20-gui-priority']
    for directory in directories:
        for p in (TASK/directory).rglob('*'):
            if p.is_file() and (p.suffix in ('.json','.txt') or p.name in ('usb_timing_evidence.jsonl','acquisition_clock_queries.jsonl')):
                evidence.append(p)
    evidence += [TASK/p for p in ('download-comparison.json','usb_recovery_native/result.json',
        'app_native_check/r2/result.json','app_build/build.json','usb_recovery_build/build.json','usb_build/build.json')]
    evidence += [ROOT/p for p in ('_artifacts/v1_ble_remaining_20261025/ce32_stop_final/result.json',
        '_artifacts/v1_sensor_lifecycle_20261025/after/result.json','_artifacts/v1_sensor_lifecycle_20261025/existing_suite/result.json')]
    for p in evidence:
        rel = p.relative_to(ROOT)
        target = OUT/'evidence'/rel
        target.parent.mkdir(parents=True,exist_ok=True)
        shutil.copyfile(p,target)
    for p in (TASK/'source_archive').iterdir():
        if p.is_file() and p.suffix in ('.zip','.json'):
            shutil.copyfile(p,OUT/p.name)
    shutil.copyfile(Path(__file__),OUT/'package_lifecycle.py')
    files=[dict(path=p.relative_to(OUT).as_posix(),bytes=p.stat().st_size,sha256=sha(p)) for p in sorted(OUT.rglob('*')) if p.is_file()]
    bundle=dict(release='V1_LIFECYCLE_USB4F7B521A_APPCC50D8D2_20261025',stage='CHECKED_COMPONENTS_DIRECT_FUSED_READY_FOR_WILDX_PUBLICATION',
        files=files,usb_crc32='4F7B521A',app_crc32='CC50D8D2',usb_version='1.0.108',
        app_version='build CC50D8D2 (SD/BLE lifecycle + UTC2)',host_version='1.0.0.137',
        merge_tool_sha256=sha(merge_tool),configuration_excluded=True,fused_lf_normalized=True,
        hardware_access_during_packaging=False,rebuilt=False,resealed=False,published=False,limits=limits)
    save(OUT/'bundle.json',bundle)
    print(json.dumps(dict(bundle=str(OUT),manifest_sha256=sha(OUT/'bundle.json'),
        fused=str(f),fused_sha256=sha(f),file_count=len(files)),indent=2))


if __name__=='__main__':
    main()
