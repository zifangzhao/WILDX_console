#ifndef NP_CL_FIRST_BRIDGE_H
#define NP_CL_FIRST_BRIDGE_H
#include "np_first_sync_record.h"
/* Once per optional detector recording, at the first retention-owned claim.
 * Copy the acquisition owner's already-checked reset/source state, not its
 * startup scanner. USB's private reset token is deliberately not its session. */
static inline int np_cl_first_bootstrap(NpClSourceState *dst,NpClSourceReceipt *out,
    const NpClSourceReceipt *claim,const NpFirstV1Record *r,const uint32_t binding[4],
    uint32_t mask,uint32_t ap)
{
    if(!dst || !out || !claim || !r || !binding || dst->seeded || dst->lost)return 0;
    const NpFirstSyncReceipt *q=&r->receipt;const NpClSourceState *s=&r->source;
    if(!q->session || !q->source_epoch || !(q->flags&NP_FIRST_FINAL) ||
       binding[0]!=q->session || binding[2]!=q->source_epoch || binding[3]!=q->profile_crc ||
       (binding[1] && binding[1]!=q->generation) || dst->source_epoch!=q->source_epoch ||
       !q->generation || r->final_generation!=q->generation || s->capture_generation!=q->generation ||
       q->mask!=mask || q->ap_hz!=ap || (uint64_t)ap*780U!=q->source_hz ||
       s->source_hz!=q->source_hz || s->source_epoch!=q->source_epoch ||
       !s->valid || !s->seeded || s->lost || !s->pending_mask ||
       claim->flags!=3U || claim->first_source_tick || !claim->source_tick_count ||
       claim->source_epoch!=q->source_epoch || claim->capture_generation!=q->generation ||
       !claim->probe_mask || (claim->probe_mask&~mask) || claim->probe_mask!=s->pending_mask ||
       claim->first_source_tick!=s->pending_first || claim->source_tick_count!=s->pending_ticks ||
       claim->flags!=s->pending_flags || !s->reset.token || s->reset.stage!=NP_CL_RESET_BOUND ||
       s->reset.capture_generation!=q->generation || s->reset.ap_hz!=ap ||
       s->reset.probe_mask!=mask || s->reset.window_closed ||
       !np_cl_reset_window(&s->reset,s->reset.last_ms))return 0;
    for(unsigned p=0U;p<3U;p++)if((mask&(1U<<p)) && s->next_tick[p])return 0;
    *dst=*s;*out=*claim;return 1;
}
/* The first full-port private copy is already retention-committed. Freeze
 * only this startup ledger while its unchanged scanner runs, allowing the
 * independent continuing CL ledger/next DMA to advance. No qualification is
 * assigned here; partial-port startup keeps the original scan-first order. */
static inline int np_cl_first_can_prefetch(const NpFirstV1Record *r,
    const NpClSourceReceipt *claim,const uint32_t binding[4],uint32_t mask,uint32_t ap)
{
    if(!r || !claim || !binding)return 0;
    const NpFirstSyncReceipt *q=&r->receipt;const NpClSourceState *s=&r->source;
    if(!q->session || !q->generation || !q->source_epoch || r->sealed ||
       r->observe!=np_first_record_observe || !(q->flags&NP_FIRST_FINAL) ||
       binding[0]!=q->session || binding[1]!=q->generation || binding[2]!=q->source_epoch ||
       binding[3]!=q->profile_crc || q->mask!=mask || q->ap_hz!=ap ||
       s->capture_generation!=q->generation || r->final_generation!=q->generation ||
       s->source_epoch!=q->source_epoch || !s->valid || !s->seeded || s->lost ||
       s->pending_mask || s->pending_ticks || s->pending_flags ||
       s->source_hz!=q->source_hz || (uint64_t)ap*780U!=q->source_hz ||
       claim->flags!=3U || claim->first_source_tick || claim->probe_mask!=mask ||
       claim->source_tick_count<2339U || claim->source_epoch!=q->source_epoch ||
       claim->capture_generation!=q->generation || !s->reset.token ||
       s->reset.stage!=NP_CL_RESET_BOUND || s->reset.capture_generation!=q->generation ||
       s->reset.ap_hz!=ap || s->reset.probe_mask!=mask || s->reset.window_closed ||
       !np_cl_reset_window(&s->reset,s->reset.last_ms))return 0;
    for(unsigned p=0U;p<3U;p++)if((mask&(1U<<p)) && s->next_tick[p]!=claim->source_tick_count)return 0;
    return 1;
}
/* Reuse the mandatory, unique 26-frame proof only for that exact first
 * committed private copy. Never infer a phase after source loss or restart. */
static inline int np_cl_first_phase(const NpFirstV1Record *r,const NpClSourceReceipt *claim,
    const uint32_t binding[4],uint32_t mask,uint32_t ap,uint32_t probe,uint32_t out[2])
{
    if(!r || !claim || !binding || !out || probe<1U || probe>3U)return 0;
    const NpFirstSyncReceipt *q=&r->receipt;const NpClSourceState *s=&r->source;
    if(!r->live || r->observe!=np_first_record_ignore ||
       (q->flags&(NP_FIRST_FINAL|NP_FIRST_ALL_PORTS))!=(NP_FIRST_FINAL|NP_FIRST_ALL_PORTS) ||
       !q->generation || !q->source_epoch || !q->session ||
       binding[0]!=q->session || binding[1]!=q->generation || binding[2]!=q->source_epoch ||
       binding[3]!=q->profile_crc || q->mask!=mask || !(mask&(1U<<(probe-1U))) || q->ap_hz!=ap ||
       q->source_hz!=(uint64_t)ap*780U || s->source_hz!=q->source_hz ||
       r->final_generation!=q->generation || s->capture_generation!=q->generation ||
       s->source_epoch!=q->source_epoch || !s->valid || !s->seeded || s->lost ||
       s->pending_mask || s->pending_ticks || s->pending_flags ||
       claim->flags!=3U || claim->probe_mask!=mask || claim->first_source_tick ||
       claim->source_tick_count<2339U || claim->capture_generation!=q->generation ||
       claim->source_epoch!=q->source_epoch || !s->reset.token ||
       s->reset.stage!=NP_CL_RESET_BOUND || s->reset.capture_generation!=q->generation ||
       s->reset.probe_mask!=mask || s->reset.ap_hz!=ap || s->reset.window_closed ||
       !np_cl_reset_window(&s->reset,s->reset.last_ms))return 0;
    for(unsigned p=0U;p<3U;p++)if((mask&(1U<<p)) && s->next_tick[p]!=claim->source_tick_count)return 0;
    const NpFirstSyncProbe *p=&q->probes[probe-1U];
    if(p->flags!=31U || p->period!=780U || p->first_tick>=780U ||
       p->counter>=0x80000U || p->counter%13U || p->phase!=(780U-p->first_tick%780U)%780U)return 0;
    out[0]=(uint32_t)p->first_tick;out[1]=p->counter;return 1;
}
#endif
