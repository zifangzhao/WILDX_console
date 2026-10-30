using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Windows.Devices.Bluetooth;
using Windows.Foundation.Metadata;

namespace USB512_interface {
    internal sealed partial class NpBleConnection {
        // Keep new Win11 activation types out of the connection's field layout.
        // This optional preference is never a recorder/clock qualification gate.
        IDisposable connectionPreference;
        BluetoothLEDevice connectionPreferenceOwner;
        int connectionPreferenceEpoch;
        object connectionPreferenceAttempt;
        Dictionary<string,object> connectionPreferenceState;
        const string BleDeviceType="Windows.Devices.Bluetooth.BluetoothLEDevice";

        static void DisposePreference(IDisposable request){try{if(request!=null)request.Dispose();}catch{/* Optional native preference cleanup cannot fail Disconnect. */}}

        internal void ClearConnectionPreference(){
            IDisposable request;
            lock(receiveLock){request=connectionPreference;connectionPreference=null;connectionPreferenceOwner=null;connectionPreferenceAttempt=null;connectionPreferenceState=null;}
            DisposePreference(request);
        }

        internal void PreferThroughput(){
            BluetoothLEDevice target=null;int targetEpoch=0;object attempt=null;IDisposable previous=null,request=null;
            var evidence=new Dictionary<string,object>{{"schema",1},{"preset","ThroughputOptimized"},{"supported",false},{"attempted",false},
                {"request_status_at_creation",null},{"error",null},{"scope","Optional Windows link preference; system acceptance is not negotiated timing or PC-clock accuracy."}};
            try{
                lock(receiveLock){
                    target=device;targetEpoch=epoch;if(target==null||!Connected)return;
                    if(ReferenceEquals(connectionPreferenceOwner,target)&&connectionPreferenceEpoch==targetEpoch&&connectionPreferenceAttempt!=null)return;
                    previous=connectionPreference;connectionPreference=null;connectionPreferenceOwner=target;connectionPreferenceEpoch=targetEpoch;
                    attempt=new object();connectionPreferenceAttempt=attempt;connectionPreferenceState=null;
                }
                DisposePreference(previous);previous=null;
                bool supported=ApiInformation.IsMethodPresent(BleDeviceType,"RequestPreferredConnectionParameters")&&
                    ApiInformation.IsPropertyPresent("Windows.Devices.Bluetooth.BluetoothLEPreferredConnectionParameters","ThroughputOptimized");
                evidence["supported"]=supported;
                if(supported){evidence["attempted"]=true;request=RequestThroughputPreference(target,evidence);}
            }catch(Exception error){evidence["error"]=error.Message;}
            finally{
                DisposePreference(previous);
                lock(receiveLock){
                    if(attempt!=null&&ReferenceEquals(connectionPreferenceAttempt,attempt)&&ReferenceEquals(device,target)&&epoch==targetEpoch){
                        connectionPreference=request;request=null;connectionPreferenceState=evidence;
                    }
                }
                DisposePreference(request); // A retired/replaced owner cannot inherit a late handle.
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static IDisposable RequestThroughputPreference(BluetoothLEDevice target,Dictionary<string,object> evidence){
            var preset=BluetoothLEPreferredConnectionParameters.ThroughputOptimized;
            evidence["requested_min_interval_raw"]=preset.MinConnectionInterval;evidence["requested_max_interval_raw"]=preset.MaxConnectionInterval;
            evidence["requested_latency_events"]=preset.ConnectionLatency;evidence["requested_link_timeout_raw"]=preset.LinkTimeout;
            var request=target.RequestPreferredConnectionParameters(preset);
            if(request==null){evidence["error"]="Windows returned no connection preference request.";return null;}
            try{var status=request.Status;evidence["request_status_at_creation"]=status.ToString();evidence["request_status_raw_at_creation"]=(int)status;}
            catch(Exception error){evidence["request_status_error_at_creation"]=error.Message;}
            return request;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static Dictionary<string,object> ActualConnectionParameters(BluetoothLEDevice target){
            var p=target.GetConnectionParameters();
            if(p==null)return new Dictionary<string,object>{{"available",false},{"error","Windows returned no connection parameters."}};
            ushort interval=p.ConnectionInterval,latency=p.ConnectionLatency,timeout=p.LinkTimeout;
            // All-zero values are documented for disconnected devices. Keep raw
            // values; do not replace an unavailable negotiated interval by a preset.
            return new Dictionary<string,object>{{"available",interval>=6&&interval<=3200&&latency<=499&&timeout>=10&&timeout<=3200},
                {"connection_interval_raw",interval},{"connection_interval_ms",interval==0?(object)null:interval*1.25m},
                {"connection_latency_events",latency},{"link_timeout_raw",timeout},{"link_timeout_ms",timeout==0?(object)null:timeout*10m}};
        }

        internal Dictionary<string,object> ReadConnectionParametersEvidence(){
            var evidence=new Dictionary<string,object>{{"schema",1},{"preset","ThroughputOptimized"},{"supported",false},{"attempted",false},{"request_status_at_creation",null},
                {"connected",false},{"connection_epoch",epoch},{"current_epoch",epoch},{"actual_api_supported",false},
                {"error",null},{"actual",null},{"parameter_units",new Dictionary<string,object>{{"connection_interval_raw_ms",1.25m},{"latency","connection events"},{"link_timeout_raw_ms",10}}},
                {"scope","Windows-reported link parameters only. Request Success means accepted by the OS; no UTC or one-way radio timing precision is inferred."}};
            BluetoothLEDevice target=null;int targetEpoch=0;bool connected=false;
            try{
                lock(receiveLock){
                    target=device;targetEpoch=epoch;evidence["connection_epoch"]=targetEpoch;
                    if(ReferenceEquals(connectionPreferenceOwner,target)&&connectionPreferenceEpoch==targetEpoch&&connectionPreferenceState!=null)
                        foreach(var item in connectionPreferenceState)evidence[item.Key]=item.Value;
                    connected=Connected;evidence["connected"]=connected;
                }
                evidence["supported"]=ApiInformation.IsMethodPresent(BleDeviceType,"RequestPreferredConnectionParameters")&&
                    ApiInformation.IsPropertyPresent("Windows.Devices.Bluetooth.BluetoothLEPreferredConnectionParameters","ThroughputOptimized");
                bool available=ApiInformation.IsMethodPresent(BleDeviceType,"GetConnectionParameters");evidence["actual_api_supported"]=available;
                if(connected&&target!=null&&available)evidence["actual"]=ActualConnectionParameters(target);
            }catch(Exception error){evidence["actual_error"]=error.Message;if(evidence["error"]==null)evidence["error"]=error.Message;}
            finally{
                lock(receiveLock){
                    evidence["current_epoch"]=epoch;
                    if(epoch!=targetEpoch||!ReferenceEquals(device,target)){
                        evidence["actual"]=null;evidence["connected"]=false;evidence["error"]="Connection changed while reading optional link parameters; stale snapshot discarded.";
                    }
                }
            }
            return evidence;
        }
    }
}
