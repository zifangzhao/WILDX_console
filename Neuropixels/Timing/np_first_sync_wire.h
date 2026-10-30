#ifndef NP_FIRST_SYNC_WIRE_H
#define NP_FIRST_SYNC_WIRE_H
#include <stdint.h>
#include <stddef.h>
#include <string.h>

/* Optional startup evidence, not a capture scanner or transport allocation.
 * Call once on accepted private data; cache the gate result off the hot path.
 * A position in that data predates foreground discovery and auxiliary release.
 * Reset/preparation alone can never supply NP_FIRST_PROBE_FRAMED. */
#define NP_FIRST_SYNC_BYTES 328U
#define NP_FIRST_SYNC_SD_OFFSET 160U
#define NP_FIRST_SYNC_UNKNOWN_TICK UINT64_MAX
#define NP_FIRST_SYNC_UNKNOWN_COUNTER UINT32_MAX
#define NP_FIRST_SYNC_UNKNOWN_PHASE UINT16_MAX
#define NP_FIRST_SYNC_UNKNOWN_OFFSET INT64_MIN
enum { NP_FIRST_FINAL=1U, NP_FIRST_ALL_PORTS=2U, NP_FIRST_DISCOVERY=4U,
       NP_FIRST_PROFILE=8U, NP_FIRST_AUX_GATED=16U, NP_FIRST_SOURCE_CLOCK=32U };
enum { NP_FIRST_PROBE_SELECTED=1U, NP_FIRST_PROBE_FRAMED=2U,
       NP_FIRST_PROBE_COUNTER=4U, NP_FIRST_PROBE_PHASE=8U,
       NP_FIRST_PROBE_CONTINUOUS=16U };
enum { NP_FIRST_AUX_ENABLED=1U, NP_FIRST_AUX_RELEASE=2U,
       NP_FIRST_AUX_COUNTER=4U, NP_FIRST_AUX_OFFSET=8U,
       NP_FIRST_AUX_SAMPLE_LOCK=16U, NP_FIRST_AUX_EXCLUDED=32U,
       NP_FIRST_AUX_POINT=64U };
enum { NP_FIRST_AUX_IMU=1U, NP_FIRST_AUX_ANALOG=2U,
       NP_FIRST_AUX_MICROPHONE=3U, NP_FIRST_AUX_CAMERA=4U };
enum { NP_FIRST_STAMP_UNKNOWN=0U, NP_FIRST_STAMP_SOURCE=1U,
       NP_FIRST_STAMP_FOREGROUND=2U };
typedef struct {
    uint64_t first_tick;
    uint32_t counter,flags;
    uint16_t phase,period;
} NpFirstSyncProbe;
typedef struct {
    uint8_t id,flags,counter_bits,stamp_kind;
    uint32_t release_ms,release_cycles,first_ms,first_cycles;
    uint64_t counter;
    uint32_t rate_num,rate_den;
    int64_t offset_num;
    uint32_t offset_den;
} NpFirstSyncAux;
typedef struct {
    uint32_t flags,session,generation,source_epoch,domain,source_hz,ap_hz;
    uint32_t counter_hz,profile_crc;
    uint8_t mask,reference,aux_mask,policy;
    uint64_t reference_tick;
    NpFirstSyncProbe probes[3];
    uint32_t discovery_ms,discovery_cycles;
    NpFirstSyncAux aux[4];
} NpFirstSyncReceipt;
typedef struct {
    uint32_t session,generation,source_epoch,domain,counter_hz,profile_crc;
    uint32_t family,mask,ap_hz;
} NpFirstSyncBinding;
static inline uint16_t np_first_u16(const uint8_t *p)
{return (uint16_t)((uint16_t)p[0]|((uint16_t)p[1]<<8));}
static inline uint32_t np_first_u32(const uint8_t *p)
{return (uint32_t)p[0]|((uint32_t)p[1]<<8)|((uint32_t)p[2]<<16)|((uint32_t)p[3]<<24);}
static inline uint64_t np_first_u64(const uint8_t *p)
{return (uint64_t)np_first_u32(p)|((uint64_t)np_first_u32(p+4)<<32);}
static inline void np_first_put16(uint8_t *p,uint16_t n)
{p[0]=(uint8_t)n;p[1]=(uint8_t)(n>>8);}
static inline void np_first_put32(uint8_t *p,uint32_t n)
{unsigned i;for(i=0U;i<4U;i++)p[i]=(uint8_t)(n>>(8U*i));}
static inline void np_first_put64(uint8_t *p,uint64_t n)
{np_first_put32(p,(uint32_t)n);np_first_put32(p+4,(uint32_t)(n>>32));}
static inline uint32_t np_first_crc(const uint8_t *p,uint32_t n)
{uint32_t c=~0U,i,j;for(i=0U;i<n;i++){c^=p[i];for(j=0U;j<8U;j++)c=(c>>1)^((c&1U)?UINT32_C(0xedb88320):0U);}return ~c;}
static inline int np_first_zero(const uint8_t *p,uint32_t n)
{uint32_t i;for(i=0U;i<n;i++)if(p[i])return 0;return 1;}
static inline uint64_t np_first_gcd(uint64_t a,uint64_t b)
{while(b){uint64_t r=a%b;a=b;b=r;}return a;}
static inline int np_first_profile(uint32_t hz)
{return hz==7500U || hz==15000U || hz==20000U || hz==30000U;}
/* NP source ticks per auxiliary sample, normalized; never truncate a fraction. */
static inline int np_first_grid_step(uint32_t source_hz,uint32_t rate_num,
                                    uint32_t rate_den,uint64_t *num,uint32_t *den)
{
    uint64_t n,g;if(!source_hz || !rate_num || !rate_den || !num || !den)return 0;
    n=(uint64_t)source_hz*rate_den;g=np_first_gcd(n,rate_num);
    *num=n/g;*den=(uint32_t)(rate_num/g);return 1;
}
static inline int np_first_sync_validate(const uint8_t *b,size_t n)
{
    uint32_t f,family,hz,mask,ref,all=1U,gated=1U,p,a;
    if(!b || n!=NP_FIRST_SYNC_BYTES || memcmp(b,"NPSTART1",8U) ||
       np_first_u16(b+8)!=1U || np_first_u16(b+10)!=NP_FIRST_SYNC_BYTES ||
       np_first_u32(b+324)!=np_first_crc(b,324U))return 0;
    f=np_first_u32(b+12);family=(f>>8)&255U;hz=np_first_u32(b+36);mask=b[48];ref=1U;
    if((f&~UINT32_C(0xff3f)) || (family!=1U && family!=2U) || !np_first_profile(hz) ||
       np_first_u32(b+32)!=hz*(family==1U?780U:1600U) || !np_first_u32(b+16) ||
       !np_first_u32(b+20) || !np_first_u32(b+24) || !mask || (mask&~7U) ||
       (b[50]&~15U) || b[51]!=(b[50]?1U:0U) ||
       ((!np_first_u32(b+28))!=(!np_first_u32(b+40))) || np_first_u32(b+40)>1000000000U ||
       (!(f&NP_FIRST_PROFILE) && np_first_u32(b+44)!=UINT32_MAX))return 0;
    while(!(mask&(1U<<(ref-1U))))ref++;
    if(b[49]!=ref)return 0;
    for(p=0U;p<3U;p++){
        const uint8_t *q=b+64U+p*20U;
        uint32_t pf=np_first_u32(q+16),required=family==1U?31U:27U;
        uint32_t period=family==1U?780U:1600U;
        if(!(mask&(1U<<p))){if(!np_first_zero(q,20U))return 0;continue;}
        if(np_first_u16(q+14)!=period || (pf!=1U && pf!=required))return 0;
        if(pf==1U){
            if(np_first_u64(q)!=UINT64_MAX || np_first_u32(q+8)!=UINT32_MAX ||
               np_first_u16(q+12)!=UINT16_MAX)return 0;all=0U;
        }else{
            uint64_t tick=np_first_u64(q);uint32_t phase=np_first_u16(q+12);
            if(!(f&NP_FIRST_FINAL) || tick==UINT64_MAX || phase>=period ||
               (tick%period+phase)%period ||
               (family==1U?np_first_u32(q+8)>UINT32_C(0xfffff):np_first_u32(q+8)!=UINT32_MAX))return 0;
        }
    }
    if(((f&NP_FIRST_ALL_PORTS)!=0U)!=(all!=0U) ||
       np_first_u64(b+52)!=np_first_u64(b+64U+(ref-1U)*20U) || !np_first_zero(b+60U,4U))return 0;
    if(f&NP_FIRST_DISCOVERY){if(!all || !np_first_u32(b+28))return 0;}
    else if(np_first_u32(b+124)!=UINT32_MAX || np_first_u32(b+128)!=UINT32_MAX)return 0;
    for(a=0U;a<4U;a++){
        const uint8_t *q=b+132U+a*48U;uint32_t af=q[1],rn=np_first_u32(q+28),rd=np_first_u32(q+32);
        uint64_t on=np_first_u64(q+36);uint32_t od=np_first_u32(q+44);
        if(!(b[50]&(1U<<a))){if(!np_first_zero(q,48U))return 0;continue;}
        if(q[0]!=a+1U || !(af&NP_FIRST_AUX_ENABLED) || (af&~127U) ||
           ((!rn)!=(!rd)) || (rn && np_first_gcd(rn,rd)!=1U))return 0;
        if(af&NP_FIRST_AUX_RELEASE){if(!all || !np_first_u32(b+28))return 0;}
        else if(np_first_u32(q+4)!=UINT32_MAX || np_first_u32(q+8)!=UINT32_MAX)return 0;
        if(af&NP_FIRST_AUX_COUNTER){if(!all || q[2]<1U || q[2]>64U ||
            (q[2]<64U && np_first_u64(q+20)>=(UINT64_C(1)<<q[2])))return 0;}
        else if(q[2] || np_first_u64(q+20)!=UINT64_MAX)return 0;
        if(af&NP_FIRST_AUX_POINT){if(!(af&NP_FIRST_AUX_COUNTER) || !np_first_u32(b+28) ||
            (q[3]!=NP_FIRST_STAMP_SOURCE && q[3]!=NP_FIRST_STAMP_FOREGROUND))return 0;}
        else if(q[3] || np_first_u32(q+12)!=UINT32_MAX || np_first_u32(q+16)!=UINT32_MAX)return 0;
        if(af&NP_FIRST_AUX_OFFSET){if(!(af&NP_FIRST_AUX_COUNTER) || !od || on>INT64_MAX ||
            np_first_gcd(on,od)!=1U)return 0;}
        else if(on!=UINT64_C(0x8000000000000000) || od)return 0;
        if((af&NP_FIRST_AUX_EXCLUDED) && !(af&NP_FIRST_AUX_OFFSET))return 0;
        if(!(af&NP_FIRST_AUX_EXCLUDED))gated=0U;
        if((af&NP_FIRST_AUX_SAMPLE_LOCK) &&
           ((af&(NP_FIRST_AUX_COUNTER|NP_FIRST_AUX_OFFSET|NP_FIRST_AUX_EXCLUDED))!=
            (NP_FIRST_AUX_COUNTER|NP_FIRST_AUX_OFFSET|NP_FIRST_AUX_EXCLUDED) || !rn || on || od!=1U ||
            ((af&NP_FIRST_AUX_POINT) && q[3]==NP_FIRST_STAMP_FOREGROUND)))return 0;
    }
    return ((f&NP_FIRST_AUX_GATED)!=0U)==(b[50]!=0U && gated!=0U);
}
/* Raw pointer validation avoids a second full receipt/SD bound object. NPAR
 * carries no domain field: that binding is supplied by this exact receipt. */
static inline int np_first_sync_source_anchor_valid(const uint8_t a[64],const uint8_t r[NP_FIRST_SYNC_BYTES])
{
    uint32_t family,hz;uint64_t reference;
    if(!a || !np_first_sync_validate(r,NP_FIRST_SYNC_BYTES))return 0;
    family=(np_first_u32(r+12)>>8)&255U;hz=np_first_u32(r+40);reference=np_first_u64(r+52);
    return (np_first_u32(r+12)&(NP_FIRST_FINAL|NP_FIRST_ALL_PORTS|NP_FIRST_SOURCE_CLOCK))==
        (NP_FIRST_FINAL|NP_FIRST_ALL_PORTS|NP_FIRST_SOURCE_CLOCK) && np_first_u32(r+28) && hz>=1000000U && hz<=1000000000U &&
        np_first_u32(a)==UINT32_C(0x5241504e) && np_first_u32(a+4)==1U && np_first_u32(a+8) &&
        np_first_u32(a+12)==((family<<8)|31U) && np_first_u32(a+16)==np_first_u32(r+20) &&
        np_first_u32(a+36)==hz && np_first_u32(a+40)==np_first_u32(r+32) && np_first_u32(a+52)==np_first_u32(r+36) &&
        np_first_u32(a+56)==1000U && np_first_u32(a+60)==np_first_crc(a,60U) &&
        np_first_u32(a+24)-np_first_u32(a+20)<=1U && np_first_u32(a+32)-np_first_u32(a+28)<=hz/10000U &&
        reference!=UINT64_MAX && np_first_u64(a+44)<=UINT64_MAX-reference;
}
static inline int np_first_sync_pack(uint8_t out[NP_FIRST_SYNC_BYTES],const NpFirstSyncReceipt *r)
{
    unsigned i;if(!out || !r)return 0;memset(out,0,NP_FIRST_SYNC_BYTES);memcpy(out,"NPSTART1",8U);
    np_first_put16(out+8,1U);np_first_put16(out+10,NP_FIRST_SYNC_BYTES);
    np_first_put32(out+12,r->flags);np_first_put32(out+16,r->session);np_first_put32(out+20,r->generation);
    np_first_put32(out+24,r->source_epoch);np_first_put32(out+28,r->domain);np_first_put32(out+32,r->source_hz);
    np_first_put32(out+36,r->ap_hz);np_first_put32(out+40,r->counter_hz);np_first_put32(out+44,r->profile_crc);
    out[48]=r->mask;out[49]=r->reference;out[50]=r->aux_mask;out[51]=r->policy;np_first_put64(out+52,r->reference_tick);
    for(i=0U;i<3U;i++){
        uint8_t *q=out+64U+i*20U;const NpFirstSyncProbe *p=&r->probes[i];
        np_first_put64(q,p->first_tick);np_first_put32(q+8,p->counter);np_first_put16(q+12,p->phase);
        np_first_put16(q+14,p->period);np_first_put32(q+16,p->flags);
    }
    np_first_put32(out+124,r->discovery_ms);np_first_put32(out+128,r->discovery_cycles);
    for(i=0U;i<4U;i++){
        uint8_t *q=out+132U+i*48U;const NpFirstSyncAux *a=&r->aux[i];
        q[0]=a->id;q[1]=a->flags;q[2]=a->counter_bits;q[3]=a->stamp_kind;
        np_first_put32(q+4,a->release_ms);np_first_put32(q+8,a->release_cycles);
        np_first_put32(q+12,a->first_ms);np_first_put32(q+16,a->first_cycles);np_first_put64(q+20,a->counter);
        np_first_put32(q+28,a->rate_num);np_first_put32(q+32,a->rate_den);
        np_first_put64(q+36,(uint64_t)a->offset_num);np_first_put32(q+44,a->offset_den);
    }
    np_first_put32(out+324,np_first_crc(out,324U));
    if(np_first_sync_validate(out,NP_FIRST_SYNC_BYTES))return 1;
    memset(out,0,NP_FIRST_SYNC_BYTES);return 0;
}
/* Once-only admission predicate. Never scan/CRC every sample. A stale binding
 * cannot release auxiliary publication; owning producer invalidates its cached
 * permission on reset/rearm, profile change, source loss or generation change. */
static inline int np_first_sync_gate_match(const uint8_t *b,size_t n,const NpFirstSyncBinding *e)
{
    uint32_t flags;if(!e || !np_first_sync_validate(b,n))return 0;flags=np_first_u32(b+12);
    return (flags&(NP_FIRST_FINAL|NP_FIRST_ALL_PORTS))==
        (NP_FIRST_FINAL|NP_FIRST_ALL_PORTS) &&
        ((flags>>8)&255U)==e->family && np_first_u32(b+16)==e->session &&
        np_first_u32(b+20)==e->generation && np_first_u32(b+24)==e->source_epoch &&
        np_first_u32(b+28)==e->domain && np_first_u32(b+40)==e->counter_hz &&
        np_first_u32(b+44)==e->profile_crc && b[48]==e->mask && np_first_u32(b+36)==e->ap_hz;
}
#endif
