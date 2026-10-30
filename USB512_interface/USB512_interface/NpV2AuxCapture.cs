using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace USB512_interface {
    // One borrowed EP82 owner. Capture controls/EP81 stay with NpV2CaptureLoop;
    // the caller wires this to its existing before-device-dispose hook only
    // AFTER stopping NP. Sinks receive immutable, completion-owned packets.
    internal sealed class NpV2AuxCapture : IDisposable {
        readonly object stopGate=new object();readonly NpV2AuxParser parser;
        readonly Action<NpV2AuxPacket> consume;readonly byte[] buffer=new byte[32768];
        Func<byte[],int> read;Action requestStop,abort,retire,release,disposeReader;Func<bool> retired;
        Thread worker,cleanup;volatile bool stop,legacyTerminal,workerExited,ownershipRetired;
        bool stopRequested;
        string error,retirementError;bool haveSequence;uint previousSequence;
        internal long Packets {get;private set;}internal long SequenceGaps {get;private set;}
        internal string Error {get{return error;}}
        internal string RetirementError {get{return retirementError;}}
        internal bool RetirementConfirmed {get{return ownershipRetired;}}
        internal bool TerminalSeen {get{return legacyTerminal;}}
        internal bool WorkerExited {get{return workerExited;}}
        internal bool Ready {get{return worker!=null;}}

        internal NpV2AuxCapture(NpV2UsbDevice device,bool gridSelected,Action<NpV2AuxPacket> sink){
            if(device==null||sink==null)throw new ArgumentNullException(device==null?"device":"sink");
            consume=sink;parser=new NpV2AuxParser(gridSelected);
            try {
                var reader=device.OpenAuxReader(this);
                read=reader.ReadInto;requestStop=device.RequestAuxStop;abort=()=>device.AbortAuxPipe(this);
                retire=reader.Retire;retired=()=>reader.RetirementConfirmed;
                release=()=>device.ReleaseAuxOwner(this);disposeReader=reader.Dispose;
                StartWorker();
            } catch(Exception ex) when(OptionalFailure(ex)) {
                Record(ex);retirementError="AUX setup failed; any submitted native ownership remains retained until proven retired.";
            }
        }
        // A bounded native-call seam for focused lifecycle tests. Production
        // always uses the exact device/reader methods above, without overrides.
        internal NpV2AuxCapture(bool gridSelected,Action<NpV2AuxPacket> sink,Func<byte[],int> read,
            Action requestStop,Action abort,Action retire,Func<bool> retired,Action release,Action disposeReader){
            if(sink==null||read==null||requestStop==null||abort==null||retire==null||retired==null||release==null||disposeReader==null)throw new ArgumentNullException("AUX lifecycle callback");
            consume=sink;parser=new NpV2AuxParser(gridSelected);this.read=read;this.requestStop=requestStop;this.abort=abort;
            this.retire=retire;this.retired=retired;this.release=release;this.disposeReader=disposeReader;StartWorker();
        }
        static bool OptionalFailure(Exception e){return e is IOException||e is InvalidDataException||e is UnauthorizedAccessException||e is Win32Exception||e is TimeoutException||e is InvalidOperationException||e is NotSupportedException||e is OverflowException;}
        void Record(Exception e){Interlocked.CompareExchange(ref error,e.Message,null);}
        void StartWorker(){worker=new Thread(Run){IsBackground=true,Name="V2 AUX EP82"};try{worker.Start();}catch{worker=null;workerExited=true;throw;}}
        void Dispatch(NpV2AuxPacket packet){
            if(legacyTerminal){Record(new InvalidDataException("AUX data followed its terminal record."));return;}
            if(haveSequence&&packet.Sequence!=unchecked(previousSequence+1)){
                SequenceGaps++;Record(new InvalidDataException("AUX transport sequence gap, duplicate or reversal; grid completeness is unknown."));
            }
            haveSequence=true;previousSequence=packet.Sequence;Packets++;
            if(packet.Error!=0)Record(new IOException("V2 AUX device reported error "+packet.Error+"; optional stream quality is unverified."));
            // A malformed optional grid/sensor or failed sink cannot prevent
            // delivery of subsequent independent camera/ADC/native records.
            try{consume(packet);}catch(Exception ex) when(OptionalFailure(ex)){Record(ex);}
            if(packet.Type==4)legacyTerminal=true;
        }
        void Run(){
            try {
                while(!stop&&!legacyTerminal){int n=read(buffer);if(n<0||n>buffer.Length)throw new IOException("AUX reader returned an invalid completion length.");if(n!=0)parser.Feed(buffer,0,n,Dispatch);}
                parser.Complete();
            } catch(Exception ex) when(!(ex is OutOfMemoryException)) {
                // Only the optional AUX worker is caught here. A bad sink or
                // decoder must not terminate the process that owns EP81.
                var native=ex as Win32Exception;
                if(!(stop&&native!=null&&native.NativeErrorCode==995))Record(ex);
            } finally {workerExited=true;}
        }
        // The device-close seam calls this after NP controls and its EP81
        // retirement. No shared reset/reopen or EP81 cancellation occurs here.
        internal bool StopAndRetire(int drainMilliseconds=1000,int joinMilliseconds=1000){
            if(drainMilliseconds<0||drainMilliseconds>5000||joinMilliseconds<0||joinMilliseconds>5000)throw new ArgumentOutOfRangeException("AUX bounded drain/join");
            lock(stopGate){
                if(ownershipRetired)return true;
                if(retire==null){retirementError=retirementError??"AUX initialization did not prove native retirement.";return false;}
                if(Thread.CurrentThread==worker){retirementError="AUX owner cannot retire from inside its packet callback.";return false;}
                if(cleanup==null||!cleanup.IsAlive){
                    cleanup=new Thread(()=>{
                        try {
                            if(!stopRequested){stopRequested=true;try{requestStop();}catch(Exception ex) when(OptionalFailure(ex)){Record(ex);}}
                            var watch=Stopwatch.StartNew();
                            while(worker!=null&&!workerExited&&watch.ElapsedMilliseconds<drainMilliseconds)worker.Join(Math.Min(25,Math.Max(1,drainMilliseconds-(int)watch.ElapsedMilliseconds)));
                            stop=true;
                            abort();
                            if(worker!=null&&!worker.Join(joinMilliseconds))throw new TimeoutException("AUX worker has not exited; native owner and shared interface remain on HOLD.");
                            if(!legacyTerminal)Record(new IOException("AUX terminal was not received; trailing grid extent is unknown."));
                            retire();if(!retired())throw new IOException("AUX native reads are not proven retired; shared interface remains on HOLD.");
                            disposeReader();release();ownershipRetired=true;retirementError=null;
                        } catch(Exception ex) when(!(ex is OutOfMemoryException)){Record(ex);retirementError=ex.Message;}
                    }){IsBackground=true,Name="V2 AUX retirement"};cleanup.Start();
                }
                if(!cleanup.Join(drainMilliseconds+joinMilliseconds)){if(ownershipRetired)return true;retirementError="AUX native cleanup is still outstanding; shared interface remains on HOLD.";return false;}
                return ownershipRetired;
            }
        }
        public void Dispose(){StopAndRetire();}
    }
}
