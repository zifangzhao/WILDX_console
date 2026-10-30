#ifndef NP_RTC_START_WIRE_H
#define NP_RTC_START_WIRE_H
#include "np_fine_clock_wire.h"
#include "np_first_sync_usb.h"
#include <limits.h>

/* Pure optional codecs; callers own actual RTC reads, freezing and admission. */
#define NP_RTC_START_BYTES 128U
#define NP_RTC_START_REQUIRED 59U
#define NP_RTC_START_PC_CALIBRATED 4U
#define NP_RTC_START_USB_FEATURES 15U
#define NP_RTC_START_PAGE UINT32_C(0x47505452) /* RTPG */
#define NP_RTC_START_MIN_US UINT64_C(946684800000000)
#define NP_RTC_START_END_US UINT64_C(4102444800000000)
enum { NP_RTC_START_OK=0U,NP_RTC_START_INVALID=1U,NP_RTC_START_FLAGS=2U,
       NP_RTC_START_CONFIG=3U,NP_RTC_START_CALENDAR=4U,NP_RTC_START_COUNTER=5U,
       NP_RTC_START_OVERFLOW=6U };
typedef struct {
    const uint8_t *receipt,*anchor; /* immutable caller-owned buffers */
    uint32_t uid[3],family,session,continuous64;
    uint64_t token;
} NpRtcStartBinding;
typedef struct {
    uint32_t flags,uid[3],family,session,generation,source_epoch,domain,fine_hz,cal_generation;
    uint64_t token,utc_lower_us,utc_upper_us,before64,after64;
    uint32_t psca,pscs,input_hz,ssr,pc_uncertainty_us,receipt_crc,calr;
} NpRtcStartStamp;
typedef struct {int64_t lower_us,upper_us;uint32_t reason,pc_uncertainty_us;} NpRtcStartBounds;
static inline int np_rtc_start_binding_valid(const NpRtcStartBinding *b)
{
    return b && b->continuous64==1U && np_first_usb_uid(b->uid) && b->token &&
        (b->family==1U || b->family==2U) &&
        np_first_sync_source_anchor_valid(b->anchor,b->receipt) &&
        (np_first_u32(b->receipt+12)>>8)==b->family &&
        (b->session?np_first_u32(b->receipt+16)==b->session:
                    np_first_u32(b->receipt+16)==np_first_u32(b->receipt+20));
}
static inline int np_rtc_start_binding_sd(NpRtcStartBinding *out,const uint8_t clock[512],
    const uint8_t companion[512],uint32_t family,uint32_t session,uint32_t ap_hz,
    const uint32_t uid[3],uint64_t token)
{
    NpFineSdCompanion parsed;NpRtcStartBinding b;
    if(!out || !session || !np_first_usb_uid(uid) || !np_fine_sd_companion_fine_counter64_read(companion,512U,clock,
        family,session,ap_hz,uid,token,&parsed))return 0;
    memset(&b,0,sizeof(b));b.receipt=clock+160U;b.anchor=companion+72U;
    memcpy(b.uid,parsed.bound.uid,12U);b.family=family;b.session=session;b.token=token;b.continuous64=1U;
    if(!np_rtc_start_binding_valid(&b))return 0;*out=b;return 1;
}
static inline int np_rtc_start_binding_usb(NpRtcStartBinding *out,const uint8_t receipt[328],
    const uint8_t anchor[64],uint32_t family,const uint32_t uid[3],uint64_t token,uint32_t features)
{
    NpRtcStartBinding b;if(!out || features!=NP_RTC_START_USB_FEATURES || !np_first_usb_uid(uid))return 0;
    memset(&b,0,sizeof(b));b.receipt=receipt;b.anchor=anchor;memcpy(b.uid,uid,12U);
    b.family=family;b.token=token;b.continuous64=1U;
    if(!np_rtc_start_binding_valid(&b))return 0;*out=b;return 1;
}
static inline int np_rtc_start_read(const uint8_t *raw,size_t bytes,const NpRtcStartBinding *b,NpRtcStartStamp *out)
{
    NpRtcStartStamp s;
    if(!raw || bytes!=128U || !np_rtc_start_binding_valid(b) || memcmp(raw,"NPRTC001",8U) ||
       np_first_u16(raw+8)!=1U || np_first_u16(raw+10)!=128U ||
       np_first_u32(raw+124)!=np_first_crc(raw,124U) || (np_first_u32(raw+12)&~63U) ||
       !np_first_usb_same_uid(raw+16,b->uid) || np_first_u32(raw+28)!=b->family ||
       np_first_u32(raw+32)!=b->session || np_first_u32(raw+36)!=np_first_u32(b->receipt+20) ||
       np_first_u32(raw+40)!=np_first_u32(b->receipt+24) || np_first_u32(raw+44)!=np_first_u32(b->receipt+28) ||
       np_first_u32(raw+48)!=np_first_u32(b->receipt+40) || np_first_u64(raw+56)!=b->token ||
       np_first_u32(raw+116)!=np_first_u32(b->receipt+324))return 0;
    memset(&s,0,sizeof(s));s.flags=np_first_u32(raw+12);memcpy(s.uid,b->uid,12U);
    s.family=b->family;s.session=b->session;s.generation=np_first_u32(raw+36);s.source_epoch=np_first_u32(raw+40);
    s.domain=np_first_u32(raw+44);s.fine_hz=np_first_u32(raw+48);s.cal_generation=np_first_u32(raw+52);
    s.token=np_first_u64(raw+56);s.utc_lower_us=np_first_u64(raw+64);s.utc_upper_us=np_first_u64(raw+72);
    s.before64=np_first_u64(raw+80);s.after64=np_first_u64(raw+88);s.psca=np_first_u32(raw+96);
    s.pscs=np_first_u32(raw+100);s.input_hz=np_first_u32(raw+104);s.ssr=np_first_u32(raw+108);
    s.pc_uncertainty_us=np_first_u32(raw+112);s.receipt_crc=np_first_u32(raw+116);s.calr=np_first_u32(raw+120);
    if((s.flags&NP_RTC_START_PC_CALIBRATED)?!s.cal_generation:(s.cal_generation || s.pc_uncertainty_us))return 0;
    if(out)*out=s;return 1;
}
static inline int np_rtc_start_pack(uint8_t out[128],const NpRtcStartStamp *s,const NpRtcStartBinding *b)
{
    if(!out || !s || !np_rtc_start_binding_valid(b) || np_fine_overlap(out,128U,s,sizeof(*s)) ||
       np_fine_overlap(out,128U,b,sizeof(*b)) || np_fine_overlap(out,128U,b->receipt,328U) || np_fine_overlap(out,128U,b->anchor,64U))return 0;
    memset(out,0,128U);memcpy(out,"NPRTC001",8U);np_first_put16(out+8,1U);np_first_put16(out+10,128U);
    np_first_put32(out+12,s->flags);np_first_usb_put_uid(out+16,b->uid);np_first_put32(out+28,b->family);
    np_first_put32(out+32,b->session);np_first_put32(out+36,np_first_u32(b->receipt+20));
    np_first_put32(out+40,np_first_u32(b->receipt+24));np_first_put32(out+44,np_first_u32(b->receipt+28));
    np_first_put32(out+48,np_first_u32(b->receipt+40));np_first_put32(out+52,s->cal_generation);np_first_put64(out+56,b->token);
    np_first_put64(out+64,s->utc_lower_us);np_first_put64(out+72,s->utc_upper_us);
    np_first_put64(out+80,s->before64);np_first_put64(out+88,s->after64);
    np_first_put32(out+96,s->psca);np_first_put32(out+100,s->pscs);np_first_put32(out+104,s->input_hz);
    np_first_put32(out+108,s->ssr);np_first_put32(out+112,s->pc_uncertainty_us);
    np_first_put32(out+116,np_first_u32(b->receipt+324));np_first_put32(out+120,s->calr);
    np_first_put32(out+124,np_first_crc(out,124U));
    if(np_rtc_start_read(out,128U,b,NULL))return 1;memset(out,0,128U);return 0;
}
static inline int np_rtc_start_add(int64_t a,int64_t b,int64_t *out)
{if((b>0 && a>INT64_MAX-b) || (b<0 && a<INT64_MIN-b))return 0;*out=a+b;return 1;}
/* Exact floor/ceil of delta*1e6/F + source_ticks*1e6/S, no floating epochs. */
static inline int np_rtc_start_offset(int64_t delta,uint64_t source_ticks,uint32_t f,uint32_t h,int up,int64_t *out)
{
    int64_t q=delta/(int64_t)f,rem=delta%(int64_t)f,base,v;
    uint64_t sq=source_ticks/h,sr=source_ticks%h,an,bn,ar,br,n,d;
    if(rem<0){q--;rem+=(int64_t)f;}
    if(q>INT64_MAX/1000000 || q<INT64_MIN/1000000 || sq>(uint64_t)INT64_MAX/1000000U)return 0;
    if(!np_rtc_start_add(q*1000000,(int64_t)(sq*1000000U),&base))return 0;
    an=(uint64_t)rem*1000000U;bn=sr*1000000U;ar=an%f;br=bn%h;
    n=ar*h+br*f;d=(uint64_t)f*h; /* each product <=1e18; sum fits uint64 */
    v=(int64_t)(an/f+bn/h+n/d+(up && n%d?1U:0U));return np_rtc_start_add(base,v,out);
}
static inline int np_rtc_start_map(const uint8_t raw[128],const NpRtcStartBinding *b,NpRtcStartBounds *out)
{
    NpRtcStartStamp s;uint64_t ticks,origin,ref;uint32_t c,w;int64_t low,high;
    if(!out)return 0;memset(out,0,sizeof(*out));out->reason=NP_RTC_START_INVALID;
    if(!np_rtc_start_read(raw,128U,b,&s))return 0;out->pc_uncertainty_us=s.pc_uncertainty_us;
    out->reason=NP_RTC_START_FLAGS;if((s.flags&NP_RTC_START_REQUIRED)!=NP_RTC_START_REQUIRED)return 0;
    out->reason=NP_RTC_START_CONFIG;if(s.psca!=74U || s.pscs!=9999U || s.input_hz!=750000U || s.calr)return 0;
    out->reason=NP_RTC_START_CALENDAR;
    if(s.ssr>9999U || s.utc_lower_us<NP_RTC_START_MIN_US || s.utc_upper_us>NP_RTC_START_END_US ||
       s.utc_lower_us>NP_RTC_START_END_US-100U || s.utc_upper_us!=s.utc_lower_us+100U ||
       s.utc_lower_us%1000000U!=(uint64_t)(9999U-s.ssr)*100U)return 0;
    out->reason=NP_RTC_START_COUNTER;
    if(s.before64>s.after64 || s.after64>=(uint64_t)INT64_MAX)return 0;
    c=np_first_u32(b->anchor+28);w=np_first_u32(b->anchor+32)-c;
    origin=np_first_u64(b->anchor+44);ref=np_first_u64(b->receipt+52);ticks=origin+ref; /* strict root checked sum */
    out->reason=NP_RTC_START_OVERFLOW;
    if(!np_rtc_start_offset((int64_t)c-(int64_t)(s.after64+1U),ticks,s.fine_hz,np_first_u32(b->receipt+32),0,&low) ||
       !np_rtc_start_offset((int64_t)c+w+1-(int64_t)s.before64,ticks,s.fine_hz,np_first_u32(b->receipt+32),1,&high) ||
       !np_rtc_start_add((int64_t)s.utc_lower_us,low,&out->lower_us) ||
       !np_rtc_start_add((int64_t)s.utc_upper_us,high,&out->upper_us) ||
       out->lower_us<(int64_t)NP_RTC_START_MIN_US || out->upper_us>(int64_t)NP_RTC_START_END_US || out->lower_us>out->upper_us){
        out->lower_us=out->upper_us=0;return 0;
    }
    out->reason=NP_RTC_START_OK;return 1;
}
static inline int np_rtc_start_sector_read(const uint8_t *sector,size_t bytes,const NpRtcStartBinding *b,NpRtcStartStamp *out)
{
    return sector && bytes==512U && np_first_zero(sector+128,380U) &&
        np_first_u32(sector+508)==np_first_crc(sector,508U) && np_rtc_start_read(sector,128U,b,out);
}
static inline int np_rtc_start_sector_pack(uint8_t out[512],const uint8_t raw[128],const NpRtcStartBinding *b)
{
    if(!out || !raw || np_fine_overlap(out,512U,raw,128U) || !np_rtc_start_read(raw,128U,b,NULL) ||
       np_fine_overlap(out,512U,b,sizeof(*b)) || np_fine_overlap(out,512U,b->receipt,328U) || np_fine_overlap(out,512U,b->anchor,64U))return 0;
    memset(out,0,512U);memcpy(out,raw,128U);np_first_put32(out+508,np_first_crc(out,508U));return 1;
}
static inline int np_rtc_start_page_body_valid(const uint8_t *p)
{
    return p && np_first_u32(p)==NP_RTC_START_PAGE && p[4]==1U && p[5]<3U && p[6]==3U && !p[7] &&
        np_first_u32(p+60)==np_first_crc(p,60U) &&
        (p[5]!=2U || (np_first_zero(p+44,16U) && np_first_u32(p+40)==np_first_u32(p+8)));
}
static inline int np_rtc_start_page_read(const uint8_t *page,size_t bytes,const NpRtcStartBinding *b,uint64_t sequence)
{
    return page && bytes==108U && np_rtc_start_binding_valid(b) && !b->session && sequence!=UINT64_MAX &&
        np_first_u32(page)==NP_FIRST_USB_ENVELOPE && np_first_u16(page+4)==1U && np_first_u16(page+6)==6U &&
        np_first_u64(page+8)==b->token && np_first_u32(page+16)==np_first_u32(b->receipt+20) &&
        np_first_usb_same_uid(page+20,b->uid) && np_first_u64(page+32)==sequence &&
        np_first_u32(page+104)==np_first_crc(page,104U) && np_rtc_start_page_body_valid(page+40);
}
static inline int np_rtc_start_page_pack(uint8_t out[108],const uint8_t raw[128],const NpRtcStartBinding *b,uint32_t index,uint64_t sequence)
{
    uint8_t *p;if(!out || !raw || index>2U || sequence==UINT64_MAX || !np_rtc_start_binding_valid(b) || b->session ||
       np_fine_overlap(out,108U,raw,128U) || !np_rtc_start_read(raw,128U,b,NULL) ||
       np_fine_overlap(out,108U,b,sizeof(*b)) || np_fine_overlap(out,108U,b->receipt,328U) || np_fine_overlap(out,108U,b->anchor,64U))return 0;
    memset(out,0,108U);np_first_put32(out,NP_FIRST_USB_ENVELOPE);np_first_put16(out+4,1U);np_first_put16(out+6,6U);
    np_first_put64(out+8,b->token);np_first_put32(out+16,np_first_u32(b->receipt+20));np_first_usb_put_uid(out+20,b->uid);
    np_first_put64(out+32,sequence);p=out+40U;np_first_put32(p,NP_RTC_START_PAGE);p[4]=1U;p[5]=(uint8_t)index;p[6]=3U;
    np_first_put32(p+8,np_first_u32(raw+124));memcpy(p+12,raw+index*48U,index==2U?32U:48U);
    np_first_put32(p+60,np_first_crc(p,60U));np_first_put32(out+104,np_first_crc(out,104U));return 1;
}
#endif
