#ifndef NP_FINE_CLOCK_WIRE_H
#define NP_FINE_CLOCK_WIRE_H
#include "np_first_sync_sd.h"

/* Pure codecs. Qualification is an owner attestation about two actual points
 * in one continuous clock lifetime, not something serialization can measure. */
#define NP_FINE_CLOCK_REQUEST UINT32_C(0x4654504e) /* NPTF */
#define NP_FINE_CLOCK_REPLY UINT32_C(0x4652504e) /* NPRF */
enum { NP_FINE_CLOCK_QUALIFIED=1U,NP_FINE_CAPTURE_BOUND=2U,NP_FINE_UART_BOUND=4U };
enum { NP_FINE_CLOCK_MAX_HOLD_US=4000U };
/* Per-record format only, not evidence of source release/sample lock/phase. */
enum { NP_FINE_SD_COMPANION_LEGACY=1U,NP_FINE_SD_COMPANION_FINE_AUX=9U,NP_FINE_SD_COMPANION_FINE_COUNTER64=25U };
typedef struct {
    uint32_t flags,domain,hz,generation,source_epoch,rx_ms,rx_counter,tx_ms,tx_counter,uart_baud;
} NpFineClockReply;
typedef struct {NpFirstSdBound bound;uint8_t anchor[64];} NpFineSdCompanion;
static inline int np_fine_overlap(const void *a,size_t an,const void *b,size_t bn)
{
    uintptr_t x=(uintptr_t)a,y=(uintptr_t)b;
    return a && b && (x<=y?y-x<an:x-y<bn);
}

static inline int np_fine_clock_request_valid_version(const uint8_t *b,size_t bytes,const uint32_t uid[3],uint16_t version)
{
    return (version==1U || version==2U) && b && bytes==64U && np_first_u32(b)==NP_FINE_CLOCK_REQUEST && np_first_u16(b+4)==version &&
        !np_first_u16(b+6) && np_first_u32(b+8) && np_first_sd_uid_same(b+12,uid) &&
        np_first_u64(b+24) && (version==1U?np_first_zero(b+32,28U):
        np_first_u32(b+32)<=NP_FINE_CLOCK_MAX_HOLD_US && np_first_zero(b+36,24U)) && np_first_u32(b+60)==np_first_crc(b,60U);
}
static inline int np_fine_clock_request_pack_version(uint8_t out[64],uint32_t id,const uint32_t uid[3],uint64_t nonce,uint16_t version,uint32_t hold_us)
{
    if(!out || np_fine_overlap(out,64U,uid,12U))return 0;
    memset(out,0,64U);
    if(!id || !nonce || !np_first_sd_uid_valid(uid) || (version!=1U && version!=2U) ||
       (version==1U?hold_us!=0U:hold_us>NP_FINE_CLOCK_MAX_HOLD_US))return 0;
    np_first_put32(out,NP_FINE_CLOCK_REQUEST);np_first_put16(out+4,version);np_first_put32(out+8,id);
    np_first_sd_uid_put(out+12,uid);np_first_put64(out+24,nonce);np_first_put32(out+32,hold_us);
    np_first_put32(out+60,np_first_crc(out,60U));return 1;
}
static inline int np_fine_clock_reply_valid_version(const uint8_t *b,size_t bytes,const uint8_t request[64],uint32_t family,uint16_t version)
{
    uint32_t uid[3],flags,hz,ms,cycles;uint64_t coarse,fine,difference;
    if(!request)return 0;
    np_first_sd_uid_get(request+12,uid);
    if(!np_fine_clock_request_valid_version(request,64U,uid,version) || !b || bytes!=80U || (family!=1U && family!=2U) ||
       np_first_u32(b)!=NP_FINE_CLOCK_REPLY || np_first_u16(b+4)!=version ||
       memcmp(b+8,request+8,24U) || (version==1U?!np_first_zero(b+68,8U):
       np_first_u32(b+68)!=np_first_u32(request+32) || !np_first_zero(b+72,4U)) || np_first_u32(b+76)!=np_first_crc(b,76U))return 0;
    flags=np_first_u16(b+6);
    if((flags&~UINT32_C(0x307)) || (flags>>8)!=family)return 0;
    flags&=7U;
    if(!(flags&NP_FINE_CLOCK_QUALIFIED))return !flags && np_first_zero(b+32,36U);
    hz=np_first_u32(b+36);
    if(!np_first_u32(b+32) || hz<1000000U || hz>1000000000U)return 0;
    if(flags&NP_FINE_CAPTURE_BOUND){if(!np_first_u32(b+40) || !np_first_u32(b+44))return 0;}
    else if(np_first_u32(b+40) || np_first_u32(b+44))return 0;
    if(flags&NP_FINE_UART_BOUND){if(np_first_u32(b+64)<1200U || np_first_u32(b+64)>12000000U)return 0;}
    else if(np_first_u32(b+64))return 0;
    ms=np_first_u32(b+56)-np_first_u32(b+48);cycles=np_first_u32(b+60)-np_first_u32(b+52);
    if(ms>1000U || cycles>hz)return 0;
    coarse=(uint64_t)ms*hz;fine=(uint64_t)cycles*1000U;
    difference=coarse>fine?coarse-fine:fine-coarse;
    return difference<=(uint64_t)hz*2U;
}
static inline int np_fine_clock_reply_pack_version(uint8_t out[80],const uint8_t request[64],uint32_t family,const NpFineClockReply *r,uint16_t version)
{
    if(!out || np_fine_overlap(out,80U,request,64U))return 0;
    memset(out,0,80U);
    if(!request || !r || (r->flags&~7U) || (family!=1U && family!=2U))return 0;
    np_first_put32(out,NP_FINE_CLOCK_REPLY);np_first_put16(out+4,version);
    np_first_put16(out+6,(uint16_t)((family<<8)|r->flags));memcpy(out+8,request+8,24U);
    np_first_put32(out+32,r->domain);np_first_put32(out+36,r->hz);np_first_put32(out+40,r->generation);
    np_first_put32(out+44,r->source_epoch);np_first_put32(out+48,r->rx_ms);np_first_put32(out+52,r->rx_counter);
    np_first_put32(out+56,r->tx_ms);np_first_put32(out+60,r->tx_counter);np_first_put32(out+64,r->uart_baud);
    if(version==2U)np_first_put32(out+68,np_first_u32(request+32));
    np_first_put32(out+76,np_first_crc(out,76U));
    if(np_fine_clock_reply_valid_version(out,80U,request,family,version))return 1;
    memset(out,0,80U);return 0;
}
/* Existing APIs deliberately remain strict v1 and byte-identical. */
static inline int np_fine_clock_request_valid(const uint8_t *b,size_t bytes,const uint32_t uid[3])
{
    return np_fine_clock_request_valid_version(b,bytes,uid,1U);
}
static inline int np_fine_clock_request_pack(uint8_t out[64],uint32_t id,const uint32_t uid[3],uint64_t nonce)
{
    return np_fine_clock_request_pack_version(out,id,uid,nonce,1U,0U);
}
static inline int np_fine_clock_reply_valid(const uint8_t *b,size_t bytes,const uint8_t request[64],uint32_t family)
{
    return np_fine_clock_reply_valid_version(b,bytes,request,family,1U);
}
static inline int np_fine_clock_reply_pack(uint8_t out[80],const uint8_t request[64],uint32_t family,const NpFineClockReply *r)
{
    return np_fine_clock_reply_pack_version(out,request,family,r,1U);
}
/* Explicit v2 opt-in. The hold is only an echoed request; actual measured
 * Rx/Tx counters, never this delay request, constrain clock precision. */
static inline int np_fine_clock_deferred_request_valid(const uint8_t *b,size_t bytes,const uint32_t uid[3])
{
    return np_fine_clock_request_valid_version(b,bytes,uid,2U);
}
static inline int np_fine_clock_deferred_request_pack(uint8_t out[64],uint32_t id,const uint32_t uid[3],uint64_t nonce,uint32_t min_hold_us)
{
    return np_fine_clock_request_pack_version(out,id,uid,nonce,2U,min_hold_us);
}
static inline int np_fine_clock_deferred_reply_valid(const uint8_t *b,size_t bytes,const uint8_t request[64],uint32_t family)
{
    return np_fine_clock_reply_valid_version(b,bytes,request,family,2U);
}
static inline int np_fine_clock_deferred_reply_pack(uint8_t out[80],const uint8_t request[64],uint32_t family,const NpFineClockReply *r)
{
    return np_fine_clock_reply_pack_version(out,request,family,r,2U);
}
static inline int np_fine_sd_anchor_valid(const uint8_t a[64],const NpFirstSdBound *bound,uint32_t family)
{
    const uint8_t *r;uint32_t hz,width;uint64_t origin,reference;
    if(!a || !bound || (family!=1U && family!=2U))return 0;
    r=bound->receipt;hz=np_first_u32(r+40);
    if((np_first_u32(r+12)&(NP_FIRST_FINAL|NP_FIRST_ALL_PORTS|NP_FIRST_SOURCE_CLOCK))!=
       (NP_FIRST_FINAL|NP_FIRST_ALL_PORTS|NP_FIRST_SOURCE_CLOCK) || !np_first_u32(r+28) ||
       hz<1000000U || hz>1000000000U || np_first_u32(a)!=UINT32_C(0x5241504e) ||
       np_first_u32(a+4)!=1U || np_first_u32(a+12)!=((family<<8)|31U) ||
       np_first_u32(a+16)!=np_first_u32(r+20) || np_first_u32(a+36)!=hz ||
       np_first_u32(a+40)!=np_first_u32(r+32) || np_first_u32(a+52)!=np_first_u32(r+36) ||
       np_first_u32(a+56)!=1000U || np_first_u32(a+60)!=np_first_crc(a,60U) ||
       np_first_u32(a+24)-np_first_u32(a+20)>1U)return 0;
    width=np_first_u32(a+32)-np_first_u32(a+28);
    origin=np_first_u64(a+44);reference=np_first_u64(r+52);
    return width<=hz/10000U && reference!=UINT64_MAX && origin<=UINT64_MAX-reference;
}
static inline int np_fine_sd_companion_read_features(const uint8_t *b,size_t bytes,const uint8_t clock[512],
    uint32_t family,uint32_t session,uint32_t ap_hz,const uint32_t expected_uid[3],uint64_t expected_token,NpFineSdCompanion *out,uint32_t features)
{
    NpFirstSdBound bound;const uint8_t *r;
    if((features!=NP_FINE_SD_COMPANION_LEGACY && features!=NP_FINE_SD_COMPANION_FINE_AUX && features!=NP_FINE_SD_COMPANION_FINE_COUNTER64) ||
       !b || bytes!=512U || np_first_zero(b,512U) ||
       !np_first_sd_sector_read(clock,512U,family,session,ap_hz,expected_uid,expected_token,&bound))return 0;
    r=bound.receipt;
    if(memcmp(b,"NPFCLK1\0",8U) || np_first_u32(b+8)!=1U || np_first_u32(b+12)!=family ||
       !np_first_sd_uid_same(b+16,bound.uid) || np_first_u32(b+28)!=session ||
       np_first_u32(b+32)!=np_first_u32(r+20) || np_first_u32(b+36)!=np_first_u32(r+24) ||
       np_first_u32(b+40)!=np_first_u32(r+44) || np_first_u32(b+44)!=r[48] ||
       np_first_u32(b+48)!=ap_hz || np_first_u32(b+52)!=np_first_u32(r+32) ||
       np_first_u64(b+56)!=bound.token || np_first_u32(b+64)!=np_first_u32(r+324) ||
       np_first_u32(b+68)!=features || !np_first_zero(b+136,372U) ||
       np_first_u32(b+508)!=np_first_crc(b,508U) || !np_fine_sd_anchor_valid(b+72,&bound,family))return 0;
    if(out){out->bound=bound;memcpy(out->anchor,b+72,64U);}return 1;
}
static inline int np_fine_sd_companion_pack_features(uint8_t out[512],const uint8_t clock[512],const uint8_t anchor[64],
    uint32_t family,const uint32_t uid[3],uint64_t token,uint32_t features)
{
    NpFirstSdBound bound;const uint8_t *r;
    if((features!=NP_FINE_SD_COMPANION_LEGACY && features!=NP_FINE_SD_COMPANION_FINE_AUX && features!=NP_FINE_SD_COMPANION_FINE_COUNTER64) ||
       !out || np_fine_overlap(out,512U,clock,512U) || np_fine_overlap(out,512U,anchor,64U))return 0;
    memset(out,0,512U);
    if(!clock || !np_first_sd_sector_read(clock,512U,family,np_first_u32(clock+12),np_first_u32(clock+72),uid,token,&bound) ||
       !np_fine_sd_anchor_valid(anchor,&bound,family))return 0;
    r=bound.receipt;
    memcpy(out,"NPFCLK1\0",8U);np_first_put32(out+8,1U);np_first_put32(out+12,family);np_first_sd_uid_put(out+16,bound.uid);
    np_first_put32(out+28,np_first_u32(r+16));np_first_put32(out+32,np_first_u32(r+20));np_first_put32(out+36,np_first_u32(r+24));
    np_first_put32(out+40,np_first_u32(r+44));np_first_put32(out+44,r[48]);np_first_put32(out+48,np_first_u32(r+36));
    np_first_put32(out+52,np_first_u32(r+32));np_first_put64(out+56,bound.token);np_first_put32(out+64,np_first_u32(r+324));
    np_first_put32(out+68,features);memcpy(out+72,anchor,64U);np_first_put32(out+508,np_first_crc(out,508U));return 1;
}
/* Existing APIs deliberately remain strict legacy1 and byte-identical. */
static inline int np_fine_sd_companion_read(const uint8_t *b,size_t bytes,const uint8_t clock[512],
    uint32_t family,uint32_t session,uint32_t ap_hz,const uint32_t expected_uid[3],uint64_t expected_token,NpFineSdCompanion *out)
{
    return np_fine_sd_companion_read_features(b,bytes,clock,family,session,ap_hz,expected_uid,expected_token,out,NP_FINE_SD_COMPANION_LEGACY);
}
static inline int np_fine_sd_companion_pack(uint8_t out[512],const uint8_t clock[512],const uint8_t anchor[64],
    uint32_t family,const uint32_t uid[3],uint64_t token)
{
    return np_fine_sd_companion_pack_features(out,clock,anchor,family,uid,token,NP_FINE_SD_COMPANION_LEGACY);
}
/* Only accepted NPSD v2 recordFeatures9 producers may select these APIs. */
static inline int np_fine_sd_companion_fine_aux_read(const uint8_t *b,size_t bytes,const uint8_t clock[512],
    uint32_t family,uint32_t session,uint32_t ap_hz,const uint32_t expected_uid[3],uint64_t expected_token,NpFineSdCompanion *out)
{
    return np_fine_sd_companion_read_features(b,bytes,clock,family,session,ap_hz,expected_uid,expected_token,out,NP_FINE_SD_COMPANION_FINE_AUX);
}
static inline int np_fine_sd_companion_fine_aux_pack(uint8_t out[512],const uint8_t clock[512],const uint8_t anchor[64],
    uint32_t family,const uint32_t uid[3],uint64_t token)
{
    return np_fine_sd_companion_pack_features(out,clock,anchor,family,uid,token,NP_FINE_SD_COMPANION_FINE_AUX);
}
/* Explicit recordFeatures25 only: the producer, not this codec, proves the
 * root BEFORE counter64 high word0 and continuous64 publication lifetime. */
static inline int np_fine_sd_companion_fine_counter64_read(const uint8_t *b,size_t bytes,const uint8_t clock[512],
    uint32_t family,uint32_t session,uint32_t ap_hz,const uint32_t expected_uid[3],uint64_t expected_token,NpFineSdCompanion *out)
{
    return np_fine_sd_companion_read_features(b,bytes,clock,family,session,ap_hz,expected_uid,expected_token,out,NP_FINE_SD_COMPANION_FINE_COUNTER64);
}
static inline int np_fine_sd_companion_fine_counter64_pack(uint8_t out[512],const uint8_t clock[512],const uint8_t anchor[64],
    uint32_t family,const uint32_t uid[3],uint64_t token)
{
    return np_fine_sd_companion_pack_features(out,clock,anchor,family,uid,token,NP_FINE_SD_COMPANION_FINE_COUNTER64);
}
#endif
