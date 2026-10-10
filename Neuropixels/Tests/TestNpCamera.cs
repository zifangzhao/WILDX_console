using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;
using USB512_interface;

static class TestNpCamera {
    static int checks;
    static void Check(bool ok,string message) {checks++;if(!ok)throw new Exception(message);}
    static ulong Hash(ulong hash,byte[] b,int offset,int count) {
        for(int i=0;i<count;i++)hash=unchecked((hash^b[offset+i])*1099511628211UL);
        return hash;
    }
    static byte[] Packet(byte id,byte flags,ushort seq,int size) {
        var b=new byte[size+6];b[0]=0x12;b[1]=0xa5;b[2]=id;b[3]=flags;b[4]=(byte)seq;b[5]=(byte)(seq>>8);return b;
    }
    sealed class Wire {
        internal byte[] Bytes,Camera;
        internal ulong NpHash=14695981039346656037UL;
        internal int NpPackets,MiscPackets;
    }
    static Wire Build(int mode,int skipCamera=-1,bool partial=false) {
        var wire=new Wire();var bytes=new MemoryStream();var camera=new MemoryStream();
        int npSequence=0,miscSequence=0,camOrdinal=0;ushort cameraSequence=65500;
        for(int frame=0;frame<2;frame++)for(int at=0;at<NpCameraRecording.FrameBytes;) {
            int count=Math.Min(506,NpCameraRecording.FrameBytes-at);
            var cam=Packet(3,(byte)((at==0?1:0)|(count<506?2:0)),cameraSequence++,count);
            for(int i=0;i<count;i++)cam[i+6]=(byte)((at+i)*17+frame*11);
            bool include=camOrdinal++!=skipCamera && !(partial && frame==1 && count<506);
            if(camOrdinal%32==1 && mode!=3) {
                var group=new byte[mode==0?506:2024];
                for(int i=0;i<group.Length;i++)group[i]=(byte)(i+npSequence*7);
                for(int lane=0;lane<(mode==0?1:4);lane++) {
                    var np=Packet((byte)(mode==2?0x76:6),(byte)(mode==0?0:mode==1?0x60:0x61|(lane<<2)),(ushort)npSequence++,506);
                    for(int i=0;i<506;i++)np[i+6]=group[mode==0?i:i*4+lane];
                    bytes.Write(np,0,np.Length);wire.NpPackets++;
                    if(lane==0 && include) {bytes.Write(cam,0,cam.Length);camera.Write(cam,6,count);include=false;}
                }
                wire.NpHash=Hash(wire.NpHash,group,0,group.Length);
                var misc=Packet(5,8,(ushort)miscSequence++,506);bytes.Write(misc,0,misc.Length);wire.MiscPackets++;
            }
            if(include) {bytes.Write(cam,0,cam.Length);camera.Write(cam,6,count);}
            at+=count;
        }
        wire.Bytes=bytes.ToArray();wire.Camera=camera.ToArray();return wire;
    }
    static void Replay(string root,int mode,int chunk,int skip=-1,bool partial=false) {
        Wire wire=Build(mode,skip,partial);
        string folder=Path.Combine(root,"m"+mode+"c"+chunk+"s"+skip+"p"+partial);Directory.CreateDirectory(folder);
        ulong actual=14695981039346656037UL;int misc=0,preview=0;
        var legacy=new XeNpPacketParser();var tagged=new NpTaggedPacketParser(1,4);
        Action<byte[],int,int> receive=delegate(byte[] b,int o,int n) {actual=Hash(actual,b,o,n);};
        using(var camera=new NpCameraRecording(folder)) {
            Action<byte[],int,int,byte,ushort> cam=delegate(byte[] b,int o,int n,byte f,ushort s) {if(camera.Write(b,o,n,f,s))preview++;};
            legacy.OnCamera=cam;tagged.OnCamera=cam;
            legacy.OnMisc=delegate {misc++;};tagged.OnMisc=delegate {misc++;};
            byte[] input=new byte[chunk];
            for(int at=0;at<wire.Bytes.Length;) {
                int n=Math.Min(chunk,wire.Bytes.Length-at);Buffer.BlockCopy(wire.Bytes,at,input,0,n);at+=n;
                if(mode==2)tagged.Feed(input,n,delegate(int probe,byte[] b,int o,int length) {Check(probe==1,"probe ID changed");receive(b,o,length);});
                else legacy.Feed(input,n,receive);
                if(mode==3 && at==Math.Min(chunk,wire.Bytes.Length))System.Threading.Thread.Sleep(10);
            }
            if(mode==2)tagged.Complete();else legacy.Complete();camera.Finish();
            Check(actual==wire.NpHash,"Neural payload changed with camera interleaving");
            Check((mode==2?tagged.Packets:legacy.Packets)==wire.NpPackets,"Neural packet count changed");
            Check((mode==2?tagged.SequenceGaps:legacy.SequenceGaps)==0,"Camera affected neural sequence");
            Check(misc==wire.MiscPackets,"Misc routing changed");
            Check(camera.PayloadBytes==wire.Camera.Length,"Camera byte count changed");
            Check(camera.Packets==634-(skip>=0?1:0)-(partial?1:0),"Camera packet count changed");
            Check(camera.Frames==(skip>=0||partial?1:2),"Camera frame count changed");
            Check(camera.Complete==(skip<0&&!partial),"Camera completeness wrong");
            Check(camera.SequenceGaps==(skip>=0?1:0),"Camera sequence wrapping/gap wrong");
            Check(preview>0,"No preview payloads");
        }
        byte[] saved=File.ReadAllBytes(Path.Combine(folder,"misc.dat"));
        Check(saved.Length==wire.Camera.Length,"Saved camera length differs");
        for(int i=0;i<saved.Length;i++)Check(saved[i]==wire.Camera[i],"Packed camera bytes changed");
        string[] index=File.ReadAllLines(Path.Combine(folder,"misc.frames.csv"));
        Check(index.Length==3,"Camera frame index lost a frame boundary");
        int complete=0,total=0;
        for(int i=1;i<index.Length;i++) {
            string[] fields=index[i].Split(',');Check(Int32.Parse(fields[0])==total,"Camera index offset changed");
            total+=Int32.Parse(fields[1]);if(fields[2]=="True")complete++;
        }
        Check(total==saved.Length,"Camera index omits received bytes");
        Check(complete==(skip>=0||partial?1:2),"Damaged camera segment marked complete");
        if(mode==3)Check(Directory.GetFiles(folder,"neuropixels*").Length==0 && !legacy.IsQuadInterleaved,"Camera-only recording depends on NP output/transport");
    }
    static void Reject(Action work,string label) {
        bool rejected=false;try {work();}catch(IOException) {rejected=true;}Check(rejected,label);
    }
    static void Put(byte[] b,int offset,uint value) {Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,offset,4);}
    static void Status(string root) {
        var b=new byte[128];Put(b,0,0x314d4143);Put(b,4,2);
        for(int i=2;i<30;i++)Put(b,i*4,(uint)(i*7));
        Put(b,12,35);Put(b,64,58);Put(b,68,35);Put(b,72,46800000);Put(b,76,0);
        Put(b,96,160000);Put(b,100,320);Put(b,104,320);Put(b,108,187200000);Put(b,112,58);Put(b,116,34988);
        var s=NpCameraStatus.Parse(b,128);Check(s.SupportsControls,"CAM1 v2 capabilities missing");
        string[] fields={"Version","Flags","RateHz","CapturedFrames","DeliveredFrames","Packets","PayloadBytes",
            "FailureCount","FailureOrigin","FailurePhase","FailureSpiError","FailureDmaError","Recoveries","LastStatus","CreditBytes",
            "RequestedHz","MaxHz","SclkHz","Profile","DroppedFrames","MissedDeadlines","FrameId","FrameTick",
            "FrameBytes","Width","Height","KernelHz","SensorMaxHz","MeasuredMillihz"};
        for(int i=0;i<fields.Length;i++)Check((uint)typeof(NpCameraStatus).GetField(fields[i],BindingFlags.NonPublic|BindingFlags.Instance).GetValue(s)==BitConverter.ToUInt32(b,(i+1)*4),"CAM1 offset wrong: "+fields[i]);
        for(int length=0;length<128;length++) {int n=length;Reject(delegate {NpCameraStatus.Parse(b,n);},"Short CAM1 v2 accepted");}
        foreach(var edit in new[]{new uint[]{0,0},new uint[]{4,3},new uint[]{12,36},new uint[]{64,59},
            new uint[]{68,59},new uint[]{76,2},new uint[]{96,159999},new uint[]{100,319},new uint[]{104,319},
            new uint[]{112,57},new uint[]{120,2},new uint[]{124,1}}) {
            byte[] bad=(byte[])b.Clone();Put(bad,(int)edit[0],edit[1]);Reject(delegate {NpCameraStatus.Parse(bad,bad.Length);},"Invalid CAM1 v2 accepted");
        }
        var old=new byte[64];Buffer.BlockCopy(b,0,old,0,64);Put(old,4,1);
        Check(!NpCameraStatus.Parse(old,64).SupportsControls,"Legacy CAM1 enables v2 controls");
        byte[] stopped=(byte[])b.Clone();Put(stopped,64,0);Put(stopped,12,0);
        Check(NpCameraStatus.Parse(stopped,128).RateHz==0,"Disabled camera rate rejected");
        Put(stopped,68,0);Check(!NpCameraStatus.Parse(stopped,128).SupportsControls,"No-clock status enables controls");
        string path=Path.Combine(root,"camera_status.txt");s.Save(path);string saved=File.ReadAllText(path);
        Check(saved.Contains("requested_rate_hz=58") && saved.Contains("effective_rate_hz=35") && saved.Contains("measured_capture_millihz=34988"),"Requested/effective/measured rates conflated");
        byte[] cameraOnly=(byte[])b.Clone();Put(cameraOnly,8,256);Put(cameraOnly,12,55);Put(cameraOnly,68,55);
        Put(cameraOnly,72,75000000);Put(cameraOnly,108,150000000);Put(cameraOnly,116,0);Put(cameraOnly,120,1);
        s=NpCameraStatus.Parse(cameraOnly,128);
        Check(s.CameraOnlyActive && s.SupportsCameraOnly && s.RequestedHz==58 && s.RateHz==55 && s.MeasuredMillihz==0,"Camera-only rate/capability or achieved-rate claim wrong");
        Put(cameraOnly,104,500);s=NpCameraStatus.Parse(cameraOnly,128);
        Check(s.Height==500 && s.FrameBytes==160000,"Corrected retained-raster geometry rejected");
        Put(cameraOnly,120,0);Reject(delegate {NpCameraStatus.Parse(cameraOnly,128);},"Unadvertised camera-only status accepted");
        var caps=new byte[48];uint[] words={0x314d504e,1,7,15,15,3,0,0,0x76,256,5,1};
        for(int i=0;i<words.Length;i++)Put(caps,i*4,words[i]);
        var mode=NpModeCapabilities.Parse(caps,48);
        Check(mode.CameraOnlyActive && mode.SupportsCameraOnly,"Zero-probe camera-only capabilities rejected");
        Reject(delegate {var active=mode.Active;},"Camera-only creates a neural profile");
        Check(!NpCaptureMode.Valid(0,0) && !NpCaptureMode.Valid(0,30000),"Camera-only loosened NP mode validity");
        Put(caps,44,0);Reject(delegate {NpModeCapabilities.Parse(caps,48);},"Older firmware enables camera-only");
        Put(caps,24,1);Put(caps,28,30000);Check(!NpModeCapabilities.Parse(caps,48).SupportsCameraOnly,"Legacy NP descriptor marked camera-only capable");
        Put(caps,44,1);mode=NpModeCapabilities.Parse(caps,48);
        Check(mode.SupportsCameraOnly && !mode.CameraOnlyActive,"Combined mode loses camera-only capability");
        Put(caps,40,7);mode=NpModeCapabilities.Parse(caps,48);
        Check(mode.SupportsPresence && !mode.SupportsCameraOnly && mode.PresenceRequestedMask==1,"Legacy presence P1 bit confused with camera-only capability");
        Put(caps,40,5);Put(caps,44,2);Reject(delegate {NpModeCapabilities.Parse(caps,48);},"Unknown camera mode capability accepted");
    }
    [STAThread]
    static int Main(string[] args) {
        try {
            string root=args.Length==0?Path.Combine(Path.GetTempPath(),"np-camera-"+Guid.NewGuid().ToString("N")):args[0];
            Directory.CreateDirectory(root);
            Status(root);
            foreach(int mode in new[]{0,1,2,3}) {
                foreach(int chunk in new[]{1,5,6,7,109,110,111,511,512,513,4093,65536})Replay(root,mode,chunk);
                Replay(root,mode,4093,100);Replay(root,mode,511,-1,true);
                Wire wire=Build(mode);var legacy=new XeNpPacketParser();var tagged=new NpTaggedPacketParser(1,4);
                Reject(delegate {if(mode==2)tagged.Feed(wire.Bytes,wire.Bytes.Length,delegate {});else legacy.Feed(wire.Bytes,wire.Bytes.Length,delegate(byte[] b,int o,int n) {});},"Unrequested camera accepted");
                var bad=Packet(3,8,1,506);legacy=new XeNpPacketParser();tagged=new NpTaggedPacketParser(1,4);
                Reject(delegate {if(mode==2)tagged.Feed(bad,bad.Length,delegate {});else legacy.Feed(bad,bad.Length,delegate(byte[] b,int o,int n) {});},"Unsupported camera flags accepted");
                var end=Packet(3,2,1,104);legacy=new XeNpPacketParser();tagged=new NpTaggedPacketParser(1,4);
                legacy.OnCamera=delegate {};tagged.OnCamera=delegate {};
                if(mode==2)tagged.Feed(end,end.Length-1,delegate {});else legacy.Feed(end,end.Length-1,delegate(byte[] b,int o,int n) {});
                Reject(delegate {if(mode==2)tagged.Complete();else legacy.Complete();},"Truncated camera packet accepted");
            }
            using(var preview=new CameraPreviewForm(delegate {})) {
                preview.KeepAliveWhileRecording=true;
                var closing=new FormClosingEventArgs(CloseReason.UserClosing,false);
                typeof(CameraPreviewForm).GetMethod("OnFormClosing",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(preview,new object[]{closing});
                Check(closing.Cancel,"Camera preview close was not deferred during capture");
                var buffers=new List<byte[]>();byte[] b;
                for(int i=0;i<3;i++) {Check(preview.TryAcquireRawFrameBuffer(out b),"Preview pool released by hiding");Check(b.Length==160000,"Preview raster size changed");buffers.Add(b);}
                Check(!preview.TryAcquireRawFrameBuffer(out b),"Preview pool is not bounded");
                foreach(var buffer in buffers)preview.ReturnRawFrameBuffer(buffer);
                preview.KeepAliveWhileRecording=false;
            }
            Console.WriteLine("PASS NP camera USB framing/save: "+checks+" checks; arbitrary fragmentation, short tails, sequence wrap, independent camera damage, unchanged NP/misc payloads.");return 0;
        } catch(Exception e) {Console.Error.WriteLine(e);return 1;}
    }
}
