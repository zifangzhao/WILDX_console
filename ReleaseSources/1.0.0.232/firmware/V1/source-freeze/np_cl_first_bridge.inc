/* Included beside the existing acquisition-owned first-source store. */
#if NP_CL_ENABLE && NP_FIRST_SYNC_ENABLE
#include "np_cl_first_bridge.h"
#include "np_cl_source_port.h"
int NP_ClCaptureBootstrapFirst(NpClSourceState *dst,NpClSourceReceipt *out,
                              const NpClSourceReceipt *claim)
{
    uint32_t binding[4];NP_ClCaptureBinding(binding);
    uint32_t key=__get_PRIMASK();__disable_irq();
    int ok=np_first_store.active && !np_first_store.pending &&
        np_cl_first_bootstrap(dst,out,claim,&np_first_store.record,binding,np_mode_mask,np_mode_hz);
    __set_PRIMASK(key);return ok;
}
#if !NP_SD_APP
int NP_ClCaptureFirstPhase(const NpClSourceReceipt *claim,uint32_t probe,uint32_t out[2])
{
    uint32_t binding[4];NP_ClCaptureBinding(binding);
    uint32_t key=__get_PRIMASK();__disable_irq();
    int ok=np_first_store.active && !np_first_store.pending &&
        np_first_store.private_view==np_first_private_ignore &&
        np_cl_first_phase(&np_first_store.record,claim,binding,np_mode_mask,np_mode_hz,probe,out);
    __set_PRIMASK(key);return ok;
}
int NP_ClCapturePrepareNext(const NpClSourceReceipt *claim)
{
    uint32_t binding[4];NP_ClCaptureBinding(binding);
    uint32_t key=__get_PRIMASK();__disable_irq();
    int ok=0;
    if(np_first_store.active && !np_first_store.pending){
        if(np_first_store.private_view==np_first_private_ignore)
            ok=np_first_store.record.live!=0U;
        else if(np_first_store.private_view==np_first_private &&
                np_cl_first_can_prefetch(&np_first_store.record,claim,binding,np_mode_mask,np_mode_hz)){
            np_first_store.begin=np_first_begin_ignore;
            np_first_store.commit=np_first_commit_ignore;
            ok=1; /* private_view, receipt, source and sensor gate unchanged. */
        }
    }
    __set_PRIMASK(key);return ok;
}
#endif
#endif
