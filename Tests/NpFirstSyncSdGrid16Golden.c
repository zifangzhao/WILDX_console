#include <stdio.h>
#include "np_first_sync_sd.h"
int main(int argc,char **argv){
 const uint32_t uid[3]={0x001a0004U,0x43425006U,0x2039314eU};const uint64_t token=UINT64_C(0x1020304050607080);
 FILE *in=NULL,*out=NULL;uint8_t h[8],b[128],legacy[64]={0},q[64],a[64],old[64];unsigned checks=0;NpFirstSdReply r={0};
 if(argc!=3||fopen_s(&in,argv[1],"rb")||fopen_s(&out,argv[2],"wb"))return 2;
 while(fread(h,1,8,in)==8){uint32_t n=np_first_u32(h+4);int ok;if(n>128||fread(b,1,n,in)!=n)return 3;ok=h[0]==0?np_first_sd_grid16_request_valid(b,n,h[1],uid):n==128&&np_first_sd_grid16_reply_valid(b,64,b+64,h[1]);if(ok!=(int)h[2]){fprintf(stderr,"v4 parity mismatch %u\n",checks);return 4;}checks++;}fclose(in);
 np_first_put32(legacy,0x32444c4eU);np_first_put32(legacy+4,2);np_first_put32(legacy+8,2);np_first_put32(legacy+12,57);np_first_put32(legacy+16,7);np_first_put32(legacy+20,20000);np_first_put32(legacy+24,12345);np_first_put32(legacy+60,np_first_crc(legacy,60));
 if(!np_first_sd_grid16_wrap(q,legacy,2,uid,token,0)||np_first_sd_fine_counter64_request_valid(q,64,2,uid)||np_first_sd_fine_aux_request_valid(q,64,2,uid)||np_first_sd_request_valid(q,64,2,uid)||np_first_sd_grid16_request_valid(q,64,1,uid))return 5;
 if(!np_first_sd_grid16_unwrap(old,q,2,uid)||memcmp(old,legacy,64)||!np_first_sd_grid16_fresh_start(q,64,2,uid,0)||np_first_sd_grid16_fresh_start(q,64,2,uid,token)||!np_first_sd_grid16_cached_duplicate(q,q,2,uid))return 6;
 r.supported=29;r.record_features=25;r.session=100;r.generation=9;r.source_epoch=11;r.profile_crc=UINT32_MAX;
 if(!np_first_sd_grid16_reply_pack(a,q,2,&r)||np_first_u16(a+4)!=4||np_first_u32(a+40)!=25||!np_first_sd_uid_same(a+12,uid)||np_first_u64(a+24)!=token||np_first_sd_fine_counter64_reply_valid(a,64,q,2))return 7;
 if(fwrite(q,1,64,out)!=64||fwrite(a,1,64,out)!=64)return 8;fclose(out);printf("PASS %u C v4 parity vectors; actual request4/reply25 UID/token golden\n",checks);return 0;
}
