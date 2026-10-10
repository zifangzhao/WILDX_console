using System;
using System.Collections.Generic;
using System.IO;

namespace CE32_console
{
    internal enum NpCe64LaneLayout { Auto=0, Gpio=1, Pssi=2, GpioV1Quad=3 }

    // NP1: 60 byte-ticks/frame, 13 frames/AP row and 12 AP rows/LFP row.
    // Startup may seek alignment; a recording never silently resynchronizes.
    internal sealed class NpCe64Decoder
    {
        internal const int Channels=384;
        private const int SuperframeBytes=780;
        private const int StartupBytes=2*SuperframeBytes;
        private const ushort SyncFrame=0x00CF, SyncSuperframe=0x0330;
        private readonly List<byte> pending=new List<byte>(65536);
        private readonly short[] lfpAssembly=new short[Channels];
        private readonly short[] apFrame=new short[Channels];
        private readonly ushort[] words=new ushort[32];
        private int lfpSlot, lfpCollected;
        private bool recordingResetDeclared, lfpPhaseInitialized;
        internal int InitialLfpSlot { get; private set; } = -1;
        internal uint FirstSuperframeCounter { get; private set; }
        internal long StartupDiscardedLfpSuperframes { get; private set; }
        internal long LfpPhaseErrors { get; private set; }
        // Only a verified new REC_NRESET epoch establishes absolute LFP phase.
        // A wrapped 20-bit counter alone cannot distinguish all LFP epochs.
        internal void DeclareRecordingReset()
        {
            if(locked || pending.Count!=0 || FramesChecked!=0 || failed || completed)
                throw new IOException("NP1 reset epoch cannot change after decoding starts.");
            recordingResetDeclared=true;
        }
        private bool locked, failed, completed;
        private uint lastCounter;
        private ushort declaredFixedWord;
        internal ushort ExpectedFixedWord { get; private set; }
        internal NpCe64LaneLayout Layout { get; private set; }
        internal long Resynchronizations { get; private set; }
        internal long StartupDiscardedBytes { get; private set; }
        internal long FramesChecked { get; private set; }
        internal long CounterErrors { get; private set; }
        internal long FrameErrors { get; private set; }

        internal NpCe64Decoder(NpCe64LaneLayout layout=NpCe64LaneLayout.Auto) {
            if(layout<NpCe64LaneLayout.Auto || layout>NpCe64LaneLayout.GpioV1Quad)throw new ArgumentOutOfRangeException("layout");
            Layout=layout;
        }
        internal void DeclareFixedWord(ushort value) {
            if(value!=0x11 && value!=0x22)throw new IOException("Unsupported NP model fixed word");
            if((declaredFixedWord!=0 && declaredFixedWord!=value) || (ExpectedFixedWord!=0 && ExpectedFixedWord!=value))
                throw new IOException("NP model changed during recording");
            if(declaredFixedWord==0 && (locked || pending.Count!=0 || FramesChecked!=0 || failed || completed))
                throw new IOException("NP model must be declared before decoding starts");
            declaredFixedWord=value;
        }
        internal void UseV1QuadFormat()
        {
            if(Layout==NpCe64LaneLayout.GpioV1Quad)return;
            if(locked || pending.Count!=0 || FramesChecked!=0 || failed || completed)
                throw new IOException("NP1 transport format changed during a recording.");
            Layout=NpCe64LaneLayout.GpioV1Quad;
        }
        private byte Lane(int offset,NpCe64LaneLayout layout)
        {
            byte raw=pending[offset];
            return layout==NpCe64LaneLayout.Pssi ? (byte)((raw<<1)|((raw>>7)&1)) : raw;
        }
        private ushort Word(int offset,int lineMask,int word,NpCe64LaneLayout layout)
        {
            ushort result=0;
            for(int bit=0;bit<10;bit++)
                if((Lane(offset+word*10+bit,layout)&lineMask)!=0)result|=(ushort)(1<<(9-bit));
            return result;
        }
        private bool FrameIsValid(int offset,ushort sync,NpCe64LaneLayout layout,ushort fixedWord)
        {
            if(Word(offset,1,0,layout)!=sync || Word(offset,0x80,2,layout)!=fixedWord)return false;
            int level=Lane(offset+10,layout)&1, transitions=0;
            for(int tick=11;tick<60;tick++) {
                int next=Lane(offset+tick,layout)&1;
                if(next!=level) { if(++transitions>1)return false; level=next; }
            }
            for(int tick=50;tick<60;tick++)if((Lane(offset+tick,layout)&0xFE)!=0)return false;
            return true;
        }
        private uint Counter(int offset,NpCe64LaneLayout layout)
        { return ((uint)Word(offset,0x80,3,layout)<<10)|Word(offset,0x80,4,layout); }
        private bool StartupIsValid(int offset,NpCe64LaneLayout layout,ushort fixedWord)
        {
            uint previous=0;
            // Qualify repeated superframe markers and continuity across their
            // boundary. This extra work runs only before the first sample.
            for(int f=0;f<26;f++) {
                int at=offset+f*60;
                if(!FrameIsValid(at,f%13==0?SyncSuperframe:SyncFrame,layout,fixedWord))return false;
                uint count=Counter(at,layout);
                if(f>0 && count!=((previous+1)&0xFFFFF))return false;
                previous=count;
            }
            return true;
        }
        private int FindAlignedStart()
        {
            for(int offset=0;offset<=pending.Count-StartupBytes;offset++) {
                if(Layout!=NpCe64LaneLayout.Auto) {
                    // FIXED is programmed SYNC_DATA, not a transport layout ID.
                    // V1 quad transport carries either approved startup profile:
                    // NRIC=0x22 or full NP1=0x11. Never learn arbitrary values or
                    // change the selected value after the initial qualification.
                    if((declaredFixedWord==0 || declaredFixedWord==0x22) && Layout==NpCe64LaneLayout.GpioV1Quad && StartupIsValid(offset,Layout,0x22)) {
                        ExpectedFixedWord=0x22;return offset;
                    }
                    if((declaredFixedWord==0 || declaredFixedWord==0x11) && StartupIsValid(offset,Layout,0x11)) {ExpectedFixedWord=0x11;return offset;}
                } else {
                    bool gpio=(declaredFixedWord==0 || declaredFixedWord==0x11) && StartupIsValid(offset,NpCe64LaneLayout.Gpio,0x11);
                    bool pssi=(declaredFixedWord==0 || declaredFixedWord==0x11) && StartupIsValid(offset,NpCe64LaneLayout.Pssi,0x11);
                    if(gpio && pssi)throw new IOException("Ambiguous NP1 lane layout; use explicit firmware capture-layout status.");
                    if(gpio || pssi) { ExpectedFixedWord=0x11;Layout=pssi?NpCe64LaneLayout.Pssi:NpCe64LaneLayout.Gpio;return offset; }
                }
            }
            return -1;
        }
        private void CheckFrame(int offset,ushort sync)
        {
            if(!FrameIsValid(offset,sync,Layout,ExpectedFixedWord)) {
                failed=true;FrameErrors++;throw new IOException("NP1 frame integrity failed; recording was not resynchronized.");
            }
            uint count=Counter(offset,Layout);
            if(FramesChecked>0 && count!=((lastCounter+1)&0xFFFFF)) {
                failed=true;CounterErrors++;throw new IOException("NP1 frame-counter discontinuity; recording is incomplete.");
            }
            lastCounter=count;FramesChecked++;
        }
        internal void Feed(byte[] lanes,Action<short[],int,short[],int> onSamples)
        { Feed(lanes,0,lanes.Length,onSamples); }
        internal void Feed(byte[] lanes,int offset,int count,Action<short[],int,short[],int> onSamples)
        {
            if(failed || completed)throw new InvalidOperationException("NP1 decoder session is closed.");
            if(lanes==null || offset<0 || count<0 || offset>lanes.Length-count)throw new ArgumentOutOfRangeException("count");
            for(int i=0;i<count;i++)pending.Add(lanes[offset+i]);
            if(!locked) {
                if(pending.Count<StartupBytes)return;
                int start=FindAlignedStart();
                if(start<0) {
                    int discard=pending.Count-(StartupBytes-1);
                    StartupDiscardedBytes+=discard;pending.RemoveRange(0,discard);
                    if(StartupDiscardedBytes>1024*1024) {
                        failed=true;throw new IOException("No valid NP1 framing in the first MiB; check lane layout, clock and probe configuration.");
                    }
                    return;
                }
                if(start>0) {
                    StartupDiscardedBytes+=start;
                    if(Layout!=NpCe64LaneLayout.GpioV1Quad)Resynchronizations++;
                    pending.RemoveRange(0,start);
                }
                locked=true;
            }
            int consumed=0;
            while(pending.Count-consumed>=SuperframeBytes) {
                for(int frame=0;frame<13;frame++)CheckFrame(consumed+frame*60,frame==0?SyncSuperframe:SyncFrame);
                DecodeSuperframe(consumed,onSamples);consumed+=SuperframeBytes;
            }
            if(consumed>0)pending.RemoveRange(0,consumed);
        }
        internal void Complete()
        {
            if(completed)return;
            if(failed)throw new IOException("NP1 decoder previously failed.");
            if(!locked)throw new IOException("No qualified pair of NP1 superframes was received.");
            for(int frame=0;frame<pending.Count/60;frame++)CheckFrame(frame*60,frame==0?SyncSuperframe:SyncFrame);
            completed=true;
        }
        private void DecodeSuperframe(int offset,Action<short[],int,short[],int> onSamples)
        {
            if(!lfpPhaseInitialized) {
                FirstSuperframeCounter=Counter(offset,Layout);
                if(recordingResetDeclared || Layout==NpCe64LaneLayout.GpioV1Quad) {
                    if(!recordingResetDeclared || FirstSuperframeCounter%13!=0) {
                        failed=true;LfpPhaseErrors++;
                        throw new IOException("NP1 LFP phase has no verified recording-reset counter epoch; no shifted LFP was emitted.");
                    }
                    lfpSlot=(int)((FirstSuperframeCounter/13)%12);
                }
                InitialLfpSlot=lfpSlot;lfpPhaseInitialized=true;
            }
            // Borrowed callback buffers: consume/copy synchronously. Reuse
            // avoids ~25 MB/s of managed allocation at 30 kS/s.
            short[] ap=apFrame;
            ExtractAdcWords(offset);
            if(lfpSlot==0)lfpCollected=0;
            // Nominal ADC counts: remove mid-supply and undo ASIC negative gain.
            // Apply once at the decoded-sample boundary; framing stays raw.
            for(int adc=0;adc<32;adc++)lfpAssembly[AdcWordToChannel(adc,lfpSlot)]=(short)(512-(int)words[adc]);
            lfpCollected++;
            for(int slot=0;slot<12;slot++) {
                ExtractAdcWords(offset+(slot+1)*60);
                for(int adc=0;adc<32;adc++)ap[AdcWordToChannel(adc,slot)]=(short)(512-(int)words[adc]);
            }
            if(++lfpSlot==12) {
                lfpSlot=0;
                if(lfpCollected==12)onSamples(ap,1,lfpAssembly,1);
                else {StartupDiscardedLfpSuperframes+=lfpCollected;onSamples(ap,1,null,0);}
            } else onSamples(ap,1,null,0);
        }
        private void ExtractAdcWords(int offset)
        {
            Array.Clear(words,0,words.Length);
            NpCe64LaneLayout layout=Layout;bool quad=layout==NpCe64LaneLayout.GpioV1Quad;
            for(int tick=0;tick<50;tick++) {
                byte lane=Lane(offset+tick,layout);int word=tick/10, bit=9-tick%10;
                // V1 NRIC1384 Table 15 / Figure 11: five ADCs per PSB line.
                // Preserve separately qualified legacy integrated-probe layouts.
                for(int line=0;line<6;line++)if((lane&(1<<(line+1)))!=0)
                    words[quad ? line*5+word : word*6+line]|=(ushort)(1<<bit);
                if(tick<20 && (lane&0x80)!=0)words[30+tick/10]|=(ushort)(1<<bit);
            }
        }
        private static int AdcWordToChannel(int adc,int slot) { return (adc/2)*24+(adc&1)+slot*2; }
    }
}
