#include <stdio.h>
#include "np_imu_grid_wire.h"

#define CHECK(x) do {checks++;if(!(x)){fprintf(stderr,"NSG1 C failure line%d\n",__LINE__);return 1;}} while(0)
int main(int argc,char **argv)
{
    FILE *input=NULL,*output=NULL;uint8_t prefix[8],raw[481],packed[480],before[480];
    int32_t rows[12][9],sample[9];unsigned checks=0U,vectors=0U;
    if(argc!=3 || fopen_s(&input,argv[1],"rb") || fopen_s(&output,argv[2],"wb"))return 2;
    while(fread(prefix,1U,8U,input)==8U){
        uint32_t valid=np_imu_grid_u32(prefix),bytes=np_imu_grid_u32(prefix+4U);NpImuGridView view;
        unsigned count=0U;size_t used;
        CHECK(bytes<=sizeof(raw)&&fread(raw,1U,bytes,input)==bytes);memset(&view,0xa5,sizeof(view));
        CHECK(np_imu_grid_read(raw,bytes,&view)==(int)valid);vectors++;
        if(!valid){CHECK(!view.header.capture_generation&&!view.rows&&!view.payload_bytes);continue;}
        for(unsigned slot=0U;slot<view.header.slot_count;slot++){
            int observed=(view.header.observed_mask&(1U<<slot))!=0U;
            memset(sample,0xa5,sizeof(sample));CHECK(np_imu_grid_row(&view,slot,sample)==observed);
            if(observed){memcpy(rows[count++],sample,36U);}
            else for(unsigned c=0U;c<9U;c++)CHECK(sample[c]==0);
        }
        CHECK(!np_imu_grid_row(&view,view.header.slot_count,sample));
        used=np_imu_grid_pack(packed,sizeof(packed),&view.header,count?rows:NULL,count);
        CHECK(used==bytes&&!memcmp(packed,raw,bytes));
        np_imu_grid_put32(prefix,(uint32_t)used);CHECK(fwrite(prefix,1U,4U,output)==4U&&fwrite(packed,1U,used,output)==used);
        memset(packed,0xa5,sizeof(packed));memcpy(before,packed,sizeof(before));
        CHECK(!np_imu_grid_pack(packed,bytes-1U,&view.header,count?rows:NULL,count)&&!memcmp(packed,before,sizeof(packed)));
        CHECK(!np_imu_grid_pack(packed,sizeof(packed),&view.header,rows,count+1U)&&!memcmp(packed,before,sizeof(packed)));
        {NpImuGridHeader h=view.header;h.capture_generation=0U;CHECK(!np_imu_grid_pack(packed,sizeof(packed),&h,rows,count)&&!memcmp(packed,before,sizeof(packed)));}
        if(count)CHECK(!np_imu_grid_pack(packed,sizeof(packed),&view.header,NULL,count)&&!memcmp(packed,before,sizeof(packed)));
    }
    {NpImuGridHeader h={0};NpImuGridView view;h.slot_count=1U;h.capture_generation=1U;
     CHECK(np_imu_grid_pack(packed,sizeof(packed),&h,NULL,0U)==48U);
     CHECK(np_imu_grid_read(packed,48U,&view)&&!view.header.observed_mask&&view.payload_bytes==0U);
     h.first_slot=UINT64_MAX;CHECK(!np_imu_grid_pack(packed,sizeof(packed),&h,NULL,0U));
     h.first_slot=UINT64_MAX-1U;CHECK(np_imu_grid_pack(packed,sizeof(packed),&h,NULL,0U)==48U);
     h.observed_mask=1U;memcpy(before,packed,sizeof(before));
     CHECK(!np_imu_grid_pack(packed,sizeof(packed),&h,(const int32_t (*)[9])(packed+48U),1U)&&!memcmp(before,packed,sizeof(before)));}
    CHECK(!np_imu_grid_read(NULL,48U,NULL)&&NP_IMU_GRID_MAX_BYTES==480U);
    fclose(input);fclose(output);printf("PASS %u native NSG1 checks; %u C/C# vectors\n",checks,vectors);return 0;
}
