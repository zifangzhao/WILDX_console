using System;
using System.IO;

namespace USB512_interface {
    // Caller-supplied independent continuous-counter bounds, not a window
    // synthesized by this mapper from HAL milliseconds. The producer/caller
    // must establish point continuity, lifetime and coarse/fine agreement.
    // Constructing this DTO (or setting Qualified) is not that proof.
    internal sealed class NpSensorFineCounterWindow {
        internal readonly NpSensorFineTime Context;
        internal readonly int Component;
        internal readonly uint SourceMs,MaximumRootDistanceMs;
        internal readonly decimal LowerCounter,UpperCounter;
        internal readonly bool Qualified;
        internal readonly string Evidence;
        internal NpSensorFineCounterWindow(NpSensorFineTime context,int component,uint sourceMs,
            decimal lowerCounter,decimal upperCounter,uint maximumRootDistanceMs,bool qualified,string evidence){
            Context=context;Component=component;SourceMs=sourceMs;LowerCounter=lowerCounter;UpperCounter=upperCounter;
            MaximumRootDistanceMs=maximumRootDistanceMs;Qualified=qualified;Evidence=evidence;
        }
    }
    internal sealed class NpSensorFineOffset {
        internal readonly long LowerUs,UpperUs;
        internal readonly decimal NativeCounter;
        internal readonly bool NativeAgeBoundary;
        internal decimal NativeCounterUpper {get{return NativeCounter+1m;}}
        internal bool PreOrigin {get{return UpperUs<0;}}
        internal bool StraddlesOrigin {get{return LowerUs<0&&UpperUs>=0;}}
        internal bool Seedable {get{return LowerUs>=0;}}
        internal NpSensorFineOffset(long lowerUs,long upperUs,decimal nativeCounter,bool nativeAgeBoundary=false){LowerUs=lowerUs;UpperUs=upperUs;NativeCounter=nativeCounter;NativeAgeBoundary=nativeAgeBoundary;}
    }
    // Pure digital source-time mapping. No I/O, PC/network epoch, grid, ADC
    // phase, clock initialization or inference of physical sensor conversion.
    internal sealed class NpSensorFineTime {
        internal readonly string Uid;
        internal readonly ulong Token,ReferenceTick;
        internal readonly uint Family,Generation,SourceEpoch,Domain,CounterHz,SourceHz,BeforeMs,BeforeCounter;
        internal readonly uint Session,ReceiptCrc;
        internal readonly decimal T0CounterLower,T0CounterUpper;
        internal readonly bool Continuous64;
        readonly bool usbTransport;
        internal bool IsUsb {get{return usbTransport;}}
        const decimal Wrap=4294967296m;
        static uint U(byte[] b,int at){return BitConverter.ToUInt32(b,at);}
        static void Need(bool ok,string why){if(!ok)throw new InvalidDataException("NP sensor fine time: "+why);}
        NpSensorFineTime(string uid,ulong token,byte[] anchor,NpFirstSyncReceipt receipt,bool continuous64=false,bool usb=false){
            Uid=uid;Token=token;Family=receipt.Family;Generation=receipt.Generation;SourceEpoch=receipt.SourceEpoch;
            Session=receipt.Session;ReceiptCrc=BitConverter.ToUInt32(receipt.Raw,324);
            Continuous64=continuous64;usbTransport=usb;
            Domain=receipt.Domain;CounterHz=receipt.CounterHz;SourceHz=receipt.SourceHz;ReferenceTick=receipt.ReferenceTick;
            BeforeMs=U(anchor,20);BeforeCounter=U(anchor,28);
            // Add the source origin exactly once; keep the root bracket rather
            // than collapse it to an assumed midpoint/physical sample phase.
            ulong source=checked(BitConverter.ToUInt64(anchor,44)+ReferenceTick);
            T0CounterLower=BeforeCounter+(decimal)source*CounterHz/SourceHz;
            T0CounterUpper=T0CounterLower+unchecked(U(anchor,32)-BeforeCounter)+1m;
        }
        internal static NpSensorFineTime FromSd(byte[] clock,byte[] companion,int family,uint session,uint apHz,string uid,ulong token){
            NpFirstSyncUsb.UidWords(uid);Need(token!=0,"exact recording token required");
            var parsed=NpFineClockWire.ReadCompanion(companion,clock,family,session,apHz,uid,token);
            Need(parsed!=null&&parsed.FineAux,"explicit fine-AUX9 SD companion required");
            var receipt=NpFirstSyncWire.ValidateSourceAnchor(parsed.Anchor,parsed.Bound.Receipt.Raw);
            // Only Features25 attests BEFORE's high word zero in the actual
            // continuous64 producer lifetime. Features9 is still raw32 only.
            return new NpSensorFineTime(parsed.Bound.Uid,parsed.Bound.Token,parsed.Anchor,receipt,parsed.FineCounter64);
        }
        internal static NpSensorFineTime FromUsb(byte[] rawInfo,NpFirstSyncUsbAssembler assembled,byte[] rootEnvelope,
            ulong containingSequence,string uid,ulong token,int family,uint generation){
            var info=NpFirstSyncUsb.ParseInfo(rawInfo,uid,family);
            Need(info.Ready&&info.FineAux&&info.Token==token&&assembled!=null&&assembled.FineReady&&
                String.Equals(assembled.Uid,uid,StringComparison.OrdinalIgnoreCase)&&assembled.Token==token&&
                assembled.Family==family&&assembled.Generation==generation&&assembled.FineSequence==containingSequence&&
                assembled.FineCounter64==info.FineCounter64,
                "matching complete fine-AUX USB binding required");
            byte[] raw=assembled.Receipt.Raw,anchor=NpFirstSyncUsb.ParseAnchor(rootEnvelope,raw,uid,token,generation,containingSequence);
            byte[] cached=assembled.FineAnchor;Need(cached!=null&&cached.Length==anchor.Length,"cached USB root required");
            for(int i=0;i<anchor.Length;i++)Need(cached[i]==anchor[i],"USB root differs from completed assembly");
            var receipt=NpFirstSyncWire.ValidateSourceAnchor(anchor,raw);
            Need(receipt.Family==family&&receipt.Generation==generation&&receipt.Session==generation,"USB source session/generation binding");
            return new NpSensorFineTime(info.Uid,token,anchor,receipt,info.FineCounter64,true);
        }
        // Stopped observers receive the actual immutable runtime snapshot,
        // not a fabricated page set. Revalidate all original bytes again.
        internal static NpSensorFineTime FromUsb(NpFirstSyncUsbFineRoot root){
            Need(root!=null,"immutable USB source snapshot required");
            var info=NpFirstSyncUsb.ParseInfo(root.Info,root.Uid,(int)root.Family);
            Need(info.Ready&&info.FineCounter64&&info.Token==root.Token,"selected continuous64 USB snapshot required");
            byte[] raw=root.Receipt,anchor=NpFirstSyncUsb.ParseAnchor(root.Envelope,raw,root.Uid,root.Token,root.Generation,root.Sequence),cached=root.Anchor;
            Need(anchor.Length==cached.Length,"USB snapshot root size");for(int i=0;i<anchor.Length;i++)Need(anchor[i]==cached[i],"USB snapshot root cache mismatch");
            var receipt=NpFirstSyncWire.ValidateSourceAnchor(anchor,raw);
            Need(receipt.Family==root.Family&&receipt.Generation==root.Generation&&receipt.Session==root.Generation,"USB snapshot final session/generation binding");
            return new NpSensorFineTime(info.Uid,root.Token,anchor,receipt,true,true);
        }
        // Parse original CRC-bound evidence here; a caller-created/mutated
        // stamp or Qualified window cannot manufacture continuous-counter
        // proof. V1 uses its exact28B sidecar; V2 may pass the original512B
        // USB/SD header, selected by this context's validated transport.
        internal bool TryMap64(byte[] original,NpSensorFineBinding native,int component,out NpSensorFineOffset offset){
            offset=null;
            if(!Continuous64||native==null||component<1||component>3||native.Sequence==null||native.Sequence.Length!=3||
                native.SourceMs==null||native.SourceMs.Length!=3)return false;
            // Freeze caller metadata for this parse/map; original native ms
            // is CRC identity only, never an alias selector or fine phase.
            var binding=new NpSensorFineBinding {Domain=native.Domain,Generation=native.Generation,SourceEpoch=native.SourceEpoch,
                CounterHz=native.CounterHz,SensorValid=native.SensorValid,Sequence=(uint[])native.Sequence.Clone(),SourceMs=(uint[])native.SourceMs.Clone()};
            if(binding.Domain!=Domain||binding.Generation!=Generation||binding.SourceEpoch!=SourceEpoch||binding.CounterHz!=CounterHz)return false;
            NpSensorFine64Stamp stamp;NpSensorFineState validity;
            if(original!=null&&original.Length==NpSensorFine64Wire.Bytes)validity=NpSensorFine64Wire.Parse(original,binding,true,out stamp);
            else if(Family==2&&original!=null&&original.Length==512)
                validity=usbTransport?NpSensorFine64Wire.UsbParse(original,binding,true,out stamp):NpSensorFine64Wire.SdParse(original,binding,true,out stamp);
            else return false;
            uint bit=1U<<(component-1);
            if(validity!=NpSensorFineState.Valid||(stamp.Mask&bit)==0)return false;
            try{
                // Counter64 is already uniquely resolved inside the bounded
                // publication-age policy by the wire codec. No HAL check or
                // modular low32 epoch inference is applied at this layer.
                decimal counter=stamp.Counter64[component-1];
                decimal lo=(counter-T0CounterUpper)*1000000m/CounterHz,hi=(counter+1m-T0CounterLower)*1000000m/CounterHz;
                offset=new NpSensorFineOffset(checked((long)Decimal.Floor(lo)-1),checked((long)Decimal.Ceiling(hi)+1),counter,
                    (stamp.NativeAgeBoundaryMask&bit)!=0);return true;
            }catch(OverflowException){return false;}
        }
        internal bool TryMap(NpSensorFineState validity,NpSensorFineStamp stamp,NpSensorFineBinding native,int component,
            NpSensorFineCounterWindow window,out NpSensorFineOffset offset){
            offset=null;
            if(validity!=NpSensorFineState.Valid||stamp==null||native==null||component<1||component>3||
                window==null||!window.Qualified||String.IsNullOrWhiteSpace(window.Evidence)||!ReferenceEquals(window.Context,this)||window.Component!=component||
                window.MaximumRootDistanceMs==0||window.MaximumRootDistanceMs>=0x80000000U||window.LowerCounter>window.UpperCounter||
                stamp.Domain!=Domain||stamp.Generation!=Generation||native.Domain!=Domain||native.Generation!=Generation||
                native.SourceEpoch!=SourceEpoch||native.CounterHz!=CounterHz||stamp.Mask==0||(stamp.Mask&~7U)!=0||
                native.Sequence==null||native.Sequence.Length!=3||native.SourceMs==null||native.SourceMs.Length!=3||
                stamp.Counter==null||stamp.Counter.Length!=3)return false;
            uint bit=1U<<(component-1),sourceMs=native.SourceMs[component-1];
            if((stamp.Mask&bit)==0||(native.SensorValid&bit)==0||native.Sequence[component-1]==0||window.SourceMs!=sourceMs)return false;
            try{
                if(window.UpperCounter-window.LowerCounter>=Wrap)return false;
                uint counter=stamp.Counter[component-1];
                // The independent proof must select one full32 read interval
                // BEFORE any coarse check; HAL cannot rescue a wide/ambiguous
                // window or choose another counter lifetime.
                decimal first=Decimal.Ceiling((window.LowerCounter-counter-1m)/Wrap),last=Decimal.Floor((window.UpperCounter-counter)/Wrap);
                if(first!=last)return false;decimal continuous=counter+first*Wrap;
                uint modular=unchecked(sourceMs-BeforeMs);if(modular==0x80000000U)return false;
                long delta=unchecked((int)modular);if(Math.Abs(delta)>window.MaximumRootDistanceMs)return false;
                // HAL integer ms is only an independently qualified <=2ms
                // consistency/alias check, never a fractional point estimate.
                decimal center=BeforeCounter+(decimal)delta*CounterHz/1000m,tolerance=CounterHz/500m;
                if(continuous<center-tolerance||continuous+1m>center+tolerance)return false;
                decimal lo=(continuous-T0CounterUpper)*1000000m/CounterHz,hi=(continuous+1m-T0CounterLower)*1000000m/CounterHz;
                long loUs=checked((long)Decimal.Floor(lo)-1),hiUs=checked((long)Decimal.Ceiling(hi)+1);
                offset=new NpSensorFineOffset(loUs,hiUs,continuous);return true;
            }catch(OverflowException){return false;}
        }
    }
}
