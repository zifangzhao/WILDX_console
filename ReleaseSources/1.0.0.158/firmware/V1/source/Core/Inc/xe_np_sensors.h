#ifndef XE_NP_SENSORS_H
#define XE_NP_SENSORS_H
#include <stdint.h>
#ifndef BME280_DOUBLE_ENABLE
#define BME280_DOUBLE_ENABLE
#endif
#ifndef BMM150_USE_FLOATING_POINT
#define BMM150_USE_FLOATING_POINT
#endif
#include "bme280.h"
#include "bmm150.h"

#define XE_NP_SENSOR_BMX 0U
#define XE_NP_SENSOR_BME 1U
#define XE_NP_SENSOR_COUNT 2U
#define XE_NP_SENSOR_MASK_BMX 1U
#define XE_NP_SENSOR_MASK_BME 2U
#define XE_NP_SENSOR_VALID_AG 1U
#define XE_NP_SENSOR_VALID_MAG 2U
#define XE_NP_SENSOR_VALID_ENV 4U
/* Nominal device configuration, not a lossless archive/host poll rate. */
#define XE_NP_SENSOR_AG_ODR_HZ 200U
#define XE_NP_SENSOR_MAG_ODR_HZ 100U
#define XE_NP_SENSOR_ERR_IO 1U
#define XE_NP_SENSOR_ERR_TIMEOUT 2U
#define XE_NP_SENSOR_ERR_ID 3U
#define XE_NP_SENSOR_ERR_CONFIG 4U
#define XE_NP_SENSOR_ERR_CALIBRATION 5U
#define XE_NP_SENSOR_ERR_SAMPLE 6U
#define XE_NP_SENSOR_ERR_DISABLED 7U
#define XE_NP_SENSOR_ERR_BUSY 8U
#define XE_NP_SENSOR_ERR_MAG_UNAVAILABLE 9U /* AG remains usable; MAG valid stays clear */

/* Foreground-owned state machine; IRQs only deliver completion tokens.
 * start returns 0 if accepted, 1 if busy, negative on failure. stop MUST
 * synchronously quiesce that bus's IRQ/buffer access, without waiting/delays.
 * recovery may reset that dedicated I2C controller, but only when idle. */
typedef struct {
    void *context;
    int (*start)(void *,uint8_t,uint8_t,uint8_t,uint8_t *,uint16_t,uint8_t,uint32_t);
    void (*stop)(void *,uint8_t);
    int (*recover)(void *,uint8_t);
} XE_NP_SensorIo;

/* Fixed units, no structs with pointers or floats on the USB control wire.
 * enabled is the effective healthy-device mask, not the requested mask.
 * It stays zero until a device publishes a verified sample and clears on
 * failure. device[].enabled retains the request for stopped-only retries. */
typedef struct {
    uint32_t magic,version,enabled,valid,published_ms;
    uint32_t imu_sequence,mag_sequence,env_sequence;
    uint32_t imu_tick_ms,mag_tick_ms,env_tick_ms,sensor_time_24;
    int32_t accel_micro_g[3],gyro_milli_dps[3],mag_nano_tesla[3];
    int32_t temperature_centi_c;
    uint32_t pressure_pa,humidity_milli_percent;
    uint32_t chip_id[2],error[2],io_errors[2],timeouts[2];
    /* Gap estimate from sensor-time intervals, not a lossless FIFO counter. */
    uint32_t missed_imu_samples,invalid_mag_samples,ignored_completions;
} XE_NP_SensorSnapshot;

typedef struct {
    /* Separate SWD diagnostics: never changes the 140-byte recording ABI.
     * Last failure survives idle retries and six-axis fallback. */
    uint32_t count,tick_ms,error,state,step,reg,actual,expected,mask,io_error;
} XE_NP_SensorFault;

typedef struct {
    uint8_t address,state,step,configured,status,enabled;
    uint8_t last_reg;
    uint8_t data[32],calibration[40];
    uint32_t token,deadline,next_due,phase_deadline,poll_started;
    volatile uint32_t completed_token,completion_tick,completion_error;
    volatile uint8_t completion,pending;
} XE_NP_SensorDevice;
typedef struct {
    XE_NP_SensorIo io;
    XE_NP_SensorDevice device[2];
    XE_NP_SensorSnapshot snapshot;
    XE_NP_SensorFault fault[2];
    struct bme280_calib_data bme_calibration;
    struct bmm150_dev magnetometer;
    uint32_t previous_sensor_time;
    uint8_t have_sensor_time,mag_calibrated,mag_bad_samples;
} XE_NP_Sensors;

void XE_NP_Sensors_Init(XE_NP_Sensors *,const XE_NP_SensorIo *,uint32_t,uint32_t);
void XE_NP_Sensors_Service(XE_NP_Sensors *,uint32_t now_ms,uint8_t recording_active);
void XE_NP_Sensors_Complete(XE_NP_Sensors *,uint8_t bus,uint32_t token,uint32_t error,uint32_t now_ms);
void XE_NP_Sensors_Snapshot(const XE_NP_Sensors *,XE_NP_SensorSnapshot *,uint32_t now_ms);
void XE_NP_Sensors_RefreshValidity(XE_NP_SensorSnapshot *,uint32_t now_ms);
static inline uint32_t XE_NP_Sensors_StreamMask(const XE_NP_SensorSnapshot *s)
{
    uint32_t valid=0U;
    if(s->enabled&XE_NP_SENSOR_MASK_BMX) {
        if(s->error[0]==0U)valid|=s->valid&(XE_NP_SENSOR_VALID_AG|XE_NP_SENSOR_VALID_MAG);
        else if(s->error[0]==XE_NP_SENSOR_ERR_MAG_UNAVAILABLE)valid|=s->valid&XE_NP_SENSOR_VALID_AG;
    }
    if((s->enabled&XE_NP_SENSOR_MASK_BME) && s->error[1]==0U)
        valid|=s->valid&XE_NP_SENSOR_VALID_ENV;
    return valid;
}
int XE_NP_Sensors_ParseBmeCalibration(const uint8_t *,const uint8_t *,struct bme280_calib_data *);
int XE_NP_Sensors_DecodeBme(const uint8_t *,struct bme280_calib_data *,XE_NP_SensorSnapshot *);
int XE_NP_Sensors_DecodeImu(XE_NP_Sensors *,const uint8_t *,uint8_t status,uint32_t now_ms);
#endif
