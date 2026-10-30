using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;

namespace USB512_interface {
    internal sealed class NpImuGridSelection {
        internal bool Supported,Selected;internal uint Revision,Version;
        internal bool EnforcedScheduleVerified {get{return Version==2&&Supported&&Selected;}}
    }
    internal static class NpImuGridControl {
        internal const uint BleSupported=0x20000000U,BleSelected=0x40000000U;
        static uint U(byte[] b,int o){return BitConverter.ToUInt32(b,o);}
        static void Put(byte[] b,int o,uint n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,o,4);}
        static bool Unsupported(Exception e){var w=e as Win32Exception;return e is NotSupportedException||w!=null&&(w.NativeErrorCode==1||w.NativeErrorCode==31||w.NativeErrorCode==50);}
        internal static NpImuGridSelection ParseUsb(byte[] raw,string uid){
            if(raw==null||raw.Length!=32||U(raw,0)!=0x4347534eU||(U(raw,4)!=1&&U(raw,4)!=2)||U(raw,28)!=NpImuGridCarrier.Crc(raw,0,28)||
                (U(raw,8)&~7U)!=0||!String.Equals(U(raw,12).ToString("X8")+U(raw,16).ToString("X8")+U(raw,20).ToString("X8"),uid,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("IMU grid control identity/version/CRC mismatch.");
            if((U(raw,8)&4)!=0)throw new IOException("IMU grid selection is frozen by an admitted capture.");
            return new NpImuGridSelection{Supported=(U(raw,8)&1)!=0,Selected=(U(raw,8)&2)!=0,Revision=U(raw,24),Version=U(raw,4)};
        }
        // Exactly one mutation. Even a throwing OUT can have reached firmware;
        // only the readback establishes the resulting selected/legacy mode.
        internal static bool PrepareUsb(Func<byte[]> read,Action<bool> select,string uid,bool desired){
            return PrepareUsbCore(read,select,uid,desired,false).Selected;
        }
        internal static NpImuGridSelection PrepareUsbSelection(Func<byte[]> read,Action<bool> select,string uid,bool desired){
            return PrepareUsbCore(read,select,uid,desired,true);
        }
        static NpImuGridSelection PrepareUsbCore(Func<byte[]> read,Action<bool> select,string uid,bool desired,bool requireEnforced){
            NpImuGridSelection before;try{before=ParseUsb(read(),uid);}catch(Exception e) when(Unsupported(e)){return new NpImuGridSelection();}
            desired=desired&&before.Supported&&(!requireEnforced||before.Version==2);
            if(before.Selected==desired)return before;
            Exception sendError=null;try{select(desired);}catch(Exception e) when(e is IOException||e is Win32Exception||e is TimeoutException){sendError=e;}
            NpImuGridSelection after;try{after=ParseUsb(read(),uid);}catch(Exception e){throw new IOException("IMU grid SELECT outcome is unknown; no replay or recording Start was sent.",e);}
            if(after.Version!=before.Version)throw new IOException("IMU grid capability version changed during selection; recording was not started.");
            if(after.Revision==before.Revision&&after.Selected!=before.Selected)throw new IOException("IMU grid selection changed without a control revision.");
            if(after.Selected&&!after.Supported)throw new IOException("IMU grid selection lost readiness; recording was not started.");
            if(after.Selected==desired&&after.Revision!=before.Revision)return after;
            if(!after.Selected)return after; // confirmed idle legacy is safe
            throw new IOException("IMU grid SELECT was not reconciled; no mutation replay.",sendError);
        }
        internal static byte[] BleRequest(uint id,bool selected){if(id==0)throw new ArgumentOutOfRangeException("id");var b=new byte[64];Put(b,0,0x31444c4e);Put(b,4,1);Put(b,8,10);Put(b,12,id);Put(b,16,selected?1U:0U);Put(b,60,NpImuGridCarrier.Crc(b,0,60));return b;}
        internal static async Task<CE32_console.NpSdStatus> PrepareBle(CE32_console.NpSdStatus before,Func<byte[],Task<CE32_console.NpSdStatus>> send,Func<Task<CE32_console.NpSdStatus>> status,bool desired){
            if(before==null||(before.Flags&BleSupported)==0)return before;
            if(!CE32_console.NpV1BleProtocol.CanManageStorage(before)||(before.Flags&8)!=0)throw new IOException("Idle V1 is required for grid selection.");
            if(((before.Flags&BleSelected)!=0)==desired)return before;
            uint id=CE32_console.NpSdControl.NextId(before);Exception error=null;
            try{await send(BleRequest(id,desired));}catch(Exception e) when(e is IOException||e is TimeoutException){error=e;}
            var after=await status();if(after==null||!CE32_console.NpV1BleProtocol.CanManageStorage(after)||(after.Flags&(8|BleSupported))!=BleSupported)throw new IOException("IMU grid SELECT outcome is unknown; read status before retrying.",error);
            if(((after.Flags&BleSelected)!=0)==desired||((after.Flags&BleSelected)==0))return after;
            throw new IOException("IMU grid SELECT was not confirmed; no replay.",error);
        }
    }
}
