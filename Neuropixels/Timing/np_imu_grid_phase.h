#ifndef NP_IMU_GRID_PHASE_H
#define NP_IMU_GRID_PHASE_H
#include <stdint.h>
#include <stddef.h>
#include <string.h>

/* NSGP v1: once-per-session producer bounds, not absolute/per-slot time or
 * SAMPLE0_LOCK. Foreground only; no heap. read() is structural, match() is
 * mandatory before associating bounds with an independently known source. */
#define NP_IMU_GRID_PHASE_MAGIC UINT32_C(0x5047534e)
#define NP_IMU_GRID_PHASE_BYTES 84U
#define NP_IMU_GRID_PHASE_INTERVAL_VALID 1U
#define NP_IMU_GRID_PHASE_JITTER_VALID 2U
typedef struct {
    uint8_t uid[12];
    uint32_t session,capture_generation,source_epoch,counter_domain;
    uint64_t token;
} NpImuGridPhaseBinding;
typedef struct {
    NpImuGridPhaseBinding binding;
    uint64_t first_accepted_slot;
    int64_t lower_numerator,upper_numerator;
    uint32_t denominator,maximum_jitter,flags;
} NpImuGridPhaseRecord;
static inline uint32_t np_imu_grid_phase_u32(const uint8_t *p)
{return (uint32_t)p[0]|((uint32_t)p[1]<<8U)|((uint32_t)p[2]<<16U)|((uint32_t)p[3]<<24U);}
static inline uint64_t np_imu_grid_phase_u64(const uint8_t *p)
{return (uint64_t)np_imu_grid_phase_u32(p)|((uint64_t)np_imu_grid_phase_u32(p+4U)<<32U);}
static inline int64_t np_imu_grid_phase_i64(const uint8_t *p)
{uint64_t n=np_imu_grid_phase_u64(p);return n<=INT64_MAX?(int64_t)n:-1-(int64_t)(~n);}
static inline void np_imu_grid_phase_put32(uint8_t *p,uint32_t n)
{for(unsigned i=0U;i<4U;i++)p[i]=(uint8_t)(n>>(i*8U));}
static inline void np_imu_grid_phase_put64(uint8_t *p,uint64_t n)
{np_imu_grid_phase_put32(p,(uint32_t)n);np_imu_grid_phase_put32(p+4U,(uint32_t)(n>>32U));}
static inline uint32_t np_imu_grid_phase_crc(const uint8_t *p)
{
    uint32_t c=UINT32_MAX;
    for(unsigned i=0U;i<80U;i++){c^=p[i];for(unsigned j=0U;j<8U;j++)c=(c>>1U)^(UINT32_C(0xedb88320)&(0U-(c&1U)));}
    return ~c;
}
static inline int np_imu_grid_phase_binding_valid(const NpImuGridPhaseBinding *b)
{
    unsigned any=0U,not_ff=0U;
    if(!b)return 0;
    for(unsigned i=0U;i<12U;i++){any|=b->uid[i];not_ff|=(unsigned)(b->uid[i]^255U);}
    return any && not_ff && b->session && b->capture_generation && b->source_epoch && b->counter_domain && b->token;
}
static inline int np_imu_grid_phase_valid(const NpImuGridPhaseRecord *p)
{
    if(!p || !np_imu_grid_phase_binding_valid(&p->binding) || p->first_accepted_slot==UINT64_MAX || (p->flags&~3U))return 0;
    if(p->flags&NP_IMU_GRID_PHASE_INTERVAL_VALID){
        if(p->lower_numerator<0 || p->upper_numerator<p->lower_numerator || !p->denominator)return 0;
    }else if(p->lower_numerator!=INT64_MIN || p->upper_numerator!=INT64_MIN || p->denominator)return 0;
    return (p->flags&NP_IMU_GRID_PHASE_JITTER_VALID)?p->maximum_jitter!=UINT32_MAX:p->maximum_jitter==UINT32_MAX;
}
static inline int np_imu_grid_phase_read(const uint8_t *raw,size_t bytes,NpImuGridPhaseRecord *out)
{
    NpImuGridPhaseRecord p={0};
    if(out)memset(out,0,sizeof(*out));
    if(!raw || bytes!=84U || np_imu_grid_phase_u32(raw)!=NP_IMU_GRID_PHASE_MAGIC ||
       raw[4]!=1U || raw[5] || raw[6]!=84U || raw[7] || np_imu_grid_phase_u32(raw+80U)!=np_imu_grid_phase_crc(raw))return 0;
    memcpy(p.binding.uid,raw+8U,12U);p.binding.session=np_imu_grid_phase_u32(raw+20U);
    p.binding.capture_generation=np_imu_grid_phase_u32(raw+24U);p.binding.source_epoch=np_imu_grid_phase_u32(raw+28U);
    p.binding.counter_domain=np_imu_grid_phase_u32(raw+32U);p.binding.token=np_imu_grid_phase_u64(raw+36U);
    p.first_accepted_slot=np_imu_grid_phase_u64(raw+44U);p.lower_numerator=np_imu_grid_phase_i64(raw+52U);
    p.upper_numerator=np_imu_grid_phase_i64(raw+60U);p.denominator=np_imu_grid_phase_u32(raw+68U);
    p.maximum_jitter=np_imu_grid_phase_u32(raw+72U);p.flags=np_imu_grid_phase_u32(raw+76U);
    if(!np_imu_grid_phase_valid(&p))return 0;
    if(out)*out=p;return 1;
}
static inline int np_imu_grid_phase_match(const uint8_t *raw,size_t bytes,const NpImuGridPhaseBinding *expected)
{
    NpImuGridPhaseRecord p;
    return np_imu_grid_phase_binding_valid(expected) && np_imu_grid_phase_read(raw,bytes,&p) &&
        !memcmp(p.binding.uid,expected->uid,12U) && p.binding.session==expected->session &&
        p.binding.capture_generation==expected->capture_generation && p.binding.source_epoch==expected->source_epoch &&
        p.binding.counter_domain==expected->counter_domain && p.binding.token==expected->token;
}
/* Input/output storage must not overlap. Rejected input leaves raw unchanged. */
static inline int np_imu_grid_phase_pack(uint8_t *raw,size_t bytes,const NpImuGridPhaseRecord *p)
{
    if(!raw || bytes!=84U || !np_imu_grid_phase_valid(p))return 0;
    memset(raw,0,84U);np_imu_grid_phase_put32(raw,NP_IMU_GRID_PHASE_MAGIC);raw[4]=1U;raw[6]=84U;
    memcpy(raw+8U,p->binding.uid,12U);np_imu_grid_phase_put32(raw+20U,p->binding.session);
    np_imu_grid_phase_put32(raw+24U,p->binding.capture_generation);np_imu_grid_phase_put32(raw+28U,p->binding.source_epoch);
    np_imu_grid_phase_put32(raw+32U,p->binding.counter_domain);np_imu_grid_phase_put64(raw+36U,p->binding.token);
    np_imu_grid_phase_put64(raw+44U,p->first_accepted_slot);np_imu_grid_phase_put64(raw+52U,(uint64_t)p->lower_numerator);
    np_imu_grid_phase_put64(raw+60U,(uint64_t)p->upper_numerator);np_imu_grid_phase_put32(raw+68U,p->denominator);
    np_imu_grid_phase_put32(raw+72U,p->maximum_jitter);np_imu_grid_phase_put32(raw+76U,p->flags);
    np_imu_grid_phase_put32(raw+80U,np_imu_grid_phase_crc(raw));return 1;
}
#endif
