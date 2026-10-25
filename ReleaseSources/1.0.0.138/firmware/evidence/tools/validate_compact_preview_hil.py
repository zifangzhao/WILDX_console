"""Independent full SD export and compact preview/source-bit acceptance."""
import argparse
import csv
import hashlib
import json
from pathlib import Path
import struct
import zlib
from verify_preview_readback import verify

def load(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def kv(p):return dict(x.split('=',1) for x in p.read_text().splitlines() if '=' in x)
def sha(p):
    h=hashlib.sha256()
    with p.open('rb') as f:
        for b in iter(lambda:f.read(1024*1024),b''):h.update(b)
    return h.hexdigest().upper()

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('ble',type=Path);parser.add_argument('readback',type=Path)
    a=parser.parse_args();ble=a.ble.resolve();readback=a.readback.resolve();out=readback/'preview_audit'
    assert not out.exists();out.mkdir()
    stopped=load(ble/'stopped-status.json');status=kv(readback/'export/capture_status.txt')
    assert stopped['State']==3 and stopped['Fault']==0 and stopped['Chunks']>0
    assert status['complete']==status['download_complete']=='True' and status['failure']=='none'
    assert status['probe_mask']=='3' and status['ap_sample_rate_hz']=='20000'
    assert int(status['committed_chunks'])==stopped['Chunks']
    for field in ('stored_fault','missing_saved_imu_publications'):
        assert int(status[field])==0,field
    lengths=[]
    for probe in (1,2):
        for field in ('frame_errors','counter_errors','relocks','lfp_phase_errors'):
            assert int(status[f'np{probe}_{field}'])==0,(probe,field)
        for band in ('ap','lfp'):
            path=readback/f'export/np{probe}_{band}.dat';expected=int(status[f'np{probe}_{band}_rows'])*768
            assert path.stat().st_size==expected;lengths.append(dict(file=path.name,bytes=expected))
    assert status['imu_valid']=='True' and int(status['valid_imu_snapshots'])==stopped['Chunks']
    before=load(ble/'catalog-before.json');after=load(ble/'catalog-after.json')
    assert len(after)==len(before)+1 and after[:-1]==before
    phases=load(ble/'phase-results.json')
    assert [p['phase'] for p in phases]==['preview-off-before','compact-ap','compact-lfp','preview-off-after']
    assert all(p['end_chunks']>p['begin_chunks'] for p in phases)
    assert phases[1]['packets']>0 and phases[2]['packets']>0
    snapshots={str(i):load(ble/f'compact-{name}.json') for i,name in enumerate(('ap','lfp'))}
    groups={};invalid=0;valid=0
    with (ble/'preview-samples.csv').open(newline='') as f:
        for row in csv.DictReader(f):
            assert not row['band'].startswith('#'),'Observer decode error'
            key=(row['band'],int(row['probe']),int(row['channel']))
            assert row['band'] in snapshots and key[1]==1 and key[2] in (0,24,25,383)
            snapshot=snapshots[row['band']]
            assert int(row['session'])==snapshot['session']==int(status['session'])
            assert snapshot['activeChannels']==[0,24,25,383] and snapshot['hz']==20000
            is_valid=int(row['valid'])==1
            if is_valid:valid+=1
            else:invalid+=1
            groups.setdefault(key,[]).append((row['source_tick'],row['raw10'] if is_valid else 'NaN'))
    assert len(groups)==8
    results=[]
    for (band,probe,channel),rows in sorted(groups.items()):
        band_name='ap' if band=='0' else 'lfp';path=out/f'p{probe}_{band_name}_ch{channel}.csv'
        with path.open('w',newline='') as f:
            writer=csv.writer(f);writer.writerow(('source_tick','adc'));writer.writerows(rows)
        result=verify(readback/'extent.bin',path,probe,channel,band_name,int(snapshots[band]['phase']))
        assert result['passed'],result;results.append(result)
    wire=(ble/'ble-notifications.bin').read_bytes();framed=0;crc_errors=0
    for at in range(len(wire)-227):
        if wire[at:at+2]!=b'\xad\xba' or wire[at+226:at+228]!=b'\xda\xda':continue
        body=wire[at+2:at+226]
        if struct.unpack_from('<I',body)[0]!=0x3156504e or (struct.unpack_from('<I',body,28)[0]>>8)&15!=5:continue
        framed+=1;crc_errors+=zlib.crc32(body[:220])!=struct.unpack_from('<I',body,220)[0]
    assert framed>0 and crc_errors==0
    report=dict(result='PASS bounded compact preview and full SD source-bit/export acceptance',
        uid='001D00065942500B20313354',host_version='1.0.0.138',app_crc32='82E51DEA',usb_crc32='4F7B521A',
        session=int(status['session']),committed_chunks=stopped['Chunks'],raw_extent_bytes=(readback/'extent.bin').stat().st_size,
        raw_extent_sha256=sha(readback/'extent.bin'),exact_dat_lengths=lengths,prior_catalog_records_unchanged=len(before),
        phases=phases,preview_valid_samples=valid,preview_explicit_missing_samples=invalid,
        framed_compact_packets=framed,framed_crc_errors=crc_errors,channel_comparisons=results,
        imu_valid_snapshots=int(status['valid_imu_snapshots']),uart_baud=115200,higher_uart_promoted=False,
        limits=['One bounded P12/20k recording; four selected channels on P1, AP then LFP; not all ports/rates or endurance.',
            'Preview remains best effort with explicit gaps; no claim of full-rate spike monitoring or physical ADC synchronization.',
            'Observer r3 exit1 after successful Stop/catalog was post-restore status confirmation across a clock reset; fresh BLE identity/profile confirmation is separate.',
            'No higher UART write/trial: module did not answer documented read queries;115200 unchanged.'])
    (out/'result.json').write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps({k:report[k] for k in ('result','committed_chunks','preview_valid_samples','preview_explicit_missing_samples','framed_compact_packets','framed_crc_errors')},indent=2))

if __name__=='__main__':main()
