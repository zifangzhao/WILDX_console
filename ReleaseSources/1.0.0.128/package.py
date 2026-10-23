from pathlib import Path
import hashlib, json, shutil, subprocess, sys, zipfile

repo = Path(__file__).resolve().parents[2]
root = Path(__file__).resolve().parent
build = repo / '_artifacts/imu_sample_time_20261023/build128'
evidence = build.parent
release = repo / 'ReleaseSources/1.0.0.128'
latest = repo / 'LATEST_INSTALLERS'
version = '1.0.0.128'
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
assert old['version'] == '1.0.0.105'
receipt = load(evidence / 'build128-receipt.json')
assert sha(build / 'compile-bin/WILDX_console.exe') == receipt['gui_sha256'].upper()
assert sha(build / 'compile-bin/NpV2SdDatExport.dll') == receipt['sdk_sha256'].upper()
payload = root / 'app'
payload.mkdir(exist_ok=resume)
for entry in old['payload']:
    source = repo / '_artifacts/release_1.0.0.105/app' / entry['path']
    assert sha(source) == entry['sha256'], entry['path']
    copy(source, payload / entry['path'])
for name in ['WILDX_console.exe', 'WILDX_console.exe.config', 'NpV2SdDatExport.dll', 'Correct-RecordingTime.ps1']:
    copy(build / 'compile-bin' / name, payload / name)
for name in ['recording-time-correction.md', 'imu-time-correction.md']:
    copy(repo / 'docs' / name, payload / 'docs' / name)

# Preserve the exact inputs used by the checked application and its reused SDK.
release.mkdir(parents=True, exist_ok=resume)
sdk_receipt = load(repo / '_artifacts/sample_time_closure_20261023/build115-receipt.json')
assert sdk_receipt['sdk_sha256'].upper() == receipt['sdk_sha256'].upper()
ledger = []
with zipfile.ZipFile(release / 'compiled-sources.zip', 'w', zipfile.ZIP_DEFLATED) as z:
    for i, entry in enumerate(receipt['gui_sources']):
        source = Path(entry['path'])
        assert sha(source) == entry['sha256'].upper()
        member = f'gui/{i:03d}_{source.name}'
        z.write(source, member)
        ledger.append({'archive': member, 'compiler_input': str(source), 'sha256': sha(source), 'component': 'GUI'})
    for i, entry in enumerate(sdk_receipt['sdk_sources']):
        source = Path(entry.get('path', entry.get('source', '')))
        assert sha(source) == entry['sha256'].upper()
        member = f'sdk/{i:03d}_{source.name}'
        z.write(source, member)
        ledger.append({'archive': member, 'compiler_input': str(source), 'sha256': sha(source), 'component': 'SDK'})
    for source in [build / 'ui-only.targets', evidence / 'build128.ps1',
                   repo / '_artifacts/sample_time_closure_20261023/build115.ps1',
                   repo / 'USB512_interface/USB512_interface/WILDX_console.csproj',
                   repo / 'USB512_interface/BuildVersion.targets']:
        if source.exists(): z.write(source, 'build/' + source.name)
save(release / 'source-inputs.json', ledger)
copy(evidence / 'build128-receipt.json', release / 'build-receipt.json')
copy(build / 'reference-inputs.txt', release / 'reference-inputs.txt')
copy(build / 'gui-build.log', release / 'build.log')
for source, target in [
    (evidence / 'independent-verification.json', 'checks/imu-independent-verification.json'),
    (evidence / 'REPORT.txt', 'checks/imu-report.txt'),
    (repo / '_artifacts/first_sample_precision_20261023/REPORT.txt', 'checks/np-timing-report.txt'),
    (repo / '_artifacts/first_sample_precision_20261023/independent-verification.json', 'checks/np-independent-verification.json'),
    (evidence / 'script-replay128.log', 'checks/script-replay.log'),
    (evidence / 'neural-regressions.log', 'checks/neural-regressions.log')]:
    if source.exists(): copy(source, release / target)
for relative in ['Tools/Correct-RecordingTime.ps1', 'Tests/NpImuTimeCorrectionTests.cs', 'test_imu_time.ps1',
                 'docs/imu-time-correction.md', 'docs/recording-time-correction.md']:
    copy(repo / relative, release / 'support' / relative)
recipe = (repo / 'USB512_interface/WILDX_console.nsi').read_text(encoding='utf-8-sig')
recipe = recipe.replace('  WriteUninstaller', '  SetOutPath "$INSTDIR\\docs"\n  File "${APP_PAYLOAD_DIR}\\docs\\*.md"\n  SetOutPath "$INSTDIR"\n  WriteUninstaller')
recipe = recipe.replace('  Delete "$INSTDIR\\Uninstall.exe"', '  Delete "$INSTDIR\\docs\\recording-time-correction.md"\n  Delete "$INSTDIR\\docs\\imu-time-correction.md"\n  RMDir "$INSTDIR\\docs"\n  Delete "$INSTDIR\\Uninstall.exe"')
(release / 'installer.nsi').write_text(recipe, encoding='utf-8')
expected = [{'path': p.relative_to(payload).as_posix(), 'bytes': p.stat().st_size, 'sha256': sha(p)}
            for p in sorted(payload.rglob('*')) if p.is_file() and not p.name.startswith('WILDX_console-Setup-')]
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
extra = actual - {x['path'] for x in expected}
assert extra == {'$PLUGINSDIR/System.dll', '$PLUGINSDIR/nsDialogs.dll', '$PLUGINSDIR/modern-wizard.bmp'}, extra
for entry in old['payload']:
    if entry['path'].startswith(('Firmware/', 'NPConfigurator/', 'FirmwareUpdater/')):
        assert sha(payload / entry['path']) == entry['sha256']

manifest = dict(version=version, releaseId='WILDX128_NP_GPIO_IMU_PC_TIMESTAMPS_20261023',
    baselineVersion=old['version'], applicationRebuiltDuringPackaging=False,
    guiSha256=sha(payload / 'WILDX_console.exe'), sdkSha256=sha(payload / 'NpV2SdDatExport.dll'),
    sourceArchiveSha256=sha(release / 'compiled-sources.zip'), firmwareChanged=False,
    firmwareCatalogChanged=False, helpersChanged=False, installerExecuted=False,
    installerPayloadExtractedAndVerified=True, extractedPayloadFilesVerified=len(expected),
    scope='PC-clock correction for neural/GPIO timestamps; distinct retained IMU samples and derived timestamps; SD test workflow. Frozen build128, not the unrelated working tree.',
    timingFirmwareIncluded=False, timingFirmwareRequiredForHighResolutionNeuralBounds=True,
    existingFirmwareSupported=True, physicalAdcGpioAlignmentQualified=False,
    softwareChecks=receipt['checks'], replayChecks=receipt['replays'],
    installer={'file': installer.name, 'bytes': installer.stat().st_size, 'sha256': sha(installer)}, payload=expected)
save(release / 'package-verification.json', manifest)
(release / 'README.md').write_text(
    '# WILD-X 1.0.0.128\n\nExact checked build128 and reused SDK115 compiler inputs are retained in compiled-sources.zip and source-inputs.json. '
    'No application compilation occurred during packaging. Other working-tree edits are excluded.\n\n'
    'Adds automatic post-recording PC-clock timestamp correction, a standalone recursive correction script, '
    'distinct retained IMU sample journaling/correction, and the previously checked SD test workflow. '
    'PC/network clock correction remains separate. Missing clock evidence stays explicitly unavailable.\n\n'
    'Bundled firmware and both helper applications are byte-identical to release105. The optional high-resolution '
    'USB timing firmware used in the 70-second V1/V2 experiments is not bundled or automatically installed. '
    'Existing firmware uses available coarse timing or marks precision unavailable. '
    'The measured neural clock intervals do not qualify physical ADC/GPIO or IMU latency.\n\n'
    f'All {len(expected)} installer payload files were extracted and hash-verified. The installer was not executed. '
    'Detailed software and earlier hardware evidence is in checks/. The Run WILD-X finish checkbox uses the existing shell launch function.\n', encoding='utf-8')

archive = latest / 'legacy/20261023_before_1.0.0.128'
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
    'Includes PC-clock timestamp correction for neural/GPIO data, distinct IMU sample timestamps, '
    'a standalone recursive correction script, and the SD card test workflow. Existing firmware remains supported.\n\n'
    'High-resolution neural timing needs the newer USB timing firmware, which remains separate from this installer. '
    'The bundled firmware is unchanged from105. Clock uncertainty is saved with the results; network/UTC correction stays separate.\n\n'
    f'All {len(expected)} packaged files were extracted and hash-verified. '
    '[Manifest](release-manifest.json) / [source and validation](../ReleaseSources/1.0.0.128/README.md).\n', encoding='utf-8')
(latest / 'WILDX_V2_README.md').write_text(f'# WILD-X / V2 bundle\n\nUse [WILD-X {version}]({installer.name}). Existing V2 firmware remains supported; high-resolution timing firmware is a separate update.\n', encoding='utf-8')
attrs = repo / '.gitattributes'
attrs.write_bytes(attrs.read_bytes() + b'\n/LATEST_INSTALLERS/legacy/20261023_before_1.0.0.128/** -text\n/ReleaseSources/1.0.0.128/** -text\n')
assert len(list(latest.glob('*.exe'))) == 1
print(json.dumps({'version': version, 'installer': manifest['installer'], 'payloadFilesVerified': len(expected)}, indent=2))
