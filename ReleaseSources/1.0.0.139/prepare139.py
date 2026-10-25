from pathlib import Path
import shutil,xml.etree.ElementTree as E,json,hashlib
repo=Path.cwd();root=repo/'_artifacts/v2_installer_20261025';prior=repo/'_artifacts/v1_ble_compact_20261025/build138';build=root/'build139'
assert not build.exists();source=build/'changed-sources';source.mkdir(parents=True);binary=build/'compile-bin';binary.mkdir()
for p in (prior/'compile-bin').iterdir():
 if p.is_file() and p.suffix.lower() in ('.dll','.config') and not p.name.startswith('BleCompactSuite'):shutil.copy2(p,binary/p.name)
ns='http://schemas.microsoft.com/developer/msbuild/2003';E.register_namespace('',ns);tree=E.parse(prior/'ui-only.targets')
for n in tree.findall('.//{'+ns+'}Compile'):
 if Path(n.get('Include','')).name=='AssemblyInfo.cs':
  shutil.copy2(repo/'USB512_interface/USB512_interface/Properties/AssemblyInfo.cs',source/'AssemblyInfo.cs');n.set('Include',str(source/'AssemblyInfo.cs'))
for n in tree.findall('.//{'+ns+'}WriteLinesToFile'):n.set('File',str(build/Path(n.get('File')).name))
for n in tree.findall('.//{'+ns+'}Copy'):n.set('DestinationFiles',str(source/'AssemblyInfo.cs'))
E.indent(tree,space='  ');tree.write(build/'ui-only.targets',encoding='utf-8',xml_declaration=True)
print('Prepared139 from exact138 compiler graph; only assembly version will change.')
