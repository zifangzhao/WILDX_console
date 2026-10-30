using System;
using System.Collections.Generic;
using System.IO;
using WildNpClosedLoop;

namespace USB512_interface
{
    // Called only after the neural header verifier. Reuse the negotiated NPTB
    // parser; an event cannot attest its own capture session.
    internal sealed class NpClosedLoopV2UsbLog
    {
        readonly string folder,uid;
        readonly ulong token;
        readonly byte[] snapshot;
        readonly NpClosedLoopControl configuration;
        readonly Queue<byte[]> pending=new Queue<byte[]>();
        NpClosedLoopLog log;
        uint generation;
        string failure;
        internal NpClosedLoopV2UsbLog(string folder,string uid,ulong token,byte[] confirmed)
        {
            this.folder=folder;this.uid=uid;this.token=token;snapshot=(byte[])confirmed.Clone();
            configuration=NpClosedLoopControl.Parse(snapshot);
            if(token==0)failure="No negotiated timing token; live event identity is unconfirmed.";
        }
        internal void Feed(byte[] b,int o)
        {
            if(failure!=null)return;
            try{
                uint flags=BitConverter.ToUInt32(b,o+52);
                if((flags&NpClosedLoopCarrier.V2Flag)==0)return;
                NpClosedLoopCarrier.ValidateV2Structure(b,o,flags);
                if(BitConverter.ToUInt32(b,o+252)!=configuration.ConfigCrc)throw new InvalidDataException("Live event configuration changed.");
                // Only legacy kind2 supplies this logger's source binding.
                // The dedicated timing owner validates/archives page, root,
                // clock and future metadata without changing event logging.
                if((flags&512)!=0&&!NpUsbTimingWire.IsAbsent(b,o+396)&&BitConverter.ToUInt16(b,o+402)==2){
                    var record=new byte[108];Buffer.BlockCopy(b,o+396,record,0,108);
                    var timing=NpUsbTimingWire.Parse(record,uid,2,token,BitConverter.ToUInt64(b,o+24));
                    if(timing.Kind==2){
                        var anchor=NpAcquisitionAnchor.Parse(timing.Body,BitConverter.ToUInt32(timing.Body,8),2);
                        if(!anchor.SourceValid||anchor.Origin!=0||anchor.SampleHz!=configuration.ApHz||anchor.Generation==0||
                           generation!=0&&generation!=anchor.Generation)throw new InvalidDataException("Live event source origin/generation is unconfirmed.");
                        generation=anchor.Generation;
                        if(log==null){log=new NpClosedLoopLog(folder,generation,snapshot);while(pending.Count!=0)Observe(pending.Dequeue(),0);}
                    }
                }
                if(log!=null)Observe(b,o);
                else{
                    // A tiny bounded startup queue preserves events preceding
                    // the independent source receipt, never neural payload.
                    if(pending.Count==16)throw new InvalidDataException("Live events arrived without a bound startup timing receipt.");
                    var header=new byte[512];Buffer.BlockCopy(b,o,header,0,512);pending.Enqueue(header);
                }
            }catch(Exception e) when(e is IOException||e is InvalidDataException||e is ArgumentException){failure=e.Message;pending.Clear();}
        }
        void Observe(byte[] b,int o)
        {
            uint flags=BitConverter.ToUInt32(b,o+52);var e=NpClosedLoopCarrier.ReadV2(b,o,flags,generation);
            if(e!=null&&e.Kind!=5&&e.SourceEpoch!=generation)throw new InvalidDataException("Live detection has no current-generation source proof.");
            log.ObserveV2(b,o,flags);
        }
        internal void Complete()
        {
            if(log!=null){if(failure==null)log.Complete();log.Dispose();}
            if(log==null&&failure==null)failure="No independently validated current-generation source receipt; detections unavailable.";
            if(failure!=null)try{File.WriteAllText(Path.Combine(folder,"closed_loop_status.txt"),"Event export incomplete: "+failure+"\nNeural data validation is independent. No claim of zero detections.\n");}
                catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
}
