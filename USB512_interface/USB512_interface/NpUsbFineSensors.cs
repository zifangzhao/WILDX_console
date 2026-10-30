#if NP_FIRST_SYNC_ENABLE
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace USB512_interface {
    // Ordered optional observer only. Archives sensor metadata, never neural
    // payload; no device handle, EP0, thread, queue or clock-window invention.
    internal sealed class NpUsbFineSensors : IDisposable {
        internal const string RawFile="imu_usb_fine_records.bin",StatusFile="imu_usb_fine_status.json";
        readonly string folder,uid;readonly int family;readonly Func<NpFirstSyncUsbFineRoot> root;
        BinaryWriter output;byte[] previous;NpFirstSyncUsbFineRoot bound;
        bool disposed,seen,havePublication,archiveDisabled;ulong publication;
        internal long Records,Duplicates;
        internal long Carriers,FineCarriers,AbsentCarriers,PlainCarriers;
        ulong nextSequence,nextSourceTick;uint sourceHz,apHz,probeMask,ticksPerBlock;
        string carrierError;
        internal string Error {get;private set;}
        static uint U(byte[] b,int at){return BitConverter.ToUInt32(b,at);}
        static void Put(byte[] b,int at,uint n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,at,4);}
        static bool Failure(Exception e){return e is IOException||e is UnauthorizedAccessException||e is InvalidDataException||e is ArgumentException||e is OverflowException;}
        internal NpUsbFineSensors(string folder,int family,string expectedUid,Func<NpFirstSyncUsbFineRoot> root){NpFirstSyncUsb.UidWords(expectedUid);this.folder=folder;this.family=family;uid=expectedUid.ToUpperInvariant();this.root=root;}
        void Fail(string error){if(Error==null)Error=error;}
        void CarrierFail(string error){if(carrierError==null)carrierError=error;}
        // These are containing NN block identities, not deduplicated native
        // sample identities. Only a strict negotiated root activates this gate;
        // all-plain legacy sessions retain their existing output behavior.
        void ObserveV2Carrier(byte[] bytes,int offset){
            ulong sequence=BitConverter.ToUInt64(bytes,offset+24),tick=BitConverter.ToUInt64(bytes,offset+32);
            uint ticks=U(bytes,offset+40),hz=U(bytes,offset+44),rate=U(bytes,offset+60),mask=U(bytes,offset+76);
            if(Carriers==0){sourceHz=hz;apHz=rate;probeMask=mask;ticksPerBlock=ticks;}
            if(sequence!=nextSequence||tick!=nextSourceTick||ticks==0||ticks!=ticksPerBlock||hz!=sourceHz||rate!=apHz||mask!=probeMask)
                CarrierFail("USB fine sensor containing blocks are missing, duplicated, out of order, or changed profile.");
            nextSequence=checked(sequence+1);nextSourceTick=checked(tick+ticks);Carriers++;
            if((U(bytes,offset+52)&4096)==0){PlainCarriers++;return;}
            FineCarriers++;
            if(U(bytes,offset+256)==0){
                bool empty=true;for(int i=88;i<96;i++)empty&=bytes[offset+i]==0;
                for(int i=256;i<396;i++)empty&=bytes[offset+i]==0;
                for(int i=504;i<512;i++)empty&=bytes[offset+i]==0;
                if(empty)AbsentCarriers++;else CarrierFail("USB fine-ABSENT carrier contains nonzero optional sensor metadata.");
            }
        }
        void BindRoot(){
            var current=root==null?null:root();
            if(current==null){if(bound!=null)Fail("Previously qualified USB source root was withdrawn.");return;}
            if(current.Uid!=uid||current.Family!=family||!NpFirstSyncUsb.ParseInfo(current.Info,current.Uid,family).FineCounter64){Fail("USB sensor source root UID/family/continuous64 selection mismatch.");return;}
            if(bound==null){bound=current;return;}
            if(current.Uid!=bound.Uid||current.Token!=bound.Token||current.Generation!=bound.Generation||current.Sequence!=bound.Sequence||!current.Envelope.SequenceEqual(bound.Envelope))Fail("USB sensor source context changed.");
        }
        internal void Feed(byte[] bytes,int offset,int count){
            if(disposed||archiveDisabled)return;
            try{
                if(bytes==null||offset<0||bytes.Length-offset<count||count!=(family==1?506:512))throw new InvalidDataException("USB sensor carrier extent mismatch.");
                int core=offset+(family==1?20:256);uint word=U(bytes,core+4);
                if(family==1){bool empty=true;for(int i=440;i<468;i++)empty&=bytes[offset+i]==0;if(empty)return;}
                else {ObserveV2Carrier(bytes,offset);if((U(bytes,offset+52)&4096)==0)return;}
                seen=true;BindRoot();
                if(family==2&&U(bytes,core)==0)return;
                ulong pub=BitConverter.ToUInt64(bytes,offset+(family==1?444:264));
                if(havePublication&&pub<publication)Fail("USB sensor publication counter64 went backwards.");publication=pub;havePublication=true;
                // Ignore only publication time and diagnostic tail. Native
                // identity/values, SIF counters, schema and root stay in key.
                var key=new byte[132];Buffer.BlockCopy(bytes,core+8,key,0,104);Array.Clear(key,8,4);
                if(family==2){Put(key,0,(word>>8)&3);Put(key,4,(word>>12)&7);}
                for(int i=0;i<3;i++)Put(key,104+i*4,U(bytes,offset+(family==1?452+i*4:i<2?88+i*4:504)));
                Put(key,116,family==1?U(bytes,offset+440):word);Put(key,120,family==1?U(bytes,offset+176):0);
                if(bound!=null){Put(key,124,bound.Generation);Put(key,128,NpFirstSyncWire.Parse(bound.Receipt).SourceEpoch);}
                if(previous!=null&&key.SequenceEqual(previous)){Duplicates++;return;}
                if(output==null)output=new BinaryWriter(new FileStream(Path.Combine(folder,RawFile),FileMode.CreateNew,FileAccess.Write,FileShare.Read,65536));
                output.Write(family==1?(ulong)U(bytes,offset+8):BitConverter.ToUInt64(bytes,offset+24));output.Write((uint)count);output.Write((uint)family);output.Write(bytes,offset,count);
                Records++;previous=key;
            }catch(Exception e) when(Failure(e)){Fail(e.Message);archiveDisabled=true;}
        }
        static void Read(Stream input,byte[] b){int used=0,n;while(used<b.Length&&(n=input.Read(b,used,b.Length-used))!=0)used+=n;if(used!=b.Length)throw new InvalidDataException("Truncated USB native sensor archive.");}
        IEnumerable<NpSdFineAnalog.Record> Replay(NpSensorFineTime clock){
            using(var input=File.OpenRead(Path.Combine(folder,RawFile))){
                var prefix=new byte[16];ulong prior=0;bool first=true;long read=0;
                while(input.Position<input.Length){
                    Read(input,prefix);ulong id=BitConverter.ToUInt64(prefix,0);int count=checked((int)U(prefix,8));
                    if(U(prefix,12)!=family||count!=(family==1?506:512)||!first&&id<=prior)throw new InvalidDataException("USB native sensor archive identity/order mismatch.");
                    first=false;prior=id;var raw=new byte[count];Read(input,raw);int core=family==1?20:256;
                    if(family==1)NpUsbSensors.RequirePacket(raw,0,506);
                    if((family==1?(ulong)U(raw,8):BitConverter.ToUInt64(raw,24))!=id)throw new InvalidDataException("USB native sensor containing sequence mismatch.");
                    uint valid=family==1?U(raw,core+12):(U(raw,core+4)>>12)&7;
                    var native=new NpSensorFineBinding {Domain=clock.Domain,Generation=clock.Generation,SourceEpoch=clock.SourceEpoch,CounterHz=clock.CounterHz,SensorValid=valid};
                    for(int i=0;i<3;i++){native.Sequence[i]=U(raw,core+20+i*4);native.SourceMs[i]=U(raw,core+32+i*4);}
                    var wire=raw;if(family==1){wire=new byte[28];Buffer.BlockCopy(raw,440,wire,0,28);}
                    var record=new NpSdFineAnalog.Record {Id=id,Wire=wire,Native=native,Temperature=BitConverter.ToInt32(raw,core+84),Pressure=U(raw,core+88),Humidity=U(raw,core+92)};
                    for(int i=0;i<9;i++)record.Values[i]=BitConverter.ToInt32(raw,core+48+i*4);read++;yield return record;
                }
                if(read!=Records)throw new InvalidDataException("USB native sensor archive record count changed.");
            }
        }
        internal NpSdFineAnalogResult Complete(ulong verifiedSourceEnd,bool evidenceComplete){
            if(family==1&&!seen)return null;NpSdFineAnalogResult result=null;
            try{
                // The source root may arrive after plain/ABSENT carriers.
                if(family==2){BindRoot();if(bound!=null)seen=true;}
                if(!seen)return null;Dispose();
                BindRoot();if(bound==null)throw new InvalidDataException("Qualified continuous64 USB source root is unavailable.");
                var clock=NpSensorFineTime.FromUsb(bound);
                if(family==2){
                    var receipt=NpFirstSyncWire.Parse(bound.Receipt);
                    if(carrierError!=null)Fail(carrierError);
                    if(Carriers==0||PlainCarriers!=0||FineCarriers!=Carriers)Fail("USB fine sensor carrier coverage is incomplete or contains native-only fallback.");
                    if(nextSourceTick!=verifiedSourceEnd||sourceHz!=clock.SourceHz||apHz!=receipt.ApHz||probeMask!=receipt.Mask)
                        Fail("USB fine sensor carrier extent/profile does not match the verified neural stream and source root.");
                }
                result=NpSdFineAnalog.Write(folder,clock,()=>Replay(clock),verifiedSourceEnd,evidenceComplete&&Error==null&&Records!=0,null);
                if(!result.Applied)Fail(result.Error);
            }catch(Exception e) when(Failure(e)){Fail(e.Message);}
            if(seen)SaveStatus(result);return result;
        }
        void SaveStatus(NpSdFineAnalogResult result){
            try{File.WriteAllText(Path.Combine(folder,StatusFile),new JavaScriptSerializer().Serialize(new {schema=1,family,expected_uid=uid,raw_file=RawFile,
                prefix="sequence_u64,raw_bytes_u32,family_u32; exact original506/512 byte sensor carrier follows",records=Records,duplicates=Duplicates,error=Error,
                containing_carriers=Carriers,fine_carriers=FineCarriers,fine_absent_carriers=AbsentCarriers,native_only_carriers=PlainCarriers,carrier_error=seen?carrierError:null,
                bound_uid=bound==null?null:bound.Uid,token_hex=bound==null?null:bound.Token.ToString("X16"),generation=bound==null?(uint?)null:bound.Generation,
                np_origin_grid_applied=result!=null&&result.Applied,live_np_origin_grid=false,
                limitation="Stopped host rebuild only. Legacy display/native journal uses retained original HAL publication milliseconds, not fine counter phase. Original sensor carriers remain exact; no neural RAW/lane8 archive or PC/UTC/physical conversion inference."}));}catch(Exception e) when(Failure(e)){Fail(e.Message);}
        }
        public void Dispose(){if(disposed)return;disposed=true;try{if(output!=null){output.Dispose();output=null;}}catch(Exception e) when(Failure(e)){Fail(e.Message);}if(seen)SaveStatus(null);}
    }
}
#endif
