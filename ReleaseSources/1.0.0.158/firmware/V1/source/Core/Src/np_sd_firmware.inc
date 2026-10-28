static int np_sd_fast_stage_init(void);
#ifndef NP_SD_STORE_V2
/* Explicit development target until integrated recording/recovery acceptance.
 * A legacy image must never silently create/adopt the new filesystem. */
#define NP_SD_STORE_V2 0
#endif
#if NP_SD_STORE_V2
static int np_store_async_irq(void);
static int np_store_async_service(uint32_t now);
static void np_store_async_cancel(void);
static void np_store_payload_binding(uint8_t *sector);
#define NP_SD_PAYLOAD_BINDING(b) np_store_payload_binding(b)
#endif
#undef NP_SD_PROBE_MASK
#define NP_SD_PROBE_MASK np_mode_mask
#undef NP_SD_AP_HZ
#define NP_SD_AP_HZ np_mode_hz
/* Included only by the private V1 SD application, after xe_np_app.c helpers.
 * No live-ring CPU loads: GPDMA14 owns all SRAM3 payload reads. */
#define NP_SD_CHUNK_WRITER 1
#define NP_SD_INTERNAL_CHUNKS 20U
#define NP_TX_UNIT 1U
#define NP_TX_PRIME_CHUNKS 16U
/* Preserve the checked cap: the private64-chunk trial still overran the
 * P12/30k source guard. Longer commands alone did not qualify that mode. */
#define NP_SD_STREAM_MAX_CHUNKS 32U
#define NP_SD_STREAM_MID_CHUNKS 32U
#define NP_SD_MEDIA_METADATA 1
#define NP_SD_BEST_EFFORT 1
static void np_sd_loss_metadata(uint8_t *sector);
#define NP_SD_RECORD_HEALTH(b,s) do { if((s)->config.probe_mask)np_sd_loss_metadata(b); } while(0)
#include "np_camera_exposure.h"
#include "np_media_metadata.h"
#include "np_sd_log.h"
#include "np_sd_stage_timing.h"
#if NP_SD_STAGE_TIMING
/* Foreground-only, stopped-inspection trace; IRQ timestamp already exists. */
static volatile NpSdStageTiming np_sd_stage_timing_diag
    __attribute__((section("NP_SD_TRACE"),aligned(32)));
static void np_sd_stage_timing_launch_current(uint32_t sequence,uint32_t probe);
#endif
static int np_sd_camera_stop(void);
static void np_mic_stop(void);
static uint32_t np_media_stopping;
static uint8_t np_media_profile,np_media_rows,np_media_gain=3U,np_media_auto=1U;
static NpCameraExposure np_media_exposure;
static void np_media_metadata_reset(void);
static void np_media_metadata_arm(uint32_t now);
static void np_media_metadata_stop(uint32_t now);
static void np_media_metadata_latch(void);
static uint32_t np_sd_camera_source_sequence(unsigned slot);
static void np_sd_camera_np_reserved(unsigned slot);
static void np_sd_camera_np_discard(unsigned slot);
static void np_sd_stream_cancel(void);
static void np_sd_stream_reset(void);
#ifndef NP_SD_INTERNAL_FIRST
#define NP_SD_INTERNAL_FIRST 1
#endif
#include "np_sd_dma_chain.h"
#if NP_SD_SENSORS
#include "xe_np_sensors_port.h"
#include "np_sd_sensors.h"
static uint32_t np_sd_sensor_queue_started;
#endif
/* Cumulative, CRC-covered best-effort evidence. Never put this in the
 * capture IRQ or scan the payload ring. Existing media128..295 and FS2
 * allocation binding464..483 retain their ownership. Counts survive STOP. */
static void np_sd_loss_metadata(uint8_t *b)
{
    memcpy(b+320,"NPLOSS01",8);
    np_put32(b+328,1U);
    np_put32(b+332,np3_loss_diag.events);
    np_put32(b+336,np3_loss_diag.resyncs);
    np_put32(b+340,np3_loss_diag.discarded_stages);
    np_put32(b+344,np3_loss_diag.unknown_gaps);
    np_put32(b+348,np3_loss_diag.last_fault);
    np_put32(b+352,np3_loss_diag.last_tick);
    np_put32(b+356,1U); /* skipped counts are lower bounds, NOT exact samples */
    for(unsigned p=0;p<3U;p++){
        uint64_t skipped=np3_loss_diag.skipped_lane_bytes[p];
        np_put32(b+360+p*8U,(uint32_t)skipped);
        np_put32(b+364+p*8U,(uint32_t)(skipped>>32U));
    }
#if NP_SD_SENSORS
    np_put32(b+384,XE_NP_SensorsPort_RecordOverflow());
#endif
}
#ifndef NP_SD_ALLOW_WRITES
#define NP_SD_ALLOW_WRITES 0
#endif
#ifndef NP_SD_PROBE_MASK
#define NP_SD_PROBE_MASK 2U
#endif
#ifndef NP_SD_AP_HZ
#define NP_SD_AP_HZ 15000U
#endif
/* Debug/application mailbox. All fields must be written before command.
 * Start token authorizes only this requested extent; no implicit format/erase.
 * Production CE32 directory allocation and BLE control are not yet integrated. */
volatile struct {
    uint32_t command, inverse, start_lba, end_lba, session;
    uint32_t state, fault, committed_chunks, max_stage_ms, max_write_ms;
    uint32_t sd_error, capture_fault, metadata_saved, enabled, rejected_commands;
    uint32_t fault_sd_state, fault_sd_sta, fault_sd_mask, fault_sd_idma, fault_sd_irq;
    uint32_t total_write_ms;
} np_sd_mailbox;
/* Read-only stopped-session card export. Data is in np_sd_buffers[0].
 * READ is acknowledged as status2 only after a complete successful read. */
volatile struct { uint32_t command, lba, blocks, status, error, bytes; } np_sd_readback;
static NpSdLog np_sd_log;
#if XE_NP_APP_P12315_PAIRED_GPIOE
/* Private cold evidence, captured before abort; tag published last. */
volatile uint32_t np_sd_paired_timeout[13];
static void np_sd_latch_paired_timeout(void)
{
    if(!NP_P12315_PAIRED_GPIOE() || np_sd_log.fault!=NP_SD_TIMEOUT ||
       np_sd_log.staging>=NP_SD_SLOTS || np_sd_paired_timeout[0])return;
    DMA_Channel_TypeDef *ch=GPDMA1_Channel14;
    np_sd_paired_timeout[1]=HAL_GetTick();
    np_sd_paired_timeout[2]=ch->CSR;np_sd_paired_timeout[3]=ch->CCR;
    np_sd_paired_timeout[4]=ch->CTR1;np_sd_paired_timeout[5]=ch->CTR2;
    np_sd_paired_timeout[6]=ch->CBR1;np_sd_paired_timeout[7]=ch->CSAR;
    np_sd_paired_timeout[8]=ch->CDAR;np_sd_paired_timeout[9]=ch->CLLR;
    np_sd_paired_timeout[10]=TIM2->DIER;np_sd_paired_timeout[11]=TIM2->CCR3;
    np_sd_paired_timeout[12]=TIM2->CNT;
    __DMB();np_sd_paired_timeout[0]=0x50535431U; /* PST1 */
}
#endif
#if NP_SD_VSC
static uint8_t np_sd_ru0[524288] __attribute__((section("NP_SD_RU0"),aligned(32)));
static uint8_t np_sd_ru1[851968] __attribute__((section("NP_SD_RU1"),aligned(32)));
#if NP_SD_ELASTIC
#include "np_sd_chunk_pool.h"
static uint8_t *np_sd_buffers[NP_SD_SLOTS];
static NpSdChunkPool np_sd_chunks;
static uint8_t np_sd_chunk_ready[128];
static struct {uint32_t active,slot,target,offset;} np_sd_chunk_copy;
volatile uint32_t np_sd_chunk_diag[6];
static uint8_t np_sd_input[65536] __attribute__((section("NP_SD_INPUT"),aligned(32)));
#include "np_sd_scratch_pair_state.inc"

volatile uint32_t np_sd_test_freeze_stage,np_sd_test_frozen; /* opt-in sequence+1; capture stops, never resumes */
#ifndef NP_SD_TRANSPORT_FINGERPRINT
#define NP_SD_TRANSPORT_FINGERPRINT 0U
#endif
volatile struct {uint32_t input_chunks,psram_checks,bounce_checks,bad_stage,bad_slot,bad_sequence;} np_sd_transport_diag;
#if NP_SD_TRANSPORT_FINGERPRINT
static uint32_t np_sd_fingerprints[NP_SD_SLOTS];
/* Diagnostic fingerprint of the first128 bytes of every time lane. This
 * localizes transport faults without scanning the live GPIO ring or claiming
 * a full-payload CRC. A mismatch stops before the affected RU reaches SD. */
static uint32_t np_sd_fingerprint(const uint8_t *data)
{
    uint32_t h=2166136261U;
    for(unsigned lane=0;lane<4;lane++) {
        const uint32_t *p=(const uint32_t *)(data+lane*NP_SD_LANE_BYTES);
        for(unsigned n=0;n<32;n++)h=(h^p[n])*16777619U;
    }
    return h;
}
volatile uint32_t np_sd_transport_fault[272]; /* cold failure evidence only */
static int np_sd_transport_check(const uint8_t *data,unsigned slot,unsigned stage)
{
    if(np_sd_fingerprint(data)==np_sd_fingerprints[slot])return 0;
    np_sd_transport_diag.bad_stage=stage;np_sd_transport_diag.bad_slot=slot;
    np_sd_transport_diag.bad_sequence=np_sd_log.slots[slot].sequence;
    if(!np_sd_transport_fault[0]) {
        const uint8_t *origin=np_sd_buffers[slot];
        np_sd_transport_fault[1]=stage;np_sd_transport_fault[2]=slot;
        np_sd_transport_fault[3]=np_sd_log.slots[slot].sequence;
        np_sd_transport_fault[4]=np_sd_fingerprints[slot];
        np_sd_transport_fault[5]=np_sd_fingerprint(origin);
        np_sd_transport_fault[6]=np_sd_fingerprint(data);
        np_sd_transport_fault[7]=(uint32_t)origin;np_sd_transport_fault[8]=(uint32_t)data;
        np_sd_transport_fault[9]=np_sd_chunks.physical[slot];
        np_sd_transport_fault[10]=np_sd_chunk_copy.target;
        np_sd_transport_fault[11]=np_sd_chunks.count;np_sd_transport_fault[12]=np_sd_chunks.external;
        np_sd_transport_fault[13]=np_sd_chunks.used[0];
        np_sd_transport_fault[14]=np_sd_chunk_copy.offset;
        np_sd_transport_fault[15]=HAL_GetTick();
        for(unsigned lane=0;lane<4;lane++)for(unsigned word=0;word<32;word++) {
            np_sd_transport_fault[16+lane*64+word]=((const uint32_t *)(origin+lane*NP_SD_LANE_BYTES))[word];
            np_sd_transport_fault[48+lane*64+word]=((const uint32_t *)(data+lane*NP_SD_LANE_BYTES))[word];
        }
        __DMB();np_sd_transport_fault[0]=0x4e505446U;
    }
    return np_sd_fail(&np_sd_log,NP_SD_PROTOCOL);
}
static inline void np_sd_transport_begin(const uint8_t *data,unsigned slot)
{np_sd_fingerprints[slot]=np_sd_fingerprint(data);}
#else
static inline void np_sd_transport_begin(const uint8_t *data,unsigned slot)
{(void)data;(void)slot;}
static inline int np_sd_transport_check(const uint8_t *data,unsigned slot,unsigned stage)
{(void)data;(void)slot;(void)stage;return 0;}
#endif
volatile struct {uint32_t psram_ok,used_rus,peak_rus,overflow_rus,stage_max_cycles,last_stage_cycles,pending_control_ms;} np_sd_elastic_diag;
/* Explicit HIL-only one-shot storage starvation. Zero by default; max250 ms.
 * Does not delay capture/staging or issue any card command. */
volatile uint32_t np_sd_test_hold_ms;
static uint32_t np_sd_hold_active,np_sd_hold_started,np_sd_hold_duration;
volatile struct {uint32_t internal_rus,spill_rus,internal_chunks,spill_chunks,direct_writes;} np_sd_tier_diag;
static int np_sd_pool_reset(void)
{
    _Static_assert(SRAM5_SIZE==851968U,"Fitted part must expose832 KiB SRAM5");
    if((uintptr_t)np_sd_ru1!=SRAM5_BASE_NS || sizeof(np_sd_ru1)!=SRAM5_SIZE)return -1;
    const volatile XE_NP_PsramDiag *d=XE_NP_Psram_GetDiag();
    np_chunk_init(&np_sd_chunks);memset(np_sd_buffers,0,sizeof(np_sd_buffers));
    np_sd_hold_active=0;
    np_sd_scratch_reset();
    memset(np_sd_chunk_ready,0,sizeof(np_sd_chunk_ready));
    memset(&np_sd_chunk_copy,0,sizeof(np_sd_chunk_copy));
    memset((void *)np_sd_chunk_diag,0,sizeof(np_sd_chunk_diag));
    memset((void*)&np_sd_tier_diag,0,sizeof(np_sd_tier_diag));
    memset((void*)&np_sd_transport_diag,0,sizeof(np_sd_transport_diag));

    memset((void*)&np_sd_elastic_diag,0,sizeof(np_sd_elastic_diag));
    if(d->result || (d->flags&0x1fU)!=0x1fU || d->size_bytes<8U*1024U*1024U)return -1;
    /* Exhaustive stopped-only address/data test. SD DMA never accesses this
     * external pool directly; it consumes only the two SRAM bounce buffers. */
    volatile uint32_t *ram=(volatile uint32_t *)XE_NP_PSRAM_BASE_ADDR;
    for(uint32_t i=0;i<8U*1024U*1024U/4;i++)ram[i]=(i*0x9e3779b1U)^0xa55a13c7U;
    __DSB();
    for(uint32_t i=0;i<8U*1024U*1024U/4;i++)if(ram[i]!=((i*0x9e3779b1U)^0xa55a13c7U))return -1;
    /* Exercise the actual optimized memcpy access pattern as well as scalar
     * words. Serial RAM can pass scalar tests but fail burst/page splitting. */
    uint32_t *input=(uint32_t *)np_sd_input;
    for(uint32_t offset=0;offset<8U*1024U*1024U;offset+=4096U) {
        for(uint32_t j=0;j<1024U;j++)input[j]=((offset/4U+j)*0x9e3779b1U)^0x41ce76d2U;
        memcpy((uint8_t *)ram+offset,np_sd_input,4096U);
    }
    __DSB();HAL_Delay(10U);
    for(uint32_t offset=0;offset<8U*1024U*1024U;offset+=4096U) {
        memcpy(np_sd_input,(uint8_t *)ram+offset,4096U);
        for(uint32_t j=0;j<1024U;j++)if(input[j]!=(((offset/4U+j)*0x9e3779b1U)^0x41ce76d2U))return -1;
    }
    np_sd_elastic_diag.psram_ok=1;return np_sd_fast_stage_init();
}
#include "np_sd_chunk_memory.inc"
/* A failed, retired stage was never published to SD. Release only its tail
 * reservation; do not change capture position, probe order or active command. */
static int np_discard_stage(void)
{
    NpSdLog *s=&np_sd_log;unsigned slot=s->staging;
    if((np3_diag[1]!=10U && np3_diag[1]!=14U) || !s->count ||
       slot>=NP_SD_SLOTS || slot!=((s->head+s->count-1U)&(NP_SD_SLOTS-1U)) ||
       s->slots[slot].state!=NP_SD_STAGING || s->writing==slot ||
       s->slots[slot].sequence+1U!=s->next_sequence ||
       s->slots[slot].lba+NP_SD_CHUNK_SECTORS!=s->next_lba ||
       np_sd_chunk_ready[slot] ||
       (np_sd_chunk_copy.active && np_sd_chunk_copy.slot==slot))return 0;
    if(np_sd_scratch_cancel(slot) || np_chunk_release(&np_sd_chunks,slot))return 0;
    np_sd_buffers[slot]=0;
    np_sd_camera_np_discard(slot);
    s->next_lba-=NP_SD_CHUNK_SECTORS;s->next_sequence--;s->count--;
    s->slots[slot].state=NP_SD_FREE;s->staging=NP_SD_NONE;s->state=NP_SD_RUNNING;
    np_chunk_update_diag();return np_discard_overflow();
}

#include "np_sd_scratch_pair_service.inc"
#else
static uint8_t *np_sd_buffers[16]={
    np_sd_ru0,np_sd_ru0+65536,np_sd_ru0+131072,np_sd_ru0+196608,
    np_sd_ru0+262144,np_sd_ru0+327680,np_sd_ru0+393216,np_sd_ru0+458752,
    np_sd_ru1,np_sd_ru1+65536,np_sd_ru1+131072,np_sd_ru1+196608,
    np_sd_ru1+262144,np_sd_ru1+327680,np_sd_ru1+393216,np_sd_ru1+458752};
#endif
#else
static uint8_t np_sd_buffers[NP_SD_SLOTS][NP_SD_CHUNK_BYTES] __attribute__((aligned(32)));
#endif
static uint8_t np_sd_metadata[512] __attribute__((aligned(32)));
static DMA_NodeTypeDef np_sd_nodes[8] __attribute__((aligned(32)));
static DMA_QListTypeDef np_sd_queue;
static DMA_HandleTypeDef np_sd_stage;
#include "np_sd_preset_stage.inc"
/* Foreground rescue for a terminal flag whose IRQ publication was missed.
 * The publisher is bounded register-only work. Its timestamp is a conservative
 * completion upper bound; normal source/physical-retirement checks still own
 * acceptance, and an expired copy is discarded rather than published. */
static uint32_t np_sd_stage_completion_status(void)
{
    const uint32_t terminal=DMA_FLAG_TC|DMA_FLAG_DTE|DMA_FLAG_ULE|DMA_FLAG_USE|DMA_FLAG_TO;
    uint32_t status=np_stage_irq_status;
    if(!(status&terminal) && np_sd_log.config.probe_mask &&
       np_sd_log.staging<NP_SD_SLOTS &&
       np_sd_log.slots[np_sd_log.staging].state==NP_SD_STAGING &&
       (GPDMA1_Channel14->CSR&terminal)) {
        uint32_t primask=__get_PRIMASK();__disable_irq();
        if(!(np_stage_irq_status&terminal))NP_SD_StageIrq();
        status=np_stage_irq_status;
        __set_PRIMASK(primask);
    }
    return status;
}
static XE_NP_AppUsbQuadDmaBatch np_sd_batch;
static uint32_t np_sd_initialized, np_sd_start_ms, np_sd_finalized, np_sd_finalizing;
#if NP_SD_VSC
static int np_sd_vsc_meta_begin(uint32_t lba);
static void np_sd_vsc_meta_end(uint32_t lba,uint32_t start,int error);
#endif
#if !NP_SD_ALLOW_WRITES && NP_SD_CE64 && NP_SD_VSC
#include "np_sd_verify.inc"
#endif

static int np_sd_metadata_write(uint32_t lba, const uint8_t *data)
{
    SD_HandleTypeDef *sd=xe_np_runtime.hsd_card;
    if(!NP_SD_ALLOW_WRITES || lba<np_sd_log.config.start_lba || lba>=np_sd_log.config.end_lba)return -1;
#if NP_SD_VSC
    if(np_sd_vsc_meta_begin(lba))return -1;
#endif
    uint32_t begin=HAL_GetTick();int error=0;
    if(data!=np_sd_metadata)memcpy(np_sd_metadata,data,512);
    XE_NP_App_DCacheClean(np_sd_metadata,512);
    if(HAL_SD_WriteBlocks(sd,np_sd_metadata,lba,1,1000)!=HAL_OK)error=-1;
    uint32_t start=HAL_GetTick();
    while(!error && HAL_SD_GetCardState(sd)!=HAL_SD_CARD_TRANSFER)
        if(HAL_GetTick()-start>=1000U)error=-1;
#if NP_SD_VSC
    np_sd_vsc_meta_end(lba,begin,error);
#else
    (void)begin;
#endif
    return error;
}
static int np_sd_quiesce(void)
{
    np_sd_stream_cancel();
    np_mic_stop();
    int camera_busy=np_sd_camera_stop();
#if NP_SD_SENSORS
    XE_NP_SensorsPort_RecordEnd();np_sd_sensor_queue_started=0;
#endif
    if(np_sd_stage.Instance && (np_sd_stage.Instance->CCR & DMA_CCR_EN))
        (void)HAL_DMA_Abort(&np_sd_stage);
    XE_NP_App_CancelUsbQuadDmaBatch();
    XE_NP_App_ResetUsbWordSession();
#if NP_SD_STORE_V2
    np_store_async_cancel();
#endif
    return camera_busy;
}
#if NP_SD_CE64
#include "np_ce64_firmware.inc"
#if NP_SD_STORE_V2
#include "np_store_v2_binding.h"
static int np_store_payload_context(void)
{
    /* Keep an authenticated failed record's prefix recoverable even after
     * ready/live was cleared. Never invent ownership for a failed Begin. */
    return np_store_selected && np_store_state.epoch && np_store_record_matches() &&
        np_sd_log.config.start_lba==np_store_state.start &&
        np_sd_log.config.session==np_store_state.session;
}
static void np_store_payload_binding(uint8_t *sector)
{
    if(np_store_payload_context())
        (void)nps2_binding_stamp(sector,np_store_state.epoch,np_store_record.index);
}
#endif
#pragma clang section bss="NP_CAMERA_STATE" data="NP_CAMERA_DATA"
#include "np_usb_camera_driver.inc"
#include "np_camera_hardware.inc"
#include "np_sd_camera.inc"
#include "np_sd_microphone.inc"
#include "np_battery_adc2.inc"
#pragma clang section bss="" data=""
#endif
#if NP_SD_VSC
#include "np_sd_vsc.inc"
#endif
static int np_sd_probe_node(uint32_t node[9],unsigned probe,uint32_t source,
                            uint32_t destination,uint32_t bytes)
{
    /* A paired source word emits a halfword; retain word-aligned output
     * segment boundaries required by the existing private-stage planner. */
    if(NP_P12315_PAIRED_GPIOE() && probe<=2U && (bytes&7U))return -1;
    if(np_stage_node(node,(const uint32_t *)&np_sd_stage_template,
                     source,destination,bytes,0))return -1;
    if(NP_P12315_PAIRED_GPIOE() && probe<=2U) {
        /* SBX exchanges the MIDDLE bytes, before PAM truncation:
         * [P1a,P2a,P1b,P2b] -> [P1a,P1b,P2a,P2b]. */
        uint32_t ctr1=node[0];
        ctr1&=~(DMA_CTR1_SDW_LOG2|DMA_CTR1_DDW_LOG2|DMA_CTR1_PAM|
                 DMA_CTR1_SBX|DMA_CTR1_SBL_1|DMA_CTR1_DBL_1);
        ctr1|=DMA_SRC_DATAWIDTH_WORD|DMA_DEST_DATAWIDTH_HALFWORD|
            DMA_EXCHANGE_SRC_BYTE|(probe==2U?DMA_DATA_LEFTALIGN_RIGHTTRUNC:
                                           DMA_DATA_RIGHTALIGN_LEFTTRUNC);
        node[0]=ctr1;
        /* E extraction is an unpaced single-beat memory copy. Timer-paced
         * singles would need125% of available CH3 requests for P123/15k.
         * Channel priority remains2; capture remains3, but in-flight memory
         * accesses still require full capture-integrity qualification. */
        node[1]=(node[1]&~(DMA_CTR2_REQSEL|DMA_CTR2_DREQ|DMA_CTR2_BREQ))|
            DMA_CTR2_SWREQ;
    }
    return 0;
}
static int np_sd_launch_stage(int slot)
{
    /* Never use HAL list initialization to silently suspend/reset a transfer
     * that still owns its destination. Completion must relinquish EN first. */
    unsigned probe=npl_probe(np_sd_log.config.probe_mask,np_sd_camera_source_sequence((unsigned)slot));
    if(NP_Triple_Probe()!=probe)return -1;
    unsigned model=np_probe_identity[probe-1].model;
    if(model!=NP_MODEL_FULL_NP1 && model!=NP_MODEL_NRIC1384)return -1;
    np_sd_log.slots[slot].fixed_word=model==NP_MODEL_FULL_NP1?0x11U:0x22U;
    if(GPDMA1_Channel14->CCR&DMA_CCR_EN)return -1;
    _Static_assert(sizeof(DMA_NodeTypeDef)==9U*sizeof(uint32_t),"Review static linear DMA node ABI");

    uint8_t *stage_buffer;
#if NP_SD_ELASTIC
    if(np_sd_pool_slot((unsigned)slot))return -1;
#if NP_SD_DIRECT_PSRAM
    stage_buffer=np_sd_buffers[slot];
#else
    stage_buffer=np_sd_slot_internal((unsigned)slot)?np_sd_buffers[slot]:np_sd_scratch_begin((unsigned)slot);
    if(!stage_buffer)return -1;
#endif
#else
    stage_buffer=np_sd_buffers[slot];
#endif
    unsigned count=0;
    const unsigned paired_probe=NP_P12315_PAIRED_GPIOE() && probe<=2U;
    for(unsigned lane=0;lane<4;lane++) {
        unsigned offset=0;
        for(unsigned part=0;part<np_sd_batch.source_segment_count[lane];part++) {
            uint32_t n=np_sd_batch.source_bytes[lane][part];
            uint32_t output=paired_probe?n/2U:n;
            if(count>=8U || !n || (n&3U) || output>NP_SD_LANE_BYTES-offset)return -1;
            uint32_t destination=(uint32_t)&stage_buffer[NP_SD_PAYLOAD_OFFSET+lane*NP_SD_LANE_BYTES+offset];
            if(np_sd_probe_node((uint32_t *)&np_sd_nodes[count],probe,
                (uint32_t)np_sd_batch.source[lane][part],destination,n))return -1;
            if(count)np_sd_nodes[count-1U].LinkRegisters[5]=NP_STAGE_LINK_UPDATE|((uint32_t)&np_sd_nodes[count]&DMA_CLLR_LA);
            count++; offset+=output;
        }
        if(offset!=NP_SD_LANE_BYTES)return -1;
    }
    if(count<4U || count>8U)return -1; /* Every final node has an explicit null tail. */
#if NP_SD_CE64
    /* Keep the validated capture batch geometry. The final seven sectors of
     * the CE64 ephys allocation are declared transport padding, not samples. */
#if !NP_SD_ELASTIC
    memset(stage_buffer+NP_SD_PAYLOAD_BYTES,0,65024U-NP_SD_PAYLOAD_BYTES);
#endif
    np_sd_header(stage_buffer+61632U,&np_sd_log,2,(uint32_t)slot);
    np_ce64_misc(stage_buffer+65024U,np_sd_log.slots[slot].sequence,HAL_GetTick(),1U,np_sd_log.config.sample_bytes);
#if NP_SD_SENSORS
    if(!np_sd_sensor_queue_started){XE_NP_SensorsPort_RecordBegin();np_sd_sensor_queue_started=1;}
    np_sd_sensor_record(stage_buffer+NP_SD_PAYLOAD_BYTES,
        XE_NP_SensorsPort_RecordSnapshot(),np_sd_log.slots[slot].sequence,HAL_GetTick());
#endif
#else
    np_sd_header(stage_buffer,&np_sd_log,2,(uint32_t)slot);
#if NP_SD_VSC
    memset(stage_buffer+512U+NP_SD_PAYLOAD_BYTES,0,NP_SD_CHUNK_BYTES-512U-NP_SD_PAYLOAD_BYTES);
#endif
#endif
    XE_NP_App_DCacheClean(stage_buffer,NP_SD_CHUNK_BYTES);
    XE_NP_Capture_DCacheInvalidate(stage_buffer,NP_SD_CHUNK_BYTES);
    XE_NP_App_DCacheClean(np_sd_nodes,sizeof(np_sd_nodes));
    __DSB();

    __DSB();
    NP_SD_STAGE_TIMING_ONLY(np_sd_stage_timing_launch_current(
        np_sd_log.slots[slot].sequence,probe));
    return np_sd_fast_stage_start();
}
#if NP_SD_USB_DOWNLOAD
#if NP_SD_ALLOW_WRITES
#error USB download personality must be read-only
#endif
uint32_t NP_SD_DownloadCapacity(void)
{
    return np_sd_initialized && !xe_np_sd_init_failed && xe_np_runtime.hsd_card
        ?xe_np_runtime.hsd_card->SdCard.LogBlockNbr:0;
}
uint8_t *NP_SD_DownloadBuffer(unsigned slot)
{
    /* Stopped-only USB personality: this private SRAM1 RU is not shared with
     * a recorder. It remains owned by USB until endpoint completion/abort. */
    return slot?np_sd_ru1:np_sd_ru0;
}
int NP_SD_DownloadRead(uint8_t *data,uint32_t lba,uint32_t blocks)
{
    uint32_t capacity=NP_SD_DownloadCapacity();
    if(!capacity || !blocks || blocks>1020 || lba>=capacity || blocks>capacity-lba ||
       np_sd_log.state!=NP_SD_IDLE || np_sd_verify_diag.state==1)return -1;
    return HAL_SD_ReadBlocks(xe_np_runtime.hsd_card,data,lba,blocks,1000)==HAL_OK?0:-2;
}
#if NP_SD_USB_FUSED_CRC
#include "np_sd_read_crc.inc"
int NP_SD_DownloadReadCrc(uint8_t *data,uint32_t lba,uint32_t blocks,uint32_t *crc)
{
    if(!NP_SD_DownloadCapacity() || np_sd_log.state!=NP_SD_IDLE || np_sd_verify_diag.state==1)return -1;
    return np_sd_read_crc(xe_np_runtime.hsd_card,data,lba,blocks,crc);
}
#endif
#endif
#include "sd_modes.inc"
int NP_ConfigBusy(void){return NP_SD_ControlTransferPending() || !np_sd_initialized ||
    (np_sd_log.state!=NP_SD_IDLE && !np_sd_finalized) || np_sd_mailbox.command ||
    np_sd_readback.command || np_v1_mode_mailbox.target;}
void NP_ConfigRearm(void){np_sd_quiesce();}

#include "np_sd_stream.inc"
#if NP_SD_STORE_V2
#include "np_store_v2_async.inc"
#endif
#if NP_SD_STAGE_TIMING
static void np_sd_stage_timing_launch_current(uint32_t sequence,uint32_t probe)
{
    if(!np_sd_stage_timing_diag.frozen)
        np_sd_stage_timing_start(&np_sd_stage_timing_diag,DWT->CYCCNT,sequence,probe,
            np_sd_stream_diag.published,np_stream_consumed);
}
#endif
#include "np_sd_control.inc"
#include "np_media_capture_metadata.inc"
#if NP_SD_CE64
/* Immutable after the first successful boot identity capture. A failed HAL
 * reinit may change SdCard/CID; it must never approve a replacement card.
 * With no valid card at boot this session cannot adopt/format an unknown card. */
static struct {uint32_t capacity,cid[4];} np_sd_maintenance_identity;
static int np_sd_maintenance_capture_boot(void)
{
    SD_HandleTypeDef *sd=xe_np_runtime.hsd_card;
    if(np_sd_maintenance_identity.capacity || !sd || sd!=&hsd1 || xe_np_sd_init_failed ||
       sd->State!=HAL_SD_STATE_READY || sd->ErrorCode || sd->SdCard.LogBlockNbr<=NP_CE64_DATA ||
       !(sd->CID[0]|sd->CID[1]|sd->CID[2]|sd->CID[3]))return -1;
    memcpy(np_sd_maintenance_identity.cid,sd->CID,sizeof(sd->CID));
    np_sd_maintenance_identity.capacity=sd->SdCard.LogBlockNbr;
    return 0;
}
static int np_sd_maintenance_ready(void)
{
    SD_HandleTypeDef *sd=xe_np_runtime.hsd_card;
    if(!npl_idle() || !sd || sd!=&hsd1 || !np_sd_maintenance_identity.capacity ||
       xe_np_quad_dma_active || np_sd_camera.enabled || USB512_Camera_IsBusy() ||
       np_sd_mailbox.command || np_sd_readback.command || NP_ConfigTransferPending() ||
       npl_mode_target || npl_schedule.active || npl_delete_phase ||
       (SDMMC1->STA&(SDMMC_STA_DPSMACT|SDMMC_STA_CPSMACT)) ||
       (SDMMC1->IDMACTRL&SDMMC_IDMA_IDMAEN))return -1;
    for(unsigned attempt=0;attempt<2U;attempt++) {
        if(!xe_np_sd_init_failed && sd->State==HAL_SD_STATE_READY && !sd->ErrorCode &&
           sd->SdCard.LogBlockNbr==np_sd_maintenance_identity.capacity &&
           !memcmp(sd->CID,np_sd_maintenance_identity.cid,sizeof(sd->CID)) &&
           !(SDMMC1->STA&(SDMMC_STA_DPSMACT|SDMMC_STA_CPSMACT)) &&
           !(SDMMC1->IDMACTRL&SDMMC_IDMA_IDMAEN) &&
           HAL_SD_GetCardState(sd)==HAL_SD_CARD_TRANSFER)return 0;
        /* An explicit stopped maintenance request may recover the host once,
         * before catalog/format I/O. Never replay a failed metadata write or
         * continue a destructive request on a replacement card. */
        if(attempt || XE_NP_Board_RetrySdInit() || xe_np_sd_init_failed)return -1;
    }
    return -1;
}
#endif
extern CE32_INTERCOM_Handle *volatile np_ble_poll_owner;
void NP_BlePollTxService(void);
static uint8_t np_sd_led_recording;
#include "np_sd_idle_sleep.inc"
#if defined(NP_SD_SINK_PROBE) && NP_SD_SINK_PROBE
/* Explicit stopped-only mailbox image, never enabled in release builds. */
#include "../../../../Tools/V1Capture/SdModes/np_sd_bandwidth.inc"
#endif
static void XE_NP_App_SdLoggerService(void)
{
#if defined(NP_SD_SINK_PROBE) && NP_SD_SINK_PROBE
    if(!np_sd_bandwidth.admitted && np_sd_bandwidth.command)np_bw_service();
    /* One admitted sink probe per reset. Queued controls cannot start capture
     * after its terminal result; rejected requests leave normal service alone. */
    if(np_sd_bandwidth.admitted)return;
#endif
#if NP_SD_ELASTIC
    if(np_sd_test_frozen)return;
#endif
    uint8_t np_sd_control_due=0U;
    const uint8_t np_sd_defer_control =
        (np_sd_log.state == NP_SD_RUNNING || np_sd_log.state == NP_SD_DRAINING);
    /* Foreground edge only: running/draining stays dark. */
    if(np_sd_led_recording != np_sd_defer_control) {
        np_sd_led_recording = np_sd_defer_control;
        XE_NP_App_SetRecordingIndicator(np_sd_defer_control);
    }
    if(!np_sd_defer_control) {
        /* Camera ownership advances before variable-time controls, even idle.
         * This service does not depend on a BLE client or preview request. */
        np_media_camera_idle_service(HAL_GetTick());
        NP_ConfigService();
        npl_service();
    }
    np_v1_sd_mode_service();
    SD_HandleTypeDef *sd=xe_np_runtime.hsd_card;
    if(!np_sd_initialized) {
        np_sd_initialized=1; np_sd_quiesce();
        if(!np_mode_mask && !np_mode_hz){np_media_restore_settings();npl_sync_system(0U,0U);}
#if NP_SD_SENSORS
        XE_NP_SensorsPort_Init(&hi2c5,&hi2c6);
#endif
#if NP_SD_VSC
        __HAL_RCC_SRAM5_CLK_ENABLE();
        memset(np_sd_ru0,0,sizeof(np_sd_ru0));memset(np_sd_ru1,0,sizeof(np_sd_ru1));
        XE_NP_App_DCacheClean(np_sd_ru0,sizeof(np_sd_ru0));
        XE_NP_App_DCacheClean(np_sd_ru1,sizeof(np_sd_ru1));
#endif
        memset(&np_camera_preview,0,sizeof(np_camera_preview));
        memset(&np_camera_full,0,sizeof(np_camera_full));
        np_sd_mailbox.enabled=NP_SD_ALLOW_WRITES;
#if NP_SD_CE64
        if(!np_sd_maintenance_capture_boot())(void)np_ce64_mount_card();
        else {np_ce64_diag.state=4;np_ce64_diag.error=1;}
#endif
    }
    if(np_sd_readback.command) {
        uint32_t request=np_sd_readback.command;
        np_sd_readback.command=0; np_sd_readback.status=1;
        np_sd_readback.bytes=0; np_sd_readback.error=0;
        if(request!=0x52454144U || !sd || xe_np_sd_init_failed ||
           (np_sd_log.state!=NP_SD_IDLE && !np_sd_finalized) ||
#if !NP_SD_ALLOW_WRITES && NP_SD_CE64 && NP_SD_VSC
           np_sd_verify_diag.state==1U || /* verifier owns the same private RU */
#endif
           !np_sd_readback.blocks || np_sd_readback.blocks>120U ||
           np_sd_readback.lba>=sd->SdCard.LogBlockNbr ||
           np_sd_readback.blocks>sd->SdCard.LogBlockNbr-np_sd_readback.lba) {
            np_sd_readback.status=3; np_sd_readback.error=0xFFFFFFFFU;
        } else if(HAL_SD_ReadBlocks(sd,
#if NP_SD_VSC
            np_sd_ru0,
#else
            np_sd_buffers[0],
#endif
            np_sd_readback.lba,np_sd_readback.blocks,1000)!=HAL_OK) {
            np_sd_readback.status=3; np_sd_readback.error=sd->ErrorCode;
        } else {
            /* Polling HAL populated this buffer through CPU stores. Clean,
             * do not invalidate those stores before the debugger reads RAM. */
            XE_NP_App_DCacheClean(
#if NP_SD_VSC
                np_sd_ru0,
#else
                np_sd_buffers[0],
#endif
                np_sd_readback.blocks*512U);
            __DMB(); np_sd_readback.bytes=np_sd_readback.blocks*512U; np_sd_readback.status=2;
        }
    }
    uint32_t command=np_sd_mailbox.command;
    /* NP_SD_RELEASE_MAILBOX_START: direct and BLE/common START use the
     * same normal mode/catalog/extent/ownership admission. Qualification
     * is status information, not a private-HIL extent requirement. */

    if(command && command!=0x53544F50U && NP_ConfigTransferPending()){
        if((command&0xFFFFFF00U)==0x44454C00U)npl_delete_complete(-1);
        np_sd_mailbox.command=0;np_sd_mailbox.rejected_commands++;command=0;
    }

#if NP_SD_CE64
    if((command&0xFFFFFF00U)==0x44454C00U && np_sd_mailbox.inverse==~command &&
       (np_sd_log.state==NP_SD_IDLE || np_sd_finalized)) {
        if(np_sd_camera_stop())return; /* retain Delete until DMA retirement */
        uint8_t count=(uint8_t)command;
        np_sd_mailbox.command=0;command=0;
        npl_delete_complete(NP_CE64_DeleteRecords(count));
    }
    if(command==0x4D4F554EU && np_sd_mailbox.inverse==~command &&
       (np_sd_log.state==NP_SD_IDLE || np_sd_finalized)) {
        if(np_sd_camera_stop())return; /* retain pending Mount during DMA retirement */
        np_sd_mailbox.command=0;command=0;
        np_sd_quiesce();
        if(!sd || np_sd_maintenance_ready()){np_ce64_diag.state=4;np_ce64_diag.error=1;}
        else (void)np_ce64_mount_card();
    }
    if((command==0x464D4154U
#if NP_SD_STORE_V2
        || command==0x464D4132U
#endif
       ) && np_sd_mailbox.inverse==~command &&
       (np_sd_log.state==NP_SD_IDLE || np_sd_finalized)) {
        /* Keep the accepted command pending until asynchronous camera DMA
         * retirement completes; idle preview cannot reopen while it is queued. */
        if(np_sd_camera_stop())return;
        uint32_t format_command=command;
        np_sd_mailbox.command=0;command=0;
        /* This user-confirmed maintenance command is idle-only. Release camera
         * and DMA ownership before synchronous metadata writes. The selected
         * idle camera reopens on the next ordinary service visit. */
        np_sd_quiesce();
        int format_error=-1;
        if(NP_SD_ALLOW_WRITES && sd && !np_sd_maintenance_ready()){
#if NP_SD_STORE_V2
            format_error=format_command==0x464D4132U?np_store_format():np_ce64_format_card();
#else
            (void)format_command;format_error=np_ce64_format_card();
#endif
        }
        if(format_error)
            {np_ce64_diag.state=4;np_ce64_diag.error=2;}
    }
#endif
    if(command && (np_sd_mailbox.inverse!=~command ||
       (command!=0x53544152U && command!=0x53544F50U) ||
       (command==0x53544152U && np_sd_log.state!=NP_SD_IDLE && !np_sd_finalized))) {
        /* Never leave a rejected START queued to overwrite this extent later. */
        if((command&0xFFFFFF00U)==0x44454C00U)npl_delete_complete(-1);
        np_sd_mailbox.command=0; np_sd_mailbox.rejected_commands++; command=0;
    }
    /* Optional sensor failures disable only their sensor fields/stream.
     * Missing IMU/BME data must never reject a neural recording START. */
    if(command==0x53544152U && np_sd_mailbox.inverse==~command &&
       (np_sd_log.state==NP_SD_IDLE || np_sd_finalized)) {
        if(np_sd_camera_stop())return; /* retain START until camera DMA retires */
        np_sd_mailbox.command=0; np_sd_finalized=0;np_sd_finalizing=0;
#if XE_NP_APP_P12315_PAIRED_GPIOE
        memset((void *)np_sd_paired_timeout,0,sizeof(np_sd_paired_timeout));
#endif
        np_ble_preview_stop(&npl_preview);npl_preview_origin_mask=0;
        if(!npl_schedule.active){memset(&npl_schedule,0,sizeof(npl_schedule));npl_schedule.clock=npl_online_clock;}
        np_sd_mailbox.metadata_saved=0; np_sd_mailbox.sd_error=0; np_sd_mailbox.capture_fault=0;
        NpSdConfig c={np_sd_mailbox.start_lba,np_sd_mailbox.end_lba,np_sd_mailbox.session,
            NP_SD_PROBE_MASK,1U,NP_SD_AP_HZ,6,250};
#if NP_SD_CE64
        if(!NP_SD_ALLOW_WRITES || !sd || xe_np_sd_init_failed || np_ce64_mount_card() ||
           (c.start_lba && c.start_lba!=np_ce64_dir.start)) {
            np_sd_fail(&np_sd_log,NP_SD_CONFIG);np_sd_finalized=1;
            np_sd_mailbox.state=np_sd_log.state;np_sd_mailbox.fault=np_sd_log.fault;return;
        }
        c.start_lba=np_ce64_dir.start;np_sd_mailbox.start_lba=c.start_lba;
#endif
        if(!NP_SD_ALLOW_WRITES || !sd || xe_np_sd_init_failed ||
           np_sd_init(&np_sd_log,&c,sd->SdCard.LogBlockNbr)!=0) {
            np_sd_fail(&np_sd_log,NP_SD_CONFIG);
        } else {
            np_sd_quiesce();
            np3_loss_reset(); /* new session, before its pessimistic footer */
            np_media_metadata_reset();
            np_sd_stream_reset();
            NP_SD_STAGE_TIMING_ONLY(np_sd_stage_timing_reset(&np_sd_stage_timing_diag));
#if NP_SD_ELASTIC
            if(np_sd_pool_reset())np_sd_fail(&np_sd_log,NP_SD_CONFIG);
#endif
#if NP_SD_VSC
            if(np_sd_vsc_check(&c)!=0)np_sd_fail(&np_sd_log,NP_SD_CONFIG);
#endif
            if(!np_sd_log.fault)np_sd_camera_prepare(); /* before all session headers */
            if(!np_sd_log.fault && np_mic_prepare())np_sd_fail(&np_sd_log,NP_SD_SOURCE);
            if(!c.probe_mask && !np_sd_camera.enabled && !np_mic.armed)np_sd_fail(&np_sd_log,NP_SD_CONFIG);
            /* Invalidate any old completion BEFORE changing this extent's
             * header or payload, even if a caller mistakenly reuses session ID. */
#if NP_SD_STORE_V2
            /* New grants are durably owned before even the initial footer.
             * Legacy ordering remains unchanged on legacy cards. */
            if(!np_sd_log.fault && np_store_selected && np_store_begin(c.end_lba,c.session))
                np_sd_fail(&np_sd_log,NP_SD_WRITE);
#endif
            np_sd_header(np_sd_metadata,&np_sd_log,3,0);
            if(!np_sd_log.fault && np_sd_metadata_write(c.start_lba+4,np_sd_metadata)!=0)
                np_sd_fail(&np_sd_log,NP_SD_WRITE);
            /* CE32 system/DSP sectors plus NP extension and pessimistic footer.
             * Directory ownership is reserved after the initial incomplete footer.
             * All metadata writes occur before starting capture. */
#if NP_SD_CE64
            if(!np_sd_log.fault &&
#if NP_SD_STORE_V2
               !np_store_selected &&
#endif
               np_ce64_reserve(&np_ce64_dir,c.end_lba,c.session,sd->SdCard.LogBlockNbr,np_ce64_write))
                np_sd_fail(&np_sd_log,NP_SD_WRITE);
            if(!np_sd_log.fault)np_ce64_sync_runtime_log();
#endif
            XE_NP_Runtime_StampRtcNow(&xe_np_runtime,
                &xe_np_runtime.sys_param.date,&xe_np_runtime.sys_param.time);
            xe_np_runtime.sys_param.unassigned_32=0x4E500000U|(NP_SD_PROBE_MASK<<8)|0x0FU;
#if NP_SD_CE64
            np_ce64_system(&xe_np_runtime.sys_param);
#endif
            memset(np_sd_metadata,0,512);
            _Static_assert(sizeof(xe_np_runtime.sys_param)<=512,"CE32 system sector overflow");
            memcpy(np_sd_metadata,&xe_np_runtime.sys_param,sizeof(xe_np_runtime.sys_param));
            if(!np_sd_log.fault && np_sd_metadata_write(c.start_lba,np_sd_metadata)!=0)np_sd_fail(&np_sd_log,NP_SD_WRITE);
            memset(np_sd_metadata,0,512);
            memcpy(np_sd_metadata,&xe_np_runtime.dsp_param[0],sizeof(xe_np_runtime.dsp_param[0]));
            if(!np_sd_log.fault && np_sd_metadata_write(c.start_lba+1,np_sd_metadata)!=0)
                np_sd_fail(&np_sd_log,NP_SD_WRITE);
            memset(np_sd_metadata,0,512);
            memcpy(np_sd_metadata,&xe_np_runtime.dsp_param[1],sizeof(xe_np_runtime.dsp_param[1]));
            if(!np_sd_log.fault && np_sd_metadata_write(c.start_lba+2,np_sd_metadata)!=0)
                np_sd_fail(&np_sd_log,NP_SD_WRITE);
            np_sd_header(np_sd_metadata,&np_sd_log,1,0);
            if(!np_sd_log.fault && np_sd_metadata_write(c.start_lba+3,np_sd_metadata)!=0)np_sd_fail(&np_sd_log,NP_SD_WRITE);
#if NP_SD_VSC
            if(!np_sd_log.fault && np_sd_vsc_start()!=0)np_sd_fail(&np_sd_log,NP_SD_WRITE);
#endif
            np_sd_mailbox.metadata_saved=0;
            np_sd_start_ms=HAL_GetTick();
            if(!np_sd_log.fault && c.probe_mask && XE_NP_App_StartUsbCapture(0)!=0)np_sd_fail(&np_sd_log,NP_SD_SOURCE);
        }
    }
    if(command==0x53544F50U && np_sd_mailbox.inverse==~command) {
        np_sd_mailbox.command=0;
        if(np_sd_log.config.probe_mask && !xe_np_probe_state.initialized)np_sd_quiesce(); /* camera-only drains privately */
        if(!np_sd_log.config.probe_mask)np_media_stop_request();else np_sd_stop(&np_sd_log);
    }
    if(np_sd_log.state==NP_SD_RUNNING || np_sd_log.state==NP_SD_DRAINING) {
        uint32_t now=HAL_GetTick();
        if(!np_sd_log.config.probe_mask){
            if(np_sd_log.state==NP_SD_DRAINING && !np_media_stopping){np_media_stopping=1;np_mic_stop_capture();USB512_Camera_StopCapture();np_media_metadata_stop(now);}
            np_tx_deadlines(&np_sd_log,&np_sd_transaction,now);
            if(!npl_schedule.valid && !np_media_stopping){int gate=NP_SD_BleArmGate(now);
                if(gate<0)np_sd_fail(&np_sd_log,NP_SD_SOURCE);
                if(gate>0){if(np_mic_arm(now))np_sd_fail(&np_sd_log,NP_SD_SOURCE);
                else np_media_metadata_arm(now);NP_SD_BleArmStamp(now);}}
            if(npl_schedule.valid || np_media_stopping){np_sd_stream_service(now);np_mic_service(now);np_sd_camera_service(now);np_media_drain(now);
                if(np_sd_camera.disabled && !np_mic.armed)np_sd_fail(&np_sd_log,NP_SD_SOURCE);}
        }else if(!xe_np_probe_state.initialized) {
            /* STOP before capture must never release the probe reset. */
            if(np_sd_log.state==NP_SD_RUNNING)XE_NP_App_ServiceProbeBringUp(&xe_np_runtime);
            if(!xe_np_probe_state.initialized && np_ble_start_expired(&npl_schedule,HAL_GetTick(),np_sd_start_ms))np_sd_fail(&np_sd_log,NP_SD_SOURCE);
        } else {
            if(XE_NP_App_UsbWordFault())np_sd_fail(&np_sd_log,NP_SD_SOURCE);
#if NP_SD_SENSORS
            if(XE_NP_SensorsPort_RecordFault())np_sd_fail(&np_sd_log,NP_SD_PROTOCOL);
#endif
            if(!np_sd_log.fault && np_sd_log.staging<NP_SD_SLOTS) {
                uint32_t csr=np_sd_stage_completion_status();
                NP_SD_STAGE_TIMING_ONLY(if(!np_sd_stage_timing_diag.frozen && (csr&DMA_FLAG_TC))
                    np_sd_stage_timing_complete(&np_sd_stage_timing_diag,
                        np_stage_complete_cycles,DWT->CYCCNT,
                        np_sd_stream_diag.published,np_stream_consumed));
                if(csr&(DMA_FLAG_DTE|DMA_FLAG_ULE|DMA_FLAG_USE|DMA_FLAG_TO))np_sd_fail(&np_sd_log,NP_SD_STAGE);
                else if((csr&DMA_FLAG_TC) && (GPDMA1_Channel14->CSR&DMA_CSR_IDLEF) &&
                        !(GPDMA1_Channel14->CCR&DMA_CCR_EN) &&
                        !(GPDMA1_Channel14->CBR1&DMA_CBR1_BNDT) && !GPDMA1_Channel14->CLLR) {
                    __DMB();
                    if(XE_NP_App_CommitUsbQuadDmaBatch(&np_sd_batch)!=0){if(!np_discard_stage())np_sd_fail(&np_sd_log,NP_SD_SOURCE);}
                    else {
#if NP_SD_ELASTIC
                        unsigned sequence=np_sd_log.slots[np_sd_log.staging].sequence;
                        if(np_sd_test_freeze_stage==sequence+1U) {
                            np_sd_test_freeze_stage=0;XE_NP_App_StopCaptureDma();
                            np_sd_fail(&np_sd_log,NP_SD_SOURCE);np_sd_test_frozen=1;return;
                        }
#if NP_SD_DIRECT_PSRAM
                        np_sd_transport_begin(np_sd_buffers[np_sd_log.staging],np_sd_log.staging);
                        np_sd_transport_diag.input_chunks++;
                        np_sd_stage_done(&np_sd_log,1,now);
#else
                        if(np_sd_slot_internal(np_sd_log.staging)) {
                            uint8_t *data=np_sd_buffers[np_sd_log.staging];
                            XE_NP_Capture_DCacheInvalidate(data,65536U);
                            np_sd_transport_begin(data,np_sd_log.staging);
                            npl_preview_tap(data,np_sd_log.staging);
                            np_sd_transport_diag.input_chunks++;np_sd_tier_diag.internal_chunks++;
                            np_sd_stage_done(&np_sd_log,1,now);
                        }else{
                            unsigned slot=np_sd_log.staging;
                            uint8_t *data=np_sd_buffers[slot];
                            XE_NP_Capture_DCacheInvalidate(data,65536U);
                            np_sd_transport_begin(data,slot);
                            npl_preview_tap(data,np_sd_log.staging);
                            np_sd_transport_diag.input_chunks++;
                            if(!np_sd_scratch_complete(slot))
                                (void)np_sd_stage_done(&np_sd_log,1,now);
                        }
#endif
#else
                        np_sd_stage_done(&np_sd_log,1,now);
#endif
                    }
                }
            }
            /* Retire/validate a completed stage (or discard its unsafe tail)
             * before treating late foreground observation as a stuck DMA.
             * An active/unretired stage and SD write still keep deadlines. */
            np_tx_deadlines(&np_sd_log,&np_sd_transaction,now);
            /* Once the admitted end boundary has been copied privately, no
             * more GPIO history is needed while the last SD writes drain. */
            if(np_sd_log.state==NP_SD_DRAINING && np_sd_log.staging==NP_SD_NONE && xe_np_quad_dma_active) {
                XE_NP_App_StopCaptureDma();
#if NP_SD_SENSORS
                XE_NP_SensorsPort_RecordEnd();
#endif
            }
            NP_SD_STAGE_TIMING_ONLY(if(!np_sd_stage_timing_diag.frozen && !np_sd_log.fault &&
                np_sd_log.state==NP_SD_RUNNING && np_sd_log.staging==NP_SD_NONE) {
                unsigned scratch_full=(unsigned)np_sd_scratch_full();
                uint32_t reasons=np_sd_stage_timing_admission(np_sd_log.count<NP_SD_SLOTS,
                    (unsigned)np_chunk_can_stage(&np_sd_chunks,(int)scratch_full),scratch_full);
                np_sd_stage_timing_wait(&np_sd_stage_timing_diag,DWT->CYCCNT,reasons,
                    np_sd_stream_diag.published,np_stream_consumed);
            });
            if(!np_sd_log.fault && np_sd_log.state==NP_SD_RUNNING && np_sd_log.count<NP_SD_SLOTS && np_sd_log.staging==NP_SD_NONE && np_chunk_can_stage(&np_sd_chunks,np_sd_scratch_full())) {
                int ready=XE_NP_App_BeginUsbQuadDmaBatch(NP_SD_LANE_BYTES,&np_sd_batch);
                NP_SD_STAGE_TIMING_ONLY(if(!np_sd_stage_timing_diag.frozen && !ready)
                    np_sd_stage_timing_wait(&np_sd_stage_timing_diag,DWT->CYCCNT,
                        NP_SD_STAGE_TIMING_SOURCE_WAIT,np_sd_stream_diag.published,np_stream_consumed));
                if(ready<0)np_sd_fail(&np_sd_log,NP_SD_SOURCE);
                else if(ready>0) {
                    int slot=np_sd_reserve(&np_sd_log,now);
                    if(slot<0)XE_NP_App_CancelUsbQuadDmaBatch();
                    else if((np_sd_camera_np_reserved((unsigned)slot),np_sd_launch_stage(slot))!=0)np_sd_fail(&np_sd_log,NP_SD_STAGE);
                }
            }
            np_sd_stream_service(now);
            np_sd_camera_service(now);
        }
    }
    if((np_sd_log.state==NP_SD_FAILED || np_sd_log.state==NP_SD_COMPLETE) && !np_sd_finalized) {
#if NP_SD_STORE_V2
        /* Normal STOP retires an already-started grow job before finalization.
         * On a fault, cold quiesce abandons it without reclaiming its grant. */
        if(!np_sd_log.fault && np_store_selected &&
           np_store_grow.phase>=1U && np_store_grow.phase<=8U &&
           np_store_async_service(HAL_GetTick()))return;
#endif
        if(!np_sd_finalizing) {
        NP_SD_STAGE_TIMING_ONLY(np_sd_stage_timing_freeze(&np_sd_stage_timing_diag,
            np_sd_log.fault,np3_diag[1]));
#if XE_NP_APP_P12315_PAIRED_GPIOE
        np_sd_latch_paired_timeout();
#endif
        np_sd_mailbox.capture_fault=XE_NP_App_UsbWordFault();
        np_sd_mailbox.sd_error=sd?sd->ErrorCode:0;
        np_sd_mailbox.fault_sd_state=sd?sd->State:0;
        np_sd_mailbox.fault_sd_sta=SDMMC1->STA;
        np_sd_mailbox.fault_sd_mask=SDMMC1->MASK;
        np_sd_mailbox.fault_sd_idma=SDMMC1->IDMACTRL;
        np_sd_mailbox.fault_sd_irq=NVIC_GetEnableIRQ(SDMMC1_IRQn);
        np_media_metadata_latch();
        np_sd_finalizing=1;
        }
        /* Keep first-fault evidence frozen while asynchronous camera DMA
         * retires. No settings, footer or catalog I/O may race its owner. */
        if(np_sd_quiesce())return;
        np_media_save_settings();
        if(sd && np_sd_log.writing<NP_SD_SLOTS)(void)HAL_SD_Abort(sd);
        /* Never write completion metadata if configuration/range was rejected.
         * A failed footer write leaves the initial INCOMPLETE marker intact. */
        if(NP_SD_ALLOW_WRITES && np_sd_log.fault!=NP_SD_CONFIG
#if NP_SD_STORE_V2
           && (!np_store_selected || np_store_payload_context())
#endif
          ) {
            if(npl_save_clock()!=0)np_sd_fail(&np_sd_log,NP_SD_WRITE);
            np_sd_header(np_sd_metadata,&np_sd_log,3,0);
            if(np_sd_metadata_write(np_sd_log.config.start_lba+4,np_sd_metadata)==0)
                np_sd_mailbox.metadata_saved=1;
            else np_sd_fail(&np_sd_log,NP_SD_WRITE);
        }
#if NP_SD_CE64
#if NP_SD_STORE_V2
        if(np_store_selected){
            if(np_store_finish(np_sd_log.config.start_lba+np_sd_header_sectors(&np_sd_log.config)+
                np_sd_log.committed_chunks*NP_SD_CHUNK_SECTORS,
                np_sd_log.state==NP_SD_COMPLETE && np_sd_mailbox.metadata_saved)){
                np_ce64_diag.state=2U;np_ce64_diag.error=3U;np_sd_fail(&np_sd_log,NP_SD_WRITE);
            }
        }else
#endif
        if(np_ce64_dir.active && np_ce64_finish(&np_ce64_dir,
            np_sd_log.config.start_lba+np_sd_header_sectors(&np_sd_log.config)+np_sd_log.committed_chunks*NP_SD_CHUNK_SECTORS,
            np_sd_log.state==NP_SD_COMPLETE && np_sd_mailbox.metadata_saved,np_ce64_write)) {
            np_ce64_diag.state=4;np_ce64_diag.error=3;np_sd_fail(&np_sd_log,NP_SD_WRITE);
        }
        if(!np_ce64_dir.active)np_ce64_sync_runtime_log();
#endif
#if NP_SD_VSC
        /* Keep DIR registrations through footer and CE64 journal/sysLog
         * publication. Release only after all final single-sector writes. */
        if(np_sd_vsc_stop()!=0) {
            np_sd_fail(&np_sd_log,NP_SD_WRITE);np_sd_mailbox.metadata_saved=0;
            /* Best-effort invalidation if release/flush failed after a COMPLETE
             * footer. Failure to persist it remains an explicit host fault. */
            np_sd_header(np_sd_metadata,&np_sd_log,3,0);
            if(
#if NP_SD_STORE_V2
               (!np_store_selected || np_store_payload_context()) &&
#endif
               np_sd_metadata_write(np_sd_log.config.start_lba+4,np_sd_metadata)==0)
                np_sd_mailbox.metadata_saved=1;
        }
#endif
        /* Refresh the next append extent after journal publication. The old
         * mailbox-only app refreshed it on START; a BLE host needs it BEFORE
         * calculating its bounded recording extent. No live capture remains. */
        if(sd && !xe_np_sd_init_failed && !np_ce64_dir.active && np_ce64_diag.state==1U)
            (void)np_ce64_mount_card();
        np_sd_finalized=1;
    }
#if NP_SD_ELASTIC
    /* One bounded private-buffer copy precedes variable-time controls.
     * Publish its completed SRAM chunk immediately so an active SD command
     * need not wait another foreground iteration for its next descriptor. */
    const uint32_t np_sd_handoff_before=np_sd_chunk_scan_sequence;
    np_sd_scratch_service();
    if(np_sd_defer_control && !np_sd_log.fault &&
       np_sd_chunk_scan_sequence!=np_sd_handoff_before)
        np_sd_stream_service(HAL_GetTick());
#endif
    /* While active, make stage/SD ownership progress before variable-time
     * BLE/configuration parsing. Keep both control services at one call/loop. */
    NP_BlePollTxService();
    if(np_sd_defer_control) {
        /* UART/DMA reception remains interrupt-driven. Control replies and
         * configuration parsing need not run at the unpaced SD-loop rate;
         * poll at a 1 ms cadence when foreground service is running, freeing
         * cycles for staging and card ownership. */
        static uint32_t np_sd_control_last_ms;
        const uint32_t control_now=HAL_GetTick();
        if(control_now!=np_sd_control_last_ms) {
            np_sd_control_last_ms=control_now;
            np_sd_control_due=1U;
            NP_ConfigService();
            npl_service();
        }
    }
    if(!np_sd_defer_control || np_sd_control_due) {
        /* The SWD/status mirror changes on state/chunk boundaries, not at
         * the main-loop rate. Publish at 1 kHz while capture is active. */
        np_sd_mailbox.state=np_sd_log.state; np_sd_mailbox.fault=np_sd_log.fault;
        np_sd_mailbox.committed_chunks=np_sd_log.committed_chunks;
        np_sd_mailbox.max_stage_ms=np_sd_log.max_stage_ms;
        np_sd_mailbox.max_write_ms=np_sd_log.max_write_ms;
        np_sd_mailbox.total_write_ms=np_sd_log.total_write_ms;
    }
#if !NP_SD_ALLOW_WRITES && NP_SD_CE64 && NP_SD_VSC
    np_sd_verify_service();
#endif
#if NP_SD_SENSORS
    /* Capture admission, completion, and SD ownership always serviced first.
     * IRQs only move I2C bytes; compensation and publication run here. */
    uint8_t sensor_capture_active=np_sd_log.state==NP_SD_DRAINING ||
        (np_sd_log.state==NP_SD_RUNNING &&
#if XE_NP_APP_QUAD_USB_WORD_EXPORT
         (xe_np_quad_dma_active || np_sd_camera.enabled || np_mic.running)
#else
         1U /* Preserve non-quad legacy ownership conservatively. */
#endif
        );
    XE_NP_SensorsPort_Service(sensor_capture_active);
#endif
#if NP_SD_CE64
    np_battery_service(HAL_GetTick());
#endif
    if(!np_sd_defer_control)np_sd_idle_sleep();
}

/* NP media reliability overlay complete */
