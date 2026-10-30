#ifndef NP_FIRST_SYNC_USB_H
#define NP_FIRST_SYNC_USB_H
#include "np_first_sync_wire.h"

/* Pure optional USB codec; no EP0, capture, queues or active renegotiation.
 * Vendor80 C0IN64 value0/indexFFFF selects v2 INFO. Legacy value/index0 and
 * all R2 version1 bytes stay unchanged in their existing implementation. */
#define NP_FIRST_USB_INFO_INDEX UINT16_C(0xffff)
#define NP_FIRST_USB_FEATURE UINT32_C(1)
#define NP_FIRST_USB_FINE_AUX UINT32_C(2)
#define NP_FIRST_USB_FINE_COUNTER64 UINT32_C(4)
#define NP_FIRST_USB_RTC_START UINT32_C(8)
#define NP_FIRST_USB_INFO UINT32_C(0x4954504e)
#define NP_FIRST_USB_NEGOTIATE UINT32_C(0x4e54504e)
#define NP_FIRST_USB_ENVELOPE UINT32_C(0x4254504e)
#define NP_FIRST_USB_PAGE UINT32_C(0x4750534e) /* NSPG */
#define NP_FIRST_USB_PAGE_COUNT 7U
#define NP_FIRST_USB_SLICE 48U
#define NP_FIRST_USB_ENVELOPE_BYTES 108U
typedef struct {uint32_t flags,uid[3],generation,caps,features,family;uint64_t token;} NpFirstUsbInfo;
static inline int np_first_usb_uid(const uint32_t uid[3])
{return uid && (uid[0]|uid[1]|uid[2]) && (uid[0]&uid[1]&uid[2])!=UINT32_MAX;}
static inline int np_first_usb_same_uid(const uint8_t *p,const uint32_t uid[3])
{return np_first_usb_uid(uid) && np_first_u32(p)==uid[0] && np_first_u32(p+4)==uid[1] && np_first_u32(p+8)==uid[2];}
static inline void np_first_usb_put_uid(uint8_t *p,const uint32_t uid[3])
{unsigned i;for(i=0U;i<3U;i++)np_first_put32(p+4U*i,uid[i]);}
/* last_token survives disable; the host never reuses any lifetime token.
 * Caller supplies stopped/prepared admission and commits only on success. */
static inline int np_first_usb_negotiate_features(const uint8_t *b,size_t bytes,
    const uint32_t uid[3],uint64_t last_token,uint64_t *token,uint32_t *enable,uint32_t features)
{
    uint32_t e;uint64_t t;if(!b || bytes!=64U || !token || !enable ||
       np_first_u32(b)!=NP_FIRST_USB_NEGOTIATE || np_first_u16(b+4)!=2U ||
       np_first_u32(b+60)!=np_first_crc(b,60U) || !np_first_usb_same_uid(b+16,uid) ||
       !np_first_zero(b+32,28U))return 0;
    e=np_first_u16(b+6);t=np_first_u64(b+8);
    if((features!=1U && features!=3U && features!=7U && features!=15U) || e>1U || (e?(!t || t==last_token || np_first_u32(b+28)!=features):
       (t!=0U || np_first_u32(b+28)!=0U)))return 0;
    *token=t;*enable=e;return 1;
}
/* Old entry points remain exact feature1; a header copy never opts a port in. */
static inline int np_first_usb_negotiate(const uint8_t *b,size_t bytes,const uint32_t uid[3],uint64_t last_token,uint64_t *token,uint32_t *enable)
{return np_first_usb_negotiate_features(b,bytes,uid,last_token,token,enable,1U);}
static inline int np_first_usb_fine_aux_negotiate(const uint8_t *b,size_t bytes,const uint32_t uid[3],uint64_t last_token,uint64_t *token,uint32_t *enable)
{return np_first_usb_negotiate_features(b,bytes,uid,last_token,token,enable,3U);}
/* Explicit continuity selection attests actual BEFORE counter64 high0 and
 * qualified continuous64 production; a CRC/low32 NPAR alone cannot prove it. */
static inline int np_first_usb_fine_counter64_negotiate(const uint8_t *b,size_t bytes,const uint32_t uid[3],uint64_t last_token,uint64_t *token,uint32_t *enable)
{return np_first_usb_negotiate_features(b,bytes,uid,last_token,token,enable,7U);}
/* Explicit optional RTC crossstamp, never selected by the old exact7 APIs. */
static inline int np_first_usb_rtc_start_negotiate(const uint8_t *b,size_t bytes,const uint32_t uid[3],uint64_t last_token,uint64_t *token,uint32_t *enable)
{return np_first_usb_negotiate_features(b,bytes,uid,last_token,token,enable,15U);}
static inline int np_first_usb_info_valid_features(const uint8_t *b,size_t bytes,
                                         const uint32_t uid[3],uint32_t family,uint32_t selected_features)
{
    uint32_t f,c,features;if(!b || bytes!=64U || (family!=1U && family!=2U) ||
       np_first_u32(b)!=NP_FIRST_USB_INFO || np_first_u16(b+4)!=2U ||
       np_first_u32(b+60)!=np_first_crc(b,60U) || !np_first_usb_same_uid(b+16,uid))return 0;
    f=np_first_u16(b+6);c=np_first_u32(b+36);features=np_first_u32(b+56);
    return (selected_features==1U || selected_features==3U || selected_features==7U || selected_features==15U) && !(f&~7U) && (f&1U) && !(c&~7U) && features==selected_features &&
        np_first_u32(b+28)==1000U && np_first_u32(b+40)==NP_FIRST_USB_ENVELOPE_BYTES &&
        np_first_u32(b+44)==1U && np_first_u32(b+48)==(family==1U?192U:396U) &&
        np_first_u32(b+52)==1U && ((f&2U)!=0U)==(np_first_u64(b+8)!=0U) &&
        (!(f&2U) || ((f&7U)==7U && (c&3U)==3U && (features&NP_FIRST_USB_FEATURE)));
}
static inline int np_first_usb_info_valid(const uint8_t *b,size_t bytes,const uint32_t uid[3],uint32_t family)
{return np_first_usb_info_valid_features(b,bytes,uid,family,1U);}
static inline int np_first_usb_fine_aux_info_valid(const uint8_t *b,size_t bytes,const uint32_t uid[3],uint32_t family)
{return np_first_usb_info_valid_features(b,bytes,uid,family,3U);}
static inline int np_first_usb_fine_counter64_info_valid(const uint8_t *b,size_t bytes,const uint32_t uid[3],uint32_t family)
{return np_first_usb_info_valid_features(b,bytes,uid,family,7U);}
static inline int np_first_usb_rtc_start_info_valid(const uint8_t *b,size_t bytes,const uint32_t uid[3],uint32_t family)
{return np_first_usb_info_valid_features(b,bytes,uid,family,15U);}
/* ENABLED/token report v2 opt-in only, never a legacy R2 token. RAW_D8 is
 * incompatible unless its owner separately proves the negotiated v2 layout.
 * Features advertises support while disabled and selection while enabled. */
static inline int np_first_usb_info_pack_features(uint8_t out[64],const NpFirstUsbInfo *s,uint32_t features)
{
    if(!out || !s)return 0;memset(out,0,64U);np_first_put32(out,NP_FIRST_USB_INFO);
    np_first_put16(out+4,2U);np_first_put16(out+6,(uint16_t)s->flags);np_first_put64(out+8,s->token);
    np_first_usb_put_uid(out+16,s->uid);np_first_put32(out+28,1000U);np_first_put32(out+32,s->generation);
    np_first_put32(out+36,s->caps);np_first_put32(out+40,NP_FIRST_USB_ENVELOPE_BYTES);np_first_put32(out+44,1U);
    np_first_put32(out+48,s->family==1U?192U:396U);np_first_put32(out+52,1U);np_first_put32(out+56,s->features);
    np_first_put32(out+60,np_first_crc(out,60U));
    if(s->flags<=7U && np_first_usb_info_valid_features(out,64U,s->uid,s->family,features))return 1;
    memset(out,0,64U);return 0;
}
static inline int np_first_usb_info_pack(uint8_t out[64],const NpFirstUsbInfo *s)
{return np_first_usb_info_pack_features(out,s,1U);}
static inline int np_first_usb_fine_aux_info_pack(uint8_t out[64],const NpFirstUsbInfo *s)
{return np_first_usb_info_pack_features(out,s,3U);}
static inline int np_first_usb_fine_counter64_info_pack(uint8_t out[64],const NpFirstUsbInfo *s)
{return np_first_usb_info_pack_features(out,s,7U);}
static inline int np_first_usb_rtc_start_info_pack(uint8_t out[64],const NpFirstUsbInfo *s)
{return np_first_usb_info_pack_features(out,s,15U);}
static inline int np_first_usb_body_valid(const uint8_t *b,size_t bytes)
{
    if(!b || bytes!=64U || np_first_u32(b)!=NP_FIRST_USB_PAGE || b[4]!=1U ||
       b[5]>=NP_FIRST_USB_PAGE_COUNT || b[6]!=NP_FIRST_USB_PAGE_COUNT || b[7] ||
       np_first_u32(b+60)!=np_first_crc(b,60U))return 0;
    /* Last slice contains receipt288..327 then eight zero padding bytes.
     * Its embedded final receipt CRC must equal the page-set hash. */
    return b[5]!=6U || (np_first_zero(b+52,8U) && np_first_u32(b+48)==np_first_u32(b+8));
}
static inline int np_first_usb_page_valid(const uint8_t *b,size_t bytes,
    const uint32_t uid[3],uint64_t token,uint32_t generation,uint64_t containing_sequence)
{
    return b && bytes==NP_FIRST_USB_ENVELOPE_BYTES && token && generation && containing_sequence!=UINT64_MAX &&
        np_first_u32(b)==NP_FIRST_USB_ENVELOPE && np_first_u16(b+4)==1U && np_first_u16(b+6)==4U &&
        np_first_u64(b+8)==token && np_first_u32(b+16)==generation && np_first_usb_same_uid(b+20,uid) &&
        np_first_u64(b+32)==containing_sequence && np_first_u32(b+104)==np_first_crc(b,104U) &&
        np_first_usb_body_valid(b+40,64U);
}
static inline int np_first_usb_page_pack(uint8_t out[NP_FIRST_USB_ENVELOPE_BYTES],
    const uint8_t receipt[NP_FIRST_SYNC_BYTES],uint32_t index,const uint32_t uid[3],
    uint64_t token,uint32_t generation,uint64_t containing_sequence)
{
    uint8_t *body;uint32_t bytes=NP_FIRST_USB_SLICE;
    if(!out)return 0;memset(out,0,NP_FIRST_USB_ENVELOPE_BYTES);
    if(!np_first_sync_validate(receipt,NP_FIRST_SYNC_BYTES) || index>=NP_FIRST_USB_PAGE_COUNT ||
       !np_first_usb_uid(uid) || !token || !generation || np_first_u32(receipt+20)!=generation ||
       containing_sequence==UINT64_MAX)return 0;
    body=out+40U;np_first_put32(body,NP_FIRST_USB_PAGE);body[4]=1U;body[5]=(uint8_t)index;body[6]=NP_FIRST_USB_PAGE_COUNT;
    np_first_put32(body+8,np_first_u32(receipt+324));if(index==6U)bytes=40U;
    memcpy(body+12,receipt+index*NP_FIRST_USB_SLICE,bytes);np_first_put32(body+60,np_first_crc(body,60U));
    np_first_put32(out,NP_FIRST_USB_ENVELOPE);np_first_put16(out+4,1U);np_first_put16(out+6,4U);
    np_first_put64(out+8,token);np_first_put32(out+16,generation);np_first_usb_put_uid(out+20,uid);
    np_first_put64(out+32,containing_sequence);np_first_put32(out+104,np_first_crc(out,104U));return 1;
}
static inline int np_first_usb_fine_anchor_valid(const uint8_t *b,size_t bytes,const uint8_t receipt[NP_FIRST_SYNC_BYTES],
    const uint32_t uid[3],uint64_t token,uint32_t generation,uint64_t containing_sequence)
{
    return b && bytes==NP_FIRST_USB_ENVELOPE_BYTES && token && generation && containing_sequence!=UINT64_MAX &&
        np_first_u32(b)==NP_FIRST_USB_ENVELOPE && np_first_u16(b+4)==1U && np_first_u16(b+6)==5U &&
        np_first_u64(b+8)==token && np_first_u32(b+16)==generation && np_first_usb_same_uid(b+20,uid) &&
        np_first_u64(b+32)==containing_sequence && np_first_u32(b+104)==np_first_crc(b,104U) &&
        np_first_sync_source_anchor_valid(b+40,receipt) && np_first_u32(receipt+20)==generation;
}
static inline int np_first_usb_ranges_overlap(const void *a,size_t an,const void *b,size_t bn)
{uintptr_t x=(uintptr_t)a,y=(uintptr_t)b;return x>=y?x-y<bn:y-x<an;}
static inline int np_first_usb_fine_anchor_pack(uint8_t out[NP_FIRST_USB_ENVELOPE_BYTES],const uint8_t anchor[64],
    const uint8_t receipt[NP_FIRST_SYNC_BYTES],const uint32_t uid[3],uint64_t token,uint32_t generation,uint64_t containing_sequence)
{
    if(!out || !anchor || !receipt || np_first_usb_ranges_overlap(out,NP_FIRST_USB_ENVELOPE_BYTES,anchor,64U) ||
       np_first_usb_ranges_overlap(out,NP_FIRST_USB_ENVELOPE_BYTES,receipt,NP_FIRST_SYNC_BYTES))return 0;
    memset(out,0,NP_FIRST_USB_ENVELOPE_BYTES);
    if(!token || !generation || containing_sequence==UINT64_MAX || !np_first_usb_uid(uid) ||
       !np_first_sync_source_anchor_valid(anchor,receipt) || np_first_u32(receipt+20)!=generation)return 0;
    np_first_put32(out,NP_FIRST_USB_ENVELOPE);np_first_put16(out+4,1U);np_first_put16(out+6,5U);
    np_first_put64(out+8,token);np_first_put32(out+16,generation);np_first_usb_put_uid(out+20,uid);
    np_first_put64(out+32,containing_sequence);memcpy(out+40,anchor,64U);np_first_put32(out+104,np_first_crc(out,104U));return 1;
}
#endif
