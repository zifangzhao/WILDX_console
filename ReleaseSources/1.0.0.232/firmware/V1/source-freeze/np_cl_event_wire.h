#ifndef NP_CL_EVENT_WIRE_H
#define NP_CL_EVENT_WIRE_H

#include <stdint.h>
#include <string.h>
#include "np_cl_kernel.h"

/* Event-only interchange, not a USB/BLE command allocation or SD writer.
 * Config/rational clock metadata is bound separately by session + config CRC.
 * source_tick is signal time; input_end_tick is the latest consumed source
 * boundary. Neither is a measurement of physical output/actuation latency. */
#define NP_CL_EVENT_BYTES 64U
#define NP_CL_EVENT_TIME_VALID 1U

static inline void np_cl_wire_u32(uint8_t *p, uint32_t v)
{ unsigned i; for(i=0;i<4U;i++)p[i]=(uint8_t)(v>>(8U*i)); }
static inline void np_cl_wire_u64(uint8_t *p, uint64_t v)
{ np_cl_wire_u32(p,(uint32_t)v);np_cl_wire_u32(p+4,(uint32_t)(v>>32)); }
static inline uint32_t np_cl_wire_float_bits(float v)
{ uint32_t bits; memcpy(&bits,&v,4U); return bits; }
static inline uint32_t np_cl_wire_crc(const uint8_t *p, uint32_t n)
{
    uint32_t c=~0U,i,j;
    for(i=0;i<n;i++){c^=p[i];for(j=0;j<8U;j++)c=(c>>1)^((c&1U)?0xedb88320U:0U);}
    return ~c;
}

/* Caller owns sequencing, queue/overflow accounting, and confirmed config.
 * A failed pack publishes no bytes. A GAP is never represented as a FALL. */
static inline int np_cl_event_pack(uint8_t *out, const NpClEvent *e,
                           uint64_t input_end_tick, uint32_t config_crc,
                           uint32_t sequence, uint32_t source_epoch, uint32_t drops,
                           uint32_t reason, uint32_t flags)
{
    uint32_t f[2],i;
    if(!out||!e||!sequence||!source_epoch||!e->session||e->probe<1U||e->probe>3U||
       e->channel>=384U||e->lane>=2U||e->kind<1U||e->kind>5U||
       (flags&~NP_CL_EVENT_TIME_VALID)||((flags&NP_CL_EVENT_TIME_VALID)&&input_end_tick<e->source_tick)||
       (e->kind!=NP_CL_EVENT_GAP && (!(flags&NP_CL_EVENT_TIME_VALID)||reason))||
       (e->kind==NP_CL_EVENT_GAP && (reason<1U||reason>7U)))return 0;
    f[0]=np_cl_wire_float_bits(e->envelope);f[1]=np_cl_wire_float_bits(e->threshold);
    for(i=0;i<2U;i++)if((f[i]&0x7f800000U)==0x7f800000U)return 0;
    memset(out,0,NP_CL_EVENT_BYTES);
    memcpy(out,"NPCL",4U);out[4]=1U;out[5]=(uint8_t)e->kind;out[6]=(uint8_t)e->lane;out[7]=(uint8_t)flags;
    np_cl_wire_u32(out+8,e->session);np_cl_wire_u32(out+12,sequence);np_cl_wire_u32(out+16,config_crc);
    out[20]=(uint8_t)e->probe;out[22]=(uint8_t)e->channel;out[23]=(uint8_t)(e->channel>>8);
    np_cl_wire_u64(out+24,(flags&NP_CL_EVENT_TIME_VALID)?e->source_tick:0U);
    np_cl_wire_u64(out+32,(flags&NP_CL_EVENT_TIME_VALID)?input_end_tick:0U);
    np_cl_wire_u32(out+40,source_epoch);
    for(i=0;i<2U;i++)np_cl_wire_u32(out+44U+4U*i,f[i]);
    np_cl_wire_u32(out+52,drops);np_cl_wire_u32(out+56,reason);
    np_cl_wire_u32(out+60,np_cl_wire_crc(out,60U));return 1;
}
#endif
