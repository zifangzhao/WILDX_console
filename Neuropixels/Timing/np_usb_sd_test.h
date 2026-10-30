#ifndef NP_USB_SD_TEST_H
#define NP_USB_SD_TEST_H

/* Shared USB SD-test wire codec. Command IDs/capability are proposed. */
#include <stdint.h>
#include <string.h>
#define NP_USB_SD_TEST_SUBMIT_PROPOSED 0xA4U
#define NP_USB_SD_TEST_GET_PROPOSED 0xA3U
#define NP_USB_SD_TEST_CAP_PROPOSED 0x00010000U
#define NP_USB_SD_TEST_PLAN_MAGIC UINT32_C(0x3154514E)
#define NP_USB_SD_TEST_REPORT_MAGIC UINT32_C(0x3152514E)
#define NP_USB_SD_TEST_ABI 1U
#define NP_USB_SD_TEST_PLAN_BYTES 128U
#define NP_USB_SD_TEST_REPORT_BYTES 256U
#define NP_USB_SD_TEST_MAX_SECONDS 120U
#define NP_USB_SD_TEST_VALID_MASK UINT32_C(0xFFFF)
#define NP_USB_SD_TEST_RECONCILE UINT32_C(1)

enum { NP_USB_SD_PHASE_NONE, NP_USB_SD_PHASE_QUEUED, NP_USB_SD_PHASE_ACCEPTED,
 NP_USB_SD_PHASE_RUNNING, NP_USB_SD_PHASE_FINISHED, NP_USB_SD_PHASE_REJECTED,
 NP_USB_SD_PHASE_INTERRUPTED_UNKNOWN };
enum { NP_USB_SD_OUTCOME_PENDING, NP_USB_SD_OUTCOME_CANDIDATE, NP_USB_SD_OUTCOME_THROUGHPUT_LOSS,
 NP_USB_SD_OUTCOME_PROBE_FAULT, NP_USB_SD_OUTCOME_UNSUPPORTED, NP_USB_SD_OUTCOME_IDENTITY,
 NP_USB_SD_OUTCOME_CATALOG, NP_USB_SD_OUTCOME_STORAGE, NP_USB_SD_OUTCOME_CANCELLED,
 NP_USB_SD_OUTCOME_INTERNAL, NP_USB_SD_OUTCOME_INTERRUPTED };
enum { NP_USB_SD_VALID_IDENTITY=1U<<0, NP_USB_SD_VALID_CATALOG=1U<<1,
 NP_USB_SD_VALID_SOURCE_QUIET=1U<<2, NP_USB_SD_VALID_SD_QUIET=1U<<3,
 NP_USB_SD_VALID_TERMINAL=1U<<4, NP_USB_SD_VALID_APPEND=1U<<5,
 NP_USB_SD_VALID_CAPTURE_COUNTERS=1U<<6, NP_USB_SD_VALID_ADMITTED=1U<<7,
 NP_USB_SD_VALID_PERFORMANCE=1U<<8, NP_USB_SD_VALID_AUX=1U<<9,
 NP_USB_SD_VALID_PROFILE_RESTORED=1U<<10, NP_USB_SD_VALID_HANDOFF=1U<<11,
 NP_USB_SD_VALID_SOURCE_GAPS=1U<<12, NP_USB_SD_VALID_MISSING_TICKS=1U<<13,
 NP_USB_SD_VALID_PNH=1U<<14, NP_USB_SD_VALID_REJECTED_UNMUTATED=1U<<15 };

typedef struct { uint64_t ticket; uint32_t uid[3],capacity,cid[4],expected_frontier,seconds,flags;
 uint8_t npsd[64]; } NpUsbSdTestPlan;
/* words 0..62 map directly to the frozen report; word 63 is wire CRC. */
typedef struct { uint32_t words[63]; } NpUsbSdTestReport;

static inline uint32_t np_usb_sd_u32(const uint8_t *p)
{return (uint32_t)p[0]|(uint32_t)p[1]<<8|(uint32_t)p[2]<<16|(uint32_t)p[3]<<24;}
static inline void np_usb_sd_put32(uint8_t *p,uint32_t v)
{p[0]=(uint8_t)v;p[1]=(uint8_t)(v>>8);p[2]=(uint8_t)(v>>16);p[3]=(uint8_t)(v>>24);}
static inline uint64_t np_usb_sd_u64(const uint8_t *p)
{return (uint64_t)np_usb_sd_u32(p)|((uint64_t)np_usb_sd_u32(p+4)<<32);}
static inline void np_usb_sd_put64(uint8_t *p,uint64_t v)
{np_usb_sd_put32(p,(uint32_t)v);np_usb_sd_put32(p+4,(uint32_t)(v>>32));}
static inline uint32_t np_usb_sd_crc_update(uint32_t crc,const uint8_t *data,uint32_t length)
{while(length--){crc^=*data++;for(unsigned b=0;b<8;b++)crc=(crc>>1)^((0U-(crc&1U))&UINT32_C(0xEDB88320));}return crc;}
static inline uint32_t np_usb_sd_crc(const uint8_t *data,uint32_t length)
{return np_usb_sd_crc_update(UINT32_MAX,data,length)^UINT32_MAX;}
static inline uint32_t np_usb_sd_plan_crc(const uint8_t p[128])
{uint32_t c=np_usb_sd_crc_update(UINT32_MAX,p,60U);return np_usb_sd_crc_update(c,p+64U,64U)^UINT32_MAX;}

static inline int np_usb_sd_plan_encode(uint8_t *out,uint32_t length,const NpUsbSdTestPlan *v)
{
 if(!out||!v||length!=128U||v->seconds<1U||v->seconds>120U||(v->flags&~NP_USB_SD_TEST_RECONCILE))return 0;
 memset(out,0,128U);np_usb_sd_put32(out,NP_USB_SD_TEST_PLAN_MAGIC);np_usb_sd_put32(out+4,NP_USB_SD_TEST_ABI);
 np_usb_sd_put64(out+8,v->ticket);for(unsigned i=0;i<3;i++)np_usb_sd_put32(out+16+4*i,v->uid[i]);
 np_usb_sd_put32(out+28,v->capacity);for(unsigned i=0;i<4;i++)np_usb_sd_put32(out+32+4*i,v->cid[i]);
 np_usb_sd_put32(out+48,v->expected_frontier);np_usb_sd_put32(out+52,v->seconds);np_usb_sd_put32(out+56,v->flags);
 memcpy(out+64,v->npsd,64U);np_usb_sd_put32(out+60,np_usb_sd_plan_crc(out));return 1;
}
static inline int np_usb_sd_plan_decode(NpUsbSdTestPlan *v,const uint8_t *in,uint32_t length)
{
 if(!v||!in||length!=128U||np_usb_sd_u32(in)!=NP_USB_SD_TEST_PLAN_MAGIC||
    np_usb_sd_u32(in+4)!=NP_USB_SD_TEST_ABI||(np_usb_sd_u32(in+56)&~NP_USB_SD_TEST_RECONCILE)||
    np_usb_sd_u32(in+52)<1U||np_usb_sd_u32(in+52)>120U||np_usb_sd_u32(in+60)!=np_usb_sd_plan_crc(in))return 0;
 NpUsbSdTestPlan x; x.ticket=np_usb_sd_u64(in+8);
 for(unsigned i=0;i<3;i++)x.uid[i]=np_usb_sd_u32(in+16+4*i);
 x.capacity=np_usb_sd_u32(in+28);for(unsigned i=0;i<4;i++)x.cid[i]=np_usb_sd_u32(in+32+4*i);
 x.expected_frontier=np_usb_sd_u32(in+48);x.seconds=np_usb_sd_u32(in+52);x.flags=np_usb_sd_u32(in+56);memcpy(x.npsd,in+64U,64U);*v=x;return 1;
}
/* Unknown fields start as all-ones; callers set values only when known. */
static inline void np_usb_sd_report_init(NpUsbSdTestReport *r)
{for(unsigned i=0;i<63;i++)r->words[i]=UINT32_MAX;r->words[0]=NP_USB_SD_TEST_REPORT_MAGIC;
 r->words[1]=1U;r->words[2]=256U;r->words[3]=0U;r->words[4]=0U;r->words[5]=0U;r->words[6]=0U;}
static inline int np_usb_sd_unknowns_valid(const NpUsbSdTestReport *r)
{
 uint32_t f=r->words[6];
 if(!(f&NP_USB_SD_VALID_IDENTITY))for(unsigned i=20;i<=24;i++)if(r->words[i]!=UINT32_MAX)return 0;
 if(!(f&NP_USB_SD_VALID_CATALOG) && (r->words[25]!=UINT32_MAX||r->words[26]!=UINT32_MAX))return 0;
 if(!(f&NP_USB_SD_VALID_CAPTURE_COUNTERS) && (r->words[45]!=UINT32_MAX||r->words[46]!=UINT32_MAX||r->words[50]!=UINT32_MAX||r->words[51]!=UINT32_MAX))return 0;
 if(!(f&NP_USB_SD_VALID_ADMITTED) && r->words[38]!=UINT32_MAX)return 0;
 if(!(f&NP_USB_SD_VALID_PERFORMANCE))for(unsigned i=54;i<=60;i++)if(r->words[i]!=UINT32_MAX)return 0;
 if(!(f&NP_USB_SD_VALID_AUX) && (r->words[61]!=UINT32_MAX||r->words[62]!=UINT32_MAX))return 0;
 if(!(f&NP_USB_SD_VALID_SOURCE_GAPS) && r->words[47]!=UINT32_MAX)return 0;
 if(!(f&NP_USB_SD_VALID_MISSING_TICKS) && (r->words[48]!=UINT32_MAX||r->words[49]!=UINT32_MAX))return 0;
 if(!(f&NP_USB_SD_VALID_PNH) && (r->words[37]!=UINT32_MAX||r->words[39]!=UINT32_MAX||r->words[40]!=UINT32_MAX))return 0;
 return 1;
}
static inline int np_usb_sd_report_encode(uint8_t *out,uint32_t length,const NpUsbSdTestReport *r)
{
 if(!out||!r||length!=256U||r->words[0]!=NP_USB_SD_TEST_REPORT_MAGIC||r->words[1]!=1U||r->words[2]!=256U||
    r->words[4]>6U||r->words[5]>10U||(r->words[6]&~NP_USB_SD_TEST_VALID_MASK)||!np_usb_sd_unknowns_valid(r))return 0;
 for(unsigned i=0;i<63;i++)np_usb_sd_put32(out+4*i,r->words[i]);
 np_usb_sd_put32(out+252,np_usb_sd_crc(out,252U));return 1;
}
static inline int np_usb_sd_report_decode(NpUsbSdTestReport *out,const uint8_t *in,uint32_t length)
{
 if(!out||!in||length!=256U||np_usb_sd_u32(in+252)!=np_usb_sd_crc(in,252U))return 0;
 NpUsbSdTestReport r;for(unsigned i=0;i<63;i++)r.words[i]=np_usb_sd_u32(in+4*i);
 if(r.words[0]!=NP_USB_SD_TEST_REPORT_MAGIC||r.words[1]!=1U||r.words[2]!=256U||r.words[4]>6U||r.words[5]>10U||
    (r.words[6]&~NP_USB_SD_TEST_VALID_MASK)||!np_usb_sd_unknowns_valid(&r))return 0;
 *out=r;return 1;
}
#endif
