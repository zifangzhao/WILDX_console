/* After SD private state. Foreground only; UART IRQ merely queues64 bytes. */
#include "np_sd_control.h"
#include "np_sd_qualification.h"
int NP_SD_ControlSubmit(const uint8_t *,unsigned);
static uint8_t npl_request[64],npl_reply[68];
static volatile unsigned npl_pending;
static unsigned npl_media_pending;
static unsigned npl_reply_ready,npl_mode_target;
static uint32_t npl_delete_phase,npl_delete_remaining; /* working,95,90,error; foreground only */
static uint32_t npl_rate_prior_mask,npl_rate_prior_hz;
#include "np_sd_rate_port.h"
static unsigned npl_ce32_start_receipt,npl_ce32_stop_receipt;
static uint32_t npl_mode_tick;
#include "np_ble_online_port.inc"
int NP_SD_ControlTransferPending(void){return npl_pending || npl_reply_ready || npl_mode_target || npl_delete_phase;}
static int npl_delete_blocks(uint8_t command)
{
 return npl_delete_phase && (command==0x01U || command==0x02U || command==0x03U ||
  command==0x15U || command==0x18U || command==0x30U || command==0x31U ||
  command==0x32U || command==0x33U || command==0x95U || command==0x9eU);
}
static int npl_idle(void){return np_sd_initialized && (np_sd_log.state==NP_SD_IDLE || np_sd_finalized);}
static void npl_sync_system(uint32_t mask,uint32_t hz)
{
 uint32_t count=npl_count(mask);
 uint32_t misc=(hz*780U*count*16U+NP_SD_PAYLOAD_BYTES/2U)/NP_SD_PAYLOAD_BYTES;
 np_ce64_apply_system_metadata(&xe_np_runtime.sys_param,mask,hz,misc);
}
/* High-rate two-probe capture is wired to the GPIOE low/high pair.
 * Reject another requested pair; do not silently remap probe identities. */
static int npl_same_gpio_high_rate(uint32_t mask,uint32_t hz)
{
 return hz<20000U || npl_count(mask)!=2U || mask==3U;
}
static int npl_select(uint32_t mask,uint32_t hz)
{
 if(!npl_idle() || npl_mode_target || !np_mode_valid(mask,hz) || !npl_same_gpio_high_rate(mask,hz))return 0;
 npl_rate_prior_mask=np_mode_mask;npl_rate_prior_hz=np_mode_hz;
 int reset_clock=NP_SD_RateStage(mask,hz);
 if(mask)xe_np_runtime.sys_param.sampling_rate[4]=0U; /* media AUX is incompatible with NP */
 np_mode_mask=mask;np_mode_hz=hz;npl_sync_system(mask,hz);np_sd_quiesce();
 if(reset_clock){npl_mode_target=6U;npl_mode_tick=HAL_GetTick();}
 return 1;
}
static int npl_qualified(uint32_t mask,uint32_t hz){
 SD_HandleTypeDef *sd=xe_np_runtime.hsd_card;
 return sd && np_sd_profile_qualified(sd->CID,mask,hz);
}
/* NP_SD_RELEASE_NO_PRIVATE_HIL_GATE: normal recording admission uses
 * mode/topology, whole-AU, catalog, capacity and ownership checks below.
 * Private diagnostic extents are not a release START requirement. */
static void npl_status(void *context,NplStatus *s){
 (void)context;SD_HandleTypeDef *sd=xe_np_runtime.hsd_card;
 s->state=np_sd_log.state;s->fault=np_sd_log.fault;s->mask=np_mode_mask;s->hz=np_mode_hz;
 s->chunks=np_sd_log.committed_chunks;s->start=np_ce64_dir.start;s->end=np_sd_mailbox.end_lba;
 s->capacity=sd && !xe_np_sd_init_failed?sd->SdCard.LogBlockNbr:0;
 s->flags=NPL_SENSOR_CAPABILITY|2097152U|(!np_mode_mask?4194304U:0U)|(np_mic.running?8388608U:0U)|
  (NP_SD_ALLOW_WRITES && np_sd_mailbox.enabled?NPL_WRITES:0)|(npl_qualified(np_mode_mask,np_mode_hz)?NPL_QUALIFIED:0)|
  (!npl_idle()?NPL_ACTIVE:0)|((np_sd_mailbox.command || np_sd_readback.command || NP_ConfigTransferPending() || npl_mode_target || npl_delete_phase)?NPL_PENDING:0)|
  (np_ce64_diag.state==1U?NPL_CARD:0)|(np_ce64_diag.state==2U?NPL_CATALOG_READ_ONLY:0)|
#if NP_SD_STORE_V2
  NPL_STORE_V2|(np_store_selected?NPL_STORE_V2_SELECTED:0U)|
#endif
  NP_SD_RATE_PROFILES_FLAG|NPL_ONLINE_CLOCK|NPL_RESCUE_RESET|NPL_VERIFIED_V1_MODES|
  0x08000000U|NPL_RAW_PREVIEW|NPL_SCHEDULE|NPL_CLOCK_METADATA|NPL_CONTINUOUS|NPL_STORAGE|(np_mode_valid(3U,20000U)?NPL_SCALED20:0)|(npl_schedule.waiting?NPL_SCHEDULE_ARMED:0)|
  (np_sd_log.state==NP_SD_RUNNING && np_mode_mask && !xe_np_probe_state.initialized?NPL_PREPARING:0);
}
static int npl_apply(void *context,const uint32_t *w){
 (void)context;unsigned op=w[2];
 if(op==NPL_MOUNT){
  if(!npl_idle() || (np_sd_log.fault && !np_sd_finalized) || !xe_np_runtime.hsd_card)return NPL_BUSY;
  /* A finalized recording fault is retained evidence, not ownership of the
   * card. Remount may validate/replay the catalog, never clear that fault. */
  /* Like FORMAT, acknowledge the queued operation; STATUS confirms its result
   * after idle camera DMA has retired. Never interrupt a live recording. */
  np_sd_mailbox.inverse=~0x4D4F554EU;__DMB();np_sd_mailbox.command=0x4D4F554EU;return NPL_OK;
 }
 if(op==NPL_RECOVER){
#if NP_SD_STORE_V2
  /* Mount accepts an interrupted bounded grant conservatively. The legacy
   * reclaim helper must never interpret the independent allocation layout. */
  if(np_store_selected)return NPL_DISABLED;
#endif
  if(!npl_idle() || np_sd_camera.enabled || np_sd_log.fault || !xe_np_runtime.hsd_card || xe_np_sd_init_failed || np_ce64_dir.active)return NPL_BUSY;
  if(np_ce64_diag.state!=1U)return NPL_IO; /* Read-only salvage never repairs an uncertain allocation. */
  if(np_ble_recover_end(&np_ce64_dir,xe_np_runtime.hsd_card->SdCard.LogBlockNbr,np_ce64_read,np_ce64_write))return NPL_IO;
  return np_ce64_mount_card()?NPL_IO:NPL_OK;
 }
 if(op==NPL_SCHEDULE_START){
  if(!npl_idle() || np_sd_log.fault || npl_schedule.active)return NPL_BUSY;
  uint64_t utc=(uint64_t)w[11]|((uint64_t)w[12]<<32);
  if(!np_ble_schedule_arm(&npl_schedule,&npl_online_clock,utc,HAL_GetTick()))return NPL_RANGE;
  uint32_t start[16];memcpy(start,w,sizeof(start));start[2]=NPL_START;
  int result=npl_apply(context,start);if(result)memset(&npl_schedule,0,sizeof(npl_schedule));
  return result;
 }
 if(op==NPL_USB_MODE){
  uint32_t mode[16]={0};mode[2]=NPL_MODE;
  mode[10]=w[4]==1U?4U:w[4]==2U?2U:w[4]==3U?5U:3U;
  return npl_apply(context,mode);
 }
 if(op==NPL_SELECT)return npl_select(w[4],w[5])?NPL_OK:NPL_BUSY;
 if(op==NPL_START){
  /* V1 storage admission requires whole 8 MiB AUs, not merely CMD25 alignment. */
  if(w[7]&16383U)return NPL_RANGE;
  if(w[4]!=np_mode_mask || w[5]!=np_mode_hz || (!w[4] && !xe_np_runtime.sys_param.sampling_rate[2] && !xe_np_runtime.sys_param.sampling_rate[4]))return NPL_RANGE;
  if(!npl_same_gpio_high_rate(w[4],w[5]))return NPL_RANGE;
  /* NP_SD_RELEASE_QUALIFICATION_INFORMATIONAL: keep the truthful status
   * bit, but do not require CID qualification or a diagnostic bench ACK
   * for an otherwise valid START. This does not qualify a recording. */
  if(np_ce64_diag.state!=1U || (w[6] && w[6]!=np_ce64_dir.start))return NPL_RANGE;
  NpSdConfig cfg={np_ce64_dir.start,w[7],w[8],w[4],1,w[5],6,250};static NpSdLog check;
  if(np_sd_init(&check,&cfg,xe_np_runtime.hsd_card->SdCard.LogBlockNbr))return NPL_RANGE;
  /* NP_SD_RELEASE_OPTIONAL_SENSOR_START: missing/failed IMU or BME data
   * stays disabled/invalid in the latest-value sensor extension. Optional
   * sensor availability is not a neural recording admission condition. */
  np_sd_mailbox.start_lba=cfg.start_lba;np_sd_mailbox.end_lba=cfg.end_lba;np_sd_mailbox.session=cfg.session;
  np_sd_mailbox.inverse=~0x53544152U;__DMB();np_sd_mailbox.command=0x53544152U;return NPL_OK;
 }
 if(op==NPL_STOP){npl_schedule.active=npl_schedule.waiting=0;np_sd_mailbox.inverse=~0x53544f50U;__DMB();np_sd_mailbox.command=0x53544f50U;return NPL_OK;}
 if(op==NPL_FORMAT){
  if(!npl_idle())return NPL_BUSY;
  if(!xe_np_runtime.hsd_card)return NPL_IO;
  /* A previous host-init failure may have cleared the mutable HAL geometry.
   * The stopped worker verifies the boot-approved card before any format I/O. */
  /* Explicit destructive maintenance may stop idle preview; the foreground
   * FORMAT handler quiesces it before writing. Recording/drain stays busy. */
  uint32_t command=0x464d4154U;
  if(w[1]==2U){
#if NP_SD_STORE_V2
   np_store_format_epoch64=(uint64_t)w[11]|((uint64_t)w[12]<<32);
   command=0x464d4132U;
#else
   return NPL_DISABLED;
#endif
  }
  np_sd_mailbox.inverse=~command;__DMB();np_sd_mailbox.command=command;return NPL_OK;
 }
 if(op==NPL_MODE){
  if(!npl_idle() || np_sd_log.fault)return NPL_BUSY;
  if(!npl_mode_available(w[10]))return NPL_IO;
  npl_mode_target=w[10];npl_mode_tick=HAL_GetTick();return NPL_OK;
 }
 return NPL_RANGE;
}
static NplControl npl_control={{0,npl_status,npl_apply},0,{0},{0}};
static uint32_t npl_record_count(void)
{
#if NP_SD_STORE_V2
 if(np_store_selected)return np_store_state.ready?np_store_state.count:0U;
#endif
 return np_ce64_dir.block*NP_CE64_ENTRIES+np_ce64_dir.index;
}
uint32_t NP_SD_Ce32RecordCount(void){return npl_record_count();}
static const char *npl_ce32_start_error;
static int npl_ce32_start(void)
{
 uint32_t w[16]={0},capacity,header,payload,end,session;
 NpSdConfig geometry;
 npl_ce32_start_error="Start rejected: invalid profile";
 if(!npl_idle()){npl_ce32_start_error="Start rejected: recorder busy";return 0;}
 if(npl_mode_target || np_sd_mailbox.command || npl_ce32_start_receipt){npl_ce32_start_error="Start rejected: command pending";return 0;}
 if(NP_SD_RateCoreHz()!=SystemCoreClock){npl_ce32_start_error="Start rejected: clock/profile";return 0;}
 if(!xe_np_runtime.hsd_card || xe_np_sd_init_failed){npl_ce32_start_error="Start rejected: SD unavailable";return 0;}
 if(np_ce64_diag.state!=1U){npl_ce32_start_error="Start rejected: SD not mounted";return 0;}
 capacity=xe_np_runtime.hsd_card->SdCard.LogBlockNbr;
 memset(&geometry,0,sizeof(geometry));geometry.start_lba=np_ce64_dir.start;
 header=np_sd_header_sectors(&geometry);
 if(geometry.start_lba>capacity || header>capacity-geometry.start_lba){npl_ce32_start_error="Start rejected: card full";return 0;}
 payload=geometry.start_lba+header;
 end=payload+((capacity-payload)/16384U)*16384U;
 if(end<=payload){npl_ce32_start_error="Start rejected: card full";return 0;}
 session=HAL_GetTick();if(!session)session=1U;
 w[2]=NPL_START;w[4]=np_mode_mask;w[5]=np_mode_hz;w[6]=np_ce64_dir.start;
 w[7]=end;w[8]=session;w[9]=1U;
 return npl_apply(0,w)==NPL_OK;
}
int NP_SD_Ce32RecordingCommandBackend(uint8_t command,const uint8_t *data,uint32_t length)
{
 if(!data || !length || data[0]!=command)return -1;
 if(npl_delete_blocks(command))return -1;
 if(command==0x01U){
  CE32_systemParam requested;uint32_t mask,hz;
  if(length!=1U+sizeof(requested) || !npl_idle() || npl_mode_target)return -1;
  memcpy(&requested,data+1,sizeof(requested));
  mask=(requested.unassigned_32&0xffff0000U)==NP_CE64_CONTAINER_TAG?
       (requested.unassigned_32>>8U)&7U:np_mode_mask;
  hz=requested.sampling_rate[0]?requested.sampling_rate[0]:requested.fs;
  if(!np_mode_valid(mask,hz) || !npl_same_gpio_high_rate(mask,hz) || (requested.sampling_rate[2]>NP_CAMERA_SENSOR_MAX_HZ ||
     (requested.sampling_rate[4] && (mask || hz || requested.sampling_rate[4]!=NP_MIC_HZ))))return -1;
  requested.firmware_version=xe_np_runtime.sys_param.firmware_version;
  requested.hw_version=xe_np_runtime.sys_param.hw_version;
  requested.system_status=xe_np_runtime.sys_param.system_status;
  requested.error_code=xe_np_runtime.sys_param.error_code;
  requested.SD_capacity=xe_np_runtime.sys_param.SD_capacity;
  memcpy(requested.MAC,xe_np_runtime.sys_param.MAC,sizeof(requested.MAC));
  xe_np_runtime.sys_param=requested;
  if(!npl_select(mask,hz))return -1;np_media_save_settings();return 0;
 }
 if(command==0x30U){
  npl_ce32_start_error="Start rejected: bad request";
  if(length!=1U || !npl_ce32_start())return -1;
  npl_ce32_start_receipt=1U;return 0;
 }
 if(command==0x31U){
  if(length!=1U)return -1;
  npl_schedule.active=npl_schedule.waiting=0;
  /* START may still be waiting for idle-camera DMA retirement. Cancel that
   * mailbox too; otherwise "Rec Stop" can precede a later unwanted start. */
  if(np_sd_log.state==NP_SD_RUNNING || np_sd_log.state==NP_SD_DRAINING ||
     np_sd_mailbox.command==0x53544152U){
   np_sd_mailbox.inverse=~0x53544f50U;__DMB();np_sd_mailbox.command=0x53544f50U;
  }
  npl_ce32_start_receipt=0U;
  npl_ce32_stop_receipt=1U;
  return 0;
 }
 if(command==0x32U){
  uint32_t hz;if(length!=3U)return -1;hz=(uint32_t)data[1]|((uint32_t)data[2]<<8U);
  return npl_select(np_mode_mask,hz)?0:-1;
 }
 if(command==0x33U){
  uint32_t rate[CE32_MAX_NCH_GROUP];
  if(length!=1U+sizeof(rate))return -1;memcpy(rate,data+1,sizeof(rate));
  if((!rate[0] && (rate[1] || (!rate[2] && !rate[4]))) || (rate[1] && rate[1]!=rate[0]/12U) || rate[2]>NP_CAMERA_SENSOR_MAX_HZ)return -1;
  if(rate[4] && (rate[0] || rate[4]!=NP_MIC_HZ))return -1;
  for(unsigned i=5;i<CE32_MAX_NCH_GROUP;i++)if(rate[i])return -1;
  uint32_t prior_camera=xe_np_runtime.sys_param.sampling_rate[2],prior_mic=xe_np_runtime.sys_param.sampling_rate[4];
  xe_np_runtime.sys_param.sampling_rate[2]=rate[2];xe_np_runtime.sys_param.sampling_rate[4]=rate[4];
  if(!npl_select(rate[0]?np_mode_mask:0U,rate[0])){xe_np_runtime.sys_param.sampling_rate[2]=prior_camera;xe_np_runtime.sys_param.sampling_rate[4]=prior_mic;return -1;}
  np_media_save_settings();return 0;
 }
 if(command==0x94U){
  uint16_t block;if(length!=3U)return -1;
  block=(uint16_t)data[1]|((uint16_t)data[2]<<8U);
#if NP_SD_STORE_V2
  /* Versioned FS2 pages read mirrored descriptors, never the live cache. */
  if(np_store_selected && (!npl_idle() || np_sd_camera.enabled))return -1;
#endif
  /* Idle camera acquisition still has deadlines; older pages require SD. */
  if((!npl_idle() || np_sd_camera.enabled) &&
     !(np_ce64_diag.state==1U && block==np_ce64_dir.block))return -1;
  return NP_CE64_ReadLogBlock(block)?-1:0;
 }
 if(command==0x95U){
  if(length!=2U || !npl_idle() || np_sd_mailbox.command || npl_mode_target || npl_schedule.active ||
     np_sd_readback.command || NP_ConfigTransferPending() || NP_SD_ControlTransferPending() ||
     !NP_CE64_DeleteAllowed())return -1;
  uint32_t queued=0x44454C00U|data[1];npl_delete_phase=1U;
  np_sd_mailbox.inverse=~queued;__DMB();np_sd_mailbox.command=queued;return 2;
 }
 return 1;
}
static int npl_ce32_frame(uint8_t command,const void *payload,uint32_t bytes)
{
 uint8_t frame[516];CE32_INTERCOM_Handle *ble=(CE32_INTERCOM_Handle *)&BLE_handle;
 if(bytes>512U)return 0;frame[0]=0xad;frame[1]=command;
 if(bytes)memcpy(frame+2,payload,bytes);frame[bytes+2]=frame[bytes+3]=0xda;
 if(CE32_INTERCOM_TX_EnqueueCmd(ble,frame,(int)bytes+4))return 0;
 (void)CE32_INTERCOM_TX_Start_DMA(ble);return 1;
}
static int npl_ce32_message(const char *text)
{
 uint8_t payload[32]={0};unsigned length=0;
 while(text[length] && length<sizeof(payload)){payload[length]=(uint8_t)text[length];length++;}
 return npl_ce32_frame(0x85U,payload,sizeof(payload));
}
static void npl_delete_complete(int result)
{
 if(npl_delete_phase!=1U)return;
 if(result)npl_delete_phase=4U;
 else {npl_delete_remaining=npl_record_count();npl_delete_phase=2U;}
}
static void npl_delete_reply_service(void)
{
 if(npl_pending || npl_reply_ready || npl_online_pending || npl_mode_target)return;
 if(npl_delete_phase==2U && npl_ce32_frame(0x95U,&npl_delete_remaining,sizeof(npl_delete_remaining)))npl_delete_phase=3U;
 if(npl_delete_phase==3U && npl_ce32_frame(0x90U,&xe_np_runtime.sys_param,sizeof(xe_np_runtime.sys_param)))npl_delete_phase=0U;
 if(npl_delete_phase==4U && npl_ce32_message("Delete failed"))npl_delete_phase=0U;
}
#include "np_ble_ce32_info.inc"
#include "np_media_ble.inc"
int NP_SD_Ce32Dispatch(const uint8_t *data,uint32_t length)
{
 uint8_t command,ack[5];int result;
 if(!data || !length)return 0;command=data[0];
 if(npl_delete_blocks(command))return 0;
 int media=np_media_ble_dispatch(data,length);if(media>=0)return media;
 int info=npl_ce32_info(data,length);if(info>=0)return info;
 if(command==0x82U && length==4U && data[1]==0x81U && !data[2] && !data[3]){
  uint8_t request[64]={0};npl_put(request,0x31444c4eU);npl_put(request+4,1U);
  npl_put(request+12,1U);npl_put(request+60,npl_crc(request,60));
  return NP_SD_ControlSubmit(request,64);
 }
 if(command==0x01U || command==0x30U || command==0x31U || command==0x32U ||
    command==0x33U || command==0x94U || command==0x95U){
  result=NP_SD_Ce32RecordingCommandBackend(command,data,length);
  if(command==0x95U && result==2)return 1; /* consumed; only foreground completion replies */
  if(result)return command==0x30U?npl_ce32_message(npl_ce32_start_error?npl_ce32_start_error:"Start rejected: bad request"):0;
  if(command==0x30U || command==0x31U){
   ack[0]=ack[1]=0x5a;ack[2]=command;ack[3]=ack[4]=0xa5;
   if(CE32_INTERCOM_TX_EnqueueCmd((CE32_INTERCOM_Handle *)&BLE_handle,ack,5))return 0;
   (void)CE32_INTERCOM_TX_Start_DMA((CE32_INTERCOM_Handle *)&BLE_handle);
  }else if(command==0x01U || command==0x33U){
   return npl_ce32_frame(0x90U,&xe_np_runtime.sys_param,sizeof(xe_np_runtime.sys_param));
  }else if(command==0x94U){
   return npl_ce32_frame(command,&xe_np_runtime.sys_log,sizeof(xe_np_runtime.sys_log));
  }
  return 1;
 }
 if(command==0x02U || command==0x03U){
  if(length!=1U+sizeof(CE32_dspParam) || !npl_idle())return 0;
  memcpy(&xe_np_runtime.dsp_param[command-0x02U],data+1,sizeof(CE32_dspParam));return 1;
 }
 if(command==0x90U && length==1U)return npl_ce32_frame(command,&xe_np_runtime.sys_param,sizeof(xe_np_runtime.sys_param));
 if((command==0x91U || command==0x92U) && length==1U)
  return npl_ce32_frame(command,&xe_np_runtime.dsp_param[command-0x91U],sizeof(CE32_dspParam));
 if(command==0x50U && length==1U){
  uint8_t status=(uint8_t)xe_np_runtime.sys_param.system_status;
  if(CE32_INTERCOM_TX_EnqueueCmd((CE32_INTERCOM_Handle *)&BLE_handle,&status,1))return 0;
  (void)CE32_INTERCOM_TX_Start_DMA((CE32_INTERCOM_Handle *)&BLE_handle);return 1;
 }
 if(command==0x80U && length==1U){
  uint8_t ping=0x80;
  if(CE32_INTERCOM_TX_EnqueueCmd((CE32_INTERCOM_Handle *)&BLE_handle,&ping,1))return 0;
  (void)CE32_INTERCOM_TX_Start_DMA((CE32_INTERCOM_Handle *)&BLE_handle);return 1;
 }
 return npl_ce32_message("CE32 control unsupported on NP");
}
static int npl_status_request(const uint8_t *request){
 if(npl_u32(request)!=0x31444c4eU || npl_u32(request+4)!=1U ||
    npl_u32(request+8)!=NPL_STATUS || !npl_u32(request+12))return 0;
 for(unsigned i=16U;i<60U;i++)if(request[i])return 0;
 return npl_u32(request+60)==npl_crc(request,60);
}
int NP_SD_ControlSubmit(const uint8_t *request,unsigned length){
 if(!request || length!=64)return 0;
 if(npl_u32(request)==NP_ANCHOR_REQUEST && !XE_NP_AcquisitionAnchor_Request(request))return 0;
 if(!memcmp(request,"MEDIA1",6) || !memcmp(request,"CAMEX1",6)){
  if(npl_media_pending || npl_pending || npl_reply_ready || npl_mode_target || npl_online_pending || npl_u32(request+60)!=npl_crc(request,60))return 0;
  for(unsigned i=6U;i<60U;i++)if(request[i])return 0;
  npl_media_pending=!memcmp(request,"CAMEX1",6)?2U:1U;return 1;
 }
 if(npl_u32(request)==NP_ONLINE_REQUEST)return 1;
 if(npl_preview_request(request))return 1;
 /* Clock-handoff reads retain PENDING; close admission at the existing reset
  * threshold so polling cannot keep the UART drain gate busy indefinitely. */
 if(npl_pending || npl_reply_ready || npl_online_pending ||
    (npl_mode_target && (npl_mode_target!=6U || HAL_GetTick()-npl_mode_tick>=500U ||
                         !npl_status_request(request))))return 0;
 memcpy(npl_request,request,64);__DMB();npl_pending=1;return 1;
}
#include "np_battery_ble.inc"
void NP_BleUartService(void);
static void npl_service(void){
 NP_BleUartService();
 (void)npl_sensor_lease_live(HAL_GetTick());
 CE32_INTERCOM_Handle *ble=(CE32_INTERCOM_Handle *)&BLE_handle;
 /* An unavailable UART must not leave all future commands permanently busy.
  * Never perform a deferred reset without first enqueueing its receipt. */
 if(npl_mode_target && HAL_GetTick()-npl_mode_tick>=5000U){
  if(npl_mode_target==6U){
   /* No capture was admitted. Cancel a stalled clock handoff back to the
    * running profile, including the retained receipt; never leave a mismatch. */
   (void)NP_SD_RateStage(npl_rate_prior_mask,npl_rate_prior_hz);
   np_mode_mask=npl_rate_prior_mask;np_mode_hz=npl_rate_prior_hz;
   npl_sync_system(np_mode_mask,np_mode_hz);np_sd_quiesce();
  }
  npl_mode_target=0;npl_reply_ready=0;
 }
 if(npl_online_pending==1U && !npl_pending && !npl_reply_ready && !npl_mode_target){
  uint32_t state=npl_online_state();
  npl_reply[0]=0xad;npl_reply[1]=0xb9;
  if(!XE_NP_Online_Exchange(&npl_online_clock,npl_online_request,npl_reply+2,
     npl_online_received,HAL_GetTick(),state,state==1U || state==5U)){
   npl_reply[66]=npl_reply[67]=0xda;npl_reply_ready=1;npl_online_pending=2;
  }else npl_online_pending=0;
 }
 if(npl_pending){npl_pending=0;npl_reply[0]=0xad;npl_reply[1]=0xb9;
  if(npl_sensor_command(npl_request,npl_reply+2)){}
  else if(XE_NP_AcquisitionAnchor_Request(npl_request))NP_CaptureAnchor_Query(npl_reply+2,npl_u32(npl_request+8));
  else npl_command(&npl_control,npl_request,npl_reply+2);
  npl_reply[66]=npl_reply[67]=0xda;npl_reply_ready=1;
 }
 if(npl_reply_ready && ble->huart && CE32_INTERCOM_TX_EnqueueCmd(ble,npl_reply,68)==0){
  npl_battery_status_queued(npl_reply,HAL_GetTick());
  npl_reply_ready=0;(void)CE32_INTERCOM_TX_Start_DMA(ble);
  if(npl_online_pending==2U){__DMB();npl_online_pending=0;}
 }
 if(npl_mode_target && !npl_reply_ready && ble->huart && HAL_GetTick()-npl_mode_tick>=500U &&
    ble->CMD_TX.cmd_pending==0U &&
    ble->huart->gState==HAL_UART_STATE_READY && __HAL_UART_GET_FLAG(ble->huart,UART_FLAG_TC)){
  uint32_t target=npl_mode_target;npl_mode_target=0;
  if(target==5U){NP_BleEnterDfu();return;}
  if(target==6U){XE_NP_Boot_RequestApplication();NVIC_SystemReset();return;}
  np_v1_mode_mailbox.inverse=~target;__DMB();np_v1_mode_mailbox.target=target;
 }
 npl_delete_reply_service();
 if(npl_delete_phase)return; /* native replies first; no preview/battery queue contention */
 if(npl_media_pending && !npl_reply_ready && !npl_pending && !npl_online_pending && (npl_media_pending==2U?np_media_exposure_status():np_media_ble_status()))npl_media_pending=0;
 if(npl_ce32_start_receipt && np_sd_log.state==NP_SD_RUNNING && !np_sd_log.fault){
  if(npl_ce32_message("Rec Start"))npl_ce32_start_receipt=0;
 }else if(npl_ce32_start_receipt && (np_sd_log.state==NP_SD_FAILED && np_sd_mailbox.command!=0x53544152U)){
  if(npl_ce32_message("NP start failed"))npl_ce32_start_receipt=0;
 }
 if(npl_ce32_stop_receipt && npl_idle()){
  if(npl_ce32_message("Rec Stop"))npl_ce32_stop_receipt=0;
 }
 npl_battery_service(HAL_GetTick());
 np_media_camera_preview_service();
 npl_preview_service();
}

int NP_BlePollTxAllowed(void){return !(BLE_handle.state&HJ580_STATE_CONFIG);}
