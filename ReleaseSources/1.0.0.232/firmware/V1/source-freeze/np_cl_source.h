#ifndef NP_CL_SOURCE_H
#define NP_CL_SOURCE_H
#include <stdint.h>
#include <string.h>
#include "np_cl_reset.h"
#define NP_CL_SOURCE_VALID 1U
#define NP_CL_SOURCE_RESET_VALID 2U
typedef struct {uint64_t origin_ticks;uint32_t generation,source_hz,flags;} NpClAnchorReceipt;
typedef struct {
    uint64_t first_source_tick;
    uint32_t source_tick_count,source_epoch,capture_generation,flags,probe_mask;
} NpClSourceReceipt;
typedef struct {
    uint64_t next_tick[3],origin_ticks,pending_first;
    uint32_t source_epoch,capture_generation,source_hz,valid,seeded,lost,pending_mask,pending_ticks,pending_flags;
    NpClResetState reset;
} NpClSourceState;
static inline void np_cl_source_reset(NpClSourceState *s,uint32_t epoch)
{memset(s,0,sizeof(*s));s->source_epoch=epoch;}
/* The adapter arms a specific START before the recording object exists. Only
 * that unseeded attempt may cross record_begin; never retain a prior capture. */
static inline void np_cl_source_record_begin(NpClSourceState *s,uint32_t epoch)
{
    NpClResetState r=s->reset;uint32_t keep=!s->seeded && !s->lost && r.token &&
        (r.stage==NP_CL_RESET_ARMED || r.stage==NP_CL_RESET_ASSERTED || r.stage==NP_CL_RESET_RELEASED);
    np_cl_source_reset(s,epoch);if(keep)s->reset=r;
}
static inline void np_cl_source_loss(NpClSourceState *s)
{
    if(!s || s->lost)return;
    s->valid=s->pending_mask=s->pending_ticks=s->pending_flags=0U;s->lost=1U;
    np_cl_reset_invalidate(&s->reset);
    s->source_epoch=s->source_epoch==UINT32_MAX?0U:s->source_epoch+1U;
}
/* Call ONLY at the initial retention-checked common cursor, never on a late
 * query or a recovery cursor. ROOT_VALID|SOURCE_VALID are existing NPAR bits. */
static inline int np_cl_source_seed(NpClSourceState *s,const NpClAnchorReceipt *a)
{
    if(!s || !a || !s->source_epoch || s->seeded || s->lost ||
       !a->generation || !a->source_hz || (a->flags&5U)!=5U)return 0;
    s->origin_ticks=a->origin_ticks;s->capture_generation=a->generation;s->source_hz=a->source_hz;
    s->seeded=s->valid=1U;return 1;
}
static inline int np_cl_source_begin(NpClSourceState *s,const NpClAnchorReceipt *a,
                                     uint32_t mask,uint32_t ticks,NpClSourceReceipt *r)
{
    uint32_t p,first=3U;
    if(!r)return 0;memset(r,0,sizeof(*r));
    if(!s)return 0;
    if(s->valid && (!a || (a->flags&5U)!=5U || a->generation!=s->capture_generation ||
       a->source_hz!=s->source_hz || a->origin_ticks!=s->origin_ticks))np_cl_source_loss(s);
    r->source_epoch=s->source_epoch;r->capture_generation=s->capture_generation;
    r->source_tick_count=ticks;r->probe_mask=mask;
    if(!s->valid || !s->source_epoch)return 0;
    if(!mask || (mask&~7U) || !ticks || s->pending_mask){np_cl_source_loss(s);r->source_epoch=s->source_epoch;return 0;}
    for(p=0;p<3U;p++)if(mask&(1U<<p)){
        if(first==3U)first=p;
        if(s->next_tick[p]!=s->next_tick[first] || s->next_tick[p]>UINT64_MAX-ticks){
            np_cl_source_loss(s);r->source_epoch=s->source_epoch;return 0;
        }
    }
    s->pending_first=s->next_tick[first];s->pending_mask=mask;s->pending_ticks=ticks;
    r->first_source_tick=s->pending_first;r->flags=s->pending_flags=NP_CL_SOURCE_VALID;return 1;
}
static inline void np_cl_source_reset_receipt(NpClSourceState *s,const NpClAnchorReceipt *a,
                                              uint32_t now,NpClSourceReceipt *r)
{
    if(!s || !a || !r || !(r->flags&NP_CL_SOURCE_VALID))return;
    NpClResetState *reset=&s->reset;
    if(reset->stage!=NP_CL_RESET_RELEASED && reset->stage!=NP_CL_RESET_BOUND)return;
    if(now<reset->release_ms || now<reset->last_ms ||
       reset->capture_generation!=a->generation || reset->capture_generation!=s->capture_generation ||
       (uint64_t)reset->ap_hz*780U!=a->source_hz || (r->probe_mask&~reset->probe_mask)){
        np_cl_reset_invalidate(reset);return;
    }
    reset->last_ms=now;
    if(!np_cl_reset_window(reset,now)){
        if(reset->stage==NP_CL_RESET_RELEASED)np_cl_reset_invalidate(reset);
        else reset->window_closed=1U; /* Locked native phase can continue. */
        return;
    }
    if(reset->window_closed)return;
    reset->stage=NP_CL_RESET_BOUND;
    r->flags|=NP_CL_SOURCE_RESET_VALID;s->pending_flags=r->flags;
}
static inline int np_cl_source_commit(NpClSourceState *s,const NpClSourceReceipt *r)
{
    uint32_t p;
    if(!s)return 0;
    if(!s->valid || !r || !(r->flags&NP_CL_SOURCE_VALID) || (r->flags&~3U) ||
       r->flags!=s->pending_flags || !s->pending_mask ||
       r->source_epoch!=s->source_epoch || r->capture_generation!=s->capture_generation ||
       r->probe_mask!=s->pending_mask || r->source_tick_count!=s->pending_ticks ||
       r->first_source_tick!=s->pending_first){np_cl_source_loss(s);return 0;}
    for(p=0;p<3U;p++)if(s->pending_mask&(1U<<p))s->next_tick[p]+=s->pending_ticks;
    s->pending_mask=s->pending_ticks=s->pending_flags=0U;return 1;
}
static inline void np_cl_source_cancel(NpClSourceState *s)
{if(s){s->pending_mask=s->pending_ticks=s->pending_flags=0U;np_cl_reset_invalidate(&s->reset);}}
#endif
