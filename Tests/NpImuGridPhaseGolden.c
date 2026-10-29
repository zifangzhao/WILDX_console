#include <stdio.h>
#include "np_imu_grid_phase.h"
#define CHECK(x) do{checks++;if(!(x)){fprintf(stderr,"NSGP C failure line%d\n",__LINE__);return 1;}}while(0)
int main(int argc,char **argv)
{
    FILE *input=NULL,*output=NULL;uint8_t prefix[8],raw[85],packed[84],before[84];unsigned checks=0U,vectors=0U;
    if(argc!=3 || fopen_s(&input,argv[1],"rb") || fopen_s(&output,argv[2],"wb"))return 2;
    while(fread(prefix,1U,8U,input)==8U){
        uint32_t valid=np_imu_grid_phase_u32(prefix),bytes=np_imu_grid_phase_u32(prefix+4U);NpImuGridPhaseRecord p;
        CHECK(bytes<=sizeof(raw)&&fread(raw,1U,bytes,input)==bytes);memset(&p,0xa5,sizeof(p));
        CHECK(np_imu_grid_phase_read(raw,bytes,&p)==(int)valid);vectors++;
        if(!valid){CHECK(!p.binding.session&&!p.flags);continue;}
        CHECK(np_imu_grid_phase_match(raw,bytes,&p.binding));
        {NpImuGridPhaseBinding e=p.binding;CHECK(!np_imu_grid_phase_match(raw,bytes,NULL));
         e.uid[0]^=1U;CHECK(!np_imu_grid_phase_match(raw,bytes,&e));e=p.binding;
         e.session++;CHECK(!np_imu_grid_phase_match(raw,bytes,&e));e=p.binding;
         e.capture_generation++;CHECK(!np_imu_grid_phase_match(raw,bytes,&e));e=p.binding;
         e.source_epoch++;CHECK(!np_imu_grid_phase_match(raw,bytes,&e));e=p.binding;
         e.counter_domain++;CHECK(!np_imu_grid_phase_match(raw,bytes,&e));e=p.binding;
         e.token++;CHECK(!np_imu_grid_phase_match(raw,bytes,&e));}
        CHECK(np_imu_grid_phase_pack(packed,sizeof(packed),&p)&&!memcmp(raw,packed,84U));
        np_imu_grid_phase_put32(prefix,84U);CHECK(fwrite(prefix,1U,4U,output)==4U&&fwrite(packed,1U,84U,output)==84U);
        memset(packed,0xa5,sizeof(packed));memcpy(before,packed,sizeof(before));
        CHECK(!np_imu_grid_phase_pack(packed,83U,&p)&&!memcmp(packed,before,sizeof(before)));
        p.first_accepted_slot=UINT64_MAX;CHECK(!np_imu_grid_phase_pack(packed,84U,&p)&&!memcmp(packed,before,sizeof(before)));
    }
    CHECK(!np_imu_grid_phase_read(NULL,84U,NULL)&&!np_imu_grid_phase_pack(NULL,84U,NULL));
    fclose(input);fclose(output);printf("PASS %u native NSGP checks; %u C/C# vectors\n",checks,vectors);return 0;
}
