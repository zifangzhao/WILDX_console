#ifndef NP_SENSOR_FINE64_WIRE_H
#define NP_SENSOR_FINE64_WIRE_H
#include "np_sensor_fine_wire.h"

/* NEW explicit continuity format; no existing SIF API or ABI is widened.
 * The independently qualified producer owns the publication64 clock lifetime.
 * HAL milliseconds are native identity bytes only, never a wrap/phase proof. */
#define NP_SENSOR_FINE64_BYTES 28U
#define NP_SENSOR_FINE64_TAG UINT32_C(0x02464953)
#define NP_SENSOR_FINE64_HEADER_FLAG 4096U
#define NP_SENSOR_FINE64_READ_QUANTUM 1U
typedef struct {
    uint32_t mask,domain,generation,source_epoch,counter_hz,native_age_boundary_mask;
    uint64_t publication_counter64;
    uint32_t counter[3];
    /* Actual interval is [counter64,counter64+1]. Never add1 in uint64_t
     * when lower==UINT64_MAX. Window intersection does not shift this read. */
    uint64_t counter64[3],window_lower[3],window_upper[3];
} NpSensorFine64Stamp;
static inline int np_sensor_fine64_overlap(const void *a,size_t an,const void *b,size_t bn)
{
    uintptr_t x=(uintptr_t)a,y=(uintptr_t)b;return a && b && (x<=y?y-x<an:x-y<bn);
}
static inline int np_sensor_fine64_resolve(NpSensorFine64Stamp *s,const NpSensorFineBinding *b)
{
    const uint32_t age_ms[3]={100U,200U,3000U};const uint64_t wrap=UINT64_C(0x100000000);
    if(!s || !np_sensor_fine_binding(b,s->mask))return 0;
    s->native_age_boundary_mask=0U;
    memset(s->counter64,0,sizeof(s->counter64));memset(s->window_lower,0,sizeof(s->window_lower));memset(s->window_upper,0,sizeof(s->window_upper));
    for(unsigned i=0U;i<3U;i++){
        if(!(s->mask&(1U<<i))){if(s->counter[i])return 0;continue;}
        /* Exact configured whole-tick policy, nominal milliseconds floored. */
        uint64_t age=(uint64_t)b->counter_hz*age_ms[i]/1000U,pub=s->publication_counter64;
        if(age>=wrap)return 0;
        uint64_t low=pub>age?pub-age:0U,n=(pub&UINT64_C(0xffffffff00000000))|s->counter[i];
        if(n>pub){if(n<wrap)return 0;n-=wrap;}
        if(n<low && low-n>1U)return 0;
        if(n>=wrap){uint64_t prior=n-wrap;if(prior>=low || low-prior<=1U)return 0;}
        s->counter64[i]=n;s->window_lower[i]=low;s->window_upper[i]=pub;
        if(n<low || n==pub)s->native_age_boundary_mask|=1U<<i;
    }
    s->domain=b->domain;s->generation=b->generation;s->source_epoch=b->source_epoch;s->counter_hz=b->counter_hz;return 1;
}
static inline uint32_t np_sensor_fine64_crc(const uint8_t raw[24],const NpSensorFineBinding *b,uint32_t mask)
{
    uint8_t proof[64];uint32_t n=40U;memcpy(proof,raw,24U);np_sensor_fine_context(proof+24U,b);
    for(unsigned i=0U;i<3U;i++)if(mask&(1U<<i)){
        np_first_put32(proof+n,b->sequence[i]);np_first_put32(proof+n+4U,b->source_ms[i]);n+=8U;
    }
    return np_first_crc(proof,n);
}
static inline int np_sensor_fine64_parse(const uint8_t *raw,uint32_t bytes,const NpSensorFineBinding *b,int allow,NpSensorFine64Stamp *out)
{
    NpSensorFine64Stamp parsed={0};if(out)memset(out,0,sizeof(*out));
    if(!raw || bytes!=NP_SENSOR_FINE64_BYTES)return NP_SENSOR_FINE_INVALID;
    if(np_first_zero(raw,NP_SENSOR_FINE64_BYTES))return NP_SENSOR_FINE_ABSENT;
    parsed.mask=(np_first_u32(raw)>>28U)&7U;
    if(!allow || np_first_u32(raw)!=(NP_SENSOR_FINE64_TAG|(parsed.mask<<28U)) || !np_sensor_fine_binding(b,parsed.mask) ||
       np_first_u32(raw+24U)!=np_sensor_fine64_crc(raw,b,parsed.mask))return NP_SENSOR_FINE_INVALID;
    parsed.publication_counter64=np_first_u64(raw+4U);
    for(unsigned i=0U;i<3U;i++)parsed.counter[i]=np_first_u32(raw+12U+i*4U);
    if(!np_sensor_fine64_resolve(&parsed,b))return NP_SENSOR_FINE_INVALID;
    if(out)*out=parsed;return NP_SENSOR_FINE_VALID;
}
static inline int np_sensor_fine64_stamp_matches(const NpSensorFine64Stamp *s,const NpSensorFineBinding *b)
{
    return s && b && s->domain==b->domain && s->generation==b->generation && s->source_epoch==b->source_epoch && s->counter_hz==b->counter_hz;
}
static inline int np_sensor_fine64_pack(uint8_t out[28],const NpSensorFine64Stamp *s,const NpSensorFineBinding *b,int allow)
{
    uint8_t raw[28]={0};
    if(!out || np_sensor_fine64_overlap(out,28U,s,sizeof(*s)) || np_sensor_fine64_overlap(out,28U,b,sizeof(*b)))return NP_SENSOR_FINE_INVALID;
    memset(out,0,28U);
    if(!allow || !np_sensor_fine64_stamp_matches(s,b) || !np_sensor_fine_binding(b,s->mask))return NP_SENSOR_FINE_INVALID;
    np_first_put32(raw,NP_SENSOR_FINE64_TAG|(s->mask<<28U));np_first_put64(raw+4U,s->publication_counter64);
    for(unsigned i=0U;i<3U;i++)np_first_put32(raw+12U+i*4U,s->counter[i]);
    np_first_put32(raw+24U,np_sensor_fine64_crc(raw,b,s->mask));
    if(np_sensor_fine64_parse(raw,sizeof(raw),b,1,NULL)!=NP_SENSOR_FINE_VALID)return NP_SENSOR_FINE_INVALID;
    memcpy(out,raw,sizeof(raw));return NP_SENSOR_FINE_VALID;
}
static inline int np_sensor_fine64_core(const uint8_t *core,const NpSensorFineBinding *b,uint32_t mask,uint32_t enabled,uint32_t valid)
{
    if(!np_sensor_fine_binding(b,mask) || enabled>3U || valid!=b->sensor_valid ||
       ((valid&3U) && !(enabled&1U)) || ((valid&4U) && !(enabled&2U)) || np_first_u32(core+44U)>=0x1000000U)return 0;
    for(unsigned i=0U;i<3U;i++)if(np_first_u32(core+20U+i*4U)!=b->sequence[i] || np_first_u32(core+32U+i*4U)!=b->source_ms[i])return 0;
    return 1;
}
static inline uint32_t np_sensor_fine64_header_crc(const uint8_t h[512],const NpSensorFineBinding *b,int sd)
{
    const uint32_t offset[2][3]={{88U,92U,504U},{384U,388U,392U}};uint8_t proof[140];
    np_sensor_fine_context(proof,b);memcpy(proof+16U,h+(sd?396U:256U),112U);
    for(unsigned i=0U;i<3U;i++)np_first_put32(proof+128U+i*4U,np_first_u32(h+offset[sd?1:0][i]));
    return np_first_crc(proof,sizeof(proof));
}
static inline int np_sensor_fine64_header_parse(const uint8_t *h,uint32_t bytes,const NpSensorFineBinding *b,int allow,int sd,NpSensorFine64Stamp *out)
{
    const uint32_t offset[2][3]={{88U,92U,504U},{384U,388U,392U}};uint32_t at=sd?396U:256U,version=sd?4U:3U;
    NpSensorFine64Stamp parsed={0};if(out)memset(out,0,sizeof(*out));
    if(!h || bytes!=512U)return NP_SENSOR_FINE_INVALID;
    uint32_t flags=np_first_u32(h+52U),word=np_first_u32(h+at+4U);
    if(!(flags&NP_SENSOR_FINE64_HEADER_FLAG)){
        if((word&255U)==version)return NP_SENSOR_FINE_INVALID;
        for(unsigned i=0U;i<3U;i++)if(np_first_u32(h+offset[sd?1:0][i]))return NP_SENSOR_FINE_INVALID;
        return NP_SENSOR_FINE_ABSENT;
    }
    if(!allow || !np_sensor_fine_v2_geometry(flags,sd))return NP_SENSOR_FINE_INVALID;
    int absent=np_first_zero(h+at,112U) && !np_first_u32(h+508U);
    for(unsigned i=0U;i<3U;i++)absent=absent && !np_first_u32(h+offset[sd?1:0][i]);
    if(absent)return NP_SENSOR_FINE_ABSENT;
    uint32_t enabled=(word>>8U)&3U,valid=(word>>12U)&7U;parsed.mask=(word>>16U)&7U;
    if(np_first_u32(h+at)!=(sd?UINT32_C(0x32534e53):UINT32_C(0x31534e53)) ||
       word!=(version|(enabled<<8U)|(valid<<12U)|(parsed.mask<<16U)) ||
       !np_sensor_fine64_core(h+at,b,parsed.mask,enabled,valid) || np_first_u32(h+508U)!=np_sensor_fine64_header_crc(h,b,sd))return NP_SENSOR_FINE_INVALID;
    parsed.publication_counter64=np_first_u64(h+at+8U);
    for(unsigned i=0U;i<3U;i++)parsed.counter[i]=np_first_u32(h+offset[sd?1:0][i]);
    if(!np_sensor_fine64_resolve(&parsed,b))return NP_SENSOR_FINE_INVALID;
    if(out)*out=parsed;return NP_SENSOR_FINE_VALID;
}
static inline int np_sensor_fine64_usb_parse(const uint8_t *h,uint32_t bytes,const NpSensorFineBinding *b,int allow,NpSensorFine64Stamp *out)
{return np_sensor_fine64_header_parse(h,bytes,b,allow,0,out);}
static inline int np_sensor_fine64_sd_parse(const uint8_t *h,uint32_t bytes,const NpSensorFineBinding *b,int allow,NpSensorFine64Stamp *out)
{return np_sensor_fine64_header_parse(h,bytes,b,allow,1,out);}
static inline int np_sensor_fine64_header_pack(uint8_t out[512],const uint8_t *source,uint32_t bytes,const NpSensorFine64Stamp *s,const NpSensorFineBinding *b,int allow,int sd)
{
    const uint32_t offset[2][3]={{88U,92U,504U},{384U,388U,392U}};uint32_t at=sd?396U:256U;uint8_t raw[28];
    if(!out || !source || bytes!=512U || np_sensor_fine64_overlap(out,512U,source,512U) ||
       np_sensor_fine64_overlap(out,512U,s,sizeof(*s)) || np_sensor_fine64_overlap(out,512U,b,sizeof(*b)))return NP_SENSOR_FINE_INVALID;
    memcpy(out,source,512U);uint32_t flags=np_first_u32(source+52U),enabled=np_first_u32(source+at+8U),valid=np_first_u32(source+at+12U);
    if(!allow || !np_sensor_fine64_stamp_matches(s,b) || (flags&NP_SENSOR_FINE64_HEADER_FLAG) || !np_sensor_fine_v2_geometry(flags,sd) ||
       np_first_u32(source+at)!=(sd?UINT32_C(0x32534e53):UINT32_C(0x31534e53)) || np_first_u32(source+at+4U)!=(sd?2U:1U) ||
       !np_sensor_fine64_core(source+at,b,s->mask,enabled,valid) ||
       (sd?np_first_u32(source+508U)!=np_first_crc(source+at,112U):np_first_u32(source+508U)!=0U))return NP_SENSOR_FINE_INVALID;
    for(unsigned i=0U;i<3U;i++)if(np_first_u32(source+offset[sd?1:0][i]))return NP_SENSOR_FINE_INVALID;
    if(np_sensor_fine64_pack(raw,s,b,allow)!=NP_SENSOR_FINE_VALID)return NP_SENSOR_FINE_INVALID;
    np_first_put32(out+52U,flags|NP_SENSOR_FINE64_HEADER_FLAG);
    np_first_put32(out+at+4U,(sd?4U:3U)|(enabled<<8U)|(valid<<12U)|(s->mask<<16U));np_first_put64(out+at+8U,s->publication_counter64);
    for(unsigned i=0U;i<3U;i++)np_first_put32(out+offset[sd?1:0][i],s->counter[i]);
    np_first_put32(out+508U,np_sensor_fine64_header_crc(out,b,sd));return NP_SENSOR_FINE_VALID;
}
static inline int np_sensor_fine64_usb_pack(uint8_t out[512],const uint8_t *source,uint32_t bytes,const NpSensorFine64Stamp *s,const NpSensorFineBinding *b,int allow)
{return np_sensor_fine64_header_pack(out,source,bytes,s,b,allow,0);}
static inline int np_sensor_fine64_sd_pack(uint8_t out[512],const uint8_t *source,uint32_t bytes,const NpSensorFine64Stamp *s,const NpSensorFineBinding *b,int allow)
{return np_sensor_fine64_header_pack(out,source,bytes,s,b,allow,1);}
#endif
