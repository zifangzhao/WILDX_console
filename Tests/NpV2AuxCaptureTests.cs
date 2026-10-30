using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using USB512_interface;

internal static class NpV2AuxCaptureTests {
    static int checks;
    static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static void Wait(Func<bool> test,string why){var w=Stopwatch.StartNew();while(!test()&&w.ElapsedMilliseconds<2500)Thread.Sleep(1);Check(test(),why);}
    static void Bad(Action run,string why){try{run();}catch(IOException){checks++;return;}catch(InvalidDataException){checks++;return;}catch(InvalidOperationException){checks++;return;}throw new Exception(why);}
    static void Put(byte[] b,int at,uint n){for(int i=0;i<4;i++)b[at+i]=(byte)(n>>(8*i));}
    static byte[] Packet(uint type,uint sequence,byte[] payload){var b=new byte[64+payload.Length];Encoding.ASCII.GetBytes("NPAVUSB1").CopyTo(b,0);Put(b,8,1);Put(b,12,type);Put(b,16,sequence);Put(b,32,(uint)payload.Length);Put(b,40,(uint)payload.Length);Put(b,44,type==3?20U:0U);Put(b,48,3);Put(b,56,type==4?0:type);payload.CopyTo(b,64);return b;}
    static NpV2UsbDevice Device(string path){return (NpV2UsbDevice)Activator.CreateInstance(typeof(NpV2UsbDevice),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{null,new IntPtr(123), (byte)0x81,true,path},null);}
    static void Set(object o,string field,object value){o.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(o,value);}
    static void Ownership(){
        string path="\\\\?\\usb#vid_1209&pid_b386#001A0004434250062039314E#{8f5a4a7b-0aa6-4f8e-a14f-a2f7b3c6aa4e}";
        var d=Device(path);var owner=new object();d.ClaimAuxOwner(owner);Bad(()=>d.ClaimAuxOwner(new object()),"Second EP82 owner admitted");
        Bad(()=>d.ReleaseAuxOwner(owner),"AUX lease released without a returned reader/proof");d.Dispose();Check(d.AuxiliaryOwnershipHeld,"Shared interface closed with unretired AUX ownership");
        Bad(()=>NpV2UsbDevice.Open(path.ToLowerInvariant()),"HOLD did not block same-path reopen before hardware enumeration");
        Check((IntPtr)typeof(NpV2UsbDevice).GetField("usb",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(d)==new IntPtr(123),"HOLD cleared shared interface");
        var reader=(USB512WinUsbQueuedReader)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(USB512WinUsbQueuedReader));Set(d,"auxReader",reader);
        Bad(()=>d.ReleaseAuxOwner(owner),"Unretired actual reader released owner");
        // Model the positive native harvest outcome without invoking WinUSB.
        Set(reader,"<RetirementConfirmed>k__BackingField",true);Set(d,"usb",IntPtr.Zero);d.ReleaseAuxOwner(owner);d.Dispose();
        Check(reader.RetirementConfirmed,"Positive reader proof lost");
        var holds=(System.Collections.IEnumerable)typeof(NpV2UsbDevice).GetField("AuxHolds",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);Check(!holds.Cast<object>().Contains(d),"Proven late retirement left device held");
    }
    sealed class Native {
        internal readonly Queue<byte[]> Inputs=new Queue<byte[]>();internal readonly ManualResetEvent ReadGate=new ManualResetEvent(true),AbortGate=new ManualResetEvent(true);
        internal int StopCalls,AbortCalls,RetireCalls,ReleaseCalls,DisposeCalls;internal bool Verified,FailAbort,FailRetire,HoldRead;internal byte AbortPipe;
        internal int Read(byte[] into){if(HoldRead)ReadGate.WaitOne();byte[] b=null;lock(Inputs){if(Inputs.Count!=0)b=Inputs.Dequeue();}if(b==null){Thread.Sleep(1);return 0;}Buffer.BlockCopy(b,0,into,0,b.Length);return b.Length;}
        internal NpV2AuxCapture Start(Action<NpV2AuxPacket> sink){return new NpV2AuxCapture(false,sink,Read,()=>StopCalls++,()=>{AbortCalls++;AbortPipe=0x82;AbortGate.WaitOne();if(FailAbort)throw new IOException("Injected AUX abort failure");},()=>{RetireCalls++;if(FailRetire)throw new IOException("Injected incomplete native harvest");Verified=true;},()=>Verified,()=>ReleaseCalls++,()=>DisposeCalls++);}
    }
    const string Uid="112233445566778899AABBCC";
    static byte[] Control(uint flags=0,uint revision=1){var b=new byte[32];Encoding.ASCII.GetBytes("NSGC").CopyTo(b,0);Put(b,4,1);Put(b,8,flags);Put(b,12,0x11223344);Put(b,16,0x55667788);Put(b,20,0x99AABBCC);Put(b,24,revision);Put(b,28,NpImuGridCarrier.Crc(b,0,28));return b;}
    static byte[] AuxStatus(){var b=new byte[128];Put(b,0,0x32535641);Put(b,4,1);Put(b,8,1);Put(b,16,4);Put(b,112,0x11223344);Put(b,116,0x55667788);Put(b,120,0x99AABBCC);return b;}
    static void LabOrdering(string repo){
        string previous=Environment.GetEnvironmentVariable("WILDX_V2_AUX_GRID_LAB");
        try {
            Environment.SetEnvironmentVariable("WILDX_V2_AUX_GRID_LAB",null);Check(NpV2AuxLabSession.ForLab("unused",Uid)==null,"Default path allocated lab session");
            Environment.SetEnvironmentVariable("WILDX_V2_AUX_GRID_LAB","1");var lab=NpV2AuxLabSession.ForLab("unused",Uid);int configure=0,select=0,reads=0;
            lab.BeforeConfiguration(true,()=>{throw new System.ComponentModel.Win32Exception(31);},()=>configure++,AuxStatus);lab.AfterTiming(()=>Control(1),s=>select++,()=>null,true);Check(!lab.Configured&&!lab.Selected&&configure==0&&select==0,"Unsupported optional control gated NP or mutated AUX");
            lab=NpV2AuxLabSession.ForLab("unused",Uid);lab.BeforeConfiguration(true,()=>Control(),()=>{configure++;throw new IOException("Lost config ACK");},AuxStatus);Check(lab.Configured&&configure==1,"One lost configuration ACK was replayed instead of reconciled");
            lab.AfterTiming(()=>++reads==1?Control(1):Control(3,2),s=>select++,()=>null,true);Check(lab.Selected&&select==1&&reads==2,"Ready/pre-arm selection was not one-shot reconciled");
            lab=NpV2AuxLabSession.ForLab("unused",Uid);configure=select=reads=0;lab.BeforeConfiguration(true,()=>Control(),()=>{configure++;throw new IOException("Configure failed");},()=>new byte[128]);lab.AfterTiming(()=>Control(1),s=>select++,()=>null,true);Check(!lab.Configured&&!lab.Selected&&configure==1&&select==0,"Failed optional config caused SELECT or a thrown Start gate");
            lab=NpV2AuxLabSession.ForLab("unused",Uid);lab.BeforeConfiguration(true,()=>Control(),()=>{},AuxStatus);lab.AfterTiming(()=>{if(++reads==1)return Control(1);throw new IOException("Selection outcome unknown");},s=>select++,()=>null,true);Check(!lab.Selected&&select==1&&reads==2&&lab.Error!=null,"Ambiguous SELECT was guessed/replayed or escaped optional scope");
            lab=NpV2AuxLabSession.ForLab("unused",Uid);select=0;lab.BeforeConfiguration(true,()=>Control(),()=>{},AuxStatus);lab.AfterTiming(()=>Control(1),s=>select++,()=>null,false);Check(!lab.Selected&&select==0,"Grid selected without feature15");
            string source=File.ReadAllText(Path.Combine(repo,"Neuropixels/USBConsoleV2/NpV2UsbConsole.cs"));int before=source.IndexOf("auxGridLab.BeforeConfiguration(device)",StringComparison.Ordinal),start=source.IndexOf("device.Start(targetBlocks)",before,StringComparison.Ordinal),timing=source.IndexOf("usbTiming=NpUsbTimingSession.Prepare",start,StringComparison.Ordinal),ready=source.IndexOf("auxGridLab.AfterTiming",timing,StringComparison.Ordinal),arm=source.IndexOf("device.ArmCapture()",ready,StringComparison.Ordinal),queue=source.IndexOf("queueFree();",arm,StringComparison.Ordinal),reader=source.IndexOf("auxGridLab.AfterArm",queue,StringComparison.Ordinal);
            Check(before>0&&start>before&&timing>start&&ready>timing&&arm>ready&&queue>arm&&reader>queue,"Actual production lifecycle ordering changed");
            Check(source.Substring(before-150,150).Contains("!NP2_CONSOLE_STANDALONE && NP_FIRST_SYNC_ENABLE && NP_IMU_GRID_ENABLE"),"Default compile path accidentally enables grid setup");
        } finally {Environment.SetEnvironmentVariable("WILDX_V2_AUX_GRID_LAB",previous);}
    }
    static void TerminalArchive(string repo){
        string p=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"raw-terminal-root");Directory.CreateDirectory(p);File.Copy(Path.Combine(repo,"Tests/Fixtures/NpV2GridR6TimingEvidence.jsonl"),Path.Combine(p,NpUsbTimingEvidence.FileName));
        var h=NpFirstSyncUsbEvidence.Read(p);Check(h.FineQualified,"Actual terminal fixture parent is not strictly bound");
        var raw=new byte[80];Encoding.ASCII.GetBytes("NPGEND01").CopyTo(raw,0);Put(raw,8,1);Put(raw,12,80);var words=NpFirstSyncUsb.UidWords(h.Uid);for(int i=0;i<3;i++)Put(raw,16+i*4,words[i]);
        Put(raw,28,h.Receipt.Session);Put(raw,32,h.Generation);Put(raw,36,h.Receipt.SourceEpoch);Put(raw,40,h.Receipt.Domain);BitConverter.GetBytes(h.Token).CopyTo(raw,44);BitConverter.GetBytes(UInt64.MaxValue).CopyTo(raw,52);Put(raw,76,NpV2AuxTerminal.Crc(raw));
        string env=Environment.GetEnvironmentVariable("WILDX_V2_AUX_GRID_LAB");try{
            Environment.SetEnvironmentVariable("WILDX_V2_AUX_GRID_LAB","1");
            for(int mode=0;mode<6;mode++){
                string output=Path.Combine(p,"case"+mode);Directory.CreateDirectory(output);var neural=new byte[]{9,8,7,6};File.WriteAllBytes(Path.Combine(output,"neuropixels_ap.dat"),neural);
                var lab=NpV2AuxLabSession.ForLab(output,h.Uid);Set(lab,"selected",true);Set(lab,"root",(Func<NpFirstSyncUsbFineRoot>)(()=>h.FineRoot));
                byte[] body=(byte[])raw.Clone();if(mode==1)body[76]^=1;else if(mode==2){Put(body,32,h.Generation+1);Put(body,76,NpV2AuxTerminal.Crc(body));}
                string archive=Path.Combine(output,"imu_grid_lab",NpV2AuxLabSession.TerminalFile);
                if(mode==3||mode==4){Directory.CreateDirectory(Path.GetDirectoryName(archive));if(mode==3)Directory.CreateDirectory(archive);else File.WriteAllBytes(archive,new byte[]{4,5});}
                byte[] unknownPhase=null;
                if(mode==5){
                    var bind=NpImuGridWriter.PhaseBinding(h.Uid,h.Token,h.Receipt);unknownPhase=NpImuGridPhase.Pack(new NpImuGridPhaseRecord{Uid=bind.Uid,Session=bind.Session,CaptureGeneration=bind.CaptureGeneration,SourceEpoch=bind.SourceEpoch,CounterDomain=bind.CounterDomain,Token=bind.Token,FirstAcceptedSlot=h.Receipt.Aux[0].Counter,LowerNumerator=Int64.MinValue,UpperNumerator=Int64.MinValue,Denominator=0,MaximumJitter=UInt32.MaxValue,Flags=0});
                    var ph=Packet(6,1,unknownPhase);BitConverter.GetBytes(h.Receipt.Aux[0].Counter).CopyTo(ph,24);var phasePackets=new List<NpV2AuxPacket>();new NpV2AuxParser(true).Feed(ph,0,ph.Length,phasePackets.Add);typeof(NpV2AuxLabSession).GetMethod("Packet",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(lab,new object[]{phasePackets[0]});
                }
                var wire=Packet(7,1,body);BitConverter.GetBytes(UInt64.MaxValue).CopyTo(wire,24);var packets=new List<NpV2AuxPacket>();new NpV2AuxParser(true).Feed(wire,0,wire.Length,packets.Add);
                typeof(NpV2AuxLabSession).GetMethod("Packet",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(lab,new object[]{packets[0]});lab.Complete(h.Receipt.ReferenceTick+320000,false);
                if(mode==0||mode==5)Check(File.ReadAllBytes(archive).SequenceEqual(raw)&&lab.Error==null,"Valid bound unknown phase/terminal became an optional mismatch error");
                else {Check(lab.Error!=null,"Invalid/context/I-O terminal was silently qualified");if(mode==4)Check(File.ReadAllBytes(archive).SequenceEqual(new byte[]{4,5}),"Existing terminal evidence overwritten");else Check(!File.Exists(archive),"Invalid terminal bytes persisted as validated evidence");}
                Check(File.ReadAllBytes(Path.Combine(output,"neuropixels_ap.dat")).SequenceEqual(neural),"Optional terminal I/O altered neural data");
                if(mode==5)Check(File.ReadAllBytes(Path.Combine(output,"imu_grid_lab/imu_grid_phase.bin")).SequenceEqual(unknownPhase)&&File.ReadAllText(Path.Combine(output,"imu_grid_lab/analogin_encoding.txt")).Contains("grid_phase_qualified=False"),"Bound unknown phase was lost or qualified");
            }
        }finally{Environment.SetEnvironmentVariable("WILDX_V2_AUX_GRID_LAB",env);}
    }
    static void Put64(byte[] b,int at,ulong n){BitConverter.GetBytes(n).CopyTo(b,at);}
    static void Seal(byte[] b){Put(b,b.Length-4,NpFirstSyncWire.Crc(b,b.Length-4));}
    static NpFirstSyncUsbFineRoot InitialRoot(NpFirstSyncUsbFineRoot seed,NpV2WireVerifier v,Action<byte[]> change=null){
        byte[] r=seed.Receipt;r[48]=(byte)v.ProbeMask;
        for(int p=0;p<3;p++){
            int at=64+p*20;Array.Clear(r,at,20);if((v.ProbeMask&(1U<<p))==0)continue;
            int phase=v.PhaseForProbe(p);Put64(r,at,(ulong)((1600-phase)%1600));Put(r,at+8,UInt32.MaxValue);
            BitConverter.GetBytes((ushort)phase).CopyTo(r,at+12);BitConverter.GetBytes((ushort)1600).CopyTo(r,at+14);Put(r,at+16,27);
        }
        Put64(r,52,BitConverter.ToUInt64(r,64));r[133]=71;r[135]=1;Put(r,144,100);Put(r,148,1000);
        if(change!=null)change(r);Seal(r);NpFirstSyncWire.Parse(r);
        byte[] info=seed.Info,anchor=seed.Anchor;var declared=NpFirstSyncUsb.ParseInfo(info,seed.Uid,2);
        var assembly=new NpFirstSyncUsbAssembler(declared,seed.Token,seed.Generation);
        var uid=NpFirstSyncUsb.UidWords(seed.Uid);
        for(int i=0;i<7;i++){
            var page=new byte[108];Put(page,0,NpFirstSyncUsb.EnvelopeMagic);Put(page,4,0x40001);Put64(page,8,seed.Token);Put(page,16,seed.Generation);
            for(int j=0;j<3;j++)Put(page,20+j*4,uid[j]);Put64(page,32,(ulong)i+1);Put(page,40,NpFirstSyncUsb.PageMagic);page[44]=1;page[45]=(byte)i;page[46]=7;
            Put(page,48,BitConverter.ToUInt32(r,324));Buffer.BlockCopy(r,i*48,page,52,i==6?40:48);Put(page,100,NpFirstSyncWire.Crc(page.Skip(40).Take(64).ToArray(),60));Seal(page);
            Check(assembly.Add(page,(ulong)i+1),"Synthetic actual source pages refused");
        }
        var env=NpFirstSyncUsb.Anchor(anchor,r,seed.Uid,seed.Token,seed.Generation,8);Check(assembly.FeedAnchor(env,8),"Actual source root refused");
        return new NpFirstSyncUsbFineRoot(info,assembly,env,8);
    }
    static void Send(NpV2AuxLabSession lab,uint type,uint sequence,byte[] body){
        byte[] raw=Packet(type,sequence,body);if(type==5)Put(raw,44,1250);
        var packets=new List<NpV2AuxPacket>();new NpV2AuxParser(true).Feed(raw,0,raw.Length,packets.Add);
        typeof(NpV2AuxLabSession).GetMethod("Packet",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(lab,new object[]{packets.Single()});
    }
    static void FirstSlot(string repo){
        string basePath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"first-slot");Directory.CreateDirectory(basePath);
        string seedPath=Path.Combine(basePath,"seed");Directory.CreateDirectory(seedPath);File.Copy(Path.Combine(repo,"Tests/Fixtures/NpV2GridR6TimingEvidence.jsonl"),Path.Combine(seedPath,NpUsbTimingEvidence.FileName));
        var seed=NpFirstSyncUsbEvidence.Read(seedPath).FineRoot;
        var maker=typeof(NpV2UsbConsoleTests).GetMethod("MakeV3Block",BindingFlags.NonPublic|BindingFlags.Static);
        string prior=Environment.GetEnvironmentVariable("WILDX_V2_AUX_GRID_LAB");Environment.SetEnvironmentVariable("WILDX_V2_AUX_GRID_LAB","1");
        try{foreach(bool merged in new[]{false,true})foreach(uint mask in new[]{3U,7U}){
            string output=Path.Combine(basePath,"m"+mask+"-"+merged);Directory.CreateDirectory(output);
            var verifier=new NpV2WireVerifier();
            using(var data=new NpV2DatRecorder(output,null,new NpV2BufferPolicy(64),null,merged,false)){
                for(ulong i=0;i<2;i++){
                    int ticks=mask==3?46080:40960;
                    var wire=(byte[])maker.Invoke(null,new object[]{i,i*(uint)ticks,mask,mask,20000U,false,UInt32.MaxValue});
                    verifier.Feed(wire,0);data.Enqueue(wire,0,verifier);
                }
                data.Complete();var root=InitialRoot(seed,verifier);byte[] stopped=root.Anchor;Put(stopped,8,1234);Seal(stopped);
                string anchorPath=Path.Combine(output,"acquisition_anchor.bin");File.WriteAllBytes(anchorPath,stopped);
                string why;Check(NpV2AuxLabSession.SavedFirstSource(output,seed.Uid,root,verifier,data,out why),"Actual closed initial V2 NP rows did not qualify: "+why);
                var laterBad=(byte[])maker.Invoke(null,new object[]{99UL,99UL*(mask==3?46080UL:40960UL),mask,mask,20000U,false,UInt32.MaxValue});
                Bad(()=>verifier.Feed(laterBad,0),"Later actual NN sequence fault was ignored");
                Check(NpV2AuxLabSession.SavedFirstSource(output,seed.Uid,root,verifier,data,out why),"Later actual NN fault erased proven saved initial NP rows");
                Bad(()=>InitialRoot(seed,verifier,r=>Put(r,72,1)),"V1 native probe counter was accepted under V2 sentinel protocol");
                string file=Path.Combine(output,merged?"neuropixels_ap.dat":"probe1/neuropixels_ap.dat");byte[] neural=File.ReadAllBytes(file);
                Check(!NpV2AuxLabSession.SavedFirstSource(output,"112233445566778899AABBCC",root,verifier,data,out why),"Different actual UID admitted source root");
                using(var held=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read))Check(!NpV2AuxLabSession.SavedFirstSource(output,seed.Uid,root,verifier,data,out why),"Unclosed/unexclusively owned DAT became initial proof");
                byte[] broken=(byte[])stopped.Clone();broken[16]^=1;File.WriteAllBytes(anchorPath,broken);
                Check(!NpV2AuxLabSession.SavedFirstSource(output,seed.Uid,root,verifier,data,out why),"Corrupt saved stopped anchor admitted initial proof");
                Seal(broken);File.WriteAllBytes(anchorPath,broken);Check(!NpV2AuxLabSession.SavedFirstSource(output,seed.Uid,root,verifier,data,out why),"Different saved source generation admitted initial proof");File.WriteAllBytes(anchorPath,stopped);
                var different=InitialRoot(seed,verifier,r=>{ushort phase=(ushort)((verifier.PhaseForProbe(0)+1)%1600);Put64(r,64,(ulong)((1600-phase)%1600));BitConverter.GetBytes(phase).CopyTo(r,76);Put64(r,52,BitConverter.ToUInt64(r,64));});
                Check(!NpV2AuxLabSession.SavedFirstSource(output,seed.Uid,different,verifier,data,out why),"Different first STARTBYTE/phase admitted initial proof");
                byte header=verifier.IdentityHeader[24];verifier.IdentityHeader[24]=1;Check(!NpV2AuxLabSession.SavedFirstSource(output,seed.Uid,root,verifier,data,out why),"Nonzero first header sequence admitted pre-start loss");verifier.IdentityHeader[24]=header;
                using(var partial=new FileStream(file,FileMode.Open,FileAccess.Write,FileShare.None))partial.SetLength(neural.Length-1);
                Check(!NpV2AuxLabSession.SavedFirstSource(output,seed.Uid,root,verifier,data,out why),"Partial DAT row qualified initial proof");File.WriteAllBytes(file,neural);
                var counterOnly=InitialRoot(seed,verifier,r=>{r[133]=7;r[135]=0;Put(r,144,UInt32.MaxValue);Put(r,148,UInt32.MaxValue);});
                foreach(bool point in new[]{false,true})foreach(bool lostFirst in new[]{false,true}){
                    var phaseRoot=point?root:counterOnly;
                    string phaseOut=Path.Combine(output,"phase"+lostFirst+"-point"+point);Directory.CreateDirectory(phaseOut);
                    // Use the actual recording directory for proof; one lab writer per case.
                    var lab=NpV2AuxLabSession.ForLab(output,seed.Uid);Set(lab,"folder",phaseOut);Set(lab,"selected",true);Set(lab,"root",(Func<NpFirstSyncUsbFineRoot>)(()=>phaseRoot));
                    var receipt=NpFirstSyncWire.Parse(phaseRoot.Receipt);var binding=NpImuGridWriter.PhaseBinding(seed.Uid,seed.Token,receipt);
                    if(!point)Check((receipt.Aux[0].Flags&64)==0&&receipt.Aux[0].StampKind==0&&receipt.Aux[0].FirstMs==UInt32.MaxValue&&receipt.Aux[0].FirstCycles==UInt32.MaxValue,"Counter-only V2 fixture invented a POINT or HAL time");
                    var phase=NpImuGridPhase.Pack(new NpImuGridPhaseRecord{Uid=binding.Uid,Session=binding.Session,CaptureGeneration=binding.CaptureGeneration,SourceEpoch=binding.SourceEpoch,CounterDomain=binding.CounterDomain,Token=binding.Token,FirstAcceptedSlot=receipt.Aux[0].Counter,LowerNumerator=100,UpperNumerator=200,Denominator=1,MaximumJitter=UInt32.MaxValue,Flags=1});
                    Send(lab,6,1,phase);Send(lab,5,2,NpImuGridWire.Pack(new NpImuGridBatch{CaptureGeneration=root.Generation,FirstSlot=lostFirst?1UL:0UL,SlotCount=1,ObservedMask=1,AgValid=1,MagValid=1,Rows=new[]{new int[9]}}));
                    lab.Complete(verifier.LastSourceTick+(ulong)verifier.TicksPerBlock,false,verifier,data);
                    string encoding=File.ReadAllText(Path.Combine(phaseOut,"analogin_encoding.txt"));
                    Check(encoding.Contains("first_slot_phase_valid="+(!lostFirst))&&encoding.Contains("grid_phase_qualified=False"),"Initial phase inherited full-record completion, or lost first observation qualified");
                    Check(File.ReadAllBytes(Path.Combine(phaseOut,"imu_grid_phase.bin")).SequenceEqual(phase),"Raw once-only first-slot phase changed");
                }
                Check(File.ReadAllBytes(file).SequenceEqual(neural),"Optional startup proof/phase handling changed saved neural DAT");
            }
        }}finally{Environment.SetEnvironmentVariable("WILDX_V2_AUX_GRID_LAB",prior);}
    }
    static void Main(string[] args){
        LabOrdering(args[0]);
        TerminalArchive(args[0]);
        FirstSlot(args[0]);
        Ownership();var imu=Packet(3,1,Enumerable.Range(0,140).Select(i=>(byte)i).ToArray());var end=Packet(4,2,new byte[0]);
        var n=new Native();n.Inputs.Enqueue(imu.Concat(end).ToArray());var seen=new List<NpV2AuxPacket>();var c=n.Start(p=>seen.Add(p));Wait(()=>c.WorkerExited,"Normal AUX reader did not finish");Check(c.TerminalSeen&&seen.Count==2&&seen[0].Payload.SequenceEqual(imu.Skip(64)),"Dispatcher changed legacy sensor/terminal bytes");Check(c.StopAndRetire(10,500)&&c.RetirementConfirmed&&n.ReleaseCalls==1&&n.DisposeCalls==1&&n.AbortPipe==0x82,"Normal retirement missing or wrong endpoint");Check(c.StopAndRetire(0,1)&&n.StopCalls==1&&n.AbortCalls==1,"Repeated disposal replayed controls");
        n=new Native();n.Inputs.Enqueue(imu.Concat(end).ToArray());int delivered=0;c=n.Start(p=>{delivered++;if(p.Type==3)throw new InvalidDataException("Bad optional sensor");});Wait(()=>c.WorkerExited,"Optional decoder error stopped terminal delivery");Check(delivered==2&&c.Error.Contains("Bad optional")&&c.StopAndRetire(0,500),"Optional sink error gated cleanup/independent record");
        n=new Native();var faulted=(byte[])imu.Clone();Put(faulted,52,8);n.Inputs.Enqueue(faulted.Concat(end).ToArray());delivered=0;c=n.Start(p=>delivered++);Wait(()=>c.WorkerExited,"Device-error test did not drain");Check(delivered==2&&c.Error.Contains("device reported error 8")&&c.StopAndRetire(0,500),"Device AUX error was hidden or discarded native records");
        n=new Native();n.Inputs.Enqueue(imu);c=n.Start(p=>{throw new ArgumentException("Unexpected optional sink bug");});Wait(()=>c.WorkerExited,"AUX programming error escaped background worker");Check(c.Error.Contains("Unexpected")&&c.StopAndRetire(0,500)&&!c.TerminalSeen,"Unexpected optional bug was silent or fabricated terminal");
        n=new Native{FailRetire=true};n.Inputs.Enqueue(end);c=n.Start(p=>{});Wait(()=>c.WorkerExited,"Incomplete-harvest test did not drain");Check(!c.StopAndRetire(0,500)&&!c.RetirementConfirmed&&n.ReleaseCalls==0&&n.DisposeCalls==0,"Incomplete harvest freed owner/native memory");n.FailRetire=false;Check(c.StopAndRetire(0,500)&&n.StopCalls==1&&n.ReleaseCalls==1,"Explicit retirement retry did not reconcile without replaying Stop");
        n=new Native{FailAbort=true};n.Inputs.Enqueue(end);c=n.Start(p=>{});Wait(()=>c.WorkerExited,"Abort-failure test did not drain");Check(!c.StopAndRetire(0,500)&&n.RetireCalls==0&&n.ReleaseCalls==0,"Failed abort was treated as retirement proof");n.FailAbort=false;Check(c.StopAndRetire(0,500)&&n.StopCalls==1&&n.AbortPipe==0x82,"AUX abort retry touched shared/NP endpoint");
        n=new Native{HoldRead=true};n.ReadGate.Reset();c=n.Start(p=>{});var watch=Stopwatch.StartNew();Check(!c.StopAndRetire(0,25)&&watch.ElapsedMilliseconds<500&&n.ReleaseCalls==0&&n.DisposeCalls==0,"Stuck AUX worker blocked owner or freed native buffers");n.ReadGate.Set();Wait(()=>c.WorkerExited,"Released test read remained stuck");Check(c.StopAndRetire(0,500),"Recovered read could not retire");
        n=new Native();n.AbortGate.Reset();c=n.Start(p=>{});watch.Restart();Check(!c.StopAndRetire(0,25)&&watch.ElapsedMilliseconds<500&&n.ReleaseCalls==0,"Poisoned AbortPipe blocked coordinator or released owner");n.AbortGate.Set();Wait(()=>c.RetirementConfirmed,"Late proven retirement did not release ownership");Check(n.AbortPipe==0x82&&n.StopCalls==1,"Late retirement used wrong endpoint/control replay");
        n=new Native();var reversed=Packet(3,0,new byte[140]);n.Inputs.Enqueue(imu.Concat(reversed).Concat(Packet(4,1,new byte[0])).ToArray());delivered=0;c=n.Start(p=>delivered++);Wait(()=>c.WorkerExited,"Sequence test did not finish");Check(delivered==3&&c.SequenceGaps==1&&c.Error!=null&&c.StopAndRetire(0,500),"Sequence gap hid native records or claimed continuity");
        n=new Native();n.Inputs.Enqueue(imu.Take(17).ToArray());c=n.Start(p=>{});Check(c.StopAndRetire(0,500)&&!c.TerminalSeen&&c.Error!=null,"Truncated body gained complete tail or prevented safe native cleanup");
        Console.WriteLine("V2 AUX actual worker/ownership: "+checks+" checks passed; no device/native driver qualification.");
    }
}
