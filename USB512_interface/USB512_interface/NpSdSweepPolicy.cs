using System;
using System.Collections.Generic;
using CE32_console;

namespace USB512_interface {
    // A result is evidence supplied by the controller, never inferred from
    // exception text, nominal card speed or a successful Start acknowledgement.
    internal enum NpSdSweepEvidence {
        MissingProof=0,VerifiedPass,VerifiedThroughputLoss,VerifiedIntegrityFailure,
        ProbeOrConfigurationFailure,TransportOrUnknownDelivery,Cancelled,InsufficientSpace
    }
    internal enum NpSdSweepDecision {Inconclusive=0,Passed,TryLower,NotPassed}
    internal static class NpSdSweepPolicy {
        static readonly uint[] Descending={30000,20000,15000,7500};
        static void Binding(int family,uint mask){
            if(family!=1&&family!=2)throw new ArgumentOutOfRangeException("family");
            if(mask==0||mask>7)throw new ArgumentOutOfRangeException("mask");
        }
        static bool Legal(int family,uint mask,uint hz){
            return family==1?NpV1BleProtocol.TheoreticalMode(mask,hz):NpV2SdControl.ProfileTicks(mask,hz)!=0;
        }
        // The mask stays fixed. The callback must use fresh cached firmware /
        // connected-probe support; it must not probe or mutate during Start.
        internal static uint[] Rates(int family,uint fixedMask,Func<uint,bool> freshlySupported){
            Binding(family,fixedMask);
            if(freshlySupported==null)throw new ArgumentNullException("freshlySupported");
            var rates=new List<uint>(4);
            foreach(uint hz in Descending)if(Legal(family,fixedMask,hz)&&freshlySupported(hz))rates.Add(hz);
            return rates.ToArray();
        }
        // Physical payload demand only, in decimal B/s. Finite reservation
        // metadata, extent/AU rounding and available space belong to the real
        // allocator. This is neither decoded DAT bandwidth nor card capacity.
        internal static decimal PhysicalBytesPerSecond(int family,uint mask,uint hz){
            Binding(family,mask);
            if(!Legal(family,mask,hz))throw new ArgumentOutOfRangeException("hz","Unsupported recording profile.");
            if(family==1){
                // Format6: 61440 source bytes in each fixed65536-byte chunk.
                // NSG lies in existing62144..65023 slack, with no extra sectors.
                return hz*NpSdGeometry.Count(mask)*832m;
            }
            // Format16 (ordered or append): four46592B NN records followed by
            // K512B AUX positions, including empty positions. Grid rate1250Hz.
            uint k=hz==7500?(mask==1?5U:mask==3?3U:2U):
                hz==15000?(mask==1?3U:mask==3?2U:1U):(mask==1?2U:1U);
            return (364m+k)*512m*hz*1600m/(4m*NpV2SdControl.ProfileTicks(mask,hz));
        }
        // Only explicit, verified throughput loss may advance the sweep, and
        // only after the same owned session/card is terminal, idle and writable.
        // No outcome here authorizes replay, Stop, repair, deletion or formatting.
        internal static NpSdSweepDecision Decide(NpSdSweepEvidence evidence,bool exactStoppedWritableSameBinding){
            if(!exactStoppedWritableSameBinding)return NpSdSweepDecision.Inconclusive;
            switch(evidence){
                case NpSdSweepEvidence.VerifiedPass:return NpSdSweepDecision.Passed;
                case NpSdSweepEvidence.VerifiedThroughputLoss:return NpSdSweepDecision.TryLower;
                case NpSdSweepEvidence.VerifiedIntegrityFailure:return NpSdSweepDecision.NotPassed;
                default:return NpSdSweepDecision.Inconclusive;
            }
        }
    }
}
