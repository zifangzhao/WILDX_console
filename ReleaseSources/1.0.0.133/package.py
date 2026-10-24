from pathlib import Path
import hashlib, json, shutil, subprocess, sys, zipfile

repo = Path(__file__).resolve().parents[2]
root = Path(__file__).resolve().parent
build = repo / '_artifacts/known_issues_20261023/build133'
evidence = build.parent
release = repo / 'ReleaseSources/1.0.0.133'
latest = repo / 'LATEST_INSTALLERS'
version = '1.0.0.133'
sha = lambda p: hashlib.sha256(Path(p).read_bytes()).hexdigest().upper()
load = lambda p: json.loads(Path(p).read_text(encoding='utf-8-sig'))

def save(p, data):
    p.write_text(json.dumps(data, indent=2) + '\n', encoding='utf-8')

def copy(source, target):
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, target)

resume = '--resume' in sys.argv
assert resume or not release.exists()
assert not subprocess.check_output(['git', 'diff', '--cached', '--name-only'], cwd=repo).strip()
old = load(latest / 'release-manifest.json')
assert old['version'] == '1.0.0.128'
receipt = load(evidence / 'build133-receipt.json')
assert sha(build / 'compile-bin/WILDX_console.exe') == receipt['gui_sha256'].upper()
assert sha(build / 'compile-bin/NpV2SdDatExport.dll') == receipt['sdk_sha256'].upper()
payload = root / 'app'
payload.mkdir(exist_ok=resume)
for entry in old['payload']:
    source = repo / '_artifacts/release_1.0.0.128/app' / entry['path']
    assert sha(source) == entry['sha256'], entry['path']
    copy(source, payload / entry['path'])
for name in ['WILDX_console.exe', 'WILDX_console.exe.config', 'NpV2SdDatExport.dll', 'Correct-RecordingTime.ps1']:
    copy(build / 'compile-bin' / name, payload / name)

release.mkdir(parents=True, exist_ok=resume)
sdk_receipt = load(repo / '_artifacts/sample_time_closure_20261023/build115-receipt.json')
assert sdk_receipt['sdk_sha256'].upper() == receipt['sdk_sha256'].upper()
ledger = []
with zipfile.ZipFile(release / 'compiled-sources.zip', 'w', zipfile.ZIP_DEFLATED) as z:
    for component, entries in [('GUI', receipt['gui_sources']), ('SDK', sdk_receipt['sdk_sources'])]:
        for i, entry in enumerate(entries):
            source = Path(entry.get('path', entry.get('source', '')))
            assert sha(source) == entry['sha256'].upper()
            member = f'{component.lower()}/{i:03d}_{source.name}'
            z.write(source, member)
            ledger.append({'archive': member, 'compiler_input': str(source), 'sha256': sha(source), 'component': component})
    for source in [build / 'ui-only.targets', evidence / 'build133.ps1', evidence / 'prepare.py',
                   repo / '_artifacts/sample_time_closure_20261023/build115.ps1',
                   repo / 'USB512_interface/USB512_interface/WILDX_console.csproj',
                   repo / 'BuildVersion.targets']:
        z.write(source, 'build/' + source.name)
save(release / 'source-inputs.json', ledger)
copy(evidence / 'build133-receipt.json', release / 'build-receipt.json')
copy(build / 'reference-inputs.txt', release / 'reference-inputs.txt')
copy(build / 'gui-build.log', release / 'build.log')
for name in ['background-final.log', 'usb-lifecycle133.log', 'imu133.log', 'clock133.log']:
    copy(evidence / name, release / 'checks' / name)
copy(repo / '_artifacts/ble_clock_evidence_20261023/build132-receipt.json', release / 'checks/build132-receipt.json')
copy(repo / '_artifacts/v2_ble_imu_20261023/build130-receipt.json', release / 'checks/build130-receipt.json')
for relative in ['Tests/NpBleBackgroundLoopTests.cs', 'Tests/NpBleImuTests.cs',
                 'Tests/NpBleClockEvidenceTests.cs', 'Neuropixels/Tests/TestNp2UsbImuLifecycle.cs',
                 'test_ble_background_loop.ps1', 'test_ble_imu.ps1', 'test_ble_clock_evidence.ps1', 'test.ps1',
                 'Note/IMU_BACKGROUND_POLLING_FIX_20261023.md', 'Note/BLE_V2_IMU_PROTOCOL_20261023.md',
                 'Note/BLE_SD_CLOCK_EVIDENCE_20261023.md']:
    copy(repo / relative, release / 'support' / relative)
copy(repo / 'ReleaseSources/1.0.0.128/installer.nsi', release / 'installer.nsi')
expected = [{'path': p.relative_to(payload).as_posix(), 'bytes': p.stat().st_size, 'sha256': sha(p)}
            for p in sorted(payload.rglob('*')) if p.is_file()]
with (root / 'nsis.log').open('wb') as log:
    subprocess.run(['C:/Program Files (x86)/NSIS/makensis.exe', '/NOCD', '/V2',
        '/DAPP_PAYLOAD_DIR=' + str(payload), str(release / 'installer.nsi')],
        cwd=repo / 'USB512_interface', stdout=log, stderr=subprocess.STDOUT, check=True)
installer = payload / f'WILDX_console-Setup-{version}.exe'
with (root / 'extract.log').open('wb') as log:
    subprocess.run(['C:/ProgramData/NVIDIA Corporation/NVIDIA App/UpdateFramework/ota-artifacts/nvapp/post-processing/241407d0d505673fd4d654200345ed76/NvApp/7z.exe',
        'x', str(installer), '-o' + str(root / 'extracted'), '-y'], stdout=log, stderr=subprocess.STDOUT, check=True)
for entry in expected:
    assert sha(root / 'extracted' / entry['path']) == entry['sha256'], entry['path']
actual = {p.relative_to(root / 'extracted').as_posix() for p in (root / 'extracted').rglob('*') if p.is_file()}
assert actual - {x['path'] for x in expected} == {'$PLUGINSDIR/System.dll', '$PLUGINSDIR/nsDialogs.dll', '$PLUGINSDIR/modern-wizard.bmp'}
for entry in old['payload']:
    if entry['path'] != 'WILDX_console.exe':
        assert sha(payload / entry['path']) == entry['sha256'], entry['path']

manifest = dict(version=version, releaseId='WILDX133_BLE_IMU_POLLING_20261024',
    baselineVersion=old['version'], applicationRebuiltDuringPackaging=False,
    guiSha256=sha(payload / 'WILDX_console.exe'), sdkSha256=sha(payload / 'NpV2SdDatExport.dll'),
    sourceArchiveSha256=sha(release / 'compiled-sources.zip'), firmwareChanged=False,
    firmwareCatalogChanged=False, helpersChanged=False, installerExecuted=False,
    installerPayloadExtractedAndVerified=True, extractedPayloadFilesVerified=len(expected),
    scope='BLE coherent IMU snapshot support, IMU priority/retry, serialized background polling, stale display, and raw periodic clock evidence. Frozen checked build133; unrelated working-tree edits excluded.',
    timingFirmwareIncluded=False, timingFirmwareRequiredForHighResolutionNeuralBounds=True,
    existingFirmwareSupported=True, bleImuRequiresCompatibleFirmware=True,
    bleImuIsSparseSnapshotStream=True, apDriftCorrectionImplemented=False,
    physicalAdcGpioAlignmentQualified=False, simultaneousBleWaveformImuHardwareQualified=False,
    v2Sd292IndependentReadbackQualified=False,
    softwareChecks=receipt['checks'],
    installer={'file': installer.name, 'bytes': installer.stat().st_size, 'sha256': sha(installer)}, payload=expected)
save(release / 'package-verification.json', manifest)
(release / 'README.md').write_text(
    '# WILD-X 1.0.0.133\n\n'
    'Exact software-checked build133 and reused SDK115 inputs are retained in compiled-sources.zip and source-inputs.json. '
    'Packaging did not recompile the application. Unrelated working-tree changes are excluded.\n\n'
    'Since128: coherent BLE IMU snapshots with compatible firmware; retries after temporary failures; '
    'IMU priority above optional clock observations and waveform requests; serialized background timer callbacks; '
    'stale IMU display after3s without a fresh observation; read-only periodic raw MCU/PC clock evidence for V1/V2. '
    'Clock evidence collection does not implement AP drift correction. Existing NP recording workers and decoders are unchanged.\n\n'
    'Existing firmware remains supported. Bundled firmware, helpers, SDK and all payload files except the GUI are byte-identical to128. '
    'The optional high-resolution USB timing images and private V2 IMU APP CBAAA2B5 are not bundled. '
    'Older firmware without BLE IMU support will not gain it from a console update alone.\n\n'
    'Validation:5 checks reproduce the prior overlap;22 actual-source background scheduling/freshness checks, '
    '5 compiled USB IMU lifecycle checks,366 compiled BLE IMU checks and43 compiled clock-evidence checks pass. '
    'Earlier preview regression checks also passed; see retained receipts. '
    f'All {len(expected)} installer payload files were extracted and hash-verified. The installer was not executed.\n\n'
    'Earlier V2 firmware129 HIL received6 valid coherent IMU views while recording P123 at20kHz and finalized session292 with fault/error0. '
    'This is not lossless200Hz IMU transport. Full independent SD292 NP readback remains pending physical V2 access; '
    'simultaneous waveform/IMU operation and physical NP/GPIO alignment remain unqualified. '
    'The133 scheduling changes have software checks only. No hardware was accessed during packaging.\n', encoding='utf-8')

archive = latest / 'legacy/20261024_before_1.0.0.133'
assert not archive.exists()
archive.mkdir(parents=True)
assert archive.resolve().is_relative_to(latest.resolve())
for source in list(latest.iterdir()):
    if source.is_file():
        assert source.resolve().parent == latest.resolve()
        shutil.move(str(source), str(archive / source.name))
copy(installer, latest / installer.name)
for name in ['release-manifest.json', 'WILDX_release-manifest.json']: save(latest / name, manifest)
(latest / 'SHA256SUMS.txt').write_text(f'{sha(installer)}  {installer.name}\n', encoding='utf-8')
(latest / 'README.md').write_text(
    f'# WILD-X {version}\n\n[Download installer]({installer.name}).\n\n'
    'Fixes overlapping BLE background polls and stale IMU readings, prioritizes IMU over waveform preview, '
    'and saves raw periodic clock observations. Includes the PC-time correction and SD-test workflow from128.\n\n'
    'Existing firmware remains supported. BLE IMU needs compatible device firmware; the private V2 IMU APP and '
    'high-resolution timing firmware are not bundled. AP drift correction and joint physical NP/GPIO timing remain under development.\n\n'
    f'All {len(expected)} packaged files were extracted and hash-verified. '
    '[Manifest](release-manifest.json) / [source and validation](../ReleaseSources/1.0.0.133/README.md).\n', encoding='utf-8')
(latest / 'WILDX_V2_README.md').write_text(
    f'# WILD-X / V2 bundle\n\nUse [WILD-X {version}]({installer.name}). '
    'Existing V2 firmware remains supported. Firmware is unchanged from128; private IMU/timing test images are excluded.\n', encoding='utf-8')
attrs = repo / '.gitattributes'
attrs.write_bytes(attrs.read_bytes() + b'\n/LATEST_INSTALLERS/legacy/20261024_before_1.0.0.133/** -text\n/ReleaseSources/1.0.0.133/** -text\n')
readme = repo / 'README.md'
text = readme.read_text(encoding='utf-8-sig')
start = text.index('Independent Windows console')
end = text.index('The application uses', start)
text = text[:start] + ('Independent Windows console for Neuropixels devices. Current published version **1.0.0.133**.\n\n'
    '[Latest installer](LATEST_INSTALLERS/README.md) includes BLE IMU polling/freshness fixes and raw clock evidence collection. '
    'Existing firmware remains supported; compatible device firmware is required for BLE IMU. '
    '[Release details](ReleaseSources/1.0.0.133/README.md).\n\n') + text[end:]
readme.write_text(text, encoding='utf-8')
assert len(list(latest.glob('*.exe'))) == 1
print(json.dumps({'version': version, 'installer': manifest['installer'], 'payloadFilesVerified': len(expected)}, indent=2))
