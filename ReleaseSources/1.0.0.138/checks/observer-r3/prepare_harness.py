from pathlib import Path
root=Path(__file__).resolve().parent;repo=root.parents[1]
s=(repo/'_artifacts/first_sample_anchor_20261023/BleAnchorSuite.cs').read_text(encoding='utf-8-sig')
a=s.index(' static async Task Record(){');b=s.index(' static string addressArgument;',a)
s=s[:a]+(root/'harness_record.cs.txt').read_text()+ '\n'+s[b:]
old='static async Task Run(string[] args){output='
assert s.count(old)==1
s=s.replace(old,'static async Task Run(string[] args){Need((args.Length==5||args.Length==6&&args[5]=="restore15")&&args[2]=="1"&&args[3]=="record"&&(Convert.ToUInt64(args[1],16)&0xffffffUL)==0xCA53BAUL,"Exact V1 CA53BA record action required");restore15=args.Length==6;output=')
s=s.replace('Directory.CreateDirectory(output);writer=', 'Directory.CreateDirectory(output);Need(!File.Exists(Path.Combine(output,"events.log")),"Use a fresh output directory");writer=')
old='{"probe",((NumericUpDown)Get(preview,"probe")).Value}'
assert s.count(old)==1
s=s.replace(old,old+',{"phase",Get(preview,"phase")},{"activeChannels",Get(preview,"activeChannels")},{"session",Get(preview,"session")},{"hz",Get(preview,"hz")},{"band",Get(preview,"activeBand")},{"request",Get(preview,"activeId")}')
s=s.replace('Width=400,Height=100}', 'Width=400,Height=100,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-20000,-20000)}')
(root/'BleCompactSuite.cs').write_text(s,encoding='utf-8')
print('Prepared bounded V1-only production API observer; do not execute without the hardware owner.')
