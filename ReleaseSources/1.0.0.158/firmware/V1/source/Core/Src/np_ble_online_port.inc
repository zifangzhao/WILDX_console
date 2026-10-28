/* Included after V1 SD private state. No capture ISR/DMA changes. */
#include "xe_np_online_impl.h"
#include "xe_np_boot_contract.h"
#include "np_ble_schedule.h"
#include "np_ble_preview.h"
#include "np_ble_dfu.h"
#include "np_ble_storage.h"
enum { NPL_ONLINE_CLOCK=256U, NPL_RESCUE_RESET=4096U, NPL_VERIFIED_V1_MODES=8192U,
       NPL_RAW_PREVIEW=128U, NPL_SCHEDULE=1024U, NPL_CLOCK_METADATA=2048U,
       NPL_SCHEDULE_ARMED=131072U, NPL_PREPARING=262144U,
       NPL_CONTINUOUS=512U, NPL_STORAGE=16384U, NPL_SCALED20=524288U };
static XE_NP_OnlineClock npl_online_clock;
static NpBleSchedule npl_schedule;
static NpBlePreview npl_preview;
/* Retain only the first private chunk's framing prefix for each probe. This
 * anchors LFP to the SAME first-superframe convention as the SD decoder, even
 * if preview is opened after the 20-bit hardware counter has wrapped. */
static uint8_t npl_preview_origin[3][1560];
static unsigned npl_preview_origin_mask;
static int npl_ce32_frame(uint8_t,const void *,uint32_t);
#include "np_ble_sensors.inc"
static uint8_t npl_online_request[64];
static volatile unsigned npl_online_pending;
static uint32_t npl_online_received;

/* Called from foreground parsing with the USART1 tail-byte RX timestamp. A serialized
 * client gets the real RX timestamp, not the later foreground service time.
 * Busy requests are dropped, never replace an unacknowledged clock commit. */
void NP_SD_BleOnlineRxAt(const uint8_t *data,uint32_t length,uint32_t received_ms)
{
    if(length!=65U || data[0]!=0xb9U ||
       xe_np_online_word(data+1)!=NP_ONLINE_REQUEST || npl_online_pending)return;
    npl_online_received=received_ms;
    memcpy(npl_online_request,data+1,64);__DMB();npl_online_pending=1;
}
void NP_SD_BleOnlineRx(const uint8_t *data,uint32_t length)
{
    NP_SD_BleOnlineRxAt(data,length,HAL_GetTick());
}
/* The shared clock protocol uses normalized lifecycle states. NLR1 recorder
 * replies continue to expose the original V1 enum, unchanged. */
static uint32_t npl_online_state(void)
{
    if(np_sd_log.fault || np_sd_log.state==NP_SD_FAILED)return 255U;
    if(!np_sd_initialized)return 0U;
    if(npl_schedule.waiting)return 6U;
    if(np_sd_mailbox.command || np_sd_readback.command ||
       NP_ConfigTransferPending() || npl_mode_target)return 2U;
    if(np_sd_log.state==NP_SD_RUNNING)return (!np_mode_mask?npl_schedule.valid:xe_np_probe_state.initialized)?3U:2U;
    if(np_sd_log.state==NP_SD_DRAINING)return 4U;
    return np_sd_finalized?5U:1U;
}
static int npl_mode_available(uint32_t target)
{
    if(target==2U)return XE_NP_Boot_UsbServiceIsValid();
    if(target==3U)return XE_NP_Boot_DownloadIsValid();
    if(target==5U)return NP_BleDfuAvailable();
    return target==4U; /* resident updater; not the system ROM DFU loader */
}
/* After existing invalidate, before publication. Startup retains 1560 bytes per
 * selected probe once; thereafter disabled preview is one cheap branch. */
static inline void npl_preview_tap(const uint8_t *data,unsigned slot)
{
    uint32_t sequence=np_sd_camera_source_sequence(slot);
    if(!npl_preview.active && sequence>=npl_count(np_mode_mask))return;
    unsigned probe=npl_probe(np_mode_mask,sequence);
    uint64_t first=(uint64_t)(sequence/npl_count(np_mode_mask))*61440U;
    if(first==0 && probe && np_sd_log.count<16U){
        for(unsigned i=0;i<1560;i++)npl_preview_origin[probe-1U][i]=np_ble_preview_byte(data,i,0,probe);
        npl_preview_origin_mask|=1U<<(probe-1U);
    }
    np_ble_preview_tap(&npl_preview,data,first,61440U,0,probe,np_sd_log.count>=16U);
}
static int npl_preview_request(const uint8_t *request)
{
    if(!np_ble_preview_request(&npl_preview,request,np_sd_log.config.session,np_mode_hz,np_mode_mask,
       np_sd_log.state==NP_SD_RUNNING && !np_sd_log.fault,HAL_GetTick()))return 0;
    if(npl_preview.active && !npl_preview.kind){
        /* Do not substitute a late snapshot with an ambiguous LFP slot. */
        if(npl_preview_origin_mask&(1U<<(npl_preview.probe-1U))){
            memcpy(npl_preview.data,npl_preview_origin[npl_preview.probe-1U],1560);
            npl_preview.first=0;npl_preview.used=1560;npl_preview.active=0;npl_preview.ready=1;
        }else np_ble_preview_stop(&npl_preview);
    }
    return 1;
}
static void npl_preview_service(void)
{
    if(!npl_preview.active && !npl_preview.ready)return;
    if(np_sd_log.fault || np_sd_log.state!=NP_SD_RUNNING ||
       (int32_t)(HAL_GetTick()-npl_preview.lease)>=0){np_ble_preview_stop(&npl_preview);return;}
    CE32_INTERCOM_Handle *ble=(CE32_INTERCOM_Handle *)&BLE_handle;
    /* At most one preview fragment in the existing TX queue. Controls always
     * win; no preview DMA channel, IRQ, busy wait or recording backpressure. */
    if(!npl_preview.ready || npl_pending || npl_reply_ready || npl_online_pending ||
       npl_mode_target || np_sd_log.count>=16U || !ble->huart ||
       ble->CMD_RX.cmd_pending || ble->CMD_TX.cmd_pending ||
       ble->huart->gState!=HAL_UART_STATE_READY)return;
    uint8_t body[224];np_ble_preview_fragment(&npl_preview,body);
    if(npl_ce32_frame(0xbaU,body,sizeof(body)))np_ble_preview_retire(&npl_preview);
}
int NP_SD_BleArmGate(uint32_t now)
{
    int gate=np_ble_schedule_gate(&npl_schedule,now);
    if(gate>0){
        if(!npl_schedule.valid){npl_schedule.before=now;npl_schedule.valid=1;}
        np_sd_start_ms=now; /* startup watchdog begins at capture, not UTC arming */
    }
    return gate;
}
void NP_SD_BleArmStamp(uint32_t now)
{
    npl_schedule.after=now;
    /* NP's exported cursor is bound later. Do not label the root marker as
     * exported source zero. Media retains its existing arm convention. */
    if(np_mode_mask)npl_schedule.valid=0U;
}
static int npl_save_clock(void)
{
    /* Reserved header sector 5; no live SD diagnostics. Existing CE64 header,
     * dataInfo and payload geometry are untouched. */
    if(np_mode_mask)npl_schedule.valid=(uint32_t)NP_CaptureAnchor_GetExportMs(
        &npl_schedule.before,&npl_schedule.after);
    uint8_t *b=np_sd_metadata;memset(b,0,512);
    /* UTC2 identifies exported source zero durably. An installed APP identity
     * cannot establish which older firmware wrote a historical UTC1 sector. */
    memcpy(b,np_mode_mask?"N1SDUTC2":"N1SDUTC1",8);
    npl_put(b+8,np_mode_mask?2U:1U);npl_put(b+12,np_sd_log.config.session);
    npl_put(b+16,(npl_schedule.clock.valid?1U:0U)|(npl_schedule.valid?2U:0U)|(npl_schedule.utc?4U:0U));
    npl_put(b+20,npl_schedule.clock.generation);npl_put(b+24,npl_schedule.clock.uncertainty_us);
    npl_put(b+28,npl_schedule.clock.tick);
    const uint64_t times[]={npl_schedule.clock.utc_us,npl_schedule.utc,
        XE_NP_Online_Utc(&npl_schedule.clock,npl_schedule.before),
        XE_NP_Online_Utc(&npl_schedule.clock,npl_schedule.after)};
    for(unsigned i=0;i<4;i++){npl_put(b+32+8*i,(uint32_t)times[i]);npl_put(b+36+8*i,(uint32_t)(times[i]>>32));}
    npl_put(b+64,npl_schedule.before);npl_put(b+68,npl_schedule.after);
    npl_put(b+72,np_sd_log.config.ap_hz);npl_put(b+76,1000U);npl_put(b+508,npl_crc(b,508));
    return np_sd_metadata_write(np_sd_log.config.start_lba+5U,b);
}
