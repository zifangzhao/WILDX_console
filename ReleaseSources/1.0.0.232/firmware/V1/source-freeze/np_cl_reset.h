#ifndef NP_CL_RESET_H
#define NP_CL_RESET_H
#include <stdint.h>
#include <string.h>
/* Internal acquisition evidence only; these are NOT NPAR/NPTB wire flags.
 * Pin readback proves the MCU-observed reset transition, not an ASIC counter.
 * The native decoder must independently check counter alignment and bounds. */
enum { NP_CL_RESET_ARMED=1U, NP_CL_RESET_ASSERTED=2U,
       NP_CL_RESET_RELEASED=3U, NP_CL_RESET_BOUND=4U };
typedef struct {
    uint32_t token,probe_mask,ap_hz,stage,release_ms,capture_generation,admission,last_ms,window_closed;
} NpClResetState;
/* Failure-only startup observation. Always pass a valid observer/no-op;
 * no callback or extra state is stored in the reset/record ABI. */
enum { NP_CL_RESET_SITE_ASSERT=1U,NP_CL_RESET_SITE_RELEASE,NP_CL_RESET_SITE_ADMISSION,
       NP_CL_RESET_SITE_ARM,NP_CL_RESET_SITE_STOP,NP_CL_RESET_SITE_PSB };
typedef void (*NpClResetReject)(const NpClResetState *,uint32_t,uint32_t,uint32_t);
static inline void np_cl_reset_ignore(const NpClResetState *r,uint32_t site,uint32_t a,uint32_t b)
{(void)r;(void)site;(void)a;(void)b;}
static inline void np_cl_reset_invalidate(NpClResetState *r)
{if(r)memset(r,0,sizeof(*r));}
static inline int np_cl_reset_matches(const NpClResetState *r,uint32_t token,
                                      uint32_t ap,uint32_t mask)
{return r && token && r->token==token && r->ap_hz==ap && r->probe_mask==mask;}
static inline int np_cl_reset_arm(NpClResetState *r,uint32_t token,uint32_t ap,uint32_t mask)
{
    if(!r)return 0;np_cl_reset_invalidate(r);
    if(!token || !ap || ap>UINT32_MAX/13U || !mask || (mask&~7U))return 0;
    r->token=token;r->ap_hz=ap;r->probe_mask=mask;r->stage=NP_CL_RESET_ARMED;return 1;
}
/* Only the existing pre-DMA PSB diagnostic may restart the assertion witness.
 * Keep current START ownership, never an edge, capture or released phase. */
static inline void np_cl_reset_psb_probe_observed(NpClResetState *r,uint32_t seeded,uint32_t lost,NpClResetReject reject)
{
    if(!r)return;
    uint32_t token=r->token,ap=r->ap_hz,mask=r->probe_mask;
    if(seeded || lost || r->capture_generation ||
       (r->stage!=NP_CL_RESET_ARMED && r->stage!=NP_CL_RESET_ASSERTED)){
        reject(r,NP_CL_RESET_SITE_PSB,seeded,lost);
        np_cl_reset_invalidate(r);return;
    }
    (void)np_cl_reset_arm(r,token,ap,mask);
}
static inline void np_cl_reset_assert_observed(NpClResetState *r,uint32_t low,NpClResetReject reject)
{
    if(!r)return;
    if(!low || !r->token || (r->stage!=NP_CL_RESET_ARMED && r->stage!=NP_CL_RESET_ASSERTED)){
        reject(r,NP_CL_RESET_SITE_ASSERT,low,0U);
        np_cl_reset_invalidate(r);return;
    }
    r->stage=NP_CL_RESET_ASSERTED;r->release_ms=0U;r->admission=0U;
}
static inline void np_cl_reset_release_observed(NpClResetState *r,uint32_t high,uint32_t now,NpClResetReject reject)
{
    if(!r)return;
    if(!high || r->stage!=NP_CL_RESET_ASSERTED || !r->capture_generation){
        reject(r,NP_CL_RESET_SITE_RELEASE,high,now);
        np_cl_reset_invalidate(r);return;
    }
    r->stage=NP_CL_RESET_RELEASED;r->release_ms=r->last_ms=now;r->window_closed=0U;
}
static inline void np_cl_reset_admission_observed(NpClResetState *r,uint32_t seeded,NpClResetReject reject)
{
    if(!r)return;
    if(seeded || r->stage!=NP_CL_RESET_RELEASED || !r->capture_generation){
        reject(r,NP_CL_RESET_SITE_ADMISSION,seeded,0U);
        np_cl_reset_invalidate(r);return;
    }
    r->admission=1U;
}
static inline void np_cl_reset_capture_arm_observed(NpClResetState *r,uint32_t generation,uint32_t seeded,NpClResetReject reject)
{
    if(!r)return;
    if(!generation || seeded ||
       !((r->stage==NP_CL_RESET_ASSERTED && !r->capture_generation) ||
         (r->stage==NP_CL_RESET_RELEASED && r->admission &&
          r->capture_generation!=UINT32_MAX && generation==r->capture_generation+1U))){
        reject(r,NP_CL_RESET_SITE_ARM,generation,seeded);
        np_cl_reset_invalidate(r);return;
    }
    r->capture_generation=generation;r->admission=0U;
}
static inline void np_cl_reset_capture_stop_observed(NpClResetState *r,NpClResetReject reject)
{
    if(r && r->capture_generation && !r->admission){
        reject(r,NP_CL_RESET_SITE_STOP,0U,0U);
        np_cl_reset_invalidate(r);
    }
}
static inline void np_cl_reset_psb_probe(NpClResetState *r,uint32_t seeded,uint32_t lost)
{np_cl_reset_psb_probe_observed(r,seeded,lost,np_cl_reset_ignore);}
static inline void np_cl_reset_assert(NpClResetState *r,uint32_t low)
{np_cl_reset_assert_observed(r,low,np_cl_reset_ignore);}
static inline void np_cl_reset_release(NpClResetState *r,uint32_t high,uint32_t now)
{np_cl_reset_release_observed(r,high,now,np_cl_reset_ignore);}
static inline void np_cl_reset_admission(NpClResetState *r,uint32_t seeded)
{np_cl_reset_admission_observed(r,seeded,np_cl_reset_ignore);}
static inline void np_cl_reset_capture_arm(NpClResetState *r,uint32_t generation,uint32_t seeded)
{np_cl_reset_capture_arm_observed(r,generation,seeded,np_cl_reset_ignore);}
static inline void np_cl_reset_capture_stop(NpClResetState *r)
{np_cl_reset_capture_stop_observed(r,np_cl_reset_ignore);}
static inline int np_cl_reset_window(const NpClResetState *r,uint32_t now)
{
    /* No modular HAL-time inference: any observed wrap is unknown. One extra
     * millisecond covers timestamp quantization, conservatively at AP*13. */
    return r && r->token && r->ap_hz && now>=r->release_ms &&
        (r->stage==NP_CL_RESET_RELEASED || r->stage==NP_CL_RESET_BOUND) &&
        ((uint64_t)now-r->release_ms+1U)*r->ap_hz*13U<UINT64_C(0x80000)*1000U;
}
#endif
