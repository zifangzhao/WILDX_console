using System;
using System.IO;
using System.Text;

namespace USB512_interface {
    // SD16 has fixed physical positions. Optional AUX evidence never decides
    // where a neural record starts; all neural/outer metadata checks stay strict.
    internal sealed class NpV2SdGrid:IDisposable {
        readonly string folder;readonly byte[] footer;readonly NpFineSdCompanion context;
        NpImuGridWriter writer;FileStream raw;bool rawFailed;string error;
        ulong positions,empty,data,observed;uint bad;
        internal ulong Rows {get{return writer==null?0:writer.Rows;}}
        internal bool PhaseQualified {get{return OutputComplete&&writer!=null&&writer.PhaseQualified;}}
        internal bool SourceWindowVerified {get{return OutputComplete&&writer!=null&&writer.SourceWindowVerified;}}
        internal string Error {get{return error;}}
        internal bool OutputComplete {get;private set;}
        internal uint MetadataSchema {get;private set;}
        internal bool TailStatsValid {get;private set;}
        internal ulong? KnownUnsavedRows {get;private set;}
        internal bool? PendingRecordRowsUnknown {get;private set;}
        internal string TailError {get;private set;}
        static uint U(byte[] b,int o){return BitConverter.ToUInt32(b,o);}
        static ulong Q(byte[] b,int o){return BitConverter.ToUInt64(b,o);}
        static bool Zero(byte[] b,int o,int n){for(int i=o;i<o+n;i++)if(b[i]!=0)return false;return true;}
        static void Need(bool ok,string why){if(!ok)throw new InvalidDataException("SD16: "+why);}
        static bool Optional(Exception ex){return ex is IOException||ex is InvalidDataException||ex is UnauthorizedAccessException||ex is ArgumentException||ex is OverflowException;}
        internal static uint K(uint mask,uint hz){
            int i=mask==1?0:mask==3?1:mask==7?2:-1;Need(i>=0,"probe mask");
            uint[] row=hz==7500?new uint[]{5,3,2}:hz==15000?new uint[]{3,2,1}:hz==20000||hz==30000?new uint[]{2,1,1}:null;
            Need(row!=null,"sample rate");return row[i];
        }
        internal static bool ClosedLoop(byte[] d){return U(d,8)==15||U(d,8)==16&&(U(d,96)&128)!=0;}
        static ulong Align(ulong n,uint unit){return checked((n+unit-1)/unit*unit);}
        internal static void Validate(byte[] d,byte[] f,uint start,long cardLength,bool incomplete,Action<byte[],string> seal){
            seal(d,"N2SDLOG1");seal(f,"N2SDEND1");
            uint mask=U(d,24),hz=U(d,16),v=U(d,220),k=K(mask,hz),au=U(d,128);
            uint ticks=mask==1?122880U:mask==3?61440U:40960U,flags=v==11?15U:v==12?31U:v==13?47U:0U;
            bool cl=(U(d,96)&128)!=0;ulong payload=U(d,60),limit=U(d,64),end=U(d,68),stride=364UL+k;
            Need(flags!=0&&U(d,8)==16&&U(f,8)==16&&U(d,96)==(flags|(cl?128U:0U)),"base geometry/flags");
            bool legacyAu=au==2048||au==4096||au==8192||au==16384||au==24576||au==32768||au==49152||au==65536||au==131072;
            Need(v==11?au==0:v==12?au>=1024&&au%1024==0:legacyAu,"allocation unit");
            ulong expected=Align(checked((ulong)start+8192),16384);if(v!=11)expected=Align(expected,au);
            ulong rounded=v==11?limit:Align(limit,au),groups=limit>payload?(limit-payload)/stride:0;
            Need(U(d,12)!=0&&U(d,20)==hz*1600U&&U(d,28)==(mask==1?7U:mask==3?63U:511U)&&U(d,32)==(mask==7?6U:4U)&&
                U(d,36)==(mask==1?3U:mask==3?6U:9U)&&U(d,40)==46592&&U(d,44)==ticks&&U(d,48)==91&&U(d,52)==4&&
                U(d,56)==start&&start>=8192&&start%8192==0&&payload==expected&&groups>0&&limit==payload+groups*stride&&
                end==Align(rounded,16384)&&end<=U(d,72)&&end*512<=(ulong)cardLength&&
                (v!=12||(payload/1024<=0x3fffff&&(rounded-au)/1024<=0x3fffff))&&U(d,100)==1&&Zero(d,104,24),"descriptor/profile/reservation");
            uint schema=U(d,216);
            Need(Encoding.ASCII.GetString(d,208,8)=="N2GRP016"&&(schema==1||schema==2)&&U(d,224)==0&&U(d,228)==k&&U(d,232)==stride&&
                U(d,236)==4&&U(d,240)==1&&U(d,244)==2&&U(d,248)==1250&&U(d,252)==0&&Zero(d,256,252),"fixed group declaration");
            Need(cl?U(d,136)==v&&U(d,140)==0x314d4c43:Zero(d,132,12),"closed-loop declaration");
            Need(Zero(d,144,64)&&Zero(f,132,12)&&(cl||Zero(f,144,64))&&(schema!=1||Zero(f,392,116)),"reserved metadata");
            if(cl){if(!Zero(f,144,64))WildNpClosedLoop.NpClosedLoopCarrier.ReadV2Summary(f,144,U(d,12),U(d,132));
                else Need(incomplete&&U(f,100)!=3,"missing final closed-loop summary");}
            for(int i=8;i<100;i++)Need(d[i]==f[i],"descriptor/footer identity");
            for(int i=208;i<256;i++)Need(d[i]==f[i],"descriptor/footer group declaration");
            ulong n=U(f,108),g=U(f,112),physical=Q(f,256);
            Need((incomplete?U(f,100)>=1&&U(f,100)<=4:U(f,100)==3&&U(f,104)==0&&n!=0)&&n%4==0&&g==n/4&&g<=groups&&
                physical==g*stride&&Q(f,264)==g*k&&U(f,116)==payload+physical&&Q(f,120)==n*ticks&&U(f,128)==au&&
                (payload+physical)*512<=(ulong)cardLength,"committed physical/neural extent");
        }
        internal NpV2SdGrid(string folder,byte[] footer,NpFineSdCompanion context){
            this.folder=folder;this.footer=footer;this.context=context;
            MetadataSchema=U(footer,216);
            if(MetadataSchema==2){
                TailStatsValid=U(footer,400)<=1&&U(footer,404)==0&&Zero(footer,408,100);
                if(TailStatsValid){KnownUnsavedRows=Q(footer,392);PendingRecordRowsUnknown=U(footer,400)!=0;}
                else TailError="Invalid optional SD16 schema2 retirement-tail fields; coverage is unknown.";
            }
            try{
                if(context==null||!context.FineCounter64||context.Bound==null)throw new InvalidDataException("SD16 grid lacks strict +5/+6 Features25 context.");
                var r=context.Bound.Receipt;writer=new NpImuGridWriter(folder,r.Generation,true);
                var phase=new byte[84];Buffer.BlockCopy(footer,288,phase,0,84);
                if(Zero(phase,0,84))Fail("SD16 phase proof is unavailable.");
                else if(!writer.AcceptPhase(phase,NpImuGridWriter.PhaseBinding(context.Bound.Uid,context.Bound.Token,r),r.SourceHz,r))Fail("SD16 phase proof is invalid or contradicts NPSTART.");
            }catch(Exception ex) when(Optional(ex)){Fail(ex.Message);}
        }
        void Fail(string why){if(error==null)error=why;}
        internal void ReadFailure(string why){positions++;bad++;Fail("Grid sector read failed: "+why);}
        internal void Feed(byte[] sector,ulong parent){
            positions++;
            try{if(!rawFailed){if(raw==null)raw=new FileStream(Path.Combine(folder,"imu_grid_sectors.bin"),FileMode.CreateNew,FileAccess.Write);raw.Write(sector,0,sector.Length);}}
            catch(Exception ex) when(Optional(ex)){rawFailed=true;Fail("Grid raw archive: "+ex.Message);}
            if(writer==null)return;
            try{
                var frame=NpV2GridSector.ReadSd16Data(sector,context.Bound.Receipt.Generation,parent);
                if(frame.Length==0){empty++;return;}
                var batch=NpImuGridWire.Parse(frame);data++;observed+=(ulong)batch.Rows.Length;
                writer.Feed(frame);
            }catch(Exception ex) when(Optional(ex)){bad++;Fail(ex.Message);}
        }
        internal void Complete(ulong sourceEnd,bool verified,bool firstSource){
            try{
                uint flags=U(footer,272),drop=U(footer,276),miss=U(footer,280);bool known=(flags&1)!=0,saturated=drop==UInt32.MaxValue||miss==UInt32.MaxValue;
                if((flags&~7U)!=0||((flags&2)!=0)!=saturated||known==(Q(footer,376)==UInt64.MaxValue)||
                    positions!=Q(footer,264)||data!=U(footer,284)||empty!=U(footer,372)||data+empty!=positions||observed!=Q(footer,384)||
                    writer!=null&&known&&Q(footer,376)<writer.Slots)Fail("SD16 optional grid counts/flags/final extent disagree.");
                if((flags&6)!=0)Fail("SD16 producer reports a grid fault or saturated drop counters.");
                if(writer!=null&&writer.Observed==0)Fail("No scheduled IMU observations were retained.");
                if(!firstSource)Fail("Saved initial NP STARTBYTE/closed DAT association is unavailable.");
                if(writer!=null){var r=context.Bound.Receipt;writer.Complete(sourceEnd,r.SourceHz,r.ReferenceTick,verified&&error==null,firstSource);OutputComplete=writer.Rows!=0;}
                File.WriteAllText(Path.Combine(folder,"imu_grid_status.txt"),"carrier=SD16_NPGRID02\nenforced_schedule_contract=2\nenforced_schedule_verified="+(writer!=null&&writer.EnforcedScheduleVerified)+"\nphase_qualified="+PhaseQualified+"\nsource_window_verified="+SourceWindowVerified+
                    "\nfirst_slot_phase_valid="+(writer!=null&&writer.FirstSlotPhaseValid)+
                    "\nfirst_source_verified="+firstSource+"\nfinal_slot_known="+known+"\nfinal_exclusive="+Q(footer,376)+"\nqueue_dropped="+drop+"\nscheduler_missing="+miss+"\nsector_positions="+positions+"\nbad_sectors="+bad+
                    "\nmetadata_schema="+MetadataSchema+"\ntail_stats_available="+TailStatsValid+"\nknown_accepted_rows_not_persisted="+(KnownUnsavedRows.HasValue?KnownUnsavedRows.Value.ToString():"unavailable")+
                    "\nknown_unsaved_rows_scope=lower_bound_not_queue_overflow_or_scheduler_misses\npending_record_rows_unknown="+(PendingRecordRowsUnknown.HasValue?PendingRecordRowsUnknown.Value.ToString():"unavailable")+
                    "\ntail_stats_error="+(TailError??(MetadataSchema==1?"schema1 retirement-tail accounting unavailable":"none"))+
                    "\nnative_conversion_phase_known=false\nerror="+(error??"none")+"\n");
            }catch(Exception ex) when(Optional(ex)){OutputComplete=false;Fail(ex.Message);if(writer!=null)writer.Report(error);}
        }
        public void Dispose(){try{if(writer!=null)writer.Dispose();}catch(Exception ex) when(Optional(ex)){OutputComplete=false;Fail(ex.Message);}try{if(raw!=null)raw.Dispose();}catch(Exception ex) when(Optional(ex)){OutputComplete=false;Fail(ex.Message);}}
    }
}
