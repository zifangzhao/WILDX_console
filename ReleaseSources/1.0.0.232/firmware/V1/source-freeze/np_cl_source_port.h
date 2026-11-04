#ifndef NP_CL_SOURCE_PORT_H
#define NP_CL_SOURCE_PORT_H
#ifndef NP_CL_ENABLE
#define NP_CL_ENABLE 0
#endif
#if NP_CL_ENABLE
#include "np_cl_source.h"
/* Mode implementation returns only its configured, recording-owned state.
 * No new acquisition globals: SD uses its preview lease, USB ordinary BSS. */
NpClSourceState *NP_ClCaptureState(void);
#if NP_FIRST_SYNC_ENABLE
void NP_ClCaptureBinding(uint32_t out[4]);
int NP_ClCaptureBootstrapFirst(NpClSourceState *,NpClSourceReceipt *,const NpClSourceReceipt *);
int NP_ClCapturePrepareNext(const NpClSourceReceipt *);
int NP_ClCaptureFirstPhase(const NpClSourceReceipt *,uint32_t probe,uint32_t out[2]);
#endif
int NP_CaptureAnchor_GetSourceReceipt(NpClAnchorReceipt *);
uint32_t HAL_GetTick(void);
static inline void NP_ClCaptureResetPsbProbe(void)
{NpClSourceState *s=NP_ClCaptureState();if(s)np_cl_reset_psb_probe(&s->reset,s->seeded,s->lost);}
static inline void NP_ClCaptureResetAssert(uint32_t low)
{NpClSourceState *s=NP_ClCaptureState();if(s)np_cl_reset_assert(&s->reset,low);}
static inline void NP_ClCaptureResetRelease(uint32_t high,uint32_t now_ms)
{NpClSourceState *s=NP_ClCaptureState();if(s)np_cl_reset_release(&s->reset,high,now_ms);}
static inline void NP_ClCaptureResetAdmission(void)
{NpClSourceState *s=NP_ClCaptureState();if(s)np_cl_reset_admission(&s->reset,s->seeded);}
static inline void NP_ClCaptureResetArm(uint32_t generation)
{NpClSourceState *s=NP_ClCaptureState();if(s)np_cl_reset_capture_arm(&s->reset,generation,s->seeded);}
static inline void NP_ClCaptureResetStop(void)
{NpClSourceState *s=NP_ClCaptureState();if(s)np_cl_reset_capture_stop(&s->reset);}
static inline void NP_ClCaptureResetInvalidate(void)
{NpClSourceState *s=NP_ClCaptureState();if(s)np_cl_reset_invalidate(&s->reset);}
static inline void NP_ClCaptureSeed(void)
{
#if !NP_FIRST_SYNC_ENABLE
    NpClAnchorReceipt a;NpClSourceState *s=NP_ClCaptureState();
    if(s && NP_CaptureAnchor_GetSourceReceipt(&a))(void)np_cl_source_seed(s,&a);
#endif
}
static inline void NP_ClCaptureBegin(NpClSourceReceipt *r,uint32_t mask,uint32_t ticks)
{
    NpClAnchorReceipt a={0};NpClSourceState *s=NP_ClCaptureState();
    if(s)(void)NP_CaptureAnchor_GetSourceReceipt(&a);
    (void)np_cl_source_begin(s,&a,mask,ticks,r);
    np_cl_source_reset_receipt(s,&a,HAL_GetTick(),r);
}
static inline void NP_ClCaptureBeginFirst(NpClSourceReceipt *r,const NpClSourceReceipt *first,
                                         uint32_t mask,uint32_t ticks)
{
#if NP_FIRST_SYNC_ENABLE
    NpClSourceState *s=NP_ClCaptureState();
    memset(r,0,sizeof(*r));
    if(!s)return;
    if(!s->seeded){
        if(!NP_ClCaptureBootstrapFirst(s,r,first))np_cl_source_loss(s);
        return;
    }
#else
    (void)first;
#endif
    NP_ClCaptureBegin(r,mask,ticks);
}
static inline void NP_ClCaptureCommit(const NpClSourceReceipt *r)
{(void)np_cl_source_commit(NP_ClCaptureState(),r);}
static inline void NP_ClCaptureCancel(void){np_cl_source_cancel(NP_ClCaptureState());}
static inline void NP_ClCaptureLoss(void){np_cl_source_loss(NP_ClCaptureState());}
#endif
#endif
