#ifndef NP_SENSOR_FINE_WIRE_H
#define NP_SENSOR_FINE_WIRE_H
#include "np_first_sync_wire.h"

/* Optional proof for a coherent native sensor tuple, not conversion phase or
 * sample lock. Seal once before mutable origin/validity filtering; retain these
 * bytes with that exact cache/queue entry. Never re-seal old counters against
 * newer native identities at serialization. No clock or device access here. */
#define NP_SENSOR_FINE_BYTES 28U
#define NP_SENSOR_FINE_TAG UINT32_C(0x01464953)
enum { NP_SENSOR_FINE_INVALID=-1, NP_SENSOR_FINE_ABSENT=0, NP_SENSOR_FINE_VALID=1 };
typedef struct {
    uint32_t domain,generation,source_epoch,counter_hz,sensor_valid;
    uint32_t sequence[3],source_ms[3]; /* AG, MAG, ENV original native identities */
} NpSensorFineBinding;
typedef struct {
    uint32_t mask,domain,generation,counter[3];
} NpSensorFineStamp;
static inline int np_sensor_fine_binding(const NpSensorFineBinding *b,uint32_t mask)
{
    if(!b || !mask || (mask&~7U) || !b->domain || !b->generation || !b->source_epoch ||
       b->counter_hz<1000000U || b->counter_hz>1000000000U ||
       (b->sensor_valid&~7U) || (mask&~b->sensor_valid))return 0;
    for(unsigned i=0U;i<3U;i++)if((mask&(1U<<i)) && !b->sequence[i])return 0;
    return 1;
}
static inline uint32_t np_sensor_fine_crc(const uint8_t prefix[24],const NpSensorFineBinding *b,uint32_t mask)
{
    uint8_t proof[56];uint32_t n=32U;
    memcpy(proof,prefix,24U);np_first_put32(proof+24U,b->source_epoch);np_first_put32(proof+28U,b->counter_hz);
    for(unsigned i=0U;i<3U;i++)if(mask&(1U<<i)){
        np_first_put32(proof+n,b->sequence[i]);np_first_put32(proof+n+4U,b->source_ms[i]);n+=8U;
    }
    return np_first_crc(proof,n);
}
/* 0 is absent only for exactly28 zero bytes; malformed optional data is -1.
 * Invalid/absent output is always cleared and must not gate neural integrity. */
static inline int np_sensor_fine_parse(const uint8_t *raw,uint32_t bytes,
    const NpSensorFineBinding *expected,NpSensorFineStamp *out)
{
    NpSensorFineStamp parsed={0};uint32_t tag;
    if(out)memset(out,0,sizeof(*out));
    if(!raw || bytes!=NP_SENSOR_FINE_BYTES)return NP_SENSOR_FINE_INVALID;
    if(np_first_zero(raw,NP_SENSOR_FINE_BYTES))return NP_SENSOR_FINE_ABSENT;
    tag=np_first_u32(raw);parsed.mask=(tag>>28U)&7U;
    if(tag!=(NP_SENSOR_FINE_TAG|(parsed.mask<<28U)) || !np_sensor_fine_binding(expected,parsed.mask))return NP_SENSOR_FINE_INVALID;
    parsed.domain=np_first_u32(raw+4U);parsed.generation=np_first_u32(raw+8U);
    if(parsed.domain!=expected->domain || parsed.generation!=expected->generation)return NP_SENSOR_FINE_INVALID;
    for(unsigned i=0U;i<3U;i++){
        parsed.counter[i]=np_first_u32(raw+12U+i*4U);
        if(!(parsed.mask&(1U<<i)) && parsed.counter[i])return NP_SENSOR_FINE_INVALID;
    }
    if(np_first_u32(raw+24U)!=np_sensor_fine_crc(raw,expected,parsed.mask))return NP_SENSOR_FINE_INVALID;
    if(out)*out=parsed;return NP_SENSOR_FINE_VALID;
}
static inline int np_sensor_fine_pack(uint8_t out[NP_SENSOR_FINE_BYTES],
    const NpSensorFineStamp *stamp,const NpSensorFineBinding *native)
{
    uint8_t raw[NP_SENSOR_FINE_BYTES]={0};
    if(!out)return NP_SENSOR_FINE_INVALID;
    memset(out,0,NP_SENSOR_FINE_BYTES);
    if(!stamp || !np_sensor_fine_binding(native,stamp->mask) ||
       stamp->domain!=native->domain || stamp->generation!=native->generation)return NP_SENSOR_FINE_INVALID;
    for(unsigned i=0U;i<3U;i++)if(!(stamp->mask&(1U<<i)) && stamp->counter[i])return NP_SENSOR_FINE_INVALID;
    np_first_put32(raw,NP_SENSOR_FINE_TAG|(stamp->mask<<28U));np_first_put32(raw+4U,stamp->domain);np_first_put32(raw+8U,stamp->generation);
    for(unsigned i=0U;i<3U;i++)np_first_put32(raw+12U+i*4U,stamp->counter[i]);
    np_first_put32(raw+24U,np_sensor_fine_crc(raw,native,stamp->mask));memcpy(out,raw,sizeof(raw));return NP_SENSOR_FINE_VALID;
}

/* V2 transports keep their complete physical sensor cores. These helpers are
 * optional proof codecs only: they never admit NN geometry, scan payloads, or
 * normalize a header for a neural verifier. Pack writes a distinct private
 * 512B header copy; on a rejected optional pack it retains the source bytes. */
#define NP_SENSOR_FINE_V2_HEADER_BYTES 512U
#define NP_SENSOR_FINE_V2_FLAG 4096U
static inline void np_sensor_fine_context(uint8_t p[16],const NpSensorFineBinding *b)
{
    np_first_put32(p,b->domain);np_first_put32(p+4U,b->generation);
    np_first_put32(p+8U,b->source_epoch);np_first_put32(p+12U,b->counter_hz);
}
static inline int np_sensor_fine_core(const uint8_t *core,const NpSensorFineBinding *b,uint32_t mask)
{
    if(!np_sensor_fine_binding(b,mask) || np_first_u32(core+12U)!=b->sensor_valid)return 0;
    for(unsigned i=0U;i<3U;i++)if(np_first_u32(core+20U+i*4U)!=b->sequence[i] ||
        np_first_u32(core+32U+i*4U)!=b->source_ms[i])return 0;
    return 1;
}
static inline int np_sensor_fine_v2_geometry(uint32_t flags,int sd)
{
    if((flags&32U)==0U)return 0; /* Existing NO_HEADER_CRC contract. */
    if(sd)return (flags&~(511U|1024U|2048U|NP_SENSOR_FINE_V2_FLAG))==0U &&
        (flags&2048U)!=0U && (flags&128U)==0U && (flags&(64U|1024U))!=(64U|1024U);
    return (flags&~(255U|512U|1024U|NP_SENSOR_FINE_V2_FLAG))==0U &&
        (flags&128U)!=0U && (flags&64U)==0U;
}
static inline uint32_t np_sensor_fine_usb_crc(const uint8_t h[512],const NpSensorFineBinding *b,uint32_t mask)
{
    uint8_t proof[56];uint32_t n=32U;
    np_first_put32(proof,np_first_u32(h+260U));
    np_first_put32(proof+4U,np_first_u32(h+88U));np_first_put32(proof+8U,np_first_u32(h+92U));np_first_put32(proof+12U,np_first_u32(h+504U));
    np_sensor_fine_context(proof+16U,b);
    for(unsigned i=0U;i<3U;i++)if(mask&(1U<<i)){
        np_first_put32(proof+n,np_first_u32(h+276U+i*4U));np_first_put32(proof+n+4U,np_first_u32(h+288U+i*4U));n+=8U;
    }
    return np_first_crc(proof,n);
}
static inline uint32_t np_sensor_fine_sd_crc(const uint8_t h[512],const NpSensorFineBinding *b)
{
    uint8_t proof[140];np_sensor_fine_context(proof,b);memcpy(proof+16U,h+384U,124U);return np_first_crc(proof,sizeof(proof));
}
static inline int np_sensor_fine_v2_parse(const uint8_t *h,uint32_t bytes,
    const NpSensorFineBinding *expected,int allow,int sd,NpSensorFineStamp *out)
{
    const uint32_t offset[2][3]={{88U,92U,504U},{384U,388U,392U}};
    NpSensorFineStamp parsed={0};uint32_t flags,core_at=sd?396U:256U,version;
    if(out)memset(out,0,sizeof(*out));
    if(!h || bytes!=512U)return NP_SENSOR_FINE_INVALID;
    flags=np_first_u32(h+52U);version=np_first_u32(h+core_at+4U);
    if((flags&NP_SENSOR_FINE_V2_FLAG)==0U){
        if((version&255U)==(sd?3U:2U))return NP_SENSOR_FINE_INVALID;
        for(unsigned i=0U;i<3U;i++)if(np_first_u32(h+offset[sd?1:0][i]))return NP_SENSOR_FINE_INVALID;
        return NP_SENSOR_FINE_ABSENT;
    }
    if(!allow || !np_sensor_fine_v2_geometry(flags,sd))return NP_SENSOR_FINE_INVALID;
    if(sd?np_first_zero(h+384U,128U):np_first_u32(h+256U)==0U)return NP_SENSOR_FINE_ABSENT;
    parsed.mask=(version>>8U)&7U;
    if(np_first_u32(h+core_at)!=(sd?UINT32_C(0x32534e53):UINT32_C(0x31534e53)) ||
       version!=((sd?3U:2U)|(parsed.mask<<8U)) || !np_sensor_fine_core(h+core_at,expected,parsed.mask))return NP_SENSOR_FINE_INVALID;
    for(unsigned i=0U;i<3U;i++){
        parsed.counter[i]=np_first_u32(h+offset[sd?1:0][i]);
        if(!(parsed.mask&(1U<<i)) && parsed.counter[i])return NP_SENSOR_FINE_INVALID;
    }
    if(np_first_u32(h+508U)!=(sd?np_sensor_fine_sd_crc(h,expected):np_sensor_fine_usb_crc(h,expected,parsed.mask)))return NP_SENSOR_FINE_INVALID;
    parsed.domain=expected->domain;parsed.generation=expected->generation;if(out)*out=parsed;return NP_SENSOR_FINE_VALID;
}
static inline int np_sensor_fine_usb_parse(const uint8_t *h,uint32_t bytes,const NpSensorFineBinding *b,int allow,NpSensorFineStamp *out)
{return np_sensor_fine_v2_parse(h,bytes,b,allow,0,out);}
static inline int np_sensor_fine_sd_parse(const uint8_t *h,uint32_t bytes,const NpSensorFineBinding *b,int allow,NpSensorFineStamp *out)
{return np_sensor_fine_v2_parse(h,bytes,b,allow,1,out);}
static inline int np_sensor_fine_v2_pack(uint8_t out[512],const uint8_t *source,uint32_t bytes,
    const NpSensorFineStamp *stamp,const NpSensorFineBinding *native,int allow,int sd)
{
    const uint32_t offset[2][3]={{88U,92U,504U},{384U,388U,392U}};
    uint32_t flags,core_at=sd?396U:256U;
    if(!out || !source || bytes!=512U)return NP_SENSOR_FINE_INVALID;
    if((uintptr_t)out<(uintptr_t)source?(uintptr_t)source-(uintptr_t)out<512U:
       (uintptr_t)out-(uintptr_t)source<512U)return NP_SENSOR_FINE_INVALID;
    memcpy(out,source,512U);flags=np_first_u32(source+52U);
    if(!allow || (flags&NP_SENSOR_FINE_V2_FLAG) || !np_sensor_fine_v2_geometry(flags,sd) || !stamp ||
       !np_sensor_fine_core(source+core_at,native,stamp->mask) || stamp->domain!=native->domain || stamp->generation!=native->generation ||
       np_first_u32(source+core_at)!=(sd?UINT32_C(0x32534e53):UINT32_C(0x31534e53)) ||
       np_first_u32(source+core_at+4U)!=(sd?2U:1U) || (!sd && np_first_u32(source+508U)))return NP_SENSOR_FINE_INVALID;
    for(unsigned i=0U;i<3U;i++)if(np_first_u32(source+offset[sd?1:0][i]) || (!(stamp->mask&(1U<<i)) && stamp->counter[i]))return NP_SENSOR_FINE_INVALID;
    np_first_put32(out+52U,flags|NP_SENSOR_FINE_V2_FLAG);np_first_put32(out+core_at+4U,(sd?3U:2U)|(stamp->mask<<8U));
    for(unsigned i=0U;i<3U;i++)np_first_put32(out+offset[sd?1:0][i],stamp->counter[i]);
    np_first_put32(out+508U,sd?np_sensor_fine_sd_crc(out,native):np_sensor_fine_usb_crc(out,native,stamp->mask));return NP_SENSOR_FINE_VALID;
}
static inline int np_sensor_fine_usb_pack(uint8_t out[512],const uint8_t *source,uint32_t bytes,
    const NpSensorFineStamp *stamp,const NpSensorFineBinding *native,int allow)
{return np_sensor_fine_v2_pack(out,source,bytes,stamp,native,allow,0);}
static inline int np_sensor_fine_sd_pack(uint8_t out[512],const uint8_t *source,uint32_t bytes,
    const NpSensorFineStamp *stamp,const NpSensorFineBinding *native,int allow)
{return np_sensor_fine_v2_pack(out,source,bytes,stamp,native,allow,1);}
#endif
