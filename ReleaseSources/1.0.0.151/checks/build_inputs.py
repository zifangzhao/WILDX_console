from pathlib import Path
import hashlib,json,shutil,xml.etree.ElementTree as ET

root=Path(__file__).resolve().parent
repo=root.parent.parent
prior=repo/'_artifacts/download_meter_20261026'
receipt=json.loads((prior/'build150-receipt.json').read_text(encoding='utf-8-sig'))
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest().upper()
for x in receipt['sources']:assert sha(Path(x['path']))==x['sha256'],x['path']
build=root/'build151';changed=build/'changed-sources';changed.mkdir(parents=True,exist_ok=True)
assert not (build/'obj/WILDX_console.exe').exists()
shutil.copy2(repo/'USB512_interface/USB512_interface/NpLogger/NpDownloadBurst.cs',changed/'NpDownloadBurst.cs')
ns={'m':'http://schemas.microsoft.com/developer/msbuild/2003'}
ET.register_namespace('',ns['m'])
tree=ET.parse(prior/'build150/ui-only.targets')
for item in tree.findall('.//m:Compile',ns):
 p=item.get('Include')
 if p and Path(p).name in ['NpDownloadBurst.cs','AssemblyInfo.cs']:item.set('Include',str(changed/Path(p).name))
target=tree.find('m:Target',ns)
for item in target:
 if item.tag.endswith('Copy'):item.set('DestinationFiles',str(changed/'AssemblyInfo.cs'))
 elif item.tag.endswith('WriteLinesToFile'):item.set('File',str(build/Path(item.get('File')).name))
tree.write(build/'ui-only.targets',encoding='utf-8',xml_declaration=True)
shutil.copytree(prior/'app150',root/'app151',ignore=shutil.ignore_patterns('*Setup*.exe'))
print('Frozen 150 sources verified; only burst policy and auto-incremented assembly version will change.')
