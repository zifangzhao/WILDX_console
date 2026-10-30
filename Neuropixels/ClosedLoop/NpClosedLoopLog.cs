using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace WildNpClosedLoop
{
    // Best-effort auxiliary logging. No exception from this class is allowed
    // to abort a caller's verified neural-data writer.
    public sealed class NpClosedLoopLog : IDisposable
    {
        readonly string folder;
        readonly uint session;
        readonly NpClosedLoopControl configuration;
        FileStream output;
        string failure;
        long records;
        uint carrierDrops;
        bool finished;
        NpClosedLoopLog(string folder,uint session,string error){this.folder=folder;this.session=session;failure=error;}
        public static NpClosedLoopLog FromV1Sd(string folder,uint session,byte[] clock)
        {
            try{
                var q=NpClosedLoopCarrier.ReadV1UtcConfiguration(clock);if(q==null)return null;
                if(Encoding.ASCII.GetString(clock,0,8)!="N1SDUTC2"||BitConverter.ToUInt32(clock,8)!=2||BitConverter.ToUInt32(clock,12)!=session||
                   BitConverter.ToUInt32(clock,508)!=NpClosedLoopEvent.Crc(clock,508)||q.ApHz!=BitConverter.ToUInt32(clock,72))
                    throw new InvalidDataException("Closed-loop snapshot is not bound to a valid recording clock/config sector.");
                var snapshot=new byte[64];Buffer.BlockCopy(clock,80,snapshot,0,64);return new NpClosedLoopLog(folder,session,snapshot);
            }catch(Exception e) when(IsAuxiliaryFailure(e)){return new NpClosedLoopLog(folder,session,e.Message);}
        }
        public static NpClosedLoopLog FromV1Usb(string folder,byte[] bytes,int offset,int count)
        {
            if(!NpClosedLoopCarrier.HasV1Usb(bytes,offset,count))return null;
            try{
                uint sid=NpClosedLoopCarrier.ValidateV1UsbEnvelope(bytes,offset,count);NpClosedLoopEvent e;
                NpClosedLoopCarrier.ReadV1UsbConfiguration(bytes,offset,sid,out e);
                var snapshot=new byte[64];Buffer.BlockCopy(bytes,offset+364,snapshot,0,64);return new NpClosedLoopLog(folder,sid,snapshot);
            }catch(Exception e) when(IsAuxiliaryFailure(e)){return new NpClosedLoopLog(folder,0,e.Message);}
        }
        public NpClosedLoopLog(string folder,uint session,byte[] confirmedConfiguration)
        {
            this.folder=folder;this.session=session;
            try{
                configuration=NpClosedLoopControl.Parse(confirmedConfiguration);
                if(session==0||configuration.Operation!=1||configuration.Channels.Length==0)
                    throw new InvalidDataException("A confirmed enabled configuration and session are required.");
                using(var snapshot=new FileStream(Path.Combine(folder,"closed_loop_config.bin"),FileMode.CreateNew,FileAccess.Write))
                    snapshot.Write(confirmedConfiguration,0,confirmedConfiguration.Length);
                output=new FileStream(Path.Combine(folder,"closed_loop_events.npcl.partial"),FileMode.CreateNew,FileAccess.Write,FileShare.Read);
            }catch(Exception e) when(IsAuxiliaryFailure(e)){Fail(e);}
        }
        static bool IsAuxiliaryFailure(Exception e){return e is IOException||e is InvalidDataException||e is UnauthorizedAccessException||e is ArgumentException;}
        void Fail(Exception e){failure=e.Message;Close();}
        void Close(){if(output!=null){try{output.Dispose();}catch(IOException e){failure=e.Message;}output=null;}}
        public void Observe(byte[] bytes,int offset)
        {
            if(finished||failure!=null||output==null)return;
            try{
                var packet=new byte[64];Buffer.BlockCopy(bytes,offset,packet,0,64);
                bool any=false;foreach(byte b in packet)any|=b!=0;if(!any)return;
                var e=NpClosedLoopEvent.Parse(packet);
                if(e.Session!=session||e.ConfigCrc!=configuration.ConfigCrc||e.Lane>=configuration.Channels.Length||
                   e.Probe!=configuration.Channels[e.Lane].Probe||e.Channel!=configuration.Channels[e.Lane].Channel)
                    throw new InvalidDataException("Event does not match the confirmed detector lane.");
                output.Write(packet,0,64);records++;
            }catch(Exception e) when(IsAuxiliaryFailure(e)){Fail(e);}
        }
        public void ObserveV2(byte[] bytes,int offset,uint flags)
        {
            if(finished||failure!=null||output==null)return;
            try{
                NpClosedLoopCarrier.ReadV2(bytes,offset,flags,session);
                uint drops=BitConverter.ToUInt32(bytes,offset+248);
                if(BitConverter.ToUInt32(bytes,offset+252)!=configuration.ConfigCrc||drops<carrierDrops)
                    throw new InvalidDataException("Closed-loop carrier configuration/drop counter changed.");
                carrierDrops=drops;Observe(bytes,offset+184);
            }catch(Exception e) when(IsAuxiliaryFailure(e)){Fail(e);}
        }
        public void ObserveV1Descriptor(byte[] bytes,int offset)
        {
            if(finished||failure!=null||output==null)return;
            try{
                NpClosedLoopCarrier.ReadV1Descriptor(bytes,offset,session);
                uint drops=BitConverter.ToUInt32(bytes,offset+456);
                if(BitConverter.ToUInt32(bytes,offset+452)!=configuration.ConfigCrc||drops<carrierDrops)
                    throw new InvalidDataException("V1 closed-loop carrier configuration/drop counter changed.");
                carrierDrops=drops;Observe(bytes,offset+388);
            }catch(Exception e) when(IsAuxiliaryFailure(e)){Fail(e);}
        }
        public void ObserveV1Usb(byte[] bytes,int offset,int count)
        {
            if(finished||failure!=null||output==null)return;
            try{
                NpClosedLoopEvent e;uint sid=NpClosedLoopCarrier.ValidateV1UsbEnvelope(bytes,offset,count);
                var q=NpClosedLoopCarrier.ReadV1UsbConfiguration(bytes,offset,sid,out e);uint drops=BitConverter.ToUInt32(bytes,offset+432);
                if(sid!=session||q.ConfigCrc!=configuration.ConfigCrc||drops<carrierDrops)
                    throw new InvalidDataException("V1 event carrier session/configuration/drop count changed.");
                carrierDrops=drops;Observe(bytes,offset+300);
            }catch(Exception e) when(IsAuxiliaryFailure(e)){Fail(e);}
        }
        public void CompleteV1(byte[] footer)
        {
            if(finished)return;string status=null;
            try{
                if(failure==null){
                    NpClosedLoopCarrier.ReadV1Descriptor(footer,0,session);
                    uint flags=BitConverter.ToUInt32(footer,460),drops=BitConverter.ToUInt32(footer,456);
                    if(BitConverter.ToUInt32(footer,452)!=configuration.ConfigCrc||drops<carrierDrops)
                        throw new InvalidDataException("V1 final closed-loop configuration/drop counter mismatch.");
                    status="Final cumulative drops: "+drops+"\nFinal stopped status: "+((flags&2)!=0)+
                        "\nSource timing unknown: "+((flags&4)!=0)+"\nDropped at Stop: "+((flags&16)!=0)+
                        "\nConfiguration saved: "+((flags&8)!=0)+"\n";
                }
            }catch(Exception e) when(IsAuxiliaryFailure(e)){Fail(e);}
            Complete(null,status);
        }
        public void Complete(NpClosedLoopCarrier.V2Summary summary=null,string finalStatus=null)
        {
            if(finished)return;finished=true;Close();
            try{
                if(failure==null){
                    string native=Path.Combine(folder,"closed_loop_events.npcl"),csv=Path.Combine(folder,"closed_loop_events.csv");
                    using(var input=File.OpenRead(native+".partial"))
                    using(var text=new StreamWriter(csv+".partial",false,new UTF8Encoding(false)))
                        NpClosedLoopEvent.Export(input,text,session,configuration.ConfigCrc);
                    File.Move(native+".partial",native);File.Move(csv+".partial",csv);
                }
                string status=failure==null?"Observed event packets validated and exported.":"Event export incomplete: "+failure;
                status+="\nObserved records: "+records.ToString(CultureInfo.InvariantCulture)+
                    "\nNo event packet is not evidence of zero detections. Native source ticks are not UTC.\n";
                if(finalStatus!=null)status+=finalStatus;
                else if(summary==null)status+="Final event-loss summary unavailable; final losses unknown.\n";
                else status+=String.Format(CultureInfo.InvariantCulture,
                    "Attempted: {0}\nPublished by firmware: {1}\nDropped: {2}\nDropped at Stop: {3}\nDelivery unknown: {4}\nObserved count matches published count: {5}\nPublication is not physical storage or host delivery.\n",
                    summary.Attempted,summary.Published,summary.Dropped,summary.TailDropped,summary.DeliveryUnknown,records==summary.Published);
                File.WriteAllText(Path.Combine(folder,"closed_loop_status.txt"),status,new UTF8Encoding(false));
            }catch(Exception e) when(IsAuxiliaryFailure(e)){
                failure=e.Message;
                try{File.WriteAllText(Path.Combine(folder,"closed_loop_status.txt"),"Event export incomplete: "+failure+"\nNeural data validation is independent.\n");}
                catch(Exception write) when(IsAuxiliaryFailure(write)){ }
            }
        }
        public void Dispose(){if(!finished){if(failure==null)failure="Recording/export ended before event-log finalization.";Complete();}}
    }
}
