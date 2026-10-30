#ifndef NP_FIRST_SYNC_SD_H
#define NP_FIRST_SYNC_SD_H
#include "np_first_sync_wire.h"

/* Pure per-record opt-in codec. No persistent next-record latch, controller
 * mutation, SD I/O or retry. Ordinary legacy controller owns range/options. */
#define NP_FIRST_SD_REQUEST UINT32_C(0x4453504e) /* NPSD */
#define NP_FIRST_SD_REPLY UINT32_C(0x4153504e) /* NPSA */
enum { NP_FIRST_SD_CAPS=0U,NP_FIRST_SD_START=1U,NP_FIRST_SD_STATUS=2U,NP_FIRST_SD_SCHEDULED=3U };
enum { NP_FIRST_SD_OK=0U,NP_FIRST_SD_UNSUPPORTED=1U,NP_FIRST_SD_INVALID=2U,
       NP_FIRST_SD_BUSY=3U,NP_FIRST_SD_NOT_FOUND=4U,NP_FIRST_SD_ERROR=5U };
enum { NP_FIRST_SD_FEATURE_SYNC=1U,NP_FIRST_SD_FEATURE_SCHEDULED=2U,NP_FIRST_SD_FEATURE_FINE_CLOCK=4U,
       NP_FIRST_SD_FEATURE_FINE_AUX=8U,NP_FIRST_SD_FEATURE_FINE_COUNTER64=16U };
typedef struct {
    uint32_t result,supported,record_features,session,generation,source_epoch,profile_crc;
} NpFirstSdReply;
typedef struct {uint32_t uid[3];uint64_t token;uint8_t receipt[NP_FIRST_SYNC_BYTES];} NpFirstSdBound;
static inline int np_first_sd_uid_valid(const uint32_t uid[3])
{return uid && (uid[0]|uid[1]|uid[2]) && (uid[0]&uid[1]&uid[2])!=UINT32_MAX;}
static inline void np_first_sd_uid_get(const uint8_t *b,uint32_t uid[3])
{unsigned i;for(i=0U;i<3U;i++)uid[i]=np_first_u32(b+4U*i);}
static inline void np_first_sd_uid_put(uint8_t *b,const uint32_t uid[3])
{unsigned i;for(i=0U;i<3U;i++)np_first_put32(b+4U*i,uid[i]);}
static inline int np_first_sd_uid_same(const uint8_t *b,const uint32_t uid[3])
{return np_first_sd_uid_valid(uid) && np_first_u32(b)==uid[0] && np_first_u32(b+4)==uid[1] && np_first_u32(b+8)==uid[2];}
static inline int np_first_sd_version_family(uint32_t version,uint32_t family)
{return version==1U || version==2U || version==3U || (version==4U && family==2U);}
static inline int np_first_sd_shape_version(const uint8_t *b,size_t bytes,uint32_t family,const uint32_t uid[3],int admit,uint32_t version)
{
    uint32_t op;uint64_t token;
    if(!b || bytes!=64U || (family!=1U && family!=2U) || np_first_u32(b)!=NP_FIRST_SD_REQUEST ||
       !np_first_sd_version_family(version,family) || np_first_u16(b+4)!=version || !np_first_u32(b+8) || !np_first_sd_uid_same(b+12,uid) ||
       np_first_u32(b+60)!=np_first_crc(b,60U))return 0;
    op=np_first_u16(b+6);token=np_first_u64(b+24);if(op>NP_FIRST_SD_SCHEDULED)return 0;
    if(op==NP_FIRST_SD_CAPS)return !token && np_first_zero(b+32,28U);
    if(!token)return 0;
    if(op==NP_FIRST_SD_STATUS)return np_first_zero(b+32,28U);
    return !(admit && family==1U && op==NP_FIRST_SD_SCHEDULED) &&
        np_first_u32(b+32) && !(np_first_u32(b+32)&~7U) && np_first_profile(np_first_u32(b+36)) &&
        (family!=2U || np_first_zero(b+52,8U));
}
/* Existing entry points remain version1 only. Merely copying this header into
 * an old firmware port cannot admit a version2 mutation. */
static inline int np_first_sd_shape(const uint8_t *b,size_t bytes,uint32_t family,const uint32_t uid[3],int admit)
{return np_first_sd_shape_version(b,bytes,family,uid,admit,1U);}
static inline int np_first_sd_request_valid(const uint8_t *b,size_t bytes,uint32_t family,const uint32_t uid[3])
{return np_first_sd_shape(b,bytes,family,uid,1);}
static inline int np_first_sd_fine_aux_request_valid(const uint8_t *b,size_t bytes,uint32_t family,const uint32_t uid[3])
{return np_first_sd_shape_version(b,bytes,family,uid,1,2U);}
static inline int np_first_sd_fine_counter64_request_valid(const uint8_t *b,size_t bytes,uint32_t family,const uint32_t uid[3])
{return np_first_sd_shape_version(b,bytes,family,uid,1,3U);}
/* Explicit V2-only SD16 opt-in. Existing v1/v2/v3 entrypoints stay strict. */
static inline int np_first_sd_grid16_request_valid(const uint8_t *b,size_t bytes,uint32_t family,const uint32_t uid[3])
{return np_first_sd_shape_version(b,bytes,family,uid,1,4U);}
/* Structural parsing precedes the cached duplicate check. Only a new mutation
 * uses freshness; an exact accepted duplicate can return its ACK read-only. */
static inline int np_first_sd_fresh_start(const uint8_t *b,size_t bytes,uint32_t family,const uint32_t uid[3],uint64_t last_token)
{return np_first_sd_request_valid(b,bytes,family,uid) &&
    (np_first_u16(b+6)==NP_FIRST_SD_START || np_first_u16(b+6)==NP_FIRST_SD_SCHEDULED) && np_first_u64(b+24)!=last_token;}
static inline int np_first_sd_cached_duplicate(const uint8_t *b,const uint8_t accepted[64],uint32_t family,const uint32_t uid[3])
{return accepted && np_first_sd_request_valid(b,64U,family,uid) && np_first_sd_request_valid(accepted,64U,family,uid) &&
    (np_first_u16(b+6)==NP_FIRST_SD_START || np_first_u16(b+6)==NP_FIRST_SD_SCHEDULED) && !memcmp(b,accepted,64U);}
static inline int np_first_sd_fine_aux_fresh_start(const uint8_t *b,size_t bytes,uint32_t family,const uint32_t uid[3],uint64_t last_token)
{return np_first_sd_fine_aux_request_valid(b,bytes,family,uid) &&
    (np_first_u16(b+6)==NP_FIRST_SD_START || np_first_u16(b+6)==NP_FIRST_SD_SCHEDULED) && np_first_u64(b+24)!=last_token;}
static inline int np_first_sd_fine_aux_cached_duplicate(const uint8_t *b,const uint8_t accepted[64],uint32_t family,const uint32_t uid[3])
{return accepted && np_first_sd_fine_aux_request_valid(b,64U,family,uid) && np_first_sd_fine_aux_request_valid(accepted,64U,family,uid) &&
    (np_first_u16(b+6)==NP_FIRST_SD_START || np_first_u16(b+6)==NP_FIRST_SD_SCHEDULED) && !memcmp(b,accepted,64U);}
static inline int np_first_sd_fine_counter64_fresh_start(const uint8_t *b,size_t bytes,uint32_t family,const uint32_t uid[3],uint64_t last_token)
{return np_first_sd_fine_counter64_request_valid(b,bytes,family,uid) &&
    (np_first_u16(b+6)==NP_FIRST_SD_START || np_first_u16(b+6)==NP_FIRST_SD_SCHEDULED) && np_first_u64(b+24)!=last_token;}
static inline int np_first_sd_fine_counter64_cached_duplicate(const uint8_t *b,const uint8_t accepted[64],uint32_t family,const uint32_t uid[3])
{return accepted && np_first_sd_fine_counter64_request_valid(b,64U,family,uid) && np_first_sd_fine_counter64_request_valid(accepted,64U,family,uid) &&
    (np_first_u16(b+6)==NP_FIRST_SD_START || np_first_u16(b+6)==NP_FIRST_SD_SCHEDULED) && !memcmp(b,accepted,64U);}
static inline int np_first_sd_grid16_fresh_start(const uint8_t *b,size_t bytes,uint32_t family,const uint32_t uid[3],uint64_t last_token)
{return np_first_sd_grid16_request_valid(b,bytes,family,uid) &&
    (np_first_u16(b+6)==NP_FIRST_SD_START || np_first_u16(b+6)==NP_FIRST_SD_SCHEDULED) && np_first_u64(b+24)!=last_token;}
static inline int np_first_sd_grid16_cached_duplicate(const uint8_t *b,const uint8_t accepted[64],uint32_t family,const uint32_t uid[3])
{return accepted && np_first_sd_grid16_request_valid(b,64U,family,uid) && np_first_sd_grid16_request_valid(accepted,64U,family,uid) &&
    (np_first_u16(b+6)==NP_FIRST_SD_START || np_first_u16(b+6)==NP_FIRST_SD_SCHEDULED) && !memcmp(b,accepted,64U);}
static inline int np_first_sd_query_pack_version(uint8_t out[64],uint32_t op,uint32_t id,uint32_t family,const uint32_t uid[3],uint64_t token,uint32_t version)
{
    if(!out)return 0;memset(out,0,64U);if((op!=NP_FIRST_SD_CAPS && op!=NP_FIRST_SD_STATUS) || !np_first_sd_uid_valid(uid))return 0;
    np_first_put32(out,NP_FIRST_SD_REQUEST);np_first_put16(out+4,(uint16_t)version);np_first_put16(out+6,(uint16_t)op);np_first_put32(out+8,id);
    np_first_sd_uid_put(out+12,uid);np_first_put64(out+24,token);np_first_put32(out+60,np_first_crc(out,60U));
    if(np_first_sd_shape_version(out,64U,family,uid,1,version))return 1;memset(out,0,64U);return 0;
}
static inline int np_first_sd_query_pack(uint8_t out[64],uint32_t op,uint32_t id,uint32_t family,const uint32_t uid[3],uint64_t token)
{return np_first_sd_query_pack_version(out,op,id,family,uid,token,1U);}
static inline int np_first_sd_fine_aux_query_pack(uint8_t out[64],uint32_t op,uint32_t id,uint32_t family,const uint32_t uid[3],uint64_t token)
{return np_first_sd_query_pack_version(out,op,id,family,uid,token,2U);}
static inline int np_first_sd_fine_counter64_query_pack(uint8_t out[64],uint32_t op,uint32_t id,uint32_t family,const uint32_t uid[3],uint64_t token)
{return np_first_sd_query_pack_version(out,op,id,family,uid,token,3U);}
static inline int np_first_sd_grid16_query_pack(uint8_t out[64],uint32_t op,uint32_t id,uint32_t family,const uint32_t uid[3],uint64_t token)
{return np_first_sd_query_pack_version(out,op,id,family,uid,token,4U);}
static inline int np_first_sd_legacy_valid(const uint8_t *b,size_t bytes,uint32_t family)
{
    uint32_t op;
    if(!b || bytes!=64U || (family!=1U && family!=2U) ||
       np_first_u32(b)!=(family==1U?UINT32_C(0x31444c4e):UINT32_C(0x32444c4e)) ||
       np_first_u32(b+4)!=(family==1U?1U:2U) || !np_first_u32(b+12) ||
       np_first_u32(b+60)!=np_first_crc(b,60U) || !np_first_zero(b+44,16U) ||
       !np_first_u32(b+16) || (np_first_u32(b+16)&~7U) || !np_first_profile(np_first_u32(b+20)))return 0;
    op=np_first_u32(b+8);
    return (op==2U || (family==2U && op==4U)) && (family!=2U || np_first_zero(b+36,8U));
}
/* A validated NLD1/2 frame is copied, never interpreted/rebudgeted. V1 op6
 * and UTC44..51 cannot fit and are rejected, leaving the legacy path intact. */
static inline int np_first_sd_wrap_version(uint8_t out[64],const uint8_t legacy[64],uint32_t family,
                                   const uint32_t uid[3],uint64_t token,uint64_t last_token,uint32_t version)
{
    if(!out || out==legacy)return 0;memset(out,0,64U);
    if(!np_first_sd_version_family(version,family) || !np_first_sd_legacy_valid(legacy,64U,family) || !np_first_sd_uid_valid(uid) || !token || token==last_token)return 0;
    np_first_put32(out,NP_FIRST_SD_REQUEST);np_first_put16(out+4,(uint16_t)version);
    np_first_put16(out+6,(uint16_t)(np_first_u32(legacy+8)==4U?NP_FIRST_SD_SCHEDULED:NP_FIRST_SD_START));
    np_first_put32(out+8,np_first_u32(legacy+12));np_first_sd_uid_put(out+12,uid);np_first_put64(out+24,token);
    memcpy(out+32,legacy+16,28U);np_first_put32(out+60,np_first_crc(out,60U));return 1;
}
static inline int np_first_sd_wrap(uint8_t out[64],const uint8_t legacy[64],uint32_t family,const uint32_t uid[3],uint64_t token,uint64_t last_token)
{return np_first_sd_wrap_version(out,legacy,family,uid,token,last_token,1U);}
static inline int np_first_sd_fine_aux_wrap(uint8_t out[64],const uint8_t legacy[64],uint32_t family,const uint32_t uid[3],uint64_t token,uint64_t last_token)
{return np_first_sd_wrap_version(out,legacy,family,uid,token,last_token,2U);}
static inline int np_first_sd_fine_counter64_wrap(uint8_t out[64],const uint8_t legacy[64],uint32_t family,const uint32_t uid[3],uint64_t token,uint64_t last_token)
{return np_first_sd_wrap_version(out,legacy,family,uid,token,last_token,3U);}
static inline int np_first_sd_grid16_wrap(uint8_t out[64],const uint8_t legacy[64],uint32_t family,const uint32_t uid[3],uint64_t token,uint64_t last_token)
{return np_first_sd_wrap_version(out,legacy,family,uid,token,last_token,4U);}
static inline int np_first_sd_unwrap_version(uint8_t out[64],const uint8_t request[64],uint32_t family,const uint32_t uid[3],uint32_t version)
{
    uint32_t op;if(!out || out==request)return 0;memset(out,0,64U);
    if(!np_first_sd_shape_version(request,64U,family,uid,1,version))return 0;
    op=np_first_u16(request+6);if(op!=NP_FIRST_SD_START && op!=NP_FIRST_SD_SCHEDULED)return 0;
    np_first_put32(out,family==1U?UINT32_C(0x31444c4e):UINT32_C(0x32444c4e));
    np_first_put32(out+4,family==1U?1U:2U);np_first_put32(out+8,op==NP_FIRST_SD_SCHEDULED?4U:2U);
    np_first_put32(out+12,np_first_u32(request+8));memcpy(out+16,request+32,28U);
    np_first_put32(out+60,np_first_crc(out,60U));return 1;
}
static inline int np_first_sd_unwrap(uint8_t out[64],const uint8_t request[64],uint32_t family,const uint32_t uid[3])
{return np_first_sd_unwrap_version(out,request,family,uid,1U);}
static inline int np_first_sd_fine_aux_unwrap(uint8_t out[64],const uint8_t request[64],uint32_t family,const uint32_t uid[3])
{return np_first_sd_unwrap_version(out,request,family,uid,2U);}
static inline int np_first_sd_fine_counter64_unwrap(uint8_t out[64],const uint8_t request[64],uint32_t family,const uint32_t uid[3])
{return np_first_sd_unwrap_version(out,request,family,uid,3U);}
static inline int np_first_sd_grid16_unwrap(uint8_t out[64],const uint8_t request[64],uint32_t family,const uint32_t uid[3])
{return np_first_sd_unwrap_version(out,request,family,uid,4U);}
static inline int np_first_sd_reply_valid_features(const uint8_t *b,size_t bytes,const uint8_t request[64],uint32_t family,uint32_t version,int continuity_caps)
{
    uint32_t uid[3],result,supported,op,record_features,allowed;if(!request)return 0;np_first_sd_uid_get(request+12,uid);
    if(!b || bytes!=64U || !np_first_sd_shape_version(request,64U,family,uid,0,version) ||
       np_first_u32(b)!=NP_FIRST_SD_REPLY || memcmp(b+4,request+4,28U) ||
       np_first_u32(b+60)!=np_first_crc(b,60U))return 0;
    result=np_first_u32(b+32);supported=np_first_u32(b+36);op=np_first_u16(request+6);
    record_features=version>=3U?25U:version==2U?9U:1U;
    allowed=(family==1U?5U:7U)|(version>=2U?8U:0U)|((version>=3U || (version==2U && continuity_caps))?16U:0U);
    if(result>NP_FIRST_SD_ERROR || (supported&~allowed) ||
       ((supported&6U) && !(supported&1U)) || ((supported&8U) && (supported&13U)!=13U) ||
       ((supported&16U) && (supported&29U)!=29U) || (np_first_u32(b+40)&~record_features) ||
       (version>=2U && !result && (supported&(version>=3U?29U:13U))!=(version>=3U?29U:13U)))return 0;
    if(result || op==NP_FIRST_SD_CAPS)return !np_first_u32(b+40) && !np_first_u32(b+44) &&
        !np_first_u32(b+48) && !np_first_u32(b+52) && np_first_u32(b+56)==UINT32_MAX;
    return np_first_u32(b+40)==record_features && (supported&NP_FIRST_SD_FEATURE_SYNC) &&
        (op!=NP_FIRST_SD_SCHEDULED || (family==2U && (supported&NP_FIRST_SD_FEATURE_SCHEDULED)));
}
static inline int np_first_sd_reply_valid_version(const uint8_t *b,size_t bytes,const uint8_t request[64],uint32_t family,uint32_t version)
{return np_first_sd_reply_valid_features(b,bytes,request,family,version,0);}
static inline int np_first_sd_reply_valid(const uint8_t *b,size_t bytes,const uint8_t request[64],uint32_t family)
{return np_first_sd_reply_valid_version(b,bytes,request,family,1U);}
static inline int np_first_sd_fine_aux_reply_valid(const uint8_t *b,size_t bytes,const uint8_t request[64],uint32_t family)
{return np_first_sd_reply_valid_version(b,bytes,request,family,2U);}
static inline int np_first_sd_fine_counter64_reply_valid(const uint8_t *b,size_t bytes,const uint8_t request[64],uint32_t family)
{return np_first_sd_reply_valid_version(b,bytes,request,family,3U);}
static inline int np_first_sd_grid16_reply_valid(const uint8_t *b,size_t bytes,const uint8_t request[64],uint32_t family)
{return np_first_sd_reply_valid_version(b,bytes,request,family,4U);}
/* Explicit modern v2 discovery/association compatibility. A v2 START still
 * selects record9, never record25; existing fine_aux entry points stay strict. */
static inline int np_first_sd_fine_aux_compatible_reply_valid(const uint8_t *b,size_t bytes,const uint8_t request[64],uint32_t family)
{return np_first_sd_reply_valid_features(b,bytes,request,family,2U,1);}
static inline int np_first_sd_reply_pack_features(uint8_t out[64],const uint8_t request[64],uint32_t family,const NpFirstSdReply *r,uint32_t version,int continuity_caps)
{
    if(!out || !request || !r || out==request)return 0;memset(out,0,64U);np_first_put32(out,NP_FIRST_SD_REPLY);memcpy(out+4,request+4,28U);
    np_first_put32(out+32,r->result);np_first_put32(out+36,r->supported);np_first_put32(out+40,r->record_features);
    np_first_put32(out+44,r->session);np_first_put32(out+48,r->generation);np_first_put32(out+52,r->source_epoch);
    np_first_put32(out+56,r->profile_crc);np_first_put32(out+60,np_first_crc(out,60U));
    if(np_first_sd_reply_valid_features(out,64U,request,family,version,continuity_caps))return 1;memset(out,0,64U);return 0;
}
static inline int np_first_sd_reply_pack_version(uint8_t out[64],const uint8_t request[64],uint32_t family,const NpFirstSdReply *r,uint32_t version)
{return np_first_sd_reply_pack_features(out,request,family,r,version,0);}
static inline int np_first_sd_reply_pack(uint8_t out[64],const uint8_t request[64],uint32_t family,const NpFirstSdReply *r)
{return np_first_sd_reply_pack_version(out,request,family,r,1U);}
static inline int np_first_sd_fine_aux_reply_pack(uint8_t out[64],const uint8_t request[64],uint32_t family,const NpFirstSdReply *r)
{return np_first_sd_reply_pack_version(out,request,family,r,2U);}
static inline int np_first_sd_fine_counter64_reply_pack(uint8_t out[64],const uint8_t request[64],uint32_t family,const NpFirstSdReply *r)
{return np_first_sd_reply_pack_version(out,request,family,r,3U);}
static inline int np_first_sd_grid16_reply_pack(uint8_t out[64],const uint8_t request[64],uint32_t family,const NpFirstSdReply *r)
{return np_first_sd_reply_pack_version(out,request,family,r,4U);}
static inline int np_first_sd_fine_aux_compatible_reply_pack(uint8_t out[64],const uint8_t request[64],uint32_t family,const NpFirstSdReply *r)
{return np_first_sd_reply_pack_features(out,request,family,r,2U,1);}
/* Common UTC2 framing/clock checks only; bytes80..159 remain owned by the
 * separately validated legacy CL/overflow provider and are never rewritten. */
static inline int np_first_sd_base_valid(const uint8_t *b,uint32_t family,uint32_t session,uint32_t hz)
{
    uint32_t f,before,after,delta,span;uint64_t utc,lo,hi;
    if(!b || (family!=1U && family!=2U) || !session || !np_first_profile(hz) ||
       memcmp(b,family==1U?"N1SDUTC2":"N2SDUTC2",8U) || np_first_u32(b+8)!=2U || np_first_u32(b+12)!=session ||
       np_first_u32(b+72)!=hz || np_first_u32(b+76)!=1000U || np_first_u32(b+508)!=np_first_crc(b,508U))return 0;
    f=np_first_u32(b+16);utc=np_first_u64(b+32);
    if((f&~(family==1U?7U:15U)) || ((f&1U) && (!np_first_u32(b+20) || np_first_u32(b+24)<1000U ||
       np_first_u32(b+24)>5000000U || utc<UINT64_C(1577836800000000) || utc>UINT64_C(4102444800000000))))return 0;
    if((f&3U)!=3U)return 1;
    before=np_first_u32(b+(family==1U?64U:48U));after=np_first_u32(b+(family==1U?68U:52U));
    lo=np_first_u64(b+(family==1U?48U:56U));hi=np_first_u64(b+(family==1U?56U:64U));
    delta=before-np_first_u32(b+28);span=after-before;
    return delta<UINT32_C(0x80000000) && span<UINT32_C(0x80000000) &&
        (uint64_t)delta+span<UINT64_C(0x80000000) && lo==utc+(uint64_t)delta*1000U && hi==lo+(uint64_t)span*1000U;
}
static inline int np_first_sd_sector_bind(uint8_t out[512],const uint8_t legacy[512],const uint8_t receipt[NP_FIRST_SYNC_BYTES],
                                         uint32_t family,const uint32_t uid[3],uint64_t token)
{
    if(!out || out==receipt)return 0;
    if(!np_first_sync_validate(receipt,NP_FIRST_SYNC_BYTES) || !np_first_sd_uid_valid(uid) || !token ||
       ((np_first_u32(receipt+12)>>8)&255U)!=family ||
       !np_first_sd_base_valid(legacy,family,np_first_u32(receipt+16),np_first_u32(receipt+36)) ||
       !np_first_zero(legacy+160,348U)){if(out!=legacy)memset(out,0,512U);return 0;}
    memmove(out,legacy,512U);memcpy(out+160,receipt,NP_FIRST_SYNC_BYTES);np_first_sd_uid_put(out+488,uid);np_first_put64(out+500,token);
    np_first_put32(out+508,np_first_crc(out,508U));return 1;
}
static inline int np_first_sd_sector_read(const uint8_t *b,size_t bytes,uint32_t family,uint32_t session,uint32_t hz,
    const uint32_t expected_uid[3],uint64_t expected_token,NpFirstSdBound *record)
{
    uint32_t uid[3];uint64_t token;
    if(!b || bytes!=512U || !np_first_sd_base_valid(b,family,session,hz) ||
       !np_first_sync_validate(b+160,NP_FIRST_SYNC_BYTES) ||
       ((np_first_u32(b+172)>>8)&255U)!=family || np_first_u32(b+176)!=session || np_first_u32(b+196)!=hz)return 0;
    np_first_sd_uid_get(b+488,uid);token=np_first_u64(b+500);
    if(!np_first_sd_uid_valid(uid) || !token || (expected_uid && !np_first_sd_uid_same(b+488,expected_uid)) ||
       (expected_token && token!=expected_token))return 0;
    if(record){memcpy(record->uid,uid,sizeof(uid));record->token=token;memcpy(record->receipt,b+160,NP_FIRST_SYNC_BYTES);}return 1;
}
#endif
