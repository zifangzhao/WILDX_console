using System;
using System.Collections.Generic;
using System.IO;
using WildNpClosedLoop;

namespace USB512_interface
{
    // Called only after the neural header verifier. Source identity comes from
    // the existing timing owner, never from an event claiming its own session.
    internal sealed class NpClosedLoopV2UsbLog
    {
        readonly string folder,uid;
        readonly ulong token;
        readonly byte[] snapshot;
        readonly NpClosedLoopControl configuration;
        readonly uint probeMask;
        readonly Func<NpFirstSyncUsbFineRoot> currentSource;
        readonly Queue<byte[]> pending=new Queue<byte[]>();
        NpClosedLoopLog log;
        NpFirstSyncUsbFineRoot boundSource;
        uint generation,sourceEpoch,pendingDrops;
        bool havePendingDrops;
        string failure;
        internal NpClosedLoopV2UsbLog(string folder,string uid,ulong token,byte[] confirmed)
            :this(folder,uid,token,confirmed,0,null){}
        internal NpClosedLoopV2UsbLog(string folder,string uid,ulong token,byte[] confirmed,uint probeMask,Func<NpFirstSyncUsbFineRoot> currentSource)
        {
            this.folder=folder;this.uid=uid;this.token=token;snapshot=(byte[])confirmed.Clone();
            this.probeMask=probeMask;this.currentSource=currentSource;
            configuration=NpClosedLoopControl.Parse(snapshot);
            if(token==0)failure="No negotiated timing token; live event identity is unconfirmed.";
        }
        void OpenLog()
        {
            if(log!=null)return;
            log=new NpClosedLoopLog(folder,generation,snapshot);
            while(pending.Count!=0)Observe(pending.Dequeue(),0);
        }
        void BindCurrentSource()
        {
            if(currentSource==null)return;
            var root=currentSource();
            if(root==null){
                if(boundSource!=null)throw new InvalidDataException("Live first-sync source binding was invalidated.");
                return;
            }
            if(Object.ReferenceEquals(root,boundSource))return;
            if(root.Uid!=uid||root.Family!=2||root.Token!=token||root.Generation==0)
                throw new InvalidDataException("Live first-sync source UID/token/generation mismatch.");
            var info=NpFirstSyncUsb.ParseInfo(root.Info,uid,2);
            var receipt=NpFirstSyncWire.ValidateSourceAnchor(root.Anchor,root.Receipt);
            var anchor=NpAcquisitionAnchor.Parse(root.Anchor,BitConverter.ToUInt32(root.Anchor,8),2);
            if(!info.Ready||!info.FineAux||info.Token!=token||receipt.Family!=2||receipt.Generation!=root.Generation||
               receipt.Session!=root.Generation||receipt.Mask!=probeMask||probeMask!=7||receipt.ApHz!=configuration.ApHz||
               receipt.SourceHz!=configuration.ApHz*1600U||anchor.Origin!=0||
               boundSource!=null)
                throw new InvalidDataException("Live first-sync source profile/origin/generation mismatch.");
            NpFirstSyncUsbEvidence.CheckSource(receipt,root.Anchor,null,uid,2,root.Generation);
            generation=receipt.Session;sourceEpoch=receipt.SourceEpoch;boundSource=root;
            OpenLog();
        }
        internal void Feed(byte[] b,int o)
        {
            if(failure!=null)return;
            try{
                BindCurrentSource();
                uint flags=BitConverter.ToUInt32(b,o+52);
                if((flags&NpClosedLoopCarrier.V2Flag)==0)return;
                NpClosedLoopCarrier.ValidateV2Structure(b,o,flags);
                if(BitConverter.ToUInt32(b,o+252)!=configuration.ConfigCrc)throw new InvalidDataException("Live event configuration changed.");
                // A negotiated current service must not downgrade to legacy
                // kind2. The current pages/root remain owned by timing.
                if(currentSource==null&&(flags&512)!=0&&!NpUsbTimingWire.IsAbsent(b,o+396)&&BitConverter.ToUInt16(b,o+402)==2){
                    var record=new byte[108];Buffer.BlockCopy(b,o+396,record,0,108);
                    var timing=NpUsbTimingWire.Parse(record,uid,2,token,BitConverter.ToUInt64(b,o+24));
                    if(timing.Kind==2){
                        var anchor=NpAcquisitionAnchor.Parse(timing.Body,BitConverter.ToUInt32(timing.Body,8),2);
                        if(!anchor.SourceValid||anchor.Origin!=0||anchor.SampleHz!=configuration.ApHz||anchor.Generation==0||
                           generation!=0&&generation!=anchor.Generation)throw new InvalidDataException("Live event source origin/generation is unconfirmed.");
                        generation=sourceEpoch=anchor.Generation;
                        OpenLog();
                    }
                }
                if(log!=null)Observe(b,o);
                else{
                    // Keep actual events and drop changes, not every empty
                    // neural header while the optional timing worker catches up.
                    var packet=NpClosedLoopCarrier.ReadV2(b,o,flags,0);uint drops=BitConverter.ToUInt32(b,o+248);
                    if(havePendingDrops&&drops<pendingDrops)throw new InvalidDataException("Live event drop counter changed.");
                    if(packet!=null||!havePendingDrops||drops!=pendingDrops){
                        if(pending.Count==16)throw new InvalidDataException("Live events arrived without a bound startup timing receipt.");
                        var header=new byte[512];Buffer.BlockCopy(b,o,header,0,512);pending.Enqueue(header);
                    }
                    pendingDrops=drops;havePendingDrops=true;
                }
            }catch(Exception e) when(e is IOException||e is InvalidDataException||e is ArgumentException){failure=e.Message;pending.Clear();}
        }
        void Observe(byte[] b,int o)
        {
            uint flags=BitConverter.ToUInt32(b,o+52);var e=NpClosedLoopCarrier.ReadV2(b,o,flags,generation);
            if(e!=null&&e.Kind!=5&&e.SourceEpoch!=sourceEpoch)throw new InvalidDataException("Live detection has no current-generation source proof.");
            log.ObserveV2(b,o,flags);
        }
        internal void Complete()
        {
            // The worker drains timing before this call; a delayed validated
            // root can finish the bounded startup queue without another USB I/O.
            if(failure==null)try{BindCurrentSource();}
                catch(Exception e) when(e is IOException||e is InvalidDataException||e is ArgumentException){failure=e.Message;pending.Clear();}
            if(log!=null){if(failure==null)log.Complete();log.Dispose();}
            if(log==null&&failure==null)failure="No independently validated current-generation source receipt; detections unavailable.";
            if(failure!=null)try{File.WriteAllText(Path.Combine(folder,"closed_loop_status.txt"),"Event export incomplete: "+failure+"\nNeural data validation is independent. No claim of zero detections.\n");}
                catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
}
