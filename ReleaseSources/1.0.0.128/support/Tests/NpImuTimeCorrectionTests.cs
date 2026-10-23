using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using System.Reflection;
using USB512_interface;

class NpImuTimeCorrectionTests
{
    const string Uid="001A0004434250062039314E";const long Pc=1792783000000000;
    static string root;static int checks;
    static void Check(bool value,string why){checks++;if(!value)throw new Exception(why);}
    static string Folder(string name){string f=Path.Combine(root,name);Directory.CreateDirectory(f);return f;}
    static void Put(byte[] b,int at,uint n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,at,4);}
    static byte[] Snapshot(uint seq,uint tick,uint native,uint valid=3) {
        var b=new byte[140];Put(b,0,0x31534e53);Put(b,4,1);Put(b,8,1);Put(b,12,valid);Put(b,16,tick);
        Put(b,20,seq);Put(b,24,seq);Put(b,32,tick);Put(b,36,tick);Put(b,44,native);
        for(int i=0;i<9;i++)Put(b,48+4*i,(uint)(100+i));return b;
    }
    static void Clock(string folder,uint start=0,string epoch=null,string uid=Uid) {
        var value=new{host_epoch=epoch??NpHostClock.Epoch,device_identity=uid,pc_clock=new{anchor_half_width_us=.1},
            clock_observations=new[]{new{DeviceMs=start,SendPcUs=Pc-50,ReceivePcUs=Pc+50},new{DeviceMs=unchecked(start+10000),SendPcUs=Pc+10000000-50,ReceivePcUs=Pc+10000000+50}}};
        File.WriteAllText(Path.Combine(folder,"first_sample_timestamps.json"),new JavaScriptSerializer().Serialize(value));
    }
    static string Hash(string path){using(var h=SHA256.Create())return Convert.ToBase64String(h.ComputeHash(File.ReadAllBytes(path)));}
    static double[] Times(string f){using(var b=new BinaryReader(File.OpenRead(Path.Combine(f,NpImuTimeCorrection.TimeName)))){var a=new List<double>();while(b.BaseStream.Position<b.BaseStream.Length)a.Add(b.ReadDouble());return a.ToArray();}}
    static void Journal(string f,params byte[][] records) {
        using(var j=new NpImuSampleJournal(f,Uid))foreach(var b in records)j.Observe(b,0,BitConverter.ToUInt32(b,16),BitConverter.ToUInt32(b,12),1,7,0,0);
    }
    static void Main(string[] args) {
        root=Path.Combine(Path.GetFullPath(args[0]),"run-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        string f=Folder("live");Clock(f);
        using(var sensor=new NpUsbSensors(f,true,Uid)) {
            var a=Snapshot(7,2000,10000);sensor.FeedSnapshot(a,0,2000);
            a=Snapshot(9,2010,10256);sensor.FeedSnapshot(a,0,2010);sensor.FeedSnapshot(a,0,2010);
            a=Snapshot(0,0,0,0);sensor.FeedSnapshot(a,0,2011);
            a=Snapshot(1,2020,10);sensor.FeedSnapshot(a,0,2020);
            Check(sensor.ImuSamples==3,"Journal changed valid-sample accounting");
        }
        string source=Hash(Path.Combine(f,NpImuSampleJournal.FileName)),held=Hash(Path.Combine(f,"analogin.dat"));
        string report=NpImuTimeCorrection.Correct(f);var times=Times(f);var csv=File.ReadAllLines(Path.Combine(f,NpImuTimeCorrection.CsvName));
        Check(times.Length==3&&csv.Length==4,"One timestamp per distinct valid retained sample");
        Check(Math.Abs(times[0]-(Pc+2000000)/1e6)<1e-6&&times[1]>times[0],"PC epoch or sample order wrong");
        Check(csv[1].Split(',').Length==26&&csv[1].Contains(",100,101,102,103,104,105,106,107,108,"),"Native measured values/units lost");
        Check(csv[2].Split(',')[11]=="1","Retained sequence gap not labeled");
        Check(csv[3].Contains("sensor_or_sequence_reset"),"Sensor reset not split");
        Check(report.Contains("\"mapped_samples\":3")&&report.Contains("I2C-completion"),"Wrong count/reference");
        var first=csv[1].Split(',');Check(Double.Parse(first[7])*1e6<=Pc+2000000&&Double.Parse(first[8])*1e6>=Pc+2000000,"Known completion time escaped PC bounds");
        Check(Hash(Path.Combine(f,NpImuSampleJournal.FileName))==source&&Hash(Path.Combine(f,"analogin.dat"))==held,"Original source/held grid modified");
        bool blocked=false;try{NpImuTimeCorrection.Correct(f);}catch(IOException){blocked=true;}Check(blocked,"Derived overwrite protection absent");NpImuTimeCorrection.Correct(f,true);
        File.WriteAllText(Path.Combine(f,"imu_native_samples_status.json"),"{\"error\":\"simulated flush failure\"}");
        Check(NpImuTimeCorrection.Correct(f,true).Contains("processed_incomplete_native_journal"),"Source journal write/flush error hidden");
        var state=new NpImuSampleState();var row=state.Accept(Snapshot(1,1000,0xffff80),0,1000,3,null,null,0,0);
        row=state.Accept(Snapshot(2,1010,128),0,1010,3,null,null,0,0);
        Check(row.segment==0&&row.sensor_time_unwrapped==0x1000080,"24-bit wrap reconstructed incorrectly");
        row=state.Accept(Snapshot(3,301010,256),0,301010,3,null,null,0,0);
        Check(row.segment==1&&row.reset_reason=="sensor_wrap_or_reset_ambiguous","Ambiguous long gap silently unwrapped");
        f=Folder("mcu-wrap");uint start=0xfffff000;Clock(f,start);Journal(f,Snapshot(1,unchecked(start+2000),1000),Snapshot(2,unchecked(start+5000),77800));
        NpImuTimeCorrection.Correct(f);times=Times(f);Check(times.Length==2&&times.All(x=>!Double.IsNaN(x))&&times[1]>times[0],"MCU millisecond wrap rejected or reversed");
        f=Folder("mcu-reset");Clock(f);Journal(f,Snapshot(100,5000,10000),Snapshot(1,50,128));NpImuTimeCorrection.Correct(f);times=Times(f);
        Check(!Double.IsNaN(times[0])&&Double.IsNaN(times[1]),"MCU reset mapped across old clock epoch");
        Check(File.ReadAllText(Path.Combine(f,NpImuTimeCorrection.CsvName)).Contains("mcu_reset_unbound"),"Missing reset validity label");
        f=Folder("identity");Clock(f,0,"different-host");Journal(f,Snapshot(1,2000,100));blocked=false;
        try{NpImuTimeCorrection.Correct(f);}catch(InvalidDataException){blocked=true;}Check(blocked&&!File.Exists(Path.Combine(f,NpImuTimeCorrection.TimeName)),"Mixed host epochs accepted");
        f=Folder("uid");Clock(f,0,null,"001D00065942500B20313354");Journal(f,Snapshot(1,2000,100));blocked=false;
        try{NpImuTimeCorrection.Correct(f);}catch(InvalidDataException){blocked=true;}Check(blocked,"Mixed MCU identities accepted");
        f=Folder("no-clock");Journal(f,Snapshot(1,2000,100));NpImuTimeCorrection.Correct(f);
        Check(Double.IsNaN(Times(f)[0]),"No-query recording acquired invented PC time");
        f=Folder("raw-disagreement");Clock(f);Journal(f,Snapshot(1,2000,100));
        string jpath=Path.Combine(f,NpImuSampleJournal.FileName);File.WriteAllText(jpath,File.ReadAllText(jpath).Replace("\"irq_ms\":2000","\"irq_ms\":2001"));blocked=false;
        try{NpImuTimeCorrection.Correct(f);}catch(InvalidDataException){blocked=true;}Check(blocked,"Edited timing fields accepted despite raw snapshot disagreement");
        f=Folder("boundary");Clock(f);Journal(f,Snapshot(1,0,100),Snapshot(2,10001,25600));NpImuTimeCorrection.Correct(f);
        Check(Times(f).All(Double.IsNaN),"Unbracketed events extrapolated");
        f=Folder("partial");File.WriteAllBytes(Path.Combine(f,"imu_snapshots.bin"),new byte[141]);blocked=false;
        try{NpImuTimeCorrection.Correct(f);}catch(InvalidDataException){blocked=true;}Check(blocked,"Partial raw snapshot accepted");
        int test=0;
        foreach(double prePhase in new[]{0.0,.999})foreach(double postPhase in new[]{0.0,.999})foreach(double eventPhase in new[]{0.0,.999})foreach(uint pos in new uint[]{100,5000,9999}) {
            f=Folder("phase-"+(test++));double rate=1000.02;
            var clock=new{host_epoch=NpHostClock.Epoch,device_identity=Uid,pc_clock=new{anchor_half_width_us=.1},clock_observations=new[]{
                new{DeviceMs=0U,SendPcUs=Pc+prePhase*rate-50,ReceivePcUs=Pc+prePhase*rate+50},
                new{DeviceMs=10000U,SendPcUs=Pc+(10000+postPhase)*rate-50,ReceivePcUs=Pc+(10000+postPhase)*rate+50}}};
            File.WriteAllText(Path.Combine(f,"first_sample_timestamps.json"),new JavaScriptSerializer().Serialize(clock));
            Journal(f,Snapshot(1,pos,100));NpImuTimeCorrection.Correct(f);var c=File.ReadAllLines(Path.Combine(f,NpImuTimeCorrection.CsvName))[1].Split(',');
            double truth=Pc+(pos+eventPhase)*rate;
            Check(Double.Parse(c[7])*1e6<=truth+1&&Double.Parse(c[8])*1e6>=truth-1,"Known fractional-ms phase escaped affine interval");
        }
        if(args.Length>2) {
            foreach(string name in new[]{"valid","invalid"}) {
                string input=Path.GetFullPath(args[name=="valid"?1:2]);f=Folder("replay-"+name);
                foreach(string file in new[]{"imu_snapshots.bin","first_sample_timestamps.json","clock_observations.csv","imu_observation_status.txt","experiment.json"})if(File.Exists(Path.Combine(input,file)))File.Copy(Path.Combine(input,file),Path.Combine(f,file));
                string before=Hash(Path.Combine(input,"imu_snapshots.bin"));report=NpImuTimeCorrection.Correct(f);times=Times(f);
                Check(Hash(Path.Combine(input,"imu_snapshots.bin"))==before&&Hash(Path.Combine(f,"imu_snapshots.bin"))==before,"Replay modified source");
                if(name=="valid")Check(times.Length==206&&times.All(x=>!Double.IsNaN(x))&&times.Zip(times.Skip(1),(a,b)=>b>a).All(x=>x),"Retained valid V2 replay missing/timestamp mismatch");
                else Check(times.Length==0&&report.Contains("no_valid_samples")&&report.Contains("1399"),"Invalid V2 snapshots fabricated samples");
                File.WriteAllText(Path.Combine(root,"replay-"+name+"-report.json"),report);
            }
        }
        if(args.Length>3) {
            var app=Assembly.LoadFrom(Path.GetFullPath(args[3]));const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
            string epoch=(string)app.GetType("USB512_interface.NpHostClock",true).GetField("Epoch",flags).GetValue(null);
            for(int family=1;family<=2;family++) {
                f=Folder("compiled-v"+family);string uid=family==1?"001D00065942500B20313354":Uid;Clock(f,0,epoch,uid);
                Type type=app.GetType("USB512_interface."+(family==1?"NpUsbSensors":"NpUsbSensorMonitor"),true);
                object target=Activator.CreateInstance(type,flags,null,family==1?new object[]{f,true,uid}:new object[]{null,f,uid,true,null},null);
                using((IDisposable)target)for(int sample=0;sample<2;sample++) {
                    var snap=Snapshot((uint)(7+sample),(uint)(2000+sample*5),(uint)(1000+sample*128));
                    if(family==1) {
                        var packet=new byte[506];System.Text.Encoding.ASCII.GetBytes("NPSENS01").CopyTo(packet,0);Put(packet,8,(uint)(40+sample));Put(packet,12,(uint)(2000+sample*5));Put(packet,16,140);snap.CopyTo(packet,20);
                        Put(packet,160,NpUsbSensors.Crc(packet,0,160));System.Text.Encoding.ASCII.GetBytes("NPUSB001").CopyTo(packet,164);Put(packet,172,1);Put(packet,176,3);Put(packet,502,NpUsbSensors.Crc(packet,0,502));
                        type.GetMethod("Feed",flags).Invoke(target,new object[]{packet,0,506});
                    } else {
                        var block=new byte[512];Put(block,52,128);snap.CopyTo(block,256);type.GetMethod("FeedBlock",flags).Invoke(target,new object[]{block,0});
                    }
                }
                var correction=app.GetType("USB512_interface.NpImuTimeCorrection",true).GetMethod("Correct",flags);
                string actual=(string)correction.Invoke(null,new object[]{f,false});
                Check(actual.Contains("\"samples\":2")&&actual.Contains("\"mapped_samples\":2"),"Compiled V"+family+" native journal/converter integration failed");
                Check(File.ReadAllLines(Path.Combine(f,NpImuSampleJournal.FileName)).Length==3,"Compiled integration lost native rows");
            }
        }
        Console.WriteLine("PASS "+checks+" checks: distinct samples, PC bounds, values, wraps/reset/gaps, identity/epoch, no extrapolation, source preservation, invalid/raw replay and overwrite protection; "+root);
    }
}
