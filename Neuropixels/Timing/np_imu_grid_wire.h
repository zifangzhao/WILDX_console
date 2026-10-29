#ifndef NP_IMU_GRID_WIRE_H
#define NP_IMU_GRID_WIRE_H
#include <stdint.h>
#include <stddef.h>
#include <string.h>

/* NSG1 RAW batch only. No carrier declaration, device I/O, clock or T0 proof.
 * Rows are AX/AY/AZ microG, GX/GY/GZ milli-dps, MX/MY/MZ nanoT, as SNS1.
 * A missing slot has no payload row. Repeated/invalid data is never inferred.
 * Pack/CRC are foreground operations; the C codec allocates no heap/buffer. */
#define NP_IMU_GRID_MAGIC UINT32_C(0x3147534e)
#define NP_IMU_GRID_HEADER_BYTES 48U
#define NP_IMU_GRID_ROW_BYTES 36U
#define NP_IMU_GRID_MAX_SLOTS 12U
#define NP_IMU_GRID_MAX_BYTES 480U
typedef struct {
    uint64_t first_slot;
    uint32_t capture_generation;
    uint16_t slot_count,observed_mask,ag_valid,mag_valid,ag_repeat,mag_repeat,error_mask;
} NpImuGridHeader;
typedef struct {
    NpImuGridHeader header;
    const uint8_t *rows; /* Compact ascending observed slots; raw must stay unchanged. */
    uint32_t payload_bytes;
} NpImuGridView;
static inline uint16_t np_imu_grid_u16(const uint8_t *p)
{return (uint16_t)((uint16_t)p[0]|((uint16_t)p[1]<<8U));}
static inline uint32_t np_imu_grid_u32(const uint8_t *p)
{return (uint32_t)p[0]|((uint32_t)p[1]<<8U)|((uint32_t)p[2]<<16U)|((uint32_t)p[3]<<24U);}
static inline uint64_t np_imu_grid_u64(const uint8_t *p)
{return (uint64_t)np_imu_grid_u32(p)|((uint64_t)np_imu_grid_u32(p+4U)<<32U);}
static inline void np_imu_grid_put16(uint8_t *p,uint16_t n)
{p[0]=(uint8_t)n;p[1]=(uint8_t)(n>>8U);}
static inline void np_imu_grid_put32(uint8_t *p,uint32_t n)
{for(unsigned i=0U;i<4U;i++)p[i]=(uint8_t)(n>>(i*8U));}
static inline void np_imu_grid_put64(uint8_t *p,uint64_t n)
{np_imu_grid_put32(p,(uint32_t)n);np_imu_grid_put32(p+4U,(uint32_t)(n>>32U));}
static inline unsigned np_imu_grid_count(uint16_t mask)
{unsigned n=0U;while(mask){n+=mask&1U;mask=(uint16_t)(mask>>1U);}return n;}
static inline int np_imu_grid_header_valid(const NpImuGridHeader *h)
{
    uint16_t allowed;
    if(!h || !h->capture_generation || !h->slot_count || h->slot_count>NP_IMU_GRID_MAX_SLOTS ||
       h->first_slot>UINT64_MAX-h->slot_count)return 0;
    allowed=(uint16_t)((1U<<h->slot_count)-1U);
    return !(h->observed_mask&~allowed) &&
        !((h->ag_valid|h->mag_valid|h->ag_repeat|h->mag_repeat|h->error_mask)&~h->observed_mask) &&
        !(h->ag_repeat&~h->ag_valid) && !(h->mag_repeat&~h->mag_valid);
}
/* CRC32/IEEE includes header0..43 then payload48..end; skips the CRC word. */
static inline uint32_t np_imu_grid_crc(const uint8_t *raw,size_t bytes)
{
    uint32_t crc=UINT32_MAX;
    for(size_t i=0U;i<bytes;i++)if(i<44U || i>=48U){
        crc^=raw[i];for(unsigned b=0U;b<8U;b++)crc=(crc>>1U)^(UINT32_C(0xedb88320)&(0U-(crc&1U)));
    }
    return ~crc;
}
static inline int np_imu_grid_overlap(const void *a,size_t an,const void *b,size_t bn)
{uintptr_t x=(uintptr_t)a,y=(uintptr_t)b;return an && bn && (x>=y?x-y<bn:y-x<an);}
/* Returns0 on malformed input; output view is cleared (except rejected alias).
 * The view borrows immutable raw storage; it is not a new ownership/lifetime. */
static inline int np_imu_grid_read(const uint8_t *raw,size_t bytes,NpImuGridView *out)
{
    NpImuGridView v={0};
    if(out && raw && np_imu_grid_overlap(out,sizeof(*out),raw,bytes))return 0;
    if(out)memset(out,0,sizeof(*out));
    if(!raw || bytes<NP_IMU_GRID_HEADER_BYTES || bytes>NP_IMU_GRID_MAX_BYTES ||
       np_imu_grid_u32(raw)!=NP_IMU_GRID_MAGIC || raw[4]!=1U || raw[5]!=1U ||
       np_imu_grid_u16(raw+6U)!=48U || np_imu_grid_u16(raw+18U)!=36U || np_imu_grid_u32(raw+36U))return 0;
    v.header.first_slot=np_imu_grid_u64(raw+8U);v.header.slot_count=np_imu_grid_u16(raw+16U);
    v.header.observed_mask=np_imu_grid_u16(raw+20U);v.header.ag_valid=np_imu_grid_u16(raw+22U);
    v.header.mag_valid=np_imu_grid_u16(raw+24U);v.header.ag_repeat=np_imu_grid_u16(raw+26U);
    v.header.mag_repeat=np_imu_grid_u16(raw+28U);v.header.error_mask=np_imu_grid_u16(raw+30U);
    v.header.capture_generation=np_imu_grid_u32(raw+40U);v.payload_bytes=np_imu_grid_u32(raw+32U);
    if(!np_imu_grid_header_valid(&v.header) || v.payload_bytes!=np_imu_grid_count(v.header.observed_mask)*36U ||
       bytes!=48U+v.payload_bytes || np_imu_grid_u32(raw+44U)!=np_imu_grid_crc(raw,bytes))return 0;
    v.rows=raw+48U;if(out)*out=v;return 1;
}
/* Slot lookup preserves compact observed-row ordering; missing returns0/zeros.
 * Only use a view from read() while its raw bytes remain unchanged. */
static inline int np_imu_grid_row(const NpImuGridView *v,unsigned slot,int32_t out[9])
{
    unsigned row;
    if(!out)return 0;
    if(v && np_imu_grid_overlap(out,36U,v->rows,v->payload_bytes))return 0;
    memset(out,0,36U);
    if(!v || !np_imu_grid_header_valid(&v->header) || !v->rows ||
       v->payload_bytes!=np_imu_grid_count(v->header.observed_mask)*36U || slot>=v->header.slot_count ||
       !(v->header.observed_mask&(1U<<slot)))return 0;
    row=np_imu_grid_count((uint16_t)(v->header.observed_mask&((1U<<slot)-1U)));
    for(unsigned c=0U;c<9U;c++){
        uint32_t n=np_imu_grid_u32(v->rows+row*36U+c*4U);
        out[c]=n<=INT32_MAX?(int32_t)n:-1-(int32_t)(UINT32_MAX-n);
    }
    return 1;
}
/* Caller-owned compact rows in ascending observed-bit order. Rejects leave
 * output unchanged. Return exact frame bytes, or0; no implicit carrier opt-in. */
static inline size_t np_imu_grid_pack(uint8_t *out,size_t capacity,const NpImuGridHeader *h,
                                     const int32_t (*rows)[9],size_t row_count)
{
    size_t bytes;
    if(!out || !np_imu_grid_header_valid(h) || row_count!=np_imu_grid_count(h->observed_mask) ||
       (row_count && !rows))return 0U;
    bytes=48U+row_count*36U;
    if(capacity<bytes || np_imu_grid_overlap(out,bytes,h,sizeof(*h)) ||
       np_imu_grid_overlap(out,bytes,rows,row_count*36U))return 0U;
    np_imu_grid_put32(out,NP_IMU_GRID_MAGIC);out[4]=1U;out[5]=1U;np_imu_grid_put16(out+6U,48U);
    np_imu_grid_put64(out+8U,h->first_slot);np_imu_grid_put16(out+16U,h->slot_count);np_imu_grid_put16(out+18U,36U);
    np_imu_grid_put16(out+20U,h->observed_mask);np_imu_grid_put16(out+22U,h->ag_valid);
    np_imu_grid_put16(out+24U,h->mag_valid);np_imu_grid_put16(out+26U,h->ag_repeat);
    np_imu_grid_put16(out+28U,h->mag_repeat);np_imu_grid_put16(out+30U,h->error_mask);
    np_imu_grid_put32(out+32U,(uint32_t)(row_count*36U));np_imu_grid_put32(out+36U,0U);
    np_imu_grid_put32(out+40U,h->capture_generation);
    for(size_t row=0U;row<row_count;row++)for(unsigned c=0U;c<9U;c++)
        np_imu_grid_put32(out+48U+row*36U+c*4U,(uint32_t)rows[row][c]);
    np_imu_grid_put32(out+44U,np_imu_grid_crc(out,bytes));return bytes;
}
#endif
