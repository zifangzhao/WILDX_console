#ifndef NP_CL_CONTROL_WIRE_H
#define NP_CL_CONTROL_WIRE_H
#include "np_cl_event_wire.h"

/* Transport-independent 64-byte mailbox. This does not allocate opcodes,
 * change a probe, start capture, or attest persistence/physical timing. */
#define NP_CL_CONTROL_CAPS 0U
#define NP_CL_CONTROL_CONFIGURE 1U
#define NP_CL_CONTROL_STATUS 2U
#define NP_CL_CONTROL_SUPPORTED 1U
#define NP_CL_CONTROL_CONFIGURED 2U
#define NP_CL_CONTROL_RUNNING 4U
#define NP_CL_CONTROL_ORIGIN_VALID 8U
#define NP_CL_CONTROL_SNAPSHOT_STAGED 16U
enum { NP_CL_CONTROL_OK=0, NP_CL_CONTROL_UNSUPPORTED=1, NP_CL_CONTROL_BUSY=2,
       NP_CL_CONTROL_INVALID=3, NP_CL_CONTROL_MEMORY=4, NP_CL_CONTROL_SOURCE=5,
       NP_CL_CONTROL_FAULT=6 };

typedef struct {
    uint32_t op,id,config_crc,ap_hz,training_start_ms,training_duration_ms;
    uint32_t count,selector[2],rectify_mask;
    float gain[2];
} NpClControlRequest;
typedef struct {
    uint32_t op,id,result,flags,config_crc,session,ap_hz,source_epoch;
    uint32_t max_lanes,probe_mask,filter_mask,channel_groups,drops;
} NpClControlReply;

static inline uint32_t np_cl_control_u32(const uint8_t *p)
{ return (uint32_t)p[0]|((uint32_t)p[1]<<8U)|((uint32_t)p[2]<<16U)|((uint32_t)p[3]<<24U); }
static inline int np_cl_control_zero(const uint8_t *p,uint32_t n)
{ uint32_t i;for(i=0;i<n;i++)if(p[i])return 0;return 1; }

/* Byte/CRC/range admission only; caller must separately attest active profile,
 * stopped ownership, phase/origin, coefficient rate and workspace availability.
 * Zero lanes disables only this optional feature. A bad request clears out. */
static inline int np_cl_control_parse(const uint8_t *b,uint32_t bytes,NpClControlRequest *out)
{
    uint32_t i,v; NpClControlRequest q;
    if(!out)return 0;memset(out,0,sizeof(*out));
    if(!b||bytes!=64U||memcmp(b,"NPCC",4U)||np_cl_control_u32(b+4)!=1U||
       np_cl_control_u32(b+8)>2U||!np_cl_control_u32(b+12)||
       np_cl_control_u32(b+60)!=np_cl_wire_crc(b,60U))return 0;
    memset(&q,0,sizeof(q));q.op=np_cl_control_u32(b+8);q.id=np_cl_control_u32(b+12);q.config_crc=np_cl_control_u32(b+60);
    if(q.op!=NP_CL_CONTROL_CONFIGURE){if(!np_cl_control_zero(b+16,44U))return 0;*out=q;return 1;}
    q.count=np_cl_control_u32(b+32);
    if(q.count==0U){if(!np_cl_control_zero(b+16,44U))return 0;*out=q;return 1;}
    q.ap_hz=np_cl_control_u32(b+20);q.training_start_ms=np_cl_control_u32(b+24);q.training_duration_ms=np_cl_control_u32(b+28);q.rectify_mask=np_cl_control_u32(b+52);
    if(q.count>2U||!q.training_duration_ms||np_cl_control_u32(b+16)||np_cl_control_u32(b+56)||
       !(q.ap_hz==7500U||q.ap_hz==15000U||q.ap_hz==20000U||q.ap_hz==30000U)||
       (q.rectify_mask&~((1U<<q.count)-1U)))return 0;
    for(i=0;i<2U;i++){
        q.selector[i]=np_cl_control_u32(b+36U+i*8U);v=np_cl_control_u32(b+40U+i*8U);memcpy(&q.gain[i],&v,4U);
        if(i>=q.count){if(q.selector[i]||v)return 0;continue;}
        if((q.selector[i]&255U)<1U||(q.selector[i]&255U)>3U||((q.selector[i]>>8U)&255U)>9U||
           (q.selector[i]>>16U)>=384U||(v&0x7f800000U)==0x7f800000U||q.gain[i]<=0.0f)return 0;
    }
    *out=q;return 1;
}

static inline int np_cl_control_reply_pack(uint8_t *b,const NpClControlReply *r)
{
    if(!b||!r||r->op>2U||!r->id||r->result>6U||(r->flags&~31U)||r->max_lanes>2U||
       (r->probe_mask&~7U)||(r->filter_mask&~1023U)||(r->channel_groups&~511U)||
       ((r->flags&NP_CL_CONTROL_ORIGIN_VALID)&&!r->source_epoch)||
       ((r->flags&NP_CL_CONTROL_CONFIGURED)&&!(r->flags&NP_CL_CONTROL_SUPPORTED))||
       ((r->flags&NP_CL_CONTROL_RUNNING)&&!(r->flags&NP_CL_CONTROL_CONFIGURED))||
       ((r->flags&NP_CL_CONTROL_SNAPSHOT_STAGED)&&!(r->flags&NP_CL_CONTROL_CONFIGURED)))return 0;
    memset(b,0,64U);memcpy(b,"NPCR",4U);np_cl_wire_u32(b+4,1U);
    np_cl_wire_u32(b+8,r->op);np_cl_wire_u32(b+12,r->id);np_cl_wire_u32(b+16,r->result);np_cl_wire_u32(b+20,r->flags);
    np_cl_wire_u32(b+24,r->config_crc);np_cl_wire_u32(b+28,r->session);np_cl_wire_u32(b+32,r->ap_hz);np_cl_wire_u32(b+36,r->source_epoch);
    np_cl_wire_u32(b+40,r->max_lanes);np_cl_wire_u32(b+44,r->probe_mask);np_cl_wire_u32(b+48,r->filter_mask);np_cl_wire_u32(b+52,r->channel_groups);
    np_cl_wire_u32(b+56,r->drops);np_cl_wire_u32(b+60,np_cl_wire_crc(b,60U));return 1;
}
#endif
