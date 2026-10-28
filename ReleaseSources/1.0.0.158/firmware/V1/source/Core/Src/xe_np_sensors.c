#include "xe_np_sensors.h"
#include <math.h>
#include <stddef.h>
#include <string.h>

#define IO_DEADLINE_MS 20U
#define RETRY_MS 2000U
#define BMX_ID 0xd8U
#define BME_ID 0x60U
#define MAG_BAD_SAMPLE_LIMIT 3U /* reject isolated samples; stop a failed stream */
enum { OFF, IDENTIFY, RESET_SENSOR, CONFIGURE, POLL_STATUS, READ_SAMPLE,
       BME_NVM, BME_CAL1, BME_CAL2, BME_CONFIG, BME_TRIGGER, BME_MEASURING,
       BME_READ, FAILED, AUX_WAIT, AG_CONFIG, AG_POLL_STATUS, AG_READ_SAMPLE };
enum { WRITE_REG, EXPECT_REG, MAG_TRIM1, MAG_TRIM2, MAG_TRIM3, MAG_TRIM4, VERIFY_CONFIG, EXPECT_PMU };
typedef struct { uint8_t reg,value,length,wait_ms,action; } SensorStep;
/* BMX160 (not BMI270): register 0x4c is MAG_IF_0. Internal BMM150 access
 * is indirect. Each write and its settling delay is a separate async step. */
static const SensorStep bmx_steps[] = {
    {0x00,BMX_ID,1,0,EXPECT_REG},
    {0x7e,0x11,1,5,WRITE_REG}, {0x7e,0x15,1,85,WRITE_REG},
    {0x7e,0x19,1,2,WRITE_REG},
    {0x40,0x29,1,1,WRITE_REG}, {0x41,0x08,1,1,WRITE_REG},
    {0x42,0x29,1,1,WRITE_REG}, {0x43,0x00,1,1,WRITE_REG},
    {0x44,0x08,1,1,WRITE_REG}, /* WILD normal: 200 Hz accel/gyro, 100 Hz mag. */
    {0x03,0x15,1,0,EXPECT_PMU},
    {0x4c,0x83,1,10,WRITE_REG}, /* manual, eight-byte shadow burst */
    {0x4c,0x83,1,0,EXPECT_REG},
    {0x4f,0x01,1,1,WRITE_REG}, {0x4e,0x4b,1,10,WRITE_REG},
    {0x4d,0x40,1,10,WRITE_REG}, {0x04,0x32,1,0,EXPECT_REG},
    {0x4d,0x5d,1,10,WRITE_REG}, {0x04,0,8,0,MAG_TRIM1},
    {0x4d,0x62,1,10,WRITE_REG}, {0x04,0,8,0,MAG_TRIM2},
    {0x4d,0x68,1,10,WRITE_REG}, {0x04,0,8,0,MAG_TRIM3},
    {0x4d,0x70,1,10,WRITE_REG}, {0x04,0,8,0,MAG_TRIM4},
    {0x4f,0x04,1,1,WRITE_REG}, {0x4e,0x51,1,10,WRITE_REG},
    {0x4f,0x0e,1,1,WRITE_REG}, {0x4e,0x52,1,10,WRITE_REG},
    {0x4f,0x02,1,1,WRITE_REG}, {0x4e,0x4c,1,10,WRITE_REG},
    {0x4d,0x42,1,10,WRITE_REG}, {0x4c,0x03,1,45,WRITE_REG},
    {0x03,0x15,1,0,EXPECT_PMU}, {0x02,0x00,1,0,EXPECT_REG},
    {0x40,0,5,0,VERIFY_CONFIG}
};
static const SensorStep bme_steps[] = {
    {0xf2,1,1,0,WRITE_REG}, /* ctrl_hum must precede ctrl_meas */
    {0xf5,0,1,0,WRITE_REG}, {0xf4,0x24,1,0,WRITE_REG},
    {0xf2,1,1,0,EXPECT_REG}, {0xf4,0x24,1,0,EXPECT_REG},
    {0xf5,0,1,0,EXPECT_REG}
};
static const SensorStep ag_only_steps[] = {
    {0x7e,0x18,1,2,WRITE_REG}, /* stop failed magnetic interface, preserve AG */
    {0x03,0x14,1,0,EXPECT_PMU}, {0x40,0,5,0,VERIFY_CONFIG}
};
static void fault_detail(XE_NP_Sensors *s,uint8_t bus,uint32_t error,uint32_t now,
                         uint8_t reg,uint8_t actual,uint8_t expected,uint8_t mask)
{
    XE_NP_SensorFault *f=&s->fault[bus];XE_NP_SensorDevice *d=&s->device[bus];
    f->count++;f->tick_ms=now;f->error=error;f->state=d->state;f->step=d->step;
    f->reg=reg;f->actual=actual;f->expected=expected;f->mask=mask;
    f->io_error=d->completion_error;
}
/* Bosch BST-BMX160-DS000-14, sections 2.11.3 and 2.11.11-18.
 * Reserved bits are not configuration fields. In particular PMU_STATUS's
 * bit 7 is shown as reset=1 in the table; 0x95 is a valid normal-mode value.
 * ERR_REG remains conservative/full-byte checked (datasheet revisions differ). */
static uint8_t register_mask(uint8_t bus,uint8_t reg)
{
    if(bus)return 0xffU;
    switch(reg) {
    case 0x03:return 0x3fU;
    case 0x41:return 0x0fU;
    case 0x43:return 0x07U;
    case 0x44:return 0x0fU;
    case 0x4c:return 0xbfU;
    default:return 0xffU;
    }
}
static void mag_unavailable(XE_NP_Sensors *s,uint32_t now)
{
    s->mag_calibrated=0;s->mag_bad_samples=0;s->snapshot.valid&=~XE_NP_SENSOR_VALID_MAG;
    memset(s->snapshot.mag_nano_tesla,0,sizeof(s->snapshot.mag_nano_tesla));
    s->snapshot.error[0]=XE_NP_SENSOR_ERR_MAG_UNAVAILABLE;
    /* A configured AG stream stays usable. Defer the magnetic suspend and
     * verification until idle, without resetting or stopping AG polling. */
    s->device[0].step=0;s->device[0].state=s->device[0].configured?AG_POLL_STATUS:AG_CONFIG;
    s->device[0].next_due=now;
    s->device[0].phase_deadline=now+100U;
}
static int reached(uint32_t now,uint32_t deadline) { return (int32_t)(now-deadline)>=0; }
static uint16_t u16(const uint8_t *p) { return (uint16_t)((uint16_t)p[0]|((uint16_t)p[1]<<8)); }
static int16_t s16(const uint8_t *p) { uint16_t v=u16(p);return (int16_t)((v&0x8000U)?(int32_t)v-65536:(int32_t)v); }
static int16_t signed12(uint16_t v) { return (int16_t)((v&0x800U)?(int32_t)v-4096:(int32_t)v); }
static int blank(const uint8_t *p,uint32_t n)
{ uint32_t zero=0,ones=0;for(uint32_t i=0;i<n;i++) { zero+=p[i]==0;ones+=p[i]==255; }return zero==n||ones==n; }
/* Compensation never performs I/O. Bosch's aux decoder checks these pointers
 * even though it only uses trim_data; fail if an unexpected bus call is added. */
static int8_t no_read(uint8_t a,uint8_t *p,uint32_t n,void *ctx) { (void)a;(void)p;(void)n;(void)ctx;return -1; }
static int8_t no_write(uint8_t a,const uint8_t *p,uint32_t n,void *ctx) { (void)a;(void)p;(void)n;(void)ctx;return -1; }
static void no_delay(uint32_t n,void *ctx) { (void)n;(void)ctx; }

int XE_NP_Sensors_ParseBmeCalibration(const uint8_t *a,const uint8_t *h,struct bme280_calib_data *c)
{
    if(!a||!h||!c||blank(a,26)||blank(h,7)||u16(a)==0||u16(a+6)==0||u16(a)==65535||u16(a+6)==65535)return -1;
    memset(c,0,sizeof(*c));
    c->dig_t1=u16(a);c->dig_t2=s16(a+2);c->dig_t3=s16(a+4);
    c->dig_p1=u16(a+6);c->dig_p2=s16(a+8);c->dig_p3=s16(a+10);
    c->dig_p4=s16(a+12);c->dig_p5=s16(a+14);c->dig_p6=s16(a+16);
    c->dig_p7=s16(a+18);c->dig_p8=s16(a+20);c->dig_p9=s16(a+22);
    c->dig_h1=a[25];c->dig_h2=s16(h);c->dig_h3=h[2];
    c->dig_h4=signed12((uint16_t)((uint16_t)h[3]*16U+(h[4]&15U)));
    c->dig_h5=signed12((uint16_t)((uint16_t)h[5]*16U+(h[4]>>4)));
    c->dig_h6=(int8_t)h[6];return 0;
}
int XE_NP_Sensors_DecodeBme(const uint8_t *p,struct bme280_calib_data *c,XE_NP_SensorSnapshot *out)
{
    struct bme280_uncomp_data raw;struct bme280_data value;
    if(!p||!c||!out||!c->dig_p1||!c->dig_t1)return -1;
    raw.pressure=((uint32_t)p[0]<<12)|((uint32_t)p[1]<<4)|(p[2]>>4);
    raw.temperature=((uint32_t)p[3]<<12)|((uint32_t)p[4]<<4)|(p[5]>>4);
    raw.humidity=((uint32_t)p[6]<<8)|p[7];
    if(raw.pressure==0x80000U||raw.temperature==0x80000U||raw.humidity==0x8000U)return -1;
    if(bme280_compensate_data(BME280_ALL,&raw,&value,c)!=BME280_OK ||
       !isfinite(value.temperature)||!isfinite(value.pressure)||!isfinite(value.humidity))return -1;
    out->temperature_centi_c=(int32_t)(value.temperature*100.0+(value.temperature>=0?0.5:-0.5));
    out->pressure_pa=(uint32_t)(value.pressure+0.5);
    out->humidity_milli_percent=(uint32_t)(value.humidity*1000.0+0.5);return 0;
}
static int parse_mag_trim(XE_NP_Sensors *s)
{
    const uint8_t *a=s->device[0].calibration,*b=a+2,*c=b+4;
    struct bmm150_trim_registers *t=&s->magnetometer.trim_data;
    if(blank(a,16))return -1;
    t->dig_x1=(int8_t)a[0];t->dig_y1=(int8_t)a[1];
    t->dig_z4=s16(b);t->dig_x2=(int8_t)b[2];t->dig_y2=(int8_t)b[3];
    t->dig_z2=s16(c);t->dig_z1=u16(c+2);t->dig_xyz1=(uint16_t)(u16(c+4)&0x7fffU);
    t->dig_z3=s16(c+6);t->dig_xy2=(int8_t)c[8];t->dig_xy1=c[9];
    if(!t->dig_z1||!t->dig_z2||!t->dig_xyz1||t->dig_z1==65535U)return -1;
    s->mag_calibrated=1;s->mag_bad_samples=0;return 0;
}
int XE_NP_Sensors_DecodeImu(XE_NP_Sensors *s,const uint8_t *p,uint8_t status,uint32_t now)
{
    if(!s||!p)return -1;
    XE_NP_SensorSnapshot *out=&s->snapshot;
    uint32_t time=(uint32_t)p[20]|((uint32_t)p[21]<<8)|((uint32_t)p[22]<<16);
    uint32_t delta=(time-s->previous_sensor_time)&0xffffffU;
    if(s->have_sensor_time && (!delta||delta>=0x800000U))return -1;
    if(s->have_sensor_time && delta>=256U)out->missed_imu_samples+=delta/128U-1U;
    s->previous_sensor_time=time;s->have_sensor_time=1;out->sensor_time_24=time;
    for(uint32_t i=0;i<3;i++) {
        out->accel_micro_g[i]=(int32_t)((int64_t)s16(p+14+i*2)*8000000/32768);
        out->gyro_milli_dps[i]=(int32_t)((int64_t)s16(p+8+i*2)*10000/164);
    }
    out->valid|=XE_NP_SENSOR_VALID_AG;out->imu_sequence++;out->imu_tick_ms=now;
    out->enabled|=XE_NP_SENSOR_MASK_BMX;
    if(status&0x20U) {
        uint8_t mag[8];struct bmm150_mag_data value;
        int16_t x=(int16_t)((int16_t)(int8_t)p[1]*32+(p[0]>>3));
        int16_t y=(int16_t)((int16_t)(int8_t)p[3]*32+(p[2]>>3));
        int16_t z=(int16_t)((int16_t)(int8_t)p[5]*128+(p[4]>>1));
        uint16_t hall=(uint16_t)(((uint16_t)p[7]<<6)|(p[6]>>2));
        memcpy(mag,p,8); /* Bosch API modifies its input; never touch raw bytes. */
        out->valid&=~XE_NP_SENSOR_VALID_MAG;
        if(s->mag_calibrated && (p[6]&1U) && hall && x!=-4096 && y!=-4096 && z!=-16384 &&
           bmm150_aux_mag_data(mag,&value,&s->magnetometer)==BMM150_OK &&
           isfinite(value.x)&&isfinite(value.y)&&isfinite(value.z)&&
           fabsf(value.x)<10000.0f&&fabsf(value.y)<10000.0f&&fabsf(value.z)<10000.0f) {
            out->mag_nano_tesla[0]=(int32_t)(value.x*1000.0f);
            out->mag_nano_tesla[1]=(int32_t)(value.y*1000.0f);
            out->mag_nano_tesla[2]=(int32_t)(value.z*1000.0f);
            out->mag_sequence++;out->mag_tick_ms=now;out->valid|=XE_NP_SENSOR_VALID_MAG;
            s->mag_bad_samples=0;
        } else {
            out->invalid_mag_samples++;
            memset(out->mag_nano_tesla,0,sizeof(out->mag_nano_tesla));
            /* An overflow/not-ready conversion is not a failed sensor. Keep
             * its verified trim and normal polling; no reset, retry I/O or
             * invented magnetic sample. Three consecutive bad conversions
             * take the existing six-axis fallback in the foreground. */
            if(s->mag_calibrated && ++s->mag_bad_samples>=MAG_BAD_SAMPLE_LIMIT) {
                s->mag_calibrated=0U;out->error[0]=XE_NP_SENSOR_ERR_MAG_UNAVAILABLE;
            }
        }
    }
    return 0;
}
static void fail(XE_NP_Sensors *s,uint8_t bus,uint32_t error,uint32_t now)
{
    XE_NP_SensorDevice *d=&s->device[bus];
    if(error!=XE_NP_SENSOR_ERR_CONFIG)
        fault_detail(s,bus,error,now,d->last_reg,d->data[0],0,0);
    s->io.stop(s->io.context,bus);d->pending=0;d->completion=0;d->token++;
    d->state=FAILED;d->configured=0;d->next_due=now+RETRY_MS;
    s->snapshot.error[bus]=error;
    s->snapshot.enabled&=~(1U<<bus);
    s->snapshot.valid&=bus==0?~(XE_NP_SENSOR_VALID_AG|XE_NP_SENSOR_VALID_MAG):~XE_NP_SENSOR_VALID_ENV;
    if(bus==XE_NP_SENSOR_BMX) {
        memset(s->snapshot.accel_micro_g,0,sizeof(s->snapshot.accel_micro_g));
        memset(s->snapshot.gyro_milli_dps,0,sizeof(s->snapshot.gyro_milli_dps));
        memset(s->snapshot.mag_nano_tesla,0,sizeof(s->snapshot.mag_nano_tesla));
    } else {
        s->snapshot.temperature_centi_c=0;
        s->snapshot.pressure_pa=s->snapshot.humidity_milli_percent=0U;
    }
    if(error==XE_NP_SENSOR_ERR_TIMEOUT)s->snapshot.timeouts[bus]++;
    if(error==XE_NP_SENSOR_ERR_IO||error==XE_NP_SENSOR_ERR_BUSY)s->snapshot.io_errors[bus]++;
}
static void begin(XE_NP_Sensors *s,uint8_t bus,uint8_t reg,uint8_t length,uint8_t write,uint8_t value,uint32_t now)
{
    XE_NP_SensorDevice *d=&s->device[bus];int result;
    d->last_reg=reg;d->completion_error=0;
    if(write)d->data[0]=value;
    d->token++;d->deadline=now+IO_DEADLINE_MS;d->completion=0;d->pending=1;
    result=s->io.start(s->io.context,bus,d->address,reg,d->data,length,write,d->token);
    if(result!=0)fail(s,bus,result>0?XE_NP_SENSOR_ERR_BUSY:XE_NP_SENSOR_ERR_IO,now);
}
void XE_NP_Sensors_Complete(XE_NP_Sensors *s,uint8_t bus,uint32_t token,uint32_t error,uint32_t now)
{
    XE_NP_SensorDevice *d;
    if(!s||bus>=2)return;
    d=&s->device[bus];
    if(!d->pending||token!=d->token||d->completion) { s->snapshot.ignored_completions++;return; }
    d->completed_token=token;d->completion_error=error;d->completion_tick=now;
    d->completion=1; /* Last store; target adapter supplies a memory barrier. */
}
void XE_NP_Sensors_Init(XE_NP_Sensors *s,const XE_NP_SensorIo *io,uint32_t enabled,uint32_t now)
{
    if(!s||!io||!io->start||!io->stop||!io->recover)return;
    memset(s,0,sizeof(*s));s->io=*io;
    s->snapshot.magic=0x31534e53UL;s->snapshot.version=1;
    s->magnetometer.read=no_read;s->magnetometer.write=no_write;s->magnetometer.delay_us=no_delay;
    s->magnetometer.intf_ptr=s;
    for(uint8_t i=0;i<2;i++) {
        XE_NP_SensorDevice *d=&s->device[i];d->enabled=(uint8_t)((enabled>>i)&1U);
        d->state=d->enabled?IDENTIFY:OFF;d->next_due=now+20U;d->address=i?0x76U:0x68U;
        s->snapshot.error[i]=d->enabled?0:XE_NP_SENSOR_ERR_DISABLED;
    }
}
static void completed(XE_NP_Sensors *s,uint8_t bus,uint32_t now)
{
    XE_NP_SensorDevice *d=&s->device[bus];const SensorStep *step;
    d->next_due=now;
    switch(d->state) {
    case IDENTIFY:
        s->snapshot.chip_id[bus]=d->data[0];
        if(d->data[0]!=(bus?BME_ID:BMX_ID)) { fail(s,bus,XE_NP_SENSOR_ERR_ID,now);return; }
        d->state=RESET_SENSOR;break;
    case RESET_SENSOR:
        d->step=0;d->state=bus?BME_NVM:CONFIGURE;d->next_due=now+(bus?3U:20U);
        d->phase_deadline=now+100U;if(!bus)s->have_sensor_time=0;break;
    case CONFIGURE:
        step=&bmx_steps[d->step];
        if((step->action==EXPECT_REG || step->action==EXPECT_PMU) &&
           (d->data[0]&register_mask(bus,step->reg))!=(step->value&register_mask(bus,step->reg))) {
            if(step->action==EXPECT_PMU && !reached(now,d->phase_deadline)) { d->next_due=now+1U;return; }
            /* Allow a bounded settling interval after enabling autonomous
             * magnetic reads. A persistent MAG-only DRDY fault still takes
             * the explicit AG fallback below; no error is blindly masked. */
            if(step->reg==0x02 && d->data[0]==0x80U && !reached(now,d->phase_deadline)) {
                d->next_due=now+40U;return;
            }
            fault_detail(s,bus,XE_NP_SENSOR_ERR_CONFIG,now,step->reg,d->data[0],step->value,register_mask(bus,step->reg));
            /* BMX160 ERR_REG bit 7 is mag_drdy_err, not an AG configuration
             * failure (Bosch DS 2.4.3.1.2 / register 0x02). Preserve the exact
             * diagnostic and take the explicit six-axis fallback. Never mask
             * any combined fatal, command, I2C or configuration error bits. */
            if(step->reg==0x04 || (step->reg==0x02 && d->data[0]==0x80U))mag_unavailable(s,now);
            else fail(s,bus,XE_NP_SENSOR_ERR_CONFIG,now);
            return;
        }
        if(step->action==MAG_TRIM1)memcpy(d->calibration,d->data,2);
        if(step->action==MAG_TRIM2)memcpy(d->calibration+2,d->data,4);
        if(step->action==MAG_TRIM3)memcpy(d->calibration+6,d->data,8);
        if(step->action==MAG_TRIM4) {
            memcpy(d->calibration+14,d->data,2);
            if(parse_mag_trim(s)!=0) {
                fault_detail(s,bus,XE_NP_SENSOR_ERR_CALIBRATION,now,step->reg,d->data[0],0,0);
                mag_unavailable(s,now);return;
            }
        }
        if(step->action==VERIFY_CONFIG) {
            static const uint8_t expected[5]={0x29,0x08,0x29,0x00,0x08};
            for(uint8_t i=0;i<5;i++) {
                uint8_t reg=(uint8_t)(0x40U+i),mask=register_mask(bus,reg);
                if((d->data[i]&mask)!=(expected[i]&mask)) {
                    fault_detail(s,bus,XE_NP_SENSOR_ERR_CONFIG,now,reg,d->data[i],expected[i],mask);
                    fail(s,bus,XE_NP_SENSOR_ERR_CONFIG,now);return;
                }
            }
        }
        d->next_due=now+step->wait_ms;
        d->phase_deadline=now+100U;
        if(++d->step==sizeof(bmx_steps)/sizeof(bmx_steps[0])) { d->configured=1;d->state=POLL_STATUS;s->snapshot.error[bus]=0; }
        else if(step->action==WRITE_REG && (step->reg==0x4d || step->reg==0x4e)) {
            /* MAG_IF writes launch an INTERNAL transfer. Host I2C completion
             * is not its completion. Poll mag_man_op with a bounded deadline
             * before another indirect command or shadow-data read (DS 2.4.3.1.1). */
            d->state=AUX_WAIT;d->phase_deadline=now+100U;
        }
        break;
    case AUX_WAIT:
        if(d->data[0]&4U) {
            if(reached(now,d->phase_deadline))fail(s,bus,XE_NP_SENSOR_ERR_TIMEOUT,now);
            else d->next_due=now+1U;
        } else d->state=CONFIGURE;
        break;
    case AG_CONFIG:
        step=&ag_only_steps[d->step];
        if(step->action==EXPECT_PMU && (d->data[0]&0x3fU)!=step->value) {
            if(!reached(now,d->phase_deadline)) { d->next_due=now+1U;return; }
            fault_detail(s,bus,XE_NP_SENSOR_ERR_CONFIG,now,step->reg,d->data[0],step->value,0x3fU);
            fail(s,bus,XE_NP_SENSOR_ERR_CONFIG,now);return;
        }
        if(step->action==VERIFY_CONFIG) {
            static const uint8_t expected[4]={0x29,0x08,0x29,0x00};
            for(uint8_t i=0;i<4;i++) {
                uint8_t reg=(uint8_t)(0x40U+i),mask=register_mask(bus,reg);
                if((d->data[i]&mask)!=(expected[i]&mask)) {
                    fault_detail(s,bus,XE_NP_SENSOR_ERR_CONFIG,now,reg,d->data[i],expected[i],mask);
                    fail(s,bus,XE_NP_SENSOR_ERR_CONFIG,now);return;
                }
            }
        }
        d->next_due=now+step->wait_ms;
        d->phase_deadline=now+100U;
        if(++d->step==sizeof(ag_only_steps)/sizeof(ag_only_steps[0])) { d->configured=1;d->state=POLL_STATUS; }
        break;
    case POLL_STATUS:case AG_POLL_STATUS:
        d->status=d->data[0];
        if((d->status&0xc0U)==0xc0U)d->state=d->state==AG_POLL_STATUS?AG_READ_SAMPLE:READ_SAMPLE;
        else if(reached(now,d->phase_deadline))fail(s,bus,XE_NP_SENSOR_ERR_TIMEOUT,now);
        else d->next_due=now+1U;
        break;
    case READ_SAMPLE:case AG_READ_SAMPLE:
    {
        uint8_t had_mag=s->mag_calibrated;
        if(!s->mag_calibrated) { memmove(d->data+8,d->data,15);memset(d->data,0,8); }
        if(XE_NP_Sensors_DecodeImu(s,d->data,(uint8_t)(d->status & (s->mag_calibrated?0xffU:0xdfU)),now)!=0) {
            fail(s,bus,XE_NP_SENSOR_ERR_SAMPLE,now);return;
        }
        if(had_mag && !s->mag_calibrated) { mag_unavailable(s,now);return; }
        d->state=d->state==AG_READ_SAMPLE?AG_POLL_STATUS:POLL_STATUS;
        d->next_due=d->poll_started+5U;
        d->phase_deadline=now+100U;break;
    }
    case BME_NVM:
        if(d->data[0]&1U) {
            if(reached(now,d->phase_deadline))fail(s,bus,XE_NP_SENSOR_ERR_TIMEOUT,now);
            else d->next_due=now+2U;
        } else d->state=BME_CAL1;
        break;
    case BME_CAL1:memcpy(d->calibration,d->data,26);d->state=BME_CAL2;break;
    case BME_CAL2:
        if(XE_NP_Sensors_ParseBmeCalibration(d->calibration,d->data,&s->bme_calibration)!=0) {
            fail(s,bus,XE_NP_SENSOR_ERR_CALIBRATION,now);return;
        }
        d->step=0;d->state=BME_CONFIG;break;
    case BME_CONFIG:
        step=&bme_steps[d->step];
        if(step->action==EXPECT_REG && d->data[0]!=step->value) {
            fault_detail(s,bus,XE_NP_SENSOR_ERR_CONFIG,now,step->reg,d->data[0],step->value,0xffU);
            fail(s,bus,XE_NP_SENSOR_ERR_CONFIG,now);return;
        }
        if(++d->step==sizeof(bme_steps)/sizeof(bme_steps[0])) {
            d->configured=1;d->state=BME_TRIGGER;s->snapshot.error[bus]=0;
        }
        break;
    case BME_TRIGGER:d->state=BME_MEASURING;d->next_due=now+10U;d->phase_deadline=now+100U;break;
    case BME_MEASURING:
        if(d->data[0]&9U) {
            if(reached(now,d->phase_deadline))fail(s,bus,XE_NP_SENSOR_ERR_TIMEOUT,now);
            else d->next_due=now+2U;
        } else d->state=BME_READ;
        break;
    case BME_READ:
        if(XE_NP_Sensors_DecodeBme(d->data,&s->bme_calibration,&s->snapshot)!=0) { fail(s,bus,XE_NP_SENSOR_ERR_SAMPLE,now);return; }
        s->snapshot.valid|=XE_NP_SENSOR_VALID_ENV;s->snapshot.env_sequence++;s->snapshot.env_tick_ms=now;
        s->snapshot.enabled|=XE_NP_SENSOR_MASK_BME;
        d->state=BME_TRIGGER;d->next_due=d->poll_started+1000U;break;
    default:fail(s,bus,XE_NP_SENSOR_ERR_CONFIG,now);break;
    }
}
void XE_NP_Sensors_Service(XE_NP_Sensors *s,uint32_t now,uint8_t recording)
{
    if(!s)return;
    for(uint8_t bus=0;bus<2;bus++) {
        XE_NP_SensorDevice *d=&s->device[bus];const SensorStep *step;
        if(!d->enabled)continue;
        if(d->pending) {
            if(d->completion) {
                uint32_t error=d->completion_error,tick=d->completion_tick;
                d->pending=0;d->completion=0;
                if(error) { fail(s,bus,XE_NP_SENSOR_ERR_IO,now);continue; }
                if(reached(tick,d->deadline)) { fail(s,bus,XE_NP_SENSOR_ERR_TIMEOUT,now);continue; }
                completed(s,bus,tick);
            } else if(reached(now,d->deadline)) { fail(s,bus,XE_NP_SENSOR_ERR_TIMEOUT,now);continue; }
            else continue;
        }
        /* A transfer admitted idle may retire above, but preparation/active
         * NP must never advance a partial reset/configuration or recovery. */
        if(recording && (!d->configured || d->state==AG_CONFIG))continue;
        if(!recording && (d->state==AG_POLL_STATUS || d->state==AG_READ_SAMPLE)) {
            d->step=0;d->state=AG_CONFIG;d->next_due=now;d->phase_deadline=now+100U;
        }
        if(!reached(now,d->next_due))continue;
        if(d->state==FAILED) {
            if(recording)continue; /* No resets/reinitialization during recording. */
            d->next_due=now+RETRY_MS;
            if(s->io.recover(s->io.context,bus)!=0)continue;
            /* An address is discovered by IDENTIFY, not by every later bus
             * error. Keep the verified address after configuration/sample
             * faults; otherwise one transient NACK adds a guaranteed failed
             * retry at the other address. Recovery remains stopped-only. */
            if(s->fault[bus].state==IDENTIFY &&
               (s->snapshot.error[bus]==XE_NP_SENSOR_ERR_IO || s->snapshot.error[bus]==XE_NP_SENSOR_ERR_ID))
                d->address^=1U;
            d->state=IDENTIFY;d->step=0;if(!bus)s->mag_calibrated=0;
        }
        switch(d->state) {
        case IDENTIFY:begin(s,bus,bus?0xd0:0x00,1,0,0,now);break;
        case RESET_SENSOR:begin(s,bus,bus?0xe0:0x7e,1,1,0xb6,now);break;
        case CONFIGURE:step=&bmx_steps[d->step];begin(s,bus,step->reg,step->length,step->action==WRITE_REG,step->value,now);break;
        case AUX_WAIT:begin(s,bus,0x1b,1,0,0,now);break;
        case AG_CONFIG:step=&ag_only_steps[d->step];begin(s,bus,step->reg,step->length,step->action==WRITE_REG,step->value,now);break;
        case POLL_STATUS:case AG_POLL_STATUS:d->poll_started=now;begin(s,bus,0x1b,1,0,0,now);break;
        case READ_SAMPLE:case AG_READ_SAMPLE:begin(s,bus,s->mag_calibrated?0x04:0x0c,s->mag_calibrated?23:15,0,0,now);break;
        case BME_NVM:case BME_MEASURING:begin(s,bus,0xf3,1,0,0,now);break;
        case BME_CAL1:begin(s,bus,0x88,26,0,0,now);break;
        case BME_CAL2:begin(s,bus,0xe1,7,0,0,now);break;
        case BME_CONFIG:step=&bme_steps[d->step];begin(s,bus,step->reg,step->length,step->action==WRITE_REG,step->value,now);break;
        case BME_TRIGGER:d->poll_started=now;begin(s,bus,0xf4,1,1,0x25,now);break;
        case BME_READ:begin(s,bus,0xf7,8,0,0,now);break;
        default:break;
        }
    }
}
void XE_NP_Sensors_Snapshot(const XE_NP_Sensors *s,XE_NP_SensorSnapshot *out,uint32_t now)
{
    if(!s||!out)return;
    *out=s->snapshot;XE_NP_Sensors_RefreshValidity(out,now);
}
void XE_NP_Sensors_RefreshValidity(XE_NP_SensorSnapshot *out,uint32_t now)
{
    if(!out)return;
    out->published_ms=now;
    out->valid=XE_NP_Sensors_StreamMask(out);
    if(!out->imu_sequence || (uint32_t)(now-out->imu_tick_ms)>100U)out->valid&=~XE_NP_SENSOR_VALID_AG;
    if(!out->mag_sequence || (uint32_t)(now-out->mag_tick_ms)>200U)out->valid&=~XE_NP_SENSOR_VALID_MAG;
    if(!out->env_sequence || (uint32_t)(now-out->env_tick_ms)>3000U)out->valid&=~XE_NP_SENSOR_VALID_ENV;
}
