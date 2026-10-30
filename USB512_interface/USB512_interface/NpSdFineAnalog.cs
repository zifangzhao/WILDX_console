using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace USB512_interface {
    internal sealed class NpSdFineAnalogResult {
        internal bool Applied;
        internal long Rows,NativeObservations;
        internal string Error;
        internal const string StatusFile="imu_fine_timing_status.json",NativeFile="imu_native_fine_timestamps.csv";
    }
    // Offline, fresh-export-only replacement. Source journals stay original;
    // the NN exporter supplies the independently verified source end. The
    // grid is relative to NP STARTBYTE, not to sensor ms, arrival or PC UTC.
    internal static class NpSdFineAnalog {
        internal sealed class Record {
            internal ulong Id;internal byte[] Wire;
            internal NpSensorFineBinding Native;
            internal NpSensorFine64Stamp Stamp;
            internal int[] Values=new int[9];
            internal int Temperature;internal uint Pressure,Humidity;
        }
        sealed class Event {
            internal long Lower,Upper;internal bool Valid;internal int[] Values;
        }
        static readonly CultureInfo Inv=CultureInfo.InvariantCulture;
        static uint U(byte[] b,int at){return BitConverter.ToUInt32(b,at);}
        static void Read(Stream input,byte[] bytes){int used=0,n;while(used<bytes.Length&&(n=input.Read(bytes,used,bytes.Length-used))!=0)used+=n;if(used!=bytes.Length)throw new InvalidDataException("Truncated fine sensor journal record.");}
        static NpSensorFineBinding Binding(NpSensorFineTime clock,byte[] core,int at,uint valid){
            var b=new NpSensorFineBinding {Domain=clock.Domain,Generation=clock.Generation,SourceEpoch=clock.SourceEpoch,CounterHz=clock.CounterHz,SensorValid=valid};
            for(int i=0;i<3;i++){b.Sequence[i]=U(core,at+20+i*4);b.SourceMs[i]=U(core,at+32+i*4);}return b;
        }
        static IEnumerable<Record> Records(string folder,int family,NpSensorFineTime clock,ulong expected){
            if(family==1){
                using(var cores=File.OpenRead(Path.Combine(folder,"sensors.bin")))
                using(var fine=File.OpenRead(Path.Combine(folder,"sensors_fine.bin"))){
                    if((ulong)cores.Length!=checked(expected*164UL)||fine.Length%32!=0)throw new InvalidDataException("Fine V1 core/prefix journal extent differs from verified NP chunks.");
                    var body=new byte[164];var side=new byte[32];bool more=fine.Position<fine.Length;uint previous=0;bool first=true;
                    if(more)Read(fine,side);
                    while(cores.Position<cores.Length){Read(cores,body);uint id=U(body,8);if(!first&&id<=previous)throw new InvalidDataException("Fine V1 sensor chunk order changed.");previous=id;first=false;
                        if(id!=(uint)(cores.Position/164-1)||Encoding.ASCII.GetString(body,0,8)!="NPSENS01"||U(body,16)!=140||U(body,20)!=0x31534e53||U(body,24)!=1||
                            U(body,160)!=NpFirstSyncWire.Crc(body,160)||U(body,12)!=U(body,36)||(U(body,28)&~3U)!=0||(U(body,32)&~7U)!=0||
                            ((U(body,32)&3)!=0&&(U(body,28)&1)==0)||((U(body,32)&4)!=0&&(U(body,28)&2)==0))
                            throw new InvalidDataException("Invalid original V1 sensor core/chunk/CRC in fine replay.");
                        var wire=new byte[28];if(more&&U(side,0)<id)throw new InvalidDataException("Fine sidecar has no exact valid sensor core at chunk "+U(side,0));
                        if(more&&U(side,0)==id){Buffer.BlockCopy(side,4,wire,0,28);more=fine.Position<fine.Length;if(more)Read(fine,side);}
                        var r=new Record {Id=id,Wire=wire,Native=Binding(clock,body,20,U(body,32))};
                        for(int i=0;i<9;i++)r.Values[i]=BitConverter.ToInt32(body,68+i*4);r.Temperature=BitConverter.ToInt32(body,104);r.Pressure=U(body,108);r.Humidity=U(body,112);
                        var state=NpSensorFine64Wire.Parse(wire,r.Native,true,out r.Stamp);if(state==NpSensorFineState.Invalid)throw new InvalidDataException("Invalid continuous64 sensor sidecar at chunk "+id);if(state==NpSensorFineState.Valid)yield return r;
                    }
                    if(more)throw new InvalidDataException("Fine sidecar remains outside the valid V1 sensor core extent.");
                }
            }else{
                using(var input=File.OpenRead(Path.Combine(folder,"imu_sd_fine_records.bin"))){
                    if((ulong)input.Length!=checked(expected*144UL))throw new InvalidDataException("Fine V2 journal extent differs from verified NP blocks.");
                    var raw=new byte[144];ulong previous=0;bool first=true;
                    while(input.Position<input.Length){Read(input,raw);ulong id=BitConverter.ToUInt64(raw,0);if(!first&&id<=previous)throw new InvalidDataException("Fine V2 sensor block order changed.");first=false;previous=id;
                        var header=new byte[512];Buffer.BlockCopy(BitConverter.GetBytes(40U|2048U|4096U),0,header,52,4);Buffer.BlockCopy(raw,16,header,384,128);
                        // This source is also linked into the GUI assembly:
                        // keep wire validation in the shared pure codec, not
                        // the SDK's internal physical-snapshot observer.
                        var r=new Record {Id=id,Wire=header,Native=Binding(clock,header,396,(U(header,400)>>12)&7U)};
                        var state=NpSensorFine64Wire.SdParse(header,r.Native,true,out r.Stamp);if(state==NpSensorFineState.Absent)continue;
                        if(state!=NpSensorFineState.Valid)throw new InvalidDataException("Invalid continuous64 sensor carrier at block "+id);
                        for(int i=0;i<9;i++)r.Values[i]=BitConverter.ToInt32(header,444+i*4);
                        r.Temperature=BitConverter.ToInt32(header,480);r.Pressure=U(header,484);r.Humidity=U(header,488);yield return r;
                    }
                }
            }
        }
        static NpSensorFineState ValidateRecord(NpSensorFineTime clock,Record r){
            if(r==null||r.Values==null||r.Values.Length!=9||r.Native==null)throw new InvalidDataException("Invalid native fine sensor record.");
            NpSensorFineState state;
            if(r.Wire!=null&&r.Wire.Length==28)state=NpSensorFine64Wire.Parse(r.Wire,r.Native,true,out r.Stamp);
            else if(clock.Family==2&&r.Wire!=null&&r.Wire.Length==512)state=(BitConverter.ToUInt32(r.Wire,52)&2048)!=0?
                NpSensorFine64Wire.SdParse(r.Wire,r.Native,true,out r.Stamp):NpSensorFine64Wire.UsbParse(r.Wire,r.Native,true,out r.Stamp);
            else state=NpSensorFineState.Invalid;
            if(state==NpSensorFineState.Invalid)throw new InvalidDataException("Invalid original continuous64 native proof at record "+r.Id);return state;
        }
        sealed class NativeState {internal bool Seen;internal uint Sequence,Ms;internal decimal Counter;internal int[] Values;internal int Temperature;internal uint Pressure,Humidity;}
        static bool Observe(NpSensorFineTime clock,Record r,int component,NativeState state,out NpSensorFineOffset offset){
            offset=null;if((r.Stamp.Mask&(1U<<(component-1)))==0)return false;
            if(!clock.TryMap64(r.Wire,r.Native,component,out offset))throw new InvalidDataException("Continuous64 mapping failed at native record "+r.Id+" component "+component);
            uint sequence=r.Native.Sequence[component-1],ms=r.Native.SourceMs[component-1];
            if(state.Seen&&sequence==state.Sequence&&ms==state.Ms){
                bool same=offset.NativeCounter==state.Counter;
                if(component<3){int first=component==1?0:6,count=component==1?6:3;for(int i=0;i<count;i++)same&=r.Values[first+i]==state.Values[first+i];}
                else same&=r.Temperature==state.Temperature&&r.Pressure==state.Pressure&&r.Humidity==state.Humidity;
                if(!same)throw new InvalidDataException("Repeated native identity has conflicting fine counter/physical values.");return false;
            }
            if(state.Seen&&offset.NativeCounter<state.Counter)throw new InvalidDataException("Native continuous64 source counter went backwards.");
            state.Seen=true;state.Sequence=sequence;state.Ms=ms;state.Counter=offset.NativeCounter;state.Values=(int[])r.Values.Clone();state.Temperature=r.Temperature;state.Pressure=r.Pressure;state.Humidity=r.Humidity;return true;
        }
        static string Cell(object value){var number=value as IFormattable;return number!=null?number.ToString(null,Inv):Convert.ToString(value,Inv);}
        static void NativeRows(Func<IEnumerable<Record>> records,NpSensorFineTime clock,TextWriter times,NpSdFineAnalogResult result,Func<bool> cancelled){
            var states=new[]{new NativeState(),new NativeState(),new NativeState()};
            foreach(var r in records()){
                if(cancelled!=null&&cancelled())throw new OperationCanceledException("Fine sensor export cancelled.");
                if(ValidateRecord(clock,r)==NpSensorFineState.Absent)continue;
                for(int component=1;component<=3;component++){
                    NpSensorFineOffset offset;if(!Observe(clock,r,component,states[component-1],out offset))continue;result.NativeObservations++;
                    var cells=new object[]{r.Id,component,r.Native.Sequence[component-1],r.Native.SourceMs[component-1],r.Stamp.PublicationCounter64,
                        offset.NativeCounter,offset.NativeCounterUpper,offset.LowerUs,offset.UpperUs,offset.PreOrigin?1:0,offset.StraddlesOrigin?1:0,offset.Seedable?1:0,offset.NativeAgeBoundary?1:0,
                        component==1?(object)r.Values[0]:component==2?(object)r.Values[6]:null,component==1?(object)r.Values[1]:component==2?(object)r.Values[7]:null,component==1?(object)r.Values[2]:component==2?(object)r.Values[8]:null,
                        component==1?(object)r.Values[3]:null,component==1?(object)r.Values[4]:null,component==1?(object)r.Values[5]:null,
                        component==3?(object)r.Temperature:null,component==3?(object)r.Pressure:null,component==3?(object)r.Humidity:null,"validated_digital_cache_observation"};
                    times.WriteLine(String.Join(",",Array.ConvertAll(cells,Cell)));
                }
            }
        }
        static IEnumerable<Event> Events(Func<IEnumerable<Record>> records,NpSensorFineTime clock,int component){
            var state=new NativeState();
            foreach(var r in records()){
                if(ValidateRecord(clock,r)==NpSensorFineState.Absent)continue;
                if((r.Stamp.Mask&(1U<<(component-1)))==0){
                    // A CRC-qualified publication can retire an unavailable
                    // component; it cannot supply that component's native tick.
                    decimal pub=r.Stamp.PublicationCounter64;
                    long lo=checked((long)Decimal.Floor((pub-clock.T0CounterUpper)*1000000m/clock.CounterHz)-1),hi=checked((long)Decimal.Ceiling((pub+1m-clock.T0CounterLower)*1000000m/clock.CounterHz)+1);
                    if(lo>=0)yield return new Event {Lower=lo,Upper=hi,Valid=false};continue;
                }
                NpSensorFineOffset offset;if(!Observe(clock,r,component,state,out offset))continue;
                // Straddling/pre-origin intervals remain in NativeRows, but
                // never seed held values in the new recording.
                if(offset.Seedable)yield return new Event {Lower=offset.LowerUs,Upper=offset.UpperUs,Valid=true,Values=(int[])r.Values.Clone()};
            }
        }
        static bool Encode(byte[] row,int component,Event held,long now){
            if(held==null||!held.Valid||now-held.Lower>(component==1?100000L:200000L))return false;
            int count=component==1?6:3,first=component==1?1:7,start=component==1?0:6;
            for(int i=0;i<count;i++){
                int physical=held.Values[start+i];double code=component==1?(i<3?physical*4096.0/1000000.0:physical*164.0/10000.0):physical/300.0;
                long value=(long)Math.Round(code,MidpointRounding.AwayFromZero);if(value<short.MinValue||value>short.MaxValue){for(int n=0;n<count;n++){row[(first+n)*2]=0;row[(first+n)*2+1]=0x80;}return false;}
                row[(first+i)*2]=(byte)value;row[(first+i)*2+1]=(byte)(value>>8);
            }return true;
        }
        static void Commit(string folder,string[] names,bool preserveLegacyChunk){
            var existed=new bool[names.Length];var backups=new string[names.Length];int committed=0;
            for(int i=0;i<names.Length;i++){
                string path=Path.Combine(folder,names[i]);backups[i]=path+(i==0&&preserveLegacyChunk?".legacy_chunk":".before_fine");existed[i]=File.Exists(path);
                if(Directory.Exists(path)||File.Exists(backups[i])||Directory.Exists(backups[i]))throw new IOException("Fine auxiliary output target is occupied: "+names[i]);
            }
            try{
                for(int i=0;i<names.Length;i++){string path=Path.Combine(folder,names[i]);if(existed[i])File.Replace(path+".fine.partial",path,backups[i]);else File.Move(path+".fine.partial",path);committed++;}
            }catch{
                for(int i=committed-1;i>=0;i--)try{string path=Path.Combine(folder,names[i]);if(existed[i])File.Replace(backups[i],path,null);else File.Delete(path);}catch{}
                throw;
            }
            for(int i=0;i<names.Length;i++)if(existed[i]&&!(i==0&&preserveLegacyChunk))try{File.Delete(backups[i]);}catch{}
        }
        internal static NpSdFineAnalogResult Export(string folder,int family,uint session,uint apHz,uint mask,ulong sourceEnd,ulong expectedRecords,bool evidenceComplete,Func<bool> cancelled){
            var result=new NpSdFineAnalogResult();NpSensorFineTime clock=null;
            try{
                byte[] rawClock=File.ReadAllBytes(Path.Combine(folder,"recorded_clock.bin")),rawCompanion=File.ReadAllBytes(Path.Combine(folder,NpFineSdArchive.RawFile));
                var bound=NpFirstSyncSd.ReadSector(rawClock,family,session,apHz);if(bound==null||bound.Receipt.Mask!=mask)throw new InvalidDataException("Fine sensor recording binding/mask unavailable.");
                var companion=NpFineClockWire.ReadCompanion(rawCompanion,rawClock,family,session,apHz,bound.Uid,bound.Token);
                if(companion==null||!companion.FineCounter64)throw new InvalidDataException("Explicit continuous64 recordFeatures25 required.");
                clock=NpSensorFineTime.FromSd(rawClock,rawCompanion,family,session,apHz,bound.Uid,bound.Token);
                return Write(folder,clock,()=>Records(folder,family,clock,expectedRecords),sourceEnd,evidenceComplete&&expectedRecords!=0,cancelled,family==1);
            }catch(Exception ex) when(ex is IOException||ex is UnauthorizedAccessException||ex is InvalidDataException||ex is OverflowException){
                result.Error=ex.Message;try{WriteStatus(Path.Combine(folder,NpSdFineAnalogResult.StatusFile),result,clock,sourceEnd);}catch{}return result;
            }
        }
        // The adapter supplies actual decoded physical values and original
        // CRC-bound carriers. This writer always reparses those carriers;
        // caller-created Stamp fields are never qualification evidence.
        internal static NpSdFineAnalogResult Write(string folder,NpSensorFineTime clock,Func<IEnumerable<Record>> records,ulong sourceEnd,bool evidenceComplete,Func<bool> cancelled,bool preserveLegacyChunk=false){
            var result=new NpSdFineAnalogResult();string[] names={"analogin.dat","analogin_validity.dat","analogin_encoding.txt",NpSdFineAnalogResult.NativeFile,NpSdFineAnalogResult.StatusFile};
            try{
                if(clock==null||!clock.Continuous64||records==null||!evidenceComplete||sourceEnd<=clock.ReferenceTick)throw new InvalidDataException("Fine sensor source/journal extent is incomplete or ambiguous; fallback retained.");
                ulong extent=sourceEnd-clock.ReferenceTick;
                result.Rows=checked((long)(extent/clock.SourceHz*1250UL+((extent%clock.SourceHz)*1250UL+clock.SourceHz-1UL)/clock.SourceHz));
                using(var times=new StreamWriter(new FileStream(Path.Combine(folder,names[3])+".fine.partial",FileMode.CreateNew,FileAccess.Write),new UTF8Encoding(false))){
                    times.WriteLine("source_record,component_id,native_sequence,original_source_ms,publication_counter64,native_counter_lower,native_counter_upper,np_relative_lower_us,np_relative_upper_us,pre_origin,straddles_origin,seedable,age_boundary,x_micro_g_or_nT,y_micro_g_or_nT,z_micro_g_or_nT,gyro_x_milli_dps,gyro_y_milli_dps,gyro_z_milli_dps,temperature_centi_c,pressure_pa,humidity_milli_percent,status");
                    NativeRows(records,clock,times,result,cancelled);
                }
                if(result.NativeObservations==0)throw new InvalidDataException("No validated native fine sensor observations; fallback retained.");
                using(var analog=new FileStream(Path.Combine(folder,names[0])+".fine.partial",FileMode.CreateNew,FileAccess.Write,FileShare.Read,65536))
                using(var validity=new FileStream(Path.Combine(folder,names[1])+".fine.partial",FileMode.CreateNew,FileAccess.Write,FileShare.Read,65536))
                using(var ag=Events(records,clock,1).GetEnumerator())
                using(var mag=Events(records,clock,2).GetEnumerator()){
                    bool nextAg=ag.MoveNext(),nextMag=mag.MoveNext();Event heldAg=null,heldMag=null;var row=new byte[32];var data=new byte[2048*32];var masks=new byte[2048];int used=0;
                    for(long i=0;i<result.Rows;i++){
                        if(i%1250==0&&cancelled!=null&&cancelled())throw new OperationCanceledException("Fine sensor export cancelled.");long now=checked(i*800L);
                        while(nextAg&&ag.Current.Upper<=now){heldAg=ag.Current;nextAg=ag.MoveNext();}while(nextMag&&mag.Current.Upper<=now){heldMag=mag.Current;nextMag=mag.MoveNext();}
                        for(int n=0;n<row.Length;n+=2){row[n]=0;row[n+1]=0x80;}byte valid=0;if(Encode(row,1,heldAg,now))valid|=1;if(Encode(row,2,heldMag,now))valid|=2;
                        Buffer.BlockCopy(row,0,data,used*32,32);masks[used++]=valid;
                        if(used==masks.Length){analog.Write(data,0,used*32);validity.Write(masks,0,used);used=0;}
                    }if(used!=0){analog.Write(data,0,used*32);validity.Write(masks,0,used);}analog.Flush(true);validity.Flush(true);
                }
                File.WriteAllLines(Path.Combine(folder,names[2])+".fine.partial",new[]{
                    "schema=NP_ANALOGIN_FINE64_V1; record_bytes=32; sample_rate_hz=1250; row0=lowest selected NP STARTBYTE reference; period_us=800",
                    "np_byte_zero_sample_grid_locked=True; native_sensor_conversion_phase_locked=false; pc_utc_mapping_applied=false",
                    "sensor_words=2-4 accel;5-7 gyro;8-10 magnetometer; signed int16 CE64-equivalent counts; unknown=-32768; all other words unavailable; no invented ADC/GPIO",
                    "gain=accel_micro_g*4096/1000000;gyro_milli_dps*164/10000;mag_nT/300; round midpoint away from zero; out-of-range group unavailable",
                    "compatibility_encoding_ranges=accel+/-8g,gyro+/-2000dps; native sensor rates are not encoded by this wire; retained sparse publications are not1250Hz native acquisition",
                    "insertion=first800us grid row at or after conservative native upper offset; pre-origin/straddling samples excluded; expiry=conservative native lower offset+AG100ms/MAG200ms",
                    "verified_source_end="+sourceEnd+"; reference_tick="+clock.ReferenceTick+"; source_hz="+clock.SourceHz+"; rows="+result.Rows,
                    "native_interval_file="+names[3]+"; validity_file="+names[1]+"; fine_status_file="+names[4],
                    "original_raw_sensor_files_preserved=true; legacy_chunk_misc_file="+(preserveLegacyChunk?"analogin.dat.legacy_chunk":"not applicable")
                });
                result.Applied=true;WriteStatus(Path.Combine(folder,names[4])+".fine.partial",result,clock,sourceEnd);
                Commit(folder,names,preserveLegacyChunk);
                AttachGridTimeline(folder,result,clock,sourceEnd);return result;
            }catch(OperationCanceledException){throw;}
            catch(Exception ex) when(ex is IOException||ex is UnauthorizedAccessException||ex is InvalidDataException||ex is OverflowException){
                result.Applied=false;result.Error=ex.Message;
                try{WriteStatus(Path.Combine(folder,NpSdFineAnalogResult.StatusFile),result,clock,sourceEnd);}catch{}
                return result;
            }
        }
        static void AttachGridTimeline(string folder,NpSdFineAnalogResult result,NpSensorFineTime clock,ulong sourceEnd){
            string path=Path.Combine(folder,NpRecordingUtc.FileName);if(!File.Exists(path))return;
            try{
                var report=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(path));string error;
                NpDerivedImuGridUtc.AppendAfterCommit(folder,report,result,clock,sourceEnd,out error);
                NpRecordingUtc.Write(folder,report);
            }catch(Exception error) when(error is IOException||error is UnauthorizedAccessException||error is ArgumentException||error is OverflowException||
                error is InvalidOperationException||error is InvalidCastException||error is FormatException){
                // The files already committed successfully. Optional timeline
                // metadata cannot undo that result or fail neural export.
                System.Diagnostics.Trace.WriteLine("Derived IMU timeline: "+error.Message);
                try{File.WriteAllText(Path.Combine(folder,"derived_imu_grid_error.txt"),error.Message);}catch{}
            }
        }
        static void WriteStatus(string path,NpSdFineAnalogResult r,NpSensorFineTime c,ulong sourceEnd){
            File.WriteAllText(path,new JavaScriptSerializer().Serialize(new Dictionary<string,object> {
                {"format","neuropixels.sensor_fine_grid"},{"schema",1},{"optional",true},{"applied",r.Applied},{"error",r.Error},
                {"np_sample_grid_locked",r.Applied},{"native_conversion_phase_locked",false},{"pc_utc_mapping_applied",false},
                {"reference","lowest selected NP STARTBYTE"},{"verified_source_end",sourceEnd},{"reference_tick",c==null?null:(object)c.ReferenceTick},
                {"uid",c==null?null:c.Uid},{"token_hex",c==null?null:c.Token.ToString("X16")},{"generation",c==null?null:(object)c.Generation},
                {"source_epoch",c==null?null:(object)c.SourceEpoch},{"domain",c==null?null:(object)c.Domain},{"counter_hz",c==null?null:(object)c.CounterHz},
                {"t0_counter_lower",c==null?null:(object)c.T0CounterLower},{"t0_counter_upper",c==null?null:(object)c.T0CounterUpper},
                {"rows",r.Applied?(object)r.Rows:null},{"native_observations",r.NativeObservations},{"native_interval_file",r.Applied?NpSdFineAnalogResult.NativeFile:null},
                {"native_interval_partial_file",!r.Applied&&File.Exists(Path.Combine(Path.GetDirectoryName(path),NpSdFineAnalogResult.NativeFile)+".fine.partial")?NpSdFineAnalogResult.NativeFile+".fine.partial":null},
                {"semantics","Measured digital cache-observation intervals; software held grid. No physical sensor conversion/DRDY lock, PC UTC or missing ADC/GPIO reconstruction."}
            })+Environment.NewLine,new UTF8Encoding(false));
        }
    }
}
