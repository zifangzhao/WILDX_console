using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace USB512_interface
{
    // Offline only. Reuse the exact USB wire verifier/384-channel decoder;
    // never add decoding, checksums or preview work to the MCU recording path.
    public static class NpV2SdDatExport
    {
        private const int Sector = 512, Block = 46592;
        private const uint RuSectors = 364;
        private static readonly uint[] CrcTable = MakeCrcTable();
        public sealed class Segment
        {
            public uint FirstStoredBlock, StoredBlocks;
            public uint FirstLogicalBlock,LogicalEndExclusive;
            public ulong SourceStartTick, SourceEndTick;
            public readonly long[] RowOffsets, Rows;
            internal Segment(uint block, ulong tick, long[] rows)
            { FirstStoredBlock=FirstLogicalBlock=block; SourceStartTick=tick; RowOffsets=(long[])rows.Clone(); Rows=new long[rows.Length]; }
        }
        public sealed class ExportProgress {
            public string Stage;public ulong Completed,Total;public double ElapsedSeconds;
        }
        public sealed class Result
        {
            public uint SampleHz, Session, CommittedBlocks, ProbeMask;
            public uint StoredState, StoredFault;
            public bool PhysicalAppendOrder;
            public bool ScheduledImuGrid,ImuGridSourceWindowVerified;
            public ulong CommittedPhysicalSectors,CommittedAuxSectors;
            public uint ImuGridMetadataSchema;
            public bool ImuGridTailStatsValid;
            public ulong? ImuGridKnownUnsavedRows;
            public bool? ImuGridPendingRecordRowsUnknown;
            public string ImuGridTailError;
            public uint LogicalPrefixBlocks, LogicalHighWaterBlocks;
            public bool IndexedGridAppend,AdmittedHighWaterKnown;
            public uint AdmittedHighWaterBlocks,MissingStoredBlocks;
            public double IndexSeconds,DecodeSeconds;
            public string StoppedLossStatsComparison;
            public bool RecordingComplete { get { return StoredState==3 && StoredFault==0 && CommittedBlocks>0 && SourceGapCount==0 && OverflowEvents==0; } }
            public bool Lossless { get { return RecordingComplete; } }
            public bool SourceGapsAdvertised;
            public bool ImuAnalogGridNpLocked;
            public string ImuNativeFineTimestampsFile,ImuFineTimingStatusFile;
            public string ImuAnalogGridOriginEvidence;
            public bool StoppedLossStatsPresent;
            public uint OverflowEvents;
            public ulong ProducerDiscardedTicks;
            public uint SourceGapCount;
            public ulong MissingSourceTicks, DroppedBlocks, ElapsedSourceTicks;
            public ulong SourceTicks;
            public readonly List<Segment> Segments = new List<Segment>();
            public readonly long[] Rows;
            public readonly int[] Phases;
            public byte[] IdentityHeader { get; internal set; }
            public bool SensorSnapshotsAdvertised;
            public long SensorSnapshots,DistinctImuObservations,ImuSequenceGaps;
            public long ImuAnalogGridRows;
            public bool ImuAnalogGridComplete;
            public string OptionalImuError;
            public Result() : this(2) { }
            internal Result(int probes) { Rows = new long[probes]; Phases = new int[probes]; }
        }

        public static Result Export(Stream card, uint startLba, string output,
            long directoryEndLba = 0, Func<bool> cancelled = null, Action<int> progress = null)
        { return ExportCore(card,startLba,output,directoryEndLba,cancelled,progress,null,false); }

        public static Result ExportCalibrated(Stream card, uint startLba, string output, NpCalibrationSet calibration,
            long directoryEndLba = 0, Func<bool> cancelled = null, Action<int> progress = null)
        { if(calibration==null)throw new ArgumentNullException("calibration");return ExportCore(card,startLba,output,directoryEndLba,cancelled,progress,calibration.Count==0?null:calibration.Copy(),false); }

        // A stopped/interrupted recording can still contain a verifiable
        // committed prefix. Preserve its stored status; never infer completion.
        public static Result ExportCommitted(Stream card,uint startLba,string output,
            long directoryEndLba=0,Func<bool> cancelled=null,Action<int> progress=null,NpCalibrationSet calibration=null)
        { return ExportCore(card,startLba,output,directoryEndLba,cancelled,progress,calibration==null || calibration.Count==0?null:calibration.Copy(),true); }

        public static Result ExportCommittedWithStages(Stream card,uint startLba,string output,
            long directoryEndLba,Func<bool> cancelled,Action<int> progress,NpCalibrationSet calibration,Action<ExportProgress> stageProgress)
        {return ExportCore(card,startLba,output,directoryEndLba,cancelled,progress,calibration==null||calibration.Count==0?null:calibration.Copy(),true,null,stageProgress);}

        // Explicit seam for a qualified NP-reference-to-sensor clock relation.
        // Ordinary/legacy callers supply no origin and retain their fallback.
        public static Result ExportWithImuOrigin(Stream card,uint startLba,string output,NpSdImuGridOrigin imuOrigin,
            long directoryEndLba=0,Func<bool> cancelled=null,Action<int> progress=null,NpCalibrationSet calibration=null,bool allowIncomplete=false)
        {
            if(imuOrigin==null)throw new ArgumentNullException("imuOrigin");
            return ExportCore(card,startLba,output,directoryEndLba,cancelled,progress,
                calibration==null || calibration.Count==0?null:calibration.Copy(),allowIncomplete,imuOrigin);
        }

        static Result ExportCore(Stream card, uint startLba, string output,
            long directoryEndLba, Func<bool> cancelled, Action<int> progress, NpCalibrationSet calibration,bool allowIncomplete,NpSdImuGridOrigin imuOrigin=null,Action<ExportProgress> stageProgress=null)
        {
            if (card == null || !card.CanRead || !card.CanSeek) throw new ArgumentException("Read-only seekable card/image required.");
            if (String.IsNullOrWhiteSpace(output) || Directory.Exists(output) || File.Exists(output))
                throw new IOException("A fresh export directory is required; existing outputs are never overwritten.");
            byte[] desc = ReadAt(card, ((long)startLba + 3) * Sector, Sector);
            byte[] end = ReadAt(card, ((long)startLba + 4) * Sector, Sector);
            ValidateMetadataCore(desc, end, startLba, card.Length,allowIncomplete);
            bool grid16=U(desc,8)==16,hasClosedLoop=NpV2SdGrid.ClosedLoop(desc);
            // The scheduled carrier owns this format's grid. A legacy HAL-ms
            // origin DTO is neither used nor allowed to gate neural export.
            if(grid16)imuOrigin=null;
            bool appendOrder=StorageVersion(desc)==14,gridAppend=grid16&&U(desc,216)==3;
            if(appendOrder && (U(end,132)!=U(end,108) || U(end,136)!=U(end,108)))
                throw new InvalidDataException("Append-order recording has logical gaps: physical="+U(end,108)+
                    ", prefix="+U(end,132)+", high-water="+U(end,136)+
                    ". DAT export requires a verified contiguous logical extent. All stored units remain on the source; no padding or renumbering was performed.");
            uint probeMask = U(desc, 24);
            if(calibration!=null)calibration.RequireModel(true,(int)probeMask);
            int probes = probeMask == 1U ? 1 : probeMask == 3U ? 2 : 3, ticks = ProfileTicks(probeMask, U(desc, 16));
            // Triple kind3 stores fingerprints, not the three full selection maps.
            // Validate/archive those exact bytes without inferring applied wiring.
            byte[] configuration = probeMask != 3U ? ReadAt(card, ((long)startLba + 8) * Sector, 2048) : null;
            if (probeMask == 1U) ValidateSingleConfiguration(configuration, desc, U(end, 108));
            if (probeMask == 7U) ValidateTripleConfiguration(configuration, desc, U(end, 108));
            if(hasClosedLoop)WildNpClosedLoop.NpClosedLoopCarrier.ReadV2Configuration(configuration,desc);
            if (directoryEndLba != 0 && (directoryEndLba < U(end, 116) || directoryEndLba > U(desc, 68)))
                throw new InvalidDataException("SD catalog entry does not contain this committed recording extent.");
            byte[] utc = ReadAt(card, ((long)startLba + 5) * Sector, Sector);
            bool hasUtc = Magic(utc, "N2SDUTC1") || Magic(utc,"N2SDUTC2");
            if (hasUtc)try {ValidateUtc(utc, U(desc, 12), U(desc, 16));}catch(InvalidDataException){hasUtc=false;}
            bool hasLossStats=hasUtc && (U(utc,16)&8U)!=0U;
            var result = new Result(probes) { ProbeMask = probeMask, SampleHz = U(desc, 16), Session = U(desc, 12),
                CommittedBlocks = U(end, 108), SourceTicks = Q(end, 120),StoredState=U(end,100),StoredFault=U(end,104),
                PhysicalAppendOrder=appendOrder||gridAppend,IndexedGridAppend=gridAppend,
                LogicalPrefixBlocks=gridAppend?U(end,408):appendOrder?U(end,132):U(end,108),
                LogicalHighWaterBlocks=gridAppend?U(end,412):appendOrder?U(end,136):U(end,108) };
            if(gridAppend){result.AdmittedHighWaterKnown=(U(end,420)&1)!=0;result.AdmittedHighWaterBlocks=U(end,416);
                result.MissingStoredBlocks=result.LogicalHighWaterBlocks-result.CommittedBlocks;}
            result.ScheduledImuGrid=grid16;result.CommittedPhysicalSectors=grid16?Q(end,256):(ulong)result.CommittedBlocks*91;
            result.CommittedAuxSectors=grid16?Q(end,264):0;
            if(imuOrigin!=null){
                var bound=NpFirstSyncSd.ReadSector(utc,2,result.Session,result.SampleHz);
                if(bound==null||(bound.Receipt.Flags&(NpFirstSyncWire.Final|NpFirstSyncWire.AllPorts))!=(NpFirstSyncWire.Final|NpFirstSyncWire.AllPorts)||
                    bound.Receipt.Mask!=probeMask||bound.Receipt.ReferenceTick!=imuOrigin.ReferenceTick)
                    throw new InvalidDataException("Qualified IMU origin requires this recording's sealed final-generation NP reference receipt.");
                imuOrigin.RequireCapture(result.SampleHz*1600U,bound.Receipt.Generation);
            }
            if(hasLossStats && result.CommittedBlocks==0U)
                throw new InvalidDataException("Stopped loss statistics have no stored header to attest source-gap support.");
            // Index only the bounded durable physical extent. No output is
            // created until all four original headers in every RU are checked.
            uint[] appendIndex=appendOrder?IndexAppendUnits(card,desc,end,cancelled,progress):null;
            Directory.CreateDirectory(output);
            string receipt = Path.Combine(output, "np2_sd_export_status.txt");
            using (var f = new FileStream(receipt, FileMode.CreateNew, FileAccess.Write))
                WriteText(f, "Export complete: False\nExport is in progress; .partial files are not complete recordings.\n");
            var files = new FileStream[probes];
            var hashes = new SHA256[probes];
            var hashBuffers = new byte[probes][];
            var hashUsed = new int[probes];
            var digests = new string[probes];
            bool completed=false;
            WildNpClosedLoop.NpClosedLoopLog closedLoop=null;
            WildNpClosedLoop.NpClosedLoopCarrier.V2Summary closedLoopSummary=null;
            // Per-record +5/+6 binding is optional evidence. Only its explicit
            // Features9 declaration admits the additional fine sensor carrier.
            var fineContext=result.CommittedBlocks==0?null:NpFineSdArchive.Read(output,utc,2,result.Session,result.SampleHz,probeMask,
                ()=>ReadAt(card,((long)startLba+6)*Sector,Sector));
            NpRtcSdArchive.Capture(output,utc,fineContext,()=>ReadAt(card,((long)startLba+7)*Sector,Sector));
            var sensorExport=new NpSdImuExport(output,fineContext);
            NpV2SdGrid grid=null;
            try
            {
                File.WriteAllBytes(Path.Combine(output, "descriptor.bin"), desc);
                File.WriteAllBytes(Path.Combine(output, "footer.bin"), end);
                if (configuration != null) File.WriteAllBytes(Path.Combine(output, "configuration.bin"), configuration);
                if(hasClosedLoop){
                    if(grid16){if(!Zero(end,144,64))closedLoopSummary=WildNpClosedLoop.NpClosedLoopCarrier.ReadV2Summary(end,144,result.Session,U(desc,132));
                        else if(!allowIncomplete||U(end,100)==3)throw new InvalidDataException("Closed-loop final summary is missing.");}
                    else {byte[] baseD,baseF;closedLoopSummary=WildNpClosedLoop.NpClosedLoopCarrier.NormalizeV2Metadata(desc,end,allowIncomplete,out baseD,out baseF);}
                    var snapshot=new byte[64];Buffer.BlockCopy(configuration,1920,snapshot,0,64);
                    closedLoop=new WildNpClosedLoop.NpClosedLoopLog(output,result.Session,snapshot);
                }
                if (hasUtc) File.WriteAllBytes(Path.Combine(output, "utc.bin"), utc);
                if(calibration!=null)calibration.Save(output);
                if(result.CommittedBlocks==0){
                    File.WriteAllText(receipt,"Export complete: True\nRecording complete: False\nStored state: "+result.StoredState+"\nStored fault: "+result.StoredFault+"\nCommitted blocks: 0\nMetadata only; no committed samples and no DAT created.\n",Encoding.UTF8);
                    if(closedLoop!=null)closedLoop.Complete(closedLoopSummary);
                    completed=true;return result;
                }
                if(grid16)grid=new NpV2SdGrid(output,end,fineContext);
                AppendUnit[] gridIndex=null;
                if(gridAppend){var timer=System.Diagnostics.Stopwatch.StartNew();
                    gridIndex=IndexGridAppend(card,desc,end,grid,cancelled,(done,total)=>{
                        if(stageProgress!=null)stageProgress(new ExportProgress{Stage="Indexing neural headers and IMU",Completed=done,Total=total,ElapsedSeconds=timer.Elapsed.TotalSeconds});
                        if(progress!=null)progress(total==0?25:(int)(done*25/total));
                    });result.IndexSeconds=timer.Elapsed.TotalSeconds;}
                if(gridIndex!=null)using(var indexFile=new StreamWriter(Path.Combine(output,"neural_storage_index.csv"))){
                    indexFile.WriteLine("first_logical_block,physical_group,stored_blocks");
                    foreach(var unit in gridIndex)indexFile.WriteLine(unit.Logical+","+unit.Physical+",4");
                }
                for (int p = 0; p < probes; p++) {
                    files[p] = new FileStream(Path.Combine(output, Name(p) + ".partial"),
                        FileMode.CreateNew, FileAccess.Write, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
                    hashes[p]=SHA256.Create();hashBuffers[p]=new byte[768*128];
                }
                var rowBytes = new byte[384 * 2];
                Action<uint,uint> onFormat=(mask,hz)=> {
                    if (mask != probeMask || hz != result.SampleHz) throw new InvalidDataException("SD/DAT probe/rate contract mismatch.");
                };
                Action<int,short[]> onRow=(probe,row)=> {
                    if (probe < 1 || probe > probes || row.Length != 384) throw new InvalidDataException("Invalid decoded SD row.");
                    if(calibration!=null)calibration.Correct(probe,row,1,null,0);
                    // The existing decoder's row array is reused. Consume it
                    // synchronously here, never retain it in an asynchronous queue.
                    for (int c = 0; c < row.Length; c++) {
                        rowBytes[c * 2] = (byte)row[c]; rowBytes[c * 2 + 1] = (byte)(row[c] >> 8);
                    }
                    int p=probe-1;
                    files[p].Write(rowBytes, 0, rowBytes.Length);result.Rows[p]++;
                    // Hash emitted DAT bytes in bounded batches. A second full
                    // disk pass can otherwise take minutes after USB reaches100%.
                    Buffer.BlockCopy(rowBytes,0,hashBuffers[p],hashUsed[p],rowBytes.Length);
                    hashUsed[p]+=rowBytes.Length;
                    if(hashUsed[p]==hashBuffers[p].Length){hashes[p].TransformBlock(hashBuffers[p],0,hashUsed[p],null,0);hashUsed[p]=0;}
                };
                ulong initialSequence=0,initialTick=0;
                if(gridAppend&&gridIndex.Length!=0){var first=ReadAt(card,checked((long)U(desc,60)*Sector+(long)gridIndex[0].Physical*(364+U(desc,228))*Sector),Sector);
                    initialSequence=Q(first,24);initialTick=Q(first,32);}
                var decoder=gridAppend?NpV2SampleDecoder.OfflineSd(onFormat,onRow,initialTick):new NpV2SampleDecoder(onFormat,onRow);
                var verifier=gridAppend?NpV2WireVerifier.OfflineSd(initialSequence,initialTick):new NpV2WireVerifier(true);
                var decodeTimer=System.Diagnostics.Stopwatch.StartNew();
                var block = new byte[Block]; byte[] immutable = null,neuralCopy=null;
                Segment segment=null;ulong nextSource=0UL,nextLogical=initialSequence;
                card.Position = (long)U(desc, 60) * Sector;
                for (uint seq = 0; seq < result.CommittedBlocks; seq++) {
                    if (cancelled != null && cancelled()) throw new OperationCanceledException("SD DAT export cancelled; output remains partial.");
                    if(gridAppend&&seq%4==0)
                        card.Position=checked((long)U(desc,60)*Sector+(long)gridIndex[seq/4].Physical*(RuSectors+U(desc,228))*Sector);
                    else if(appendIndex!=null && seq%4==0)
                        card.Position=checked((long)U(desc,60)*Sector+(long)(appendIndex[seq/4]-1)*RuSectors*Sector);
                    uint logical=gridAppend?checked(gridIndex[seq/4].Logical+seq%4):seq;
                    ReadExact(card, block);
                    bool marked=(U(block,52)&NpV2WireVerifier.FlagSourceGaps)!=0U;
                    ulong source=Q(block,32);
                    if (Q(block, 24) != logical || !SourceTickValid(source,logical,(uint)ticks,marked) ||
                        U(block, 44) != result.SampleHz * 1600 || U(block, 60) != result.SampleHz ||
                        U(block, 48) != U(desc, 28) || U(block, 76) != probeMask || U(block, 80) != probeMask || U(block, 84) != 1 ||
                        U(block, 156) != U(desc, 36) || U(block, 160) != U(desc, 32) || (U(block, 52) & 40) != 40 ||
                        U(block, 56) != 0 || ((U(block,52)&NpSdImuSnapshot.Flag)==0U && U(block, 508) != 0) ||
                        (hasClosedLoop!=((U(block,52)&WildNpClosedLoop.NpClosedLoopCarrier.V2Flag)!=0)))
                        throw new InvalidDataException("SD block/header/sequence contract mismatch at block " + seq + ".");
                    // Sequence/tick and explicitly advertised extension bytes
                    // can vary; identity/phase/provenance stays immutable.
                    // Known optional SD sensor tail geometry is independent
                    // of clock/context validity. The base NN verifier still
                    // checks every other field on its private normalized copy.
                    if(NpSdImuSnapshot.Present(block))NpSdImuSnapshot.ValidateGeometry(U(block,52),true);
                    if (immutable == null) immutable = (byte[])block.Clone();
                    else {
                        uint flags=U(block,52),firstFlags=U(immutable,52);
                        bool sensorLayout=(flags&NpSdImuSnapshot.Flag)!=0U&&(firstFlags&NpSdImuSnapshot.Flag)!=0U;
                        int sensorStart=((flags|firstFlags)&NpSdImuSnapshot.FineFlag)!=0U?NpSdImuSnapshot.FineOffset:NpSdImuSnapshot.Offset;
                        for (int n = 0; n < Sector; n++) if ((n < 24 || n >= 40) &&
                            !(sensorLayout&&n>=52&&n<56&&((flags^firstFlags)&~NpSdImuSnapshot.FineFlag)==0U) &&
                            !((flags&64U)!=0U && n>=184 && n<184+U(block,180)) &&
                            !(sensorLayout&&n>=sensorStart) &&
                            !WildNpClosedLoop.NpClosedLoopCarrier.IsV2DynamicByte(flags,n) && block[n] != immutable[n])
                            throw new InvalidDataException("SD metadata changed at block " + seq + ".");
                    }
                    byte[] neural=block;
                    if(NpSdImuSnapshot.Present(block)){
                        sensorExport.Feed(block);
                        if(neuralCopy==null)neuralCopy=new byte[Block];
                        NpSdImuSnapshot.NormalizeNeuralCopy(block,neuralCopy,true);neural=neuralCopy;
                    }
                    bool storageBreak=gridAppend&&seq!=0&&logical!=nextLogical;
                    if(storageBreak)verifier.BeginStoredSegment(logical,source);
                    verifier.Feed(neural, 0);
                    if(closedLoop!=null)closedLoop.ObserveV2(neural,0,U(neural,52));
                    if(segment==null || storageBreak || source!=nextSource){
                        if(segment!=null)FinishSegment(segment,seq,nextSource,result.Rows,checked((uint)nextLogical));
                        segment=new Segment(seq,source,result.Rows){FirstLogicalBlock=logical};result.Segments.Add(segment);
                    }
                    decoder.Feed(neural, 0, verifier);
                    nextSource=checked(source+(uint)ticks);nextLogical=checked((ulong)logical+1);
                    if(grid16&&!gridAppend&&seq%4==3){for(uint a=0;a<U(desc,228);a++){
                        var aux=new byte[Sector];long position=card.Position;bool read=false;
                        try{ReadExact(card,aux);read=true;}catch(IOException ex){grid.ReadFailure(ex.Message);}
                        // A failed optional read cannot change the known fixed
                        // position of the next NN record. No magic scan/retry.
                        card.Position=checked(position+Sector);if(read)grid.Feed(aux,seq);
                    }}
                    if(seq%64==0||seq+1==result.CommittedBlocks){
                        if(stageProgress!=null)stageProgress(new ExportProgress{Stage="Downloading and decoding neural data",Completed=seq+1,Total=result.CommittedBlocks,ElapsedSeconds=decodeTimer.Elapsed.TotalSeconds});
                        if(progress!=null)progress(appendOrder||gridAppend?25+(int)((ulong)(seq+1)*74/result.CommittedBlocks):
                            (int)((ulong)(seq+1)*99/result.CommittedBlocks));
                    }
                }
                decoder.Complete();
                result.IdentityHeader=new byte[Sector];Buffer.BlockCopy(immutable,0,result.IdentityHeader,0,Sector);
                if(segment!=null)FinishSegment(segment,result.CommittedBlocks,nextSource,result.Rows,checked((uint)nextLogical));
                result.DecodeSeconds=decodeTimer.Elapsed.TotalSeconds;
                if(stageProgress!=null)stageProgress(new ExportProgress{Stage="Finalizing recording files",Completed=0,Total=1});
                result.SourceGapsAdvertised=verifier.SdSourceGapsEnabled;
                result.SourceGapCount=verifier.SourceGapCount;result.MissingSourceTicks=verifier.MissingSourceTicks;
                result.DroppedBlocks=verifier.DroppedBlocks;result.ElapsedSourceTicks=nextSource;
                if(imuOrigin!=null&&!sensorExport.FineAdvertised&&imuOrigin.ReferenceTick>=nextSource)
                    throw new InvalidDataException("Qualified IMU NP reference is outside the exported source extent.");
                if (verifier.Blocks != result.CommittedBlocks) throw new InvalidDataException("SD verifier extent mismatch.");
                if(hasLossStats&&gridAppend&&(result.MissingStoredBlocks!=0||initialSequence!=0)){
                    result.StoppedLossStatsComparison="Raw stopped statistics retained; logical storage holes prevent a whole-record acquisition-loss comparison.";
                    result.OverflowEvents=U(utc,80);result.ProducerDiscardedTicks=Q(utc,104);
                }
                else if(hasLossStats){
                    if(!result.SourceGapsAdvertised || U(utc,84)!=result.DroppedBlocks ||
                        Q(utc,88)!=result.MissingSourceTicks || Q(utc,96)!=result.ElapsedSourceTicks)
                        throw new InvalidDataException("Stopped loss statistics do not match the marked stored source extent.");
                    result.StoppedLossStatsPresent=true;result.OverflowEvents=U(utc,80);
                    result.ProducerDiscardedTicks=Q(utc,104);
                }
                for (int p = 0; p < probes; p++) {
                    if(cancelled!=null && cancelled())throw new OperationCanceledException("SD DAT export cancelled before finalization; output remains partial.");
                    result.Phases[p] = verifier.PhaseForProbe(p);
                    if(result.Phases[p]<0 || result.Phases[p]>=1600)
                        throw new InvalidDataException("SD DAT probe phase is unavailable.");
                    long expected=0L;
                    foreach(var part in result.Segments){
                        ulong first=FirstRowTick(part.SourceStartTick,result.Phases[p]);
                        long rows=part.SourceEndTick<=first?0L:checked((long)((part.SourceEndTick-first)/1600UL));
                        if(part.Rows[p]!=rows || part.RowOffsets[p]!=expected)
                            throw new InvalidDataException("SD DAT segment row count/offset mismatch.");
                        expected=checked(expected+rows);
                    }
                    if (result.Phases[p] < 0 || result.Rows[p] != expected || files[p].Length != expected * 768)
                        throw new InvalidDataException("SD DAT row count/length mismatch.");
                    if(hashUsed[p]>0)hashes[p].TransformBlock(hashBuffers[p],0,hashUsed[p],null,0);
                    hashes[p].TransformFinalBlock(new byte[0],0,0);
                    digests[p]=BitConverter.ToString(hashes[p].Hash).Replace("-","");
                    files[p].Flush(true); files[p].Dispose(); files[p] = null;
                }
                if(cancelled!=null && cancelled())throw new OperationCanceledException("SD DAT export cancelled before publishing; output remains partial.");
                var firstRows=new System.Collections.Generic.List<NpFirstSampleRow>();
                for(int p=0;p<probes;p++) {
                    var timingSegments=new System.Collections.Generic.List<NpSampleSegment>();
                    foreach(var part in result.Segments)if(part.Rows[p]>0)timingSegments.Add(new NpSampleSegment(part.RowOffsets[p],part.Rows[p],FirstRowTick(part.SourceStartTick,result.Phases[p])));
                    firstRows.Add(new NpFirstSampleRow(p+1,"ap",timingSegments.Count>0?(ulong?)timingSegments[0].first_source_tick:null,result.SampleHz).ForData(Name(p),384,result.Rows[p],result.SampleHz,1,timingSegments));
                }
                NpAcquisitionTiming.SaveSd(output,utc,result.Session,2,result.SampleHz,firstRows);
                // A supplied origin was matched to this sealed final receipt
                // before any output. Null never infers phase from UTC/ARM.
                ulong imuExtent=imuOrigin==null||sensorExport.FineAdvertised?result.ElapsedSourceTicks:
                    result.ElapsedSourceTicks>imuOrigin.ReferenceTick?result.ElapsedSourceTicks-imuOrigin.ReferenceTick:0;
                sensorExport.Complete(result.SampleHz*1600U,imuExtent,imuOrigin,cancelled,!grid16);sensorExport.Dispose();
                result.SensorSnapshotsAdvertised=sensorExport.Advertised;result.SensorSnapshots=sensorExport.Observations;
                result.DistinctImuObservations=sensorExport.Samples;result.ImuSequenceGaps=sensorExport.SequenceGaps;result.OptionalImuError=sensorExport.Error;
                result.ImuAnalogGridRows=sensorExport.GridRows;
                result.ImuAnalogGridComplete=sensorExport.GridComplete;
                result.ImuAnalogGridNpLocked=sensorExport.GridNpLocked;result.ImuAnalogGridOriginEvidence=sensorExport.GridOriginEvidence;
                if(!grid16&&fineContext!=null&&fineContext.FineCounter64){
                    var fineGrid=NpSdFineAnalog.Export(output,2,result.Session,result.SampleHz,probeMask,result.ElapsedSourceTicks,result.CommittedBlocks,
                        result.SourceGapCount==0&&result.OverflowEvents==0&&result.OptionalImuError==null,cancelled);
                    result.ImuFineTimingStatusFile=NpSdFineAnalogResult.StatusFile;
                    result.ImuNativeFineTimestampsFile=fineGrid.Applied?NpSdFineAnalogResult.NativeFile:null;
                    if(fineGrid.Applied){result.ImuAnalogGridRows=fineGrid.Rows;result.ImuAnalogGridNpLocked=true;result.ImuAnalogGridOriginEvidence="Validated Features25 continuous64 cache-observation intervals relative to NP STARTBYTE; no native conversion phase or PC UTC mapping";}
                    else {result.ImuAnalogGridNpLocked=false;result.OptionalImuError=fineGrid.Error;}
                }
                if(grid!=null){
                    bool first=GridFirstSource(fineContext,result,output);
                    grid.Complete(result.ElapsedSourceTicks,result.SourceGapCount==0&&result.OverflowEvents==0&&result.MissingStoredBlocks==0&&initialSequence==0,first);
                    grid.Dispose();result.ImuAnalogGridRows=checked((long)grid.Rows);result.ImuAnalogGridComplete=grid.OutputComplete;
                    result.ImuAnalogGridNpLocked=grid.PhaseQualified;result.ImuGridSourceWindowVerified=grid.SourceWindowVerified;
                    result.ImuGridMetadataSchema=grid.MetadataSchema;result.ImuGridTailStatsValid=grid.TailStatsValid;
                    result.ImuGridKnownUnsavedRows=grid.KnownUnsavedRows;result.ImuGridPendingRecordRowsUnknown=grid.PendingRecordRowsUnknown;result.ImuGridTailError=grid.TailError;
                    result.ImuAnalogGridOriginEvidence="SD16 contract2 scheduled cached-copy slots; retained phase and NP source window separate; native conversion phase unknown";
                    if(grid.Error!=null)result.OptionalImuError=grid.Error;
                }
                if(cancelled!=null&&cancelled())throw new OperationCanceledException("SD DAT export cancelled before publishing.");
                // Publish the success receipt LAST. No padding, phase relearning,
                // silent loss or fabricated LFP stream is permitted.
                for (int p = 0; p < probes; p++) File.Move(Path.Combine(output, Name(p) + ".partial"), Path.Combine(output, Name(p)));
                var report = new StringBuilder("Export complete: True\nStored terminal state: "+(result.StoredState==3 && result.StoredFault==0?"Complete":"Incomplete")+"\nRecording complete: "+result.RecordingComplete+"\nStored state: "+result.StoredState+"\nStored fault: "+result.StoredFault+"\n");
                if(result.SourceGapCount!=0)report.Append("Best-effort source loss: complete real AP rows were concatenated; no gap padding or cross-gap partial rows. The recording is not lossless.\n");
                else if(result.OverflowEvents!=0)report.Append("Stopped overflow events were recorded, even though the stored prefix has no source gap. The recording is not lossless.\n");
                else if(!result.RecordingComplete)report.Append("Only the verified committed prefix was exported; the recording remains incomplete.\n");
                report.AppendFormat(CultureInfo.InvariantCulture,
                    "Session: {0}\nSample rate Hz: {1}\nCommitted blocks: {2}\nSource ticks: {3}\n", result.Session, result.SampleHz, result.CommittedBlocks, result.SourceTicks);
                report.AppendFormat(CultureInfo.InvariantCulture,
                    "Lossless: {0}\nSource gaps advertised: {1}\nSource gap count: {2}\nMissing source ticks: {3}\nDropped source blocks: {4}\nElapsed source ticks: {5}\nFooter source ticks are stored tick quantity, not elapsed source span.\n",
                    result.Lossless,result.SourceGapsAdvertised,result.SourceGapCount,result.MissingSourceTicks,result.DroppedBlocks,result.ElapsedSourceTicks);
                if(result.StoppedLossStatsPresent)report.AppendFormat(CultureInfo.InvariantCulture,
                    "Stopped loss statistics: present\nPhysical overflow events: {0}\nProducer discarded raw ticks: {1}\nPhysical event/raw-discard counts are not inferred from stored source gaps.\n",
                    result.OverflowEvents,result.ProducerDiscardedTicks);
                else report.Append("Stopped loss statistics: not recorded; physical overflow events and producer-discarded ticks are unknown, not reconstructed from stored gaps.\n");
                if(result.StoppedLossStatsComparison!=null)report.AppendLine(result.StoppedLossStatsComparison);
                for(int n=0;n<result.Segments.Count;n++){
                    var part=result.Segments[n];
                    report.AppendFormat(CultureInfo.InvariantCulture,"Segment {0}: first_stored_block={1}; stored_blocks={2}; source_start_tick={3}; source_end_tick={4}\n",
                        n,part.FirstStoredBlock,part.StoredBlocks,part.SourceStartTick,part.SourceEndTick);
                    if(gridAppend)report.AppendLine("Segment "+n+": first_logical_block="+part.FirstLogicalBlock+"; logical_end_exclusive="+part.LogicalEndExclusive);
                    for(int p=0;p<probes;p++)report.AppendFormat(CultureInfo.InvariantCulture,
                        "Segment {0} probe {1}: row_offset={2}; rows={3}; first_source_tick={4}\n",
                        n,p+1,part.RowOffsets[p],part.Rows[p],FirstRowTick(part.SourceStartTick,result.Phases[p]));
                }
                if(appendOrder||gridAppend)report.Append("Storage order: physical append; original blocks verified and decoded in logical sequence order\n"+
                    "Physical durable blocks: "+result.CommittedBlocks+"\nLogical contiguous-prefix blocks: "+result.LogicalPrefixBlocks+
                    "\nLogical high-water blocks: "+result.LogicalHighWaterBlocks+
                    "\nFooter tick field is stored tick quantity, not elapsed logical time. Stored status/fault and original metadata are unchanged.\n");
                if(gridAppend){
                    report.AppendLine("Missing stored NN blocks below high-water: "+result.MissingStoredBlocks+"; storage holes are not inferred acquisition drops.");
                    report.AppendLine("Admitted high-water blocks: "+(result.AdmittedHighWaterKnown?result.AdmittedHighWaterBlocks.ToString():"unknown")+"; unknown or unsealed suffix is not fabricated.");
                    report.AppendLine("Index seconds: "+result.IndexSeconds.ToString("F6",CultureInfo.InvariantCulture)+"; download/decode seconds: "+result.DecodeSeconds.ToString("F6",CultureInfo.InvariantCulture));
                    using(var segments=new StreamWriter(Path.Combine(output,"neural_segments.csv"))){
                        segments.WriteLine("segment,probe,first_logical_block,logical_end_exclusive,first_stored_block,stored_blocks,source_start_tick,source_end_tick,file_row_offset,rows,first_row_source_tick");
                        for(int n=0;n<result.Segments.Count;n++){var part=result.Segments[n];for(int p=0;p<probes;p++)
                            segments.WriteLine(String.Join(",",n,p+1,part.FirstLogicalBlock,part.LogicalEndExclusive,part.FirstStoredBlock,part.StoredBlocks,part.SourceStartTick,part.SourceEndTick,part.RowOffsets[p],part.Rows[p],FirstRowTick(part.SourceStartTick,result.Phases[p])));}
                    }
                }
                report.Append("Format: per-probe AP, 384 channels, sample-major int16 little-endian, signed nominal ADC counts\n");
                report.Append("sample_encoding=" + (calibration==null?NpV2SampleDecoder.SampleEncoding:"signed_calibrated_nominal_adc_counts") + "\n");
                report.Append("sample_transform=" + NpV2SampleDecoder.SampleTransform + (calibration==null?"":"; then factory gain correction") + "\n");
                report.Append(calibration==null?"sample_range=-2047..2048\n":"sample_range=int16; factory-dependent; no clipping\n");
                report.Append("sample_polarity=inverted relative to raw ASIC ADC codes; ASIC negative analog gain undone\n");
                report.Append(calibration==null?"No gain/comparator calibration or microvolt conversion. ":"Factory host gain correction applied; see adc_calibration.json. No hardware trim or microvolt conversion claim. ");
                report.Append("Legacy DAT before WILDX 1.0.0.9 contains unsigned raw 12-bit codes; existing DAT files are not rewritten.\n");
                report.Append("Checks: descriptor/footer CRC32, exact immutable headers/"+(gridAppend?"original indexed sequence within stored segments":"dense sequence")+", source ticks contiguous or explicitly marked whole-block forward gaps, native SuperSync in every block, exact segment row/file lengths\n");
                if(result.SensorSnapshotsAdvertised)report.Append("IMU: recorded SNS2 latest-value snapshots; "+result.DistinctImuObservations+" distinct valid observations; see sensors.csv and imu_sd_export_status.txt. Sensor quality is independent of neural verification.\n");
                report.Append("No ADC payload CRC. Whole-superframe loss or arbitrary ADC bit errors are not excluded by this export. Live producer continuity and analog quality require separate hardware evidence.\n");
                report.Append("Boundary policy: trim incomplete leading/trailing AP rows in each contiguous source segment; do not pad. Probe rows have independent source-tick origins; use segment offsets for alignment.\n");
                if (probeMask == 1U) {
                    report.Append("Configuration: configuration.bin; N2IMRO1; planned/staged P1 request only, NOT applied-mapping proof. Physical electrode geometry unavailable.\n");
                }
                if (probeMask == 7U) {
                    report.AppendFormat(CultureInfo.InvariantCulture,
                        "Time base: source ticks / {0} Hz; AP step=1600/{0} seconds (exact rational).\n", result.SampleHz * 1600U);
                    report.AppendFormat(CultureInfo.InvariantCulture,
                        "Configuration: configuration.bin; N2IMRO2 kind3; staged_selection_mask=0x{0:X}; identity/request-CRC fingerprints only, NOT full maps or applied-mapping proof. Physical electrode geometry unavailable.\n", configuration[72]);
                    for (int p = 0; p < probes; p++) if ((configuration[72] & (1 << p)) != 0)
                        report.AppendFormat(CultureInfo.InvariantCulture, "Probe {0} staged configuration: ID={1}; request_CRC32={2:X8}; not applied proof\n",
                            p + 1, BitConverter.ToString(configuration, 128 + p * 36, 32).Replace("-", ""), U(configuration, 160 + p * 36));
                }
                for (int p = 0; p < probes; p++) report.AppendFormat(CultureInfo.InvariantCulture,
                    "Probe {0}: file={1}; rows={2}; phase={3}; first_source_tick={4}; trailing_ticks={5}; SHA256={6}\n",
                    p + 1, Name(p), result.Rows[p], result.Phases[p], FirstRowTick(result.Segments[0].SourceStartTick,result.Phases[p]),
                    SegmentTrailing(result.Segments[result.Segments.Count-1],result.Phases[p]), digests[p]);
                report.Append("SHA256 scope: emitted DAT bytes, computed during writing; flush/length checks, no second disk-readback pass.\n");
                report.Append(hasUtc ? "UTC: validated utc.bin; capture-start bracket only, not ADC synchronization\n" : "UTC: not present; no timing accuracy claim\n");
                File.WriteAllText(receipt, report.ToString(), Encoding.UTF8);
                if(closedLoop!=null)closedLoop.Complete(closedLoopSummary);
                completed=true;
                NpTimeCorrection.Automatic(output);
                return result;
            }
            catch (Exception ex)
            {
                File.WriteAllText(receipt, "Export complete: False\nPartial or unqualified output: do not treat as an error-free recording.\nError: " + ex.Message + "\n", Encoding.UTF8);
                throw;
            }
            finally {
                sensorExport.Dispose();
                if(grid!=null)grid.Dispose();
                if(closedLoop!=null)closedLoop.Dispose();
                foreach (var f in files) if (f != null) f.Dispose();
                foreach (var hash in hashes) if (hash != null) hash.Dispose();
                try { NpNeuroscopeXml.WriteFolder(output,(int)probeMask,false,(int)result.SampleHz,null,null); }
                catch(IOException ex) { File.AppendAllText(receipt,"Neuroscope XML unavailable: "+ex.Message+"\n"); }
                if(completed && progress!=null)progress(100);
            }
        }

        public static void ValidateMetadata(byte[] d, byte[] f, uint start, long length)
        { ValidateMetadataCore(d,f,start,length,false); }

        public static void ValidateCommittedMetadata(byte[] d,byte[] f,uint start,long length)
        { ValidateMetadataCore(d,f,start,length,true); }

        static void ValidateMetadataCore(byte[] d,byte[] f,uint start,long length,bool allowIncomplete)
        {
            if(d!=null&&d.Length>=12&&U(d,8)==16){NpV2SdGrid.Validate(d,f,start,length,allowIncomplete,Seal);return;}
            if(d!=null && d.Length>=12 && U(d,8)==15){
                byte[] baseD,baseF;
                WildNpClosedLoop.NpClosedLoopCarrier.NormalizeV2Metadata(d,f,allowIncomplete,out baseD,out baseF);
                ValidateMetadataCore(baseD,baseF,start,length,allowIncomplete);return;
            }
            if(d!=null && d.Length>=12 && U(d,8)==14){ValidateAppendMetadata(d,f,start,length,allowIncomplete);return;}
            Seal(d, "N2SDLOG1"); Seal(f, "N2SDEND1");
            int ticks = ProfileTicks(U(d, 24), U(d, 16));
            uint mask = U(d, 24);
            ulong payload = U(d, 60), limit = U(d, 64), end = U(d, 68);
            uint version=U(d,8);
            bool unitGeometry=version==12 || version==13;
            uint au=unitGeometry?U(d,128):0;
            ulong expectedPayload = ((ulong)start + 8192 + 16383) & ~16383UL;
            bool geometry=true;
            if(unitGeometry){
                if(version==12?(au<1024 || au%1024!=0):!IsLegacyAu(au))
                    throw new InvalidDataException("Invalid SD allocation unit for metadata version "+version+".");
                expectedPayload=AlignUp(expectedPayload,au);
                ulong auEnd=AlignUp(limit,au);
                geometry=payload<limit && end==AlignUp(auEnd,16384) &&
                    (version!=12 || (payload/1024<=0x3fffff && (auEnd-au)/1024<=0x3fffff));
            }
            ulong recordingEnd=unitGeometry?limit:end;
            ulong rus = recordingEnd > payload ? (recordingEnd - payload) / RuSectors : 0;
            if ((version!=11 && !unitGeometry) || !geometry || U(d, 12) == 0 ||
                U(d, 20) != U(d, 16) * 1600 || U(d, 28) != (mask==1U?7U:mask==3U?63U:511U) || U(d, 32) != (mask==7U?6U:4U) ||
                U(d, 36) != (mask==1U?3U:mask==3U?6U:9U) || U(d, 40) != Block || U(d, 44) != (uint)ticks || U(d, 48) != 91 || U(d, 52) != 4 ||
                U(d, 56) != start || start < 8192 || start % 8192 != 0 || payload != expectedPayload ||
                end % 16384 != 0 || end > U(d, 72) || rus == 0 || limit != payload + rus * RuSectors ||
                U(d, 96) != (version==13?47U:version==12?31U:15U) || U(d, 100) != 1 || !Zero(d,104,24) ||
                (unitGeometry?!Zero(d,132,376):!Zero(d,128,380)))
                throw new InvalidDataException("Unsupported SD v11/v12/v13 NP2 profile/descriptor/geometry.");
            for (int n = 8; n < 100; n++) if (d[n] != f[n]) throw new InvalidDataException("SD descriptor/footer belong to different records.");
            ulong blocks = U(f, 108), committed = U(f, 112), writtenEnd = U(f, 116);
            if ((allowIncomplete ? U(f,100)<1 || U(f,100)>4 : U(f,100)!=3 || U(f,104)!=0 || blocks==0) || blocks % 4 != 0 ||
                committed != blocks / 4 || committed > rus || writtenEnd != payload + committed * RuSectors ||
                Q(f, 120) != blocks * (uint)ticks || writtenEnd * Sector > (ulong)length ||
                (unitGeometry?U(f,128)!=au || !Zero(f,132,376):!Zero(f,128,380)))
                throw new InvalidDataException("Incomplete, failed, torn or out-of-bounds SD recording; strict DAT export refused.");
        }

        static uint AppendBaseVersion(byte[] descriptor)
        {
            switch(U(descriptor,96)&~128U){case 79:return 11;case 95:return 12;case 111:return 13;
                default:throw new InvalidDataException("Unsupported v14 storage metadata flags.");}
        }

        static void ValidateAppendMetadata(byte[] d,byte[] f,uint start,long length,bool allowIncomplete)
        {
            if(d.Length!=Sector || f==null || f.Length!=Sector)throw new InvalidDataException("Missing v14 descriptor/footer sector.");
            Seal(d,"N2SDLOG1");Seal(f,"N2SDEND1");
            for(int n=8;n<100;n++)if(d[n]!=f[n])throw new InvalidDataException("V14 descriptor/footer identity mismatch.");
            if(!Zero(d,132,376) || !Zero(f,140,368))throw new InvalidDataException("Nonzero v14 reserved bytes.");
            uint version=AppendBaseVersion(d),physical=U(f,108),prefix=U(f,132),high=U(f,136),state=U(f,100),fault=U(f,104);
            // Private validation copies reuse the old exact geometry contract.
            // Original descriptor/footer bytes are never normalized or written.
            var legacyD=(byte[])d.Clone();var legacyF=(byte[])f.Clone();
            foreach(var bytes in new[]{legacyD,legacyF}){
                Put(bytes,8,version);Put(bytes,96,U(bytes,96)&~64U);
            }
            Array.Clear(legacyF,132,8);Put(legacyD,508,Crc(legacyD,508));Put(legacyF,508,Crc(legacyF,508));
            ValidateMetadataCore(legacyD,legacyF,start,length,allowIncomplete);
            ulong maximum=((ulong)U(d,64)-U(d,60))/91;
            if(prefix%4!=0 || high%4!=0 || prefix>physical || physical>high || high>maximum ||
                (physical==0 && (prefix!=0 || high!=0)) || ((prefix==physical)!=(high==physical)) ||
                (state==3 && (physical==0 || fault!=0)) || (state==4 && fault==0) ||
                (state==1 && (physical!=0 || fault==0)) || (state!=1 && state!=3 && state!=4) ||
                ((state==3 || state==4 && fault==0x56455249U) && (physical==0 || prefix!=physical)))
                throw new InvalidDataException("Invalid v14 physical/prefix/high-water counts or stored state: physical="+physical+", prefix="+prefix+", high-water="+high+".");
        }

        static uint[] IndexAppendUnits(Stream card,byte[] d,byte[] f,Func<bool> cancelled,Action<int> progress)
        {
            uint blocks=U(f,108),units=blocks/4;var index=new uint[checked((int)units)];
            var header=new byte[Sector];byte[] immutable=null;uint ticks=U(d,44);
            long payload=checked((long)U(d,60)*Sector);
            for(uint physical=0;physical<units;physical++){
                ulong first=0;
                for(uint within=0;within<4;within++){
                    if(cancelled!=null && cancelled())throw new OperationCanceledException("SD append-order indexing cancelled; source unchanged.");
                    card.Position=checked(payload+(long)physical*RuSectors*Sector+(long)within*Block);ReadExact(card,header);
                    ulong sequence=Q(header,24);
                    if(within==0){
                        first=sequence;
                        if(first%4!=0 || first>=(ulong)blocks || first+3>=(ulong)blocks || index[first/4]!=0)
                            throw new InvalidDataException("Duplicate, overlapping or out-of-range logical RU at physical unit "+physical+".");
                    }
                    if(sequence!=first+within || !SourceTickValid(Q(header,32),sequence,ticks,
                        (U(header,52)&NpV2WireVerifier.FlagSourceGaps)!=0U) || !Magic(header,"NP2USB03") ||
                        U(header,8)!=3 || U(header,12)!=Sector || U(header,16)!=Block || U(header,20)!=Block-Sector || U(header,40)!=ticks ||
                        U(header,44)!=U(d,20) || U(header,48)!=U(d,28) || U(header,60)!=U(d,16) ||
                        U(header,76)!=U(d,24) || U(header,80)!=U(d,24) || U(header,84)!=1 ||
                        U(header,156)!=U(d,36) || U(header,160)!=U(d,32) || (U(header,52)&40)!=40 ||
                        U(header,56)!=0 || ((U(header,52)&NpSdImuSnapshot.Flag)==0U && U(header,508)!=0) || ((U(header,52)&64)!=0 && U(header,180)>200))
                        throw new InvalidDataException("V14 native sequence/tick/profile header mismatch at physical unit "+physical+", block "+within+".");
                    if(NpSdImuSnapshot.Present(header))NpSdImuSnapshot.ValidateGeometry(U(header,52));
                    if(immutable==null)immutable=(byte[])header.Clone();
                    else for(int n=0;n<Sector;n++)if((n<24 || n>=40) &&
                        !((U(header,52)&64)!=0 && n>=184 && n<184+U(header,180)) &&
                        !((U(header,52)&NpSdImuSnapshot.Flag)!=0U && n>=NpSdImuSnapshot.Offset) &&
                        !WildNpClosedLoop.NpClosedLoopCarrier.IsV2DynamicByte(U(header,52),n) && header[n]!=immutable[n])
                        throw new InvalidDataException("V14 native provenance changed at physical unit "+physical+", block "+within+".");
                }
                index[first/4]=physical+1; // Zero means missing; preserve physical RU zero.
                if(progress!=null && (physical%64==0 || physical+1==units))progress((int)((ulong)(physical+1)*25/units));
            }
            for(int n=0;n<index.Length;n++)if(index[n]==0)throw new InvalidDataException("Missing logical RU "+n+" in committed v14 payload.");
            return index;
        }

        struct AppendUnit {internal uint Logical,Physical;}
        static AppendUnit[] IndexGridAppend(Stream card,byte[] d,byte[] f,NpV2SdGrid grid,Func<bool> cancelled,Action<ulong,ulong> progress)
        {
            uint groups=U(f,112),ticks=U(d,44),stride=U(d,232);ulong high=U(f,412);
            var index=new AppendUnit[checked((int)groups)];var header=new byte[Sector];byte[] immutable=null;
            long payload=checked((long)U(d,60)*Sector);
            for(uint physical=0;physical<groups;physical++){
                ulong first=0;
                for(uint within=0;within<4;within++){
                    if(cancelled!=null&&cancelled())throw new OperationCanceledException("SD16 indexing cancelled; source unchanged.");
                    card.Position=checked(payload+(long)physical*stride*Sector+(long)within*Block);ReadExact(card,header);
                    ulong sequence=Q(header,24);if(within==0){first=sequence;
                        if(first%4!=0||first>UInt32.MaxValue-4UL||first+4>high)throw new InvalidDataException("SD16 original logical group is unaligned or outside declared high-water.");}
                    uint flags=U(header,52);bool sensor=(flags&NpSdImuSnapshot.Flag)!=0;
                    if(sequence!=first+within||!SourceTickValid(Q(header,32),sequence,ticks,(flags&NpV2WireVerifier.FlagSourceGaps)!=0)||
                        !Magic(header,"NP2USB03")||U(header,8)!=3||U(header,12)!=Sector||U(header,16)!=Block||U(header,20)!=Block-Sector||
                        U(header,40)!=ticks||U(header,44)!=U(d,20)||U(header,48)!=U(d,28)||U(header,60)!=U(d,16)||
                        U(header,76)!=U(d,24)||U(header,80)!=U(d,24)||U(header,84)!=1||U(header,156)!=U(d,36)||U(header,160)!=U(d,32)||
                        (flags&40)!=40||U(header,56)!=0||(!sensor&&U(header,508)!=0)||
                        (((flags&WildNpClosedLoop.NpClosedLoopCarrier.V2Flag)!=0)!=NpV2SdGrid.ClosedLoop(d)))
                        throw new InvalidDataException("SD16 original logical/profile header mismatch at physical group "+physical+".");
                    if(sensor)NpSdImuSnapshot.ValidateGeometry(flags,true);
                    if(immutable==null)immutable=(byte[])header.Clone();
                    else {
                        uint originalFlags=U(immutable,52);bool sensorLayout=sensor&&(originalFlags&NpSdImuSnapshot.Flag)!=0;
                        int sensorStart=((flags|originalFlags)&NpSdImuSnapshot.FineFlag)!=0?NpSdImuSnapshot.FineOffset:NpSdImuSnapshot.Offset;
                        for(int n=0;n<Sector;n++)if((n<24||n>=40)&&
                            !(sensorLayout&&n>=52&&n<56&&((flags^originalFlags)&~NpSdImuSnapshot.FineFlag)==0)&&
                            !((flags&64)!=0&&n>=184&&n<184+U(header,180))&&!(sensorLayout&&n>=sensorStart)&&
                            !WildNpClosedLoop.NpClosedLoopCarrier.IsV2DynamicByte(flags,n)&&header[n]!=immutable[n])
                            throw new InvalidDataException("SD16 original header provenance changed during indexing.");
                    }
                }
                index[physical]=new AppendUnit{Logical=checked((uint)first),Physical=physical};
                card.Position=checked(payload+(long)physical*stride*Sector+4L*Block);
                for(uint a=0;a<U(d,228);a++){
                    var aux=new byte[Sector];long position=card.Position;bool read=false;
                    try{ReadExact(card,aux);read=true;}catch(IOException ex){grid.ReadFailure(ex.Message);}
                    card.Position=checked(position+Sector);if(read)grid.Feed(aux,first+3);
                }
                if(progress!=null&&(physical%64==0||physical+1==groups))progress(physical+1,groups);
            }
            Array.Sort(index,(a,b)=>a.Logical.CompareTo(b.Logical));
            uint prefix=0,derivedHigh=0;
            for(int n=0;n<index.Length;n++){
                uint logical=index[n].Logical;
                if(n!=0&&logical==index[n-1].Logical)throw new InvalidDataException("Duplicate SD16 original logical group.");
                if(logical==prefix)prefix=checked(prefix+4);derivedHigh=checked(logical+4);
            }
            if(prefix!=U(f,408)||derivedHigh!=U(f,412))throw new InvalidDataException("SD16 indexed logical prefix/high-water differs from sealed footer.");
            return index;
        }

        static bool UnitGeometry(byte[] descriptor)
        {uint version=StorageVersion(descriptor);return version==12 || version==13 || version==14 && AppendBaseVersion(descriptor)!=11;}
        static uint StorageVersion(byte[] descriptor){return U(descriptor,8)==16?U(descriptor,220):U(descriptor,8)==15?U(descriptor,136):U(descriptor,8);}
        static bool ConfigurationCapacity(byte[] b,byte[] d){
            ulong capacity=(ulong)U(d,64)-U(d,60),n=U(b,44);
            return U(d,8)==16?n/4*(364UL+U(d,228))==capacity:n*91<=capacity&&(!UnitGeometry(d)||n*91==capacity);
        }

        private static bool IsLegacyAu(uint unit)
        {
            switch(unit){case 2048:case 4096:case 8192:case 16384:case 24576:case 32768:
                case 49152:case 65536:case 131072:return true;default:return false;}
        }

        private static int ProfileTicks(uint mask, uint hz)
        {
            if (hz == 7500U || hz == 15000U || hz == 20000U || hz == 30000U) {
                if (mask == 1U) return 122880;
                if (mask == 3U) return 61440;
                if (mask == 7U) return 40960;
            }
            throw new InvalidDataException("Unsupported SD NP2 probe/rate tuple.");
        }

        private static void ValidateSingleConfiguration(byte[] b, byte[] d, uint committed)
        {
            uint kind=U(b,12);
            if (!Magic(b,"N2SDCFG1") || U(b,8)!=1U || (kind!=1U && kind!=2U) ||
                Crc(b,b.Length)!=U(d,76) || U(b,16)!=U(d,16) || U(b,20)!=U(d,20) ||
                U(b,24)!=1U || U(b,28)!=7U || U(b,32)!=4U || U(b,36)!=3U || U(b,40)!=Block ||
                U(b,44)<committed || U(b,44)%4!=0 ||
                !ConfigurationCapacity(b,d) ||
                !Zero(b,71,1) || b[72]!=(kind==2U?1:0) || !Zero(b,73,55) || !Zero(b,1004,1044))
                throw new InvalidDataException("Invalid single-probe SD configuration provenance.");
            for(int n=0;n<16;n++)if(b[48+n]!=d[80+n])
                throw new InvalidDataException("Single-probe configuration/card identity mismatch.");
            for(int n=0;n<7;n++)if(b[64+n]!="N2IMRO1"[n])
                throw new InvalidDataException("Unsupported single-probe SD selection provenance.");
            if(kind==1U){
                if(!Zero(b,128,876))throw new InvalidDataException("Planned profile contains an unexpected selection request.");
            }else if(Encoding.ASCII.GetString(b,128,4)!="N2B1" || b[132]!=4 || !Zero(b,133,3))
                throw new InvalidDataException("Single-probe staged selection is not an NP2013 P1 request.");
        }

        private static void ValidateTripleConfiguration(byte[] b, byte[] d, uint committed)
        {
            if (!Magic(b, "N2SDCFG1") || U(b, 8) != 1U || U(b, 12) != 3U ||
                Crc(b, b.Length) != U(d, 76) || U(b, 16) != U(d, 16) || U(b, 20) != U(d, 20) ||
                U(b, 24) != 7U || U(b, 28) != 511U || U(b, 32) != 6U || U(b, 36) != 9U || U(b, 40) != Block ||
                U(b, 44) < committed || U(b, 44) % 4 != 0 ||
                !ConfigurationCapacity(b,d) ||
                !Zero(b, 71, 1) || (b[72] & ~7) != 0 || !Zero(b, 73, 55) || !Zero(b, 236, NpV2SdGrid.ClosedLoop(d)?1652:1812))
                throw new InvalidDataException("Invalid triple SD configuration/fingerprint provenance.");
            for (int n = 0; n < 16; n++) if (b[48 + n] != d[80 + n])
                throw new InvalidDataException("Triple configuration/card identity mismatch.");
            for (int n = 0; n < 7; n++) if (b[64 + n] != "N2IMRO2"[n])
                throw new InvalidDataException("Unsupported triple SD selection provenance.");
            for (int p = 0; p < 3; p++) if ((b[72] & (1 << p)) == 0 && !Zero(b, 128 + p * 36, 36))
                throw new InvalidDataException("Unselected triple port has unexpected fingerprints.");
        }

        private static void ValidateUtc(byte[] b, uint session, uint hz)
        {
            uint version=U(b,8);Seal(b, version==2?"N2SDUTC2":"N2SDUTC1"); uint flags = U(b, 16);
            if(version==2&&!Zero(b,160,348)){
                // Only a recognized, sealed, recording-bound extension may be
                // removed for the unchanged legacy clock/loss-stat validator.
                // Archive the original sector, not this private validation copy.
                if(NpFirstSyncSd.ReadSector(b,2,session,hz)==null)
                    throw new InvalidDataException("Invalid SD first-sync UTC extension.");
                b=(byte[])b.Clone();Array.Clear(b,160,348);NpAcquisitionAnchor.Put(b,508,NpAcquisitionAnchor.Crc(b,508));
            }
            int reserved=(flags&8U)!=0U?112:80;
            if ((version!=1 && version!=2) || U(b, 12) != session || (flags & ~15U) != 0 ||
                U(b, 72) != hz || U(b, 76) != 1000 || !Zero(b, reserved, 508-reserved) ||
                ((flags & 1) != 0 && (U(b, 20) == 0 || U(b, 24) < 1000 || Q(b, 32) == 0)))
                throw new InvalidDataException("Invalid SD UTC companion.");
            if ((flags & 3) == 3) for (int p = 0; p < 2; p++) {
                uint delta = unchecked(U(b, 48 + p * 4) - U(b, 28));
                if (delta >= 0x80000000 || Q(b, 56 + p * 8) != checked(Q(b, 32) + (ulong)delta * 1000))
                    throw new InvalidDataException("SD UTC stamp does not match its anchor.");
            }
        }

        static bool GridFirstSource(NpFineSdCompanion context,Result result,string folder){
            try{
                if(context==null||!context.FineCounter64||context.Bound==null||result.IdentityHeader==null)return false;
                var bound=context.Bound;var r=bound.Receipt;
                var clock=NpSensorFineTime.FromSd(File.ReadAllBytes(Path.Combine(folder,"recorded_clock.bin")),
                    File.ReadAllBytes(Path.Combine(folder,NpFineSdArchive.RawFile)),2,result.Session,result.SampleHz,bound.Uid,bound.Token);
                byte[] h=result.IdentityHeader;
                if(clock.Family!=2||Q(context.Anchor,44)!=0||r.Mask!=result.ProbeMask||r.ApHz!=result.SampleHz||r.SourceHz!=result.SampleHz*1600U||
                    !Magic(h,"NP2USB03")||Q(h,24)!=0||Q(h,32)!=0||U(h,76)!=r.Mask||U(h,80)!=r.Mask||U(h,60)!=r.ApHz||U(h,44)!=r.SourceHz)return false;
                for(int p=0;p<result.Rows.Length;p++){
                    var pos=r.Probes[p];int phase=result.Phases[p];
                    if(pos.Counter!=UInt32.MaxValue||pos.Phase!=phase||pos.FirstTick!=(ulong)((1600-phase)%1600)||result.Rows[p]<=0)return false;
                    using(var file=new FileStream(Path.Combine(folder,Name(p)+".partial"),FileMode.Open,FileAccess.Read,FileShare.None))
                        if(file.Length!=checked(result.Rows[p]*768))return false;
                }
                return true;
            }catch(Exception ex) when(ex is IOException||ex is InvalidDataException||ex is UnauthorizedAccessException||ex is ArgumentException||ex is OverflowException){return false;}
        }
        private static string Name(int p) { return "neuropixels_probe" + (p + 1) + "_ap.dat"; }
        private static bool SourceTickValid(ulong source,ulong sequence,uint ticks,bool marked)
        {
            if(ticks==0U || sequence>UInt64.MaxValue/ticks || (sequence==0UL && source!=0UL))return false;
            ulong minimum=sequence*ticks;
            return marked?source>=minimum && (source-minimum)%ticks==0UL:source==minimum;
        }
        private static ulong FirstRowTick(ulong start,int phase)
        {return checked(start+(1600UL-(start%1600UL+(ulong)phase)%1600UL)%1600UL);}
        private static ulong SegmentTrailing(Segment part,int phase)
        {ulong first=FirstRowTick(part.SourceStartTick,phase);return part.SourceEndTick<=first?part.SourceEndTick-part.SourceStartTick:(part.SourceEndTick-first)%1600UL;}
        private static void FinishSegment(Segment part,uint endBlock,ulong endTick,long[] rows,uint? endLogical=null)
        {
            part.StoredBlocks=endBlock-part.FirstStoredBlock;part.LogicalEndExclusive=endLogical??endBlock;part.SourceEndTick=endTick;
            for(int p=0;p<rows.Length;p++)part.Rows[p]=rows[p]-part.RowOffsets[p];
        }
        private static void WriteText(Stream f, string text) { var b = Encoding.UTF8.GetBytes(text); f.Write(b, 0, b.Length); }
        private static byte[] ReadAt(Stream f, long at, int count) { f.Position = at; var b = new byte[count]; ReadExact(f, b); return b; }
        private static void ReadExact(Stream f, byte[] b) { int n = 0, got; while (n < b.Length && (got = f.Read(b, n, b.Length - n)) != 0) n += got; if (n != b.Length) throw new EndOfStreamException("Truncated SD record."); }
        private static bool Magic(byte[] b, string s) { for (int n = 0; n < s.Length; n++) if (b[n] != s[n]) return false; return true; }
        private static bool Zero(byte[] b, int at, int count) { for (int n = at; n < at + count; n++) if (b[n] != 0) return false; return true; }
        private static ulong AlignUp(ulong value,uint unit){return (value+unit-1)/unit*unit;}
        private static uint U(byte[] b, int n) { return BitConverter.ToUInt32(b, n); }
        private static void Put(byte[] b,int n,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,n,4);}
        private static ulong Q(byte[] b, int n) { return BitConverter.ToUInt64(b, n); }
        private static void Seal(byte[] b, string magic) { if (!Magic(b, magic) || U(b, 508) != Crc(b, 508)) throw new InvalidDataException("Missing/torn/corrupt SD metadata: " + magic); }
        private static uint[] MakeCrcTable() { var t = new uint[256]; for (uint i = 0; i < 256; i++) { uint c = i; for (int n = 0; n < 8; n++) c = (c >> 1) ^ ((c & 1) == 0 ? 0 : 0xedb88320U); t[i] = c; } return t; }
        private static uint Crc(byte[] b, int count) { uint c = uint.MaxValue; for (int n = 0; n < count; n++) c = CrcTable[(c ^ b[n]) & 255] ^ (c >> 8); return c ^ uint.MaxValue; }
    }
}
