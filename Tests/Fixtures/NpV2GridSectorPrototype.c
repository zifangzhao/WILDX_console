/* Native-only NPGRID02 candidate; no production transport is selected. */
#include <assert.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include "np_imu_grid_wire.h"

#define SECTOR 512U
#define HEADER 32U
static int encode(uint8_t *out, uint32_t generation, uint64_t preceding,
                  const uint8_t *frame, size_t n)
{
    NpImuGridView view;
    if (!out || !generation || !frame || n > SECTOR-HEADER ||
        !np_imu_grid_read(frame,n,&view) || view.header.capture_generation != generation)
        return 0;
    memset(out,0,SECTOR);
    memcpy(out,"NPGRID02",8);
    np_imu_grid_put32(out+8,1U);
    np_imu_grid_put32(out+12,generation);
    np_imu_grid_put64(out+16,preceding);
    np_imu_grid_put16(out+24,(uint16_t)n);
    np_imu_grid_put32(out+28,np_imu_grid_crc(out,28));
    memcpy(out+HEADER,frame,n);
    return 1;
}
static int decode(const uint8_t *sector, size_t bytes, uint32_t generation,
                  uint64_t preceding, NpImuGridView *view)
{
    NpImuGridView v;
    size_t n;
    if (!sector || bytes != SECTOR || !generation ||
        memcmp(sector,"NPGRID02",8) || np_imu_grid_u32(sector+8)!=1U ||
        np_imu_grid_u32(sector+12)!=generation ||
        np_imu_grid_u64(sector+16)!=preceding || np_imu_grid_u16(sector+26)!=0U ||
        np_imu_grid_u32(sector+28)!=np_imu_grid_crc(sector,28)) return 0;
    n=np_imu_grid_u16(sector+24);
    if (n>SECTOR-HEADER || !np_imu_grid_read(sector+HEADER,n,&v) ||
        v.header.capture_generation!=generation) return 0;
    for (size_t i=HEADER+n;i<SECTOR;i++) if (sector[i]) return 0;
    if(view)*view=v;
    return 1;
}
static void refresh_header_crc(uint8_t *sector)
{ np_imu_grid_put32(sector+28,np_imu_grid_crc(sector,28)); }
int main(void)
{
    uint8_t frames[2][480], sectors[2][512], bad[512];
    int32_t rows[12][9];
    NpImuGridHeader h={0};
    NpImuGridView v;
    size_t sizes[2];
    for(unsigned i=0;i<12;i++) for(unsigned c=0;c<9;c++) rows[i][c]=(int32_t)(i*10+c);
    h.capture_generation=42; h.slot_count=12; h.observed_mask=0x0fff;
    h.ag_valid=h.mag_valid=0x0fff;
    sizes[0]=np_imu_grid_pack(frames[0],sizeof frames[0],&h,rows,12);
    assert(sizes[0]==480 && encode(sectors[0],42,7,frames[0],sizes[0]));
    h.first_slot=12; h.slot_count=1; h.observed_mask=1; h.ag_valid=h.mag_valid=1;
    sizes[1]=np_imu_grid_pack(frames[1],sizeof frames[1],&h,rows,1);
    assert(sizes[1]==84 && encode(sectors[1],42,7,frames[1],sizes[1]));
    for(unsigned i=0;i<2;i++) {
        assert(decode(sectors[i],512,42,7,&v));
        assert(v.header.first_slot==(i?12U:0U));
        assert(!decode(sectors[i],511,42,7,0));
        assert(!decode(sectors[i],513,42,7,0));
        assert(!decode(sectors[i],512,41,7,0));
        assert(!decode(sectors[i],512,42,8,0));
    }
    memcpy(bad,sectors[1],512); bad[16]^=1; assert(!decode(bad,512,42,7,0));
    memcpy(bad,sectors[1],512); bad[40]^=1; assert(!decode(bad,512,42,7,0));
    memcpy(bad,sectors[1],512); bad[511]=1; assert(!decode(bad,512,42,7,0));
    memcpy(bad,sectors[1],512); np_imu_grid_put16(bad+26,1);refresh_header_crc(bad);
    assert(!decode(bad,512,42,7,0));
    memcpy(bad,sectors[1],512); np_imu_grid_put16(bad+24,481);refresh_header_crc(bad);
    assert(!decode(bad,512,42,7,0));
    memcpy(bad,sectors[1],512); np_imu_grid_put32(bad+12,43);refresh_header_crc(bad);
    assert(!decode(bad,512,43,7,0)); /* inner generation still 42 */
    assert(!encode(bad,43,7,frames[1],sizes[1]));
    /* Capacity for each 1/2/3-probe profile at 7.5/15/20/30 kHz.
       A single NP block spans ticks/(rate*1600) seconds. Verify two
       sectors/block cover a worst-case integral slot boundary. This is a
       capacity bound only, not a scheduling or sustained-I/O proof. */
    { const uint32_t ticks[]={122880,46080,40960}, rates[]={7500,15000,20000,30000};
      for(unsigned p=0;p<3;p++) for(unsigned r=0;r<4;r++) {
        uint64_t num=(uint64_t)ticks[p]*1250, den=(uint64_t)rates[r]*1600;
        uint64_t slots=(num+den-1)/den;
        uint64_t sectors_needed=(slots+11)/12;
        assert(sectors_needed<=2);
      }
    }
    puts("NPGRID02 native candidate: 2 same-block sectors, CRC/generation/length/padding corruption checks and 12 profile capacity bounds passed");
    return 0;
}
