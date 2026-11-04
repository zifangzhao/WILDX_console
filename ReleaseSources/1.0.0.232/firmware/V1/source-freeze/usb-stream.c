#include "D:/Code/WILDX_console/WILDX_console/_artifacts/v1-cl-published/candidate/usb-headers/np_cl_source_port.h"
#include "C:/Frank/Code/XE_NP/_artifacts/v1_deterministic_20261029/usb_rtc_settled_capacity_incremental_r1/xe_np_usb_stream/2f3a2f7566fd_np_v1_rtc_port.h"
#if NP_FIRST_SYNC_ENABLE && NP_FINE_COUNTER64_ENABLE
volatile uint32_t np_usb_lifecycle_diag[44];
static void NP_UsbDiagStart(void);
static void NP_UsbDiagSample(uint32_t active);
static void NP_UsbFineStart(void);
static void NP_UsbFineService(void);
#endif
#include "C:/Frank/Code/XE_NP/_artifacts/v1_usb_fast_startup_r79r2_20261006/source/Core/Inc/np_config_port.h"
#include "C:/Frank/Code/XE_NP/_artifacts/v1_deterministic_20261029/usb_grid_r7_source/xe_np_usb_stream/Inc/xe_np_sensors_port.h"
#define NP_USB_TIMING_R2 1
static void NP_UsbTimingSlot(uint8_t *,uint32_t);
static void NP_UsbTimingDisarm(uint32_t);
#include "C:/Frank/Code/USB_PCconsole/_artifacts/closed_loop_fix_20261102/V1/usb_policy_check/misc.h"
#define NP_DIRECT_GROUPS 28U
#include "C:/Frank/Code/XE_NP/_artifacts/v1_usb_integrity_20261016/paired_restore/source/Core/Inc/np_direct_packets.h"
#include "C:/Frank/Code/XE_NP/_artifacts/v1_usb_fast_startup_r79r2_20261006/source/Core/Inc/np_modes.h"
#define NP_USB20_PAIRED() (np_mode_hz==20000U && np_mode_mask==3U)
extern unsigned NP_Triple_Probe(void);
#define NP_SD_USB_READ_BLOCKS 1020U
#define NP_SD_USB_HW_CRC 1
#define NP_SD_USB_FUSED_CRC 1
#define NP_SD_USB_PREFETCH 1
#define NP_SD_USB_CONTINUOUS 1
#include "C:/Frank/Code/XE_NP/_artifacts/v1_usb_fast_startup_r79r2_20261006/source/Core/Inc/mode_policy.h"
#include "C:/Frank/Code/XE_NP/_artifacts/v1_rtc_20261029/usb_source/frozen/Firmware/V1/Core/Inc/np_usb_auto_sd.h"
static NpUsbAutoSd np_usb_auto_sd;
#include "C:/Frank/Code/XE_NP/_artifacts/v1_usb_fast_startup_r79r2_20261006/source/Core/Inc/xe_np_boot_contract.h"
#define NP_V1_IMAGE_ROLE 2U
#include "C:/Frank/Code/XE_NP/_artifacts/v1_usb_fast_startup_r79r2_20261006/source/Core/Inc/np_sd_usb_identity.h"
#include "C:/Frank/Code/XE_NP/_artifacts/v1_usb_fast_startup_r79r2_20261006/source/Core/Inc/np_sd_dma_chain.h"
#include "C:/Frank/Code/XE_NP/_artifacts/v1_usb_fast_startup_r79r2_20261006/source/Core/Inc/xe_np_usb_stream.h"

#include "C:/Frank/Code/XE_NP/_artifacts/v1_usb_fast_startup_r79r2_20261006/source/Core/Inc/dataMGR.h"
#include "C:/Frank/Code/XE_NP/_artifacts/v1_usb_fast_startup_r79r2_20261006/source/Core/Inc/main.h"
#include "C:/Frank/Code/USB_PCconsole/_artifacts/closed_loop_fix_20261102/V1/source_r2/usb/xe_np_app.h"
#include "C:/Frank/Code/XE_NP/_artifacts/v1_usb_fast_startup_r79r2_20261006/source/Core/Inc/xe_np_capture.h"
#include "C:/Frank/Code/XE_NP/_artifacts/v1_usb_fast_startup_r79r2_20261006/source/Core/Inc/xe_np_psram.h"

#include <string.h>
#include <stddef.h>

extern DCACHE_HandleTypeDef hdcache1;
#define NP_USB_BLOCK_PROBE_OWNER 1
#include "C:/Frank/Code/USB_PCconsole/_artifacts/closed_loop_fix_20261102/V1/usb_loss_generic_profile/np_usb_block_probe.h"
#if NP_USB_BLOCK_PROBE
static void NP_UsbBlockInvalidateCall(const void *addr,uint32_t size)
{
 NpUsbBlockStamp block=NP_UsbBlockBegin(NP_UB_INVALIDATE,(uint32_t)(uintptr_t)addr,size,
     hdcache1.Instance?hdcache1.Instance->SR:0U);
 XE_NP_Capture_DCacheInvalidate(addr,size);
 NP_UsbBlockEnd(block,NP_UB_UNKNOWN_RESULT|hdcache1.ErrorCode,hdcache1.Instance?hdcache1.Instance->SR:0U);
}
#define XE_NP_Capture_DCacheInvalidate NP_UsbBlockInvalidateCall
#endif

#define XE_NP_USB_STREAM_VENDOR_CODE 0x20U
#define XE_NP_USB_STREAM_REQ_START 0x40U
#define XE_NP_USB_STREAM_REQ_STOP 0x41U
#define XE_NP_USB_STREAM_REQ_GET_STATUS 0x42U
#define XE_NP_USB_STREAM_REQ_START_SYNTHETIC 0x43U
#define XE_NP_USB_STREAM_REQ_SET_MCO_DIVIDER 0x44U
#define XE_NP_USB_STREAM_REQ_START_STREAMS 0x47U
#define XE_NP_USB_STREAM_CONSUMER_IDX 2U
#define XE_NP_USB_STREAM_EP0_MPS 64U
#define XE_NP_USB_STREAM_BULK_MPS 512U
#define XE_NP_USB_STREAM_TX_BUFFER_COUNT 10U
#define XE_NP_USB_STREAM_MAX_CAPTURE_PAYLOAD_BYTES XE_NP_USB_STREAM_TRANSFER_PAYLOAD_BYTES
#define XE_NP_USB_STREAM_SYNTHETIC_PAYLOAD_BYTES XE_NP_USB_STREAM_PAYLOAD_BYTES
#define XE_NP_USB_STREAM_BACKLOG_TARGET_BYTES (512UL * 1024UL)
#define XE_NP_USB_STREAM_MIN_PAYLOAD_BYTES 512U
#define XE_NP_USB_STREAM_PARTIAL_FLUSH_MS 4U
#define XE_NP_USB_STREAM_MAGIC_DIAG 0x55534253UL
#define XE_NP_USB_STREAM_DIAG_VERSION 5U

#define XE_NP_USB_FLAG_STREAMING 0x00000001UL
#define XE_NP_USB_FLAG_DROPPED 0x00000002UL
#define XE_NP_USB_FLAG_MGR_OVERRUN 0x00000004UL
#define XE_NP_USB_FLAG_SOURCE_ACTIVE 0x00000008UL
#define XE_NP_USB_FLAG_PACKED_NP2_LANE8 0x00000010UL
#define XE_NP_USB_FLAG_SYNTHETIC 0x00000020UL
#define XE_NP_USB_FLAG_START_PENDING 0x00000040UL
#define XE_NP_USB_FLAG_PAYLOAD_HASH_VALID 0x00000080UL
#define XE_NP_USB_FLAG_EDGE_HASH_VALID 0x00000100UL

#define XE_NP_USB_START_STATUS_OK 0U
#define XE_NP_USB_START_STATUS_PORT_INVALID 1U
#define XE_NP_USB_START_STATUS_APP_REJECTED 2U
#define XE_NP_USB_START_STATUS_APP_NOT_READY 3U
#define XE_NP_USB_START_STATUS_EXPORT_ENABLE_FAIL 4U

#define XE_NP_USB_DISARM_REASON_NONE 0U
#define XE_NP_USB_DISARM_REASON_START_BEGIN 1U
#define XE_NP_USB_DISARM_REASON_HOST_STOP 2U
#define XE_NP_USB_DISARM_REASON_DISCONNECT 3U
#define XE_NP_USB_DISARM_REASON_SUSPEND 4U
#define XE_NP_USB_DISARM_REASON_USB_RESET 5U
#define XE_NP_USB_DISARM_REASON_SET_CONFIG_ZERO 6U
#define XE_NP_USB_DISARM_REASON_START_SYNTHETIC_BEGIN 7U
#define XE_NP_USB_DISARM_REASON_CAPTURE_EXPORT_FAULT 8U
/*
 * Keep the default USB stream path minimal: raw portal semantics with header
 * metadata only. Payload integrity experiments stay available, but they are
 * bench-only and must be enabled explicitly.
 */
#ifndef XE_NP_USB_STREAM_PACKET_HASH_ENABLE
#define XE_NP_USB_STREAM_PACKET_HASH_ENABLE 0U
#endif
#ifndef XE_NP_USB_STREAM_EDGE_HASH_ENABLE
#define XE_NP_USB_STREAM_EDGE_HASH_ENABLE 0U
#endif
#ifndef XE_NP_USB_STREAM_LANE_VALIDATOR_ENABLE
#define XE_NP_USB_STREAM_LANE_VALIDATOR_ENABLE 0U
#endif
#ifndef XE_NP_USB_STREAM_SEAM_PROBE_ENABLE
#define XE_NP_USB_STREAM_SEAM_PROBE_ENABLE 0U
#endif
#ifndef XE_NP_USB_STREAM_REAL_CAPTURE_SYNTHETIC_PAYLOAD_ENABLE
#define XE_NP_USB_STREAM_REAL_CAPTURE_SYNTHETIC_PAYLOAD_ENABLE 0U
#endif
#ifndef XE_NP_USB_STREAM_SYNTHETIC_ENABLE_NP_VCC_TEST
#define XE_NP_USB_STREAM_SYNTHETIC_ENABLE_NP_VCC_TEST 0U
#endif
#ifndef XE_NP_USB_STREAM_SYNTHETIC_RELEASE_NP_RESETS_TEST
#define XE_NP_USB_STREAM_SYNTHETIC_RELEASE_NP_RESETS_TEST 0U
#endif
#ifndef XE_NP_USB_STREAM_REAL_CAPTURE_SYNTHETIC_DETACH_PORT_ENABLE
#define XE_NP_USB_STREAM_REAL_CAPTURE_SYNTHETIC_DETACH_PORT_ENABLE 0U
#endif
#ifndef XE_NP_USB_STREAM_REAL_CAPTURE_RESTART_SYNTHETIC_ENABLE
#define XE_NP_USB_STREAM_REAL_CAPTURE_RESTART_SYNTHETIC_ENABLE 0U
#endif
#ifndef XE_NP_USB_STREAM_REAL_CAPTURE_SKIP_RELEASE_ENABLE
#define XE_NP_USB_STREAM_REAL_CAPTURE_SKIP_RELEASE_ENABLE 0U
#endif
#ifndef XE_NP_USB_STREAM_REAL_EXPORT_SYNTHETIC_PACKET_ENABLE
#define XE_NP_USB_STREAM_REAL_EXPORT_SYNTHETIC_PACKET_ENABLE 0U
#endif
#ifndef XE_NP_USB_STREAM_INTERLEAVED_STAGING_DMA_ENABLE
#define XE_NP_USB_STREAM_INTERLEAVED_STAGING_DMA_ENABLE 0U
#endif
#ifndef XE_NP_USB_STREAM_QUAD_STAGING_DMA_ENABLE
/* Four-lane 30 kHz path: GPDMA, not the CPU, reads the guarded SRAM3 lanes. */
#define XE_NP_USB_STREAM_QUAD_STAGING_DMA_ENABLE 0U
#endif
#ifndef XE_NP_USB_STREAM_QUAD_STAGING_LPDMA_ENABLE
/* LPDMA is a separate DMA controller/master.  This diagnostic moves bulk
 * SRAM3 reads out of GPDMA completely while GPIO capture stays on GPDMA. */
#define XE_NP_USB_STREAM_QUAD_STAGING_LPDMA_ENABLE 0U
#endif
#ifndef XE_NP_USB_STREAM_CAPTURE_SRAM1_ENABLE
/* The one-probe 30 kS/s bank-isolation experiment places the producer in
 * SRAM1.  Its GPDMA staging reads must therefore use port 0 instead of the
 * SRAM3 port used by the normal capture layout. */
#define XE_NP_USB_STREAM_CAPTURE_SRAM1_ENABLE 0U
#endif
#ifndef XE_NP_USB_STREAM_QUAD_ZEROCOPY_ENABLE
/* OTG-HS reads completed physical lane bytes directly. This is a transport
 * characterization mode: packets deliberately have no software header. */
#define XE_NP_USB_STREAM_QUAD_ZEROCOPY_ENABLE 0U
#endif
#if ((XE_NP_USB_STREAM_QUAD_ZEROCOPY_ENABLE != 0U) && \
     (XE_NP_USB_STREAM_QUAD_STAGING_DMA_ENABLE != 0U))
#error "Quad zero-copy and quad staging are mutually exclusive"
#endif
#if ((XE_NP_USB_STREAM_QUAD_STAGING_LPDMA_ENABLE != 0U) && \
     (XE_NP_USB_STREAM_QUAD_STAGING_DMA_ENABLE == 0U))
#error "LPDMA staging requires the quad staging producer"
#endif
#if ((XE_NP_USB_STREAM_INTERLEAVED_STAGING_DMA_ENABLE != 0U) && \
     (XE_NP_USB_STREAM_QUAD_STAGING_DMA_ENABLE != 0U))
#error "Select either legacy paired staging or four-lane staging, not both"
#endif
#ifndef XE_NP_USB_STREAM_INTERLEAVED_STAGING_DMA_PRIORITY
/* The capture lanes are edge-clocked high-priority requesters.  Staging is
 * deliberately the weakest possible GPDMA client: it may fall behind, but it
 * must never earn an arbitration turn ahead of an arriving GPIO sample. */
#define XE_NP_USB_STREAM_INTERLEAVED_STAGING_DMA_PRIORITY DMA_LOW_PRIORITY_LOW_WEIGHT
#endif
#ifndef XE_NP_USB_STREAM_INTERLEAVED_STAGING_PACKETS_PER_BATCH
/* One A-lane operation plus one B-lane operation fills a complete 63-packet
 * bulk transfer (32/31 packets).  Eight-packet batches were correct but
 * required many foreground passes before USB could be armed. */
#define XE_NP_USB_STREAM_INTERLEAVED_STAGING_PACKETS_PER_BATCH 63U
#endif
#ifndef XE_NP_APP_QUAD_USB_WORD_EXPORT
#define XE_NP_APP_QUAD_USB_WORD_EXPORT 0U
#endif
#ifndef XE_NP_APP_QUAD_USB_PACED
#define XE_NP_APP_QUAD_USB_PACED 0U
#endif
#if (XE_NP_APP_QUAD_USB_WORD_EXPORT != 0U)
#define XE_NP_USB_STREAM_QUAD_STAGING_GROUPS_PER_BATCH 28U
#define XE_NP_USB_WORD_LANE_BYTES (28U * XE_NP_USB_STREAM_PAYLOAD_BYTES)
static uint8_t xe_np_usb_quad_word_scratch[(4U * XE_NP_USB_WORD_LANE_BYTES + 31U) & ~31U]
    __attribute__((aligned(32)));
static volatile uint8_t xe_np_usb_quad_word_ready;
static uint32_t xe_np_usb_quad_word_started_ms;
static uint8_t xe_np_usb_v1_restart_pending;
static uint32_t xe_np_usb_v1_session_id, xe_np_usb_v1_previous_fault;
static uint32_t xe_np_usb_v1_reset_count, xe_np_usb_v1_latched_fault;
#else
#define XE_NP_USB_STREAM_QUAD_STAGING_GROUPS_PER_BATCH 15U
#endif
#define XE_NP_USB_STREAM_QUAD_STAGING_PACKETS_PER_BATCH \
    (XE_NP_APP_USB_QUAD_DMA_LANE_COUNT * XE_NP_USB_STREAM_QUAD_STAGING_GROUPS_PER_BATCH)
#define XE_NP_USB_STREAM_QUAD_STAGING_MAX_NODES \
    (XE_NP_USB_STREAM_QUAD_STAGING_PACKETS_PER_BATCH + XE_NP_APP_USB_QUAD_DMA_LANE_COUNT)
#if (XE_NP_APP_QUAD_USB_WORD_EXPORT != 0U)
_Static_assert(XE_NP_USB_STREAM_QUAD_STAGING_PACKETS_PER_BATCH <= XE_NP_USB_STREAM_PACKETS_PER_TRANSFER,
               "USB packet buffer is smaller than the word-stage batch");
#endif
#define XE_NP_USB_STREAM_FNV1A32_INIT 2166136261UL
#define XE_NP_USB_STREAM_FNV1A32_PRIME 16777619UL
#define XE_NP_USB_STREAM_EDGE_HASH_BYTES 64U
#define XE_NP_USB_STREAM_SEAM_PROBE_BYTES 64U
#define XE_NP_USB_STREAM_SEAM_PROBE_PAYLOAD_OFFSET 56544U
#define XE_NP_USB_STREAM_PSRAM_FULL_DCACHE_INVALIDATE 1U

typedef struct
{
    uint8_t bmRequest;
    uint8_t bRequest;
    uint16_t wValue;
    uint16_t wIndex;
    uint16_t wLength;
} XE_NP_UsbSetup;

typedef struct
{
    PCD_HandleTypeDef *hpcd;
    XE_NP_CapturePort *port;
    uint64_t payload_bytes_sent;
    uint32_t dropped_bytes;
    uint32_t sequence;
    uint32_t packets_sent;
    uint32_t last_tx_tick_ms;
    const uint8_t *ep0_tx_data;
    uint16_t ep0_tx_len;
    uint16_t ep0_tx_sent;
    uint8_t configured;
    uint8_t streaming;
    uint8_t port_index;
    uint8_t ep0_status_out_pending;
    uint8_t ep0_address_pending;
    uint8_t ep0_pending_address;
    uint8_t ep0_zlp_pending;
    uint8_t synthetic_source;
    volatile uint8_t real_start_pending; /* 0 none, 2 WAIT_STATUS_ACK, 1 released */
    uint8_t real_start_port_index;
    volatile uint8_t tx_pending;
    volatile uint8_t tx_active_idx;
    volatile uint8_t tx_pending_idx;
    volatile uint8_t tx_busy;
    volatile uint8_t tx_stage;
    uint8_t packet_assembly_idx;
    uint16_t packet_assembly_bytes;
    const uint8_t *tx_payload_ptr[XE_NP_USB_STREAM_TX_BUFFER_COUNT];
    uint32_t tx_len[XE_NP_USB_STREAM_TX_BUFFER_COUNT];
    uint32_t tx_payload_bytes[XE_NP_USB_STREAM_TX_BUFFER_COUNT];
    uint8_t tx_split_payload[XE_NP_USB_STREAM_TX_BUFFER_COUNT];
#if (XE_NP_USB_STREAM_QUAD_ZEROCOPY_ENABLE != 0U)
    XE_NP_AppUsbQuadDmaBatch quad_zero_copy_batch;
    uint8_t quad_zero_copy_active;
    uint8_t quad_zero_copy_lane;
#endif
} XE_NP_UsbStreamState;

typedef struct
{
    XE_NP_PsbValidator validator;
    uint64_t payload_bytes_processed;
    uint32_t first_error_flags;
    uint32_t first_error_packet_sequence;
    uint32_t first_error_payload_offset;
    uint32_t first_error_stream_offset_low;
    uint32_t lock_offset;
    uint32_t lock_clean_frames;
    uint32_t packets_checked;
    uint8_t tail[XE_NP_USB_STREAM_LANE_VALIDATOR_TAIL_BYTES];
    uint8_t tail_len;
    uint8_t locked;
    uint8_t disabled;
    uint8_t reserved0;
} XE_NP_UsbLaneValidatorState;

typedef struct
{
    DMA_HandleTypeDef hdma;
    XE_NP_AppUsbInterleavedDmaBatch batch;
    XE_NP_AppUsbQuadDmaBatch quad_batch;
    DMA_QListTypeDef quad_queue;
    DMA_NodeTypeDef quad_nodes[XE_NP_USB_STREAM_QUAD_STAGING_MAX_NODES];
    uint8_t initialized;
    volatile uint8_t busy;
    volatile uint8_t error;
    uint8_t quad_mode;
    uint8_t phase_lane_b;
    uint8_t buffer_index;
    uint8_t packet_start_index;
    uint8_t packet_count;
    uint8_t quad_sequence_valid;
    uint16_t quad_sequence_base;
    uint16_t quad_next_sequence;
} XE_NP_UsbInterleavedStagingDma;

typedef struct
{
    uint32_t packet_sequence;
    uint32_t compare_after_packets_sent;
    uint32_t ring_offset;
    uint32_t payload_offset;
    uint32_t tail_offset;
    uint32_t payload_bytes;
    uint32_t before_hash;
    uint32_t after_hash;
    uint32_t mismatch_count;
    uint8_t snapshot[XE_NP_USB_STREAM_SEAM_PROBE_BYTES];
    uint8_t active;
    uint8_t compared;
    uint8_t valid;
    uint8_t reserved0;
} XE_NP_UsbSeamProbeState;

static XE_NP_UsbStreamState xe_np_usb_state;
static XE_NP_UsbLaneValidatorState xe_np_usb_lane_validator;
static XE_NP_UsbSeamProbeState xe_np_usb_seam_probe;
/* GPDMA CLLR stores only the link offset within one 64 KiB bank.
 * Keep all embedded nodes inside an aligned 8 KiB allocation, even when
 * unrelated globals change the surrounding linker placement. */
_Static_assert(sizeof(XE_NP_UsbInterleavedStagingDma) <= 8192U,
               "USB staging state exceeds its DMA link-bank alignment");
static XE_NP_UsbInterleavedStagingDma xe_np_usb_interleaved_staging_dma
    __attribute__((aligned(8192)));
static uint32_t xe_np_usb_start_request_count;
static uint32_t xe_np_usb_start_try_count;
static uint32_t xe_np_usb_start_try_fail_count;
static uint32_t xe_np_usb_last_start_try_status;
static volatile uint32_t xe_np_usb_disarm_count;
static uint32_t xe_np_usb_last_disarm_reason;
volatile XE_NP_UsbStreamDiag xe_np_usb_stream_diag;
/* OTG HS internal DMA owns these buffers during endpoint transfers. Keep them
 * out of the normal SRAM1 allocation, and also separate from the GPIO-DMA
 * capture ring. The dedicated region mirrors USB512's proven dma_usb layout. */
static CE32_ALIGN(XE_NP_CAPTURE_DCACHE_LINE_BYTES) uint8_t
    xe_np_usb_tx_packet[XE_NP_USB_STREAM_TX_BUFFER_COUNT][XE_NP_USB_STREAM_TRANSFER_BYTES]
    __attribute__((section("NP_CAPTURE_SRAM1")));
static CE32_ALIGN(XE_NP_CAPTURE_DCACHE_LINE_BYTES) uint8_t
    xe_np_usb_out[XE_NP_USB_STREAM_BULK_MPS] __attribute__((section("NP_USB_DMA")));
static CE32_ALIGN(XE_NP_CAPTURE_DCACHE_LINE_BYTES) uint8_t
    xe_np_usb_ep0_tx[XE_NP_USB_STREAM_EP0_MPS] __attribute__((section("NP_USB_DMA")));
static uint8_t xe_np_usb_ep0_status_out[4] __attribute__((section("NP_USB_DMA")));
static uint8_t xe_np_usb_string_buf[128];

static void XE_NP_UsbStream_SetDiagStatus(uint32_t status);
static void NP_UsbControlReject(PCD_HandleTypeDef *hpcd);
static volatile uint32_t np_usb_queue_produced, np_usb_queue_consumed;
static uint8_t np_usb_queue_write, np_usb_queue_read;
volatile uint32_t np_usb_queue_diag[6] = {0x51554531U,0,0,0,0,0};
static void NP_UsbQueueKick(void);
static void NP_UsbCameraStop(void);
static HAL_StatusTypeDef XE_NP_UsbStream_StartPreparedPacket(uint8_t buffer_index);
static HAL_StatusTypeDef XE_NP_UsbStream_StartPreparedPayload(uint8_t buffer_index);
static void XE_NP_UsbStream_StartPendingPacket(void);
static void XE_NP_UsbStream_AbortBulkInTransfer(void);
static volatile uint32_t np_usb_runtime_ready;
static volatile uint32_t np_usb_stage_retire_failed;
int NP_USB_IsReady(void){return np_usb_runtime_ready!=0U && !np_usb_stage_retire_failed;}
void NP_USB_SetReady(void){__DMB();np_usb_runtime_ready=1U;
#if NP_HARDWARE_RTC_ENABLE
NP_V1RtcPortReady();
#endif
}
static int NP_UsbGridRequest(PCD_HandleTypeDef *,const XE_NP_UsbSetup *);
static void NP_UsbGridBusReset(void);
static void NP_UsbGridStop(void);
static void NP_UsbGridAdmit(void);
static int XE_NP_UsbStream_Disarm(uint32_t reason);
static void NP_UsbStartAckCancel(void);
static void NP_UsbStartAckWait(void);
static void XE_NP_UsbStream_ReleaseCompletedCaptureWindow(void);
static void XE_NP_UsbStream_DCacheCleanTx(const void *addr, uint32_t size_bytes);
static uint8_t XE_NP_UsbStream_SourceInPsram(const void *addr, uint32_t size_bytes);
static void XE_NP_UsbStream_ServiceSynthetic(void);
static int XE_NP_UsbStream_StartSynthetic(void);
static int XE_NP_UsbStream_TryStartReal(uint8_t port_index);
#if (XE_NP_USB_STREAM_QUAD_ZEROCOPY_ENABLE != 0U)
static void XE_NP_UsbStream_ServiceQuadZeroCopy(void);
#endif
static uint32_t XE_NP_UsbStream_Fnv1a32Update(uint32_t hash, uint8_t value);
static uint32_t XE_NP_UsbStream_Fnv1a32Buffer(const uint8_t *src, uint32_t size_bytes);
static uint32_t XE_NP_UsbStream_EdgeHash(const uint8_t *src, uint32_t size_bytes);
static uint8_t XE_NP_UsbStream_ShouldPackNp2Lane8(void);
static uint8_t XE_NP_UsbStream_IsNp2Lane8Payload(void);
static void XE_NP_UsbStream_ResetLaneValidator(void);
static void XE_NP_UsbStream_ResetSeamProbe(void);
static void XE_NP_UsbStream_TryCompareSeamProbe(void);
static void XE_NP_UsbStream_MaybeArmSeamProbe(
    const XE_NP_AppUsbCaptureWindow *source_window,
    const uint8_t *payload_ptr,
    uint32_t payload_bytes);
static void XE_NP_UsbStream_ProcessLanePayload(
    uint32_t packet_sequence,
    const uint8_t *payload,
    uint32_t payload_bytes);
static void XE_NP_UsbStream_WritePacketHeader(
    uint8_t *dst,
    uint32_t payload_bytes,
    uint16_t packet_sequence,
    const XE_NP_AppUsbCaptureWindow *source_window,
    uint32_t available_bytes,
    uint32_t payload_hash,
    uint32_t edge_prefix_hash,
    uint32_t edge_suffix_hash);
static int XE_NP_UsbStream_InitInterleavedStagingDma(void);
static int XE_NP_UsbStream_StopInterleavedStagingDma(void);
static int XE_NP_UsbStream_StartInterleavedStagingBatch(
    uint8_t buffer_index,
    uint8_t packet_start_index,
    const XE_NP_AppUsbInterleavedDmaBatch *batch);
static HAL_StatusTypeDef XE_NP_UsbStream_StartInterleavedStagingLane(uint8_t lane_b);
static void XE_NP_UsbStream_InterleavedStagingComplete(DMA_HandleTypeDef *hdma);
static void XE_NP_UsbStream_InterleavedStagingError(DMA_HandleTypeDef *hdma);
static int XE_NP_UsbStream_StartQuadStagingBatchOwned(uint8_t buffer_index,uint32_t generation);
static int XE_NP_UsbStream_StartQuadStagingBatch(uint8_t buffer_index);
static HAL_StatusTypeDef XE_NP_UsbStream_BuildQuadStagingNode(
    DMA_NodeTypeDef *node,
    const uint8_t *src,
    uint8_t *dst,
    uint32_t bytes);

static const uint8_t xe_np_usb_device_descriptor[] = {
    0x12, 0x01, 0x00, 0x02, 0x00, 0x00, 0x00, XE_NP_USB_STREAM_EP0_MPS,
    (uint8_t)(XE_NP_USB_STREAM_VENDOR_ID & 0xFFU),
    (uint8_t)(XE_NP_USB_STREAM_VENDOR_ID >> 8U),
    (uint8_t)(XE_NP_USB_STREAM_PRODUCT_ID & 0xFFU),
    (uint8_t)(XE_NP_USB_STREAM_PRODUCT_ID >> 8U),
    0x03, 0x01, 0x01, 0x02, 0x03, 0x01,
};

static const uint8_t xe_np_usb_device_qualifier_descriptor[] = {
    0x0A, 0x06, 0x10, 0x02, 0x00, 0x00, 0x00, XE_NP_USB_STREAM_EP0_MPS, 0x01, 0x00,
};

static const uint8_t xe_np_usb_config_descriptor[] = {
    0x09, 0x02, 0x20, 0x00, 0x01, 0x01, 0x00, 0x80, 0xFA,
    0x09, 0x04, 0x00, 0x00, 0x02, 0xFF, 0x00, 0x00, 0x04,
    0x07, 0x05, XE_NP_USB_STREAM_IN_EP, 0x02,
    (uint8_t)(XE_NP_USB_STREAM_BULK_MPS & 0xFFU),
    (uint8_t)(XE_NP_USB_STREAM_BULK_MPS >> 8U), 0x00,
    0x07, 0x05, XE_NP_USB_STREAM_OUT_EP, 0x02,
    (uint8_t)(XE_NP_USB_STREAM_BULK_MPS & 0xFFU),
    (uint8_t)(XE_NP_USB_STREAM_BULK_MPS >> 8U), 0x00,
};

static const uint8_t xe_np_usb_other_speed_config_descriptor[] = {
    0x09, 0x07, 0x20, 0x00, 0x01, 0x01, 0x00, 0x80, 0xFA,
    0x09, 0x04, 0x00, 0x00, 0x02, 0xFF, 0x00, 0x00, 0x04,
    0x07, 0x05, XE_NP_USB_STREAM_IN_EP, 0x02, 0x40, 0x00, 0x00,
    0x07, 0x05, XE_NP_USB_STREAM_OUT_EP, 0x02, 0x40, 0x00, 0x00,
};

static const uint8_t xe_np_usb_bos_descriptor[] = {
    0x05, 0x0F, 0x21, 0x00, 0x01,
    0x1C, 0x10, 0x05, 0x00,
    0xDF, 0x60, 0xDD, 0xD8, 0x89, 0x45, 0xC7, 0x4C,
    0x9C, 0xD2, 0x65, 0x9D, 0x9E, 0x64, 0x8A, 0x9F,
    0x00, 0x00, 0x03, 0x06, 0xB2, 0x00,
    XE_NP_USB_STREAM_VENDOR_CODE, 0x00,
};

static const uint8_t xe_np_usb_ms_os_20_descriptor[] = {
    0x0A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03, 0x06, 0xB2, 0x00,
    0x08, 0x00, 0x01, 0x00, 0x00, 0x00, 0xA8, 0x00,
    0x08, 0x00, 0x02, 0x00, 0x00, 0x00, 0xA0, 0x00,
    0x14, 0x00, 0x03, 0x00,
    0x57, 0x49, 0x4E, 0x55, 0x53, 0x42, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x84, 0x00, 0x04, 0x00, 0x07, 0x00, 0x2A, 0x00,
    0x44, 0x00, 0x65, 0x00, 0x76, 0x00, 0x69, 0x00,
    0x63, 0x00, 0x65, 0x00, 0x49, 0x00, 0x6E, 0x00,
    0x74, 0x00, 0x65, 0x00, 0x72, 0x00, 0x66, 0x00,
    0x61, 0x00, 0x63, 0x00, 0x65, 0x00, 0x47, 0x00,
    0x55, 0x00, 0x49, 0x00, 0x44, 0x00, 0x73, 0x00,
    0x00, 0x00, 0x50, 0x00,
    0x7B, 0x00, 0x38, 0x00, 0x46, 0x00, 0x35, 0x00,
    0x41, 0x00, 0x34, 0x00, 0x41, 0x00, 0x37, 0x00,
    0x42, 0x00, 0x2D, 0x00, 0x30, 0x00, 0x41, 0x00,
    0x41, 0x00, 0x36, 0x00, 0x2D, 0x00, 0x34, 0x00,
    0x46, 0x00, 0x38, 0x00, 0x45, 0x00, 0x2D, 0x00,
    0x41, 0x00, 0x31, 0x00, 0x34, 0x00, 0x46, 0x00,
    0x2D, 0x00, 0x41, 0x00, 0x32, 0x00, 0x46, 0x00,
    0x37, 0x00, 0x42, 0x00, 0x33, 0x00, 0x43, 0x00,
    0x36, 0x00, 0x41, 0x00, 0x41, 0x00, 0x34, 0x00,
    0x45, 0x00, 0x7D, 0x00, 0x00, 0x00, 0x00, 0x00,
};

typedef char xe_np_usb_ms_os_20_descriptor_size_check[
    (sizeof(xe_np_usb_ms_os_20_descriptor) == 178U) ? 1 : -1];

static const uint8_t xe_np_usb_ms_os_10_compat_id_descriptor[] = {
    0x28, 0x00, 0x00, 0x00,
    0x00, 0x01,
    0x04, 0x00,
    0x01,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x01,
    0x57, 0x49, 0x4E, 0x55, 0x53, 0x42, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
};

typedef char xe_np_usb_ms_os_10_compat_id_descriptor_size_check[
    (sizeof(xe_np_usb_ms_os_10_compat_id_descriptor) == 40U) ? 1 : -1];

static const uint8_t xe_np_usb_ms_os_10_ext_prop_descriptor[] = {
    0x92, 0x00, 0x00, 0x00,
    0x00, 0x01,
    0x05, 0x00,
    0x01, 0x00,
    0x88, 0x00, 0x00, 0x00,
    0x07, 0x00, 0x00, 0x00,
    0x2A, 0x00,
    0x44, 0x00, 0x65, 0x00, 0x76, 0x00, 0x69, 0x00,
    0x63, 0x00, 0x65, 0x00, 0x49, 0x00, 0x6E, 0x00,
    0x74, 0x00, 0x65, 0x00, 0x72, 0x00, 0x66, 0x00,
    0x61, 0x00, 0x63, 0x00, 0x65, 0x00, 0x47, 0x00,
    0x55, 0x00, 0x49, 0x00, 0x44, 0x00, 0x73, 0x00,
    0x00, 0x00,
    0x50, 0x00, 0x00, 0x00,
    0x7B, 0x00, 0x38, 0x00, 0x46, 0x00, 0x35, 0x00,
    0x41, 0x00, 0x34, 0x00, 0x41, 0x00, 0x37, 0x00,
    0x42, 0x00, 0x2D, 0x00, 0x30, 0x00, 0x41, 0x00,
    0x41, 0x00, 0x36, 0x00, 0x2D, 0x00, 0x34, 0x00,
    0x46, 0x00, 0x38, 0x00, 0x45, 0x00, 0x2D, 0x00,
    0x41, 0x00, 0x31, 0x00, 0x34, 0x00, 0x46, 0x00,
    0x2D, 0x00, 0x41, 0x00, 0x32, 0x00, 0x46, 0x00,
    0x37, 0x00, 0x42, 0x00, 0x33, 0x00, 0x43, 0x00,
    0x36, 0x00, 0x41, 0x00, 0x41, 0x00, 0x34, 0x00,
    0x45, 0x00, 0x7D, 0x00, 0x00, 0x00, 0x00, 0x00,
};

typedef char xe_np_usb_ms_os_10_ext_prop_descriptor_size_check[
    (sizeof(xe_np_usb_ms_os_10_ext_prop_descriptor) == 146U) ? 1 : -1];

static uint16_t XE_NP_UsbStream_MinU16(uint16_t a, uint16_t b)
{
    return (a < b) ? a : b;
}

static void XE_NP_UsbStream_Put16(uint8_t *dst, uint32_t offset, uint16_t value)
{
    dst[offset] = (uint8_t)(value & 0xFFU);
    dst[offset + 1U] = (uint8_t)(value >> 8U);
}

static void XE_NP_UsbStream_Put32(uint8_t *dst, uint32_t offset, uint32_t value)
{
    dst[offset] = (uint8_t)(value & 0xFFU);
    dst[offset + 1U] = (uint8_t)((value >> 8U) & 0xFFU);
    dst[offset + 2U] = (uint8_t)((value >> 16U) & 0xFFU);
    dst[offset + 3U] = (uint8_t)((value >> 24U) & 0xFFU);
}

static void XE_NP_UsbStream_Put64(uint8_t *dst, uint32_t offset, uint64_t value)
{
    XE_NP_UsbStream_Put32(dst, offset, (uint32_t)value);
    XE_NP_UsbStream_Put32(dst, offset + 4U, (uint32_t)(value >> 32U));
}

static uint16_t XE_NP_UsbStream_PackHighBytesFromTwoSnapshots(uint32_t snapshots)
{
    return (uint16_t)(((snapshots >> 8U) & 0x00FFU) | ((snapshots >> 16U) & 0xFF00U));
}

static uint32_t XE_NP_UsbStream_Fnv1a32Update(uint32_t hash, uint8_t value)
{
    hash ^= (uint32_t)value;
    hash *= XE_NP_USB_STREAM_FNV1A32_PRIME;
    return hash;
}

static uint32_t XE_NP_UsbStream_Fnv1a32Buffer(const uint8_t *src, uint32_t size_bytes)
{
#if (XE_NP_USB_STREAM_PACKET_HASH_ENABLE != 0U)
    uint32_t hash = XE_NP_USB_STREAM_FNV1A32_INIT;

    while(size_bytes-- != 0U)
    {
        hash = XE_NP_UsbStream_Fnv1a32Update(hash, *src++);
    }

    return hash;
#else
    (void)src;
    (void)size_bytes;
    return 0U;
#endif
}

static uint32_t XE_NP_UsbStream_EdgeHash(const uint8_t *src, uint32_t size_bytes)
{
#if (XE_NP_USB_STREAM_EDGE_HASH_ENABLE != 0U)
    uint32_t hash = XE_NP_USB_STREAM_FNV1A32_INIT;

    if((src == NULL) || (size_bytes == 0U))
    {
        return 0U;
    }

    if(size_bytes > XE_NP_USB_STREAM_EDGE_HASH_BYTES)
    {
        size_bytes = XE_NP_USB_STREAM_EDGE_HASH_BYTES;
    }

    while(size_bytes-- != 0U)
    {
        hash = XE_NP_UsbStream_Fnv1a32Update(hash, *src++);
    }

    return hash;
#else
    (void)src;
    (void)size_bytes;
    return 0U;
#endif
}

static uint32_t XE_NP_UsbStream_PackNp2Lane8(uint8_t *dst, const uint8_t *src, uint32_t payload_bytes)
{
#if (XE_NP_USB_STREAM_PACKET_HASH_ENABLE != 0U)
    uint32_t hash = XE_NP_USB_STREAM_FNV1A32_INIT;
#endif

    while(payload_bytes >= 4U)
    {
        uint32_t in0;
        uint32_t in1;
        uint32_t out;

        memcpy(&in0, src, sizeof(in0));
        memcpy(&in1, &src[4], sizeof(in1));
        out = (uint32_t)XE_NP_UsbStream_PackHighBytesFromTwoSnapshots(in0) |
              ((uint32_t)XE_NP_UsbStream_PackHighBytesFromTwoSnapshots(in1) << 16U);
        memcpy(dst, &out, sizeof(out));
#if (XE_NP_USB_STREAM_PACKET_HASH_ENABLE != 0U)
        hash = XE_NP_UsbStream_Fnv1a32Update(hash, dst[0]);
        hash = XE_NP_UsbStream_Fnv1a32Update(hash, dst[1]);
        hash = XE_NP_UsbStream_Fnv1a32Update(hash, dst[2]);
        hash = XE_NP_UsbStream_Fnv1a32Update(hash, dst[3]);
#endif

        src += 8U;
        dst += 4U;
        payload_bytes -= 4U;
    }

    if(payload_bytes >= 2U)
    {
        uint32_t in0;
        uint16_t out;

        memcpy(&in0, src, sizeof(in0));
        out = XE_NP_UsbStream_PackHighBytesFromTwoSnapshots(in0);
        memcpy(dst, &out, sizeof(out));
#if (XE_NP_USB_STREAM_PACKET_HASH_ENABLE != 0U)
        hash = XE_NP_UsbStream_Fnv1a32Update(hash, dst[0]);
        hash = XE_NP_UsbStream_Fnv1a32Update(hash, dst[1]);
#endif

        src += 4U;
        dst += 2U;
        payload_bytes -= 2U;
    }

    if(payload_bytes != 0U)
    {
        *dst = src[1];
#if (XE_NP_USB_STREAM_PACKET_HASH_ENABLE != 0U)
        hash = XE_NP_UsbStream_Fnv1a32Update(hash, *dst);
#endif
    }

#if (XE_NP_USB_STREAM_PACKET_HASH_ENABLE != 0U)
    return hash;
#else
    return 0U;
#endif
}

static void XE_NP_UsbStream_ResetLaneValidator(void)
{
    memset(&xe_np_usb_lane_validator, 0, sizeof(xe_np_usb_lane_validator));
    XE_NP_PsbValidator_Init(&xe_np_usb_lane_validator.validator);
}

static void XE_NP_UsbStream_ResetSeamProbe(void)
{
    memset(&xe_np_usb_seam_probe, 0, sizeof(xe_np_usb_seam_probe));
}

static void XE_NP_UsbStream_TryCompareSeamProbe(void)
{
#if (XE_NP_USB_STREAM_SEAM_PROBE_ENABLE != 0U)
    const uint8_t *tail_ptr;
    uint32_t after_hash;
    uint32_t mismatch_count = 0U;

    if((xe_np_usb_seam_probe.active == 0U) ||
       (xe_np_usb_seam_probe.compared != 0U) ||
       (xe_np_usb_state.port == NULL) ||
       (xe_np_usb_state.packets_sent <= xe_np_usb_seam_probe.compare_after_packets_sent))
    {
        return;
    }

    if((xe_np_usb_state.port->storage == NULL) ||
       (xe_np_usb_state.port->storage_physical_bytes == 0U) ||
       ((xe_np_usb_seam_probe.tail_offset + XE_NP_USB_STREAM_SEAM_PROBE_BYTES) >
        xe_np_usb_state.port->storage_physical_bytes))
    {
        xe_np_usb_seam_probe.compared = 1U;
        xe_np_usb_seam_probe.active = 0U;
        return;
    }

    tail_ptr = &xe_np_usb_state.port->storage[xe_np_usb_seam_probe.tail_offset];
    XE_NP_Capture_DCacheInvalidate(tail_ptr, XE_NP_USB_STREAM_SEAM_PROBE_BYTES);
    after_hash = XE_NP_UsbStream_EdgeHash(tail_ptr, XE_NP_USB_STREAM_SEAM_PROBE_BYTES);
    for(uint32_t i = 0U; i < XE_NP_USB_STREAM_SEAM_PROBE_BYTES; ++i)
    {
        if(tail_ptr[i] != xe_np_usb_seam_probe.snapshot[i])
        {
            mismatch_count++;
        }
    }

    xe_np_usb_seam_probe.after_hash = after_hash;
    xe_np_usb_seam_probe.mismatch_count = mismatch_count;
    xe_np_usb_seam_probe.compared = 1U;
    xe_np_usb_seam_probe.active = 0U;
#else
    return;
#endif
}

static void XE_NP_UsbStream_MaybeArmSeamProbe(
    const XE_NP_AppUsbCaptureWindow *source_window,
    const uint8_t *payload_ptr,
    uint32_t payload_bytes)
{
#if (XE_NP_USB_STREAM_SEAM_PROBE_ENABLE != 0U)
    uint32_t payload_offset;
    uint32_t tail_offset;

    if((xe_np_usb_seam_probe.valid != 0U) ||
       (source_window == NULL) ||
       (payload_ptr == NULL) ||
       (xe_np_usb_state.port == NULL) ||
       (XE_NP_UsbStream_IsNp2Lane8Payload() == 0U) ||
       (XE_NP_UsbStream_ShouldPackNp2Lane8() != 0U) ||
       (payload_bytes < (XE_NP_USB_STREAM_SEAM_PROBE_PAYLOAD_OFFSET +
                         XE_NP_USB_STREAM_SEAM_PROBE_BYTES)) ||
       (source_window->ring_offset_bytes >= xe_np_usb_state.port->storage_physical_bytes))
    {
        return;
    }

    payload_offset = XE_NP_USB_STREAM_SEAM_PROBE_PAYLOAD_OFFSET;
    tail_offset = source_window->ring_offset_bytes + payload_offset;
    if((tail_offset + XE_NP_USB_STREAM_SEAM_PROBE_BYTES) >
       xe_np_usb_state.port->storage_physical_bytes)
    {
        return;
    }

    memcpy(
        xe_np_usb_seam_probe.snapshot,
        &payload_ptr[payload_offset],
        XE_NP_USB_STREAM_SEAM_PROBE_BYTES);
    xe_np_usb_seam_probe.packet_sequence = xe_np_usb_state.sequence;
    xe_np_usb_seam_probe.compare_after_packets_sent = xe_np_usb_state.packets_sent + 1U;
    xe_np_usb_seam_probe.ring_offset = source_window->ring_offset_bytes;
    xe_np_usb_seam_probe.payload_offset = payload_offset;
    xe_np_usb_seam_probe.tail_offset = tail_offset;
    xe_np_usb_seam_probe.payload_bytes = payload_bytes;
    xe_np_usb_seam_probe.before_hash =
        XE_NP_UsbStream_EdgeHash(
            &payload_ptr[payload_offset],
            XE_NP_USB_STREAM_SEAM_PROBE_BYTES);
    xe_np_usb_seam_probe.after_hash = 0U;
    xe_np_usb_seam_probe.mismatch_count = 0U;
    xe_np_usb_seam_probe.active = 1U;
    xe_np_usb_seam_probe.compared = 0U;
    xe_np_usb_seam_probe.valid = 1U;
#else
    (void)source_window;
    (void)payload_ptr;
    (void)payload_bytes;
#endif
}

static uint8_t XE_NP_UsbStream_LaneValidatorFrameHasFatalError(uint32_t flags)
{
    return (flags != 0U) ? 1U : 0U;
}

static void XE_NP_UsbStream_LaneValidatorRecordError(
    XE_NP_UsbLaneValidatorState *state,
    uint32_t packet_sequence,
    uint32_t payload_offset,
    uint32_t stream_offset_low,
    uint32_t flags)
{
    if((state == NULL) || (state->first_error_flags != 0U))
    {
        return;
    }

    state->first_error_flags = flags;
    state->first_error_packet_sequence = packet_sequence;
    state->first_error_payload_offset = payload_offset;
    state->first_error_stream_offset_low = stream_offset_low;
    state->disabled = 1U;
}

static uint32_t XE_NP_UsbStream_TryLockLaneValidator(
    XE_NP_UsbLaneValidatorState *state,
    const uint8_t *payload,
    uint32_t payload_bytes)
{
    uint32_t best_offset = 0U;
    uint32_t best_clean_frames = 0U;
    const uint32_t frame_bytes = XE_NP_CAPTURE_FRAME_BIT_COUNT;
    const uint32_t max_probe_frames = 156U;

    if((state == NULL) || (payload == NULL) || (payload_bytes < frame_bytes))
    {
        return 1U;
    }

    for(uint32_t candidate = 0U; candidate < frame_bytes; ++candidate)
    {
        XE_NP_PsbValidator validator;
        XE_NP_PsbFrame frame;
        uint32_t clean_frames = 0U;
        uint32_t frame_offset = candidate;

        (void)XE_NP_PsbFrame_Decode(&frame, &payload[frame_offset], frame_bytes);
        if(((frame.flags & XE_NP_PSB_FRAME_FLAG_SYNC_VALID) == 0U) ||
           ((frame.flags & XE_NP_PSB_FRAME_FLAG_SUPERFRAME) == 0U) ||
           ((frame.flags & XE_NP_PSB_FRAME_FLAG_HEARTBEAT_ERROR) != 0U) ||
           ((frame.flags & XE_NP_PSB_FRAME_FLAG_FILL_ERROR) != 0U))
        {
            continue;
        }

        XE_NP_PsbValidator_Init(&validator);
        validator.expected_fixed_word = frame.fixed_word;
        validator.expected_fixed_word_valid = 1U;
        while((frame_offset + frame_bytes) <= payload_bytes)
        {
            uint32_t flags;

            (void)XE_NP_PsbFrame_Decode(&frame, &payload[frame_offset], frame_bytes);
            flags = XE_NP_PsbValidator_ConsumeFrame(&validator, &frame);
            if(XE_NP_UsbStream_LaneValidatorFrameHasFatalError(flags) != 0U)
            {
                break;
            }

            clean_frames++;
            if(clean_frames >= max_probe_frames)
            {
                break;
            }
            frame_offset += frame_bytes;
        }

        if(clean_frames > best_clean_frames)
        {
            best_clean_frames = clean_frames;
            best_offset = candidate;
            if(best_clean_frames >= max_probe_frames)
            {
                break;
            }
        }
    }

    if(best_clean_frames == 0U)
    {
        return 1U;
    }

    XE_NP_PsbValidator_Init(&state->validator);
    state->validator.expected_fixed_word = 0U;
    state->validator.expected_fixed_word_valid = 0U;
    state->lock_offset = best_offset;
    state->lock_clean_frames = best_clean_frames;
    state->locked = 1U;
    state->tail_len = 0U;
    return 0U;
}

static void XE_NP_UsbStream_ConsumeLaneFrame(
    XE_NP_UsbLaneValidatorState *state,
    uint32_t packet_sequence,
    uint32_t payload_offset,
    uint32_t stream_offset_low,
    const uint8_t *frame_bytes)
{
    XE_NP_PsbFrame frame;
    uint32_t flags;

    if((state == NULL) || (frame_bytes == NULL) || (state->disabled != 0U))
    {
        return;
    }

    (void)XE_NP_PsbFrame_Decode(&frame, frame_bytes, XE_NP_CAPTURE_FRAME_BIT_COUNT);
    if(state->validator.expected_fixed_word_valid == 0U)
    {
        state->validator.expected_fixed_word = frame.fixed_word;
        state->validator.expected_fixed_word_valid = 1U;
    }
    flags = XE_NP_PsbValidator_ConsumeFrame(&state->validator, &frame);
    if(XE_NP_UsbStream_LaneValidatorFrameHasFatalError(flags) != 0U)
    {
        XE_NP_UsbStream_LaneValidatorRecordError(
            state,
            packet_sequence,
            payload_offset,
            stream_offset_low,
            flags);
    }
}

static void XE_NP_UsbStream_ProcessLanePayload(
    uint32_t packet_sequence,
    const uint8_t *payload,
    uint32_t payload_bytes)
{
#if (XE_NP_USB_STREAM_LANE_VALIDATOR_ENABLE == 0U)
    (void)packet_sequence;
    (void)payload;
    (void)payload_bytes;
    return;
#else
    XE_NP_UsbLaneValidatorState *state = &xe_np_usb_lane_validator;
    const uint32_t frame_bytes = XE_NP_CAPTURE_FRAME_BIT_COUNT;
    uint32_t payload_offset = 0U;
    uint8_t frame_buf[XE_NP_CAPTURE_FRAME_BIT_COUNT];
    uint8_t old_tail_len;
    uint64_t packet_stream_base;

    if((payload == NULL) || (payload_bytes == 0U))
    {
        return;
    }
    if((XE_NP_UsbStream_IsNp2Lane8Payload() == 0U) || (xe_np_usb_state.synthetic_source != 0U))
    {
        return;
    }

    state->packets_checked++;
    if(state->disabled != 0U)
    {
        state->payload_bytes_processed += payload_bytes;
        return;
    }

    if(state->locked == 0U)
    {
        if(XE_NP_UsbStream_TryLockLaneValidator(state, payload, payload_bytes) != 0U)
        {
            state->payload_bytes_processed += payload_bytes;
            return;
        }
        payload_offset = state->lock_offset;
    }

    old_tail_len = state->tail_len;
    packet_stream_base = state->payload_bytes_processed;

    if(old_tail_len != 0U)
    {
        uint32_t needed = frame_bytes - old_tail_len;
        if(payload_bytes >= needed)
        {
            memcpy(frame_buf, state->tail, old_tail_len);
            memcpy(&frame_buf[old_tail_len], payload, needed);
            XE_NP_UsbStream_ConsumeLaneFrame(
                state,
                packet_sequence,
                0U,
                (uint32_t)(packet_stream_base - old_tail_len),
                frame_buf);
            payload_offset = needed;
            state->tail_len = 0U;
        }
        else
        {
            memcpy(&state->tail[old_tail_len], payload, payload_bytes);
            state->tail_len = (uint8_t)(old_tail_len + payload_bytes);
            state->payload_bytes_processed += payload_bytes;
            return;
        }
    }

    while((payload_offset + frame_bytes) <= payload_bytes)
    {
        XE_NP_UsbStream_ConsumeLaneFrame(
            state,
            packet_sequence,
            payload_offset,
            (uint32_t)(packet_stream_base + payload_offset - old_tail_len),
            &payload[payload_offset]);
        if(state->disabled != 0U)
        {
            break;
        }
        payload_offset += frame_bytes;
    }

    if((state->disabled == 0U) && (payload_offset < payload_bytes))
    {
        state->tail_len = (uint8_t)(payload_bytes - payload_offset);
        memcpy(state->tail, &payload[payload_offset], state->tail_len);
    }
    else if(state->disabled == 0U)
    {
        state->tail_len = 0U;
    }

    state->payload_bytes_processed += payload_bytes;
#endif
}

static HAL_StatusTypeDef XE_NP_UsbStream_StartPreparedPacket(uint8_t buffer_index)
{
    const uint8_t *packet;
    uint32_t total_len;
    uint32_t payload_bytes;
    uint32_t packet_count;

    if((buffer_index >= XE_NP_USB_STREAM_TX_BUFFER_COUNT) ||
       (xe_np_usb_state.hpcd == NULL) ||
       (xe_np_usb_state.tx_payload_ptr[buffer_index] == NULL) ||
       (xe_np_usb_state.tx_payload_bytes[buffer_index] == 0U))
    {
        XE_NP_UsbStream_SetDiagStatus(0xBAD00007UL);
        return HAL_ERROR;
    }

    payload_bytes = xe_np_usb_state.tx_payload_bytes[buffer_index];
    xe_np_usb_state.tx_active_idx = buffer_index;
    xe_np_usb_state.tx_busy = 1U;
    packet = xe_np_usb_state.tx_payload_ptr[buffer_index];
    total_len = xe_np_usb_state.tx_len[buffer_index];
    if((packet == NULL) || (total_len == 0U) ||
       ((total_len % XE_NP_USB_STREAM_PACKET_BYTES) != 0U))
    {
        XE_NP_UsbStream_ReleaseCompletedCaptureWindow();
        xe_np_usb_state.tx_busy = 0U;
        XE_NP_UsbStream_SetDiagStatus(0xBAD00007UL);
        return HAL_ERROR;
    }

    xe_np_usb_state.tx_stage = 0U;
    packet_count = payload_bytes / ((!xe_np_usb_state.synthetic_source && NP_USB20_PAIRED())?504U:XE_NP_USB_STREAM_PAYLOAD_BYTES); /* NP only; misc has independent sequence. */
    /* Keep the endpoint source coherent in both OTG modes.  The HS core may
     * fetch the buffer through its bus-master path even when its internal-DMA
     * option is disabled; USB512 uses the same unconditional clean. */
    __DMB();
    if(!xe_np_usb_state.synthetic_source && packet == xe_np_usb_tx_packet[buffer_index] &&
       payload_bytes == NP_DIRECT_GROUPS * NP_DIRECT_LANES * (NP_USB20_PAIRED()?504U:NP_DIRECT_PAYLOAD) && total_len >= NP_DIRECT_WIRE_BYTES)
    {
        if(total_len > NP_DIRECT_WIRE_BYTES)
            XE_NP_UsbStream_DCacheCleanTx(packet + NP_DIRECT_WIRE_BYTES, total_len - NP_DIRECT_WIRE_BYTES);
        __DSB(); /* DMA completion and header pre-clean precede USB ownership. */
    }
    else XE_NP_UsbStream_DCacheCleanTx(packet, total_len);

    if(HAL_PCD_EP_Transmit(
           xe_np_usb_state.hpcd,
           XE_NP_USB_STREAM_IN_EP,
           (uint8_t *)(void *)packet,
           total_len) == HAL_OK)
    {
        xe_np_usb_state.sequence += packet_count;
        xe_np_usb_state.packets_sent += packet_count;
        xe_np_usb_state.payload_bytes_sent += payload_bytes;
        xe_np_usb_state.last_tx_tick_ms = HAL_GetTick();
        XE_NP_UsbStream_SetDiagStatus(0U);
        return HAL_OK;
    }

    XE_NP_UsbStream_ReleaseCompletedCaptureWindow();
    xe_np_usb_state.tx_busy = 0U;
    XE_NP_UsbStream_SetDiagStatus(0xBAD00003UL);
    return HAL_ERROR;
}

static HAL_StatusTypeDef XE_NP_UsbStream_StartPreparedPayload(uint8_t buffer_index)
{
    const uint8_t *payload;
    uint32_t payload_bytes;

    if((buffer_index >= XE_NP_USB_STREAM_TX_BUFFER_COUNT) ||
       (xe_np_usb_state.hpcd == NULL))
    {
        XE_NP_UsbStream_SetDiagStatus(0xBAD00007UL);
        return HAL_ERROR;
    }

    payload = xe_np_usb_state.tx_payload_ptr[buffer_index];
    payload_bytes = xe_np_usb_state.tx_payload_bytes[buffer_index];
    if((payload == NULL) || (payload_bytes == 0U))
    {
        XE_NP_UsbStream_SetDiagStatus(0xBAD00007UL);
        return HAL_ERROR;
    }

    __DMB();
    XE_NP_UsbStream_DCacheCleanTx(payload, payload_bytes);

    if(HAL_PCD_EP_Transmit(
           xe_np_usb_state.hpcd,
           XE_NP_USB_STREAM_IN_EP,
           (uint8_t *)(void *)payload,
           payload_bytes) == HAL_OK)
    {
        xe_np_usb_state.tx_stage = 2U;
        XE_NP_UsbStream_SetDiagStatus(0U);
        return HAL_OK;
    }

    XE_NP_UsbStream_ReleaseCompletedCaptureWindow();
    xe_np_usb_state.tx_busy = 0U;
    xe_np_usb_state.tx_stage = 0U;
    xe_np_usb_state.packet_assembly_idx = 0U;
    xe_np_usb_state.packet_assembly_bytes = 0U;
    XE_NP_UsbStream_SetDiagStatus(0xBAD00009UL);
    return HAL_ERROR;
}

static void XE_NP_UsbStream_StartPendingPacket(void)
{
    uint8_t pending_idx;
    if(xe_np_usb_state.streaming && !xe_np_usb_state.synthetic_source)
    { NP_UsbQueueKick(); return; }

    if((xe_np_usb_state.tx_busy != 0U) || (xe_np_usb_state.tx_pending == 0U))
    {
        return;
    }

    pending_idx = xe_np_usb_state.tx_pending_idx;
    xe_np_usb_state.tx_pending = 0U;
    (void)XE_NP_UsbStream_StartPreparedPacket(pending_idx);
}

static void XE_NP_UsbStream_ReleaseCompletedCaptureWindow(void)
{
#if (XE_NP_USB_STREAM_REAL_CAPTURE_SKIP_RELEASE_ENABLE != 0U)
    /* Short-lived endpoint isolation: leave the app-side window untouched
     * so the IN-complete callback is otherwise identical to the synthetic
     * source. Never enable for a real recording: ring backpressure must win. */
    return;
#else
    if((xe_np_usb_state.synthetic_source == 0U) && (xe_np_usb_state.port != NULL))
    {
        XE_NP_App_ReleaseUsbActiveWindow(xe_np_usb_state.port_index);
    }
#endif
}

static void XE_NP_UsbStream_ClearCtlInState(void)
{
    xe_np_usb_state.ep0_tx_data = NULL;
    xe_np_usb_state.ep0_tx_len = 0U;
    xe_np_usb_state.ep0_tx_sent = 0U;
    xe_np_usb_state.ep0_zlp_pending = 0U;
}

#include "C:/Frank/Code/XE_NP/_artifacts/v1_usb_fast_startup_r79r2_20261006/source/Core/Src/np_sd_usb_ep0.inc"
static void XE_NP_UsbStream_ParseSetup(const PCD_HandleTypeDef *hpcd, XE_NP_UsbSetup *req)
{
    const uint8_t *setup = (const uint8_t *)(const void *)hpcd->Setup;

    req->bmRequest = setup[0];
    req->bRequest = setup[1];
    req->wValue = (uint16_t)setup[2] | ((uint16_t)setup[3] << 8U);
    req->wIndex = (uint16_t)setup[4] | ((uint16_t)setup[5] << 8U);
    req->wLength = (uint16_t)setup[6] | ((uint16_t)setup[7] << 8U);
}

static HAL_StatusTypeDef XE_NP_UsbStream_CtlSend(
    PCD_HandleTypeDef *hpcd,
    const uint8_t *data,
    uint16_t len,
    uint16_t requested_len)
{
    uint16_t send_len = XE_NP_UsbStream_MinU16(len, requested_len);

    xe_np_usb_state.ep0_tx_data = data;
    xe_np_usb_state.ep0_tx_len = send_len;
    xe_np_usb_state.ep0_tx_sent = 0U;
    xe_np_usb_state.ep0_zlp_pending =
        ((send_len != requested_len) &&
         (send_len != 0U) &&
         ((send_len % XE_NP_USB_STREAM_EP0_MPS) == 0U)) ? 1U : 0U;
    xe_np_usb_state.ep0_status_out_pending = (send_len != 0U) ? 1U : 0U;
    xe_np_usb_stream_diag.ep0_last_in_len = send_len;
    if(send_len == 0U)
    {
        return HAL_PCD_EP_Transmit(hpcd, 0x80U, xe_np_usb_string_buf, 0U);
    }
    return XE_NP_UsbStream_CtlSendNext(hpcd);
}

static void XE_NP_UsbStream_CtlStatus(PCD_HandleTypeDef *hpcd)
{
    XE_NP_UsbStream_ClearCtlInState();
    xe_np_usb_state.ep0_status_out_pending = 0U;
    xe_np_usb_stream_diag.ep0_last_in_len = 0U;
    (void)HAL_PCD_EP_Transmit(hpcd, 0x80U, xe_np_usb_string_buf, 0U);
}

static void XE_NP_UsbStream_Stall(PCD_HandleTypeDef *hpcd)
{
    NP_UsbControlReject(hpcd); /* Before clearing the failed control phase. */
    XE_NP_UsbStream_ClearCtlInState();
    xe_np_usb_state.ep0_status_out_pending = 0U;
    xe_np_usb_state.ep0_address_pending = 0U;
    (void)HAL_PCD_EP_SetStall(hpcd, 0x80U);
    (void)HAL_PCD_EP_SetStall(hpcd, 0x00U);
}

static void XE_NP_UsbStream_PrepareSetup(PCD_HandleTypeDef *hpcd)
{
#if defined(USB_OTG_FS) || defined(USB_OTG_HS)
    if(hpcd != NULL)
    {
        (void)USB_EP0_OutStart(
            hpcd->Instance,
            (uint8_t)hpcd->Init.dma_enable,
            (uint8_t *)hpcd->Setup);
    }
#else
    (void)hpcd;
#endif
}

static uint16_t XE_NP_UsbStream_BuildStringDescriptor(const char *text)
{
    uint16_t len = 2U;

    xe_np_usb_string_buf[1] = 0x03U;
    if(text != NULL)
    {
        while((text[0] != '\0') && ((len + 2U) <= sizeof(xe_np_usb_string_buf)))
        {
            xe_np_usb_string_buf[len] = (uint8_t)text[0];
            xe_np_usb_string_buf[len + 1U] = 0U;
            len = (uint16_t)(len + 2U);
            text++;
        }
    }
    xe_np_usb_string_buf[0] = (uint8_t)len;
    return len;
}

static uint16_t XE_NP_UsbStream_SelectString(uint8_t index)
{
    static const uint8_t lang_id[] = {0x04, 0x03, 0x09, 0x04};
    static const uint8_t msft100[] = {
        0x12, 0x03,
        'M', 0x00, 'S', 0x00, 'F', 0x00, 'T', 0x00,
        '1', 0x00, '0', 0x00, '0', 0x00,
        XE_NP_USB_STREAM_VENDOR_CODE, 0x00,
    };

    if(index == 0U)
    {
        memcpy(xe_np_usb_string_buf, lang_id, sizeof(lang_id));
        return (uint16_t)sizeof(lang_id);
    }
    if(index == 0xEEU)
    {
        memcpy(xe_np_usb_string_buf, msft100, sizeof(msft100));
        return (uint16_t)sizeof(msft100);
    }
    if(index == 1U)
    {
        return XE_NP_UsbStream_BuildStringDescriptor("WILDX");
    }
    if(index == 2U)
    {
        return XE_NP_UsbStream_BuildStringDescriptor("WILDX USB Device");
    }
    if(index == 3U)
    {
        char serial[30];
        np_sd_usb_serial(serial,HAL_GetUIDw0(),HAL_GetUIDw1(),HAL_GetUIDw2());
        return XE_NP_UsbStream_BuildStringDescriptor(serial);
    }
    if(index == 4U)
    {
        return XE_NP_UsbStream_BuildStringDescriptor("Neuropixels Raw Stream");
    }
    return 0U;
}

static void XE_NP_UsbStream_SetDiagStatus(uint32_t status)
{
    xe_np_usb_stream_diag.last_status = status;
}

#if NP_USB_BLOCK_PROBE
static void XE_NP_UsbStream_DCacheCleanTx_BlockBody(const void *addr, uint32_t size_bytes, uint32_t *block_result)
{
#if (XE_NP_USB_STREAM_QUAD_ZEROCOPY_ENABLE != 0U)
    /* Quad capture storage is explicitly non-cacheable in this experiment;
     * maintenance would add the very bus traffic being characterized. */
    if(XE_NP_Capture_Sram3RangeContained(addr, size_bytes) != 0U)
    {
        return;
    }
#if (XE_NP_USB_STREAM_CAPTURE_SRAM1_ENABLE != 0U)
    if((((uintptr_t)addr) >= 0x20060000UL) &&
       ((((uintptr_t)addr) + (uintptr_t)size_bytes) <= 0x200C0000UL))
    {
        return;
    }
#endif
#endif
#if defined(DCACHE1) && defined(HAL_DCACHE_MODULE_ENABLED)
    uintptr_t start_addr;
    uintptr_t end_addr;

    if((addr == NULL) || (size_bytes == 0U))
    {
        return;
    }

    start_addr = ((uintptr_t)addr) & ~((uintptr_t)XE_NP_CAPTURE_DCACHE_LINE_BYTES - 1U);
    end_addr = (((uintptr_t)addr) + (uintptr_t)size_bytes + (XE_NP_CAPTURE_DCACHE_LINE_BYTES - 1U)) &
               ~((uintptr_t)XE_NP_CAPTURE_DCACHE_LINE_BYTES - 1U);
    *block_result=(uint32_t)HAL_DCACHE_CleanByAddr(
        &hdcache1,
        (const uint32_t *const)start_addr,
        (uint32_t)(end_addr - start_addr));
#elif defined(__DCACHE_PRESENT) && (__DCACHE_PRESENT == 1U)
    uintptr_t start_addr;
    uintptr_t end_addr;

    if((addr == NULL) || (size_bytes == 0U))
    {
        return;
    }

    start_addr = ((uintptr_t)addr) & ~((uintptr_t)XE_NP_CAPTURE_DCACHE_LINE_BYTES - 1U);
    end_addr = (((uintptr_t)addr) + (uintptr_t)size_bytes + (XE_NP_CAPTURE_DCACHE_LINE_BYTES - 1U)) &
               ~((uintptr_t)XE_NP_CAPTURE_DCACHE_LINE_BYTES - 1U);
    SCB_CleanDCache_by_Addr((void *)start_addr, (int32_t)(end_addr - start_addr));
#else
    (void)addr;
    (void)size_bytes;
#endif
}
static void XE_NP_UsbStream_DCacheCleanTx(const void *addr,uint32_t size_bytes)
{
 uint32_t result=NP_UB_UNKNOWN_RESULT;
 NpUsbBlockStamp block=NP_UsbBlockBegin(NP_UB_CLEAN,(uint32_t)(uintptr_t)addr,size_bytes,
     hdcache1.Instance?hdcache1.Instance->SR:0U);
 XE_NP_UsbStream_DCacheCleanTx_BlockBody(addr,size_bytes,&result);
 NP_UsbBlockEnd(block,result,hdcache1.Instance?hdcache1.Instance->SR:0U);
}
#else
static void XE_NP_UsbStream_DCacheCleanTx(const void *addr, uint32_t size_bytes)
{
#if (XE_NP_USB_STREAM_QUAD_ZEROCOPY_ENABLE != 0U)
    /* Quad capture storage is explicitly non-cacheable in this experiment;
     * maintenance would add the very bus traffic being characterized. */
    if(XE_NP_Capture_Sram3RangeContained(addr, size_bytes) != 0U)
    {
        return;
    }
#if (XE_NP_USB_STREAM_CAPTURE_SRAM1_ENABLE != 0U)
    if((((uintptr_t)addr) >= 0x20060000UL) &&
       ((((uintptr_t)addr) + (uintptr_t)size_bytes) <= 0x200C0000UL))
    {
        return;
    }
#endif
#endif
#if defined(DCACHE1) && defined(HAL_DCACHE_MODULE_ENABLED)
    uintptr_t start_addr;
    uintptr_t end_addr;

    if((addr == NULL) || (size_bytes == 0U))
    {
        return;
    }

    start_addr = ((uintptr_t)addr) & ~((uintptr_t)XE_NP_CAPTURE_DCACHE_LINE_BYTES - 1U);
    end_addr = (((uintptr_t)addr) + (uintptr_t)size_bytes + (XE_NP_CAPTURE_DCACHE_LINE_BYTES - 1U)) &
               ~((uintptr_t)XE_NP_CAPTURE_DCACHE_LINE_BYTES - 1U);
    (void)HAL_DCACHE_CleanByAddr(
        &hdcache1,
        (const uint32_t *const)start_addr,
        (uint32_t)(end_addr - start_addr));
#elif defined(__DCACHE_PRESENT) && (__DCACHE_PRESENT == 1U)
    uintptr_t start_addr;
    uintptr_t end_addr;

    if((addr == NULL) || (size_bytes == 0U))
    {
        return;
    }

    start_addr = ((uintptr_t)addr) & ~((uintptr_t)XE_NP_CAPTURE_DCACHE_LINE_BYTES - 1U);
    end_addr = (((uintptr_t)addr) + (uintptr_t)size_bytes + (XE_NP_CAPTURE_DCACHE_LINE_BYTES - 1U)) &
               ~((uintptr_t)XE_NP_CAPTURE_DCACHE_LINE_BYTES - 1U);
    SCB_CleanDCache_by_Addr((void *)start_addr, (int32_t)(end_addr - start_addr));
#else
    (void)addr;
    (void)size_bytes;
#endif
}
#endif

static int XE_NP_UsbStream_InitInterleavedStagingDma(void)
{
#if ((XE_NP_USB_STREAM_INTERLEAVED_STAGING_DMA_ENABLE != 0U) || \
     (XE_NP_USB_STREAM_QUAD_STAGING_DMA_ENABLE != 0U))
    DMA_DataHandlingConfTypeDef data_handling;

    if(xe_np_usb_interleaved_staging_dma.initialized != 0U)
    {
        return 0;
    }
#if (XE_NP_USB_STREAM_QUAD_STAGING_LPDMA_ENABLE != 0U)
    __HAL_RCC_LPDMA1_CLK_ENABLE();
#else
    __HAL_RCC_GPDMA1_CLK_ENABLE();
#endif
    memset(&xe_np_usb_interleaved_staging_dma.hdma, 0,
           sizeof(xe_np_usb_interleaved_staging_dma.hdma));
#if (XE_NP_USB_STREAM_QUAD_STAGING_LPDMA_ENABLE != 0U)
    xe_np_usb_interleaved_staging_dma.hdma.Instance = LPDMA1_Channel0;
#elif (XE_NP_USB_STREAM_INTERLEAVED_STAGING_DMA_CHANNEL == 10U)
    xe_np_usb_interleaved_staging_dma.hdma.Instance = GPDMA1_Channel10;
#else
    xe_np_usb_interleaved_staging_dma.hdma.Instance = GPDMA1_Channel14;
#endif
    xe_np_usb_interleaved_staging_dma.hdma.Init.Request = DMA_REQUEST_SW;
#if (XE_NP_USB_STREAM_QUAD_STAGING_DMA_ENABLE != 0U)
    xe_np_usb_interleaved_staging_dma.hdma.InitLinkedList.Priority =
        XE_NP_USB_STREAM_INTERLEAVED_STAGING_DMA_PRIORITY;
    xe_np_usb_interleaved_staging_dma.hdma.InitLinkedList.LinkStepMode = DMA_LSM_FULL_EXECUTION;
    /* Descriptors live in ordinary SRAM1, the same port as their private USB
     * destination.  They must never reload through the SRAM3 capture port. */
#if (XE_NP_USB_STREAM_QUAD_STAGING_LPDMA_ENABLE == 0U)
    xe_np_usb_interleaved_staging_dma.hdma.InitLinkedList.LinkAllocatedPort =
#if (XE_NP_APP_QUAD_USB_WORD_EXPORT != 0U)
        DMA_LINK_ALLOCATED_PORT1;
#else
        DMA_LINK_ALLOCATED_PORT0;
#endif
#endif
    xe_np_usb_interleaved_staging_dma.hdma.InitLinkedList.TransferEventMode =
        DMA_TCEM_LAST_LL_ITEM_TRANSFER;
    xe_np_usb_interleaved_staging_dma.hdma.InitLinkedList.LinkedListMode =
        DMA_LINKEDLIST_NORMAL;
    if(HAL_DMAEx_List_Init(&xe_np_usb_interleaved_staging_dma.hdma) != HAL_OK)
    {
        return -1;
    }
#else
    xe_np_usb_interleaved_staging_dma.hdma.Init.BlkHWRequest = DMA_BREQ_SINGLE_BURST;
    xe_np_usb_interleaved_staging_dma.hdma.Init.Direction = DMA_MEMORY_TO_MEMORY;
    xe_np_usb_interleaved_staging_dma.hdma.Init.SrcInc = DMA_SINC_INCREMENTED;
    xe_np_usb_interleaved_staging_dma.hdma.Init.DestInc = DMA_DINC_INCREMENTED;
    xe_np_usb_interleaved_staging_dma.hdma.Init.SrcDataWidth = DMA_SRC_DATAWIDTH_BYTE;
    xe_np_usb_interleaved_staging_dma.hdma.Init.DestDataWidth = DMA_DEST_DATAWIDTH_BYTE;
    /* GPIO capture occupies channels 12/13 at high priority.  This bulk copy
     * must always yield its SRAM3 source port to an edge-triggered capture
     * request, while retaining enough weight to empty a completed lane. */
    xe_np_usb_interleaved_staging_dma.hdma.Init.Priority =
        XE_NP_USB_STREAM_INTERLEAVED_STAGING_DMA_PRIORITY;
    xe_np_usb_interleaved_staging_dma.hdma.Init.SrcBurstLength = 1U;
    xe_np_usb_interleaved_staging_dma.hdma.Init.DestBurstLength = 1U;
    xe_np_usb_interleaved_staging_dma.hdma.Init.TransferAllocatedPort =
        DMA_SRC_ALLOCATED_PORT1 | DMA_DEST_ALLOCATED_PORT0;
    xe_np_usb_interleaved_staging_dma.hdma.Init.TransferEventMode = DMA_TCEM_REPEATED_BLOCK_TRANSFER;
    xe_np_usb_interleaved_staging_dma.hdma.Init.Mode = DMA_NORMAL;
    if(HAL_DMA_Init(&xe_np_usb_interleaved_staging_dma.hdma) != HAL_OK)
    {
        return -1;
    }
#endif
    memset(&data_handling, 0, sizeof(data_handling));
    data_handling.DataExchange = DMA_EXCHANGE_NONE;
    data_handling.DataAlignment = DMA_DATA_RIGHTALIGN_ZEROPADDED;
    if((HAL_DMAEx_ConfigDataHandling(&xe_np_usb_interleaved_staging_dma.hdma,
                                     &data_handling) != HAL_OK) ||
       (HAL_DMA_RegisterCallback(&xe_np_usb_interleaved_staging_dma.hdma,
                                 HAL_DMA_XFER_CPLT_CB_ID,
                                 XE_NP_UsbStream_InterleavedStagingComplete) != HAL_OK) ||
       (HAL_DMA_RegisterCallback(&xe_np_usb_interleaved_staging_dma.hdma,
                                 HAL_DMA_XFER_ERROR_CB_ID,
                                 XE_NP_UsbStream_InterleavedStagingError) != HAL_OK))
    {
        (void)HAL_DMA_DeInit(&xe_np_usb_interleaved_staging_dma.hdma);
        return -1;
    }
#if (XE_NP_USB_STREAM_QUAD_STAGING_LPDMA_ENABLE != 0U)
    HAL_NVIC_SetPriority(LPDMA1_Channel0_IRQn, 6U, 0U);
    HAL_NVIC_EnableIRQ(LPDMA1_Channel0_IRQn);
#elif (XE_NP_USB_STREAM_INTERLEAVED_STAGING_DMA_CHANNEL == 10U)
    HAL_NVIC_SetPriority(GPDMA1_Channel10_IRQn, 6U, 0U);
    HAL_NVIC_EnableIRQ(GPDMA1_Channel10_IRQn);
#else
    HAL_NVIC_SetPriority(GPDMA1_Channel14_IRQn, 6U, 0U);
    HAL_NVIC_EnableIRQ(GPDMA1_Channel14_IRQn);
#endif
    xe_np_usb_interleaved_staging_dma.initialized = 1U;
    return 0;
#else
    return 0;
#endif
}

/* NP_USB_STAGE_RETIRE_GUARD: lifecycle only. Never wait for SysTick in
 * EP0/IRQ context, and never release a source claim while DMA still owns it. */
static int NP_UsbStageRetireHardware(void)
{
#if ((XE_NP_USB_STREAM_INTERLEAVED_STAGING_DMA_ENABLE != 0U) || \
     (XE_NP_USB_STREAM_QUAD_STAGING_DMA_ENABLE != 0U))
    DMA_HandleTypeDef *h=&xe_np_usb_interleaved_staging_dma.hdma;
    if(h->Instance==NULL)
        return (xe_np_usb_interleaved_staging_dma.initialized || xe_np_usb_interleaved_staging_dma.busy)?-1:0;
    h->Instance->CCR&=~(DMA_CCR_TCIE|DMA_CCR_HTIE|DMA_CCR_DTEIE|DMA_CCR_ULEIE|
                        DMA_CCR_USEIE|DMA_CCR_SUSPIE|DMA_CCR_TOIE);
    if(h->Instance->CCR&DMA_CCR_EN){
        h->Instance->CCR|=DMA_CCR_SUSP;__DSB();
        for(uint32_t n=0U;n<1024U && (h->Instance->CCR&DMA_CCR_EN) &&
            !(h->Instance->CSR&DMA_CSR_SUSPF);n++)__NOP();
        if(h->Instance->CCR&DMA_CCR_EN){
            if(!(h->Instance->CSR&DMA_CSR_SUSPF))return -1;
            h->Instance->CCR|=DMA_CCR_RESET;__DSB();
            for(uint32_t n=0U;n<1024U && (h->Instance->CCR&DMA_CCR_EN);n++)__NOP();
            if(h->Instance->CCR&DMA_CCR_EN)return -1;
        }
    }
    __HAL_DMA_CLEAR_FLAG(h,DMA_FLAG_TC|DMA_FLAG_HT|DMA_FLAG_DTE|DMA_FLAG_ULE|
                          DMA_FLAG_USE|DMA_FLAG_SUSP|DMA_FLAG_TO);
    h->Instance->CBR1=0U;h->Instance->CLLR=0U;
    h->State=HAL_DMA_STATE_READY;h->ErrorCode=HAL_DMA_ERROR_NONE;
    if(h->LinkedListQueue!=NULL)h->LinkedListQueue->State=HAL_DMA_QUEUE_STATE_READY;
    __HAL_UNLOCK(h);__DSB();
#endif
    return 0;
}

static int XE_NP_UsbStream_StopInterleavedStagingDma(void)
{
    uint32_t mask=__get_PRIMASK();__disable_irq();
#if (XE_NP_APP_QUAD_USB_WORD_EXPORT != 0U)
    xe_np_usb_quad_word_ready = 0U;
#endif
    if(NP_UsbStageRetireHardware()!=0){
        np_usb_stage_retire_failed=1U;
        xe_np_usb_interleaved_staging_dma.busy=1U;
        xe_np_usb_interleaved_staging_dma.error=1U;
        __set_PRIMASK(mask);return -1;
    }
    if(xe_np_usb_interleaved_staging_dma.quad_mode != 0U)
    {
        XE_NP_App_CancelUsbQuadDmaBatch();
    }
    else
    {
        XE_NP_App_CancelUsbInterleavedDmaBatch();
    }
    xe_np_usb_interleaved_staging_dma.busy = 0U;
    xe_np_usb_interleaved_staging_dma.error = 0U;
    xe_np_usb_interleaved_staging_dma.phase_lane_b = 0U;
    xe_np_usb_interleaved_staging_dma.quad_mode = 0U;
    xe_np_usb_interleaved_staging_dma.packet_count = 0U;
    xe_np_usb_interleaved_staging_dma.quad_sequence_valid = 0U;
    xe_np_usb_interleaved_staging_dma.quad_sequence_base = 0U;
    xe_np_usb_interleaved_staging_dma.quad_next_sequence = 0U;
    memset(&xe_np_usb_interleaved_staging_dma.batch, 0,
           sizeof(xe_np_usb_interleaved_staging_dma.batch));
    memset(&xe_np_usb_interleaved_staging_dma.quad_batch, 0,
           sizeof(xe_np_usb_interleaved_staging_dma.quad_batch));
    __set_PRIMASK(mask);return 0;
}

static HAL_StatusTypeDef XE_NP_UsbStream_StartInterleavedStagingLane(uint8_t lane_b)
{
#if (XE_NP_USB_STREAM_INTERLEAVED_STAGING_DMA_ENABLE != 0U)
    DMA_RepeatBlockConfTypeDef repeat;
    const XE_NP_AppUsbInterleavedDmaBatch *batch = &xe_np_usb_interleaved_staging_dma.batch;
    const uint8_t *src = (lane_b != 0U) ? batch->lane_b : batch->lane_a;
    uint32_t lane_packets = (lane_b != 0U) ?
        batch->lane_b_packet_count : batch->lane_a_packet_count;
    uint32_t first_packet_offset;
    uint8_t *dst;

    if((src == NULL) || (lane_packets == 0U) || (batch->payload_bytes_per_packet == 0U))
    {
        return HAL_ERROR;
    }
    first_packet_offset =
        ((batch->starts_with_lane_b != 0U) == (lane_b != 0U)) ? 0U : 1U;
    dst = &xe_np_usb_tx_packet[xe_np_usb_interleaved_staging_dma.buffer_index][
        ((uint32_t)xe_np_usb_interleaved_staging_dma.packet_start_index +
         first_packet_offset) * XE_NP_USB_STREAM_PACKET_BYTES +
        XE_NP_USB_STREAM_HEADER_BYTES];

    memset(&repeat, 0, sizeof(repeat));
    repeat.RepeatCount = lane_packets;
    repeat.SrcAddrOffset = 0;
    repeat.DestAddrOffset = 0;
    repeat.BlkSrcAddrOffset = 0;
    /* Auto-increment consumes the 506-byte payload; skip the next packet's
     * header plus its opposite lane payload to reach the next same-lane slot. */
    repeat.BlkDestAddrOffset =
        (2U * XE_NP_USB_STREAM_PACKET_BYTES) - batch->payload_bytes_per_packet;
    if(HAL_DMAEx_ConfigRepeatBlock(&xe_np_usb_interleaved_staging_dma.hdma, &repeat) != HAL_OK)
    {
        return HAL_ERROR;
    }
    xe_np_usb_interleaved_staging_dma.phase_lane_b = lane_b;
    return HAL_DMA_Start_IT(
        &xe_np_usb_interleaved_staging_dma.hdma,
        (uint32_t)src,
        (uint32_t)dst,
        batch->payload_bytes_per_packet);
#else
    (void)lane_b;
    return HAL_ERROR;
#endif
}

static HAL_StatusTypeDef XE_NP_UsbStream_BuildQuadStagingNode(
    DMA_NodeTypeDef *node,
    const uint8_t *src,
    uint8_t *dst,
    uint32_t bytes)
{
    DMA_NodeConfTypeDef config;

    if((node == NULL) || (src == NULL) || (dst == NULL) || (bytes == 0U))
    {
        return HAL_ERROR;
    }
    memset(&config, 0, sizeof(config));
#if (XE_NP_USB_STREAM_QUAD_STAGING_LPDMA_ENABLE != 0U)
    config.NodeType = DMA_LPDMA_LINEAR_NODE;
#else
    config.NodeType = DMA_GPDMA_LINEAR_NODE;
#endif
    config.Init.Request = DMA_REQUEST_SW;
    config.Init.BlkHWRequest = DMA_BREQ_SINGLE_BURST;
    config.Init.Direction = DMA_MEMORY_TO_MEMORY;
    config.Init.SrcInc = DMA_SINC_INCREMENTED;
    config.Init.DestInc = DMA_DINC_INCREMENTED;
    config.Init.SrcDataWidth = DMA_SRC_DATAWIDTH_BYTE;
    config.Init.DestDataWidth = DMA_DEST_DATAWIDTH_BYTE;
    config.Init.Priority = XE_NP_USB_STREAM_INTERLEAVED_STAGING_DMA_PRIORITY;
    /* A single beat guarantees that a GPIO request can pre-empt this bulk
     * mover at the next AHB arbitration point. */
    config.Init.SrcBurstLength = 4U;
    config.Init.DestBurstLength = 4U;
#if (XE_NP_USB_STREAM_QUAD_STAGING_LPDMA_ENABLE != 0U)
    /* LPDMA has one AHB master, so its port-allocation fields are ignored. */
    config.Init.TransferAllocatedPort = DMA_SRC_ALLOCATED_PORT0 | DMA_DEST_ALLOCATED_PORT0;
#else
#if (XE_NP_USB_STREAM_CAPTURE_SRAM1_ENABLE != 0U)
    config.Init.TransferAllocatedPort = DMA_SRC_ALLOCATED_PORT0 | DMA_DEST_ALLOCATED_PORT0;
#else
    config.Init.TransferAllocatedPort = DMA_SRC_ALLOCATED_PORT1 | DMA_DEST_ALLOCATED_PORT0;
#endif
#endif
    config.Init.TransferEventMode = DMA_TCEM_BLOCK_TRANSFER;
    config.Init.Mode = DMA_NORMAL;
#if (XE_NP_APP_QUAD_USB_WORD_EXPORT != 0U)
    if(((uintptr_t)src | (uintptr_t)dst | bytes) & 1U) { return HAL_ERROR; }
    config.Init.SrcDataWidth = DMA_SRC_DATAWIDTH_HALFWORD;
    config.Init.DestDataWidth = DMA_DEST_DATAWIDTH_HALFWORD;
    /* CTR2 is reloaded from EVERY node. The handle's LAST_LL setting alone
     * is insufficient: block-level TC would publish partially copied lanes. */
    config.Init.TransferEventMode = DMA_TCEM_LAST_LL_ITEM_TRANSFER;
    /* All staging reads, writes AND descriptor fetches use port 1. Capture
     * GPIO reads retain port 0. Bank/matrix contention can still occur. */
    config.Init.TransferAllocatedPort = DMA_SRC_ALLOCATED_PORT1 | DMA_DEST_ALLOCATED_PORT1;
#if (XE_NP_APP_QUAD_USB_PACED != 0U)
    /* Separate TIM2 CH3 compare paces single-word reads; all source and
     * destination traffic remains on port 1. This is not a latency guarantee. */
    config.Init.Direction = DMA_PERIPH_TO_MEMORY;
    config.Init.Request = GPDMA1_REQUEST_TIM2_CH3;
    config.Init.SrcBurstLength = 4U;
    config.Init.DestBurstLength = 4U;
#endif
#endif
    if(NP_USB20_PAIRED() && (((uintptr_t)src | (uintptr_t)dst | bytes) & 3U))return HAL_ERROR;
    config.DataHandlingConfig.DataExchange = DMA_EXCHANGE_NONE;
    config.DataHandlingConfig.DataAlignment = DMA_DATA_RIGHTALIGN_ZEROPADDED;
    if(NP_USB20_PAIRED()) {
        /* One aligned word copy preserves both chronological GPIO samples.
         * The PC separates P1/P2; no second read or CPU payload copy. */
        config.Init.Direction=DMA_PERIPH_TO_MEMORY;
        config.Init.Request=GPDMA1_REQUEST_TIM2_CH3;
        config.Init.SrcDataWidth=DMA_SRC_DATAWIDTH_WORD;
        config.Init.DestDataWidth=DMA_DEST_DATAWIDTH_WORD;
        config.Init.SrcBurstLength=1U;config.Init.DestBurstLength=1U;
        config.DataHandlingConfig.DataExchange=DMA_EXCHANGE_NONE;
        config.DataHandlingConfig.DataAlignment=DMA_DATA_RIGHTALIGN_ZEROPADDED;
    }
    config.TriggerConfig.TriggerMode = DMA_TRIGM_SINGLE_BURST_TRANSFER;
    config.TriggerConfig.TriggerPolarity = DMA_TRIG_POLARITY_MASKED;
    config.TriggerConfig.TriggerSelection = 0U;
    config.SrcAddress = (uint32_t)src;
    config.DstAddress = (uint32_t)dst;
    config.DataSize = bytes;
    config.RepeatBlockConfig.RepeatCount = 1U;
    return HAL_DMAEx_List_BuildNode(&config, node);
}

static int NP_DirectEmit(void *context, const uint8_t *src, uint8_t *dst, uint32_t bytes)
{
    uint32_t *count = (uint32_t *)context;
    if(*count >= XE_NP_USB_STREAM_QUAD_STAGING_MAX_NODES) return -1;
    DMA_NodeTypeDef *node = &xe_np_usb_interleaved_staging_dma.quad_nodes[*count];
    if(XE_NP_UsbStream_BuildQuadStagingNode(node, src, dst, bytes) != HAL_OK) return -1;
    ++*count;
    return 0;
}

/* Build one low-priority linked-list transfer for 15 four-lane sample groups.
 * The CPU writes only packet headers and DMA descriptors in SRAM1; it never
 * dereferences capture SRAM3.  A lane ring can wrap at any byte, so a physical
 * 506-byte packet is split into two DMA nodes only when it crosses that ring
 * boundary (at most four extra nodes per 60-packet batch). */
#if NP_USB_BLOCK_PROBE
static int XE_NP_UsbStream_StartQuadStagingBatchOwned_BlockBody(uint8_t buffer_index,uint32_t generation)
{
#if (XE_NP_USB_STREAM_QUAD_STAGING_DMA_ENABLE != 0U)
    /* NP_USB_STAGE_LAUNCH_GUARD: preparation stays interruptible. STOP/reset
     * may cancel the claim meanwhile; never arm a stale descriptor chain. */
    if(!xe_np_usb_state.streaming || generation!=xe_np_usb_disarm_count)return 0;
    XE_NP_AppUsbQuadDmaBatch batch;
    uint8_t *packet_base;
    uint32_t node_count = 0U;
    int begin_status;

    if((buffer_index >= XE_NP_USB_STREAM_TX_BUFFER_COUNT) ||
       (xe_np_usb_interleaved_staging_dma.busy != 0U) ||
       (XE_NP_UsbStream_InitInterleavedStagingDma() != 0))
    {
        return -1;
    }
    /* Bind the short source claim, not descriptor/cache preparation. */
    uint32_t claim_mask=__get_PRIMASK();__disable_irq();
    if(!xe_np_usb_state.streaming || generation!=xe_np_usb_disarm_count){
        __set_PRIMASK(claim_mask);return 0;
    }
    begin_status = XE_NP_App_BeginUsbQuadDmaBatch(
        NP_USB20_PAIRED()?NP_DIRECT_GROUPS*252U:NP_DIRECT_LANE_BYTES,
        &batch);
    __set_PRIMASK(claim_mask);
    if(!xe_np_usb_state.streaming || generation!=xe_np_usb_disarm_count)return 0;
    if(begin_status <= 0)
    {
        return begin_status;
    }
    packet_base = xe_np_usb_tx_packet[buffer_index];
    /* GPDMA completion and USB IN completion are independent IRQ domains.
     * Reserve compact packet sequence numbers when the batch is claimed,
     * rather than sampling the USB-completion counter while its prior batch
     * may still be in flight. */
    if(xe_np_usb_interleaved_staging_dma.quad_sequence_valid == 0U)
    {
        xe_np_usb_interleaved_staging_dma.quad_next_sequence =
            (uint16_t)xe_np_usb_state.sequence;
        xe_np_usb_interleaved_staging_dma.quad_sequence_valid = 1U;
    }
    xe_np_usb_interleaved_staging_dma.quad_sequence_base =
        xe_np_usb_interleaved_staging_dma.quad_next_sequence;
    xe_np_usb_interleaved_staging_dma.quad_next_sequence = (uint16_t)(
        xe_np_usb_interleaved_staging_dma.quad_next_sequence +
        XE_NP_USB_STREAM_QUAD_STAGING_PACKETS_PER_BATCH);
    for(uint32_t packet = 0U; packet < XE_NP_USB_STREAM_QUAD_STAGING_PACKETS_PER_BATCH;
        ++packet)
    {
        XE_NP_UsbStream_WritePacketHeader(
            &packet_base[packet * XE_NP_USB_STREAM_PACKET_BYTES],
            XE_NP_USB_STREAM_PAYLOAD_BYTES,
            (uint16_t)(xe_np_usb_interleaved_staging_dma.quad_sequence_base + packet),
            NULL, 0U, 0U, 0U, 0U);
        packet_base[packet*XE_NP_USB_STREAM_PACKET_BYTES+2]=0x76; /* experimental triple, NEVER legacy NP */
        packet_base[packet*XE_NP_USB_STREAM_PACKET_BYTES+3]=(uint8_t)(0x60U|NP_Triple_Probe()|((packet&3U)<<2));
        if(NP_USB20_PAIRED()) {
            uint8_t *h=packet_base+packet*XE_NP_USB_STREAM_PACKET_BYTES;
            h[2]=0x77U;h[3]=(uint8_t)(packet&3U);h[6]=3U;h[7]=1U;
        }
    }
    /* Preserve header bytes before the staging DMA modifies their cache lines. */
    XE_NP_UsbStream_DCacheCleanTx(
        packet_base,
        XE_NP_USB_STREAM_QUAD_STAGING_PACKETS_PER_BATCH * XE_NP_USB_STREAM_PACKET_BYTES);
    XE_NP_Capture_DCacheInvalidate(
        packet_base,
        XE_NP_USB_STREAM_QUAD_STAGING_PACKETS_PER_BATCH * XE_NP_USB_STREAM_PACKET_BYTES);

    if(HAL_DMAEx_List_ResetQ(&xe_np_usb_interleaved_staging_dma.quad_queue) != HAL_OK)
    {
        XE_NP_App_CancelUsbQuadDmaBatch();
        return -1;
    }
    memset(xe_np_usb_interleaved_staging_dma.quad_nodes,0,sizeof(xe_np_usb_interleaved_staging_dma.quad_nodes));
    uint32_t planned_nodes = 0U;
    _Static_assert(XE_NP_USB_STREAM_QUAD_STAGING_MAX_NODES >= NP_DIRECT_MAX_NODES, "Direct scatter node capacity");
    _Static_assert(XE_NP_USB_STREAM_QUAD_STAGING_GROUPS_PER_BATCH == NP_DIRECT_GROUPS, "Direct scatter geometry");
    _Static_assert(XE_NP_USB_WORD_LANE_BYTES == NP_DIRECT_LANE_BYTES, "Legacy direct claim geometry");
    if(NpDirectVisitFormat(batch.source, batch.source_bytes, batch.source_segment_count,
          packet_base, XE_NP_USB_STREAM_TRANSFER_BYTES, NP_DirectEmit, &node_count, &planned_nodes, NP_USB20_PAIRED()?2U:1U, NP_USB20_PAIRED()) != 0 ||
       planned_nodes != node_count || !NpDirectLinkStatic(
          xe_np_usb_interleaved_staging_dma.quad_nodes, node_count,
          (uint32_t)xe_np_usb_interleaved_staging_dma.quad_nodes) || !NpDirectChainValid(xe_np_usb_interleaved_staging_dma.quad_nodes,
          node_count, (uint32_t)xe_np_usb_interleaved_staging_dma.quad_nodes))
    { XE_NP_App_CancelUsbQuadDmaBatch(); return -1; }
    _Static_assert(sizeof(DMA_NodeTypeDef)==36U, "Static node ABI");
    _Static_assert((DMA_GPDMA_LINEAR_NODE | (NODE_CLLR_LINEAR_DEFAULT_OFFSET << NODE_CLLR_IDX_POS))==0x521U, "Static node format");
    DMA_QListTypeDef *direct_queue=&xe_np_usb_interleaved_staging_dma.quad_queue;
    direct_queue->Head=xe_np_usb_interleaved_staging_dma.quad_nodes;
    direct_queue->NodeNumber=node_count;
    direct_queue->FirstCircularNode=NULL;
    direct_queue->ErrorCode=HAL_DMA_QUEUE_ERROR_NONE;
    direct_queue->Type=QUEUE_TYPE_STATIC;
    direct_queue->State=HAL_DMA_QUEUE_STATE_READY;
    if((node_count == 0U) ||
       (HAL_DMAEx_List_LinkQ(
            &xe_np_usb_interleaved_staging_dma.hdma,
            &xe_np_usb_interleaved_staging_dma.quad_queue) != HAL_OK))
    {
        XE_NP_App_CancelUsbQuadDmaBatch();
        return -1;
    }
    XE_NP_UsbStream_DCacheCleanTx(
        xe_np_usb_interleaved_staging_dma.quad_nodes,
        node_count * sizeof(xe_np_usb_interleaved_staging_dma.quad_nodes[0]));
    XE_NP_UsbStream_DCacheCleanTx(
        &xe_np_usb_interleaved_staging_dma.quad_queue,
        sizeof(xe_np_usb_interleaved_staging_dma.quad_queue));
    uint32_t mask = __get_PRIMASK();
    __disable_irq();
    if(!xe_np_usb_state.streaming || generation != xe_np_usb_disarm_count)
    {
        __set_PRIMASK(mask);
        return 0; /* Disarm already cancelled the old claim. */
    }
    xe_np_usb_interleaved_staging_dma.quad_batch = batch;
    xe_np_usb_interleaved_staging_dma.buffer_index = buffer_index;
    xe_np_usb_interleaved_staging_dma.packet_start_index = 0U;
    xe_np_usb_interleaved_staging_dma.packet_count =
        XE_NP_USB_STREAM_QUAD_STAGING_PACKETS_PER_BATCH;
    xe_np_usb_interleaved_staging_dma.quad_mode = 1U;
    xe_np_usb_interleaved_staging_dma.error = 0U;
    xe_np_usb_interleaved_staging_dma.busy = 1U;
#if (XE_NP_APP_QUAD_USB_WORD_EXPORT != 0U)
    xe_np_usb_quad_word_started_ms = HAL_GetTick();
#endif
    if(HAL_DMAEx_List_Start_IT(&xe_np_usb_interleaved_staging_dma.hdma) != HAL_OK)
    {
        XE_NP_UsbStream_StopInterleavedStagingDma();
        __set_PRIMASK(mask);
        return -1;
    }
    __set_PRIMASK(mask);
    return 1;
#else
    (void)buffer_index;(void)generation;
    return -1;
#endif
}
static int XE_NP_UsbStream_StartQuadStagingBatchOwned(uint8_t buffer_index,uint32_t generation)
{
 NpUsbBlockStamp block=NP_UsbBlockBegin(NP_UB_BUILDER,0U,0U,0U);
 int result=XE_NP_UsbStream_StartQuadStagingBatchOwned_BlockBody(buffer_index,generation);
 NP_UsbBlockEnd(block,(uint32_t)result,0U);
 return result;
}
#else
static int XE_NP_UsbStream_StartQuadStagingBatchOwned(uint8_t buffer_index,uint32_t generation)
{
#if (XE_NP_USB_STREAM_QUAD_STAGING_DMA_ENABLE != 0U)
    /* NP_USB_STAGE_LAUNCH_GUARD: preparation stays interruptible. STOP/reset
     * may cancel the claim meanwhile; never arm a stale descriptor chain. */
    if(!xe_np_usb_state.streaming || generation!=xe_np_usb_disarm_count)return 0;
    XE_NP_AppUsbQuadDmaBatch batch;
    uint8_t *packet_base;
    uint32_t node_count = 0U;
    int begin_status;

    if((buffer_index >= XE_NP_USB_STREAM_TX_BUFFER_COUNT) ||
       (xe_np_usb_interleaved_staging_dma.busy != 0U) ||
       (XE_NP_UsbStream_InitInterleavedStagingDma() != 0))
    {
        return -1;
    }
    /* Bind the short source claim, not descriptor/cache preparation. */
    uint32_t claim_mask=__get_PRIMASK();__disable_irq();
    if(!xe_np_usb_state.streaming || generation!=xe_np_usb_disarm_count){
        __set_PRIMASK(claim_mask);return 0;
    }
    begin_status = XE_NP_App_BeginUsbQuadDmaBatch(
        NP_USB20_PAIRED()?NP_DIRECT_GROUPS*252U:NP_DIRECT_LANE_BYTES,
        &batch);
    __set_PRIMASK(claim_mask);
    if(!xe_np_usb_state.streaming || generation!=xe_np_usb_disarm_count)return 0;
    if(begin_status <= 0)
    {
        return begin_status;
    }
    packet_base = xe_np_usb_tx_packet[buffer_index];
    /* GPDMA completion and USB IN completion are independent IRQ domains.
     * Reserve compact packet sequence numbers when the batch is claimed,
     * rather than sampling the USB-completion counter while its prior batch
     * may still be in flight. */
    if(xe_np_usb_interleaved_staging_dma.quad_sequence_valid == 0U)
    {
        xe_np_usb_interleaved_staging_dma.quad_next_sequence =
            (uint16_t)xe_np_usb_state.sequence;
        xe_np_usb_interleaved_staging_dma.quad_sequence_valid = 1U;
    }
    xe_np_usb_interleaved_staging_dma.quad_sequence_base =
        xe_np_usb_interleaved_staging_dma.quad_next_sequence;
    xe_np_usb_interleaved_staging_dma.quad_next_sequence = (uint16_t)(
        xe_np_usb_interleaved_staging_dma.quad_next_sequence +
        XE_NP_USB_STREAM_QUAD_STAGING_PACKETS_PER_BATCH);
    for(uint32_t packet = 0U; packet < XE_NP_USB_STREAM_QUAD_STAGING_PACKETS_PER_BATCH;
        ++packet)
    {
        XE_NP_UsbStream_WritePacketHeader(
            &packet_base[packet * XE_NP_USB_STREAM_PACKET_BYTES],
            XE_NP_USB_STREAM_PAYLOAD_BYTES,
            (uint16_t)(xe_np_usb_interleaved_staging_dma.quad_sequence_base + packet),
            NULL, 0U, 0U, 0U, 0U);
        packet_base[packet*XE_NP_USB_STREAM_PACKET_BYTES+2]=0x76; /* experimental triple, NEVER legacy NP */
        packet_base[packet*XE_NP_USB_STREAM_PACKET_BYTES+3]=(uint8_t)(0x60U|NP_Triple_Probe()|((packet&3U)<<2));
        if(NP_USB20_PAIRED()) {
            uint8_t *h=packet_base+packet*XE_NP_USB_STREAM_PACKET_BYTES;
            h[2]=0x77U;h[3]=(uint8_t)(packet&3U);h[6]=3U;h[7]=1U;
        }
    }
    /* Preserve header bytes before the staging DMA modifies their cache lines. */
    XE_NP_UsbStream_DCacheCleanTx(
        packet_base,
        XE_NP_USB_STREAM_QUAD_STAGING_PACKETS_PER_BATCH * XE_NP_USB_STREAM_PACKET_BYTES);
    XE_NP_Capture_DCacheInvalidate(
        packet_base,
        XE_NP_USB_STREAM_QUAD_STAGING_PACKETS_PER_BATCH * XE_NP_USB_STREAM_PACKET_BYTES);

    if(HAL_DMAEx_List_ResetQ(&xe_np_usb_interleaved_staging_dma.quad_queue) != HAL_OK)
    {
        XE_NP_App_CancelUsbQuadDmaBatch();
        return -1;
    }
    memset(xe_np_usb_interleaved_staging_dma.quad_nodes,0,sizeof(xe_np_usb_interleaved_staging_dma.quad_nodes));
    uint32_t planned_nodes = 0U;
    _Static_assert(XE_NP_USB_STREAM_QUAD_STAGING_MAX_NODES >= NP_DIRECT_MAX_NODES, "Direct scatter node capacity");
    _Static_assert(XE_NP_USB_STREAM_QUAD_STAGING_GROUPS_PER_BATCH == NP_DIRECT_GROUPS, "Direct scatter geometry");
    _Static_assert(XE_NP_USB_WORD_LANE_BYTES == NP_DIRECT_LANE_BYTES, "Legacy direct claim geometry");
    if(NpDirectVisitFormat(batch.source, batch.source_bytes, batch.source_segment_count,
          packet_base, XE_NP_USB_STREAM_TRANSFER_BYTES, NP_DirectEmit, &node_count, &planned_nodes, NP_USB20_PAIRED()?2U:1U, NP_USB20_PAIRED()) != 0 ||
       planned_nodes != node_count || !NpDirectLinkStatic(
          xe_np_usb_interleaved_staging_dma.quad_nodes, node_count,
          (uint32_t)xe_np_usb_interleaved_staging_dma.quad_nodes) || !NpDirectChainValid(xe_np_usb_interleaved_staging_dma.quad_nodes,
          node_count, (uint32_t)xe_np_usb_interleaved_staging_dma.quad_nodes))
    { XE_NP_App_CancelUsbQuadDmaBatch(); return -1; }
    _Static_assert(sizeof(DMA_NodeTypeDef)==36U, "Static node ABI");
    _Static_assert((DMA_GPDMA_LINEAR_NODE | (NODE_CLLR_LINEAR_DEFAULT_OFFSET << NODE_CLLR_IDX_POS))==0x521U, "Static node format");
    DMA_QListTypeDef *direct_queue=&xe_np_usb_interleaved_staging_dma.quad_queue;
    direct_queue->Head=xe_np_usb_interleaved_staging_dma.quad_nodes;
    direct_queue->NodeNumber=node_count;
    direct_queue->FirstCircularNode=NULL;
    direct_queue->ErrorCode=HAL_DMA_QUEUE_ERROR_NONE;
    direct_queue->Type=QUEUE_TYPE_STATIC;
    direct_queue->State=HAL_DMA_QUEUE_STATE_READY;
    if((node_count == 0U) ||
       (HAL_DMAEx_List_LinkQ(
            &xe_np_usb_interleaved_staging_dma.hdma,
            &xe_np_usb_interleaved_staging_dma.quad_queue) != HAL_OK))
    {
        XE_NP_App_CancelUsbQuadDmaBatch();
        return -1;
    }
    XE_NP_UsbStream_DCacheCleanTx(
        xe_np_usb_interleaved_staging_dma.quad_nodes,
        node_count * sizeof(xe_np_usb_interleaved_staging_dma.quad_nodes[0]));
    XE_NP_UsbStream_DCacheCleanTx(
        &xe_np_usb_interleaved_staging_dma.quad_queue,
        sizeof(xe_np_usb_interleaved_staging_dma.quad_queue));
    uint32_t mask = __get_PRIMASK();
    __disable_irq();
    if(!xe_np_usb_state.streaming || generation != xe_np_usb_disarm_count)
    {
        __set_PRIMASK(mask);
        return 0; /* Disarm already cancelled the old claim. */
    }
    xe_np_usb_interleaved_staging_dma.quad_batch = batch;
    xe_np_usb_interleaved_staging_dma.buffer_index = buffer_index;
    xe_np_usb_interleaved_staging_dma.packet_start_index = 0U;
    xe_np_usb_interleaved_staging_dma.packet_count =
        XE_NP_USB_STREAM_QUAD_STAGING_PACKETS_PER_BATCH;
    xe_np_usb_interleaved_staging_dma.quad_mode = 1U;
    xe_np_usb_interleaved_staging_dma.error = 0U;
    xe_np_usb_interleaved_staging_dma.busy = 1U;
#if (XE_NP_APP_QUAD_USB_WORD_EXPORT != 0U)
    xe_np_usb_quad_word_started_ms = HAL_GetTick();
#endif
    if(HAL_DMAEx_List_Start_IT(&xe_np_usb_interleaved_staging_dma.hdma) != HAL_OK)
    {
        XE_NP_UsbStream_StopInterleavedStagingDma();
        __set_PRIMASK(mask);
        return -1;
    }
    __set_PRIMASK(mask);
    return 1;
#else
    (void)buffer_index;(void)generation;
    return -1;
#endif
}
#endif
static int XE_NP_UsbStream_StartQuadStagingBatch(uint8_t buffer_index)
{
    return XE_NP_UsbStream_StartQuadStagingBatchOwned(buffer_index,xe_np_usb_disarm_count);
}


static int XE_NP_UsbStream_StartInterleavedStagingBatch(
    uint8_t buffer_index,
    uint8_t packet_start_index,
    const XE_NP_AppUsbInterleavedDmaBatch *batch)
{
#if (XE_NP_USB_STREAM_INTERLEAVED_STAGING_DMA_ENABLE != 0U)
    /* NP_USB_STAGE_LAUNCH_GUARD: preparation stays interruptible. STOP/reset
     * may cancel the claim meanwhile; never arm a stale descriptor chain. */
    const uint32_t generation = xe_np_usb_disarm_count;
    if(!xe_np_usb_state.streaming)return 0;
    uint8_t *packet_base;

    if((batch == NULL) || (batch->packet_count == 0U) ||
       ((uint32_t)packet_start_index + batch->packet_count > XE_NP_USB_STREAM_PACKETS_PER_TRANSFER) ||
       (xe_np_usb_interleaved_staging_dma.busy != 0U) ||
       (XE_NP_UsbStream_InitInterleavedStagingDma() != 0))
    {
        return -1;
    }
    packet_base = &xe_np_usb_tx_packet[buffer_index]
        [(uint32_t)packet_start_index * XE_NP_USB_STREAM_PACKET_BYTES];
    for(uint32_t i = 0U; i < batch->packet_count; ++i)
    {
        uint8_t lane_b = (uint8_t)(batch->starts_with_lane_b ^ (i & 1U));
        XE_NP_UsbStream_WritePacketHeader(
            &packet_base[i * XE_NP_USB_STREAM_PACKET_BYTES],
            batch->payload_bytes_per_packet,
            (uint16_t)(xe_np_usb_state.sequence + packet_start_index + i),
            NULL, 0U, 0U, 0U, 0U);
        packet_base[(i * XE_NP_USB_STREAM_PACKET_BYTES) + 3U] |=
            XE_NP_USB_STREAM_FLAG_INTERLEAVED_LANES;
        if(lane_b != 0U)
        {
            packet_base[(i * XE_NP_USB_STREAM_PACKET_BYTES) + 3U] |=
                XE_NP_USB_STREAM_FLAG_INTERLEAVED_LANE_B;
        }
    }
    /* Flush header writes then evict this private destination before GPDMA
     * populates payload bytes.  DMA never writes a cache line that remains
     * dirty in the CPU cache. */
    XE_NP_UsbStream_DCacheCleanTx(
        packet_base,
        batch->packet_count * XE_NP_USB_STREAM_PACKET_BYTES);
    XE_NP_Capture_DCacheInvalidate(
        packet_base,
        batch->packet_count * XE_NP_USB_STREAM_PACKET_BYTES);

    uint32_t mask = __get_PRIMASK();
    __disable_irq();
    if(!xe_np_usb_state.streaming || generation != xe_np_usb_disarm_count)
    {
        __set_PRIMASK(mask);
        return 0; /* Disarm already cancelled the old claim. */
    }
    xe_np_usb_interleaved_staging_dma.batch = *batch;
    xe_np_usb_interleaved_staging_dma.buffer_index = buffer_index;
    xe_np_usb_interleaved_staging_dma.packet_start_index = packet_start_index;
    xe_np_usb_interleaved_staging_dma.error = 0U;
    xe_np_usb_interleaved_staging_dma.busy = 1U;
    if(batch->lane_a_packet_count != 0U)
    {
        if(XE_NP_UsbStream_StartInterleavedStagingLane(0U) != HAL_OK)
        {
            XE_NP_UsbStream_StopInterleavedStagingDma();
            __set_PRIMASK(mask);
            return -1;
        }
    }
    else if(XE_NP_UsbStream_StartInterleavedStagingLane(1U) != HAL_OK)
    {
        XE_NP_UsbStream_StopInterleavedStagingDma();
        __set_PRIMASK(mask);
        return -1;
    }
    __set_PRIMASK(mask);
    return 0;
#else
    (void)buffer_index;
    (void)packet_start_index;
    (void)batch;
    return -1;
#endif
}

static void NP_Triple_StagingCompleteLocked(DMA_HandleTypeDef *hdma);
static void NP_DirectCompleteLocked(DMA_HandleTypeDef *hdma);
static void XE_NP_UsbStream_InterleavedStagingComplete(DMA_HandleTypeDef *hdma)
{
    uint32_t mask = __get_PRIMASK(); __disable_irq();
    NP_DirectCompleteLocked(hdma);
    __set_PRIMASK(mask);
}
static void NP_DirectCompleteLocked(DMA_HandleTypeDef *hdma)
{
    /* USB STOP can preempt this lower-priority DMA IRQ. Keep the completion
     * ownership check and commit indivisible against cancel/reset. No payload
     * copying is done here, and capture itself is interrupt-free hardware DMA. */
    uint32_t mask=__get_PRIMASK();__disable_irq();
    NP_Triple_StagingCompleteLocked(hdma);
    __set_PRIMASK(mask);
}
static void NP_Triple_StagingCompleteLocked(DMA_HandleTypeDef *hdma)
{
#if ((XE_NP_USB_STREAM_INTERLEAVED_STAGING_DMA_ENABLE != 0U) || \
     (XE_NP_USB_STREAM_QUAD_STAGING_DMA_ENABLE != 0U))
    (void)hdma;
    if(xe_np_usb_interleaved_staging_dma.busy == 0U)
    {
        return;
    }
    if(xe_np_usb_interleaved_staging_dma.quad_mode != 0U)
    {
#if (XE_NP_APP_QUAD_USB_WORD_EXPORT != 0U)
        if(!(hdma->Instance->CSR & DMA_CSR_IDLEF) ||
           (hdma->Instance->CBR1 & DMA_CBR1_BNDT) || hdma->Instance->CLLR)
        {
            xe_np_usb_interleaved_staging_dma.error = 1U;
            xe_np_usb_interleaved_staging_dma.busy = 0U;
            XE_NP_App_CancelUsbQuadDmaBatch();
            return;
        }
#endif
        if(XE_NP_App_CommitUsbQuadDmaBatch(
               &xe_np_usb_interleaved_staging_dma.quad_batch) != 0)
        {
            xe_np_usb_interleaved_staging_dma.error = 1U;
        }
        else
        {
#if (XE_NP_APP_QUAD_USB_WORD_EXPORT != 0U)
            xe_np_usb_quad_word_ready = 1U;
#else
            xe_np_usb_state.packet_assembly_bytes = (uint16_t)(
                xe_np_usb_state.packet_assembly_bytes +
                ((uint32_t)xe_np_usb_interleaved_staging_dma.packet_count *
                 XE_NP_USB_STREAM_PAYLOAD_BYTES));
#endif
        }
        xe_np_usb_interleaved_staging_dma.busy = 0U;
        return;
    }
    if((xe_np_usb_interleaved_staging_dma.phase_lane_b == 0U) &&
       (xe_np_usb_interleaved_staging_dma.batch.lane_b_packet_count != 0U))
    {
        if(XE_NP_UsbStream_StartInterleavedStagingLane(1U) != HAL_OK)
        {
            xe_np_usb_interleaved_staging_dma.error = 1U;
            xe_np_usb_interleaved_staging_dma.busy = 0U;
            XE_NP_App_CancelUsbInterleavedDmaBatch();
        }
        return;
    }
    if(XE_NP_App_CommitUsbInterleavedDmaBatch(
           &xe_np_usb_interleaved_staging_dma.batch) != 0)
    {
        xe_np_usb_interleaved_staging_dma.error = 1U;
    }
    else
    {
        xe_np_usb_state.packet_assembly_bytes = (uint16_t)(
            xe_np_usb_state.packet_assembly_bytes +
            (xe_np_usb_interleaved_staging_dma.batch.packet_count *
             xe_np_usb_interleaved_staging_dma.batch.payload_bytes_per_packet));
    }
    xe_np_usb_interleaved_staging_dma.busy = 0U;
#else
    (void)hdma;
#endif
}

static void XE_NP_UsbStream_InterleavedStagingError(DMA_HandleTypeDef *hdma)
{
    (void)hdma;
    xe_np_usb_interleaved_staging_dma.error = 1U;
    xe_np_usb_interleaved_staging_dma.busy = 0U;
    if(xe_np_usb_interleaved_staging_dma.quad_mode != 0U)
    {
        XE_NP_App_CancelUsbQuadDmaBatch();
    }
    else
    {
        XE_NP_App_CancelUsbInterleavedDmaBatch();
    }
}

void XE_NP_UsbStream_StagingDmaIrqHandler(void)
{
#if ((XE_NP_USB_STREAM_INTERLEAVED_STAGING_DMA_ENABLE != 0U) || \
     (XE_NP_USB_STREAM_QUAD_STAGING_DMA_ENABLE != 0U))
    if(xe_np_usb_interleaved_staging_dma.initialized != 0U)
    {
        HAL_DMA_IRQHandler(&xe_np_usb_interleaved_staging_dma.hdma);
    }
#endif
}

static uint8_t XE_NP_UsbStream_SourceInPsram(const void *addr, uint32_t size_bytes)
{
    uintptr_t start_addr;
    uintptr_t end_addr;

    if((addr == NULL) || (size_bytes == 0U))
    {
        return 0U;
    }

    start_addr = (uintptr_t)addr;
    end_addr = start_addr + (uintptr_t)size_bytes;
    if(end_addr < start_addr)
    {
        return 0U;
    }

    return ((start_addr >= (uintptr_t)XE_NP_PSRAM_BASE_ADDR) &&
            (end_addr <= ((uintptr_t)XE_NP_PSRAM_BASE_ADDR + (uintptr_t)XE_NP_PSRAM_SIZE_BYTES))) ? 1U : 0U;
}

static uint8_t XE_NP_UsbStream_ShouldPackNp2Lane8(void)
{
    return ((xe_np_usb_state.port_index == 0U) &&
            (xe_np_usb_state.port != NULL) &&
            ((xe_np_usb_state.port->active_probe_mask & XE_NP_CAPTURE_ALL_PROBES_MASK) ==
            XE_NP_CAPTURE_PROBE2_MASK) &&
            (xe_np_usb_state.port->bytes_per_tick == sizeof(uint16_t))) ? 1U : 0U;
}

static uint8_t XE_NP_UsbStream_IsNp2Lane8Payload(void)
{
    return ((xe_np_usb_state.port_index == 0U) &&
            (xe_np_usb_state.port != NULL) &&
            ((xe_np_usb_state.port->active_probe_mask & XE_NP_CAPTURE_ALL_PROBES_MASK) ==
             XE_NP_CAPTURE_PROBE2_MASK) &&
            ((xe_np_usb_state.port->bytes_per_tick == sizeof(uint8_t)) ||
             (xe_np_usb_state.port->bytes_per_tick == sizeof(uint16_t)))) ? 1U : 0U;
}

static void XE_NP_UsbStream_UpdateDiag(void)
{
    XE_NP_AppUsbExportDiag app_usb_diag;
    /* EP0 START/STOP can clear state.port while this foreground snapshot runs.
     * Capture-port objects have static lifetime; retain the checked pointer. */
    XE_NP_CapturePort *const port = xe_np_usb_state.port;
    uint32_t flags = 0U;
    uint32_t available = 0U;
    uint32_t log_state = 0U;

    memset(&app_usb_diag, 0, sizeof(app_usb_diag));

    if(xe_np_usb_state.streaming != 0U)
    {
        flags |= XE_NP_USB_FLAG_STREAMING;
    }
    if(xe_np_usb_state.synthetic_source != 0U)
    {
        flags |= XE_NP_USB_FLAG_SYNTHETIC;
    }
    if(xe_np_usb_state.real_start_pending != 0U)
    {
        flags |= XE_NP_USB_FLAG_START_PENDING;
    }
    if(xe_np_usb_state.dropped_bytes != 0U)
    {
        flags |= XE_NP_USB_FLAG_DROPPED;
    }
    if(port != NULL)
    {
        if(xe_np_usb_state.synthetic_source == 0U)
        {
            available = XE_NP_App_GetUsbWindowPendingBytes(xe_np_usb_state.port_index);
        }
        log_state = port->raw_mgr.logState;
        if((log_state & MGR_STATE_TRANSOVR) != 0U)
        {
            flags |= XE_NP_USB_FLAG_MGR_OVERRUN;
        }
        if(port->storage != NULL)
        {
            flags |= XE_NP_USB_FLAG_SOURCE_ACTIVE;
        }
        if(XE_NP_UsbStream_ShouldPackNp2Lane8() != 0U)
        {
            flags |= XE_NP_USB_FLAG_PACKED_NP2_LANE8;
        }
    }

    xe_np_usb_stream_diag.magic = XE_NP_USB_STREAM_MAGIC_DIAG;
    xe_np_usb_stream_diag.version = XE_NP_USB_STREAM_DIAG_VERSION;
    xe_np_usb_stream_diag.tick_ms = HAL_GetTick();
    xe_np_usb_stream_diag.flags = flags;
    xe_np_usb_stream_diag.configured = xe_np_usb_state.configured;
    xe_np_usb_stream_diag.streaming = xe_np_usb_state.streaming;
    xe_np_usb_stream_diag.port_index = xe_np_usb_state.port_index;
    xe_np_usb_stream_diag.tx_busy =
        xe_np_usb_state.tx_busy | (uint8_t)(xe_np_usb_state.tx_pending << 1U);
    xe_np_usb_stream_diag.sequence = xe_np_usb_state.sequence;
    xe_np_usb_stream_diag.packets_sent = xe_np_usb_state.packets_sent;
    xe_np_usb_stream_diag.payload_bytes_low = (uint32_t)xe_np_usb_state.payload_bytes_sent;
    xe_np_usb_stream_diag.dropped_bytes_low = xe_np_usb_state.dropped_bytes;
    xe_np_usb_stream_diag.mgr_available_bytes = available;
    xe_np_usb_stream_diag.mgr_log_state = log_state;
    xe_np_usb_stream_diag.pcd_state =
        (xe_np_usb_state.hpcd != NULL) ? (uint32_t)xe_np_usb_state.hpcd->State : 0U;
    xe_np_usb_stream_diag.pcd_error =
        (xe_np_usb_state.hpcd != NULL) ? xe_np_usb_state.hpcd->ErrorCode : 0U;
    xe_np_usb_stream_diag.negotiated_speed =
        (xe_np_usb_state.hpcd != NULL) ? (uint32_t)xe_np_usb_state.hpcd->Init.speed : 0xFFFFFFFFUL;
    xe_np_usb_stream_diag.ep0_flags =
        ((uint32_t)xe_np_usb_state.ep0_status_out_pending) |
        ((uint32_t)xe_np_usb_state.ep0_address_pending << 1U) |
        ((uint32_t)xe_np_usb_state.ep0_pending_address << 8U);
    xe_np_usb_stream_diag.lane_validator_frames_seen =
        xe_np_usb_lane_validator.validator.frames_seen;
    xe_np_usb_stream_diag.lane_validator_first_error_flags =
        xe_np_usb_lane_validator.first_error_flags;
    xe_np_usb_stream_diag.lane_validator_first_error_packet =
        xe_np_usb_lane_validator.first_error_packet_sequence;
    xe_np_usb_stream_diag.lane_validator_first_error_payload_offset =
        xe_np_usb_lane_validator.first_error_payload_offset;
    xe_np_usb_stream_diag.lane_validator_first_error_stream_offset_low =
        xe_np_usb_lane_validator.first_error_stream_offset_low;
    xe_np_usb_stream_diag.lane_validator_lock_offset =
        xe_np_usb_lane_validator.lock_offset;
    xe_np_usb_stream_diag.lane_validator_lock_clean_frames =
        xe_np_usb_lane_validator.lock_clean_frames;
    xe_np_usb_stream_diag.lane_validator_packets_checked =
        xe_np_usb_lane_validator.packets_checked;
    xe_np_usb_stream_diag.lane_validator_tail_len =
        xe_np_usb_lane_validator.tail_len;
    xe_np_usb_stream_diag.lane_validator_status =
        ((uint32_t)xe_np_usb_lane_validator.locked) |
        ((uint32_t)xe_np_usb_lane_validator.disabled << 1U) |
        ((uint32_t)xe_np_usb_lane_validator.validator.sync_errors << 8U) |
        ((uint32_t)xe_np_usb_lane_validator.validator.heartbeat_errors << 16U) |
        ((uint32_t)xe_np_usb_lane_validator.validator.fill_errors << 24U);
    xe_np_usb_stream_diag.seam_probe_status =
        ((uint32_t)xe_np_usb_seam_probe.active) |
        ((uint32_t)xe_np_usb_seam_probe.compared << 1U) |
        ((uint32_t)xe_np_usb_seam_probe.valid << 2U);
    xe_np_usb_stream_diag.seam_probe_packet_sequence =
        xe_np_usb_seam_probe.packet_sequence;
    xe_np_usb_stream_diag.seam_probe_compare_after_packets_sent =
        xe_np_usb_seam_probe.compare_after_packets_sent;
    xe_np_usb_stream_diag.seam_probe_ring_offset =
        xe_np_usb_seam_probe.ring_offset;
    xe_np_usb_stream_diag.seam_probe_payload_offset =
        xe_np_usb_seam_probe.payload_offset;
    xe_np_usb_stream_diag.seam_probe_tail_offset =
        xe_np_usb_seam_probe.tail_offset;
    xe_np_usb_stream_diag.seam_probe_payload_bytes =
        xe_np_usb_seam_probe.payload_bytes;
    xe_np_usb_stream_diag.seam_probe_before_hash =
        xe_np_usb_seam_probe.before_hash;
    xe_np_usb_stream_diag.seam_probe_after_hash =
        xe_np_usb_seam_probe.after_hash;
    xe_np_usb_stream_diag.seam_probe_mismatch_count =
        xe_np_usb_seam_probe.mismatch_count;
    xe_np_usb_stream_diag.start_request_count = xe_np_usb_start_request_count;
    xe_np_usb_stream_diag.start_try_count = xe_np_usb_start_try_count;
    xe_np_usb_stream_diag.start_try_fail_count = xe_np_usb_start_try_fail_count;
    xe_np_usb_stream_diag.last_start_try_status = xe_np_usb_last_start_try_status;
    xe_np_usb_stream_diag.disarm_count = xe_np_usb_disarm_count;
    xe_np_usb_stream_diag.last_disarm_reason = xe_np_usb_last_disarm_reason;
    xe_np_usb_stream_diag.real_start_pending = xe_np_usb_state.real_start_pending;
    xe_np_usb_stream_diag.real_start_port_index = xe_np_usb_state.real_start_port_index;
    if(NP_USB_IsReady())XE_NP_App_GetUsbExportDiag(xe_np_usb_state.port_index, &app_usb_diag);
    xe_np_usb_stream_diag.app_capture_requested = app_usb_diag.capture_requested;
    xe_np_usb_stream_diag.app_capture_port_index = app_usb_diag.capture_port_index;
    xe_np_usb_stream_diag.app_export_enabled = app_usb_diag.export_enabled;
    xe_np_usb_stream_diag.app_export_port_index = app_usb_diag.export_port_index;
    xe_np_usb_stream_diag.app_port_active = app_usb_diag.port_active;
    xe_np_usb_stream_diag.app_pending_read_idx = app_usb_diag.pending_read_idx;
    xe_np_usb_stream_diag.app_pending_write_idx = app_usb_diag.pending_write_idx;
    xe_np_usb_stream_diag.app_usb_active_window_valid = app_usb_diag.usb_active_window_valid;
    xe_np_usb_stream_diag.app_stable_window_valid = app_usb_diag.stable_window_valid;
    xe_np_usb_stream_diag.app_active_probe_mask = app_usb_diag.active_probe_mask;
    xe_np_usb_stream_diag.mco_divider = XE_NP_Board_GetRuntimeMcoDivider();
    xe_np_usb_stream_diag.expected_mclk_hz = XE_NP_Board_GetRuntimeExpectedMcoHz();
    xe_np_usb_stream_diag.expected_psb_clk_hz = XE_NP_Board_GetRuntimeExpectedPsbClkHz();
    xe_np_usb_stream_diag.mco_apply_count = XE_NP_Board_GetRuntimeMcoApplyCount();
    xe_np_usb_stream_diag.mco_last_apply_status = XE_NP_Board_GetRuntimeMcoLastApplyStatus();
#if (XE_NP_APP_QUAD_USB_WORD_EXPORT != 0U)
    _Static_assert(offsetof(XE_NP_UsbStreamDiag, v1_recovery_magic) == 304U, "VSR1 wire offset changed");
    xe_np_usb_stream_diag.v1_recovery_magic = 0x31525356UL;
    xe_np_usb_stream_diag.v1_recovery_capabilities = 1U; /* new START resets session */
    xe_np_usb_stream_diag.v1_session_id = xe_np_usb_v1_session_id;
    xe_np_usb_stream_diag.v1_previous_fault = xe_np_usb_v1_previous_fault;
    xe_np_usb_stream_diag.v1_session_reset_count = xe_np_usb_v1_reset_count;
    xe_np_usb_stream_diag.v1_latched_fault = xe_np_usb_v1_latched_fault;
#endif
    if(xe_np_usb_state.hpcd != NULL)
    {
        USB_OTG_DeviceTypeDef *device = (USB_OTG_DeviceTypeDef *)(void *)
            ((uintptr_t)xe_np_usb_state.hpcd->Instance + USB_OTG_DEVICE_BASE);
        USB_OTG_INEndpointTypeDef *in_ep = (USB_OTG_INEndpointTypeDef *)(void *)
            ((uintptr_t)xe_np_usb_state.hpcd->Instance + USB_OTG_IN_ENDPOINT_BASE +
             USB_OTG_EP_REG_SIZE);

        xe_np_usb_stream_diag.otg_gintsts = xe_np_usb_state.hpcd->Instance->GINTSTS;
        xe_np_usb_stream_diag.otg_daint = device->DAINT;
        xe_np_usb_stream_diag.otg_diepctl1 = in_ep->DIEPCTL;
        xe_np_usb_stream_diag.otg_diepint1 = in_ep->DIEPINT;
        xe_np_usb_stream_diag.otg_dieptsiz1 = in_ep->DIEPTSIZ;
        xe_np_usb_stream_diag.otg_dtxfsts1 = in_ep->DTXFSTS;
    }
}

static int XE_NP_UsbStream_Disarm(uint32_t reason)
{
#if NP_USB_BLOCK_PROBE
 NP_UsbBlockSelect(0U,xe_np_usb_disarm_count);
#endif
    uint32_t mask=__get_PRIMASK();__disable_irq();
    NP_UsbGridStop();
    
#if NP_FIRST_SYNC_ENABLE
    NP_FirstCaptureStop();
#endif

    NP_UsbStartAckCancel(); /* Before any owner mutation or cold-ready return. */
    NP_UsbTimingDisarm(reason);
    if(!NP_USB_IsReady()){
        __set_PRIMASK(mask);
        if(np_usb_runtime_ready && np_usb_stage_retire_failed)XE_NP_App_StopUsbCapture();
        return -1; /* Cold/unbound or held retirement failure: no new owner. */
    }
#if (XE_NP_APP_QUAD_USB_WORD_EXPORT != 0U)
    xe_np_usb_v1_restart_pending = 0U;
#endif
    if(reason != XE_NP_USB_DISARM_REASON_NONE)
    {
        xe_np_usb_disarm_count++;
        xe_np_usb_last_disarm_reason = reason;
    }
    /* Revoke foreground launch before trying to retire its hardware. */
    xe_np_usb_state.streaming=0U;xe_np_usb_state.real_start_pending=0U;
    if(XE_NP_UsbStream_StopInterleavedStagingDma()!=0){
        XE_NP_UsbStream_SetDiagStatus(0xBADA0018UL);
        __set_PRIMASK(mask);XE_NP_App_StopUsbCapture();return -1;
    }
    XE_NP_App_DisableUsbWindowExport();
    XE_NP_UsbStream_ResetLaneValidator();
    xe_np_usb_state.streaming = 0U;
    xe_np_usb_state.real_start_pending = 0U;
    xe_np_usb_state.real_start_port_index = 0U;
    np_usb_queue_produced = np_usb_queue_consumed = 0U;
    np_usb_queue_write = np_usb_queue_read = 0U;
    xe_np_usb_state.tx_busy = 0U;
    xe_np_usb_state.tx_pending = 0U;
    xe_np_usb_state.tx_active_idx = 0U;
    xe_np_usb_state.tx_pending_idx = 0U;
    xe_np_usb_state.tx_stage = 0U;
    xe_np_usb_state.packet_assembly_idx = 0U;
    xe_np_usb_state.packet_assembly_bytes = 0U;
    xe_np_usb_state.synthetic_source = 0U;
    memset(xe_np_usb_state.tx_payload_ptr, 0, sizeof(xe_np_usb_state.tx_payload_ptr));
    memset(xe_np_usb_state.tx_len, 0, sizeof(xe_np_usb_state.tx_len));
    memset(xe_np_usb_state.tx_payload_bytes, 0, sizeof(xe_np_usb_state.tx_payload_bytes));
    memset(xe_np_usb_state.tx_split_payload, 0, sizeof(xe_np_usb_state.tx_split_payload));
    xe_np_usb_state.port = NULL;
    __set_PRIMASK(mask);return 0;
}

static void XE_NP_UsbStream_StopInternal(uint32_t reason)
{
#if NP_USB_BLOCK_PROBE
 NP_UsbBlockSelect(0U,xe_np_usb_disarm_count);
#endif
    if(!np_usb_runtime_ready)return; /* A retire fault still needs capture shutdown. */
#if (XE_NP_APP_QUAD_USB_WORD_EXPORT != 0U)
    if(reason == XE_NP_USB_DISARM_REASON_CAPTURE_EXPORT_FAULT)
        xe_np_usb_v1_latched_fault = xe_np_usb_stream_diag.last_status;
#endif
    uint8_t was_capture_stream =
        (((xe_np_usb_state.port != NULL) && (xe_np_usb_state.synthetic_source == 0U)) || xe_np_usb_state.real_start_pending) ? 1U : 0U;

    XE_NP_UsbStream_AbortBulkInTransfer();
    NP_UsbCameraStop();
    int retired=XE_NP_UsbStream_Disarm(reason);
    if(was_capture_stream != 0U && retired==0)
    {
        XE_NP_App_StopUsbCapture();
    }
    XE_NP_UsbStream_UpdateDiag();
#if NP_HARDWARE_RTC_ENABLE
    NP_V1RtcPortReady();
#endif
}

void XE_NP_UsbStream_Stop(void)
{
    
#if NP_FIRST_SYNC_ENABLE
    NP_FirstCaptureStop();
#endif

    XE_NP_UsbStream_StopInternal(XE_NP_USB_DISARM_REASON_HOST_STOP);
}

void XE_NP_UsbStream_RearmConsumer(void)
{
    if((xe_np_usb_state.streaming == 0U) || (xe_np_usb_state.port == NULL))
    {
        return;
    }

    if(xe_np_usb_state.synthetic_source == 0U)
    {
        (void)XE_NP_App_EnableUsbWindowExport(xe_np_usb_state.port_index);
    }
    xe_np_usb_state.last_tx_tick_ms = HAL_GetTick();
    XE_NP_UsbStream_UpdateDiag();
}

static int XE_NP_UsbStream_Start(uint8_t port_index)
{
#if (XE_NP_APP_QUAD_USB_WORD_EXPORT != 0U)
    uint32_t previous = xe_np_usb_v1_latched_fault;
    if(previous == 0U && XE_NP_App_UsbWordFault() != 0U)
        previous = 0xBADA0020UL | XE_NP_App_UsbWordFault();
    if(previous == 0U && xe_np_usb_state.streaming != 0U)
        previous = 0xBADA0030UL; /* unclosed/superseded prior session */
#endif
    XE_NP_UsbStream_AbortBulkInTransfer();
    NP_UsbCameraStop();
    if(XE_NP_UsbStream_Disarm(XE_NP_USB_DISARM_REASON_START_BEGIN)!=0)return -1;
    xe_np_usb_start_request_count++;

    if((xe_np_usb_state.configured == 0U) ||
       (XE_NP_App_GetCapturePortForUsb(port_index) == NULL))
    {
        XE_NP_UsbStream_SetDiagStatus(0xBAD00001UL);
        return -1;
    }

    NP_UsbGridAdmit();
#if NP_HARDWARE_RTC_ENABLE
    NP_V1RtcPortFreeze();
#endif
    NP_UsbStartAckWait();
    xe_np_usb_state.real_start_port_index = port_index;
    xe_np_usb_state.port_index = port_index;
#if (XE_NP_APP_QUAD_USB_WORD_EXPORT != 0U)
    xe_np_usb_v1_previous_fault = previous;
    xe_np_usb_v1_restart_pending = 1U;
#endif
    /* This function is reached from the EP0 setup callback.  Do not begin
     * probe power-up, I2C traffic, timer/DMA configuration, or paired-lane
     * qualification in USB IRQ context: besides extending the control
     * transaction unpredictably, that can starve the OTG core just as the
     * first bulk-IN read is being queued.  The foreground service below
     * owns the complete real-start sequence and keeps retrying it until the
     * app reports that both producers are truly ready. */
    XE_NP_UsbStream_SetDiagStatus(0x10000001UL);

    return 0;
}

static int XE_NP_UsbStream_TryStartReal(uint8_t port_index)
{
    XE_NP_CapturePort *port = XE_NP_App_GetCapturePortForUsb(port_index);
#if (XE_NP_APP_QUAD_USB_WORD_EXPORT != 0U)
    const uint32_t start_generation = xe_np_usb_disarm_count;
    uint32_t primask;
#endif

    xe_np_usb_start_try_count++;

    if((xe_np_usb_state.configured == 0U) ||
       (port == NULL) ||
       ((XE_NP_App_IsUsbQuadInterleavedCaptureConfigured() == 0) &&
        ((port->storage == NULL) || (port->storage_bytes == 0U) ||
         (port->raw_mgr.dataSize == 0U))))
    {
        xe_np_usb_start_try_fail_count++;
        xe_np_usb_last_start_try_status = XE_NP_USB_START_STATUS_PORT_INVALID;
        XE_NP_UsbStream_SetDiagStatus(0xBAD00001UL);
        return -1;
    }
#if (XE_NP_APP_QUAD_USB_WORD_EXPORT != 0U)
    if(xe_np_usb_v1_restart_pending != 0U)
    {
#if NP_FIRST_SYNC_ENABLE
        if(xe_np_usb_disarm_count!=start_generation || !xe_np_usb_state.real_start_pending)return -1;
#if NP_FINE_COUNTER64_ENABLE
        NP_UsbFineStart();
#endif
        NP_FirstCaptureStart(0U,NP_USB20_PAIRED()?NP_FIRST_USB16:NP_FIRST_USB8);
        NP_UsbDiagStart();
#endif
        xe_np_usb_v1_restart_pending = 0U;
        XE_NP_App_ResetUsbWordSession();
        if((xe_np_usb_disarm_count != start_generation) ||
           (xe_np_usb_state.real_start_pending == 0U))
        {
            return -1;
        }
        ++xe_np_usb_v1_reset_count;
        ++xe_np_usb_v1_session_id;
        xe_np_usb_v1_latched_fault = 0U;
    }
#endif
    if(XE_NP_App_StartUsbCapture(port_index) != 0)
    {
        xe_np_usb_start_try_fail_count++;
        xe_np_usb_last_start_try_status = XE_NP_USB_START_STATUS_APP_REJECTED;
        return -1;
    }
    /*
     * A host START can arrive while the bench stop path is still holding the
     * probe session down. Keep the request pending until the probe/DMA
     * producers are actually back up; otherwise xe_np_usb_state.streaming
     * flips high too early and suppresses the app-side bring-up state machine.
     */
    if(XE_NP_App_IsUsbCaptureReady(port_index) == 0)
    {
        xe_np_usb_start_try_fail_count++;
        xe_np_usb_last_start_try_status = XE_NP_USB_START_STATUS_APP_NOT_READY;
        return -1;
    }

    xe_np_usb_state.port = port;
    xe_np_usb_state.port_index = port_index;
    xe_np_usb_state.payload_bytes_sent = 0U;
    xe_np_usb_state.dropped_bytes = 0U;
    xe_np_usb_state.sequence = 0U;
    xe_np_usb_state.packets_sent = 0U;
    xe_np_usb_state.last_tx_tick_ms = HAL_GetTick();
    XE_NP_UsbStream_ResetLaneValidator();
    XE_NP_UsbStream_ResetSeamProbe();
#if (XE_NP_USB_STREAM_REAL_CAPTURE_SYNTHETIC_PAYLOAD_ENABLE != 0U)
    /* This diagnostic keeps the real probe session armed but uses the exact
     * synthetic-stream lifecycle, including no capture-window retirement from
     * the endpoint ISR. */
    xe_np_usb_state.synthetic_source = 1U;
#endif
    port->raw_mgr.logState &= ~(MGR_STATE_TRANSOVR | MGR_STATE_TRANSERR);
#if (XE_NP_USB_STREAM_REAL_CAPTURE_SYNTHETIC_DETACH_PORT_ENABLE != 0U)
    /* The real session continues in the app, but the USB state machine is
     * made identical to an ordinary synthetic stream. */
    xe_np_usb_state.port = NULL;
    xe_np_usb_state.port_index = 0xFFU;
#endif
    if((XE_NP_App_IsUsbInterleavedCapture() == 0) &&
       (XE_NP_App_IsUsbQuadInterleavedCaptureConfigured() == 0) &&
       (XE_NP_App_EnableUsbWindowExport(port_index) != 0))
    {
        xe_np_usb_state.port = NULL;
        xe_np_usb_start_try_fail_count++;
        xe_np_usb_last_start_try_status = XE_NP_USB_START_STATUS_EXPORT_ENABLE_FAIL;
        return -1;
    }
#if (XE_NP_USB_STREAM_REAL_CAPTURE_RESTART_SYNTHETIC_ENABLE != 0U)
    /* Isolation control: after the real NP session is fully active, restart
     * the endpoint through the exact synthetic START lifecycle.  This tells
     * us whether the OTG endpoint itself is affected by the active probe, or
     * whether the paired producer's initial endpoint state is the cause. */
    return XE_NP_UsbStream_StartSynthetic();
#endif
#if (XE_NP_APP_QUAD_USB_WORD_EXPORT != 0U)
    /* STOP/disconnect must win over a foreground bring-up already in flight. */
    primask = __get_PRIMASK();
    __disable_irq();
    if((xe_np_usb_disarm_count != start_generation) ||
       (xe_np_usb_state.real_start_pending == 0U))
    {
        __set_PRIMASK(primask);
        return -1;
    }
#endif
    xe_np_usb_state.streaming = 1U;
#if NP_USB_BLOCK_PROBE
 NP_UsbBlockSelect(1U,xe_np_usb_disarm_count);
#endif
    xe_np_usb_state.real_start_pending = 0U;
    xe_np_usb_last_start_try_status = XE_NP_USB_START_STATUS_OK;
    XE_NP_UsbStream_SetDiagStatus(0U);
#if (XE_NP_APP_QUAD_USB_WORD_EXPORT != 0U)
    __set_PRIMASK(primask);
#endif
    return 0;
}

static void XE_NP_UsbStream_FillSyntheticPayload(uint8_t buffer_index)
{
    uint8_t *dst;
    uint32_t i;

    if(buffer_index >= XE_NP_USB_STREAM_TX_BUFFER_COUNT)
    {
        return;
    }

    dst = &xe_np_usb_tx_packet[buffer_index][XE_NP_USB_STREAM_HEADER_BYTES];
    for(i = 0U; i < XE_NP_USB_STREAM_SYNTHETIC_PAYLOAD_BYTES; i++)
    {
        dst[i] = (uint8_t)(i + ((uint32_t)buffer_index * 17UL));
    }
}

static int XE_NP_UsbStream_StartSynthetic(void)
{
#if NP_HARDWARE_RTC_ENABLE
    NP_V1RtcPortFreeze();
#endif
    uint8_t i;

    XE_NP_UsbStream_AbortBulkInTransfer();
    NP_UsbCameraStop();
    if(XE_NP_UsbStream_Disarm(XE_NP_USB_DISARM_REASON_START_SYNTHETIC_BEGIN)!=0)return -1;
    if((xe_np_usb_state.configured == 0U) || (xe_np_usb_state.hpcd == NULL))
    {
        XE_NP_UsbStream_SetDiagStatus(0xBAD00008UL);
        return -1;
    }

#if (XE_NP_USB_STREAM_SYNTHETIC_ENABLE_NP_VCC_TEST != 0U)
    /* Hardware-isolation control: reproduce only the real-session VCC rail
     * assertion while keeping NP reset, MCLK, I2C and GPIO DMA untouched. */
    HAL_GPIO_WritePin(VCC_EN_GPIO_Port, VCC_EN_Pin, GPIO_PIN_SET);
#endif
#if (XE_NP_USB_STREAM_SYNTHETIC_RELEASE_NP_RESETS_TEST != 0U)
    HAL_GPIO_WritePin(VCC_EN_GPIO_Port, VCC_EN_Pin, GPIO_PIN_SET);
    HAL_GPIO_WritePin(NP_NRST_GPIO_Port, NP_NRST_Pin, GPIO_PIN_SET);
    HAL_GPIO_WritePin(NP_REC_NRST_GPIO_Port, NP_REC_NRST_Pin, GPIO_PIN_SET);
#endif

    for(i = 0U; i < XE_NP_USB_STREAM_TX_BUFFER_COUNT; i++)
    {
        XE_NP_UsbStream_FillSyntheticPayload(i);
    }

    xe_np_usb_state.synthetic_source = 1U;
    xe_np_usb_state.port_index = 0xFFU;
    xe_np_usb_state.payload_bytes_sent = 0U;
    xe_np_usb_state.dropped_bytes = 0U;
    xe_np_usb_state.sequence = 0U;
    xe_np_usb_state.packets_sent = 0U;
    xe_np_usb_state.last_tx_tick_ms = HAL_GetTick();
    XE_NP_UsbStream_ResetLaneValidator();
    xe_np_usb_state.streaming = 1U;
#if NP_USB_BLOCK_PROBE
 NP_UsbBlockSelect(1U,xe_np_usb_disarm_count);
#endif
    XE_NP_UsbStream_SetDiagStatus(0U);
    return 0;
}

static void XE_NP_UsbStream_OpenControlEndpoints(PCD_HandleTypeDef *hpcd)
{
    (void)HAL_PCD_EP_Open(hpcd, 0x00U, XE_NP_USB_STREAM_EP0_MPS, EP_TYPE_CTRL);
    (void)HAL_PCD_EP_Open(hpcd, 0x80U, XE_NP_USB_STREAM_EP0_MPS, EP_TYPE_CTRL);
}

static void XE_NP_UsbStream_OpenBulkEndpoints(PCD_HandleTypeDef *hpcd)
{
    (void)HAL_PCD_EP_Open(
        hpcd,
        XE_NP_USB_STREAM_IN_EP,
        XE_NP_USB_STREAM_BULK_MPS,
        EP_TYPE_BULK);
    (void)HAL_PCD_EP_Open(
        hpcd,
        XE_NP_USB_STREAM_OUT_EP,
        XE_NP_USB_STREAM_BULK_MPS,
        EP_TYPE_BULK);
    (void)HAL_PCD_EP_Receive(
        hpcd,
        XE_NP_USB_STREAM_OUT_EP,
        xe_np_usb_out,
        XE_NP_USB_STREAM_BULK_MPS);
}

static void XE_NP_UsbStream_CloseBulkEndpoints(PCD_HandleTypeDef *hpcd)
{
    (void)HAL_PCD_EP_Close(hpcd, XE_NP_USB_STREAM_IN_EP);
    (void)HAL_PCD_EP_Close(hpcd, XE_NP_USB_STREAM_OUT_EP);
}

static void XE_NP_UsbStream_AbortBulkInTransfer(void)
{
    PCD_HandleTypeDef *hpcd = xe_np_usb_state.hpcd;

    if((hpcd == NULL) || (xe_np_usb_state.configured == 0U))
    {
        return;
    }

    (void)HAL_PCD_EP_Abort(hpcd, XE_NP_USB_STREAM_IN_EP);
    (void)HAL_PCD_EP_Flush(hpcd, XE_NP_USB_STREAM_IN_EP);
    xe_np_usb_state.tx_stage = 0U;
}

static void XE_NP_UsbStream_HandleGetDescriptor(PCD_HandleTypeDef *hpcd, const XE_NP_UsbSetup *req)
{
    uint8_t desc_type = (uint8_t)(req->wValue >> 8U);
    uint8_t desc_index = (uint8_t)(req->wValue & 0xFFU);
    uint16_t string_len;

    switch(desc_type)
    {
        case 0x01U:
            (void)XE_NP_UsbStream_CtlSend(
                hpcd,
                xe_np_usb_device_descriptor,
                (uint16_t)sizeof(xe_np_usb_device_descriptor),
                req->wLength);
            return;
        case 0x02U:
            (void)XE_NP_UsbStream_CtlSend(
                hpcd,
                xe_np_usb_config_descriptor,
                (uint16_t)sizeof(xe_np_usb_config_descriptor),
                req->wLength);
            return;
        case 0x03U:
            string_len = XE_NP_UsbStream_SelectString(desc_index);
            if(string_len != 0U)
            {
                (void)XE_NP_UsbStream_CtlSend(hpcd, xe_np_usb_string_buf, string_len, req->wLength);
                return;
            }
            break;
        case 0x06U:
            (void)XE_NP_UsbStream_CtlSend(
                hpcd,
                xe_np_usb_device_qualifier_descriptor,
                (uint16_t)sizeof(xe_np_usb_device_qualifier_descriptor),
                req->wLength);
            return;
        case 0x07U:
            (void)XE_NP_UsbStream_CtlSend(
                hpcd,
                xe_np_usb_other_speed_config_descriptor,
                (uint16_t)sizeof(xe_np_usb_other_speed_config_descriptor),
                req->wLength);
            return;
        case 0x0FU:
            (void)XE_NP_UsbStream_CtlSend(
                hpcd,
                xe_np_usb_bos_descriptor,
                (uint16_t)sizeof(xe_np_usb_bos_descriptor),
                req->wLength);
            return;
        default:
            break;
    }

    XE_NP_UsbStream_Stall(hpcd);
}

static void XE_NP_UsbStream_HandleStandardRequest(PCD_HandleTypeDef *hpcd, const XE_NP_UsbSetup *req)
{
    static uint8_t status_buf[2];

    switch(req->bRequest)
    {
        case 0x00U:
            status_buf[0] = 0U;
            status_buf[1] = 0U;
            (void)XE_NP_UsbStream_CtlSend(hpcd, status_buf, 2U, req->wLength);
            return;

        case 0x01U:
            if(((req->bmRequest & 0x1FU) == 0x02U) && (req->wLength == 0U))
            {
                (void)HAL_PCD_EP_ClrStall(hpcd, (uint8_t)req->wIndex);
                XE_NP_UsbStream_CtlStatus(hpcd);
                return;
            }
            break;

        case 0x05U:
            if((req->wIndex == 0U) && (req->wLength == 0U) && (req->wValue < 128U))
            {
                xe_np_usb_state.ep0_pending_address = (uint8_t)(req->wValue & 0x7FU);
                xe_np_usb_state.ep0_address_pending = 0U;
                (void)HAL_PCD_SetAddress(hpcd, xe_np_usb_state.ep0_pending_address);
                XE_NP_UsbStream_CtlStatus(hpcd);
                return;
            }
            break;

        case 0x06U:
            XE_NP_UsbStream_HandleGetDescriptor(hpcd, req);
            return;

        case 0x08U:
            status_buf[0] = xe_np_usb_state.configured;
            (void)XE_NP_UsbStream_CtlSend(hpcd, status_buf, 1U, req->wLength);
            return;

        case 0x09U:
            if((req->wIndex == 0U) && (req->wLength == 0U))
            {
                if((req->wValue & 0xFFU) == 1U)
                {
                    np_usb_auto_sd.enumerated = 1U;
                    xe_np_usb_state.configured = 1U;
                    XE_NP_UsbStream_OpenBulkEndpoints(hpcd);
                    XE_NP_UsbStream_CtlStatus(hpcd);
                    return;
                }
                if((req->wValue & 0xFFU) == 0U)
                {
                    XE_NP_UsbStream_Disarm(XE_NP_USB_DISARM_REASON_SET_CONFIG_ZERO);
                    xe_np_usb_state.configured = 0U;
                    XE_NP_UsbStream_CloseBulkEndpoints(hpcd);
                    XE_NP_UsbStream_CtlStatus(hpcd);
                    return;
                }
            }
            break;

        case 0x0AU:
            status_buf[0] = 0U;
            (void)XE_NP_UsbStream_CtlSend(hpcd, status_buf, 1U, req->wLength);
            return;

        case 0x0BU:
            if((req->wValue == 0U) && (req->wLength == 0U))
            {
                XE_NP_UsbStream_CtlStatus(hpcd);
                return;
            }
            break;

        default:
            break;
    }

    XE_NP_UsbStream_Stall(hpcd);
}

#include "C:/Frank/Code/XE_NP/_artifacts/v1_rtc_20261029/usb_source/frozen/_artifacts/v1_usb_uhs200_restore_20261020/retained-r2/source/np_usb_backend.inc"
#include "C:/Frank/Code/XE_NP/_artifacts/v1_usb_integrity_20261016/paired_restore/source/Core/Src/np_usb_camera_driver.inc"
#include "C:/Frank/Code/XE_NP/_artifacts/v1_usb_integrity_20261016/paired_restore/source/Core/Src/np_usb_camera_transport.inc"
#include "C:/Frank/Code/XE_NP/_artifacts/v1_usb_integrity_20261016/paired_restore/source/Core/Src/np_usb_camera_only.inc"
#include "C:/Frank/Code/XE_NP/_artifacts/v1_rtc_20261029/usb_source/frozen/Firmware/V1/Core/Src/np_sd_usb_download.inc"
#include "C:/Frank/Code/XE_NP/_artifacts/v1_usb_sd_job_20261030/usb_stage_cold_r1/modes.inc"
#include "C:/Frank/Code/XE_NP/_artifacts/v1_deterministic_20261029/usb_rtc_settled_capacity_incremental_r1/xe_np_usb_stream/73bab3d5dfed_np_config_usb.inc"
#include "C:/Frank/Code/XE_NP/_artifacts/v1_rtc_20261029/ep0_integrated_r1/usb_source/Core/Src/np_usb_start_ack.inc"
#include "C:/Frank/Code/XE_NP/_artifacts/v1_usb_integrity_20261016/paired_restore/source/Core/Src/np_modes_usb.inc"
#include "C:/Frank/Code/XE_NP/_artifacts/v1_usb_fast_startup_r79r2_20261006/source/Core/Src/startup_diag_usb.inc"
#include "C:/Frank/Code/XE_NP/_artifacts/v1_usb_fast_startup_r79r2_20261006/source/Core/Src/startup_admission_usb.inc"
#include "C:/Frank/Code/XE_NP/_artifacts/v1_usb_fast_startup_r79r2_20261006/source/Core/Src/np_phase_sweep_usb.inc"
#include "C:/Frank/Code/XE_NP/_artifacts/v1_rtc_20261029/usb_source/frozen/Firmware/V1/Core/Src/np_usb_sd_format.inc"
#include "C:/Frank/Code/XE_NP/Firmware/V1/Core/Src/np_usb_sd_job_service.inc"
#include "C:/Frank/Code/XE_NP/Firmware/V1/Core/Src/np_usb_sd_job_backend.inc"
#include "C:/Frank/Code/XE_NP/Firmware/V1/Core/Src/np_usb_sd_job_platform.inc"
#if NP_CL_ENABLE
static void NP_ClUsbSlot(uint8_t *,uint32_t);
#endif
#include "C:/Frank/Code/USB_PCconsole/_artifacts/closed_loop_fix_20261102/V1/usb_policy_check/grid.inc"
#include "C:/Frank/Code/XE_NP/_artifacts/v1_rtc_20261029/control_reject_diag_r2/usb_source/Core/Src/np_usb_control_reject.inc"
static void XE_NP_UsbStream_HandleVendorRequest(PCD_HandleTypeDef *hpcd, const XE_NP_UsbSetup *req)
{
    if(req->bRequest == 0x40U || req->bRequest == 0x47U)
        NP_UsbControlRejectReset(); /* A fresh Start attempt, not every USB SETUP. */
    /* Standard enumeration and WinUSB descriptors work while optional services initialize. */
    if(!NP_USB_IsReady() && req->bRequest!=XE_NP_USB_STREAM_VENDOR_CODE &&
       req->bRequest!=0x6EU){XE_NP_UsbStream_Stall(hpcd);return;}
    if(np_usb_sd_job_request(hpcd,req))return;
    if(np_usb_sd_job_owned() && req->bRequest!=0x6EU){XE_NP_UsbStream_Stall(hpcd);return;}
    if(np_usb_format_request(hpcd,req))return;
    if(np_usb_format_owned()){XE_NP_UsbStream_Stall(hpcd);return;}
    /* Existing mode reset owns the stopped engine until detach/re-enumeration. */
    if(np_v1_mode.target && req->bRequest==0x75U){XE_NP_UsbStream_Stall(hpcd);return;}
    if((req->bRequest==XE_NP_USB_STREAM_REQ_START || req->bRequest==XE_NP_USB_STREAM_REQ_START_STREAMS) &&
       (np_v1_mode.target || !np_mode_valid(np_mode_mask,np_mode_hz) ||
        ((np_mode_hz==20000U)!=NP_USB20_Scaled()) ||
        ((!np_mode_mask && !np_mode_hz)!=NP_USB20_CameraOnly()))) {XE_NP_UsbStream_Stall(hpcd);return;}
    if(NP_UsbGridRequest(hpcd,req))return;
    if(np_admission_request(hpcd,req))return;
    if(np_startup_request(hpcd,req))return;
    if(np_mode_request(hpcd,req))return;
    if(np_phase_sweep_request(hpcd,req))return;
    if(np_v1_mode_request(hpcd,req))return;
    if(npc_usb_request(hpcd,req))return;
#if NP_HARDWARE_RTC_ENABLE
    if(NP_V1RtcPortInitializing() && (req->bRequest==XE_NP_USB_STREAM_REQ_START || req->bRequest==XE_NP_USB_STREAM_REQ_START_STREAMS || req->bRequest==XE_NP_USB_STREAM_REQ_START_SYNTHETIC)){XE_NP_UsbStream_Stall(hpcd);return;}
    if(NP_ConfigRtcPending() && req->bRequest==XE_NP_USB_STREAM_REQ_START_SYNTHETIC){XE_NP_UsbStream_Stall(hpcd);return;}
#endif
    if(NP_ConfigTransferPending() && (req->bRequest==XE_NP_USB_STREAM_REQ_START || req->bRequest==XE_NP_USB_STREAM_REQ_START_STREAMS)){XE_NP_UsbStream_Stall(hpcd);return;}
    if(NP_USB_IsDownload() && NP_ConfigTransferPending()){XE_NP_UsbStream_Stall(hpcd);return;}
    if(NP_USB_IsDownload() && NP_SD_UsbRequest(hpcd,req))return;
    if(!NP_USB_IsDownload() && NP_UsbCameraVendorRequest(hpcd,req))return;
    if((req->bmRequest & 0x80U) != 0U)
    {
        if((req->bRequest == XE_NP_USB_STREAM_VENDOR_CODE) && (req->wIndex == 0x0007U))
        {
            (void)XE_NP_UsbStream_CtlSend(
                hpcd,
                xe_np_usb_ms_os_20_descriptor,
                (uint16_t)sizeof(xe_np_usb_ms_os_20_descriptor),
                req->wLength);
            return;
        }
        if((req->bRequest == XE_NP_USB_STREAM_VENDOR_CODE) && (req->wIndex == 0x0004U))
        {
            (void)XE_NP_UsbStream_CtlSend(
                hpcd,
                xe_np_usb_ms_os_10_compat_id_descriptor,
                (uint16_t)sizeof(xe_np_usb_ms_os_10_compat_id_descriptor),
                req->wLength);
            return;
        }
        if((req->bRequest == XE_NP_USB_STREAM_VENDOR_CODE) && (req->wIndex == 0x0005U))
        {
            (void)XE_NP_UsbStream_CtlSend(
                hpcd,
                xe_np_usb_ms_os_10_ext_prop_descriptor,
                (uint16_t)sizeof(xe_np_usb_ms_os_10_ext_prop_descriptor),
                req->wLength);
            return;
        }
        if(req->bRequest == XE_NP_USB_STREAM_REQ_GET_STATUS)
        {
            XE_NP_UsbStream_UpdateDiag();
            (void)XE_NP_UsbStream_CtlSend(
                hpcd,
                (const uint8_t *)(const void *)&xe_np_usb_stream_diag,
                (uint16_t)sizeof(xe_np_usb_stream_diag),
                req->wLength);
            return;
        }
    }
    else
    {
        if((req->bRequest == XE_NP_USB_STREAM_REQ_START) && (req->wLength == 0U) && np_mode_mask)
        {
            if(XE_NP_UsbStream_Start((uint8_t)(req->wValue & 0x01U)) == 0)
            {
                XE_NP_UsbStream_CtlStatus(hpcd);
            }
            else
            {
                XE_NP_UsbStream_Stall(hpcd);
            }
            XE_NP_UsbStream_UpdateDiag();
            return;
        }
        if((req->bRequest == XE_NP_USB_STREAM_REQ_START_STREAMS) &&
           (req->wLength == 0U) &&
           (((req->wValue & XE_NP_USB_STREAM_MASK_NEUROPIXELS) && np_mode_mask) ||
            ((req->wValue & NP_USB_CAMERA_STREAM_MASK) &&
             !(req->wValue & XE_NP_USB_STREAM_MASK_NEUROPIXELS) && NP_USB20_CameraOnly())))
        {
            /* USB512-compatible host request. The connected NP1 is captured
             * on GPIOE; stream selection is explicit so an attached USB cable
             * never starts sampling until the host has queued its reads. */
            if((NP_USB20_CameraOnly()?NP_UsbCameraOnlyStart():XE_NP_UsbStream_Start(0U)) == 0)
            {
                NP_UsbCameraSelect(req->wValue);
                XE_NP_UsbStream_CtlStatus(hpcd);
            }
            else
            {
                XE_NP_UsbStream_Stall(hpcd);
            }
            XE_NP_UsbStream_UpdateDiag();
            return;
        }
        if((req->bRequest == XE_NP_USB_STREAM_REQ_START_SYNTHETIC) && (req->wLength == 0U))
        {
            if(XE_NP_UsbStream_StartSynthetic() == 0)
            {
                XE_NP_UsbStream_CtlStatus(hpcd);
            }
            else
            {
                XE_NP_UsbStream_Stall(hpcd);
            }
            XE_NP_UsbStream_UpdateDiag();
            return;
        }
        if((req->bRequest == XE_NP_USB_STREAM_REQ_STOP) && (req->wLength == 0U))
        {
            XE_NP_UsbStream_Stop();
            np_mode_stop_pending=1U;
            XE_NP_UsbStream_CtlStatus(hpcd);
            return;
        }
        if((req->bRequest == XE_NP_USB_STREAM_REQ_SET_MCO_DIVIDER) && (req->wLength == 0U))
        {
            if(req->wValue == NP_USB20_CaptureDivider(np_mode_hz))
            {
                XE_NP_UsbStream_SetDiagStatus(0U);
                XE_NP_UsbStream_CtlStatus(hpcd);
            }
            else
            {
                XE_NP_UsbStream_SetDiagStatus(0xBAD0000AUL);
                XE_NP_UsbStream_Stall(hpcd);
            }
            XE_NP_UsbStream_UpdateDiag();
            return;
        }
    }

    XE_NP_UsbStream_Stall(hpcd);
}

static void XE_NP_UsbStream_WritePacketHeader(
    uint8_t *dst,
    uint32_t payload_bytes,
    uint16_t packet_sequence,
    const XE_NP_AppUsbCaptureWindow *source_window,
    uint32_t available_bytes,
    uint32_t payload_hash,
    uint32_t edge_prefix_hash,
    uint32_t edge_suffix_hash)
{
    uint8_t flags = 0U;

    /* Match USB512's compact one-transfer framing.  The GPIO capture is copied
     * into private endpoint memory before this header is submitted, so USB DMA
     * cannot contend for an in-flight capture-ring cache line. */
    if(payload_bytes != XE_NP_USB_STREAM_PAYLOAD_BYTES)
    {
        flags = XE_NP_USB_STREAM_FLAG_PAYLOAD_LENGTH_IN_SEQUENCE;
    }
    dst[0] = (uint8_t)(XE_NP_USB_STREAM_PACKET_MAGIC & 0xFFU);
    dst[1] = (uint8_t)(XE_NP_USB_STREAM_PACKET_MAGIC >> 8U);
    dst[2] = XE_NP_USB_STREAM_ID_NEUROPIXELS;
    dst[3] = flags;
    XE_NP_UsbStream_Put16(
        dst,
        4U,
        (flags != 0U) ? (uint16_t)payload_bytes : packet_sequence);
    (void)source_window;
    (void)available_bytes;
    (void)payload_hash;
    (void)edge_prefix_hash;
    (void)edge_suffix_hash;
}

static void XE_NP_UsbStream_PrepareSyntheticPacket(uint8_t buffer_index)
{
    XE_NP_UsbStream_WritePacketHeader(
        xe_np_usb_tx_packet[buffer_index],
        XE_NP_USB_STREAM_SYNTHETIC_PAYLOAD_BYTES,
        (uint16_t)xe_np_usb_state.sequence,
        NULL,
        0U,
        0U,
        0U,
        0U);
    xe_np_usb_state.tx_payload_ptr[buffer_index] = xe_np_usb_tx_packet[buffer_index];
    xe_np_usb_state.tx_len[buffer_index] = XE_NP_USB_STREAM_PACKET_BYTES;
    xe_np_usb_state.tx_payload_bytes[buffer_index] = XE_NP_USB_STREAM_SYNTHETIC_PAYLOAD_BYTES;
    xe_np_usb_state.tx_split_payload[buffer_index] = 0U;
}

static void XE_NP_UsbStream_ServiceSynthetic(void)
{
    uint8_t buffer_index = 0U;

    if(xe_np_usb_state.tx_pending != 0U)
    {
        XE_NP_UsbStream_StartPendingPacket();
        XE_NP_UsbStream_UpdateDiag();
        return;
    }

    if(xe_np_usb_state.tx_busy != 0U)
    {
        buffer_index = (xe_np_usb_state.tx_active_idx == 0U) ? 1U : 0U;
    }

    XE_NP_UsbStream_PrepareSyntheticPacket(buffer_index);
    if(xe_np_usb_state.tx_busy != 0U)
    {
        xe_np_usb_state.tx_pending_idx = buffer_index;
        xe_np_usb_state.tx_pending = 1U;
        XE_NP_UsbStream_SetDiagStatus(0U);
    }
    else
    {
        (void)XE_NP_UsbStream_StartPreparedPacket(buffer_index);
    }

    XE_NP_UsbStream_UpdateDiag();
}

#if (XE_NP_USB_STREAM_QUAD_ZEROCOPY_ENABLE != 0U)
static void XE_NP_UsbStream_ServiceQuadZeroCopy(void)
{
    XE_NP_AppUsbQuadDmaBatch *batch = &xe_np_usb_state.quad_zero_copy_batch;
    uint8_t lane;

    if(xe_np_usb_state.tx_busy != 0U)
    {
        return;
    }
    if(xe_np_usb_state.quad_zero_copy_active == 0U)
    {
        int begin_status = XE_NP_App_BeginUsbQuadDmaBatch(
            XE_NP_USB_STREAM_PACKET_BYTES, batch);

        if(begin_status <= 0)
        {
            return;
        }
        /* The endpoint cannot scatter-gather a ring wrap. Refuse it rather
         * than creating an unmeasured foreground splice. */
        for(lane = 0U; lane < XE_NP_APP_USB_QUAD_DMA_LANE_COUNT; ++lane)
        {
            if((batch->source_segment_count[lane] != 1U) ||
               (batch->source_bytes[lane][0] != XE_NP_USB_STREAM_PACKET_BYTES))
            {
                XE_NP_App_CancelUsbQuadDmaBatch();
                XE_NP_UsbStream_SetDiagStatus(0xBADA0010UL);
                return;
            }
        }
        xe_np_usb_state.quad_zero_copy_lane = 0U;
        xe_np_usb_state.quad_zero_copy_active = 1U;
    }

    lane = xe_np_usb_state.quad_zero_copy_lane;
    xe_np_usb_state.tx_active_idx = 0U;
    xe_np_usb_state.tx_payload_ptr[0] = batch->source[lane][0];
    xe_np_usb_state.tx_payload_bytes[0] = XE_NP_USB_STREAM_PACKET_BYTES;
    xe_np_usb_state.tx_len[0] = XE_NP_USB_STREAM_PACKET_BYTES;
    xe_np_usb_state.tx_busy = 1U;
    xe_np_usb_state.tx_stage = 0U;
    if(XE_NP_UsbStream_StartPreparedPayload(0U) == HAL_OK)
    {
        ++xe_np_usb_state.sequence;
        ++xe_np_usb_state.packets_sent;
        xe_np_usb_state.payload_bytes_sent += XE_NP_USB_STREAM_PACKET_BYTES;
        xe_np_usb_state.last_tx_tick_ms = HAL_GetTick();
    }
    else
    {
        XE_NP_App_CancelUsbQuadDmaBatch();
        xe_np_usb_state.quad_zero_copy_active = 0U;
    }
}
#endif

#include "C:/Frank/Code/XE_NP/_artifacts/v1_deterministic_20261029/diagnostic/usb_lifecycle_diag.inc"
#if NP_CL_ENABLE
#include "C:/Frank/Code/XE_NP/Firmware/V1/Core/Src/np_cl_usb.inc"
static void NP_UsbClPrelaunchSelect(uint32_t active);
#endif
#include "C:/Frank/Code/USB_PCconsole/_artifacts/closed_loop_fix_20261102/V1/usb_policy_check/sensors.inc"
#include "C:/Frank/Code/USB_PCconsole/_artifacts/closed_loop_fix_20261102/V1/usb_ready_profile/queue.inc"
#if NP_USB_BLOCK_PROBE
/* max_cycles0/13 are diagnostic reason/timestamp, NOT cycle maxima. */
void NP_UsbBlockPrelaunchZero(uint32_t reason)
{
    uint32_t now=HAL_GetTick(),key=__get_PRIMASK();__disable_irq();
    if(np_usb_block_probe.active && np_usb_block_probe.outside.site!=NP_UB_FIRST_SOURCE_FAILURE){
        np_usb_block_probe.max_cycles[0]=reason;np_usb_block_probe.max_cycles[13]=now;
    }
    __set_PRIMASK(key);
}
void NP_UsbBlockSourceFailure(uint32_t fault,uint32_t lane,uint32_t available,uint32_t requested,uint32_t head,
    uint32_t previous,uint32_t hal_gap,uint32_t now,uint32_t claimed)
{
    uint32_t context=NP_UsbBlockContext(),key=__get_PRIMASK();__disable_irq();
    if(fault && np_usb_block_probe.active && np_usb_block_probe.outside.site!=NP_UB_FIRST_SOURCE_FAILURE){
        uint32_t reason=np_usb_block_probe.max_cycles[0]&15U;
        uint32_t age=reason?now-np_usb_block_probe.max_cycles[13]:0U;
        if(age>4095U)age=4095U;
        uint32_t queue=(np_usb_queue_produced-np_usb_queue_consumed)&255U;
        queue|=(xe_np_usb_interleaved_staging_dma.busy!=0U)<<8U;
        queue|=(xe_np_usb_quad_word_ready!=0U)<<9U;
        queue|=(xe_np_usb_state.tx_busy!=0U)<<10U;
        queue|=(claimed!=0U)<<11U;queue|=(lane&15U)<<12U;queue|=reason<<16U;queue|=age<<20U;
        /* cycles0 is explicitly UNKNOWN: no additional steady DWT work. */
        NpUsbBlockRecord r={0U,hal_gap,NP_UB_FIRST_SOURCE_FAILURE,np_usb_block_probe.parent,
            available,requested,head,previous,fault,context,queue,now};
        np_usb_block_probe.outside=r;
    }
    __set_PRIMASK(key);
}
#endif
#if NP_USB_BLOCK_PROBE
static void __attribute__((noinline)) NP_V1UsbOriginalService_BlockBody(void)
{
    if(!NP_USB_IsReady())return;
    #if NP_USB_BLOCK_PROBE
{ NpUsbBlockStamp block=NP_UsbBlockBegin(NP_UB_FORMAT,0U,0U,0U);
 int result=np_usb_format_service();
 NP_UsbBlockEnd(block,(uint32_t)result,0U);
 if(result)return; }
#else
if(np_usb_format_service())return;
#endif
    #if NP_USB_BLOCK_PROBE
{ NpUsbBlockStamp block=NP_UsbBlockBegin(NP_UB_MODE,0U,0U,0U);
np_v1_mode_service();
NP_UsbBlockEnd(block,NP_UB_NO_RESULT,0U); }
#else
np_v1_mode_service();
#endif
#if NP_HARDWARE_RTC_ENABLE
    #if NP_USB_BLOCK_PROBE
{ NpUsbBlockStamp block=NP_UsbBlockBegin(NP_UB_RTC,0U,0U,0U);
NP_V1RtcPortService();
NP_UsbBlockEnd(block,NP_UB_NO_RESULT,0U); }
#else
NP_V1RtcPortService();
#endif
#endif
    #if NP_USB_BLOCK_PROBE
{ NpUsbBlockStamp block=NP_UsbBlockBegin(NP_UB_CONFIG,0U,0U,0U);
NP_ConfigService();
NP_UsbBlockEnd(block,NP_UB_NO_RESULT,0U); }
#else
NP_ConfigService();
#endif /* Also available in idle download mode before any START. */
    if(NP_USB_IsDownload()){
        if(np_usb_sd_prepare_pending){NP_USB_PrepareDownload();return;}
        if(np_v1_sd_reset_service())return;NP_SD_UsbService();return;
    }
    NP_UsbCameraService();
    #if NP_USB_BLOCK_PROBE
{ NpUsbBlockStamp block=NP_UsbBlockBegin(NP_UB_SENSORS,0U,0U,0U);
NP_UsbSensorsService();
NP_UsbBlockEnd(block,NP_UB_NO_RESULT,0U); }
#else
NP_UsbSensorsService();
#endif
    if(np_mode_stop_pending) {
        np_mode_stop_pending=0U;
        XE_NP_App_ResetUsbWordSession();
    }
    uint8_t *packet_ptr;
    XE_NP_AppUsbCaptureWindow source_window;
    uint32_t remaining_bytes;
    uint32_t packet_index;
    uint32_t packet_payload_offset;
    uint32_t copied_bytes;
    uint32_t interleaved_export_debug;
    int dequeue_status;
    uint8_t interleaved_capture;
    uint8_t interleaved_lane_b;
    uint8_t quad_interleaved_capture;
    uint8_t buffer_index;
    XE_NP_AppUsbInterleavedDmaBatch interleaved_dma_batch;
    int interleaved_dma_status;
    uint32_t interleaved_packet_target;
    uint32_t interleaved_payload_target;

    if((xe_np_usb_state.configured == 0U) || (xe_np_usb_state.hpcd == NULL))
    {
        XE_NP_UsbStream_UpdateDiag();
        return;
    }

    if((xe_np_usb_state.streaming == 0U) &&
       (xe_np_usb_state.real_start_pending == 1U))
    {
        NP_UsbCameraPrepareStart();
        if(NP_USB20_CameraOnly())NP_UsbCameraOnlyArm();
        else (void)XE_NP_UsbStream_TryStartReal(xe_np_usb_state.real_start_port_index);
        XE_NP_UsbStream_UpdateDiag();
        return;
    }

    if(xe_np_usb_state.streaming == 0U)
    {
        XE_NP_UsbStream_UpdateDiag();
        return;
    }
    if(xe_np_usb_state.synthetic_source != 0U)
    {
        XE_NP_UsbStream_ServiceSynthetic();
        return;
    }
    if(NP_USB20_CameraOnly()){
        if(!np_usb_camera_enabled){XE_NP_UsbStream_SetDiagStatus(0xBAD0CA02U);
            XE_NP_UsbStream_StopInternal(XE_NP_USB_DISARM_REASON_CAPTURE_EXPORT_FAULT);return;}
        np_usb_camera_credit=NP_USB_CAMERA_FRAME_BYTES;
        (void)NP_UsbCameraTryTransmit();NP_UsbQueueKick();
        XE_NP_UsbStream_UpdateDiag();return;
    }
    if(xe_np_usb_state.port == NULL)
    {
        XE_NP_UsbStream_UpdateDiag();
        return;
    }
    (void)NP_UsbCameraTryTransmit(); /* foreground only: camera packet copy */
    #if NP_USB_BLOCK_PROBE
{ NpUsbBlockStamp block=NP_UsbBlockBegin(NP_UB_QUEUE,0U,0U,0U);
NP_UsbQueueService();
NP_UsbBlockEnd(block,NP_UB_NO_RESULT,0U); }
#else
NP_UsbQueueService();
#endif
    XE_NP_UsbStream_UpdateDiag();
    return;

    if(xe_np_usb_state.tx_pending != 0U)
    {
        XE_NP_UsbStream_StartPendingPacket();
        XE_NP_UsbStream_UpdateDiag();
        return;
    }

#if (XE_NP_APP_QUAD_USB_WORD_EXPORT != 0U)
    if(xe_np_usb_interleaved_staging_dma.busy || xe_np_usb_quad_word_ready)
    { xe_np_usb_state.packet_assembly_idx = xe_np_usb_interleaved_staging_dma.buffer_index; }
    else
#endif
    if(xe_np_usb_state.packet_assembly_bytes == 0U)
    {
        xe_np_usb_state.packet_assembly_idx =
            (xe_np_usb_state.tx_busy != 0U) ?
                ((xe_np_usb_state.tx_active_idx == 0U) ? 1U : 0U) :
                0U;
    }
    buffer_index = xe_np_usb_state.packet_assembly_idx;
    packet_ptr = xe_np_usb_tx_packet[buffer_index];
    quad_interleaved_capture =
        (XE_NP_App_IsUsbQuadInterleavedCapture() != 0) ? 1U : 0U;
    interleaved_capture = ((XE_NP_App_IsUsbInterleavedCapture() != 0) ||
                           (quad_interleaved_capture != 0U)) ? 1U : 0U;
#if (XE_NP_USB_STREAM_QUAD_ZEROCOPY_ENABLE != 0U)
    if(quad_interleaved_capture != 0U)
    {
        XE_NP_UsbStream_ServiceQuadZeroCopy();
        XE_NP_UsbStream_UpdateDiag();
        return;
    }
#endif
#if (XE_NP_USB_STREAM_QUAD_STAGING_DMA_ENABLE != 0U)
    interleaved_packet_target = (quad_interleaved_capture != 0U) ?
        XE_NP_USB_STREAM_QUAD_STAGING_PACKETS_PER_BATCH :
        XE_NP_USB_STREAM_PACKETS_PER_TRANSFER;
#else
    interleaved_packet_target = XE_NP_USB_STREAM_PACKETS_PER_TRANSFER;
#endif
    interleaved_payload_target =
        interleaved_packet_target * XE_NP_USB_STREAM_PAYLOAD_BYTES;

    if(interleaved_capture != 0U)
    {
#if ((XE_NP_USB_STREAM_INTERLEAVED_STAGING_DMA_ENABLE != 0U) || \
     (XE_NP_USB_STREAM_QUAD_STAGING_DMA_ENABLE != 0U))
        if(xe_np_usb_interleaved_staging_dma.error != 0U)
        {
            XE_NP_UsbStream_SetDiagStatus(0xBADA000AU);
            XE_NP_UsbStream_StopInternal(XE_NP_USB_DISARM_REASON_CAPTURE_EXPORT_FAULT);
            XE_NP_UsbStream_UpdateDiag();
            return;
        }
        if(xe_np_usb_interleaved_staging_dma.busy != 0U)
        {
#if (XE_NP_APP_QUAD_USB_WORD_EXPORT != 0U)
            if((HAL_GetTick() - xe_np_usb_quad_word_started_ms) >= 6U)
            {
                XE_NP_UsbStream_SetDiagStatus(0xBADA0011UL);
                XE_NP_UsbStream_StopInternal(XE_NP_USB_DISARM_REASON_CAPTURE_EXPORT_FAULT);
                XE_NP_UsbStream_UpdateDiag();
                return;
            }
#endif
            /* Channel 14 owns a private SRAM1 destination and a guarded,
             * completed SRAM3 source.  Do not let the foreground touch either
             * range until its DMA completion callback commits the cursor. */
            XE_NP_UsbStream_UpdateDiag();
            return;
        }
#if (XE_NP_APP_QUAD_USB_WORD_EXPORT != 0U)
        if(xe_np_usb_quad_word_ready)
        {
            /* Foreground packetization reads only completed private SRAM1,
             * never the live capture bank. Completion IRQ remains bounded. */
            XE_NP_Capture_DCacheInvalidate(xe_np_usb_quad_word_scratch, sizeof(xe_np_usb_quad_word_scratch));
            for(uint32_t group = 0U; group < XE_NP_USB_STREAM_QUAD_STAGING_GROUPS_PER_BATCH; ++group)
            {
                for(uint32_t lane = 0U; lane < 4U; ++lane)
                {
                    memcpy(&xe_np_usb_tx_packet[buffer_index][(group * 4U + lane) * XE_NP_USB_STREAM_PACKET_BYTES + XE_NP_USB_STREAM_HEADER_BYTES],
                        &xe_np_usb_quad_word_scratch[lane * XE_NP_USB_WORD_LANE_BYTES + group * XE_NP_USB_STREAM_PAYLOAD_BYTES],
                        XE_NP_USB_STREAM_PAYLOAD_BYTES);
                }
            }
            xe_np_usb_state.packet_assembly_bytes = XE_NP_USB_STREAM_QUAD_STAGING_PACKETS_PER_BATCH * XE_NP_USB_STREAM_PAYLOAD_BYTES;
            xe_np_usb_quad_word_ready = 0U;
        }
#endif
        if(xe_np_usb_state.packet_assembly_bytes < interleaved_payload_target)
        {
            if(quad_interleaved_capture != 0U)
            {
                if(xe_np_usb_state.packet_assembly_bytes != 0U)
                {
                    XE_NP_UsbStream_SetDiagStatus(0xBADA000CU);
                    XE_NP_UsbStream_StopInternal(XE_NP_USB_DISARM_REASON_CAPTURE_EXPORT_FAULT);
                    XE_NP_UsbStream_UpdateDiag();
                    return;
                }
                interleaved_dma_status = XE_NP_UsbStream_StartQuadStagingBatch(buffer_index);
                if(interleaved_dma_status < 0)
                {
                    interleaved_export_debug = XE_NP_App_GetUsbInterleavedExportDebug();
                    XE_NP_UsbStream_SetDiagStatus(0xBADA0000UL | interleaved_export_debug);
                    XE_NP_UsbStream_StopInternal(XE_NP_USB_DISARM_REASON_CAPTURE_EXPORT_FAULT);
                }
                XE_NP_UsbStream_UpdateDiag();
                return;
            }
            packet_index = xe_np_usb_state.packet_assembly_bytes /
                           XE_NP_USB_STREAM_PAYLOAD_BYTES;
            remaining_bytes = XE_NP_USB_STREAM_TRANSFER_PAYLOAD_BYTES -
                              xe_np_usb_state.packet_assembly_bytes;
            interleaved_dma_status = XE_NP_App_BeginUsbInterleavedDmaBatch(
                XE_NP_USB_STREAM_PAYLOAD_BYTES,
                ((remaining_bytes / XE_NP_USB_STREAM_PAYLOAD_BYTES) <
                 XE_NP_USB_STREAM_INTERLEAVED_STAGING_PACKETS_PER_BATCH) ?
                    (remaining_bytes / XE_NP_USB_STREAM_PAYLOAD_BYTES) :
                    XE_NP_USB_STREAM_INTERLEAVED_STAGING_PACKETS_PER_BATCH,
                &interleaved_dma_batch);
            if(interleaved_dma_status < 0)
            {
                interleaved_export_debug = XE_NP_App_GetUsbInterleavedExportDebug();
                XE_NP_UsbStream_SetDiagStatus(0xBADA0000UL | interleaved_export_debug);
                XE_NP_UsbStream_StopInternal(XE_NP_USB_DISARM_REASON_CAPTURE_EXPORT_FAULT);
                XE_NP_UsbStream_UpdateDiag();
                return;
            }
            if(interleaved_dma_status == 0)
            {
                interleaved_export_debug = XE_NP_App_GetUsbInterleavedExportDebug();
                XE_NP_UsbStream_SetDiagStatus(0xBADA0000UL | interleaved_export_debug);
                XE_NP_UsbStream_UpdateDiag();
                return;
            }
            if(XE_NP_UsbStream_StartInterleavedStagingBatch(
                   buffer_index,
                   (uint8_t)packet_index,
                   &interleaved_dma_batch) != 0)
            {
                XE_NP_UsbStream_SetDiagStatus(0xBADA000BU);
                XE_NP_UsbStream_StopInternal(XE_NP_USB_DISARM_REASON_CAPTURE_EXPORT_FAULT);
            }
            XE_NP_UsbStream_UpdateDiag();
            return;
        }
#else
        /*
         * Bound each foreground visit to four 506-byte lane packets.  One
         * packet per visit cannot keep up with the application's coarse
         * foreground cadence, while reconstructing a full USB aggregation in
         * one visit creates an unnecessarily long SRAM3 read burst.  This
         * 2,024-byte bound keeps DMA's private SRAM3 lane ownership intact
         * and lets the completed aggregate move only from private SRAM1.
         */
        for(uint32_t packets_this_service = 0U;
            (packets_this_service < XE_NP_USB_STREAM_INTERLEAVED_COPY_PACKETS_PER_SERVICE) &&
            (xe_np_usb_state.packet_assembly_bytes < XE_NP_USB_STREAM_TRANSFER_PAYLOAD_BYTES);
            ++packets_this_service)
        {
            packet_index = xe_np_usb_state.packet_assembly_bytes /
                           XE_NP_USB_STREAM_PAYLOAD_BYTES;
            if(packet_index >= XE_NP_USB_STREAM_PACKETS_PER_TRANSFER)
            {
                XE_NP_UsbStream_SetDiagStatus(0xBAD00003UL);
                XE_NP_UsbStream_UpdateDiag();
                return;
            }
            copied_bytes = (quad_interleaved_capture != 0U) ?
                XE_NP_App_CopyUsbQuadInterleavedLanePayload(
                    &packet_ptr[(packet_index * XE_NP_USB_STREAM_PACKET_BYTES) +
                                XE_NP_USB_STREAM_HEADER_BYTES],
                    XE_NP_USB_STREAM_PAYLOAD_BYTES,
                    &interleaved_lane_b) :
                XE_NP_App_CopyUsbInterleavedLanePayload(
                    &packet_ptr[(packet_index * XE_NP_USB_STREAM_PACKET_BYTES) +
                                XE_NP_USB_STREAM_HEADER_BYTES],
                    XE_NP_USB_STREAM_PAYLOAD_BYTES,
                    &interleaved_lane_b);
            if(copied_bytes == 0U)
            {
                interleaved_export_debug = XE_NP_App_GetUsbInterleavedExportDebug();
                XE_NP_UsbStream_SetDiagStatus(
                    0xBADA0000UL | interleaved_export_debug);
                /* A ring-wrap is unrecoverable for a lossless recording. Stop
                 * rather than silently emitting shifted or reused lane data. */
                if(interleaved_export_debug == 5U)
                {
                    XE_NP_UsbStream_StopInternal(
                        XE_NP_USB_DISARM_REASON_CAPTURE_EXPORT_FAULT);
                }
                XE_NP_UsbStream_UpdateDiag();
                return;
            }
            if(copied_bytes != XE_NP_USB_STREAM_PAYLOAD_BYTES)
            {
                XE_NP_UsbStream_SetDiagStatus(0xBAD00003UL);
                XE_NP_UsbStream_UpdateDiag();
                return;
            }
            XE_NP_UsbStream_WritePacketHeader(
                &packet_ptr[packet_index * XE_NP_USB_STREAM_PACKET_BYTES],
                XE_NP_USB_STREAM_PAYLOAD_BYTES,
                (uint16_t)(xe_np_usb_state.sequence + packet_index),
                NULL,
                0U,
                0U,
                0U,
                0U);
            packet_ptr[(packet_index * XE_NP_USB_STREAM_PACKET_BYTES) + 3U] |=
                XE_NP_USB_STREAM_FLAG_INTERLEAVED_LANES;
            if(quad_interleaved_capture != 0U)
            {
                packet_ptr[(packet_index * XE_NP_USB_STREAM_PACKET_BYTES) + 3U] |=
                    XE_NP_USB_STREAM_FLAG_QUAD_INTERLEAVED_LANES;
            }
            else if(interleaved_lane_b != 0U)
            {
                packet_ptr[(packet_index * XE_NP_USB_STREAM_PACKET_BYTES) + 3U] |=
                    XE_NP_USB_STREAM_FLAG_INTERLEAVED_LANE_B;
            }
            xe_np_usb_state.packet_assembly_bytes = (uint16_t)(
                xe_np_usb_state.packet_assembly_bytes + copied_bytes);
        }
        if(xe_np_usb_state.packet_assembly_bytes < interleaved_payload_target)
        {
            XE_NP_UsbStream_UpdateDiag();
            return;
        }
#endif
    }

    while((interleaved_capture == 0U) &&
          (xe_np_usb_state.packet_assembly_bytes < XE_NP_USB_STREAM_TRANSFER_PAYLOAD_BYTES))
    {
        packet_index = xe_np_usb_state.packet_assembly_bytes / XE_NP_USB_STREAM_PAYLOAD_BYTES;
        packet_payload_offset =
            xe_np_usb_state.packet_assembly_bytes % XE_NP_USB_STREAM_PAYLOAD_BYTES;
        remaining_bytes = XE_NP_USB_STREAM_PAYLOAD_BYTES - packet_payload_offset;
        dequeue_status = XE_NP_App_DequeueUsbCaptureWindowLimited(
            xe_np_usb_state.port_index,
            &source_window,
            remaining_bytes);
        if(dequeue_status <= 0)
        {
            break;
        }
        if((source_window.data == NULL) || (source_window.size_bytes == 0U) ||
           (source_window.size_bytes > remaining_bytes))
        {
            XE_NP_UsbStream_SetDiagStatus(0xBAD00002UL);
            XE_NP_UsbStream_UpdateDiag();
            return;
        }

        XE_NP_Capture_DCacheInvalidate(source_window.data, source_window.size_bytes);
        memcpy(
            &packet_ptr[(packet_index * XE_NP_USB_STREAM_PACKET_BYTES) +
                        XE_NP_USB_STREAM_HEADER_BYTES + packet_payload_offset],
            source_window.data,
            source_window.size_bytes);
        xe_np_usb_state.packet_assembly_bytes =
            (uint16_t)(xe_np_usb_state.packet_assembly_bytes + source_window.size_bytes);
        /* The payload is now in private endpoint memory; release the live
         * capture window immediately rather than making USB DMA a competing
         * reader of capture SRAM. */
        XE_NP_App_ReleaseUsbActiveWindow(xe_np_usb_state.port_index);
    }

    if((interleaved_capture != 0U) &&
       (xe_np_usb_state.packet_assembly_bytes < interleaved_payload_target))
    {
        XE_NP_UsbStream_UpdateDiag();
        return;
    }
    if((interleaved_capture == 0U) &&
       (xe_np_usb_state.packet_assembly_bytes < XE_NP_USB_STREAM_TRANSFER_PAYLOAD_BYTES))
    {
        XE_NP_UsbStream_UpdateDiag();
        return;
    }

#if (XE_NP_USB_STREAM_REAL_EXPORT_SYNTHETIC_PACKET_ENABLE != 0U)
    /* Keep the real paired producer and completion lifecycle, but replace
     * only the final endpoint payload. This isolates endpoint data/cache
     * handling from capture-window scheduling. */
    XE_NP_UsbStream_FillSyntheticPayload(buffer_index);
#endif
    for(packet_index = 0U;
        (interleaved_capture == 0U) &&
        (packet_index < XE_NP_USB_STREAM_PACKETS_PER_TRANSFER);
        ++packet_index)
    {
        XE_NP_UsbStream_WritePacketHeader(
            &packet_ptr[packet_index * XE_NP_USB_STREAM_PACKET_BYTES],
            XE_NP_USB_STREAM_PAYLOAD_BYTES,
            (uint16_t)(xe_np_usb_state.sequence + packet_index),
            NULL,
            0U,
            0U,
            0U,
            0U);
    }
    xe_np_usb_state.tx_payload_ptr[buffer_index] = packet_ptr;
    xe_np_usb_state.tx_len[buffer_index] =
        ((interleaved_capture != 0U) ? interleaved_packet_target :
         XE_NP_USB_STREAM_PACKETS_PER_TRANSFER) * XE_NP_USB_STREAM_PACKET_BYTES;
    xe_np_usb_state.tx_payload_bytes[buffer_index] =
        (interleaved_capture != 0U) ? interleaved_payload_target :
        XE_NP_USB_STREAM_TRANSFER_PAYLOAD_BYTES;
    xe_np_usb_state.tx_split_payload[buffer_index] = 0U;
    xe_np_usb_state.packet_assembly_bytes = 0U;

    if(xe_np_usb_state.tx_busy != 0U)
    {
        xe_np_usb_state.tx_pending_idx = buffer_index;
        xe_np_usb_state.tx_pending = 1U;
        XE_NP_UsbStream_SetDiagStatus(0U);
    }
    else
    {
        (void)XE_NP_UsbStream_StartPreparedPacket(buffer_index);
    }

    XE_NP_UsbStream_UpdateDiag();
}
static void __attribute__((noinline)) NP_V1UsbOriginalService(void)
{
 NpUsbBlockStamp block=NP_UsbBlockBegin(NP_UB_ORIGINAL,0U,0U,0U);
 NP_V1UsbOriginalService_BlockBody();
 NP_UsbBlockEnd(block,NP_UB_NO_RESULT,0U);
}
#else
static void __attribute__((noinline)) NP_V1UsbOriginalService(void)
{
    if(!NP_USB_IsReady())return;
    #if NP_USB_BLOCK_PROBE
{ NpUsbBlockStamp block=NP_UsbBlockBegin(NP_UB_FORMAT,0U,0U,0U);
 int result=np_usb_format_service();
 NP_UsbBlockEnd(block,(uint32_t)result,0U);
 if(result)return; }
#else
if(np_usb_format_service())return;
#endif
    #if NP_USB_BLOCK_PROBE
{ NpUsbBlockStamp block=NP_UsbBlockBegin(NP_UB_MODE,0U,0U,0U);
np_v1_mode_service();
NP_UsbBlockEnd(block,NP_UB_NO_RESULT,0U); }
#else
np_v1_mode_service();
#endif
#if NP_HARDWARE_RTC_ENABLE
    #if NP_USB_BLOCK_PROBE
{ NpUsbBlockStamp block=NP_UsbBlockBegin(NP_UB_RTC,0U,0U,0U);
NP_V1RtcPortService();
NP_UsbBlockEnd(block,NP_UB_NO_RESULT,0U); }
#else
NP_V1RtcPortService();
#endif
#endif
    #if NP_USB_BLOCK_PROBE
{ NpUsbBlockStamp block=NP_UsbBlockBegin(NP_UB_CONFIG,0U,0U,0U);
NP_ConfigService();
NP_UsbBlockEnd(block,NP_UB_NO_RESULT,0U); }
#else
NP_ConfigService();
#endif /* Also available in idle download mode before any START. */
    if(NP_USB_IsDownload()){
        if(np_usb_sd_prepare_pending){NP_USB_PrepareDownload();return;}
        if(np_v1_sd_reset_service())return;NP_SD_UsbService();return;
    }
    NP_UsbCameraService();
    #if NP_USB_BLOCK_PROBE
{ NpUsbBlockStamp block=NP_UsbBlockBegin(NP_UB_SENSORS,0U,0U,0U);
NP_UsbSensorsService();
NP_UsbBlockEnd(block,NP_UB_NO_RESULT,0U); }
#else
NP_UsbSensorsService();
#endif
    if(np_mode_stop_pending) {
        np_mode_stop_pending=0U;
        XE_NP_App_ResetUsbWordSession();
    }
    uint8_t *packet_ptr;
    XE_NP_AppUsbCaptureWindow source_window;
    uint32_t remaining_bytes;
    uint32_t packet_index;
    uint32_t packet_payload_offset;
    uint32_t copied_bytes;
    uint32_t interleaved_export_debug;
    int dequeue_status;
    uint8_t interleaved_capture;
    uint8_t interleaved_lane_b;
    uint8_t quad_interleaved_capture;
    uint8_t buffer_index;
    XE_NP_AppUsbInterleavedDmaBatch interleaved_dma_batch;
    int interleaved_dma_status;
    uint32_t interleaved_packet_target;
    uint32_t interleaved_payload_target;

    if((xe_np_usb_state.configured == 0U) || (xe_np_usb_state.hpcd == NULL))
    {
        XE_NP_UsbStream_UpdateDiag();
        return;
    }

    if((xe_np_usb_state.streaming == 0U) &&
       (xe_np_usb_state.real_start_pending == 1U))
    {
        NP_UsbCameraPrepareStart();
        if(NP_USB20_CameraOnly())NP_UsbCameraOnlyArm();
        else (void)XE_NP_UsbStream_TryStartReal(xe_np_usb_state.real_start_port_index);
        XE_NP_UsbStream_UpdateDiag();
        return;
    }

    if(xe_np_usb_state.streaming == 0U)
    {
        XE_NP_UsbStream_UpdateDiag();
        return;
    }
    if(xe_np_usb_state.synthetic_source != 0U)
    {
        XE_NP_UsbStream_ServiceSynthetic();
        return;
    }
    if(NP_USB20_CameraOnly()){
        if(!np_usb_camera_enabled){XE_NP_UsbStream_SetDiagStatus(0xBAD0CA02U);
            XE_NP_UsbStream_StopInternal(XE_NP_USB_DISARM_REASON_CAPTURE_EXPORT_FAULT);return;}
        np_usb_camera_credit=NP_USB_CAMERA_FRAME_BYTES;
        (void)NP_UsbCameraTryTransmit();NP_UsbQueueKick();
        XE_NP_UsbStream_UpdateDiag();return;
    }
    if(xe_np_usb_state.port == NULL)
    {
        XE_NP_UsbStream_UpdateDiag();
        return;
    }
    (void)NP_UsbCameraTryTransmit(); /* foreground only: camera packet copy */
    #if NP_USB_BLOCK_PROBE
{ NpUsbBlockStamp block=NP_UsbBlockBegin(NP_UB_QUEUE,0U,0U,0U);
NP_UsbQueueService();
NP_UsbBlockEnd(block,NP_UB_NO_RESULT,0U); }
#else
NP_UsbQueueService();
#endif
    XE_NP_UsbStream_UpdateDiag();
    return;

    if(xe_np_usb_state.tx_pending != 0U)
    {
        XE_NP_UsbStream_StartPendingPacket();
        XE_NP_UsbStream_UpdateDiag();
        return;
    }

#if (XE_NP_APP_QUAD_USB_WORD_EXPORT != 0U)
    if(xe_np_usb_interleaved_staging_dma.busy || xe_np_usb_quad_word_ready)
    { xe_np_usb_state.packet_assembly_idx = xe_np_usb_interleaved_staging_dma.buffer_index; }
    else
#endif
    if(xe_np_usb_state.packet_assembly_bytes == 0U)
    {
        xe_np_usb_state.packet_assembly_idx =
            (xe_np_usb_state.tx_busy != 0U) ?
                ((xe_np_usb_state.tx_active_idx == 0U) ? 1U : 0U) :
                0U;
    }
    buffer_index = xe_np_usb_state.packet_assembly_idx;
    packet_ptr = xe_np_usb_tx_packet[buffer_index];
    quad_interleaved_capture =
        (XE_NP_App_IsUsbQuadInterleavedCapture() != 0) ? 1U : 0U;
    interleaved_capture = ((XE_NP_App_IsUsbInterleavedCapture() != 0) ||
                           (quad_interleaved_capture != 0U)) ? 1U : 0U;
#if (XE_NP_USB_STREAM_QUAD_ZEROCOPY_ENABLE != 0U)
    if(quad_interleaved_capture != 0U)
    {
        XE_NP_UsbStream_ServiceQuadZeroCopy();
        XE_NP_UsbStream_UpdateDiag();
        return;
    }
#endif
#if (XE_NP_USB_STREAM_QUAD_STAGING_DMA_ENABLE != 0U)
    interleaved_packet_target = (quad_interleaved_capture != 0U) ?
        XE_NP_USB_STREAM_QUAD_STAGING_PACKETS_PER_BATCH :
        XE_NP_USB_STREAM_PACKETS_PER_TRANSFER;
#else
    interleaved_packet_target = XE_NP_USB_STREAM_PACKETS_PER_TRANSFER;
#endif
    interleaved_payload_target =
        interleaved_packet_target * XE_NP_USB_STREAM_PAYLOAD_BYTES;

    if(interleaved_capture != 0U)
    {
#if ((XE_NP_USB_STREAM_INTERLEAVED_STAGING_DMA_ENABLE != 0U) || \
     (XE_NP_USB_STREAM_QUAD_STAGING_DMA_ENABLE != 0U))
        if(xe_np_usb_interleaved_staging_dma.error != 0U)
        {
            XE_NP_UsbStream_SetDiagStatus(0xBADA000AU);
            XE_NP_UsbStream_StopInternal(XE_NP_USB_DISARM_REASON_CAPTURE_EXPORT_FAULT);
            XE_NP_UsbStream_UpdateDiag();
            return;
        }
        if(xe_np_usb_interleaved_staging_dma.busy != 0U)
        {
#if (XE_NP_APP_QUAD_USB_WORD_EXPORT != 0U)
            if((HAL_GetTick() - xe_np_usb_quad_word_started_ms) >= 6U)
            {
                XE_NP_UsbStream_SetDiagStatus(0xBADA0011UL);
                XE_NP_UsbStream_StopInternal(XE_NP_USB_DISARM_REASON_CAPTURE_EXPORT_FAULT);
                XE_NP_UsbStream_UpdateDiag();
                return;
            }
#endif
            /* Channel 14 owns a private SRAM1 destination and a guarded,
             * completed SRAM3 source.  Do not let the foreground touch either
             * range until its DMA completion callback commits the cursor. */
            XE_NP_UsbStream_UpdateDiag();
            return;
        }
#if (XE_NP_APP_QUAD_USB_WORD_EXPORT != 0U)
        if(xe_np_usb_quad_word_ready)
        {
            /* Foreground packetization reads only completed private SRAM1,
             * never the live capture bank. Completion IRQ remains bounded. */
            XE_NP_Capture_DCacheInvalidate(xe_np_usb_quad_word_scratch, sizeof(xe_np_usb_quad_word_scratch));
            for(uint32_t group = 0U; group < XE_NP_USB_STREAM_QUAD_STAGING_GROUPS_PER_BATCH; ++group)
            {
                for(uint32_t lane = 0U; lane < 4U; ++lane)
                {
                    memcpy(&xe_np_usb_tx_packet[buffer_index][(group * 4U + lane) * XE_NP_USB_STREAM_PACKET_BYTES + XE_NP_USB_STREAM_HEADER_BYTES],
                        &xe_np_usb_quad_word_scratch[lane * XE_NP_USB_WORD_LANE_BYTES + group * XE_NP_USB_STREAM_PAYLOAD_BYTES],
                        XE_NP_USB_STREAM_PAYLOAD_BYTES);
                }
            }
            xe_np_usb_state.packet_assembly_bytes = XE_NP_USB_STREAM_QUAD_STAGING_PACKETS_PER_BATCH * XE_NP_USB_STREAM_PAYLOAD_BYTES;
            xe_np_usb_quad_word_ready = 0U;
        }
#endif
        if(xe_np_usb_state.packet_assembly_bytes < interleaved_payload_target)
        {
            if(quad_interleaved_capture != 0U)
            {
                if(xe_np_usb_state.packet_assembly_bytes != 0U)
                {
                    XE_NP_UsbStream_SetDiagStatus(0xBADA000CU);
                    XE_NP_UsbStream_StopInternal(XE_NP_USB_DISARM_REASON_CAPTURE_EXPORT_FAULT);
                    XE_NP_UsbStream_UpdateDiag();
                    return;
                }
                interleaved_dma_status = XE_NP_UsbStream_StartQuadStagingBatch(buffer_index);
                if(interleaved_dma_status < 0)
                {
                    interleaved_export_debug = XE_NP_App_GetUsbInterleavedExportDebug();
                    XE_NP_UsbStream_SetDiagStatus(0xBADA0000UL | interleaved_export_debug);
                    XE_NP_UsbStream_StopInternal(XE_NP_USB_DISARM_REASON_CAPTURE_EXPORT_FAULT);
                }
                XE_NP_UsbStream_UpdateDiag();
                return;
            }
            packet_index = xe_np_usb_state.packet_assembly_bytes /
                           XE_NP_USB_STREAM_PAYLOAD_BYTES;
            remaining_bytes = XE_NP_USB_STREAM_TRANSFER_PAYLOAD_BYTES -
                              xe_np_usb_state.packet_assembly_bytes;
            interleaved_dma_status = XE_NP_App_BeginUsbInterleavedDmaBatch(
                XE_NP_USB_STREAM_PAYLOAD_BYTES,
                ((remaining_bytes / XE_NP_USB_STREAM_PAYLOAD_BYTES) <
                 XE_NP_USB_STREAM_INTERLEAVED_STAGING_PACKETS_PER_BATCH) ?
                    (remaining_bytes / XE_NP_USB_STREAM_PAYLOAD_BYTES) :
                    XE_NP_USB_STREAM_INTERLEAVED_STAGING_PACKETS_PER_BATCH,
                &interleaved_dma_batch);
            if(interleaved_dma_status < 0)
            {
                interleaved_export_debug = XE_NP_App_GetUsbInterleavedExportDebug();
                XE_NP_UsbStream_SetDiagStatus(0xBADA0000UL | interleaved_export_debug);
                XE_NP_UsbStream_StopInternal(XE_NP_USB_DISARM_REASON_CAPTURE_EXPORT_FAULT);
                XE_NP_UsbStream_UpdateDiag();
                return;
            }
            if(interleaved_dma_status == 0)
            {
                interleaved_export_debug = XE_NP_App_GetUsbInterleavedExportDebug();
                XE_NP_UsbStream_SetDiagStatus(0xBADA0000UL | interleaved_export_debug);
                XE_NP_UsbStream_UpdateDiag();
                return;
            }
            if(XE_NP_UsbStream_StartInterleavedStagingBatch(
                   buffer_index,
                   (uint8_t)packet_index,
                   &interleaved_dma_batch) != 0)
            {
                XE_NP_UsbStream_SetDiagStatus(0xBADA000BU);
                XE_NP_UsbStream_StopInternal(XE_NP_USB_DISARM_REASON_CAPTURE_EXPORT_FAULT);
            }
            XE_NP_UsbStream_UpdateDiag();
            return;
        }
#else
        /*
         * Bound each foreground visit to four 506-byte lane packets.  One
         * packet per visit cannot keep up with the application's coarse
         * foreground cadence, while reconstructing a full USB aggregation in
         * one visit creates an unnecessarily long SRAM3 read burst.  This
         * 2,024-byte bound keeps DMA's private SRAM3 lane ownership intact
         * and lets the completed aggregate move only from private SRAM1.
         */
        for(uint32_t packets_this_service = 0U;
            (packets_this_service < XE_NP_USB_STREAM_INTERLEAVED_COPY_PACKETS_PER_SERVICE) &&
            (xe_np_usb_state.packet_assembly_bytes < XE_NP_USB_STREAM_TRANSFER_PAYLOAD_BYTES);
            ++packets_this_service)
        {
            packet_index = xe_np_usb_state.packet_assembly_bytes /
                           XE_NP_USB_STREAM_PAYLOAD_BYTES;
            if(packet_index >= XE_NP_USB_STREAM_PACKETS_PER_TRANSFER)
            {
                XE_NP_UsbStream_SetDiagStatus(0xBAD00003UL);
                XE_NP_UsbStream_UpdateDiag();
                return;
            }
            copied_bytes = (quad_interleaved_capture != 0U) ?
                XE_NP_App_CopyUsbQuadInterleavedLanePayload(
                    &packet_ptr[(packet_index * XE_NP_USB_STREAM_PACKET_BYTES) +
                                XE_NP_USB_STREAM_HEADER_BYTES],
                    XE_NP_USB_STREAM_PAYLOAD_BYTES,
                    &interleaved_lane_b) :
                XE_NP_App_CopyUsbInterleavedLanePayload(
                    &packet_ptr[(packet_index * XE_NP_USB_STREAM_PACKET_BYTES) +
                                XE_NP_USB_STREAM_HEADER_BYTES],
                    XE_NP_USB_STREAM_PAYLOAD_BYTES,
                    &interleaved_lane_b);
            if(copied_bytes == 0U)
            {
                interleaved_export_debug = XE_NP_App_GetUsbInterleavedExportDebug();
                XE_NP_UsbStream_SetDiagStatus(
                    0xBADA0000UL | interleaved_export_debug);
                /* A ring-wrap is unrecoverable for a lossless recording. Stop
                 * rather than silently emitting shifted or reused lane data. */
                if(interleaved_export_debug == 5U)
                {
                    XE_NP_UsbStream_StopInternal(
                        XE_NP_USB_DISARM_REASON_CAPTURE_EXPORT_FAULT);
                }
                XE_NP_UsbStream_UpdateDiag();
                return;
            }
            if(copied_bytes != XE_NP_USB_STREAM_PAYLOAD_BYTES)
            {
                XE_NP_UsbStream_SetDiagStatus(0xBAD00003UL);
                XE_NP_UsbStream_UpdateDiag();
                return;
            }
            XE_NP_UsbStream_WritePacketHeader(
                &packet_ptr[packet_index * XE_NP_USB_STREAM_PACKET_BYTES],
                XE_NP_USB_STREAM_PAYLOAD_BYTES,
                (uint16_t)(xe_np_usb_state.sequence + packet_index),
                NULL,
                0U,
                0U,
                0U,
                0U);
            packet_ptr[(packet_index * XE_NP_USB_STREAM_PACKET_BYTES) + 3U] |=
                XE_NP_USB_STREAM_FLAG_INTERLEAVED_LANES;
            if(quad_interleaved_capture != 0U)
            {
                packet_ptr[(packet_index * XE_NP_USB_STREAM_PACKET_BYTES) + 3U] |=
                    XE_NP_USB_STREAM_FLAG_QUAD_INTERLEAVED_LANES;
            }
            else if(interleaved_lane_b != 0U)
            {
                packet_ptr[(packet_index * XE_NP_USB_STREAM_PACKET_BYTES) + 3U] |=
                    XE_NP_USB_STREAM_FLAG_INTERLEAVED_LANE_B;
            }
            xe_np_usb_state.packet_assembly_bytes = (uint16_t)(
                xe_np_usb_state.packet_assembly_bytes + copied_bytes);
        }
        if(xe_np_usb_state.packet_assembly_bytes < interleaved_payload_target)
        {
            XE_NP_UsbStream_UpdateDiag();
            return;
        }
#endif
    }

    while((interleaved_capture == 0U) &&
          (xe_np_usb_state.packet_assembly_bytes < XE_NP_USB_STREAM_TRANSFER_PAYLOAD_BYTES))
    {
        packet_index = xe_np_usb_state.packet_assembly_bytes / XE_NP_USB_STREAM_PAYLOAD_BYTES;
        packet_payload_offset =
            xe_np_usb_state.packet_assembly_bytes % XE_NP_USB_STREAM_PAYLOAD_BYTES;
        remaining_bytes = XE_NP_USB_STREAM_PAYLOAD_BYTES - packet_payload_offset;
        dequeue_status = XE_NP_App_DequeueUsbCaptureWindowLimited(
            xe_np_usb_state.port_index,
            &source_window,
            remaining_bytes);
        if(dequeue_status <= 0)
        {
            break;
        }
        if((source_window.data == NULL) || (source_window.size_bytes == 0U) ||
           (source_window.size_bytes > remaining_bytes))
        {
            XE_NP_UsbStream_SetDiagStatus(0xBAD00002UL);
            XE_NP_UsbStream_UpdateDiag();
            return;
        }

        XE_NP_Capture_DCacheInvalidate(source_window.data, source_window.size_bytes);
        memcpy(
            &packet_ptr[(packet_index * XE_NP_USB_STREAM_PACKET_BYTES) +
                        XE_NP_USB_STREAM_HEADER_BYTES + packet_payload_offset],
            source_window.data,
            source_window.size_bytes);
        xe_np_usb_state.packet_assembly_bytes =
            (uint16_t)(xe_np_usb_state.packet_assembly_bytes + source_window.size_bytes);
        /* The payload is now in private endpoint memory; release the live
         * capture window immediately rather than making USB DMA a competing
         * reader of capture SRAM. */
        XE_NP_App_ReleaseUsbActiveWindow(xe_np_usb_state.port_index);
    }

    if((interleaved_capture != 0U) &&
       (xe_np_usb_state.packet_assembly_bytes < interleaved_payload_target))
    {
        XE_NP_UsbStream_UpdateDiag();
        return;
    }
    if((interleaved_capture == 0U) &&
       (xe_np_usb_state.packet_assembly_bytes < XE_NP_USB_STREAM_TRANSFER_PAYLOAD_BYTES))
    {
        XE_NP_UsbStream_UpdateDiag();
        return;
    }

#if (XE_NP_USB_STREAM_REAL_EXPORT_SYNTHETIC_PACKET_ENABLE != 0U)
    /* Keep the real paired producer and completion lifecycle, but replace
     * only the final endpoint payload. This isolates endpoint data/cache
     * handling from capture-window scheduling. */
    XE_NP_UsbStream_FillSyntheticPayload(buffer_index);
#endif
    for(packet_index = 0U;
        (interleaved_capture == 0U) &&
        (packet_index < XE_NP_USB_STREAM_PACKETS_PER_TRANSFER);
        ++packet_index)
    {
        XE_NP_UsbStream_WritePacketHeader(
            &packet_ptr[packet_index * XE_NP_USB_STREAM_PACKET_BYTES],
            XE_NP_USB_STREAM_PAYLOAD_BYTES,
            (uint16_t)(xe_np_usb_state.sequence + packet_index),
            NULL,
            0U,
            0U,
            0U,
            0U);
    }
    xe_np_usb_state.tx_payload_ptr[buffer_index] = packet_ptr;
    xe_np_usb_state.tx_len[buffer_index] =
        ((interleaved_capture != 0U) ? interleaved_packet_target :
         XE_NP_USB_STREAM_PACKETS_PER_TRANSFER) * XE_NP_USB_STREAM_PACKET_BYTES;
    xe_np_usb_state.tx_payload_bytes[buffer_index] =
        (interleaved_capture != 0U) ? interleaved_payload_target :
        XE_NP_USB_STREAM_TRANSFER_PAYLOAD_BYTES;
    xe_np_usb_state.tx_split_payload[buffer_index] = 0U;
    xe_np_usb_state.packet_assembly_bytes = 0U;

    if(xe_np_usb_state.tx_busy != 0U)
    {
        xe_np_usb_state.tx_pending_idx = buffer_index;
        xe_np_usb_state.tx_pending = 1U;
        XE_NP_UsbStream_SetDiagStatus(0U);
    }
    else
    {
        (void)XE_NP_UsbStream_StartPreparedPacket(buffer_index);
    }

    XE_NP_UsbStream_UpdateDiag();
}
#endif

void XE_NP_UsbStream_Init(PCD_HandleTypeDef *hpcd)
{
    memset(&np_usb_auto_sd,0,sizeof(np_usb_auto_sd));
    np_usb_auto_sd.attached_ms=HAL_GetTick();
    memset(&xe_np_usb_state, 0, sizeof(xe_np_usb_state));
    memset(&xe_np_usb_interleaved_staging_dma, 0,
           sizeof(xe_np_usb_interleaved_staging_dma));
    xe_np_usb_state.hpcd = hpcd;
    xe_np_usb_state.port_index = 0U;
    HAL_GPIO_WritePin(USB_MODE_GPIO_Port, USB_MODE_Pin, GPIO_PIN_RESET);

    if(hpcd != NULL)
    {
        /*
         * The OTG HS core has 1024 FIFO words, but internal-DMA bulk IN is
         * not reliable when all of them are allocated.  Keep the same proven
         * 192-word safety margin as the USB512 WinUSB pipeline: 320 RX words,
         * 64 for EP0, and 448 for the continuous bulk-IN endpoint.
         */
        (void)HAL_PCDEx_SetRxFiFo(hpcd, 0x140U);
        (void)HAL_PCDEx_SetTxFiFo(hpcd, 0U, 0x40U);
        (void)HAL_PCDEx_SetTxFiFo(hpcd, 1U, 0x1C0U);
        (void)HAL_PCD_Start(hpcd);
    }

    XE_NP_UsbStream_UpdateDiag();
}

const volatile XE_NP_UsbStreamDiag *XE_NP_UsbStream_GetDiag(void)
{
    return &xe_np_usb_stream_diag;
}

int XE_NP_UsbStream_IsStreaming(void)
{
    return (xe_np_usb_state.streaming != 0U) ? 1 : 0;
}

int XE_NP_UsbStream_IsConfigured(void)
{
    return (xe_np_usb_state.configured != 0U) ? 1 : 0;
}

int XE_NP_UsbStream_IsSyntheticActive(void)
{
    return ((xe_np_usb_state.streaming != 0U) &&
            (xe_np_usb_state.synthetic_source != 0U)) ? 1 : 0;
}

void HAL_PCD_ResetCallback(PCD_HandleTypeDef *hpcd)
{
    if(hpcd==xe_np_usb_state.hpcd){NP_UsbGridBusReset();npc_usb_receiving=0;npc_usb_reply_inflight=0;NP_ConfigDisconnect(NPC_USB);}
    if(hpcd==xe_np_usb_state.hpcd && NP_USB_IsDownload())NP_SD_UsbCancel();
    if(hpcd==xe_np_usb_state.hpcd)np_usb_sd_job_cancel();
    NP_USB20_CancelSelection();np_v1_mode_bus_event();
    if(hpcd != xe_np_usb_state.hpcd)
    {
        return;
    }

    NP_UsbControlRejectReset();
    XE_NP_UsbStream_Disarm(XE_NP_USB_DISARM_REASON_USB_RESET);
    xe_np_usb_state.configured = 0U;
    XE_NP_UsbStream_ClearCtlInState();
    xe_np_usb_state.ep0_status_out_pending = 0U;
    xe_np_usb_state.ep0_address_pending = 0U;
    xe_np_usb_state.ep0_pending_address = 0U;
    xe_np_usb_stream_diag.reset_count++;
    XE_NP_UsbStream_OpenControlEndpoints(hpcd);
    XE_NP_UsbStream_UpdateDiag();
}

void HAL_PCD_DisconnectCallback(PCD_HandleTypeDef *hpcd)
{
    if(hpcd==xe_np_usb_state.hpcd){NP_UsbGridBusReset();npc_usb_receiving=0;npc_usb_reply_inflight=0;NP_ConfigDisconnect(NPC_USB);}
    if(hpcd==xe_np_usb_state.hpcd && NP_USB_IsDownload())NP_SD_UsbCancel();
    if(hpcd==xe_np_usb_state.hpcd)np_usb_sd_job_cancel();
    NP_USB20_CancelSelection();np_v1_mode_bus_event();
    if(hpcd == xe_np_usb_state.hpcd)
    {
        XE_NP_UsbStream_StopInternal(XE_NP_USB_DISARM_REASON_DISCONNECT);
    }
}

void HAL_PCD_SuspendCallback(PCD_HandleTypeDef *hpcd)
{
    if(hpcd==xe_np_usb_state.hpcd){NP_UsbGridBusReset();npc_usb_receiving=0;npc_usb_reply_inflight=0;NP_ConfigDisconnect(NPC_USB);}
    if(hpcd==xe_np_usb_state.hpcd && NP_USB_IsDownload())NP_SD_UsbCancel();
    if(hpcd==xe_np_usb_state.hpcd)np_usb_sd_job_cancel();
    NP_USB20_CancelSelection();np_v1_mode_bus_event();
    if(hpcd == xe_np_usb_state.hpcd)
    {
        XE_NP_UsbStream_StopInternal(XE_NP_USB_DISARM_REASON_SUSPEND);
    }
}

void HAL_PCD_SetupStageCallback(PCD_HandleTypeDef *hpcd)
{
    XE_NP_UsbSetup req;

    if(hpcd != xe_np_usb_state.hpcd)
    {
        return;
    }

    if(hpcd->Init.dma_enable != 0U)
    {
        XE_NP_Capture_DCacheInvalidate((const void *)hpcd->Setup, 8U);
    }

    XE_NP_UsbStream_ParseSetup(hpcd, &req); /* Preserve before old DMA SETUP rearm. */
    NP_UsbStartAckDrain(hpcd);
    np_usb_sd_job_cancel();
    NP_UsbStartAckCancelWait();
    if(hpcd==xe_np_usb_state.hpcd)NP_UsbTimingSetupCancel();
    if(!np_v1_mode.acknowledged){NP_USB20_CancelSelection();np_v1_mode_cancel(&np_v1_mode);}
    npc_usb_receiving=0;npc_usb_reply_inflight=0;np_usb_format_receiving=0;
    xe_np_usb_stream_diag.setup_count++;
    xe_np_usb_stream_diag.last_setup =
        (uint32_t)req.bmRequest |
        ((uint32_t)req.bRequest << 8U) |
        ((uint32_t)req.wValue << 16U);
    xe_np_usb_stream_diag.last_request =
        (uint32_t)req.wIndex | ((uint32_t)req.wLength << 16U);

    if((req.bmRequest & 0x60U) == 0x00U)
    {
        XE_NP_UsbStream_HandleStandardRequest(hpcd, &req);
    }
    else if((req.bmRequest & 0x60U) == 0x40U)
    {
        XE_NP_UsbStream_HandleVendorRequest(hpcd, &req);
    }
    else
    {
        XE_NP_UsbStream_Stall(hpcd);
    }

    XE_NP_UsbStream_UpdateDiag();
}

void HAL_PCD_DataInStageCallback(PCD_HandleTypeDef *hpcd, uint8_t epnum)
{
    if(hpcd==xe_np_usb_state.hpcd && epnum==0 && npc_usb_reply_inflight){npc_usb_reply_inflight=0;NP_ConfigReplyConsumed(NPC_USB);}
    if(epnum==1 && hpcd==xe_np_usb_state.hpcd && NP_USB_IsDownload()){NP_SD_UsbComplete();xe_np_usb_state.tx_busy=0;return;}
    np_usb_sd_job_in_ack(hpcd,epnum);
    if(hpcd==xe_np_usb_state.hpcd && epnum==0)np_v1_mode_ack(&np_v1_mode);
    if(hpcd != xe_np_usb_state.hpcd)
    {
        return;
    }

    if(epnum == 0U)
    {
        np_usb_start_ack(hpcd, epnum); /* No dispatch in the bulk callback. */
        xe_np_usb_stream_diag.ep0_in_count++;
        if((xe_np_usb_state.ep0_tx_sent < xe_np_usb_state.ep0_tx_len) ||
           (xe_np_usb_state.ep0_zlp_pending != 0U))
        {
            (void)XE_NP_UsbStream_CtlSendNext(hpcd);
            XE_NP_UsbStream_UpdateDiag();
            return;
        }

        XE_NP_UsbStream_ClearCtlInState();
        if(xe_np_usb_state.ep0_address_pending != 0U)
        {
            (void)HAL_PCD_SetAddress(hpcd, xe_np_usb_state.ep0_pending_address);
            xe_np_usb_state.ep0_address_pending = 0U;
        }

        if(xe_np_usb_state.ep0_status_out_pending != 0U)
        {
            xe_np_usb_state.ep0_status_out_pending = 0U;
            if(HAL_PCD_EP_Receive(hpcd, 0x00U, xe_np_usb_ep0_status_out, 0U) != HAL_OK)
            {
                XE_NP_UsbStream_SetDiagStatus(0xBAD00004UL);
            }
        }
        else
        {
            XE_NP_UsbStream_PrepareSetup(hpcd);
        }
        XE_NP_UsbStream_UpdateDiag();
        return;
    }

    if(epnum == (XE_NP_USB_STREAM_IN_EP & 0x7FU))
    {
#if (XE_NP_USB_STREAM_QUAD_ZEROCOPY_ENABLE != 0U)
        if(xe_np_usb_state.quad_zero_copy_active != 0U)
        {
            if((xe_np_usb_state.quad_zero_copy_lane + 1U) >=
               XE_NP_APP_USB_QUAD_DMA_LANE_COUNT)
            {
                if(XE_NP_App_CommitUsbQuadDmaBatch(
                       &xe_np_usb_state.quad_zero_copy_batch) != 0)
                {
                    XE_NP_UsbStream_SetDiagStatus(0xBADA0011UL);
                    XE_NP_UsbStream_StopInternal(
                        XE_NP_USB_DISARM_REASON_CAPTURE_EXPORT_FAULT);
                }
                xe_np_usb_state.quad_zero_copy_active = 0U;
            }
            else
            {
                ++xe_np_usb_state.quad_zero_copy_lane;
            }
            xe_np_usb_state.tx_busy = 0U;
            xe_np_usb_state.tx_stage = 0U;
            XE_NP_UsbStream_UpdateDiag();
            return;
        }
#endif
        if(NP_UsbCameraTxActive() != 0U)
        {
            NP_UsbCameraOnTxComplete();
            xe_np_usb_state.tx_busy = 0U;
            xe_np_usb_state.tx_stage = 0U;
            NP_UsbQueueKick();
            XE_NP_UsbStream_UpdateDiag();
            return;
        }
        if(xe_np_usb_state.tx_stage == 1U)
        {
            if(XE_NP_UsbStream_StartPreparedPayload(xe_np_usb_state.tx_active_idx) != HAL_OK)
            {
                XE_NP_UsbStream_ReleaseCompletedCaptureWindow();
                xe_np_usb_state.tx_busy = 0U;
            }
            XE_NP_UsbStream_UpdateDiag();
            return;
        }

        if(xe_np_usb_state.streaming && !xe_np_usb_state.synthetic_source)
        {
            if(!xe_np_usb_state.tx_busy || np_usb_queue_produced == np_usb_queue_consumed ||
               xe_np_usb_state.tx_active_idx != np_usb_queue_read)
            {
                np_usb_queue_diag[5]++;
                XE_NP_UsbStream_SetDiagStatus(0xBADA0014UL);
                XE_NP_UsbStream_StopInternal(XE_NP_USB_DISARM_REASON_CAPTURE_EXPORT_FAULT);
                return;
            }
            __DMB(); /* USB has completed before the slot can be reused. */
            NP_UsbCameraCreditNpTransfer(
                xe_np_usb_state.tx_payload_bytes[xe_np_usb_state.tx_active_idx]);
            ++np_usb_queue_consumed;
            np_usb_queue_read = (uint8_t)((np_usb_queue_read+1U) % XE_NP_USB_STREAM_TX_BUFFER_COUNT);
            np_usb_queue_diag[2] = np_usb_queue_consumed;
        }
        XE_NP_UsbStream_ReleaseCompletedCaptureWindow();
        xe_np_usb_state.tx_busy = 0U;
        xe_np_usb_state.tx_stage = 0U;
        XE_NP_UsbStream_StartPendingPacket();
        XE_NP_UsbStream_UpdateDiag();
    }
}

void HAL_PCD_DataOutStageCallback(PCD_HandleTypeDef *hpcd, uint8_t epnum)
{
    if(hpcd==xe_np_usb_state.hpcd && epnum==0U && np_usb_sd_job_out(hpcd))return;
    if(hpcd==xe_np_usb_state.hpcd && epnum==0U && NP_UsbTimingControlOut(hpcd))return;
    if(hpcd==xe_np_usb_state.hpcd && epnum==0 && np_usb_format_out(hpcd))return;
    if(hpcd==xe_np_usb_state.hpcd && epnum==0 && npc_usb_out(hpcd))return;
    if(hpcd != xe_np_usb_state.hpcd)
    {
        return;
    }

    if(epnum == 0U)
    {
        xe_np_usb_stream_diag.ep0_out_count++;
        xe_np_usb_stream_diag.ep0_last_out_count = HAL_PCD_EP_GetRxCount(hpcd, 0x00U);
        XE_NP_UsbStream_PrepareSetup(hpcd);
        XE_NP_UsbStream_UpdateDiag();
        return;
    }

    if(epnum == (XE_NP_USB_STREAM_OUT_EP & 0x7FU))
    {
        NP_UsbTimingReceive(xe_np_usb_out,HAL_PCD_EP_GetRxCount(hpcd,XE_NP_USB_STREAM_OUT_EP));
        (void)HAL_PCD_EP_Receive(
            hpcd,
            XE_NP_USB_STREAM_OUT_EP,
            xe_np_usb_out,
            XE_NP_USB_STREAM_BULK_MPS);
    }
}

#if NP_USB_BLOCK_PROBE
static void XE_NP_UsbStream_Service_BlockBody(void)
{
    if(NP_USB_IsReady() && NP_USB_IsDownload() && !np_usb_sd_prepare_pending && !np_usb_format_owned()) {
        if(!NP_V1UsbJob_PlatformReady()) {
            if(!NP_V1UsbJob_PlatformRecoverProfile())return;
            NP_V1UsbJob_PlatformInit();
        }
        NP_V1UsbJob_Service();
        if(NP_V1UsbJob_Owns())return;
    }
    NP_V1UsbOriginalService();
}
void XE_NP_UsbStream_Service(void)
{
 NpUsbBlockStamp block=NP_UsbBlockServiceEnter();
 XE_NP_UsbStream_Service_BlockBody();
 NP_UsbBlockServiceLeave(block);
}
#else
void XE_NP_UsbStream_Service(void)
{
    if(NP_USB_IsReady() && NP_USB_IsDownload() && !np_usb_sd_prepare_pending && !np_usb_format_owned()) {
        if(!NP_V1UsbJob_PlatformReady()) {
            if(!NP_V1UsbJob_PlatformRecoverProfile())return;
            NP_V1UsbJob_PlatformInit();
        }
        NP_V1UsbJob_Service();
        if(NP_V1UsbJob_Owns())return;
    }
    NP_V1UsbOriginalService();
}
#endif
