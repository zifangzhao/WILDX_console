int NP_SD_BleArmGate(unsigned now);
void NP_SD_BleArmStamp(unsigned now);
#include "np_sd_internal_memory.h"
#include "np_modes.h"
#include "probe_identity.h"
extern volatile NP_ProbeIdentity np_probe_identity[3];
volatile uint32_t np_mode_mask=2U,np_mode_hz=15000U;
volatile uint32_t np_staged_clock_diag[8];
volatile uint32_t np_psb_cycle_diag[4];
volatile uint32_t np_mco_hw_diag[8];
#include "np_config_port.h"
#define XE_NP_APP_EXPERIMENT_GPIO_FULLPORT_QUAD_STRESS_ENABLE 1U
static void np3_reset(void);
#include "xe_np_app.h"
#if NP_SD_APP
static void XE_NP_App_SdLoggerService(void);
#endif

#include "CE32_system.h"
#include "CE32_filesystem.h"
#include "CE32_HJ580.h"
#include "CE32_BMX160.h"
#include "HD32_SD_driver.h"
#include "main.h"
#include "xe_np_legacy_fs.h"
#include "xe_np_nric.h"
#include "xe_np_psram.h"
#include "xe_np_probe_config_generated.h"
#include "xe_np_usb_stream.h"
#include "stm32u5xx_hal_sd_ex.h"

#include <string.h>
#include "np_capture_anchor_port.inc"

extern ADC_HandleTypeDef hadc1;
extern DAC_HandleTypeDef hdac1;
extern DCACHE_HandleTypeDef hdcache1;
extern I2C_HandleTypeDef hi2c2;
extern I2C_HandleTypeDef hi2c3;
extern I2C_HandleTypeDef hi2c4;
extern I2C_HandleTypeDef hi2c5;
extern I2C_HandleTypeDef hi2c6;
extern SD_HandleTypeDef hsd1;
extern TIM_HandleTypeDef htim2;
extern TIM_HandleTypeDef htim8;
extern UART_HandleTypeDef huart1;
extern uint8_t xe_np_sd_init_failed;
extern int XE_NP_Board_RetrySdInit(void);
extern uint8_t ACC_buf_raw[20];

#ifndef XE_NP_APP_RAW_SD_LINKED_STREAM_ENABLE
/* Experimental recorder: CMD23-bounded SDMMC IDMA writes read guarded SRAM3
 * spans directly.  Completed nodes are retired before the capture producer
 * can wrap into them. */
#define XE_NP_APP_RAW_SD_LINKED_STREAM_ENABLE 0U
#endif

#ifndef XE_NP_APP_EXPERIMENT_CAPTURE_SRAM1_ENABLE
/* One-NP 30 kS/s transport experiment.  SRAM1 has a dedicated 384 KiB
 * scatter region above the OTG-HS DMA buffers; this moves the GPIO producer
 * out of SRAM3 so the exporter can be qualified against a different RAM
 * matrix target.  It is never enabled for the normal multi-probe layout. */
#define XE_NP_APP_EXPERIMENT_CAPTURE_SRAM1_ENABLE 0U
#endif

#if (XE_NP_APP_EXPERIMENT_CAPTURE_SRAM1_ENABLE != 0U)
#define XE_NP_APP_CAPTURE_STORAGE_SECTION "NP_CAPTURE_SRAM1"
#else
#define XE_NP_APP_CAPTURE_STORAGE_SECTION "NP_CAPTURE"
#endif
#ifndef XE_NP_APP_RAW_SD_LINKED_RETARGET_ENABLE
/* Reuse each IDMA node exactly once in a CMD23-bounded circular list.  The
 * foreground only rewrites a descriptor after Btc and before that node is
 * revisited; this turns a 480 KiB command into chronological unique bytes
 * while retaining a four-node SRAM3 guard. */
#define XE_NP_APP_RAW_SD_LINKED_RETARGET_ENABLE 0U
#endif

#define XE_NP_APP_RAW_SD_LL_NODE_BLOCKS 127U
#define XE_NP_APP_RAW_SD_LL_NODE_BYTES \
    (XE_NP_APP_RAW_SD_LL_NODE_BLOCKS * XE_NP_APP_RAW_SD_BLOCK_SIZE)
#define XE_NP_APP_RAW_SD_LL_NODES_PER_BATCH 4U
#define XE_NP_APP_RAW_SD_LL_NODE_COUNT_MAX 5U
#define XE_NP_APP_RAW_SD_LL_BATCH_BYTES \
    (XE_NP_APP_RAW_SD_LL_NODES_PER_BATCH * XE_NP_APP_RAW_SD_LL_NODE_BYTES)
#define XE_NP_APP_RAW_SD_LL_BATCH_BLOCKS \
    (XE_NP_APP_RAW_SD_LL_BATCH_BYTES / XE_NP_APP_RAW_SD_BLOCK_SIZE)
#define XE_NP_APP_RAW_SD_RT_NODE_BLOCKS 120U
#define XE_NP_APP_RAW_SD_RT_NODE_BYTES \
    (XE_NP_APP_RAW_SD_RT_NODE_BLOCKS * XE_NP_APP_RAW_SD_BLOCK_SIZE)
#define XE_NP_APP_RAW_SD_RT_NODE_COUNT 4U
#define XE_NP_APP_RAW_SD_RT_BATCH_BLOCKS \
    (XE_NP_APP_RAW_SD_RT_NODE_BLOCKS * XE_NP_APP_RAW_SD_RT_NODE_COUNT * 2U)
#define XE_NP_APP_RAW_SD_RT_BATCH_BYTES \
    (XE_NP_APP_RAW_SD_RT_BATCH_BLOCKS * XE_NP_APP_RAW_SD_BLOCK_SIZE)

#define XE_NP_APP_DMA_NODE_BYTES XE_NP_CAPTURE_DMA_NODE_BYTES
#define XE_NP_APP_DMA_HALF_BYTES (XE_NP_APP_DMA_NODE_BYTES / 2UL)
#define XE_NP_APP_DMA_NODE_COUNT_MAX XE_NP_CAPTURE_DMA_NODE_COUNT_MAX
#ifndef XE_NP_APP_CAPTURE_PSRAM_ENABLE
#define XE_NP_APP_CAPTURE_PSRAM_ENABLE 0U
#endif
/*
 * Use a trimmed in-SRAM ring whose live size is an integer number of DMA
 * nodes. The global BUF_SIZE remains the backing-store ceiling, but the
 * active ephys ring stays on the shared NP/USB/SD-compatible geometry.
 */
/*
 * Keep the normal image within the legacy buffer budget, but allow the
 * dedicated 15 kS/s recorder image to reserve more of isolated SRAM3.  The
 * requested value is rounded down to an integer number of capture nodes.
 */
#ifndef XE_NP_APP_CAPTURE_STORAGE_BYTES
#define XE_NP_APP_CAPTURE_STORAGE_BYTES BUF_SIZE
#endif
#define XE_NP_APP_INTERNAL_CAPTURE_STORAGE_BYTES \
    ((XE_NP_APP_CAPTURE_STORAGE_BYTES / XE_NP_APP_DMA_NODE_BYTES) * \
     XE_NP_APP_DMA_NODE_BYTES)
#define XE_NP_APP_CAPTURE_PSRAM_NODE_COUNT 128U
#define XE_NP_APP_CAPTURE_PSRAM_BYTES \
    (XE_NP_APP_CAPTURE_PSRAM_NODE_COUNT * XE_NP_APP_DMA_NODE_BYTES)
#define XE_NP_APP_USB_CAPTURE_INTERNAL_TIMER_ENABLE 0U
#define XE_NP_APP_USB_CAPTURE_INTERNAL_TIMER_HZ XE_NP_NRIC_CONFIGURED_PSB_CLK_HZ
#ifndef XE_NP_APP_USB_CAPTURE_KEEP_SESSION_ON_HOST_STOP
#define XE_NP_APP_USB_CAPTURE_KEEP_SESSION_ON_HOST_STOP 1U
#endif
#define XE_NP_APP_USB_CAPTURE_FREEZE_DEBUG_ON_HOST_STOP 0U
#define XE_NP_APP_CAPTURE_POSTHOC_ONLY 1U
#define XE_NP_APP_DMA_SAMPLE_FRAMES_AGGRESSIVE 2U
#define XE_NP_APP_DMA_SAMPLE_FRAMES_LOCKED 1U
#define XE_NP_APP_VALIDATION_STRIDE_LOCKED 8U
#define XE_NP_APP_LIVE_DECODE_INTERVAL_MS 250U
#define XE_NP_APP_MCLK_REFRESH_INTERVAL_MS 25U
#define XE_NP_APP_BLE_UART_BEACON_ENABLE 0U
#define XE_NP_APP_BLE_UART_BEACON_PERIOD_MS 1000U
#define XE_NP_APP_SD_REFRESH_INTERVAL_MS 1000U
#define XE_NP_APP_SD_RETRY_INTERVAL_MS 250U
#define XE_NP_APP_SD_UHS_RETRY_INTERVAL_MS 1000U
#define XE_NP_APP_AUTO_SD_HOST_RECOVERY_ENABLE 0U
#define XE_NP_APP_AUTO_SD_UHS_RECOVERY_ENABLE 0U
#ifndef XE_NP_APP_RAW_SD_SINK_ENABLE
#define XE_NP_APP_RAW_SD_SINK_ENABLE 0U
#endif
#ifndef XE_NP_APP_EXPERIMENT_ALLOW_UNQUALIFIED_SD_RAW
/* SWD characterization only: exercise the SDMMC writer even when the card
 * has not negotiated the production UHS write-budget contract. */
#define XE_NP_APP_EXPERIMENT_ALLOW_UNQUALIFIED_SD_RAW 0U
#endif
#ifndef XE_NP_APP_EXPERIMENT_SD_SINK_IGNORE_USB_CONFIG
/* SWD characterization only: allow raw SDMMC writes while USB is enumerated
 * but not streaming.  Production images always reserve the session for USB
 * as soon as the host configures the device. */
#define XE_NP_APP_EXPERIMENT_SD_SINK_IGNORE_USB_CONFIG 0U
#endif
#ifndef XE_NP_APP_EXPERIMENT_ALLOW_UNQUALIFIED_SD_CAPTURE_LOCK
/* SWD characterization only: let the raw SD sink measure SDMMC contention
 * when the online paired-lane watchdog cannot establish its stream lock.
 * This never accepts data as a valid recording. */
#define XE_NP_APP_EXPERIMENT_ALLOW_UNQUALIFIED_SD_CAPTURE_LOCK 0U
#endif
#ifndef XE_NP_APP_EXPERIMENT_SD_STRESS_WRITER_ENABLE
/* Characterization only: continuously issue SDMMC IDMA writes from a private
 * SRAM1 buffer while capture runs.  This deliberately does not consume the
 * live capture ring, so its result measures SDMMC arbitration alone. */
#define XE_NP_APP_EXPERIMENT_SD_STRESS_WRITER_ENABLE 0U
#endif
#ifndef XE_NP_APP_RAW_SD_DMA_STAGING_ENABLE
/* Move a completed, contiguous lane range from the live SRAM3 producer to a
 * private SRAM1 SD-IDMA buffer without a foreground CPU read of SRAM3.  This
 * is deliberately opt-in until the full 30 kHz qualification has passed. */
#define XE_NP_APP_RAW_SD_DMA_STAGING_ENABLE 0U
#endif
/* Dedicated raw-SD capture has no foreground consumer of the live SRAM3
 * lanes. Its DMA IRQ work is limited to publishing completed bytes to the
 * raw recorder; decode/preview queues belong to USB mode. */
#ifndef XE_NP_APP_RAW_SD_DEDICATED_CAPTURE_ENABLE
#define XE_NP_APP_RAW_SD_DEDICATED_CAPTURE_ENABLE 0U
#endif
#ifndef XE_NP_APP_LEGACY_FS_ENABLE
#define XE_NP_APP_LEGACY_FS_ENABLE 0U
#endif
/*
 * NP-only bench mode: let the probe bring-up and DMA capture run without
 * requiring USB or SD storage readiness. This is the cleanest way to isolate
 * the Neuropixels timing/GPIO/DMA path from transport-side effects.
 */
#ifndef XE_NP_APP_BENCH_CAPTURE_WITHOUT_STORAGE
#define XE_NP_APP_BENCH_CAPTURE_WITHOUT_STORAGE 1U
#endif
/* Production USB capture must retry a failed probe bring-up.  Holding the
 * probe in the bench fault state leaves a successfully enumerated WinUSB
 * device with no raw payload on subsequent host Start requests. */
#define XE_NP_APP_BENCH_HOLD_PROBE_ON_FAILURE 0U
#define XE_NP_APP_BENCH_I2C_RECOVERY_ON_PING_FAIL 1U
#define XE_NP_APP_BENCH_I2C_SCAN_ON_PING_FAIL 1U
#define XE_NP_APP_BENCH_I2C_SCAN_MAGIC 0x49324353UL
#define XE_NP_APP_BENCH_I2C_SCAN_VERSION 1U
#define XE_NP_APP_BENCH_I2C_SCAN_TIMEOUT_MS 2U
#define XE_NP_APP_BENCH_PSB_CLOCK_PROBE_ENABLE 1U
#define XE_NP_APP_GPIOE_CAPTURE_USE_PA0_TIM2_CH1 1U
#ifndef XE_NP_APP_GPIOE_CAPTURE_TIM2_RISING_EDGE
#define XE_NP_APP_GPIOE_CAPTURE_TIM2_RISING_EDGE 1U
#endif
#ifndef XE_NP_APP_GPIOE_CAPTURE_TIM2_FILTER
#define XE_NP_APP_GPIOE_CAPTURE_TIM2_FILTER 0U
#endif
#define XE_NP_APP_GPIOE_CAPTURE_TIM2_UPDATE_DMA_ENABLE 0U
#define XE_NP_APP_GPIOE_CAPTURE_TIM2_DELAYED_DMA_ENABLE 1U
#ifndef XE_NP_APP_GPIOE_CAPTURE_TIM2_DELAY_TICKS
/*
 * P3-loaded multi-probe 15 kHz capture needs the earlier compare. Singles
 * and P12 retain 7 ticks; 7.5 kHz retains 7 and >=20 kHz retains 2 ticks.
 */
#define XE_NP_APP_GPIOE_CAPTURE_TIM2_DELAY_TICKS 6U
#endif
#ifndef XE_NP_APP_GPIOE_CAPTURE_TIM2_START_DELAY_TICKS
#define XE_NP_APP_GPIOE_CAPTURE_TIM2_START_DELAY_TICKS 0U
#endif
#define XE_NP_APP_GPIOE_CAPTURE_TIM2_START_DELAY_TIMEOUT_MS 100U
#ifndef XE_NP_APP_EXPERIMENT_GPIOE_CAPTURE_TIM2_PSB_ETR_ENABLE
#define XE_NP_APP_EXPERIMENT_GPIOE_CAPTURE_TIM2_PSB_ETR_ENABLE 0U
#endif
#define XE_NP_APP_PSB_CLOCK_PROBE_MS 2U
#define XE_NP_APP_PSB_CLOCK_PROBE_FLAG_RAN 0x00000001UL
#define XE_NP_APP_PSB_CLOCK_PROBE_FLAG_NONZERO 0x00000002UL
#define XE_NP_APP_PSB_CLOCK_PROBE_FLAG_TIMER_MISSING 0x00000004UL
#define XE_NP_APP_PSB_CLOCK_PROBE_FLAG_NO_GPIOE_PROBE 0x00000008UL
#define XE_NP_APP_PSB_CLOCK_PROBE_FLAG_GPIOE_PA0_TIM2_CH1 0x00000010UL
#define XE_NP_APP_PSB_CLOCK_PROBE_FLAG_GPIOE_TIM2_ETR 0x00000040UL
#define XE_NP_APP_PSB_CLOCK_PROBE_FLAG_TIMER_CONFIG_FAIL 0x00000020UL
/*
 * Keep the shipping firmware on the simplest possible capture/export path.
 * Any MCU-side diagnostics that go beyond raw sampling and packet export
 * should be opt-in bench experiments, not part of the default image.
 */
#ifndef XE_NP_APP_EXPERIMENT_DMA_SELF_LINK_ENABLE
#define XE_NP_APP_EXPERIMENT_DMA_SELF_LINK_ENABLE 0U
#endif
#ifndef XE_NP_APP_EXPERIMENT_HALF_BOUNDARY_PROBE_ENABLE
#define XE_NP_APP_EXPERIMENT_HALF_BOUNDARY_PROBE_ENABLE 0U
#endif
#ifndef XE_NP_APP_EXPERIMENT_HALF_BOUNDARY_PROBE_ISR_SNAPSHOT_ENABLE
#define XE_NP_APP_EXPERIMENT_HALF_BOUNDARY_PROBE_ISR_SNAPSHOT_ENABLE 0U
#endif
#ifndef XE_NP_APP_EXPERIMENT_TIMING_SWEEP_ENABLE
#define XE_NP_APP_EXPERIMENT_TIMING_SWEEP_ENABLE 0U
#endif
#ifndef XE_NP_APP_EXPERIMENT_SYNC_WATCHDOG_ENABLE
#define XE_NP_APP_EXPERIMENT_SYNC_WATCHDOG_ENABLE 0U
#endif
#ifndef XE_NP_APP_EXPERIMENT_NP2_PSB_PULLUP_ENABLE
#define XE_NP_APP_EXPERIMENT_NP2_PSB_PULLUP_ENABLE 0U
#endif
#ifndef XE_NP_APP_EXPERIMENT_NP2_PSB_PULLDOWN_ENABLE
#define XE_NP_APP_EXPERIMENT_NP2_PSB_PULLDOWN_ENABLE 0U
#endif

/*
 * Keep the default image on the simpler reset path. Early REC_NRESET release
 * did not clear the current chain2 bring-up failure and should stay a
 * dedicated bench override instead of the normal flashed behavior.
 */
#ifndef XE_NP_APP_EXPERIMENT_RELEASE_RECORD_RESET_BEFORE_NRIC_CONFIG
#define XE_NP_APP_EXPERIMENT_RELEASE_RECORD_RESET_BEFORE_NRIC_CONFIG 0U
#endif
#ifndef XE_NP_APP_EXPERIMENT_PSB_CLK_PULLUP_ENABLE
#define XE_NP_APP_EXPERIMENT_PSB_CLK_PULLUP_ENABLE 0U
#endif
#ifndef XE_NP_APP_EXPERIMENT_PSB_CLK_PULLDOWN_ENABLE
#define XE_NP_APP_EXPERIMENT_PSB_CLK_PULLDOWN_ENABLE 0U
#endif
#ifndef XE_NP_APP_EXPERIMENT_USB_STABLE_WINDOW_DEFER_ENABLE
#define XE_NP_APP_EXPERIMENT_USB_STABLE_WINDOW_DEFER_ENABLE 0U
#endif
#ifndef XE_NP_APP_EXPERIMENT_USB_STABLE_WINDOW_DEFER_BYTES
#define XE_NP_APP_EXPERIMENT_USB_STABLE_WINDOW_DEFER_BYTES 0U
#endif
#ifndef XE_NP_APP_EXPERIMENT_USB_WINDOW_CHUNK_BYTES
#define XE_NP_APP_EXPERIMENT_USB_WINDOW_CHUNK_BYTES 0U
#endif
#ifndef XE_NP_APP_EXPERIMENT_FULL_NODE_WINDOW_PUBLICATION_ENABLE
#define XE_NP_APP_EXPERIMENT_FULL_NODE_WINDOW_PUBLICATION_ENABLE 0U
#endif
#ifndef XE_NP_APP_EXPERIMENT_HALF_WINDOW_QUEUE_RESERVE_ENABLE
#define XE_NP_APP_EXPERIMENT_HALF_WINDOW_QUEUE_RESERVE_ENABLE 0U
#endif
#ifndef XE_NP_APP_EXPERIMENT_CAPTURE_STORAGE_OFFSET_BYTES
#define XE_NP_APP_EXPERIMENT_CAPTURE_STORAGE_OFFSET_BYTES 0U
#endif
#ifndef XE_NP_APP_EXPERIMENT_DMA_MINIMAL_IRQ_DISPATCH_ENABLE
#define XE_NP_APP_EXPERIMENT_DMA_MINIMAL_IRQ_DISPATCH_ENABLE 0U
#endif
#ifndef XE_NP_APP_EXPERIMENT_DMA_LINK_ALLOCATED_PORT
#define XE_NP_APP_EXPERIMENT_DMA_LINK_ALLOCATED_PORT 1U
#endif
#ifndef XE_NP_APP_EXPERIMENT_DMA_IRQ_JITTER_DETECT_ENABLE
#define XE_NP_APP_EXPERIMENT_DMA_IRQ_JITTER_DETECT_ENABLE 0U
#endif
#ifndef XE_NP_APP_EXPERIMENT_TIM2_PSB_ARMED_FREERUN_ENABLE
#define XE_NP_APP_EXPERIMENT_TIM2_PSB_ARMED_FREERUN_ENABLE 0U
#endif
#ifndef XE_NP_APP_EXPERIMENT_TIM2_RESET_EACH_PSB_EDGE_ENABLE
/*
 * Deterministic direct-capture topology: PA0 resets TIM2 on every PSB edge
 * and CC2 raises the sole GPIO DMA request at a fixed offset afterwards.
 * This avoids synthesising a nominal 8-tick cadence that can drift from the
 * actual probe clock after an MCO divider change.
 */
#define XE_NP_APP_EXPERIMENT_TIM2_RESET_EACH_PSB_EDGE_ENABLE 0U
#endif
#ifndef XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_ENABLE
/*
 * 15 kS/s GPIOE capture experiment. TIM2 remains phase-locked to every PSB
 * edge and exports the selected sample instant to TIM8. TIM8 alternates that
 * edge stream between GPDMA channels 12 and 13, so each channel runs at half
 * the raw GPIO rate and fills one of two even/odd sample lanes.
 */
#define XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_ENABLE 0U
#endif
#ifndef XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_PHASE_PROBE_ENABLE
/*
 * Dedicated NP1 DDR capture: CC2 and CC3 are two independent, timer-delayed
 * GPIO read requests.  Unlike the older interleave experiment, they do not
 * alternate complete PSB cells through TIM8; they acquire the two data-valid
 * phases of the same cell into separate SRAM3 lanes.
 */
#define XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_PHASE_PROBE_ENABLE 0U
#endif
#ifndef XE_NP_APP_GPIOE_DDR_PHASE_A_DELAY_TICKS
#define XE_NP_APP_GPIOE_DDR_PHASE_A_DELAY_TICKS 4U
#endif
#ifndef XE_NP_APP_GPIOE_DDR_PHASE_B_DELAY_TICKS
#define XE_NP_APP_GPIOE_DDR_PHASE_B_DELAY_TICKS 12U
#endif
#if ((XE_NP_APP_GPIOE_DDR_PHASE_A_DELAY_TICKS >= 16U) || \
     (XE_NP_APP_GPIOE_DDR_PHASE_B_DELAY_TICKS >= 16U))
#error "NP1 DDR GPIO DMA phases must fit within the 16-tick PSB cell"
#endif
#ifndef XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_INTERLEAVE_ENABLE
/*
 * Bench-only full-rate variant: TIM8 distributes consecutive PSB samples over
 * CH1, CH2, CH3, and update DMA requests.  Four 5.85 MHz DMA lanes replace the
 * two 11.7 MHz lanes used by the 30 kS/s dual-lane experiment.  It deliberately
 * has no producer/USB/SD publication path; the frozen four-lane ring is decoded
 * over SWD before this topology is considered for production.
 */
#define XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_INTERLEAVE_ENABLE 0U
#endif
#ifndef XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_INTERLEAVE_USB_ENABLE
#define XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_INTERLEAVE_USB_ENABLE 0U
#endif
#ifndef XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_LINEAR_CHANNELS_ENABLE
/* GPDMA1 channels 0..11 use the compact FIFO/linear datapath.  AN5593
 * recommends that topology for the lowest-latency peripheral request service;
 * channels 12..15 remain available for 2D memory work. */
#define XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_LINEAR_CHANNELS_ENABLE 0U
#endif
#ifndef XE_NP_APP_QUAD_DMA_DAR_ONLY_ENABLE
#define XE_NP_APP_QUAD_DMA_DAR_ONLY_ENABLE 0U
#endif
#ifndef XE_NP_APP_QUAD_DMA_STAGGER_ENABLE
#define XE_NP_APP_QUAD_DMA_STAGGER_ENABLE 0U
#endif
#ifndef XE_NP_APP_QUAD_DMA_QUIET_ENABLE
#define XE_NP_APP_QUAD_DMA_QUIET_ENABLE 0U
#endif
#ifndef XE_NP_APP_QUAD_DMA_CHANNEL_ROTATION
#define XE_NP_APP_QUAD_DMA_CHANNEL_ROTATION 0U
#endif
#ifndef XE_NP_APP_P3_MULTI15_SOURCE_PORT1
/* Private latency-isolation candidate; preserve the production port by default. */
#define XE_NP_APP_P3_MULTI15_SOURCE_PORT1 0U
#endif
#ifndef XE_NP_APP_P12315_PAIRED_GPIOE
/* Private P123/15k topology candidate; every other mode keeps byte capture. */
#define XE_NP_APP_P12315_PAIRED_GPIOE 0U
#endif
#define NP_P12315_PAIRED_GPIOE() \
    (XE_NP_APP_P12315_PAIRED_GPIOE && np_mode_mask == 7U && np_mode_hz == 15000U)
#ifndef XE_NP_APP_QUAD_DMA_LLI_SRAM2
#define XE_NP_APP_QUAD_DMA_LLI_SRAM2 0U
#endif
#ifndef XE_NP_APP_QUAD_DMA_PACK_WORDS
#define XE_NP_APP_QUAD_DMA_PACK_WORDS 0U
#endif
#ifndef XE_NP_APP_QUAD_DMA_CPU_GUARD
#define XE_NP_APP_QUAD_DMA_CPU_GUARD 0U
#endif
#ifndef XE_NP_APP_QUAD_DMA_CPU_GUARD_NEGATIVE
#define XE_NP_APP_QUAD_DMA_CPU_GUARD_NEGATIVE 0U
#endif
#ifndef XE_NP_APP_QUAD_DMA_NO_PROBE_DRYRUN
#define XE_NP_APP_QUAD_DMA_NO_PROBE_DRYRUN 0U
#endif
#ifndef XE_NP_APP_QUAD_USB_WORD_EXPORT
#define XE_NP_APP_QUAD_USB_WORD_EXPORT 0U
#endif
#ifndef XE_NP_APP_QUAD_USB_PACED
#define XE_NP_APP_QUAD_USB_PACED 0U
#endif
#if (XE_NP_APP_QUAD_USB_WORD_EXPORT && (!XE_NP_APP_QUAD_DMA_DAR_ONLY_ENABLE || !XE_NP_APP_QUAD_DMA_PACK_WORDS || !XE_NP_APP_QUAD_DMA_STAGGER_ENABLE || XE_NP_APP_QUAD_DMA_QUIET_ENABLE))
#error "Word USB qualifier requires packed staggered DAR lanes, without quiet mode."
#endif
#if (XE_NP_APP_QUAD_DMA_NO_PROBE_DRYRUN && !XE_NP_APP_QUAD_DMA_QUIET_ENABLE)
#error "No-probe dry run requires the finite quiet capture test."
#endif
#if ((XE_NP_APP_QUAD_DMA_LLI_SRAM2 || XE_NP_APP_QUAD_DMA_PACK_WORDS) && !XE_NP_APP_QUAD_DMA_DAR_ONLY_ENABLE)
#error "Descriptor isolation/packing requires isolated DAR-only capture."
#endif
#if (XE_NP_APP_QUAD_DMA_CPU_GUARD && !XE_NP_APP_QUAD_DMA_QUIET_ENABLE)
#error "CPU exclusion is a quiet frozen-capture diagnostic, not a live recorder."
#endif
#if (XE_NP_APP_QUAD_DMA_CPU_GUARD_NEGATIVE && !XE_NP_APP_QUAD_DMA_CPU_GUARD)
#error "Intentional MPU fault requires the CPU guard."
#endif
#if (XE_NP_APP_QUAD_DMA_CHANNEL_ROTATION > 3U) || \
    ((XE_NP_APP_QUAD_DMA_CHANNEL_ROTATION != 0U) && !XE_NP_APP_QUAD_DMA_DAR_ONLY_ENABLE)
#error "Channel rotation is a four-lane minimal-DMA isolation probe."
#endif
#if ((XE_NP_APP_QUAD_DMA_STAGGER_ENABLE || XE_NP_APP_QUAD_DMA_QUIET_ENABLE) && !XE_NP_APP_QUAD_DMA_DAR_ONLY_ENABLE)
#error "Stagger/quiet mode requires minimal quad DMA."
#endif
#if (XE_NP_APP_QUAD_DMA_DAR_ONLY_ENABLE != 0U)
#if (XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_INTERLEAVE_ENABLE == 0U)
#error "Quad DAR-only reload requires the quad capture topology."
#endif
#endif
#ifndef XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_TIMED_SHADOW_ENABLE
/* One low-priority M2M request follows each high-priority GPIO sample on the
 * same TIM8 compare event.  Unlike a free-running exporter, it may only touch
 * a lane in its explicitly allocated four-phase time slot. */
#define XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_TIMED_SHADOW_ENABLE 0U
#endif
#ifndef XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_PINGPONG_ENABLE
/* One-NP 30 kS/s transport topology.  Each phase lane owns two complete
 * epochs.  The GPDMA writer switches only at a node boundary, allowing the
 * USB side to consume the previous epoch without touching the active one. */
#define XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_PINGPONG_ENABLE 0U
#endif
#ifndef XE_NP_APP_EXPERIMENT_GPIO_FULLPORT_QUAD_STRESS_ENABLE
/*
 * Full-board 30 kS/s qualification: TIM8 CH1..CH4 feed four byte lanes for
 * each wired probe bus (GPIOE low, GPIOE high and GPIOD high).  A halfword
 * GPIO read was not phase-compatible with the proven byte capture: it changes
 * the peripheral/DMA service timing enough to corrupt DDR fields.  The three
 * byte groups retain that timing while exercising all three probe inputs.
 * This mode is deliberately frozen/SWD-only: it establishes whether the
 * on-board capture fabric can sustain the complete input load before a USB or
 * SD consumer is allowed to touch SRAM3.
 */
#define XE_NP_APP_EXPERIMENT_GPIO_FULLPORT_QUAD_STRESS_ENABLE 0U
#endif
#ifndef XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_STORAGE_ENABLE
/*
 * Promote the two physical DMA lanes to two independent raw-storage rings.
 * The persisted streams are lane A followed by lane B, not chronological
 * snapshots; host tooling must interleave them before decoding PSB frames.
 */
#define XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_STORAGE_ENABLE 0U
#endif
#ifndef XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_PUBLISH_ENABLE
/*
 * Deliver a completed lane ring to the normal producer bookkeeping. This
 * makes the lane split testable with SWD before a recorder is allowed to use
 * the streams. Persistent storage implies publication.
 */
#define XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_PUBLISH_ENABLE \
    XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_STORAGE_ENABLE
#endif
#ifndef XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_DELAYED_PWM2_ENABLE
/*
 * NP1's proven 15 kS/s capture uses PWM1: OC2REF rises at the PSB-reset
 * edge.  PWM2, which defers that rise to CCR2, is retained as an opt-in
 * timing experiment only; it has not yet demonstrated a valid stream on
 * the connected device.
 */
#define XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_DELAYED_PWM2_ENABLE 0U
#endif
#ifndef XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_BLOCK_BYTES
#define XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_BLOCK_BYTES 60U
#endif
#ifndef XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_REPEAT_COUNT
#define XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_REPEAT_COUNT 2048U
#endif
#ifndef XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_BLOCK_BYTES
#define XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_BLOCK_BYTES 100U
#endif
#ifndef XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT
#define XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT 4U
#endif
#ifndef XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_USB_GUARD_BYTES
/* At 5.85 MB/s per lane this leaves roughly 700 us between a USB CPU read and
 * the GPDMA writer.  The foreground never reads the active DMA neighbourhood. */
#define XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_USB_GUARD_BYTES 4096U
#endif
#if ((XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT != 3U) && \
     (XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT != 4U))
#error "Interleaved multi-lane bench supports three or four DMA lanes"
#endif
#if ((XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_INTERLEAVE_USB_ENABLE != 0U) && \
     (XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT != 4U))
#error "The production USB exporter requires the four-lane topology"
#endif
#if ((XE_NP_APP_EXPERIMENT_GPIO_FULLPORT_QUAD_STRESS_ENABLE != 0U) && \
     ((XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT != 4U) || \
      (XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_LINEAR_CHANNELS_ENABLE == 0U) || \
      (XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_TIMED_SHADOW_ENABLE != 0U)))
#error "Full-port quad stress requires four linear capture lanes and no exporter"
#endif
#if ((XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_PINGPONG_ENABLE != 0U) && \
     (XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_INTERLEAVE_ENABLE == 0U))
#error "Quad ping-pong requires the four-lane GPIOE capture topology"
#endif
#define XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_STRIDE_BYTES 2U
/*
 * The paired watchdog reconstructs this many chronological GPIO bytes from
 * the two alternating DMA lanes.  10,240 bytes accommodates the complete
 * startup search (one superframe of phase search plus one ultraframe of PSB
 * frames) without touching the live capture ring.
 */
#define XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_VALIDATION_BYTES 10240U
#define XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_VALIDATION_LANE_BYTES \
    (XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_VALIDATION_BYTES / \
     XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_STRIDE_BYTES)
/*
 * The first pair of DMA half-windows can begin mid-frame and needs phase
 * discovery.  Let the one-second capture-lock timeout make that decision;
 * four transient startup windows is unnecessarily strict for this path.
 */
#define XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_LOCK_FAIL_LIMIT 16U
#if ((XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_VALIDATION_BYTES % \
      XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_STRIDE_BYTES) != 0U)
#error "Interleaved validation window must divide evenly between the two DMA lanes"
#endif
#if (XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_VALIDATION_BYTES < \
     ((XE_NP_CAPTURE_FRAMES_PER_SUPERFRAME + XE_NP_CAPTURE_FRAMES_PER_ULTRAFRAME + 1U) * \
      XE_NP_CAPTURE_FRAME_BIT_COUNT))
#error "Interleaved validation window is too small for startup PSB lock search"
#endif
#if ((XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_STORAGE_ENABLE != 0U) && \
     (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_ENABLE == 0U))
#error "Dual-lane storage requires the GPIOE dual-DMA interleave capture path"
#endif
#if ((XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_STORAGE_ENABLE != 0U) && \
     (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_PUBLISH_ENABLE == 0U))
#error "Dual-lane storage requires completed-lane publication"
#endif
#ifndef XE_NP_APP_EXPERIMENT_FREEZE_CAPTURE_AFTER_MS
#define XE_NP_APP_EXPERIMENT_FREEZE_CAPTURE_AFTER_MS 0U
#endif
#ifndef XE_NP_APP_EXPERIMENT_FREEZE_QUIESCE_ONLY
/*
 * SWD ring qualification: stop the timer request sources but deliberately
 * leave the GPDMA descriptors armed.  This separates the recorded bytes from
 * any side effects of HAL_DMA_Abort()/List_DeInit() at the snapshot boundary.
 */
#define XE_NP_APP_EXPERIMENT_FREEZE_QUIESCE_ONLY 0U
#endif
#if ((XE_NP_APP_EXPERIMENT_DMA_LINK_ALLOCATED_PORT != 0U) && \
     (XE_NP_APP_EXPERIMENT_DMA_LINK_ALLOCATED_PORT != 1U))
#error "XE_NP_APP_EXPERIMENT_DMA_LINK_ALLOCATED_PORT must be 0 or 1"
#endif
#define XE_NP_APP_DMA_IRQ_JITTER_EVENT_HT 0U
#define XE_NP_APP_DMA_IRQ_JITTER_EVENT_COUNT 2U
#define XE_NP_APP_DMA_LINK_ALLOCATED_PORT_VALUE \
    ((XE_NP_APP_EXPERIMENT_DMA_LINK_ALLOCATED_PORT != 0U) ? \
         DMA_LINK_ALLOCATED_PORT1 : \
         DMA_LINK_ALLOCATED_PORT0)
#ifndef XE_NP_BOARD_EXPERIMENT_MCO_GPIO_SPEED
#define XE_NP_BOARD_EXPERIMENT_MCO_GPIO_SPEED GPIO_SPEED_FREQ_VERY_HIGH
#endif
/*
 * Match the old CE64 capture semantics as closely as STM32U5 GPDMA allows:
 * run a static circular queue continuously in hardware and only publish
 * producer progress on HT/TC boundaries. Avoid the experimental 2D-node,
 * dynamic-queue, and one-window defer paths while validating NP integrity.
 *
 * Direct repeated-block mode is a diagnostic bypass for linked-list reload.
 * It removes the descriptor fetch path entirely and runs one full-ring 2D
 * transfer with HT/TC at half/full ring. This is not a permanent circular
 * solution on STM32U5, but it is the cleanest way to prove whether the
 * corruption comes from linked-list reload or from repeated-block hardware.
 */
#ifndef XE_NP_APP_CAPTURE_DMA_DIRECT_REPEAT_DIAG_ENABLE
#define XE_NP_APP_CAPTURE_DMA_DIRECT_REPEAT_DIAG_ENABLE 0U
#endif
#ifndef XE_NP_APP_CAPTURE_DMA_REPEAT_EVENT_MODE
/*
 * The no-reload DMA experiments still want node-granular publication and
 * startup-lock progress. Generate TC/HT on each repeated block instead of
 * only once at the end of the full repeated span; the repeat address offsets
 * still remove most descriptor churn, but software keeps seeing the same
 * window cadence as the normal linked-list capture path.
 */
#define XE_NP_APP_CAPTURE_DMA_REPEAT_EVENT_MODE DMA_TCEM_BLOCK_TRANSFER
#endif
#ifndef XE_NP_APP_CAPTURE_DMA_MINIMAL_SELF_LINK_ENABLE
#define XE_NP_APP_CAPTURE_DMA_MINIMAL_SELF_LINK_ENABLE \
    XE_NP_APP_EXPERIMENT_DMA_SELF_LINK_ENABLE
#endif
#ifndef XE_NP_APP_CAPTURE_DMA_DYNAMIC_QUEUE_ENABLE
#define XE_NP_APP_CAPTURE_DMA_DYNAMIC_QUEUE_ENABLE 0U
#endif
#ifndef XE_NP_APP_EXPERIMENT_HOST_TRIGGERED_BRINGUP_ONLY
/*
 * The no-reload DMA experiments are only meaningful when the host has
 * explicitly requested raw USB observation. If they auto-start during idle
 * boot, the normal startup guards can trip before USB bypass is armed and the
 * branch never reaches its intended test conditions.
 */
#define XE_NP_APP_EXPERIMENT_HOST_TRIGGERED_BRINGUP_ONLY \
    ((XE_NP_APP_CAPTURE_DMA_DIRECT_REPEAT_DIAG_ENABLE != 0U) || \
     (XE_NP_APP_EXPERIMENT_DMA_SELF_LINK_ENABLE != 0U))
#endif
#ifndef XE_NP_APP_EXPERIMENT_DISABLE_CAPTURE_RATE_MONITOR
/*
 * The no-reload DMA benches intentionally change when capture-complete events
 * are surfaced to software. That invalidates the normal per-50 ms byte-rate
 * watchdog long before it tells us anything useful about transport viability.
 */
#define XE_NP_APP_EXPERIMENT_DISABLE_CAPTURE_RATE_MONITOR \
    (XE_NP_APP_EXPERIMENT_HOST_TRIGGERED_BRINGUP_ONLY || \
     ((XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_PUBLISH_ENABLE != 0U) && \
      (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_STORAGE_ENABLE == 0U)))
#endif
#ifndef XE_NP_APP_CAPTURE_DMA_SINGLE_2D_NODE_ENABLE
/*
 * The one-node 2D repeat path produces a deterministic byte discontinuity
 * after two 61,440-byte blocks on the live NP1 path.  Keep the proven
 * descriptor-ring implementation for the 7.5 kHz fallback; no-reload modes
 * remain explicit experiments until their boundary accounting is qualified.
 */
#define XE_NP_APP_CAPTURE_DMA_SINGLE_2D_NODE_ENABLE \
    XE_NP_APP_EXPERIMENT_DMA_SELF_LINK_ENABLE
#endif
#ifndef XE_NP_APP_CAPTURE_DMA_FORCE_SELF_RELOAD_MASK_ENABLE
/*
 * The HAL circular queue already owns the tail CLLR.  Replacing it with the
 * hand-built mask is useful only as an explicit diagnostic; it is not part of
 * the normal one-node circular path.
 */
#define XE_NP_APP_CAPTURE_DMA_FORCE_SELF_RELOAD_MASK_ENABLE 0U
#endif
#ifndef XE_NP_APP_CAPTURE_DMA_DAR_ONLY_CIRCULAR_ENABLE
/*
 * STM32U5 circular-buffer fast path from ST's GPDMA training material: the
 * channel's CLLR reloads only CDAR from a one-word LLI, retaining all transfer
 * registers.  This removes the normal 2D descriptor's 8-word reload at wrap.
 */
#define XE_NP_APP_CAPTURE_DMA_DAR_ONLY_CIRCULAR_ENABLE 0U
#endif
#ifndef XE_NP_APP_DUAL_DMA_DAR_ONLY_CIRCULAR_ENABLE
/*
 * The 30 kS/s alternating-lane experiment uses the same minimal CDAR-only
 * reload as the qualified direct capture, but leaves the normal one-lane
 * topology untouched.  Each lane owns its own 61,440-byte circular region.
 */
#define XE_NP_APP_DUAL_DMA_DAR_ONLY_CIRCULAR_ENABLE 0U
#endif
#ifndef XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_PHASE_SEARCH_RADIUS
/*
 * A normal linked-list pair starts within a few requests.  The independent
 * CDAR-only channels can retain a bounded FIFO/service lead at startup, so
 * qualification must search that measured, deterministic displacement before
 * exposing a chronological USB stream.
 */
#if (XE_NP_APP_DUAL_DMA_DAR_ONLY_CIRCULAR_ENABLE != 0U)
#define XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_PHASE_SEARCH_RADIUS 128
#else
#define XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_PHASE_SEARCH_RADIUS 4
#endif
#endif
#ifndef XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_FIXED_LANE_B_OFFSET
/* The alternating TIM8 requests start the two CDAR lanes at the same logical
 * byte ordinal.  A frozen snapshot's physical heads differ because channels
 * are stopped sequentially; that is not a live lane phase offset. */
#if (XE_NP_APP_DUAL_DMA_DAR_ONLY_CIRCULAR_ENABLE != 0U)
#define XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_FIXED_LANE_B_OFFSET 0
#else
#define XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_FIXED_LANE_B_OFFSET 127
#endif
#endif
#ifndef XE_NP_APP_CAPTURE_DMA_DAR_ONLY_RING_BYTES
#define XE_NP_APP_CAPTURE_DMA_DAR_ONLY_RING_BYTES XE_NP_APP_DMA_NODE_BYTES
#endif
#if ((XE_NP_APP_CAPTURE_DMA_DAR_ONLY_RING_BYTES == 0U) || \
     (XE_NP_APP_CAPTURE_DMA_DAR_ONLY_RING_BYTES > 65535U) || \
     ((XE_NP_APP_CAPTURE_DMA_DAR_ONLY_RING_BYTES % XE_NP_CAPTURE_FRAME_BIT_COUNT) != 0U))
#error "DAR-only circular ring must be a nonzero, frame-aligned GPDMA block"
#endif
#ifndef XE_NP_APP_CAPTURE_DMA_DEST_BURST_LENGTH
/*
 * Keep the default at one beat.  Larger values let GPDMA combine writes to
 * SRAM while it services the request-paced GPIO source one beat at a time;
 * this is useful when characterising whether the destination-side FIFO can
 * bridge a linked-list reload.
 */
#define XE_NP_APP_CAPTURE_DMA_DEST_BURST_LENGTH 1U
#endif
#ifndef XE_NP_APP_CAPTURE_DMA_DEST_ALLOCATED_PORT
/* GPIO uses GPDMA port 0; SRAM3 capture normally uses destination port 1. */
#define XE_NP_APP_CAPTURE_DMA_DEST_ALLOCATED_PORT DMA_DEST_ALLOCATED_PORT1
#endif
#if ((XE_NP_APP_CAPTURE_DMA_DEST_ALLOCATED_PORT != DMA_DEST_ALLOCATED_PORT0) && \
     (XE_NP_APP_CAPTURE_DMA_DEST_ALLOCATED_PORT != DMA_DEST_ALLOCATED_PORT1))
#error "Capture destination must use GPDMA port 0 or port 1"
#endif
#define XE_NP_APP_CAPTURE_DMA_REPEAT_GROUP_MAX_NODES 16U
#define XE_NP_APP_CAPTURE_DMA_DIRECT_REPEAT_FULLSPAN_DEFER_ENABLE 1U
#define XE_NP_APP_CAPTURE_DMA_STABLE_WINDOW_DEFER_ENABLE \
    XE_NP_APP_EXPERIMENT_USB_STABLE_WINDOW_DEFER_ENABLE
#define XE_NP_APP_CAPTURE_DMA_HT_WINDOW_DEFER_ENABLE 0U
#define XE_NP_APP_CAPTURE_DMA_TC_WINDOW_DEFER_ENABLE 1U
#define XE_NP_APP_CAPTURE_DMA_TC_RELEASE_OFFSET_BYTES 64U
#define XE_NP_APP_CAPTURE_DMA_STABLE_WINDOW_RELEASE_BYTES \
    XE_NP_APP_EXPERIMENT_USB_STABLE_WINDOW_DEFER_BYTES
#define XE_NP_APP_USB_WINDOW_WARMUP_WINDOWS 0U
#define XE_NP_APP_CAPTURE_HALF_BOUNDARY_PROBE_ENABLE \
    XE_NP_APP_EXPERIMENT_HALF_BOUNDARY_PROBE_ENABLE
#define XE_NP_APP_CAPTURE_STORAGE_OFFSET_BYTES \
    XE_NP_APP_EXPERIMENT_CAPTURE_STORAGE_OFFSET_BYTES
#define XE_NP_APP_CAPTURE_STORAGE_OFFSET_MAX_BYTES 64U
#ifndef XE_NP_APP_USB_RING_PACKET_HEADROOM_ENABLE
#define XE_NP_APP_USB_RING_PACKET_HEADROOM_ENABLE 1U
#endif
#define XE_NP_APP_USB_RING_PACKET_HEADER_BYTES XE_NP_USB_STREAM_HEADER_BYTES
#define XE_NP_APP_USB_RING_PACKET_WINDOW_COUNT 4U
#define XE_NP_APP_USB_RING_PACKET_HEADROOM_TOTAL_BYTES \
    (XE_NP_APP_USB_RING_PACKET_HEADER_BYTES * XE_NP_APP_USB_RING_PACKET_WINDOW_COUNT)

#if (XE_NP_APP_INTERNAL_CAPTURE_STORAGE_BYTES == 0U)
#error "XE_NP_APP_INTERNAL_CAPTURE_STORAGE_BYTES must be nonzero"
#endif

#if ((XE_NP_APP_INTERNAL_CAPTURE_STORAGE_BYTES % XE_NP_APP_DMA_NODE_BYTES) != 0U)
#error "Internal capture ring must be an integer number of DMA nodes"
#endif

#if ((XE_NP_APP_INTERNAL_CAPTURE_STORAGE_BYTES % XE_NP_APP_USB_RING_PACKET_WINDOW_COUNT) != 0U)
#error "Internal capture ring must divide evenly into USB ring windows"
#endif
/*
 * Diagnostic alternative to the normal NP2 byte-lane read at GPIOE->IDR+1.
 * A halfword read from the aligned IDR address has a different AHB access
 * path; it is useful for separating byte-lane timing from PSB signal timing.
 */
#ifndef XE_NP_APP_CAPTURE_FORCE_NP2_HALFWORD_DIAG
#define XE_NP_APP_CAPTURE_FORCE_NP2_HALFWORD_DIAG 0U
#endif
#define XE_NP_APP_I2C_RECOVERY_PULSES 9U
#define XE_NP_APP_I2C_RECOVERY_DELAY_MS 1U
#define XE_NP_APP_CAPTURE_BENCH_DIAG_MAGIC 0x43415044UL
#define XE_NP_APP_CAPTURE_BENCH_DIAG_VERSION 4U
#define XE_NP_APP_CAPTURE_BENCH_DIAG_REASON_LOCK_TIMEOUT 1U
#define XE_NP_APP_CAPTURE_BENCH_DIAG_REASON_PATH_FAULT 2U
#define XE_NP_APP_CAPTURE_BENCH_DIAG_REASON_RUNTIME_CAPTURE_FAULT 3U
#define XE_NP_APP_CAPTURE_BENCH_DIAG_REASON_FROZEN_SNAPSHOT 4U
#define XE_NP_APP_CAPTURE_BENCH_SAMPLE_WORDS 16U
#define XE_NP_APP_CAPTURE_WRAP_PROBE_SIDE_BYTES 64U
#define XE_NP_APP_CAPTURE_WRAP_PROBE_TOTAL_BYTES (XE_NP_APP_CAPTURE_WRAP_PROBE_SIDE_BYTES * 2U)
#define XE_NP_APP_BENCH_CAPTURE_TIMING_SWEEP_ENABLE \
    XE_NP_APP_EXPERIMENT_TIMING_SWEEP_ENABLE
#ifndef XE_NP_APP_USB_START_TIMING_CANDIDATE
#define XE_NP_APP_USB_START_TIMING_CANDIDATE 1U
#endif
#define XE_NP_APP_RAW_SD_DIAG_MAGIC 0x52535744UL
#define XE_NP_APP_RAW_SD_DIAG_VERSION 7U
#define XE_NP_APP_RAW_SD_DIAG_REASON_START_ENTER 1U
#define XE_NP_APP_RAW_SD_DIAG_REASON_START_FAIL_PRECONDITION 2U
#define XE_NP_APP_RAW_SD_DIAG_REASON_START_FAIL_STORAGE 3U
#define XE_NP_APP_RAW_SD_DIAG_REASON_START_FAIL_CARD 4U
#define XE_NP_APP_RAW_SD_DIAG_REASON_START_FAIL_NO_PORT 5U
#define XE_NP_APP_RAW_SD_DIAG_REASON_START_OK 6U
#define XE_NP_APP_RAW_SD_DIAG_REASON_LAUNCH_BUSY 7U
#define XE_NP_APP_RAW_SD_DIAG_REASON_LAUNCH_FAIL 8U
#define XE_NP_APP_RAW_SD_DIAG_REASON_LAUNCH_OK 9U
#define XE_NP_APP_RAW_SD_DIAG_REASON_TIMEOUT 10U
#define XE_NP_APP_RAW_SD_DIAG_REASON_WRITE_ERROR 11U
#define XE_NP_APP_RAW_SD_DIAG_REASON_WRITE_COMPLETE 12U
#define XE_NP_APP_RAW_SD_DIAG_REASON_STORAGE_FAULT 13U
#define XE_NP_APP_RAW_SD_DIAG_REASON_WAIT_DATA 15U
#define XE_NP_APP_RAW_SD_DIAG_REASON_STAGE_START 16U
#define XE_NP_APP_RAW_SD_DIAG_REASON_STAGE_COMPLETE 17U
#define XE_NP_APP_RAW_SD_DIAG_REASON_CAPTURE_OVERRUN 18U
#define XE_NP_APP_RAW_SD_DIAG_REASON_PRODUCER_STOP 19U
#define XE_NP_APP_STREAM_DIAG_MAGIC 0x5354524DUL
#define XE_NP_APP_STREAM_DIAG_VERSION 4U
#if (XE_NP_APP_RAW_SD_SINK_ENABLE != 0U)
/*
 * A 512-byte SDMMC transaction per scheduler pass cannot sustain the raw PSB
 * stream.  Batch 32 KiB into SRAM1 before IDMA; capture DMA stays in SRAM3
 * and keeps higher interrupt priority, avoiding a shared live-ring reader.
 */
#ifndef XE_NP_APP_RAW_SD_MAX_BLOCKS_PER_WRITE
#define XE_NP_APP_RAW_SD_MAX_BLOCKS_PER_WRITE 64U
#endif
#define XE_NP_APP_RAW_SD_BLOCKS_PER_SERVICE 128U
#else
#ifndef XE_NP_APP_RAW_SD_MAX_BLOCKS_PER_WRITE
#define XE_NP_APP_RAW_SD_MAX_BLOCKS_PER_WRITE 1U
#endif
#define XE_NP_APP_RAW_SD_BLOCKS_PER_SERVICE 1U
#endif
#if (XE_NP_APP_CAPTURE_POSTHOC_ONLY != 0U)
#define XE_NP_APP_CAPTURE_SYNC_WATCHDOG_ENABLE XE_NP_APP_EXPERIMENT_SYNC_WATCHDOG_ENABLE
#define XE_NP_APP_CAPTURE_SYNC_WATCHDOG_RUNTIME_ENABLE \
    XE_NP_APP_EXPERIMENT_SYNC_WATCHDOG_ENABLE
#define XE_NP_APP_CAPTURE_FOREGROUND_QUEUE_ENABLE 0U
#define XE_NP_APP_CAPTURE_VALIDATION_ENABLE 0U
#define XE_NP_APP_CAPTURE_RATE_MONITOR_ENABLE 1U
#define XE_NP_APP_CAPTURE_DIAG_ENABLE 0U
#define XE_NP_APP_CAPTURE_SELFTEST_ENABLE 0U
#else
#define XE_NP_APP_CAPTURE_SYNC_WATCHDOG_ENABLE 0U
#define XE_NP_APP_CAPTURE_FOREGROUND_QUEUE_ENABLE 1U
#define XE_NP_APP_CAPTURE_VALIDATION_ENABLE 1U
#define XE_NP_APP_CAPTURE_RATE_MONITOR_ENABLE 1U
#define XE_NP_APP_CAPTURE_DIAG_ENABLE 1U
#define XE_NP_APP_CAPTURE_SELFTEST_ENABLE 0U
#endif
#define XE_NP_APP_RAW_SD_BLOCK_SIZE 512U
#define XE_NP_APP_RAW_SD_WRITE_TIMEOUT_MS 250U
#define XE_NP_APP_RAW_SD_RECOVER_TIMEOUT_MS 200U
#ifndef XE_NP_APP_DMA_IRQ_PREEMPT_PRIO
/* GPIO sampling priority is set in the GPDMA channel, not in this completion
 * IRQ.  The linked-list capture runs autonomously between notifications, so
 * this ISR must not starve OTG-HS endpoint service during USB-only capture. */
#define XE_NP_APP_DMA_IRQ_PREEMPT_PRIO 3U
#endif
#ifndef XE_NP_APP_CAPTURE_LINKED_LIST_PRIORITY
/* Capture requests are timer paced.  High is the production default; the
 * bench may lower this arbitration setting to qualify USB coexistence. */
#define XE_NP_APP_CAPTURE_LINKED_LIST_PRIORITY DMA_HIGH_PRIORITY
#endif
#ifndef XE_NP_APP_EXPERIMENT_CAPTURE_SUPPRESS_DMA_ENABLE
#define XE_NP_APP_EXPERIMENT_CAPTURE_SUPPRESS_DMA_ENABLE 0U
#endif
#ifndef XE_NP_APP_EXPERIMENT_SKIP_NRIC_CONFIG_ENABLE
#define XE_NP_APP_EXPERIMENT_SKIP_NRIC_CONFIG_ENABLE 0U
#endif
#define XE_NP_APP_DMA_IRQ_SUBPRIO 0U
#define XE_NP_APP_CAPTURE_DMA_HT_IRQ_ENABLE 1U
#define XE_NP_APP_SD_IRQ_PREEMPT_PRIO 1U
#define XE_NP_APP_SD_IRQ_SUBPRIO 0U
#define XE_NP_APP_BLE_IRQ_PREEMPT_PRIO 3U
#define XE_NP_APP_BLE_IRQ_SUBPRIO 0U
#define XE_NP_APP_RECORDER_BACKLOG_GUARD_BYTES XE_NP_APP_DMA_NODE_BYTES
#define XE_NP_APP_RECORDER_BACKLOG_HIGH_RATE_GUARD_BYTES \
    (XE_NP_APP_DMA_NODE_BYTES + XE_NP_APP_DMA_HALF_BYTES)
#define XE_NP_APP_RECORDER_BACKLOG_GRACE_MS 1U
#define XE_NP_APP_RECORDER_BACKLOG_HIGH_RATE_PCT2 45U
#define XE_NP_APP_RECORDER_RATE_MONITOR_INTERVAL_MS 250U
#define XE_NP_APP_RECORDER_RATE_TOLERANCE_PCT 35U
#define XE_NP_APP_RECORDER_RATE_BAD_SAMPLE_LIMIT 2U
#define XE_NP_APP_RECORDER_RATE_BACKLOG_TRIGGER_BYTES XE_NP_APP_DMA_HALF_BYTES
#define XE_NP_APP_RATE_MONITOR_INTERVAL_MS 50U
#define XE_NP_APP_RATE_MONITOR_TOLERANCE_PCT 20U
#define XE_NP_APP_RATE_MONITOR_BAD_SAMPLE_LIMIT 2U
#define XE_NP_APP_SYNC_WATCHDOG_SAMPLE_FRAMES_UNLOCKED 8U
#define XE_NP_APP_SYNC_WATCHDOG_SAMPLE_FRAMES_LOCKED 8U
#define XE_NP_APP_SYNC_WATCHDOG_SAMPLE_FRAMES_STARTUP XE_NP_CAPTURE_FRAMES_PER_ULTRAFRAME
#define XE_NP_APP_SYNC_WATCHDOG_MAX_PREFIX_BYTES XE_NP_CAPTURE_DMA_NODE_BYTES
#define XE_NP_APP_SYNC_WATCHDOG_STARTUP_OK_WINDOWS 2U
#define XE_NP_APP_SYNC_WATCHDOG_FAIL_LIMIT_UNLOCKED 4U
#define XE_NP_APP_SYNC_WATCHDOG_RUNTIME_STRIDE_LOCKED 1U
#define XE_NP_APP_DMA_WINDOW_QUEUE_LEN 16U
#define XE_NP_APP_DMA_WINDOW_QUEUE_MASK (XE_NP_APP_DMA_WINDOW_QUEUE_LEN - 1U)
#define XE_NP_APP_DMA_WINDOWS_PER_SERVICE 4U
#define XE_NP_APP_POWER_RAIL_SETTLE_MS 250U
#define XE_NP_APP_PROBE_RESET_SETTLE_MS 50U
#define XE_NP_APP_CAPTURE_SYNC_LOCK_TIMEOUT_MS 1000U
#define XE_NP_APP_PROBE_RUNTIME_VERIFY_INTERVAL_MS 250U
#define XE_NP_APP_PROBE_RETRY_DELAY_MS 250U
#define XE_NP_APP_DMA_FAST_EVENT_FLAGS (DMA_CSR_HTF | DMA_CSR_TCF)
#define XE_NP_APP_DMA_FAST_FALLBACK_FLAGS DMA_CSR_SUSPF
#define XE_NP_APP_DMA_FAULT_SOURCE_FAST_ERROR 1U
#define XE_NP_APP_DMA_FAULT_SOURCE_FAST_FALLBACK 2U
#define XE_NP_APP_DMA_FAULT_SOURCE_ERROR_CALLBACK 3U
#define XE_NP_APP_DMA_DISABLE_IT_MASK \
    (DMA_IT_TC | DMA_IT_HT | DMA_IT_DTE | DMA_IT_ULE | DMA_IT_USE | DMA_IT_TO | DMA_IT_SUSP)
#define XE_NP_APP_CAPTURE_BLOCKING_FLAGS                                     \
    (XE_NP_CAPTURE_FLAG_CLOCK_MODE_UNSUPPORTED | XE_NP_CAPTURE_FLAG_TIMER_MISSING | \
     XE_NP_CAPTURE_FLAG_UNSUPPORTED_PROBE_MASK | XE_NP_CAPTURE_FLAG_STORAGE_LAYOUT_UNSUPPORTED | \
     XE_NP_CAPTURE_FLAG_GPIO_LAYOUT_UNSUPPORTED)

#define XE_NP_APP_PROBE_VERIFY_FLAG_I2C_FAIL 0x01U
#define XE_NP_APP_PROBE_VERIFY_FLAG_CAL_MOD_MISMATCH 0x04U
#define XE_NP_APP_PROBE_VERIFY_FLAG_SYNC_MISMATCH 0x08U
#define XE_NP_APP_PROBE_VERIFY_FLAG_REC_MOD_MISMATCH 0x10U
#define XE_NP_APP_PROBE_VERIFY_FLAG_OP_MODE_MISMATCH 0x20U

#define XE_NP_APP_PROBE_DIAG_FAILURE_NONE 0U
#define XE_NP_APP_PROBE_DIAG_FAILURE_CONFIG_NOT_READY 1U
#define XE_NP_APP_PROBE_DIAG_FAILURE_PING_FAIL 2U
#define XE_NP_APP_PROBE_DIAG_FAILURE_APPLY_FAIL 3U
#define XE_NP_APP_PROBE_DIAG_FAILURE_STORAGE_WAIT 4U
#define XE_NP_APP_PROBE_DIAG_FAILURE_DMA_START_FAIL 5U
#define XE_NP_APP_PROBE_DIAG_FAILURE_LEGACY_FS_FAIL 6U
#define XE_NP_APP_PROBE_DIAG_FAILURE_CAPTURE_PATH_FAULT 7U
#define XE_NP_APP_PROBE_DIAG_FAILURE_VERIFY_RUN_FAIL 8U
#define XE_NP_APP_PROBE_DIAG_FAILURE_CAPTURE_LOCK_TIMEOUT 9U
#define XE_NP_APP_PROBE_DIAG_FAILURE_RUNTIME_MCLK_FAULT 10U
#define XE_NP_APP_PROBE_DIAG_FAILURE_RUNTIME_CAPTURE_FAULT 11U
#define XE_NP_APP_PROBE_DIAG_FAILURE_RUNTIME_VERIFY_FAULT 12U
#define XE_NP_APP_PROBE_DIAG_FAILURE_RAW_SD_SINK_FAIL 13U

#define XE_NP_APP_PROBE_DIAG_STATE_INITIALIZED 0x01U
#define XE_NP_APP_PROBE_DIAG_STATE_INIT_FAILED 0x02U
#define XE_NP_APP_PROBE_DIAG_STATE_INIT_ERROR_REPORTED 0x04U
#define XE_NP_APP_PROBE_DIAG_STATE_I2C_READY 0x08U
#define XE_NP_APP_PROBE_DIAG_STATE_BASE_CONFIGURED 0x10U
#define XE_NP_APP_PROBE_DIAG_STATE_POWER_RAILS_ENABLED 0x20U
#define XE_NP_APP_PROBE_DIAG_STATE_GLOBAL_RESET_RELEASED 0x40U
#define XE_NP_APP_PROBE_DIAG_STATE_RECORD_RESET_RELEASED 0x80U

#define XE_NP_APP_PROBE_DIAG_RAIL_VEE_EN 0x01U
#define XE_NP_APP_PROBE_DIAG_RAIL_VCC_EN 0x02U
#define XE_NP_APP_PROBE_DIAG_RAIL_NP_NRST 0x04U
#define XE_NP_APP_PROBE_DIAG_RAIL_NP_REC_NRST 0x08U
#define XE_NP_APP_PROBE_DIAG_RAIL_SD_VSEL 0x10U
#define XE_NP_APP_PROBE_DIAG_RAIL_SCL_HIGH 0x20U
#define XE_NP_APP_PROBE_DIAG_RAIL_SDA_HIGH 0x40U

#if ((XE_NP_APP_DMA_WINDOW_QUEUE_LEN == 0U) || \
     ((XE_NP_APP_DMA_WINDOW_QUEUE_LEN & XE_NP_APP_DMA_WINDOW_QUEUE_MASK) != 0U))
#error "XE_NP_APP_DMA_WINDOW_QUEUE_LEN must be a non-zero power of two"
#endif

#if ((XE_NP_APP_CAPTURE_PSRAM_ENABLE != 0U) && \
     (XE_NP_APP_CAPTURE_PSRAM_BYTES > XE_NP_PSRAM_VALIDATED_SIZE_BYTES))
#error "PSRAM capture ring exceeds validated APS6404L capacity"
#endif

#if ((XE_NP_APP_CAPTURE_PSRAM_ENABLE != 0U) && \
     (XE_NP_APP_CAPTURE_PSRAM_NODE_COUNT > XE_NP_APP_DMA_NODE_COUNT_MAX))
#error "PSRAM capture ring exceeds capture DMA node descriptor capacity"
#endif

typedef struct
{
    uint32_t offset_bytes;
    uint32_t size_bytes;
    uint8_t node_index;
    uint8_t half_index;
    uint64_t total_bytes_end;
} XE_NP_AppDmaWindow;

typedef struct
{
    DMA_HandleTypeDef hdma;
    DMA_QListTypeDef queue;
    DMA_NodeTypeDef nodes[XE_NP_APP_DMA_NODE_COUNT_MAX];
    XE_NP_AppDmaWindow pending_windows[XE_NP_APP_DMA_WINDOW_QUEUE_LEN];
    XE_NP_AppDmaWindow stable_window;
    XE_NP_CaptureState *capture;
    XE_NP_CapturePort *capture_port;
    CE32_systemParam *sys_param;
    TIM_HandleTypeDef *htim;
    volatile uint32_t *gpio_idr;
    IRQn_Type irqn;
    uint32_t dma_request;
    uint32_t timer_dma_mask;
    uint32_t node_bytes;
    uint8_t port_index;
    uint8_t node_count;
    uint8_t current_node_index;
    volatile uint8_t pending_read_idx;
    volatile uint8_t pending_write_idx;
    uint8_t stable_window_valid;
    uint8_t usb_active_window_valid;
    uint8_t pending_peak_depth;
    uint8_t usb_window_warmup_remaining;
    uint8_t stable_window_reserved;
    uint8_t active;
    uint8_t validation_error_seen;
    uint8_t dma_error_seen;
    uint8_t backlog_error_seen;
    uint8_t live_decode_probe_mask;
    volatile uint8_t backlog_resync_requested;
    volatile uint8_t sync_watchdog_pending;
    uint8_t sync_watchdog_fail_count;
    uint8_t recorder_backlog_fault_seen;
    uint8_t rate_error_seen;
    uint8_t rate_bad_samples;
    uint8_t rate_ratio_pct2;
    uint16_t byte_sync_best_offset;
    uint16_t byte_sync_best_sync0;
    uint16_t byte_sync_best_sync1;
    uint32_t byte_sync_best_flags0;
    uint32_t byte_sync_best_flags1;
    uint8_t byte_sync_best_clean_frames;
    uint8_t byte_sync_best_sync_valid_frames;
    uint8_t byte_sync_best_frame_skip;
    uint8_t byte_sync_best_phase;
    uint8_t byte_sync_sample_frames;
    uint8_t capture_timing_flags;
    uint8_t capture_bytes_per_tick;
    uint8_t usb_packet_headroom_enabled;
    uint8_t usb_packet_window_count;
    uint8_t dma_source_width_bytes;
    uint8_t gpio_source_addr_lsb;
    uint8_t direct_repeat_mode;
    uint8_t dma_queue_node_count;
    uint8_t dma_repeat_block_count;
    /* ISR-only divider for GPDMA repeated-block completion events. */
    uint16_t dma_repeat_event_target;
    uint16_t dma_repeat_event_count;
    uint16_t usb_packet_header_bytes;
    uint32_t usb_packet_window_payload_bytes;
    uint32_t sync_watchdog_size_bytes;
    uint32_t sync_watchdog_offset_bytes;
    uint64_t sync_watchdog_total_bytes_end;
    uint32_t recorder_backlog_high_since_tick_ms;
    uint32_t sync_watchdog_ok_count;
    uint32_t sync_watchdog_error_count;
    uint32_t continuity_ok_count;
    uint32_t continuity_error_count;
    uint32_t continuity_last_expected_counter;
    uint32_t continuity_last_observed_counter;
    uint32_t continuity_last_frame_delta;
    uint32_t pending_overflow_count;
    uint32_t windows_processed;
    uint32_t live_decode_ok_count;
    uint32_t live_decode_fail_count;
    uint32_t last_live_decode_tick_ms;
    uint32_t last_rate_sample_tick_ms;
    uint32_t stable_window_release_tick_ms;
    uint64_t last_rate_sample_bytes;
    uint64_t continuity_last_abs_offset_bytes;
    uint32_t continuity_last_counter;
    uint8_t continuity_last_valid;
    volatile uint8_t wrap_probe_tail_ready;
    volatile uint8_t wrap_probe_ready;
    uint8_t wrap_probe_valid;
    uint8_t wrap_probe_reserved;
    uint32_t wrap_probe_count;
    uint32_t wrap_probe_tail_offset;
    uint32_t wrap_probe_head_offset;
    uint64_t wrap_probe_tail_total_bytes_end;
    uint64_t wrap_probe_head_total_bytes_end;
    uint8_t wrap_probe_bytes[XE_NP_APP_CAPTURE_WRAP_PROBE_TOTAL_BYTES];
    uint32_t last_queued_offset_bytes;
    uint32_t last_queued_size_bytes;
    uint32_t last_dequeued_offset_bytes;
    uint32_t last_dequeued_size_bytes;
    uint32_t usb_active_window_consumed_bytes;
    uint32_t queued_size_min_bytes;
    uint32_t queued_size_max_bytes;
    uint32_t dequeued_size_min_bytes;
    uint32_t dequeued_size_max_bytes;
    uint32_t queued_window_count;
    uint32_t dequeued_window_count;
    XE_NP_AppDmaWindow usb_active_window;
    uint32_t dma_fault_tick_ms;
    uint32_t dma_fault_source;
    uint32_t dma_fault_csr;
    uint32_t dma_fault_ccr;
    uint32_t dma_fault_ctr1;
    uint32_t dma_fault_ctr2;
    uint32_t dma_fault_cbr1;
    uint32_t dma_fault_csar;
    uint32_t dma_fault_cdar;
    uint32_t dma_fault_cllr;
    uint32_t dma_fault_error_code;
    uint32_t first_backlog_resync_tick_ms;
    uint32_t first_backlog_total_bytes_low;
    uint32_t first_backlog_last_queued_offset_bytes;
    uint32_t first_backlog_last_dequeued_offset_bytes;
    uint8_t first_backlog_resync_valid;
    uint8_t first_backlog_reason;
    uint8_t first_backlog_pending_read_idx;
    uint8_t first_backlog_pending_write_idx;
    uint8_t first_backlog_current_depth;
    uint8_t first_backlog_max_pending_windows;
    uint8_t first_backlog_usb_active_window_valid;
    uint8_t first_backlog_stable_window_valid;
#if (XE_NP_APP_EXPERIMENT_DMA_IRQ_JITTER_DETECT_ENABLE != 0U)
    uint32_t dma_irq_jitter_last_cnt_ht;
    uint32_t dma_irq_jitter_last_cnt_tc;
    uint32_t dma_irq_jitter_prev_delta_ht;
    uint32_t dma_irq_jitter_prev_delta_tc;
    uint32_t dma_irq_jitter_delta_min_ht;
    uint32_t dma_irq_jitter_delta_min_tc;
    uint32_t dma_irq_jitter_delta_max_ht;
    uint32_t dma_irq_jitter_delta_max_tc;
    uint32_t dma_irq_jitter_event_count_ht;
    uint32_t dma_irq_jitter_event_count_tc;
    uint32_t dma_irq_jitter_glitch_count_ht;
    uint32_t dma_irq_jitter_glitch_count_tc;
#endif
} XE_NP_AppDmaPort;

#if (XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_INTERLEAVE_ENABLE != 0U)
typedef struct
{
    DMA_HandleTypeDef hdma;
    DMA_QListTypeDef queue;
    DMA_NodeTypeDef nodes[2];
} XE_NP_AppQuadDmaLane;

#if (XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_TIMED_SHADOW_ENABLE != 0U)
static CE32_ALIGN(XE_NP_CAPTURE_DCACHE_LINE_BYTES) uint8_t
    xe_np_quad_dma_timed_shadow_storage[
        XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT *
        XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_BLOCK_BYTES *
        XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_REPEAT_COUNT];
static XE_NP_AppQuadDmaLane xe_np_quad_dma_timed_shadow_lanes[
    XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT];
#endif
#endif

typedef struct
{
    uint32_t next_retry_tick_ms;
    uint32_t stage_deadline_tick_ms;
    uint8_t initialized;
    uint8_t init_failed;
    uint8_t init_error_reported;
    uint8_t i2c_ready;
    uint8_t base_configured;
    uint8_t shank_defaults_active;
    uint8_t bringup_stage;
    uint8_t active_probe_index;
    uint8_t last_status[XE_NP_NEUROPIXELS_MAX_PROBES];
    uint8_t last_cal_mod[XE_NP_NEUROPIXELS_MAX_PROBES];
    uint8_t last_sync[XE_NP_NEUROPIXELS_MAX_PROBES];
    uint8_t last_rec_mod[XE_NP_NEUROPIXELS_MAX_PROBES];
    uint8_t last_op_mode[XE_NP_NEUROPIXELS_MAX_PROBES];
    uint8_t last_verify_flags[XE_NP_NEUROPIXELS_MAX_PROBES];
} XE_NP_AppProbeState;

typedef struct
{
    uint32_t tick_ms;
    uint32_t i2c_error_code;
    uint32_t i2c_isr;
    uint32_t i2c_cr1;
    uint32_t i2c_cr2;
    uint32_t gpioc_idr;
    uint8_t version;
    uint8_t stage;
    uint8_t probe_index;
    uint8_t failure;
    uint8_t hal_status;
    uint8_t state_flags;
    uint8_t rail_flags;
    uint8_t active_probe_mask;
} XE_NP_AppProbeDiag;

typedef char xe_np_app_probe_diag_size_check[
    (sizeof(XE_NP_AppProbeDiag) == 32U) ? 1 : -1];

typedef struct
{
    uint32_t magic;
    uint32_t version;
    uint32_t tick_ms;
    uint32_t probe_index;
    uint32_t target_dev_addr;
    uint32_t last_addr;
    uint32_t last_status;
    uint32_t last_error_code;
    uint32_t last_isr;
    uint32_t last_cr1;
    uint32_t last_cr2;
    uint32_t ack_bitmap[4];
    uint32_t timeout_bitmap[4];
    uint32_t error_bitmap[4];
    uint32_t busy_bitmap[4];
    uint16_t ok_count;
    uint16_t error_count;
    uint16_t busy_count;
    uint16_t timeout_count;
    uint8_t first_ok_addr;
    uint8_t first_error_addr;
    uint8_t first_busy_addr;
    uint8_t first_timeout_addr;
} XE_NP_AppI2cScanDiag;

typedef struct
{
    uint32_t total_bytes_low;
    uint32_t dma_windows_captured;
    uint32_t storage_bytes;
    uint32_t raw_log_state;
    uint32_t sync_watchdog_ok_count;
    uint32_t sync_watchdog_error_count;
    uint32_t windows_processed;
    uint32_t tim_cnt;
    uint32_t tim_cr1;
    uint32_t tim_smcr;
    uint32_t tim_dier;
    uint32_t tim_sr;
    uint32_t dma_csr;
    uint32_t dma_ccr;
    uint32_t dma_ctr1;
    uint32_t dma_ctr2;
    uint32_t dma_cbr1;
    uint32_t dma_csar;
    uint32_t dma_cdar;
    uint32_t dma_cllr;
    uint32_t dma_fault_tick_ms;
    uint32_t dma_fault_source;
    uint32_t dma_fault_csr;
    uint32_t dma_fault_ccr;
    uint32_t dma_fault_ctr1;
    uint32_t dma_fault_ctr2;
    uint32_t dma_fault_cbr1;
    uint32_t dma_fault_csar;
    uint32_t dma_fault_cdar;
    uint32_t dma_fault_cllr;
    uint32_t dma_fault_error_code;
    uint16_t frame_alignment_ticks;
    uint8_t active;
    uint8_t active_probe_mask;
    uint8_t validated_probe_mask;
    uint8_t frame_alignment_locked;
    uint8_t pending_read_idx;
    uint8_t pending_write_idx;
    uint8_t pending_peak_depth;
    uint8_t sync_watchdog_pending;
    uint8_t sync_watchdog_fail_count;
    uint8_t dma_error_seen;
    uint8_t validation_error_seen;
    uint8_t rate_error_seen;
    uint16_t byte_sync_best_offset;
    uint16_t byte_sync_best_sync0;
    uint16_t byte_sync_best_sync1;
    uint32_t byte_sync_best_flags0;
    uint32_t byte_sync_best_flags1;
    uint8_t byte_sync_best_clean_frames;
    uint8_t byte_sync_best_sync_valid_frames;
    uint8_t byte_sync_best_frame_skip;
    uint8_t byte_sync_best_phase;
    uint8_t byte_sync_sample_frames;
    uint8_t capture_timing_flags;
    uint8_t live_decode_probe_mask;
    uint8_t capture_bytes_per_tick;
    uint8_t dma_source_width_bytes;
    uint8_t gpio_source_addr_lsb;
    uint8_t dma_queue_node_count;
    uint8_t dma_repeat_block_count;
    uint32_t continuity_ok_count;
    uint32_t continuity_error_count;
    uint32_t continuity_last_expected_counter;
    uint32_t continuity_last_observed_counter;
    uint32_t continuity_last_frame_delta;
    uint32_t live_decode_ok_count;
    uint32_t live_decode_fail_count;
    uint16_t sample_words[XE_NP_APP_CAPTURE_BENCH_SAMPLE_WORDS];
    uint32_t wrap_probe_count;
    uint32_t wrap_probe_tail_offset;
    uint32_t wrap_probe_head_offset;
    uint32_t wrap_probe_tail_total_low;
    uint32_t wrap_probe_head_total_low;
    uint16_t wrap_probe_sample_bytes;
    uint8_t wrap_probe_valid;
    uint8_t wrap_probe_reserved;
    uint8_t wrap_probe_bytes[XE_NP_APP_CAPTURE_WRAP_PROBE_TOTAL_BYTES];
    uint32_t first_backlog_resync_tick_ms;
    uint32_t first_backlog_total_bytes_low;
    uint32_t first_backlog_last_queued_offset_bytes;
    uint32_t first_backlog_last_dequeued_offset_bytes;
    uint8_t first_backlog_resync_valid;
    uint8_t first_backlog_reason;
    uint8_t first_backlog_pending_read_idx;
    uint8_t first_backlog_pending_write_idx;
    uint8_t first_backlog_current_depth;
    uint8_t first_backlog_max_pending_windows;
    uint8_t first_backlog_usb_active_window_valid;
    uint8_t first_backlog_stable_window_valid;
#if (XE_NP_APP_EXPERIMENT_DMA_IRQ_JITTER_DETECT_ENABLE != 0U)
    uint32_t dma_irq_jitter_last_cnt_ht;
    uint32_t dma_irq_jitter_last_cnt_tc;
    uint32_t dma_irq_jitter_prev_delta_ht;
    uint32_t dma_irq_jitter_prev_delta_tc;
    uint32_t dma_irq_jitter_delta_min_ht;
    uint32_t dma_irq_jitter_delta_min_tc;
    uint32_t dma_irq_jitter_delta_max_ht;
    uint32_t dma_irq_jitter_delta_max_tc;
    uint32_t dma_irq_jitter_event_count_ht;
    uint32_t dma_irq_jitter_event_count_tc;
    uint32_t dma_irq_jitter_glitch_count_ht;
    uint32_t dma_irq_jitter_glitch_count_tc;
#endif
} XE_NP_AppCaptureBenchPortDiag;

typedef struct
{
    uint32_t magic;
    uint32_t version;
    uint32_t tick_ms;
    uint32_t reason;
    uint32_t runtime_flags;
    uint32_t capture_flags;
    uint32_t gpioe_idr;
    uint32_t gpiod_idr;
    uint32_t gpioc_idr;
    uint8_t active_probe_mask;
    uint8_t capture_layout;
    uint8_t active_port_count;
    uint8_t reserved0;
    XE_NP_AppCaptureBenchPortDiag gpioe;
    XE_NP_AppCaptureBenchPortDiag gpiod;
} XE_NP_AppCaptureBenchDiag;

typedef struct
{
    uint32_t magic;
    uint32_t version;
    uint32_t tick_ms;
    uint32_t next_retry_tick_ms;
    uint32_t stage_deadline_tick_ms;
    uint32_t runtime_flags;
    uint32_t capture_flags;
    uint32_t tim2_cr1;
    uint32_t tim2_smcr;
    uint32_t tim2_dier;
    uint32_t tim2_sr;
    uint32_t tim2_cnt;
    uint32_t psb_clock_probe_count;
    uint32_t psb_clock_probe_flags;
    uint32_t psb_clock_probe_gpioa_start;
    uint32_t psb_clock_probe_gpioa_end;
    uint32_t psb_clock_probe_tim2_sr;
    uint32_t dma12_csr;
    uint32_t dma12_ccr;
    uint32_t dma12_cbr1;
    uint32_t dma12_csar;
    uint32_t dma12_cdar;
    uint32_t dma12_cllr;
    uint32_t dma12_last_queued_offset_bytes;
    uint32_t dma12_last_queued_size_bytes;
    uint32_t dma12_last_dequeued_offset_bytes;
    uint32_t dma12_last_dequeued_size_bytes;
    uint32_t dma12_queued_size_min_bytes;
    uint32_t dma12_queued_size_max_bytes;
    uint32_t dma12_dequeued_size_min_bytes;
    uint32_t dma12_dequeued_size_max_bytes;
    uint32_t dma12_queued_window_count;
    uint32_t dma12_dequeued_window_count;
    uint32_t gpioa_idr;
    uint32_t gpiob_idr;
    uint32_t gpioc_idr;
    uint32_t gpioe_idr;
    uint32_t i2c4_error_code;
    uint32_t i2c4_isr;
    uint32_t nric_tick_ms;
    uint32_t nric_i2c_error_code;
    uint32_t nric_i2c_isr;
    uint8_t bringup_stage;
    uint8_t initialized;
    uint8_t init_failed;
    uint8_t i2c_ready;
    uint8_t base_configured;
    uint8_t active_probe_index;
    uint8_t active_probe_mask;
    uint8_t record_reset_released;
    uint8_t global_reset_released;
    uint8_t nric_step;
    uint8_t nric_reg_addr;
    uint8_t nric_hal_status;
    uint8_t nric_status_reg;
    uint8_t nric_read_value;
    uint8_t nric_expected_value;
    uint8_t last_status_np2;
    uint8_t last_verify_flags_np2;
    uint8_t last_cal_mod_np2;
    uint8_t last_sync_np2;
    uint8_t last_rec_mod_np2;
    uint8_t last_op_mode_np2;
    uint8_t dma12_direct_repeat_mode;
    uint8_t dma12_queue_node_count;
    uint8_t dma12_repeat_block_count;
    uint8_t dma12_reserved0;
} XE_NP_AppStreamDiag;

typedef enum
{
    XE_NP_APP_PROBE_STAGE_IDLE = 0U,
    XE_NP_APP_PROBE_STAGE_WAIT_POWER_RAILS,
    XE_NP_APP_PROBE_STAGE_WAIT_GLOBAL_RESET,
    XE_NP_APP_PROBE_STAGE_PING_PROBES,
    XE_NP_APP_PROBE_STAGE_PROGRAM_PROBES,
    XE_NP_APP_PROBE_STAGE_START_CAPTURE,
    XE_NP_APP_PROBE_STAGE_WAIT_RECORD_RESET,
    XE_NP_APP_PROBE_STAGE_WAIT_CAPTURE_LOCK,
    XE_NP_APP_PROBE_STAGE_WAIT_POWER_OFF = 8U,
    XE_NP_APP_PROBE_STAGE_WAIT_CLOCK_STABLE = 9U
} XE_NP_AppProbeBringUpStage;

typedef struct
{
    XE_NP_NricConfig probe_config[XE_NP_NEUROPIXELS_MAX_PROBES];
    XE_NP_AppProbeConfigState state;
    uint8_t initialized;
} XE_NP_AppProbeConfigStore;

typedef struct
{
    dataMGR *mgr;
    uint32_t next_block;
    uint32_t end_block;
    uint32_t blocks_written;
    uint32_t blocks_dropped;
    uint8_t out_idx;
    uint8_t active;
    uint8_t error_seen;
} XE_NP_AppRawSdPort;

typedef struct
{
    XE_NP_AppRawSdPort gpioe;
    XE_NP_AppRawSdPort gpiod;
    uint32_t total_blocks_written;
    uint32_t total_blocks_dropped;
    uint32_t in_flight_bytes;
    uint32_t in_flight_start_tick_ms;
    uint16_t in_flight_blocks;
    uint8_t next_port_index;
    uint8_t in_flight_port_index;
    uint8_t active;
    uint8_t error_reported;
    uint8_t write_in_flight;
    volatile uint8_t write_complete;
    volatile uint8_t write_error;
    uint8_t stage_initialized;
    volatile uint8_t stage_busy;
    volatile uint8_t stage_complete;
    volatile uint8_t stage_error;
    uint8_t staged_port_index;
    uint16_t staged_blocks;
    uint32_t staged_bytes;
    uint32_t last_telemetry_tick_ms;
    uint32_t service_call_count;
    uint32_t service_state;
    DMA_HandleTypeDef stage_dma;
    uint8_t stress_writer;
#if (XE_NP_APP_RAW_SD_LINKED_STREAM_ENABLE != 0U)
    SD_DMALinkNodeTypeDef linked_nodes[XE_NP_APP_RAW_SD_LL_NODE_COUNT_MAX];
    SD_DMALinkedListTypeDef linked_list;
    XE_NP_AppRawSdPort *linked_node_port[XE_NP_APP_RAW_SD_LL_NODE_COUNT_MAX];
    uint32_t linked_node_bytes[XE_NP_APP_RAW_SD_LL_NODE_COUNT_MAX];
    volatile uint8_t linked_nodes_complete;
    uint8_t linked_node_count;
    uint8_t linked_nodes_released;
    uint8_t linked_data_end_seen;
    uint8_t linked_active;
    uint32_t linked_batches_completed;
    uint32_t linked_nodes_retired;
    uint32_t linked_last_batch_ms;
    uint32_t linked_worst_batch_ms;
    uint32_t linked_reserved_bytes;
    uint8_t linked_rearm_mask;
    uint8_t linked_nodes_rearmed;
#endif
    CE32_ALIGN(XE_NP_CAPTURE_DCACHE_LINE_BYTES) uint8_t scratch[
        XE_NP_APP_RAW_SD_BLOCK_SIZE * XE_NP_APP_RAW_SD_MAX_BLOCKS_PER_WRITE];
} XE_NP_AppRawSdSink;

typedef struct
{
    uint32_t magic;
    uint32_t version;
    uint32_t tick_ms;
    uint32_t reason;
    uint32_t runtime_flags;
    uint32_t capture_flags;
    uint32_t raw_bytes_per_sec;
    uint32_t sd_budget_bytes_per_sec;
    uint32_t sd_speed_mode;
    uint32_t card_state;
    uint32_t hal_state;
    uint32_t hal_context;
    uint32_t sdmmc_sta;
    uint32_t sdmmc_dctrl;
    uint32_t total_blocks_written;
    uint32_t total_blocks_dropped;
    uint32_t port_blocks_written;
    uint32_t port_blocks_dropped;
    uint32_t port_next_block;
    uint32_t port_end_block;
    uint32_t in_flight_bytes;
    uint32_t hal_status;
    uint16_t in_flight_blocks;
    uint8_t active;
    uint8_t write_in_flight;
    uint8_t write_complete;
    uint8_t write_error;
    uint8_t in_flight_port_index;
    uint8_t port_index;
    uint32_t gpioe_available_bytes;
    uint32_t gpiod_available_bytes;
    uint32_t stage_bytes;
    uint16_t stage_blocks;
    uint8_t stage_busy;
    uint8_t stage_complete;
    uint8_t stage_error;
    uint8_t stage_port_index;
    uint32_t gpioe_total_bytes_low;
    uint32_t gpiod_total_bytes_low;
    uint32_t gpioe_raw_log_state;
    uint32_t gpiod_raw_log_state;
    /* Producer/consumer ownership evidence.  These fields make a lost raw
     * consumer arm distinguishable from a capture or SDMMC throughput fault. */
    uint32_t gpioe_active_out_mask;
    uint32_t gpiod_active_out_mask;
    uint32_t gpioe_in_ptr;
    uint32_t gpiod_in_ptr;
    uint32_t gpioe_out_ptr;
    uint32_t gpiod_out_ptr;
    uint32_t gpioe_buffer_used;
    uint32_t gpiod_buffer_used;
    uint32_t gpioe_dma_csr;
    uint32_t gpioe_dma_ccr;
    uint32_t gpioe_dma_cbr1;
    uint32_t gpioe_dma_cllr;
    uint32_t gpiod_dma_csr;
    uint32_t gpiod_dma_ccr;
    uint32_t gpiod_dma_cbr1;
    uint32_t gpiod_dma_cllr;
    uint32_t tim2_cnt;
    uint32_t tim2_dier;
    uint32_t tim8_cnt;
    uint32_t tim8_dier;
    uint32_t capture_shutdown_reason;
    uint32_t service_call_count;
    uint32_t service_state;
    /* Channel 14 state at the instant a staged raw write is diagnosed. */
    uint32_t stage_dma_csr;
    uint32_t stage_dma_ccr;
    uint32_t stage_dma_ctr1;
    uint32_t stage_dma_ctr2;
    uint32_t stage_dma_cbr1;
    uint32_t stage_dma_csar;
    uint32_t stage_dma_cdar;
    uint32_t stage_dma_cllr;
    uint32_t linked_batch_bytes;
    uint32_t linked_batches_completed;
    uint32_t linked_nodes_retired;
    uint32_t linked_timing;
    uint32_t linked_elapsed;
} XE_NP_AppRawSdDiag;

static XE_NP_Runtime xe_np_runtime;
static CE32_ALIGN(XE_NP_CAPTURE_DCACHE_LINE_BYTES) uint32_t
    xe_np_raw_capture_storage[(XE_NP_APP_INTERNAL_CAPTURE_STORAGE_BYTES +
                               XE_NP_APP_CAPTURE_STORAGE_OFFSET_MAX_BYTES +
                               XE_NP_APP_USB_RING_PACKET_HEADROOM_TOTAL_BYTES +
                               sizeof(uint32_t) - 1U) /
                              sizeof(uint32_t)] __attribute__((section(XE_NP_APP_CAPTURE_STORAGE_SECTION)));
#if ((XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_ENABLE != 0U) && \
     (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_PUBLISH_ENABLE != 0U) && \
     (XE_NP_APP_EXPERIMENT_SYNC_WATCHDOG_ENABLE != 0U))
static CE32_ALIGN(XE_NP_CAPTURE_DCACHE_LINE_BYTES) uint8_t
    xe_np_gpioe_interleaved_validation_bytes[
        XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_VALIDATION_BYTES];
static uint64_t xe_np_gpioe_interleaved_validation_last_pair_end;
/* The two TIM8 DMA request paths have a deterministic, board-specific phase. */
static int8_t xe_np_gpioe_interleaved_validation_lane_b_offset;
static uint8_t xe_np_gpioe_interleaved_validation_lane_b_offset_valid;
/* Absolute per-lane cursor for the USB-only chronological export. */
static uint64_t xe_np_gpioe_interleaved_usb_next_lane_byte;
static uint8_t xe_np_gpioe_interleaved_usb_next_lane_byte_valid;
static uint8_t xe_np_gpioe_interleaved_usb_next_lane_is_b;
static uint8_t xe_np_gpioe_interleaved_usb_dma_batch_claimed;
static uint8_t xe_np_gpioe_interleaved_usb_dma_batch_committed;
static uint32_t xe_np_gpioe_interleaved_usb_dma_batch_lane_b_packets;
static uint32_t xe_np_gpioe_interleaved_usb_dma_batch_payload_bytes;
static volatile uint32_t xe_np_gpioe_interleaved_usb_export_debug;
#endif
#if (XE_NP_APP_CAPTURE_FOREGROUND_QUEUE_ENABLE != 0U)
static XE_NP_ElectrodePacket xe_np_live_decode_packet;
#endif
static XE_NP_AppDmaPort xe_np_dma_gpioe;
static XE_NP_AppDmaPort xe_np_dma_gpiod;
#if (XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_INTERLEAVE_ENABLE != 0U)
static XE_NP_AppQuadDmaLane xe_np_quad_dma_lanes[
    XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT];
#if (XE_NP_APP_QUAD_DMA_LLI_SRAM2 != 0U)
/* Only the immutable DMA words live in SRAM2, not CPU-owned HAL handles.
 * One 32-byte cache line per lane, entirely within one CLBAR 64-KiB window. */
static uint32_t xe_np_quad_dma_reload_words[12][8]
    __attribute__((section("NP_CAPTURE_LLI"), aligned(32)));
#endif
#if (XE_NP_APP_EXPERIMENT_GPIO_FULLPORT_QUAD_STRESS_ENABLE != 0U)
/* GPIOE and GPIOD each receive the same four timer phases.  Keep their
 * descriptor objects in SRAM1 while all eight live payload lanes stay in
 * SRAM3, so a descriptor fetch can never target the capture destination. */
static XE_NP_AppQuadDmaLane xe_np_fullport_quad_dma_gpioe_high_lanes[
    XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT];
static XE_NP_AppQuadDmaLane xe_np_fullport_quad_dma_gpiod_high_lanes[
    XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT];
static volatile uint32_t xe_np_fullport_quad_dma_gpioe_high_head_snapshot[
    XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT];
static volatile uint32_t xe_np_fullport_quad_dma_gpiod_high_head_snapshot[
    XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT];
#endif
static uint8_t xe_np_quad_dma_active;
#if (XE_NP_APP_EXPERIMENT_GPIO_FULLPORT_QUAD_STRESS_ENABLE != 0U)
static TIM_HandleTypeDef xe_np_quad_dma_probe2_timer = {.Instance = TIM3};
static TIM_HandleTypeDef xe_np_quad_dma_probe3_timer = {.Instance = TIM4};
static uint32_t xe_np_quad_dma_aux_mask;
#endif
static uint8_t xe_np_quad_dma_error_seen;
/* SWD-only bring-up marker for the four-lane prototype.  Kept outside the
 * request-paced path; it records the first setup stage that rejects. */
static volatile uint32_t xe_np_quad_dma_start_stage;
/* Captured after TIM8 request gating during a quiesced frozen snapshot. */
static volatile uint32_t xe_np_quad_dma_head_snapshot[
    XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT];
/* Q1F3 SWD ABI: magic, state (0=not frozen, 1=valid, 2=timeout, 3=DMA error,
 * 4=quiet preconditions rejected, 5=CPU fault), TIM8 phase,
 * four {CDAR,CBR1,CSR,CLLR}, four initial offsets, four CTR1 values.
 * CSR.FIFOL distinguishes read samples from committed RAM bytes. */
static volatile uint32_t xe_np_quad_dma_freeze_diag[
    3U + 6U * XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT];
static volatile uint32_t xe_np_quad_dma_initial_offsets[12];
#if (XE_NP_APP_QUAD_DMA_QUIET_ENABLE != 0U)
static volatile uint32_t xe_np_quad_dma_quiet_diag[4];
#endif
#if (XE_NP_APP_QUAD_DMA_NO_PROBE_DRYRUN != 0U)
/* QDR1: magic,state(1=init,2=armed,3=frozen,4=start failed),HAL status,
 * HCLK Hz,start tick,initial GPIO byte. This is never NP recording data. */
static volatile uint32_t xe_np_quad_dryrun_diag[6];
#endif
#if (XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_INTERLEAVE_USB_ENABLE != 0U)
static uint32_t xe_np_quad_dma_usb_cursor[
    XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT];
static uint8_t xe_np_quad_dma_usb_cursor_valid;
static uint8_t xe_np_quad_dma_usb_next_lane;
static volatile uint32_t xe_np_quad_dma_usb_export_debug;
static uint8_t xe_np_quad_dma_usb_batch_claimed;
#if (XE_NP_APP_QUAD_USB_WORD_EXPORT != 0U)
static uint32_t xe_np_quad_usb_start_ms, xe_np_quad_usb_last_poll_ms;
static uint32_t xe_np_quad_usb_previous_head[4], xe_np_quad_usb_available[4];
/* QUX1: magic, fault, batches, last availability by lane, maximum poll gap. */
static volatile uint32_t xe_np_quad_usb_word_diag[8];
#if NP_SD_APP
#include "np_sd_ring_observer.h"
static NpSdRingClock xe_np_sd_ring_clock[4];
volatile uint32_t xe_np_sd_ring_clock_diag[8]; /* fault,lane,cycles,advance,expected,head,previous,max_cycles */
#endif
static uint8_t xe_np_quad_usb_history_valid;
#endif
#endif
#endif
static XE_NP_AppProbeState xe_np_probe_state;
static uint8_t xe_np_probe_last_status_retained[XE_NP_NEUROPIXELS_MAX_PROBES];
static uint8_t xe_np_probe_last_verify_flags_retained[XE_NP_NEUROPIXELS_MAX_PROBES];
static uint8_t xe_np_probe_last_cal_mod_retained[XE_NP_NEUROPIXELS_MAX_PROBES];
static uint8_t xe_np_probe_last_sync_retained[XE_NP_NEUROPIXELS_MAX_PROBES];
static uint8_t xe_np_probe_last_rec_mod_retained[XE_NP_NEUROPIXELS_MAX_PROBES];
static uint8_t xe_np_probe_last_op_mode_retained[XE_NP_NEUROPIXELS_MAX_PROBES];
static uint8_t xe_np_probe_last_verify_valid_mask;
volatile XE_NP_AppProbeDiag xe_np_probe_diag;
volatile XE_NP_AppI2cScanDiag xe_np_i2c_scan_diag[XE_NP_NEUROPIXELS_MAX_PROBES];
volatile XE_NP_AppCaptureBenchDiag xe_np_capture_bench_diag;
volatile XE_NP_AppStreamDiag xe_np_stream_diag;
static uint8_t xe_np_dma_start_error_seen;
static uint8_t xe_np_capture_validation_enabled;
static uint8_t xe_np_capture_selftest_probe_mask;
static uint8_t xe_np_capture_live_decode_probe_mask;
static volatile uint32_t xe_np_pending_error_code;
#if (XE_NP_APP_EXPERIMENT_FREEZE_CAPTURE_AFTER_MS != 0U)
static uint32_t xe_np_capture_freeze_deadline_tick_ms;
static uint8_t xe_np_capture_freeze_done;
#endif

static void XE_NP_AppDma_HalfTransferCallback(DMA_HandleTypeDef *hdma);
static void XE_NP_AppDma_TransferCompleteCallback(DMA_HandleTypeDef *hdma);
static void XE_NP_AppDma_ErrorCallback(DMA_HandleTypeDef *hdma);
static void XE_NP_AppDma_HandleHalfTransferFromIsr(XE_NP_AppDmaPort *port);
static void XE_NP_AppDma_HandleTransferCompleteFromIsr(XE_NP_AppDmaPort *port);
static void XE_NP_AppDma_RecordIrqJitterFromIsr(
    XE_NP_AppDmaPort *port,
    uint8_t irq_event_type);
static uint8_t xe_np_mclk_error_reported;
static uint8_t xe_np_sd_error_reported;
static uint8_t xe_np_recorder_rate_bad_samples;
static uint8_t xe_np_recorder_rate_fault_seen;
static uint32_t xe_np_sd_capacity_cached;
static uint32_t xe_np_sd_next_refresh_tick_ms;
static uint32_t xe_np_mclk_next_refresh_tick_ms;
static uint32_t xe_np_probe_next_verify_tick_ms;
static uint32_t xe_np_recorder_rate_sample_tick_ms;
static uint32_t xe_np_recorder_rate_sample_blocks;
static uint32_t xe_np_psb_clock_probe_count;
static uint32_t xe_np_psb_clock_probe_flags;
static uint32_t xe_np_psb_clock_probe_gpioa_start;
static uint32_t xe_np_psb_clock_probe_gpioa_end;
static uint32_t xe_np_psb_clock_probe_tim2_sr;
#if (XE_NP_APP_BENCH_CAPTURE_WITHOUT_STORAGE != 0U) && \
    (XE_NP_APP_BENCH_CAPTURE_TIMING_SWEEP_ENABLE != 0U) && \
    (XE_NP_APP_GPIOE_CAPTURE_USE_PA0_TIM2_CH1 != 0U)
typedef struct
{
    uint8_t rising_edge;
    uint8_t filter;
} XE_NP_AppCaptureTimingCandidate;

static const XE_NP_AppCaptureTimingCandidate xe_np_gpioe_capture_timing_candidates[] = {
    {0U, 0U},
    {1U, 0U},
    {0U, 1U},
    {1U, 1U},
    {0U, 2U},
    {1U, 2U},
    {0U, 4U},
    {1U, 4U},
};
static uint8_t xe_np_gpioe_capture_timing_candidate_index;
#endif
static uint8_t xe_np_gpioe_capture_timing_rising_edge = XE_NP_APP_GPIOE_CAPTURE_TIM2_RISING_EDGE;
static uint8_t xe_np_gpioe_capture_timing_filter = XE_NP_APP_GPIOE_CAPTURE_TIM2_FILTER;
static uint8_t xe_np_capture_start_phase_delay_armed;
static uint8_t xe_np_capture_start_phase_aligned;
static uint8_t xe_np_dma_service_next_port;
static XE_NP_AppRawSdSink xe_np_raw_sd_sink;
volatile XE_NP_AppRawSdDiag xe_np_raw_sd_diag;
/* Last path which intentionally returned the capture hardware to safe state.
 * Kept separate from transient diagnostics, which are cleared by shutdown. */
static volatile uint32_t xe_np_capture_shutdown_reason;
static XE_NP_AppProbeConfigStore xe_np_probe_config_store;
static uint16_t xe_np_probe_cal_dac_code;
static uint8_t xe_np_imu_fail_count;
static uint8_t xe_np_probe_calibration_warning_reported;
static volatile uint8_t xe_np_usb_capture_requested;
static volatile uint8_t xe_np_usb_window_export_enabled;
static volatile uint8_t xe_np_usb_window_export_port_index;

static void XE_NP_App_ApplySafeNpStartup(XE_NP_Runtime *runtime);
static void XE_NP_App_RefreshProbeStatus(XE_NP_Runtime *runtime);
static void XE_NP_App_ResetFirstBacklogResyncSnapshot(XE_NP_AppDmaPort *port);

static uint8_t XE_NP_App_GetGpioeCaptureTimingFlags(void)
{
    return (uint8_t)((xe_np_gpioe_capture_timing_rising_edge & 0x01U) |
                     ((xe_np_gpioe_capture_timing_filter & 0x0FU) << 1U)
#if (XE_NP_APP_BENCH_CAPTURE_WITHOUT_STORAGE != 0U) && \
    (XE_NP_APP_BENCH_CAPTURE_TIMING_SWEEP_ENABLE != 0U) && \
    (XE_NP_APP_GPIOE_CAPTURE_USE_PA0_TIM2_CH1 != 0U)
                     | ((xe_np_gpioe_capture_timing_candidate_index & 0x07U) << 5U)
#endif
    );
}

#if (XE_NP_APP_BENCH_CAPTURE_WITHOUT_STORAGE != 0U) && \
    (XE_NP_APP_BENCH_CAPTURE_TIMING_SWEEP_ENABLE != 0U) && \
    (XE_NP_APP_GPIOE_CAPTURE_USE_PA0_TIM2_CH1 != 0U)
static void XE_NP_App_ApplyGpioeCaptureTimingCandidate(uint8_t candidate_index)
{
    if(candidate_index >=
       (sizeof(xe_np_gpioe_capture_timing_candidates) / sizeof(xe_np_gpioe_capture_timing_candidates[0])))
    {
        candidate_index = 0U;
    }

    xe_np_gpioe_capture_timing_candidate_index = candidate_index;
    xe_np_gpioe_capture_timing_rising_edge =
        xe_np_gpioe_capture_timing_candidates[candidate_index].rising_edge;
    xe_np_gpioe_capture_timing_filter =
        xe_np_gpioe_capture_timing_candidates[candidate_index].filter;
}

static int XE_NP_App_ShouldSweepGpioeCaptureTiming(const XE_NP_Runtime *runtime)
{
    if(runtime == NULL)
    {
        return 0;
    }

    return (((runtime->capture.flags & XE_NP_CAPTURE_FLAG_GPIOE_ACTIVE) != 0U) &&
            ((runtime->capture.flags & XE_NP_CAPTURE_FLAG_GPIOD_ACTIVE) == 0U) &&
            ((runtime->active_np_probe_mask & XE_NP_CAPTURE_PROBE2_MASK) != 0U)) ? 1 : 0;
}

static int XE_NP_App_AdvanceGpioeCaptureTimingCandidate(XE_NP_Runtime *runtime, uint32_t now_ms)
{
    uint8_t next_index;

    if(XE_NP_App_ShouldSweepGpioeCaptureTiming(runtime) == 0)
    {
        return 0;
    }

    next_index = (uint8_t)(xe_np_gpioe_capture_timing_candidate_index + 1U);
    if(next_index >=
       (sizeof(xe_np_gpioe_capture_timing_candidates) / sizeof(xe_np_gpioe_capture_timing_candidates[0])))
    {
        return 0;
    }

    XE_NP_App_ApplyGpioeCaptureTimingCandidate(next_index);
    XE_NP_App_ApplySafeNpStartup(runtime);
    xe_np_probe_state.init_failed = 1U;
    xe_np_probe_state.next_retry_tick_ms = now_ms + XE_NP_APP_PROBE_RETRY_DELAY_MS;
    XE_NP_App_RefreshProbeStatus(runtime);
    return 1;
}
#else
static void XE_NP_App_ApplyGpioeCaptureTimingCandidate(uint8_t candidate_index)
{
    xe_np_gpioe_capture_timing_rising_edge = candidate_index & 1U;
    xe_np_gpioe_capture_timing_filter = XE_NP_APP_GPIOE_CAPTURE_TIM2_FILTER;
}

static int XE_NP_App_AdvanceGpioeCaptureTimingCandidate(XE_NP_Runtime *runtime, uint32_t now_ms)
{
    (void)runtime;
    (void)now_ms;
    return 0;
}
#endif
static volatile uint8_t xe_np_usb_capture_port_index;

static int XE_NP_App_RefreshNpMclkStatus(XE_NP_Runtime *runtime);
static int XE_NP_App_UsbCaptureStorageBypassActive(const XE_NP_Runtime *runtime);
static void XE_NP_App_ServiceProbeBringUp(XE_NP_Runtime *runtime);
static void XE_NP_App_BeginProbeBringUpFromIdle(XE_NP_Runtime *runtime, uint32_t now_ms);
static void XE_NP_App_SetProbePowerRails(XE_NP_Runtime *runtime, uint8_t enabled);

static XE_NP_AppDmaPort *XE_NP_App_GetDmaPortForUsb(uint8_t port_index)
{
    if(port_index == 0U)
    {
        return &xe_np_dma_gpioe;
    }
    if(port_index == 1U)
    {
        return &xe_np_dma_gpiod;
    }

    return NULL;
}

static uint32_t XE_NP_App_GetCaptureStoragePhysicalBytes(const XE_NP_AppDmaPort *port)
{
    if((port == NULL) || (port->capture_port == NULL))
    {
        return 0U;
    }

    return (port->capture_port->storage_physical_bytes != 0U) ?
               port->capture_port->storage_physical_bytes :
               port->capture_port->storage_bytes;
}

static int XE_NP_App_CanUseUsbRingPacketHeadroom(const XE_NP_AppDmaPort *port)
{
#if ((XE_NP_APP_CAPTURE_DMA_DAR_ONLY_CIRCULAR_ENABLE != 0U) && \
     (XE_NP_APP_DUAL_DMA_DAR_ONLY_CIRCULAR_ENABLE == 0U))
    (void)port;
    return 0;
#else
#if (XE_NP_APP_USB_RING_PACKET_HEADROOM_ENABLE != 0U)
    return ((port != NULL) &&
            (port->capture_port != NULL) &&
            (port->port_index == 0U) &&
            ((port->capture_port->active_probe_mask & XE_NP_CAPTURE_ALL_PROBES_MASK) ==
             XE_NP_CAPTURE_PROBE2_MASK) &&
            (port->capture_port->bytes_per_tick == sizeof(uint8_t))) ? 1 : 0;
#else
    (void)port;
    return 0;
#endif
#endif
}

static uint32_t XE_NP_App_MapRawOffsetToPhysicalOffset(
    const XE_NP_AppDmaPort *port,
    uint32_t raw_offset)
{
    uint32_t payload_window_bytes;
    uint32_t window_index;
    uint32_t window_offset;

    if((port == NULL) || (port->usb_packet_headroom_enabled == 0U))
    {
        return raw_offset;
    }

    payload_window_bytes = port->usb_packet_window_payload_bytes;
    if((payload_window_bytes == 0U) || (port->usb_packet_window_count == 0U))
    {
        return raw_offset;
    }

    window_index = raw_offset / payload_window_bytes;
    window_offset = raw_offset % payload_window_bytes;
    if(window_index >= port->usb_packet_window_count)
    {
        window_index = port->usb_packet_window_count - 1U;
        window_offset = payload_window_bytes - 1U;
    }

    return ((window_index + 1U) * (uint32_t)port->usb_packet_header_bytes) +
           (window_index * payload_window_bytes) + window_offset;
}

static int XE_NP_App_UsbWindowExportArmedForPort(const XE_NP_AppDmaPort *port)
{
    return ((port != NULL) &&
            (xe_np_usb_window_export_enabled != 0U) &&
            (xe_np_usb_window_export_port_index == port->port_index)) ? 1 : 0;
}

static uint32_t XE_NP_App_GetUsbExportChunkBytes(const XE_NP_AppDmaPort *port)
{
    if((port == NULL) || (port->capture_port == NULL))
    {
        return 0U;
    }

    /* USB512 framing copies each payload into a private 512-byte endpoint
     * buffer.  Keep producer windows small enough that this copy never holds
     * a live GPIO-DMA ring region while USB DMA is in progress. */
    return XE_NP_USB_STREAM_PAYLOAD_BYTES;
}

static uint32_t XE_NP_App_GetLogicalDmaNodeSpanBytes(const XE_NP_AppDmaPort *port)
{
    uint32_t logical_node_bytes;

    if((port == NULL) || (port->capture_port == NULL))
    {
        return 0U;
    }

    logical_node_bytes = port->node_bytes;
    if((port->dma_queue_node_count != 0U) &&
       (port->capture_port->storage_bytes >= port->dma_queue_node_count) &&
       ((port->capture_port->storage_bytes % port->dma_queue_node_count) == 0U))
    {
        logical_node_bytes = port->capture_port->storage_bytes / port->dma_queue_node_count;
    }

    return logical_node_bytes;
}

static int XE_NP_App_UsesFullNodeWindowPublication(const XE_NP_AppDmaPort *port)
{
    return ((port != NULL) &&
            ((port->usb_packet_headroom_enabled != 0U) ||
             (XE_NP_APP_EXPERIMENT_FULL_NODE_WINDOW_PUBLICATION_ENABLE != 0U))) ? 1 : 0;
}

static uint8_t *XE_NP_App_SelectCaptureStorage(uint32_t *storage_bytes)
{
#if (XE_NP_APP_CAPTURE_PSRAM_ENABLE != 0U)
    const volatile XE_NP_PsramDiag *psram_diag = XE_NP_Psram_GetDiag();

    if((psram_diag != NULL) &&
       ((psram_diag->flags & (XE_NP_PSRAM_FLAG_MEMMAP_OK | XE_NP_PSRAM_FLAG_BANDWIDTH_OK)) ==
        (XE_NP_PSRAM_FLAG_MEMMAP_OK | XE_NP_PSRAM_FLAG_BANDWIDTH_OK)) &&
       (psram_diag->result == 0U))
    {
        if(storage_bytes != NULL)
        {
#if (XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_INTERLEAVE_ENABLE != 0U)
            /* Four-lane 30 kS/s uses one proven 46,080-byte circular span per
             * phase.  Keep that strict geometry while using PSRAM only as the
             * physical target; the rest of the 8 MiB remains transport slack. */
            *storage_bytes = XE_NP_APP_INTERNAL_CAPTURE_STORAGE_BYTES;
#else
            *storage_bytes = XE_NP_APP_CAPTURE_PSRAM_BYTES;
#endif
        }
        return (uint8_t *)XE_NP_PSRAM_BASE_ADDR;
    }
#endif

#if (XE_NP_APP_CAPTURE_STORAGE_OFFSET_BYTES > XE_NP_APP_CAPTURE_STORAGE_OFFSET_MAX_BYTES)
#error "XE_NP_APP_CAPTURE_STORAGE_OFFSET_BYTES exceeds reserved slack"
#endif

    if(storage_bytes != NULL)
    {
        *storage_bytes = XE_NP_APP_INTERNAL_CAPTURE_STORAGE_BYTES;
    }
    return ((uint8_t *)xe_np_raw_capture_storage) + XE_NP_APP_CAPTURE_STORAGE_OFFSET_BYTES;
}

static int XE_NP_App_UsesGroupedRepeatCapture(const XE_NP_AppDmaPort *port)
{
    return ((port != NULL) &&
            (port->capture_bytes_per_tick == sizeof(uint8_t)) &&
            (port->dma_repeat_block_count > 1U)) ? 1 : 0;
}

static uint32_t XE_NP_App_CriticalSection_Enter(void)
{
    uint32_t primask = __get_PRIMASK();
    __disable_irq();
    return primask;
}

static void XE_NP_App_CriticalSection_Exit(uint32_t primask)
{
    if((primask & 0x1U) == 0U)
    {
        __enable_irq();
    }
}

static void XE_NP_App_ResetPsbClockProbeDiag(void)
{
    xe_np_psb_clock_probe_count = 0U;
    xe_np_psb_clock_probe_flags = 0U;
    xe_np_psb_clock_probe_gpioa_start = 0U;
    xe_np_psb_clock_probe_gpioa_end = 0U;
    xe_np_psb_clock_probe_tim2_sr = 0U;
}

static void XE_NP_App_ConfigurePsbPa0Af(uint32_t alternate)
{
    GPIO_InitTypeDef gpio_init;

    memset(&gpio_init, 0, sizeof(gpio_init));
    __HAL_RCC_GPIOA_CLK_ENABLE();
    gpio_init.Pin = GPIO_PIN_0;
    gpio_init.Mode = GPIO_MODE_AF_PP;
    gpio_init.Pull =
#if (XE_NP_APP_EXPERIMENT_PSB_CLK_PULLUP_ENABLE != 0U)
        GPIO_PULLUP;
#elif (XE_NP_APP_EXPERIMENT_PSB_CLK_PULLDOWN_ENABLE != 0U)
        GPIO_PULLDOWN;
#else
        GPIO_NOPULL;
#endif
    gpio_init.Speed = GPIO_SPEED_FREQ_VERY_HIGH;
    gpio_init.Alternate = (uint8_t)alternate;
    HAL_GPIO_Init(GPIOA, &gpio_init);
}

static void XE_NP_App_ConfigureNp2PsbBusPull(void)
{
    GPIO_InitTypeDef gpio_init;

    memset(&gpio_init, 0, sizeof(gpio_init));
    __HAL_RCC_GPIOE_CLK_ENABLE();
    gpio_init.Pin = NP2_SYNC_Pin | NP2_D0_Pin | NP2_D1_Pin | NP2_D2_Pin |
                    NP2_D3_Pin | NP2_D4_Pin | NP2_D5_Pin | NP2_D6_Pin;
    gpio_init.Mode = GPIO_MODE_INPUT;
    gpio_init.Pull =
#if (XE_NP_APP_EXPERIMENT_NP2_PSB_PULLUP_ENABLE != 0U)
        GPIO_PULLUP;
#elif (XE_NP_APP_EXPERIMENT_NP2_PSB_PULLDOWN_ENABLE != 0U)
        GPIO_PULLDOWN;
#else
        GPIO_NOPULL;
#endif
    HAL_GPIO_Init(GPIOE, &gpio_init);
}

static void XE_NP_App_ConfigurePsbPa0ForTim8Etr(void)
{
    XE_NP_App_ConfigurePsbPa0Af(GPIO_AF3_TIM8);
}

static void XE_NP_App_ConfigurePsbPa0ForTim2Ch1(void)
{
    XE_NP_App_ConfigurePsbPa0Af(GPIO_AF1_TIM2);
}

static void XE_NP_App_ConfigurePsbPa5ForTim2Etr(void)
{
    GPIO_InitTypeDef gpio_init;

    memset(&gpio_init, 0, sizeof(gpio_init));
    __HAL_RCC_GPIOA_CLK_ENABLE();
    gpio_init.Pin = GPIO_PIN_5;
    gpio_init.Mode = GPIO_MODE_AF_PP;
    gpio_init.Pull =
#if (XE_NP_APP_EXPERIMENT_PSB_CLK_PULLUP_ENABLE != 0U)
        GPIO_PULLUP;
#elif (XE_NP_APP_EXPERIMENT_PSB_CLK_PULLDOWN_ENABLE != 0U)
        GPIO_PULLDOWN;
#else
        GPIO_NOPULL;
#endif
    gpio_init.Speed = GPIO_SPEED_FREQ_VERY_HIGH;
    gpio_init.Alternate = GPIO_AF2_TIM2;
    HAL_GPIO_Init(GPIOA, &gpio_init);
}

static HAL_StatusTypeDef XE_NP_App_ConfigureTimerEtrMode2Falling(TIM_HandleTypeDef *htim)
{
    TIM_ClockConfigTypeDef clock_config;

    if((htim == NULL) || (htim->Instance == NULL))
    {
        return HAL_ERROR;
    }

    memset(&clock_config, 0, sizeof(clock_config));
    clock_config.ClockSource = TIM_CLOCKSOURCE_ETRMODE2;
    clock_config.ClockPolarity = TIM_CLOCKPOLARITY_INVERTED;
    clock_config.ClockPrescaler = TIM_CLOCKPRESCALER_DIV1;
    clock_config.ClockFilter = 0U;
    return HAL_TIM_ConfigClockSource(htim, &clock_config);
}

static uint32_t XE_NP_App_GetTimerKernelHz(const TIM_HandleTypeDef *htim)
{
    RCC_ClkInitTypeDef clk_init;
    uint32_t flash_latency;
    uint32_t pclk_hz;
    uint32_t apb_divider;

    if((htim == NULL) || (htim->Instance == NULL))
    {
        return 0U;
    }

    HAL_RCC_GetClockConfig(&clk_init, &flash_latency);
    (void)flash_latency;

    if(htim->Instance == TIM2)
    {
        pclk_hz = HAL_RCC_GetPCLK1Freq();
        apb_divider = clk_init.APB1CLKDivider;
    }
    else if(htim->Instance == TIM8)
    {
        pclk_hz = HAL_RCC_GetPCLK2Freq();
        apb_divider = clk_init.APB2CLKDivider;
    }
    else
    {
        return 0U;
    }

    return (apb_divider == RCC_HCLK_DIV1) ? pclk_hz : (pclk_hz * 2U);
}

static HAL_StatusTypeDef XE_NP_App_PrepareTimerForInternalUpdateCapture(
    TIM_HandleTypeDef *htim,
    uint32_t sample_hz)
{
    TIM_ClockConfigTypeDef clock_config;
    uint32_t timer_hz;
    uint32_t divisor;
    uint32_t max_arr;

    if((htim == NULL) || (htim->Instance == NULL) || (sample_hz == 0U))
    {
        return HAL_ERROR;
    }

    timer_hz = XE_NP_App_GetTimerKernelHz(htim);
    if(timer_hz == 0U)
    {
        return HAL_ERROR;
    }

    divisor = (timer_hz + (sample_hz / 2U)) / sample_hz;
    if(divisor == 0U)
    {
        divisor = 1U;
    }
    max_arr = (htim->Instance == TIM2) ? 0xFFFFFFFFUL : 0xFFFFUL;
    if((max_arr != 0xFFFFFFFFUL) && (divisor > (max_arr + 1UL)))
    {
        divisor = max_arr + 1UL;
    }

    __HAL_TIM_DISABLE_DMA(htim, TIM_DMA_UPDATE | TIM_DMA_CC1);
    (void)HAL_TIM_Base_Stop(htim);
    __HAL_TIM_DISABLE(htim);

    memset(&clock_config, 0, sizeof(clock_config));
    clock_config.ClockSource = TIM_CLOCKSOURCE_INTERNAL;
    if(HAL_TIM_ConfigClockSource(htim, &clock_config) != HAL_OK)
    {
        return HAL_ERROR;
    }

    htim->Instance->PSC = 0U;
    htim->Instance->ARR = divisor - 1U;
    htim->Instance->CNT = 0U;
    SET_BIT(htim->Instance->CR1, TIM_CR1_URS);
    htim->Instance->EGR = TIM_EGR_UG;
    __HAL_TIM_CLEAR_FLAG(htim, TIM_FLAG_UPDATE | TIM_FLAG_CC1);
    return HAL_OK;
}

static void XE_NP_App_RunPsbClockProbe(const XE_NP_Runtime *runtime)
{
    TIM_HandleTypeDef *probe_htim;
    TIM_TypeDef *tim;
    uint32_t flags = XE_NP_APP_PSB_CLOCK_PROBE_FLAG_RAN;

    XE_NP_App_ResetPsbClockProbeDiag();

#if (XE_NP_APP_BENCH_PSB_CLOCK_PROBE_ENABLE == 0U)
    (void)runtime;
    return;
#else
    if((runtime == NULL) ||
       ((runtime->capture.flags & XE_NP_CAPTURE_FLAG_GPIOE_ACTIVE) == 0U))
    {
        xe_np_psb_clock_probe_flags =
            flags | XE_NP_APP_PSB_CLOCK_PROBE_FLAG_NO_GPIOE_PROBE;
        return;
    }

#if (XE_NP_APP_GPIOE_CAPTURE_USE_PA0_TIM2_CH1 != 0U)
#if (XE_NP_APP_EXPERIMENT_GPIOE_CAPTURE_TIM2_PSB_ETR_ENABLE != 0U)
    flags |= XE_NP_APP_PSB_CLOCK_PROBE_FLAG_GPIOE_TIM2_ETR;
    XE_NP_App_ConfigurePsbPa5ForTim2Etr();
    probe_htim = &htim2;
#else
    flags |= XE_NP_APP_PSB_CLOCK_PROBE_FLAG_GPIOE_PA0_TIM2_CH1;
    XE_NP_App_ConfigurePsbPa0ForTim8Etr();
    probe_htim = &htim8;
#endif
#else
    probe_htim = runtime->htim_np_capture_gpioe;
#endif

    if((probe_htim == NULL) || (probe_htim->Instance == NULL))
    {
        xe_np_psb_clock_probe_flags =
            flags | XE_NP_APP_PSB_CLOCK_PROBE_FLAG_TIMER_MISSING;
        return;
    }

    tim = probe_htim->Instance;
    xe_np_psb_clock_probe_gpioa_start = GPIOA->IDR;

    __HAL_TIM_DISABLE_DMA(probe_htim, TIM_DMA_UPDATE | TIM_DMA_CC1);
    (void)HAL_TIM_Base_Stop(probe_htim);
    __HAL_TIM_DISABLE(probe_htim);
    if(XE_NP_App_ConfigureTimerEtrMode2Falling(probe_htim) != HAL_OK)
    {
        xe_np_psb_clock_probe_flags =
            flags | XE_NP_APP_PSB_CLOCK_PROBE_FLAG_TIMER_CONFIG_FAIL;
        return;
    }
    __HAL_TIM_SET_AUTORELOAD(probe_htim, 0xFFFFFFFFUL);
    __HAL_TIM_SET_COUNTER(probe_htim, 0U);
    __HAL_TIM_CLEAR_FLAG(probe_htim, TIM_FLAG_UPDATE | TIM_FLAG_CC1);

    HAL_GPIO_WritePin(NP_REC_NRST_GPIO_Port, NP_REC_NRST_Pin, GPIO_PIN_SET);
    __HAL_TIM_ENABLE(probe_htim);
    np_psb_cycle_diag[0] = DWT->CYCCNT;
    HAL_Delay(XE_NP_APP_PSB_CLOCK_PROBE_MS);
    np_psb_cycle_diag[1] = DWT->CYCCNT - np_psb_cycle_diag[0];
    xe_np_psb_clock_probe_count = tim->CNT;
    np_psb_cycle_diag[2] = xe_np_psb_clock_probe_count;
    np_psb_cycle_diag[3] = SystemCoreClock;
    xe_np_psb_clock_probe_tim2_sr = tim->SR;
    xe_np_psb_clock_probe_gpioa_end = GPIOA->IDR;
    __HAL_TIM_DISABLE(probe_htim);
    HAL_GPIO_WritePin(NP_REC_NRST_GPIO_Port, NP_REC_NRST_Pin, GPIO_PIN_RESET);

    if(xe_np_psb_clock_probe_count != 0U)
    {
        flags |= XE_NP_APP_PSB_CLOCK_PROBE_FLAG_NONZERO;
    }
    xe_np_psb_clock_probe_flags = flags;
#endif
}

static void XE_NP_App_DCachePrepare(void *addr, uint32_t size_bytes)
{
#if NP_SD_APP && defined(STM32U595xx)
    if(np_sd_internal_range((uintptr_t)addr,size_bytes)){__DMB();return;}
#endif

#if (XE_NP_CAPTURE_SRAM3_MPU_NONCACHEABLE != 0U)
    if(XE_NP_Capture_Sram3RangeContained(addr, size_bytes) != 0U)
    {
        return;
    }
#endif
#if (XE_NP_PSRAM_MPU_NONCACHEABLE_EXPERIMENT != 0U)
    if(XE_NP_Psram_RangeContained(addr, size_bytes) != 0U)
    {
        return;
    }
#endif
#if defined(DCACHE1) && defined(HAL_DCACHE_MODULE_ENABLED)
    uintptr_t start_addr;
    uintptr_t end_addr;

    if((addr == NULL) || (size_bytes == 0U))
    {
        return;
    }

    start_addr = ((uintptr_t)addr) & ~((uintptr_t)XE_NP_CAPTURE_DCACHE_LINE_BYTES - 1U);
    end_addr = (((uintptr_t)addr) + (uintptr_t)size_bytes +
                (XE_NP_CAPTURE_DCACHE_LINE_BYTES - 1U)) &
               ~((uintptr_t)XE_NP_CAPTURE_DCACHE_LINE_BYTES - 1U);
    (void)HAL_DCACHE_CleanInvalidByAddr(
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
    end_addr = (((uintptr_t)addr) + (uintptr_t)size_bytes +
                (XE_NP_CAPTURE_DCACHE_LINE_BYTES - 1U)) &
               ~((uintptr_t)XE_NP_CAPTURE_DCACHE_LINE_BYTES - 1U);
    SCB_CleanInvalidateDCache_by_Addr((void *)start_addr, (int32_t)(end_addr - start_addr));
#else
    (void)addr;
    (void)size_bytes;
#endif
}

static void XE_NP_App_DCacheClean(const void *addr, uint32_t size_bytes)
{
#if NP_SD_APP && defined(STM32U595xx)
    if(np_sd_internal_range((uintptr_t)addr,size_bytes)){__DMB();return;}
#endif

#if (XE_NP_CAPTURE_SRAM3_MPU_NONCACHEABLE != 0U)
    if(XE_NP_Capture_Sram3RangeContained(addr, size_bytes) != 0U)
    {
        return;
    }
#endif
#if defined(DCACHE1) && defined(HAL_DCACHE_MODULE_ENABLED)
    uintptr_t start_addr;
    uintptr_t end_addr;

    if((addr == NULL) || (size_bytes == 0U))
    {
        return;
    }

    start_addr = ((uintptr_t)addr) & ~((uintptr_t)XE_NP_CAPTURE_DCACHE_LINE_BYTES - 1U);
    end_addr = (((uintptr_t)addr) + (uintptr_t)size_bytes +
                (XE_NP_CAPTURE_DCACHE_LINE_BYTES - 1U)) &
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
    end_addr = (((uintptr_t)addr) + (uintptr_t)size_bytes +
                (XE_NP_CAPTURE_DCACHE_LINE_BYTES - 1U)) &
               ~((uintptr_t)XE_NP_CAPTURE_DCACHE_LINE_BYTES - 1U);
    SCB_CleanDCache_by_Addr((void *)start_addr, (int32_t)(end_addr - start_addr));
#else
    (void)addr;
    (void)size_bytes;
#endif
}

static void XE_NP_App_DmaDescriptorDCacheClean(XE_NP_AppDmaPort *port)
{
    if(port == NULL)
    {
        return;
    }

    /*
     * STM32U5 GPDMA linked-list execution fetches node descriptors from SRAM.
     * The HAL updates those descriptors in normal cacheable memory but does
     * not clean D-cache for them before Start_IT(). Clean the queue metadata
     * and the node array once the list is fully built so the DMA engine sees
     * the same descriptor image that the M33 just configured.
     */
    XE_NP_App_DCacheClean(&port->queue, (uint32_t)sizeof(port->queue));
    XE_NP_App_DCacheClean(port->nodes, (uint32_t)sizeof(port->nodes));
}

static int XE_NP_App_TimeReached(uint32_t now_ms, uint32_t deadline_ms)
{
    return ((int32_t)(now_ms - deadline_ms) >= 0) ? 1 : 0;
}

static int XE_NP_App_ServiceNpMclkStatus(XE_NP_Runtime *runtime)
{
    uint32_t now_ms;

    if(runtime == NULL)
    {
        return -1;
    }

    now_ms = HAL_GetTick();
    if((xe_np_mclk_next_refresh_tick_ms != 0U) &&
       (XE_NP_App_TimeReached(now_ms, xe_np_mclk_next_refresh_tick_ms) == 0))
    {
        return (XE_NP_Runtime_HasValidNpMclk(runtime) != 0) ? 0 : -1;
    }

    xe_np_mclk_next_refresh_tick_ms = now_ms + XE_NP_APP_MCLK_REFRESH_INTERVAL_MS;
    return XE_NP_App_RefreshNpMclkStatus(runtime);
}

static void XE_NP_App_ApplySensitivePinSafety(void)
{
    GPIO_InitTypeDef GPIO_InitStruct = {0};

    /*
     * PA8 is the intended Neuropixels MCLK source. Re-apply the MCO pin
     * configuration after Cube init so later manual checks can trust both
     * the alternate-function route and the output drive strength.
     */
    __HAL_RCC_GPIOA_CLK_ENABLE();
    GPIO_InitStruct.Pin = GPIO_PIN_8;
    GPIO_InitStruct.Mode = GPIO_MODE_AF_PP;
    GPIO_InitStruct.Pull = GPIO_NOPULL;
    GPIO_InitStruct.Speed = XE_NP_BOARD_EXPERIMENT_MCO_GPIO_SPEED;
    GPIO_InitStruct.Alternate = GPIO_AF0_MCO;
    HAL_GPIO_Init(GPIOA, &GPIO_InitStruct);
    /* PRIVATE V1 test. The board must already be provisioned for low VDD;
     * firmware never changes this nonvolatile supply-voltage declaration. */
    if((FLASH->OPTR & FLASH_OPTR_IO_VDD_HSLV) != 0U)
        HAL_GPIO_EnableHighSPeedLowVoltage(GPIOA, GPIO_PIN_8);
    else
        HAL_GPIO_DisableHighSPeedLowVoltage(GPIOA, GPIO_PIN_8);


    /*
     * PF4 is tied onto the Neuropixels MCLK net in the board schematic, but
     * XE_NP intentionally sources MCLK from PA8/MCO. Force PF4 into analog
     * mode so the MCU never loads or accidentally drives that net.
     */
    __HAL_RCC_GPIOF_CLK_ENABLE();
    GPIO_InitStruct.Pin = GPIO_PIN_4;
    GPIO_InitStruct.Mode = GPIO_MODE_ANALOG;
    GPIO_InitStruct.Pull = GPIO_NOPULL;
    HAL_GPIO_Init(GPIOF, &GPIO_InitStruct);
}

static int XE_NP_App_RefreshNpMclkStatus(XE_NP_Runtime *runtime)
{
    uint8_t mclk_ok = 1U;
    uint32_t expected_mclk_hz;
    uint32_t expected_mco_divider;
    uint32_t mco_bits;
    uint32_t mclk_hz;
    uint32_t pll1src_bits;
    uint32_t pll1m;
    uint32_t pll1n;
    uint32_t pll1r;
    uint32_t pll1fracn;

    if(runtime == NULL)
    {
        return -1;
    }

    if((RCC->CFGR1 & RCC_CFGR1_MCOSEL) == RCC_MCO1SOURCE_NOCLOCK &&
       (runtime->flags & (XE_NP_RUNTIME_FLAG_NP_GLOBAL_RESET_RELEASED |
                          XE_NP_RUNTIME_FLAG_NP_RECORD_RESET_RELEASED |
                          XE_NP_RUNTIME_FLAG_NP_CAPTURE_DMA_ACTIVE)) == 0U)
    {
        runtime->flags &= ~XE_NP_RUNTIME_FLAG_NP_MCLK_VALID;
        return -1;
    }

    expected_mclk_hz = XE_NP_Board_GetRuntimeExpectedMcoHz();
    expected_mco_divider = XE_NP_Board_GetRuntimeMcoDivider();
    mclk_hz = expected_mclk_hz;
    if(XE_NP_Runtime_SetNpMclkHz(runtime, mclk_hz) != 0)
    {
        mclk_ok = 0U;
    }

    if(__HAL_RCC_GET_SYSCLK_SOURCE() != RCC_SYSCLKSOURCE_STATUS_PLLCLK)
    {
        mclk_ok = 0U;
    }

    if((RCC->PLL1CFGR & RCC_PLL1CFGR_PLL1REN) == 0U)
    {
        mclk_ok = 0U;
    }
    pll1src_bits = RCC->PLL1CFGR & RCC_PLL1CFGR_PLL1SRC;
    if(pll1src_bits != RCC_PLLSOURCE_HSE)
    {
        mclk_ok = 0U;
    }
    pll1m = ((RCC->PLL1CFGR & RCC_PLL1CFGR_PLL1M) >> RCC_PLL1CFGR_PLL1M_Pos) + 1U;
    pll1n = ((RCC->PLL1DIVR & RCC_PLL1DIVR_PLL1N) >> RCC_PLL1DIVR_PLL1N_Pos) + 1U;
    pll1r = ((RCC->PLL1DIVR & RCC_PLL1DIVR_PLL1R) >> RCC_PLL1DIVR_PLL1R_Pos) + 1U;
    pll1fracn = (RCC->PLL1FRACR & RCC_PLL1FRACR_PLL1FRACN) >> RCC_PLL1FRACR_PLL1FRACN_Pos;
    /* Match SystemClock_Config's boot-selected 20 kHz PLL, not the default core. */
    if((pll1m != XE_NP_BOARD_PLL1_M) || (pll1n != (np_mode_hz == 20000U ? 52U : XE_NP_BOARD_PLL1_N)) ||
       (pll1r != XE_NP_BOARD_PLL1R_DIV) ||
       (pll1fracn != XE_NP_BOARD_PLL1_FRACN))
    {
        mclk_ok = 0U;
    }

    mco_bits = RCC->CFGR1 & (RCC_CFGR1_MCOSEL | RCC_CFGR1_MCOPRE);
    if(expected_mclk_hz == 0U)
    {
        if(mco_bits != RCC_MCO1SOURCE_NOCLOCK)
        {
            mclk_ok = 0U;
        }
    }
    else if(expected_mco_divider == 16U)
    {
        if(mco_bits != (RCC_MCO1SOURCE_PLL1CLK | RCC_MCODIV_16))
        {
            mclk_ok = 0U;
        }
    }
    else if(expected_mco_divider == 8U)
    {
        if(mco_bits != (RCC_MCO1SOURCE_PLL1CLK | RCC_MCODIV_8))
        {
            mclk_ok = 0U;
        }
    }
    else if(expected_mco_divider == 4U)
    {
        if(mco_bits != (RCC_MCO1SOURCE_PLL1CLK | RCC_MCODIV_4))
        {
            mclk_ok = 0U;
        }
    }
    else if(expected_mco_divider == 2U)
    {
        if(mco_bits != (RCC_MCO1SOURCE_PLL1CLK | RCC_MCODIV_2))
        {
            mclk_ok = 0U;
        }
    }
    else if(expected_mco_divider == 1U)
    {
        if(mco_bits != (RCC_MCO1SOURCE_PLL1CLK | RCC_MCODIV_1))
        {
            mclk_ok = 0U;
        }
    }
    else
    {
        mclk_ok = 0U;
    }

    if((GPIOA->MODER & GPIO_MODER_MODE8) != GPIO_MODER_MODE8_1)
    {
        mclk_ok = 0U;
    }
    if((GPIOA->AFR[1] & GPIO_AFRH_AFSEL8) != 0U)
    {
        mclk_ok = 0U;
    }
    if((FLASH->OPTR & FLASH_OPTR_IO_VDD_HSLV) == 0U ||
       (GPIOA->HSLVR & GPIO_PIN_8) == 0U)
        mclk_ok = 0U;
    if((GPIOA->OSPEEDR & GPIO_OSPEEDR_OSPEED8) != GPIO_OSPEEDR_OSPEED8)
    {
        mclk_ok = 0U;
    }

    if((GPIOF->MODER & GPIO_MODER_MODE4) != GPIO_MODER_MODE4)
    {
        mclk_ok = 0U;
    }

    if(mclk_ok == 0U)
    {
        runtime->flags &= ~XE_NP_RUNTIME_FLAG_NP_MCLK_VALID;
        if(xe_np_mclk_error_reported == 0U)
        {
            CE32_ReportError(&runtime->sys_param, CE32_ERRORID_CORE_ERROR);
            xe_np_mclk_error_reported = 1U;
        }
        return -1;
    }

    runtime->flags |= XE_NP_RUNTIME_FLAG_NP_MCLK_VALID;
    xe_np_mclk_error_reported = 0U;
    return 0;
}


/* Cold/foreground only. These deadlines are discharge/settle guards, not
 * measured rail voltages. Camera may already own the shared VCC supply. */
#define NP_STARTUP_OFF_MS 50U
#define NP_STARTUP_CLOCK_SETTLE_MS 50U
#define NP_STARTUP_I2C_B_PULL_MASK 0x3cf00000UL
#define NP_STARTUP_I2C_C_PULL_MASK 0x0000000fUL
static uint32_t np_startup_i2c_b_pull,np_startup_i2c_c_pull;
static uint8_t np_startup_inputs_parked,np_camera_power_owner;

void XE_NP_App_SetCameraPowerOwner(uint8_t owned)
{np_camera_power_owner=owned!=0U;}

void XE_NP_App_ColdPowerOff(void)
{
    GPIO_InitTypeDef pins={0};
    __HAL_RCC_GPIOB_CLK_ENABLE();__HAL_RCC_GPIOC_CLK_ENABLE();__HAL_RCC_GPIOF_CLK_ENABLE();
    /* Set low output latches before changing mode. Never gate MCU/USB/BLE power. */
    HAL_GPIO_WritePin(VCC_EN_GPIO_Port,VCC_EN_Pin,GPIO_PIN_RESET);
    HAL_GPIO_WritePin(VEE_EN_GPIO_Port,VEE_EN_Pin,GPIO_PIN_RESET);
    HAL_GPIO_WritePin(NP_NRST_GPIO_Port,NP_NRST_Pin|NP_REC_NRST_Pin,GPIO_PIN_RESET);
    pins.Mode=GPIO_MODE_OUTPUT_PP;pins.Pull=GPIO_NOPULL;pins.Speed=GPIO_SPEED_FREQ_LOW;
    pins.Pin=VCC_EN_Pin;HAL_GPIO_Init(VCC_EN_GPIO_Port,&pins);
    pins.Pin=VEE_EN_Pin;HAL_GPIO_Init(VEE_EN_GPIO_Port,&pins);
    pins.Pin=NP_NRST_Pin|NP_REC_NRST_Pin;HAL_GPIO_Init(NP_NRST_GPIO_Port,&pins);
    HAL_RCC_MCOConfig(RCC_MCO1,RCC_MCO1SOURCE_NOCLOCK,RCC_MCODIV_1);
}

static void NP_StartupParkInputs(void)
{
    /* Do not clock an unpowered ASIC or feed it through MCU weak pull-ups.
     * External pull-ups can still feed the probe; firmware cannot remove them. */
    HAL_RCC_MCOConfig(RCC_MCO1,RCC_MCO1SOURCE_NOCLOCK,RCC_MCODIV_1);
    if(!np_startup_inputs_parked){
        np_startup_i2c_b_pull=GPIOB->PUPDR & NP_STARTUP_I2C_B_PULL_MASK;
        np_startup_i2c_c_pull=GPIOC->PUPDR & NP_STARTUP_I2C_C_PULL_MASK;
        np_startup_inputs_parked=1U;
    }
    CLEAR_BIT(GPIOB->PUPDR,NP_STARTUP_I2C_B_PULL_MASK);
    CLEAR_BIT(GPIOC->PUPDR,NP_STARTUP_I2C_C_PULL_MASK);
}

static int NP_StartupPowerStep(XE_NP_Runtime *runtime,uint32_t now)
{
    switch(xe_np_probe_state.bringup_stage){
    case XE_NP_APP_PROBE_STAGE_WAIT_POWER_OFF:
        if(!XE_NP_App_TimeReached(now,xe_np_probe_state.stage_deadline_tick_ms))return 1;
        XE_NP_App_SetProbePowerRails(runtime,1U);
        xe_np_probe_state.stage_deadline_tick_ms=now+XE_NP_APP_POWER_RAIL_SETTLE_MS;
        xe_np_probe_state.bringup_stage=XE_NP_APP_PROBE_STAGE_WAIT_POWER_RAILS;
        XE_NP_App_RefreshProbeStatus(runtime);return 1;
    case XE_NP_APP_PROBE_STAGE_WAIT_POWER_RAILS:
        if(!XE_NP_App_TimeReached(now,xe_np_probe_state.stage_deadline_tick_ms))return 1;
        MODIFY_REG(GPIOB->PUPDR,NP_STARTUP_I2C_B_PULL_MASK,np_startup_i2c_b_pull);
        MODIFY_REG(GPIOC->PUPDR,NP_STARTUP_I2C_C_PULL_MASK,np_startup_i2c_c_pull);
        np_startup_inputs_parked=0U;
        (void)XE_NP_Board_SetRuntimeMcoDivider(XE_NP_Board_GetRuntimeMcoDivider());
        XE_NP_App_ApplySensitivePinSafety();
        xe_np_mclk_next_refresh_tick_ms=0U;
        (void)XE_NP_App_ServiceNpMclkStatus(runtime);
        xe_np_probe_state.stage_deadline_tick_ms=now+NP_STARTUP_CLOCK_SETTLE_MS;
        xe_np_probe_state.bringup_stage=XE_NP_APP_PROBE_STAGE_WAIT_CLOCK_STABLE;
        XE_NP_App_RefreshProbeStatus(runtime);return 1;
    case XE_NP_APP_PROBE_STAGE_WAIT_CLOCK_STABLE:
        if(!XE_NP_App_TimeReached(now,xe_np_probe_state.stage_deadline_tick_ms))return 1;
        /* Rejoin the existing validated reset/configuration state machine. */
        xe_np_probe_state.bringup_stage=XE_NP_APP_PROBE_STAGE_WAIT_POWER_RAILS;
        xe_np_probe_state.stage_deadline_tick_ms=now;return 0;
    default:return 0;
    }
}

static void XE_NP_App_BeginProbeBringUpFromIdle(XE_NP_Runtime *runtime, uint32_t now_ms)
{
    if(runtime == NULL)
    {
        return;
    }

    memset(xe_np_probe_state.last_status, 0, sizeof(xe_np_probe_state.last_status));
    memset(xe_np_probe_state.last_cal_mod, 0, sizeof(xe_np_probe_state.last_cal_mod));
    memset(xe_np_probe_state.last_sync, 0, sizeof(xe_np_probe_state.last_sync));
    memset(xe_np_probe_state.last_rec_mod, 0, sizeof(xe_np_probe_state.last_rec_mod));
    memset(xe_np_probe_state.last_op_mode, 0, sizeof(xe_np_probe_state.last_op_mode));
    memset(xe_np_probe_state.last_verify_flags, 0, sizeof(xe_np_probe_state.last_verify_flags));
    XE_NP_App_ResetPsbClockProbeDiag();
    xe_np_probe_state.init_failed = 0U;
    xe_np_probe_state.init_error_reported = 0U;
    xe_np_probe_state.i2c_ready = 0U;
    xe_np_probe_state.base_configured = 0U;
    xe_np_probe_state.shank_defaults_active = 0U;
    xe_np_capture_start_phase_delay_armed = 0U;
    xe_np_capture_start_phase_aligned = 0U;
    xe_np_probe_state.active_probe_index = 0U;
    HAL_GPIO_WritePin(NP_REC_NRST_GPIO_Port, NP_REC_NRST_Pin, GPIO_PIN_RESET);
    HAL_GPIO_WritePin(NP_NRST_GPIO_Port, NP_NRST_Pin, GPIO_PIN_RESET);
    runtime->flags &= ~(XE_NP_RUNTIME_FLAG_NP_GLOBAL_RESET_RELEASED |
                        XE_NP_RUNTIME_FLAG_NP_RECORD_RESET_RELEASED);
    NP_StartupParkInputs();
    XE_NP_App_SetProbePowerRails(runtime, 0U);
    xe_np_probe_state.stage_deadline_tick_ms = now_ms + NP_STARTUP_OFF_MS;
    xe_np_probe_state.bringup_stage = XE_NP_APP_PROBE_STAGE_WAIT_POWER_OFF;
    XE_NP_App_RefreshProbeStatus(runtime);
}

#include "operation_led.h"
static void XE_NP_App_SetWakeLed(uint8_t enabled)
{
    WILDX_OperationLedApply(enabled != 0U ? WILDX_LED_IDLE : WILDX_LED_SLEEP);
}

void XE_NP_App_SetRecordingIndicator(uint8_t recording)
{
    WILDX_OperationLedApply(recording != 0U ? WILDX_LED_RECORDING : WILDX_LED_IDLE);
}

static int XE_NP_App_CaptureProducersRunning(const XE_NP_Runtime *runtime)
{
    uint32_t active_port_flags;

    if(runtime == NULL)
    {
        return 0;
    }

#if (XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_INTERLEAVE_ENABLE != 0U)
    if((runtime->capture.flags & XE_NP_CAPTURE_FLAG_GPIOE_ACTIVE) != 0U)
    {
        return (xe_np_quad_dma_active != 0U) ? 1 : 0;
    }
#endif

    active_port_flags =
        runtime->capture.flags & (XE_NP_CAPTURE_FLAG_GPIOE_ACTIVE | XE_NP_CAPTURE_FLAG_GPIOD_ACTIVE);
    if(active_port_flags == 0U)
    {
        return 0;
    }

    if(((active_port_flags & XE_NP_CAPTURE_FLAG_GPIOE_ACTIVE) != 0U) &&
       (xe_np_dma_gpioe.active == 0U))
    {
        return 0;
    }
    if(((active_port_flags & XE_NP_CAPTURE_FLAG_GPIOD_ACTIVE) != 0U) &&
       (xe_np_dma_gpiod.active == 0U))
    {
        return 0;
    }

    return 1;
}

static int XE_NP_App_CapturePathFaulted(const XE_NP_Runtime *runtime)
{
    uint32_t active_port_flags;
    uint8_t ignore_rate_fault;
    uint8_t ignore_validation_fault;

    if(runtime == NULL)
    {
        return 0;
    }

#if (XE_NP_APP_EXPERIMENT_CAPTURE_SUPPRESS_DMA_ENABLE != 0U)
    /* Hardware-isolation image only.  StartCaptureDma intentionally leaves
     * no live producer, so its absence must not immediately tear down the
     * otherwise-real NP session before the USB transport control can run. */
    return 0;
#endif

#if (XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_INTERLEAVE_ENABLE != 0U)
    if((runtime->capture.flags & XE_NP_CAPTURE_FLAG_GPIOE_ACTIVE) != 0U)
    {
        return ((xe_np_quad_dma_active == 0U) || (xe_np_quad_dma_error_seen != 0U)) ? 1 : 0;
    }
#endif

    active_port_flags =
        runtime->capture.flags & (XE_NP_CAPTURE_FLAG_GPIOE_ACTIVE | XE_NP_CAPTURE_FLAG_GPIOD_ACTIVE);
    ignore_rate_fault =
        (XE_NP_App_UsbCaptureStorageBypassActive(runtime) != 0) ? 1U : 0U;
    ignore_validation_fault = ignore_rate_fault;
#if (XE_NP_APP_RAW_SD_DEDICATED_CAPTURE_ENABLE != 0U)
    /* The dedicated SD characterization path validates the stored paired
     * stream off-line. Do not recycle a live session merely because the
     * generic per-lane validator (which assumes non-DDR snapshots) rejects
     * it. Hardware DMA faults and a stopped producer remain fatal. */
    ignore_rate_fault = 1U;
    ignore_validation_fault = 1U;
#endif
    if(active_port_flags == 0U)
    {
        return 0;
    }

    if(((active_port_flags & XE_NP_CAPTURE_FLAG_GPIOE_ACTIVE) != 0U) &&
       ((xe_np_dma_gpioe.active == 0U) ||
        (xe_np_dma_gpioe.dma_error_seen != 0U) ||
#if (XE_NP_APP_CAPTURE_SYNC_WATCHDOG_ENABLE != 0U)
        ((ignore_validation_fault == 0U) &&
         (xe_np_dma_gpioe.validation_error_seen != 0U)) ||
#endif
        ((ignore_rate_fault == 0U) && (xe_np_dma_gpioe.rate_error_seen != 0U))))
    {
        return 1;
    }
    if(((active_port_flags & XE_NP_CAPTURE_FLAG_GPIOD_ACTIVE) != 0U) &&
       ((xe_np_dma_gpiod.active == 0U) ||
        (xe_np_dma_gpiod.dma_error_seen != 0U) ||
#if (XE_NP_APP_CAPTURE_SYNC_WATCHDOG_ENABLE != 0U)
        ((ignore_validation_fault == 0U) &&
         (xe_np_dma_gpiod.validation_error_seen != 0U)) ||
#endif
        ((ignore_rate_fault == 0U) && (xe_np_dma_gpiod.rate_error_seen != 0U))))
    {
        return 1;
    }

    return 0;
}

static int XE_NP_App_CaptureOverrunLatched(const XE_NP_Runtime *runtime)
{
    uint32_t active_port_flags;
    uint32_t raw_mgr_state = 0U;

    if(runtime == NULL)
    {
        return 0;
    }

    active_port_flags =
        runtime->capture.flags & (XE_NP_CAPTURE_FLAG_GPIOE_ACTIVE | XE_NP_CAPTURE_FLAG_GPIOD_ACTIVE);
    if(active_port_flags == 0U)
    {
        return 0;
    }

    if((active_port_flags & XE_NP_CAPTURE_FLAG_GPIOE_ACTIVE) != 0U)
    {
        raw_mgr_state |= runtime->capture.gpioe.raw_mgr.logState;
    }
    if((active_port_flags & XE_NP_CAPTURE_FLAG_GPIOD_ACTIVE) != 0U)
    {
        raw_mgr_state |= runtime->capture.gpiod.raw_mgr.logState;
    }

    return ((raw_mgr_state & MGR_STATE_TRANSOVR) != 0U) ? 1 : 0;
}

static int XE_NP_App_UsbCaptureStorageBypassActive(const XE_NP_Runtime *runtime)
{
    uint32_t required_port_flag;

    if((runtime == NULL) || (xe_np_usb_capture_requested == 0U))
    {
        return 0;
    }

    if(xe_np_usb_capture_port_index == 0U)
    {
        required_port_flag = XE_NP_CAPTURE_FLAG_GPIOE_ACTIVE;
    }
    else if(xe_np_usb_capture_port_index == 1U)
    {
        required_port_flag = XE_NP_CAPTURE_FLAG_GPIOD_ACTIVE;
    }
    else
    {
        return 0;
    }

    return ((runtime->capture.flags & required_port_flag) != 0U) ? 1 : 0;
}

static int XE_NP_App_LegacyFsRecordPathArmed(const XE_NP_Runtime *runtime)
{
#if (XE_NP_APP_BENCH_CAPTURE_WITHOUT_STORAGE != 0U)
    (void)runtime;
    return 0;
#else
    uint32_t active_flags;

    if(runtime == NULL)
    {
        return 0;
    }

    active_flags = XE_NP_RUNTIME_FLAG_NP_GLOBAL_RESET_RELEASED |
                   XE_NP_RUNTIME_FLAG_NP_RECORD_RESET_RELEASED |
                   XE_NP_RUNTIME_FLAG_NP_CAPTURE_DMA_ACTIVE;
    return (((runtime->flags & active_flags) == active_flags) ? 1 : 0);
#endif
}

#if (XE_NP_APP_LEGACY_FS_ENABLE != 0U)
static int XE_NP_App_LegacyFsPathFaulted(const XE_NP_Runtime *runtime)
{
    uint32_t active_port_flags;

    if(XE_NP_App_LegacyFsRecordPathArmed(runtime) == 0)
    {
        return 0;
    }

    active_port_flags =
        runtime->capture.flags & (XE_NP_CAPTURE_FLAG_GPIOE_ACTIVE | XE_NP_CAPTURE_FLAG_GPIOD_ACTIVE);
    if(((active_port_flags & XE_NP_CAPTURE_FLAG_GPIOE_ACTIVE) != 0U) &&
       (xe_np_dma_gpioe.recorder_backlog_fault_seen != 0U))
    {
        return 1;
    }
    if(((active_port_flags & XE_NP_CAPTURE_FLAG_GPIOD_ACTIVE) != 0U) &&
       (xe_np_dma_gpiod.recorder_backlog_fault_seen != 0U))
    {
        return 1;
    }

    if(CE32_SystemCompat_GetLegacyFsError() != 0U)
    {
        return 1;
    }

    return (XE_NP_LegacyFs_IsActive() == 0) ? 1 : 0;
}
#endif

static int XE_NP_App_NpSessionActive(const XE_NP_Runtime *runtime)
{
    uint32_t active_flags;

    if(runtime == NULL)
    {
        return 0;
    }

    active_flags = XE_NP_RUNTIME_FLAG_NP_GLOBAL_RESET_RELEASED |
                   XE_NP_RUNTIME_FLAG_NP_RECORD_RESET_RELEASED |
                   XE_NP_RUNTIME_FLAG_NP_CAPTURE_DMA_ACTIVE;
    if((xe_np_probe_state.initialized != 0U) ||
       (xe_np_probe_state.bringup_stage != XE_NP_APP_PROBE_STAGE_IDLE) ||
       ((runtime->flags & active_flags) != 0U))
    {
        return 1;
    }

    return 0;
}

#if (XE_NP_APP_RAW_SD_SINK_ENABLE != 0U)
static int XE_NP_App_RawSdSinkPathFaulted(const XE_NP_Runtime *runtime)
{
    uint32_t active_flags;

#if (XE_NP_APP_BENCH_CAPTURE_WITHOUT_STORAGE != 0U)
    (void)runtime;
    return 0;
#else
    if((runtime == NULL) || (xe_np_probe_state.initialized == 0U))
    {
        return 0;
    }
    if(XE_NP_App_UsbCaptureStorageBypassActive(runtime) != 0)
    {
        return 0;
    }

    active_flags = XE_NP_RUNTIME_FLAG_NP_GLOBAL_RESET_RELEASED |
                   XE_NP_RUNTIME_FLAG_NP_RECORD_RESET_RELEASED |
                   XE_NP_RUNTIME_FLAG_NP_CAPTURE_DMA_ACTIVE;
    if((runtime->flags & active_flags) != active_flags)
    {
        return 0;
    }

    /*
     * A raw recorder is either lossless or failed.  Do not leave it running
     * after a write error, a capacity fault, or a high-water breach: the
     * caller will stop capture before the next DMA half can overwrite data.
     */
    return ((xe_np_raw_sd_sink.active == 0U) ||
            (xe_np_raw_sd_sink.error_reported != 0U) ||
            (xe_np_raw_sd_sink.total_blocks_dropped != 0U) ||
            (xe_np_raw_sd_sink.gpioe.error_seen != 0U) ||
            (xe_np_raw_sd_sink.gpiod.error_seen != 0U)) ? 1 : 0;
#endif
}
#endif

static const XE_NP_AppRawSdPort *XE_NP_App_GetRawSdSinkPortForDmaPort(const XE_NP_AppDmaPort *port)
{
    if(port == NULL)
    {
        return NULL;
    }

    if(port->port_index == 0U)
    {
        return &xe_np_raw_sd_sink.gpioe;
    }
    if(port->port_index == 1U)
    {
        return &xe_np_raw_sd_sink.gpiod;
    }

    return NULL;
}

static int XE_NP_App_PortHasRawLoggingBackpressure(const XE_NP_AppDmaPort *port)
{
    const XE_NP_AppRawSdPort *sink_port;
    uint32_t backlog_bytes;
    uint32_t backlog_threshold;

    if((port == NULL) || (port->capture_port == NULL) || (xe_np_raw_sd_sink.active == 0U))
    {
        return 0;
    }

    sink_port = XE_NP_App_GetRawSdSinkPortForDmaPort(port);
    if((sink_port == NULL) || (sink_port->active == 0U) ||
       (sink_port->mgr != &port->capture_port->raw_mgr))
    {
        return 0;
    }

    backlog_bytes = dataMGR_BytesAvailable(sink_port->mgr, sink_port->out_idx);
    backlog_threshold =
        (xe_np_raw_sd_sink.write_in_flight != 0U) ? XE_NP_APP_DMA_HALF_BYTES : XE_NP_APP_DMA_NODE_BYTES;
    if(backlog_threshold > port->capture_port->storage_bytes)
    {
        backlog_threshold = port->capture_port->storage_bytes;
    }

    return (backlog_bytes >= backlog_threshold) ? 1 : 0;
}

static uint32_t XE_NP_App_RecorderBacklogThresholdBytes(const XE_NP_CapturePort *capture_port)
{
    uint32_t threshold;

    if((capture_port == NULL) || (capture_port->storage_bytes == 0U))
    {
        return 0U;
    }

    threshold = (capture_port->storage_bytes > XE_NP_APP_RECORDER_BACKLOG_GUARD_BYTES) ?
                    (capture_port->storage_bytes - XE_NP_APP_RECORDER_BACKLOG_GUARD_BYTES) :
                    (capture_port->storage_bytes / 2U);
    if(threshold < XE_NP_APP_DMA_HALF_BYTES)
    {
        threshold = XE_NP_APP_DMA_HALF_BYTES;
    }
    if(threshold > capture_port->storage_bytes)
    {
        threshold = capture_port->storage_bytes;
    }

    return threshold;
}

static int XE_NP_App_PortAtHighMeasuredRate(const XE_NP_AppDmaPort *port)
{
    if((port == NULL) || (port->active == 0U))
    {
        return 0;
    }

    return (port->rate_ratio_pct2 >= XE_NP_APP_RECORDER_BACKLOG_HIGH_RATE_PCT2) ? 1 : 0;
}

static uint32_t XE_NP_App_RecorderBacklogThresholdBytesForPort(const XE_NP_AppDmaPort *port)
{
    uint32_t threshold;
    uint32_t stressed_threshold;
    const XE_NP_CapturePort *capture_port;

    if((port == NULL) || (port->capture_port == NULL))
    {
        return 0U;
    }

    capture_port = port->capture_port;
    threshold = XE_NP_App_RecorderBacklogThresholdBytes(capture_port);
    if((threshold == 0U) || (XE_NP_App_PortAtHighMeasuredRate(port) == 0))
    {
        return threshold;
    }

    stressed_threshold =
        (capture_port->storage_bytes > XE_NP_APP_RECORDER_BACKLOG_HIGH_RATE_GUARD_BYTES) ?
            (capture_port->storage_bytes - XE_NP_APP_RECORDER_BACKLOG_HIGH_RATE_GUARD_BYTES) :
            (capture_port->storage_bytes / 2U);
    if(stressed_threshold < XE_NP_APP_DMA_HALF_BYTES)
    {
        stressed_threshold = XE_NP_APP_DMA_HALF_BYTES;
    }
    if(stressed_threshold > capture_port->storage_bytes)
    {
        stressed_threshold = capture_port->storage_bytes;
    }

    return (stressed_threshold < threshold) ? stressed_threshold : threshold;
}

static int XE_NP_App_PortHasRecorderBacklogPressure(const XE_NP_AppDmaPort *port)
{
    uint32_t backlog_bytes;
    uint32_t backlog_threshold;

    if((port == NULL) || (port->capture_port == NULL) || (XE_NP_LegacyFs_IsActive() == 0))
    {
        return 0;
    }

    backlog_threshold = XE_NP_App_RecorderBacklogThresholdBytesForPort(port);
    if(backlog_threshold == 0U)
    {
        return 0;
    }

    backlog_bytes = dataMGR_BytesAvailable(&port->capture_port->raw_mgr, 0U);
    return (backlog_bytes >= backlog_threshold) ? 1 : 0;
}

static uint32_t XE_NP_App_PortRecorderBacklogBytes(const XE_NP_AppDmaPort *port)
{
    if((port == NULL) || (port->capture_port == NULL))
    {
        return 0U;
    }

    return dataMGR_BytesAvailable(&port->capture_port->raw_mgr, 0U);
}

static uint32_t XE_NP_App_ActiveRawBytesPerSec(const XE_NP_Runtime *runtime)
{
    uint32_t raw_bytes_per_sec = 0U;

    if(runtime == NULL)
    {
        return 0U;
    }

    if(((runtime->capture.flags & XE_NP_CAPTURE_FLAG_GPIOE_ACTIVE) != 0U) &&
       (xe_np_dma_gpioe.active != 0U))
    {
        raw_bytes_per_sec += runtime->capture.gpioe.raw_bytes_per_sec;
    }
    if(((runtime->capture.flags & XE_NP_CAPTURE_FLAG_GPIOD_ACTIVE) != 0U) &&
       (xe_np_dma_gpiod.active != 0U))
    {
        raw_bytes_per_sec += runtime->capture.gpiod.raw_bytes_per_sec;
    }

    return raw_bytes_per_sec;
}

static int XE_NP_App_RecorderRateBacklogTriggerPresent(const XE_NP_Runtime *runtime)
{
    if(runtime == NULL)
    {
        return 0;
    }

    if(((runtime->capture.flags & XE_NP_CAPTURE_FLAG_GPIOE_ACTIVE) != 0U) &&
       (XE_NP_App_PortRecorderBacklogBytes(&xe_np_dma_gpioe) >=
        XE_NP_APP_RECORDER_RATE_BACKLOG_TRIGGER_BYTES))
    {
        return 1;
    }
    if(((runtime->capture.flags & XE_NP_CAPTURE_FLAG_GPIOD_ACTIVE) != 0U) &&
       (XE_NP_App_PortRecorderBacklogBytes(&xe_np_dma_gpiod) >=
        XE_NP_APP_RECORDER_RATE_BACKLOG_TRIGGER_BYTES))
    {
        return 1;
    }

    return 0;
}

static void XE_NP_App_ResetRecorderRateMonitor(void)
{
    xe_np_recorder_rate_bad_samples = 0U;
    xe_np_recorder_rate_fault_seen = 0U;
    xe_np_recorder_rate_sample_tick_ms = 0U;
    xe_np_recorder_rate_sample_blocks = 0U;
}

static void XE_NP_App_LatchRecorderRateFault(void)
{
    xe_np_recorder_rate_fault_seen = 1U;
    if((xe_np_runtime.capture.flags & XE_NP_CAPTURE_FLAG_GPIOE_ACTIVE) != 0U)
    {
        xe_np_dma_gpioe.recorder_backlog_fault_seen = 1U;
        xe_np_runtime.capture.gpioe.raw_mgr.logState |= MGR_STATE_TRANSERR;
    }
    if((xe_np_runtime.capture.flags & XE_NP_CAPTURE_FLAG_GPIOD_ACTIVE) != 0U)
    {
        xe_np_dma_gpiod.recorder_backlog_fault_seen = 1U;
        xe_np_runtime.capture.gpiod.raw_mgr.logState |= MGR_STATE_TRANSERR;
    }
    CE32_ReportError(&xe_np_runtime.sys_param, CE32_ERRORID_CORE_ERROR);
}

static void XE_NP_App_ServiceRecorderRateWatchdog(const XE_NP_Runtime *runtime)
{
    uint32_t now_ms;
    uint32_t dt_ms;
    uint32_t file_blocks;
    uint32_t delta_blocks;
    uint32_t raw_bytes_per_sec;
    uint64_t bytes_delta;
    uint64_t expected_bytes;
    uint64_t lower_bound;

    if((runtime == NULL) || (XE_NP_App_LegacyFsRecordPathArmed(runtime) == 0) ||
       (XE_NP_LegacyFs_IsActive() == 0))
    {
        XE_NP_App_ResetRecorderRateMonitor();
        return;
    }

    raw_bytes_per_sec = XE_NP_App_ActiveRawBytesPerSec(runtime);
    if((raw_bytes_per_sec == 0U) ||
       ((XE_NP_App_PortAtHighMeasuredRate(&xe_np_dma_gpioe) == 0) &&
        (XE_NP_App_PortAtHighMeasuredRate(&xe_np_dma_gpiod) == 0)))
    {
        xe_np_recorder_rate_bad_samples = 0U;
        xe_np_recorder_rate_sample_tick_ms = 0U;
        xe_np_recorder_rate_sample_blocks = 0U;
        return;
    }

    now_ms = HAL_GetTick();
    file_blocks = fs.file_block_cnt;
    if(xe_np_recorder_rate_sample_tick_ms == 0U)
    {
        xe_np_recorder_rate_sample_tick_ms = now_ms;
        xe_np_recorder_rate_sample_blocks = file_blocks;
        return;
    }

    dt_ms = (uint32_t)(now_ms - xe_np_recorder_rate_sample_tick_ms);
    if(dt_ms < XE_NP_APP_RECORDER_RATE_MONITOR_INTERVAL_MS)
    {
        return;
    }

    delta_blocks = file_blocks - xe_np_recorder_rate_sample_blocks;
    xe_np_recorder_rate_sample_tick_ms = now_ms;
    xe_np_recorder_rate_sample_blocks = file_blocks;

    bytes_delta = (uint64_t)delta_blocks * 512ULL;
    expected_bytes =
        ((uint64_t)raw_bytes_per_sec * (uint64_t)dt_ms + 500ULL) / 1000ULL;
    lower_bound = (expected_bytes * (100U - XE_NP_APP_RECORDER_RATE_TOLERANCE_PCT)) / 100U;

    if((bytes_delta < lower_bound) &&
       (XE_NP_App_RecorderRateBacklogTriggerPresent(runtime) != 0))
    {
        if(xe_np_recorder_rate_bad_samples < 0xFFU)
        {
            xe_np_recorder_rate_bad_samples++;
        }
        if((xe_np_recorder_rate_bad_samples >= XE_NP_APP_RECORDER_RATE_BAD_SAMPLE_LIMIT) &&
           (xe_np_recorder_rate_fault_seen == 0U))
        {
            XE_NP_App_LatchRecorderRateFault();
        }
    }
    else
    {
        xe_np_recorder_rate_bad_samples = 0U;
    }
}

static void XE_NP_App_ServiceRecorderBacklogWatchdogPort(XE_NP_AppDmaPort *port)
{
    uint32_t now_ms;

    if((port == NULL) || (port->capture_port == NULL) || (port->active == 0U))
    {
        return;
    }

    if(XE_NP_App_PortHasRecorderBacklogPressure(port) == 0)
    {
        port->recorder_backlog_high_since_tick_ms = 0U;
        return;
    }

    now_ms = HAL_GetTick();
    if(port->recorder_backlog_high_since_tick_ms == 0U)
    {
        port->recorder_backlog_high_since_tick_ms = now_ms;
        return;
    }

    if((uint32_t)(now_ms - port->recorder_backlog_high_since_tick_ms) <
       XE_NP_APP_RECORDER_BACKLOG_GRACE_MS)
    {
        return;
    }

    port->recorder_backlog_fault_seen = 1U;
    port->capture_port->raw_mgr.logState |= MGR_STATE_TRANSERR;
}

static void XE_NP_App_ServiceRecorderBacklogWatchdogs(const XE_NP_Runtime *runtime)
{
    if((runtime == NULL) || (XE_NP_App_LegacyFsRecordPathArmed(runtime) == 0) ||
       (XE_NP_LegacyFs_IsActive() == 0))
    {
        xe_np_dma_gpioe.recorder_backlog_high_since_tick_ms = 0U;
        xe_np_dma_gpiod.recorder_backlog_high_since_tick_ms = 0U;
        XE_NP_App_ResetRecorderRateMonitor();
        return;
    }

    XE_NP_App_ServiceRecorderRateWatchdog(runtime);
    XE_NP_App_ServiceRecorderBacklogWatchdogPort(&xe_np_dma_gpioe);
    XE_NP_App_ServiceRecorderBacklogWatchdogPort(&xe_np_dma_gpiod);
}

static void XE_NP_App_ResetCapturePortRuntimeState(XE_NP_CapturePort *capture_port)
{
    if((capture_port == NULL) || (capture_port->storage == NULL) || (capture_port->storage_bytes == 0U))
    {
        return;
    }

    dataMGR_init(&capture_port->raw_mgr, (char *)capture_port->storage, capture_port->storage_bytes);
    dataMGR_setActiveOutMask(&capture_port->raw_mgr, 0U);
    capture_port->total_bytes_captured = 0ULL;
    capture_port->dma_windows_captured = 0U;
    capture_port->validated_probe_mask = 0U;
    capture_port->frame_alignment_locked = 0U;
    capture_port->frame_alignment_ticks = 0U;
}

static void XE_NP_App_ResetCaptureRuntimeState(XE_NP_CaptureState *capture)
{
    if(capture == NULL)
    {
        return;
    }

    XE_NP_App_ResetCapturePortRuntimeState(&capture->gpioe);
    XE_NP_App_ResetCapturePortRuntimeState(&capture->gpiod);
    for(uint8_t i = 0U; i < XE_NP_NEUROPIXELS_MAX_PROBES; ++i)
    {
        XE_NP_PsbValidator_Init(&capture->validators[i]);
    }
}

static void XE_NP_App_LatchCaptureBenchPortDiag(
    XE_NP_AppCaptureBenchPortDiag *dst,
    const XE_NP_AppDmaPort *port)
{
    size_t sample_words;

    if(dst == NULL)
    {
        return;
    }

    memset(dst, 0, sizeof(*dst));
    if((port == NULL) || (port->capture_port == NULL))
    {
        return;
    }

    dst->active = port->active;
    dst->pending_read_idx = port->pending_read_idx;
    dst->pending_write_idx = port->pending_write_idx;
    dst->pending_peak_depth = port->pending_peak_depth;
    dst->sync_watchdog_pending = port->sync_watchdog_pending;
    dst->sync_watchdog_fail_count = port->sync_watchdog_fail_count;
    dst->dma_error_seen = port->dma_error_seen;
    dst->validation_error_seen = port->validation_error_seen;
    dst->rate_error_seen = port->rate_error_seen;
    dst->capture_timing_flags = port->capture_timing_flags;
    dst->byte_sync_best_offset = port->byte_sync_best_offset;
    dst->byte_sync_best_sync0 = port->byte_sync_best_sync0;
    dst->byte_sync_best_sync1 = port->byte_sync_best_sync1;
    dst->byte_sync_best_flags0 = port->byte_sync_best_flags0;
    dst->byte_sync_best_flags1 = port->byte_sync_best_flags1;
    dst->byte_sync_best_clean_frames = port->byte_sync_best_clean_frames;
    dst->byte_sync_best_sync_valid_frames = port->byte_sync_best_sync_valid_frames;
    dst->byte_sync_best_frame_skip = port->byte_sync_best_frame_skip;
    dst->byte_sync_best_phase = port->byte_sync_best_phase;
    dst->byte_sync_sample_frames = port->byte_sync_sample_frames;
    dst->continuity_ok_count = port->continuity_ok_count;
    dst->continuity_error_count = port->continuity_error_count;
    dst->continuity_last_expected_counter = port->continuity_last_expected_counter;
    dst->continuity_last_observed_counter = port->continuity_last_observed_counter;
    dst->continuity_last_frame_delta = port->continuity_last_frame_delta;
    dst->live_decode_ok_count = port->live_decode_ok_count;
    dst->live_decode_fail_count = port->live_decode_fail_count;
    dst->wrap_probe_count = port->wrap_probe_count;
    dst->wrap_probe_tail_offset = port->wrap_probe_tail_offset;
    dst->wrap_probe_head_offset = port->wrap_probe_head_offset;
    dst->wrap_probe_tail_total_low = (uint32_t)port->wrap_probe_tail_total_bytes_end;
    dst->wrap_probe_head_total_low = (uint32_t)port->wrap_probe_head_total_bytes_end;
    dst->wrap_probe_sample_bytes = XE_NP_APP_CAPTURE_WRAP_PROBE_TOTAL_BYTES;
    dst->wrap_probe_valid = port->wrap_probe_valid;
    memcpy(dst->wrap_probe_bytes, port->wrap_probe_bytes, sizeof(dst->wrap_probe_bytes));
    dst->first_backlog_resync_tick_ms = port->first_backlog_resync_tick_ms;
    dst->first_backlog_total_bytes_low = port->first_backlog_total_bytes_low;
    dst->first_backlog_last_queued_offset_bytes = port->first_backlog_last_queued_offset_bytes;
    dst->first_backlog_last_dequeued_offset_bytes = port->first_backlog_last_dequeued_offset_bytes;
    dst->first_backlog_resync_valid = port->first_backlog_resync_valid;
    dst->first_backlog_reason = port->first_backlog_reason;
    dst->first_backlog_pending_read_idx = port->first_backlog_pending_read_idx;
    dst->first_backlog_pending_write_idx = port->first_backlog_pending_write_idx;
    dst->first_backlog_current_depth = port->first_backlog_current_depth;
    dst->first_backlog_max_pending_windows = port->first_backlog_max_pending_windows;
    dst->first_backlog_usb_active_window_valid = port->first_backlog_usb_active_window_valid;
    dst->first_backlog_stable_window_valid = port->first_backlog_stable_window_valid;
    dst->dma_fault_tick_ms = port->dma_fault_tick_ms;
    dst->dma_fault_source = port->dma_fault_source;
    dst->dma_fault_csr = port->dma_fault_csr;
    dst->dma_fault_ccr = port->dma_fault_ccr;
    dst->dma_fault_ctr1 = port->dma_fault_ctr1;
    dst->dma_fault_ctr2 = port->dma_fault_ctr2;
    dst->dma_fault_cbr1 = port->dma_fault_cbr1;
    dst->dma_fault_csar = port->dma_fault_csar;
    dst->dma_fault_cdar = port->dma_fault_cdar;
    dst->dma_fault_cllr = port->dma_fault_cllr;
    dst->dma_fault_error_code = port->dma_fault_error_code;
    dst->sync_watchdog_ok_count = port->sync_watchdog_ok_count;
    dst->sync_watchdog_error_count = port->sync_watchdog_error_count;
    dst->windows_processed = port->windows_processed;
    dst->active_probe_mask = port->capture_port->active_probe_mask;
    dst->validated_probe_mask = port->capture_port->validated_probe_mask;
    dst->live_decode_probe_mask = port->live_decode_probe_mask;
    dst->capture_bytes_per_tick = port->capture_port->bytes_per_tick;
    dst->dma_source_width_bytes = port->dma_source_width_bytes;
    dst->gpio_source_addr_lsb = port->gpio_source_addr_lsb;
    dst->dma_queue_node_count = port->dma_queue_node_count;
    dst->dma_repeat_block_count = port->dma_repeat_block_count;
    dst->frame_alignment_locked = port->capture_port->frame_alignment_locked;
    dst->frame_alignment_ticks = port->capture_port->frame_alignment_ticks;
    dst->storage_bytes = port->capture_port->storage_bytes;
    dst->total_bytes_low = (uint32_t)port->capture_port->total_bytes_captured;
    dst->dma_windows_captured = port->capture_port->dma_windows_captured;
    dst->raw_log_state = port->capture_port->raw_mgr.logState;

    if((port->htim != NULL) && (port->htim->Instance != NULL))
    {
        dst->tim_cnt = port->htim->Instance->CNT;
        dst->tim_cr1 = port->htim->Instance->CR1;
        dst->tim_smcr = port->htim->Instance->SMCR;
        dst->tim_dier = port->htim->Instance->DIER;
        dst->tim_sr = port->htim->Instance->SR;
    }

    if((port->hdma.Instance != NULL))
    {
        dst->dma_csr = port->hdma.Instance->CSR;
        dst->dma_ccr = port->hdma.Instance->CCR;
        dst->dma_ctr1 = port->hdma.Instance->CTR1;
        dst->dma_ctr2 = port->hdma.Instance->CTR2;
        dst->dma_cbr1 = port->hdma.Instance->CBR1;
        dst->dma_csar = port->hdma.Instance->CSAR;
        dst->dma_cdar = port->hdma.Instance->CDAR;
        dst->dma_cllr = port->hdma.Instance->CLLR;
    }

    if((port->capture_port->storage == NULL) ||
       (port->capture_port->storage_bytes < sizeof(uint16_t)))
    {
        return;
    }

    sample_words = port->capture_port->storage_bytes / sizeof(uint16_t);
    if(sample_words > XE_NP_APP_CAPTURE_BENCH_SAMPLE_WORDS)
    {
        sample_words = XE_NP_APP_CAPTURE_BENCH_SAMPLE_WORDS;
    }

    XE_NP_Capture_DCacheInvalidate(
        port->capture_port->storage,
        (uint32_t)(sample_words * sizeof(uint16_t)));
    memcpy(
        dst->sample_words,
        port->capture_port->storage,
        sample_words * sizeof(uint16_t));
}

static void XE_NP_App_LatchCaptureBenchDiag(const XE_NP_Runtime *runtime, uint32_t reason)
{
    XE_NP_AppCaptureBenchDiag diag;

    memset(&diag, 0, sizeof(diag));
    diag.magic = XE_NP_APP_CAPTURE_BENCH_DIAG_MAGIC;
    diag.version = XE_NP_APP_CAPTURE_BENCH_DIAG_VERSION;
    diag.tick_ms = HAL_GetTick();
    diag.reason = reason;
    diag.gpioe_idr = GPIOE->IDR;
    diag.gpiod_idr = GPIOD->IDR;
    diag.gpioc_idr = GPIOC->IDR;

    if(runtime != NULL)
    {
        diag.runtime_flags = runtime->flags;
        diag.capture_flags = runtime->capture.flags;
        diag.active_probe_mask =
            (uint8_t)(runtime->active_np_probe_mask & XE_NP_CAPTURE_ALL_PROBES_MASK);
        diag.capture_layout = (uint8_t)runtime->capture.layout;
        diag.active_port_count = runtime->capture.active_port_count;
    }

    XE_NP_App_LatchCaptureBenchPortDiag(&diag.gpioe, &xe_np_dma_gpioe);
    XE_NP_App_LatchCaptureBenchPortDiag(&diag.gpiod, &xe_np_dma_gpiod);
    xe_np_capture_bench_diag = diag;
}

static void XE_NP_App_UpdateStreamDiag(const XE_NP_Runtime *runtime)
{
    XE_NP_AppStreamDiag diag;
    I2C_HandleTypeDef *hi2c_np2 = NULL;

    memset(&diag, 0, sizeof(diag));
    diag.magic = XE_NP_APP_STREAM_DIAG_MAGIC;
    diag.version = XE_NP_APP_STREAM_DIAG_VERSION;
    diag.tick_ms = HAL_GetTick();
    diag.next_retry_tick_ms = xe_np_probe_state.next_retry_tick_ms;
    diag.stage_deadline_tick_ms = xe_np_probe_state.stage_deadline_tick_ms;
    diag.gpioa_idr = GPIOA->IDR;
    diag.gpiob_idr = GPIOB->IDR;
    diag.gpioc_idr = GPIOC->IDR;
    diag.gpioe_idr = GPIOE->IDR;

    diag.bringup_stage = xe_np_probe_state.bringup_stage;
    diag.initialized = xe_np_probe_state.initialized;
    diag.init_failed = xe_np_probe_state.init_failed;
    diag.i2c_ready = xe_np_probe_state.i2c_ready;
    diag.base_configured = xe_np_probe_state.base_configured;
    diag.active_probe_index = xe_np_probe_state.active_probe_index;
    if((xe_np_probe_last_verify_valid_mask & XE_NP_CAPTURE_PROBE2_MASK) != 0U)
    {
        diag.last_status_np2 = xe_np_probe_last_status_retained[1];
        diag.last_verify_flags_np2 = xe_np_probe_last_verify_flags_retained[1];
        diag.last_cal_mod_np2 = xe_np_probe_last_cal_mod_retained[1];
        diag.last_sync_np2 = xe_np_probe_last_sync_retained[1];
        diag.last_rec_mod_np2 = xe_np_probe_last_rec_mod_retained[1];
        diag.last_op_mode_np2 = xe_np_probe_last_op_mode_retained[1];
    }
    else
    {
        diag.last_status_np2 = xe_np_probe_state.last_status[1];
        diag.last_verify_flags_np2 = xe_np_probe_state.last_verify_flags[1];
        diag.last_cal_mod_np2 = xe_np_probe_state.last_cal_mod[1];
        diag.last_sync_np2 = xe_np_probe_state.last_sync[1];
        diag.last_rec_mod_np2 = xe_np_probe_state.last_rec_mod[1];
        diag.last_op_mode_np2 = xe_np_probe_state.last_op_mode[1];
    }
    diag.record_reset_released =
        (HAL_GPIO_ReadPin(NP_REC_NRST_GPIO_Port, NP_REC_NRST_Pin) == GPIO_PIN_SET) ? 1U : 0U;
    diag.global_reset_released =
        (HAL_GPIO_ReadPin(NP_NRST_GPIO_Port, NP_NRST_Pin) == GPIO_PIN_SET) ? 1U : 0U;

    if(runtime != NULL)
    {
        diag.runtime_flags = runtime->flags;
        diag.capture_flags = runtime->capture.flags;
        diag.active_probe_mask =
            (uint8_t)(runtime->active_np_probe_mask & XE_NP_CAPTURE_ALL_PROBES_MASK);
        hi2c_np2 = runtime->hi2c_np[1];
    }

    if(htim2.Instance != NULL)
    {
        diag.tim2_cr1 = htim2.Instance->CR1;
        diag.tim2_smcr = htim2.Instance->SMCR;
        diag.tim2_dier = htim2.Instance->DIER;
        diag.tim2_sr = htim2.Instance->SR;
        diag.tim2_cnt = htim2.Instance->CNT;
    }

    diag.psb_clock_probe_count = xe_np_psb_clock_probe_count;
    diag.psb_clock_probe_flags = xe_np_psb_clock_probe_flags;
    diag.psb_clock_probe_gpioa_start = xe_np_psb_clock_probe_gpioa_start;
    diag.psb_clock_probe_gpioa_end = xe_np_psb_clock_probe_gpioa_end;
    diag.psb_clock_probe_tim2_sr = xe_np_psb_clock_probe_tim2_sr;

    if(xe_np_dma_gpioe.hdma.Instance != NULL)
    {
        diag.dma12_csr = xe_np_dma_gpioe.hdma.Instance->CSR;
        diag.dma12_ccr = xe_np_dma_gpioe.hdma.Instance->CCR;
        diag.dma12_cbr1 = xe_np_dma_gpioe.hdma.Instance->CBR1;
        diag.dma12_csar = xe_np_dma_gpioe.hdma.Instance->CSAR;
        diag.dma12_cdar = xe_np_dma_gpioe.hdma.Instance->CDAR;
        diag.dma12_cllr = xe_np_dma_gpioe.hdma.Instance->CLLR;
    }
    diag.dma12_last_queued_offset_bytes = xe_np_dma_gpioe.last_queued_offset_bytes;
    diag.dma12_last_queued_size_bytes = xe_np_dma_gpioe.last_queued_size_bytes;
    diag.dma12_last_dequeued_offset_bytes = xe_np_dma_gpioe.last_dequeued_offset_bytes;
    diag.dma12_last_dequeued_size_bytes = xe_np_dma_gpioe.last_dequeued_size_bytes;
    diag.dma12_queued_size_min_bytes = xe_np_dma_gpioe.queued_size_min_bytes;
    diag.dma12_queued_size_max_bytes = xe_np_dma_gpioe.queued_size_max_bytes;
    diag.dma12_dequeued_size_min_bytes = xe_np_dma_gpioe.dequeued_size_min_bytes;
    diag.dma12_dequeued_size_max_bytes = xe_np_dma_gpioe.dequeued_size_max_bytes;
    diag.dma12_queued_window_count = xe_np_dma_gpioe.queued_window_count;
    diag.dma12_dequeued_window_count = xe_np_dma_gpioe.dequeued_window_count;
    diag.dma12_direct_repeat_mode = xe_np_dma_gpioe.direct_repeat_mode;
    diag.dma12_queue_node_count = xe_np_dma_gpioe.dma_queue_node_count;
    diag.dma12_repeat_block_count = xe_np_dma_gpioe.dma_repeat_block_count;

    if(hi2c_np2 != NULL)
    {
        diag.i2c4_error_code = hi2c_np2->ErrorCode;
        diag.i2c4_isr =
            (hi2c_np2->Instance != NULL) ? hi2c_np2->Instance->ISR : 0U;
    }

    diag.nric_tick_ms = xe_np_nric_diag.tick_ms;
    diag.nric_i2c_error_code = xe_np_nric_diag.i2c_error_code;
    diag.nric_i2c_isr = xe_np_nric_diag.i2c_isr;
    diag.nric_step = xe_np_nric_diag.step;
    diag.nric_reg_addr = xe_np_nric_diag.reg_addr;
    diag.nric_hal_status = xe_np_nric_diag.hal_status;
    diag.nric_status_reg = xe_np_nric_diag.status_reg;
    diag.nric_read_value = xe_np_nric_diag.read_value;
    diag.nric_expected_value = xe_np_nric_diag.expected_value;

    xe_np_stream_diag = diag;
}

static int XE_NP_App_SetProbeCalDacRawInternal(XE_NP_Runtime *runtime, uint16_t dac_code)
{
    if((runtime == NULL) || (runtime->hdac_np_cal == NULL))
    {
        return -1;
    }

    dac_code &= 0x0FFFU;
    if(HAL_DAC_SetValue(runtime->hdac_np_cal, DAC_CHANNEL_1, DAC_ALIGN_12B_R, dac_code) != HAL_OK)
    {
        return -1;
    }
    if(HAL_DAC_Start(runtime->hdac_np_cal, DAC_CHANNEL_1) != HAL_OK)
    {
        return -1;
    }

    xe_np_probe_cal_dac_code = dac_code;
    return 0;
}

static void XE_NP_App_InitializeProbeCalLine(XE_NP_Runtime *runtime)
{
    if(XE_NP_App_SetProbeCalDacRawInternal(runtime, 0U) != 0)
    {
        CE32_ReportError(&runtime->sys_param, CE32_ERRORID_CORE_ERROR);
    }
}

static uint8_t XE_NP_App_DefaultProbeSyncValue(uint8_t probe_index)
{
    return (uint8_t)((probe_index + 1U) * XE_NP_NRIC_DEFAULT_SYNC_VALUE);
}

static void XE_NP_App_EnsureProbeConfigsInitialized(void)
{
    if(xe_np_probe_config_store.initialized != 0U)
    {
        return;
    }

    memset(&xe_np_probe_config_store, 0, sizeof(xe_np_probe_config_store));
    for(uint8_t probe_index = 0U; probe_index < XE_NP_NEUROPIXELS_MAX_PROBES; ++probe_index)
    {
        XE_NP_NricConfig_InitDefaults(&xe_np_probe_config_store.probe_config[probe_index]);
        xe_np_probe_config_store.probe_config[probe_index].sync_value =
            XE_NP_App_DefaultProbeSyncValue(probe_index);
        XE_NP_PsbValidator_SetExpectedSyncValue(
            &xe_np_runtime.capture.validators[probe_index],
            xe_np_probe_config_store.probe_config[probe_index].sync_value);
    }
    xe_np_probe_config_store.initialized = 1U;
}

static int XE_NP_App_ActiveProbeConfigsReady(const XE_NP_Runtime *runtime)
{
    uint8_t active_probe_mask;
#if !XE_NP_NRIC_USE_SR_DEFAULT_STARTUP
    uint8_t base_mask;
#endif

    if(runtime == NULL)
    {
        return 0;
    }

    XE_NP_App_EnsureProbeConfigsInitialized();
    active_probe_mask = (uint8_t)(runtime->active_np_probe_mask & XE_NP_CAPTURE_ALL_PROBES_MASK);
#if XE_NP_NRIC_USE_SR_DEFAULT_STARTUP
    (void)active_probe_mask;
    return 1;
#else
    base_mask = xe_np_probe_config_store.state.base_override_mask & xe_np_probe_config_store.state.shank_configured_mask;

    /*
     * The direct-NRIC bench path programs the documented base chains at 0x0c
     * and 0x0d. The extracted shank chain is not a valid NRIC memory-map chain
     * for this board revision because 0x0e is reserved in the NRIC1384 map.
     */
    return ((active_probe_mask & base_mask) == active_probe_mask) ? 1 : 0;
#endif
}

static int XE_NP_App_ActiveProbeCalibrationReady(const XE_NP_Runtime *runtime)
{
    uint8_t active_probe_mask;
    uint8_t calibration_mask;

    if(runtime == NULL)
    {
        return 0;
    }

    XE_NP_App_EnsureProbeConfigsInitialized();
    active_probe_mask = (uint8_t)(runtime->active_np_probe_mask & XE_NP_CAPTURE_ALL_PROBES_MASK);
    if(active_probe_mask == 0U)
    {
        return 0;
    }

    calibration_mask = xe_np_probe_config_store.state.calibration_override_mask;
    return ((active_probe_mask & calibration_mask) == active_probe_mask) ? 1 : 0;
}

static void XE_NP_App_RefreshProbeStatus(XE_NP_Runtime *runtime);
static void XE_NP_App_RefreshCaptureDiag(CE32_systemParam *sys_param);

static uint8_t XE_NP_App_DiagProbeIndex(const XE_NP_Runtime *runtime, uint8_t probe_index)
{
    if(probe_index < XE_NP_NEUROPIXELS_MAX_PROBES)
    {
        return probe_index;
    }
    if(runtime == NULL)
    {
        return 0U;
    }
    for(uint8_t i = 0U; i < XE_NP_NEUROPIXELS_MAX_PROBES; ++i)
    {
        if((runtime->active_np_probe_mask & (1U << i)) != 0U)
        {
            return i;
        }
    }
    return 0U;
}

static void XE_NP_App_GetProbeI2cPins(
    uint8_t probe_index,
    GPIO_TypeDef **scl_port,
    uint16_t *scl_pin,
    GPIO_TypeDef **sda_port,
    uint16_t *sda_pin)
{
    GPIO_TypeDef *local_scl_port = GPIOC;
    GPIO_TypeDef *local_sda_port = GPIOC;
    uint16_t local_scl_pin = GPIO_PIN_0;
    uint16_t local_sda_pin = GPIO_PIN_1;

    if(probe_index == 1U)
    {
        local_scl_port = NP2_SCL_GPIO_Port;
        local_sda_port = NP2_SDA_GPIO_Port;
        local_scl_pin = NP2_SCL_Pin;
        local_sda_pin = NP2_SDA_Pin;
    }
    else if(probe_index == 2U)
    {
        local_scl_port = GPIOB;
        local_sda_port = GPIOB;
        local_scl_pin = GPIO_PIN_13;
        local_sda_pin = GPIO_PIN_14;
    }

    if(scl_port != NULL)
    {
        *scl_port = local_scl_port;
    }
    if(scl_pin != NULL)
    {
        *scl_pin = local_scl_pin;
    }
    if(sda_port != NULL)
    {
        *sda_port = local_sda_port;
    }
    if(sda_pin != NULL)
    {
        *sda_pin = local_sda_pin;
    }
}

static void XE_NP_App_ReadProbeI2cPins(
    uint8_t probe_index,
    GPIO_PinState *scl_state,
    GPIO_PinState *sda_state)
{
    GPIO_TypeDef *scl_port;
    GPIO_TypeDef *sda_port;
    uint16_t scl_pin;
    uint16_t sda_pin;

    XE_NP_App_GetProbeI2cPins(
        probe_index,
        &scl_port,
        &scl_pin,
        &sda_port,
        &sda_pin);

    if(scl_state != NULL)
    {
        *scl_state = HAL_GPIO_ReadPin(scl_port, scl_pin);
    }
    if(sda_state != NULL)
    {
        *sda_state = HAL_GPIO_ReadPin(sda_port, sda_pin);
    }
}

#if (XE_NP_APP_BENCH_I2C_RECOVERY_ON_PING_FAIL != 0U)
static void XE_NP_App_EnableGpioClock(GPIO_TypeDef *gpio)
{
    if(gpio == GPIOA)
    {
        __HAL_RCC_GPIOA_CLK_ENABLE();
    }
    else if(gpio == GPIOB)
    {
        __HAL_RCC_GPIOB_CLK_ENABLE();
    }
    else if(gpio == GPIOC)
    {
        __HAL_RCC_GPIOC_CLK_ENABLE();
    }
    else if(gpio == GPIOD)
    {
        __HAL_RCC_GPIOD_CLK_ENABLE();
    }
    else if(gpio == GPIOE)
    {
        __HAL_RCC_GPIOE_CLK_ENABLE();
    }
    else if(gpio == GPIOF)
    {
        __HAL_RCC_GPIOF_CLK_ENABLE();
    }
    else if(gpio == GPIOG)
    {
        __HAL_RCC_GPIOG_CLK_ENABLE();
    }
}

static void XE_NP_App_ConfigI2cRecoveryPin(GPIO_TypeDef *gpio, uint16_t pin)
{
    GPIO_InitTypeDef GPIO_InitStruct = {0};

    XE_NP_App_EnableGpioClock(gpio);
    HAL_GPIO_WritePin(gpio, pin, GPIO_PIN_SET);
    GPIO_InitStruct.Pin = pin;
    GPIO_InitStruct.Mode = GPIO_MODE_OUTPUT_OD;
    GPIO_InitStruct.Pull = GPIO_PULLUP;
    GPIO_InitStruct.Speed = GPIO_SPEED_FREQ_LOW;
    HAL_GPIO_Init(gpio, &GPIO_InitStruct);
}

static void XE_NP_App_ClearProbeI2cBus(uint8_t probe_index)
{
    GPIO_TypeDef *scl_port;
    GPIO_TypeDef *sda_port;
    uint16_t scl_pin;
    uint16_t sda_pin;

    XE_NP_App_GetProbeI2cPins(
        probe_index,
        &scl_port,
        &scl_pin,
        &sda_port,
        &sda_pin);

    XE_NP_App_ConfigI2cRecoveryPin(scl_port, scl_pin);
    XE_NP_App_ConfigI2cRecoveryPin(sda_port, sda_pin);
    HAL_Delay(XE_NP_APP_I2C_RECOVERY_DELAY_MS);

    for(uint8_t pulse = 0U; pulse < XE_NP_APP_I2C_RECOVERY_PULSES; ++pulse)
    {
        if(HAL_GPIO_ReadPin(sda_port, sda_pin) == GPIO_PIN_SET)
        {
            break;
        }
        HAL_GPIO_WritePin(scl_port, scl_pin, GPIO_PIN_RESET);
        HAL_Delay(XE_NP_APP_I2C_RECOVERY_DELAY_MS);
        HAL_GPIO_WritePin(scl_port, scl_pin, GPIO_PIN_SET);
        HAL_Delay(XE_NP_APP_I2C_RECOVERY_DELAY_MS);
    }

    HAL_GPIO_WritePin(sda_port, sda_pin, GPIO_PIN_RESET);
    HAL_Delay(XE_NP_APP_I2C_RECOVERY_DELAY_MS);
    HAL_GPIO_WritePin(scl_port, scl_pin, GPIO_PIN_SET);
    HAL_Delay(XE_NP_APP_I2C_RECOVERY_DELAY_MS);
    HAL_GPIO_WritePin(sda_port, sda_pin, GPIO_PIN_SET);
    HAL_Delay(XE_NP_APP_I2C_RECOVERY_DELAY_MS);
}

static uint8_t XE_NP_App_ForceResetI2cPeripheral(I2C_HandleTypeDef *hi2c)
{
    if((hi2c == NULL) || (hi2c->Instance == NULL))
    {
        return 0U;
    }

    if(hi2c->Instance == I2C2)
    {
        __HAL_RCC_I2C2_FORCE_RESET();
        __DSB();
        __HAL_RCC_I2C2_RELEASE_RESET();
    }
    else if(hi2c->Instance == I2C3)
    {
        __HAL_RCC_I2C3_FORCE_RESET();
        __DSB();
        __HAL_RCC_I2C3_RELEASE_RESET();
    }
    else if(hi2c->Instance == I2C4)
    {
        __HAL_RCC_I2C4_FORCE_RESET();
        __DSB();
        __HAL_RCC_I2C4_RELEASE_RESET();
    }
    else
    {
        return 0U;
    }

    return 1U;
}

static HAL_StatusTypeDef XE_NP_App_ReinitI2cPeripheral(I2C_HandleTypeDef *hi2c)
{
    if((hi2c == NULL) || (hi2c->Instance == NULL))
    {
        return HAL_ERROR;
    }

    if(HAL_I2C_Init(hi2c) != HAL_OK)
    {
        return HAL_ERROR;
    }

    if(HAL_I2CEx_ConfigAnalogFilter(hi2c, I2C_ANALOGFILTER_ENABLE) != HAL_OK)
    {
        return HAL_ERROR;
    }

    return HAL_I2CEx_ConfigDigitalFilter(hi2c, 0);
}

static HAL_StatusTypeDef XE_NP_App_RecoverProbeI2cBus(
    I2C_HandleTypeDef *hi2c,
    uint8_t probe_index)
{
    if((hi2c == NULL) || (hi2c->Instance == NULL))
    {
        return HAL_ERROR;
    }

    (void)HAL_I2C_DeInit(hi2c);
    XE_NP_App_ClearProbeI2cBus(probe_index);

    if(XE_NP_App_ForceResetI2cPeripheral(hi2c) == 0U)
    {
        return HAL_ERROR;
    }

    return XE_NP_App_ReinitI2cPeripheral(hi2c);
}
#endif

#if (XE_NP_APP_BENCH_I2C_SCAN_ON_PING_FAIL != 0U)
static void XE_NP_App_I2cScanSetBit(uint32_t bitmap[4], uint8_t addr)
{
    bitmap[(addr >> 5U) & 0x03U] |= (1UL << (addr & 0x1FU));
}

static void XE_NP_App_RunProbeI2cScan(I2C_HandleTypeDef *hi2c, uint8_t probe_index)
{
    XE_NP_AppI2cScanDiag diag;

    if(probe_index >= XE_NP_NEUROPIXELS_MAX_PROBES)
    {
        return;
    }

    memset(&diag, 0, sizeof(diag));
    diag.magic = XE_NP_APP_BENCH_I2C_SCAN_MAGIC;
    diag.version = XE_NP_APP_BENCH_I2C_SCAN_VERSION;
    diag.tick_ms = HAL_GetTick();
    diag.probe_index = probe_index;
    diag.target_dev_addr = XE_NP_NRIC_I2C_ADDR;
    diag.first_ok_addr = 0xFFU;
    diag.first_error_addr = 0xFFU;
    diag.first_busy_addr = 0xFFU;
    diag.first_timeout_addr = 0xFFU;

    if(hi2c == NULL)
    {
        xe_np_i2c_scan_diag[probe_index] = diag;
        XE_NP_App_DCacheClean(
            (const void *)&xe_np_i2c_scan_diag[probe_index],
            (uint32_t)sizeof(xe_np_i2c_scan_diag[probe_index]));
        return;
    }

    for(uint8_t addr = 0x03U; addr <= 0x77U; ++addr)
    {
        HAL_StatusTypeDef status = HAL_I2C_IsDeviceReady(
            hi2c,
            (uint16_t)(addr << 1U),
            1U,
            XE_NP_APP_BENCH_I2C_SCAN_TIMEOUT_MS);

        diag.last_addr = addr;
        diag.last_status = (uint32_t)status;
        diag.last_error_code = hi2c->ErrorCode;
        diag.last_isr = (hi2c->Instance != NULL) ? hi2c->Instance->ISR : 0U;
        diag.last_cr1 = (hi2c->Instance != NULL) ? hi2c->Instance->CR1 : 0U;
        diag.last_cr2 = (hi2c->Instance != NULL) ? hi2c->Instance->CR2 : 0U;

        if(status == HAL_OK)
        {
            XE_NP_App_I2cScanSetBit(diag.ack_bitmap, addr);
            if(diag.ok_count == 0U)
            {
                diag.first_ok_addr = addr;
            }
            ++diag.ok_count;
        }
        else if(status == HAL_BUSY)
        {
            XE_NP_App_I2cScanSetBit(diag.busy_bitmap, addr);
            if(diag.busy_count == 0U)
            {
                diag.first_busy_addr = addr;
            }
            ++diag.busy_count;
        }
        else if(status == HAL_TIMEOUT)
        {
            XE_NP_App_I2cScanSetBit(diag.timeout_bitmap, addr);
            if(diag.timeout_count == 0U)
            {
                diag.first_timeout_addr = addr;
            }
            ++diag.timeout_count;
        }
        else
        {
            XE_NP_App_I2cScanSetBit(diag.error_bitmap, addr);
            if(diag.error_count == 0U)
            {
                diag.first_error_addr = addr;
            }
            ++diag.error_count;
        }
    }

    xe_np_i2c_scan_diag[probe_index] = diag;
    /* Keep this diagnostic visible to an SWD memory read while D-cache is on. */
    XE_NP_App_DCacheClean(
        (const void *)&xe_np_i2c_scan_diag[probe_index],
        (uint32_t)sizeof(xe_np_i2c_scan_diag[probe_index]));
}
#endif

static void XE_NP_App_UpdateProbeDiag(
    const XE_NP_Runtime *runtime,
    const I2C_HandleTypeDef *hi2c,
    uint8_t stage,
    uint8_t probe_index,
    uint8_t failure,
    HAL_StatusTypeDef hal_status)
{
    uint8_t state_flags = 0U;
    uint8_t rail_flags = 0U;
    GPIO_PinState scl_state = GPIO_PIN_RESET;
    GPIO_PinState sda_state = GPIO_PIN_RESET;
    uint8_t diag_probe_index = XE_NP_App_DiagProbeIndex(runtime, probe_index);

    xe_np_probe_diag.tick_ms = HAL_GetTick();
    xe_np_probe_diag.i2c_error_code = (hi2c != NULL) ? hi2c->ErrorCode : 0U;
    xe_np_probe_diag.i2c_isr =
        ((hi2c != NULL) && (hi2c->Instance != NULL)) ? hi2c->Instance->ISR : 0U;
    xe_np_probe_diag.i2c_cr1 =
        ((hi2c != NULL) && (hi2c->Instance != NULL)) ? hi2c->Instance->CR1 : 0U;
    xe_np_probe_diag.i2c_cr2 =
        ((hi2c != NULL) && (hi2c->Instance != NULL)) ? hi2c->Instance->CR2 : 0U;
    xe_np_probe_diag.gpioc_idr = GPIOC->IDR;
    xe_np_probe_diag.version = 1U;
    xe_np_probe_diag.stage = stage;
    xe_np_probe_diag.probe_index = diag_probe_index;
    xe_np_probe_diag.failure = failure;
    xe_np_probe_diag.hal_status = (uint8_t)hal_status;

    if(xe_np_probe_state.initialized != 0U)
    {
        state_flags |= XE_NP_APP_PROBE_DIAG_STATE_INITIALIZED;
    }
    if(xe_np_probe_state.init_failed != 0U)
    {
        state_flags |= XE_NP_APP_PROBE_DIAG_STATE_INIT_FAILED;
    }
    if(xe_np_probe_state.init_error_reported != 0U)
    {
        state_flags |= XE_NP_APP_PROBE_DIAG_STATE_INIT_ERROR_REPORTED;
    }
    if(xe_np_probe_state.i2c_ready != 0U)
    {
        state_flags |= XE_NP_APP_PROBE_DIAG_STATE_I2C_READY;
    }
    if(xe_np_probe_state.base_configured != 0U)
    {
        state_flags |= XE_NP_APP_PROBE_DIAG_STATE_BASE_CONFIGURED;
    }
    if((runtime != NULL) &&
       ((runtime->flags & XE_NP_RUNTIME_FLAG_NP_POWER_RAILS_ENABLED) != 0U))
    {
        state_flags |= XE_NP_APP_PROBE_DIAG_STATE_POWER_RAILS_ENABLED;
    }
    if((runtime != NULL) &&
       ((runtime->flags & XE_NP_RUNTIME_FLAG_NP_GLOBAL_RESET_RELEASED) != 0U))
    {
        state_flags |= XE_NP_APP_PROBE_DIAG_STATE_GLOBAL_RESET_RELEASED;
    }
    if((runtime != NULL) &&
       ((runtime->flags & XE_NP_RUNTIME_FLAG_NP_RECORD_RESET_RELEASED) != 0U))
    {
        state_flags |= XE_NP_APP_PROBE_DIAG_STATE_RECORD_RESET_RELEASED;
    }

    if(HAL_GPIO_ReadPin(VEE_EN_GPIO_Port, VEE_EN_Pin) == GPIO_PIN_SET)
    {
        rail_flags |= XE_NP_APP_PROBE_DIAG_RAIL_VEE_EN;
    }
    if(HAL_GPIO_ReadPin(VCC_EN_GPIO_Port, VCC_EN_Pin) == GPIO_PIN_SET)
    {
        rail_flags |= XE_NP_APP_PROBE_DIAG_RAIL_VCC_EN;
    }
    if(HAL_GPIO_ReadPin(NP_NRST_GPIO_Port, NP_NRST_Pin) == GPIO_PIN_SET)
    {
        rail_flags |= XE_NP_APP_PROBE_DIAG_RAIL_NP_NRST;
    }
    if(HAL_GPIO_ReadPin(NP_REC_NRST_GPIO_Port, NP_REC_NRST_Pin) == GPIO_PIN_SET)
    {
        rail_flags |= XE_NP_APP_PROBE_DIAG_RAIL_NP_REC_NRST;
    }
    if(HAL_GPIO_ReadPin(SD_VSEL_GPIO_Port, SD_VSEL_Pin) == GPIO_PIN_SET)
    {
        rail_flags |= XE_NP_APP_PROBE_DIAG_RAIL_SD_VSEL;
    }
    XE_NP_App_ReadProbeI2cPins(diag_probe_index, &scl_state, &sda_state);
    if(scl_state == GPIO_PIN_SET)
    {
        rail_flags |= XE_NP_APP_PROBE_DIAG_RAIL_SCL_HIGH;
    }
    if(sda_state == GPIO_PIN_SET)
    {
        rail_flags |= XE_NP_APP_PROBE_DIAG_RAIL_SDA_HIGH;
    }

    xe_np_probe_diag.state_flags = state_flags;
    xe_np_probe_diag.rail_flags = rail_flags;
    xe_np_probe_diag.active_probe_mask =
        (runtime != NULL) ? (uint8_t)(runtime->active_np_probe_mask & XE_NP_CAPTURE_ALL_PROBES_MASK) : 0U;
}

static void XE_NP_App_RefreshProbeConfigExports(void)
{
    XE_NP_App_RefreshProbeStatus(&xe_np_runtime);
    XE_NP_App_RefreshCaptureDiag(&xe_np_runtime.sys_param);
}

static uint32_t xe_np_ble_uart_beacon_next_tick_ms = 0U;

static void XE_NP_App_InitLegacyBleTransport(XE_NP_Runtime *runtime)
{
    static const char xe_np_ble_name[] = XE_NP_APP_LEGACY_BLE_NAME;

    if((runtime == NULL) || (runtime->huart_ble == NULL) || (runtime->huart_ble->Instance == NULL))
    {
        return;
    }

    memset(&BLE_handle, 0, sizeof(BLE_handle));
    BLE_handle.baud = 115200U;
    BLE_handle.parity = HJ580_PARITY_NA;
    BLE_handle.stop_bit = HJ580_STOPBIT_1;
    memcpy(BLE_handle.DeviceName, xe_np_ble_name, sizeof(xe_np_ble_name));

    CE32_INTERCOM_Init((CE32_INTERCOM_Handle *)&BLE_handle, runtime->huart_ble);
    CE32_HJ580_COMMAND_Init(&BLE_handle.Config_CMD_RX);
    CE32_HJ580_COMMAND_Init(&BLE_handle.Config_CMD_TX);
    CE32_INTERCOM_UART_TX_DMA_Init((CE32_INTERCOM_Handle *)&BLE_handle);
    INTERCOM_UART_ENABLE(((CE32_INTERCOM_Handle *)&BLE_handle));
    INTERCOM_UART_RXIT_ENABLE(((CE32_INTERCOM_Handle *)&BLE_handle));
    HAL_NVIC_SetPriority(USART1_IRQn, XE_NP_APP_BLE_IRQ_PREEMPT_PRIO, XE_NP_APP_BLE_IRQ_SUBPRIO);
    HAL_NVIC_ClearPendingIRQ(USART1_IRQn);
    HAL_NVIC_EnableIRQ(USART1_IRQn);
    CE32_HJ580_Start(&BLE_handle);

#if (XE_NP_APP_BLE_BOOT_RENAME_ENABLE != 0U)
    HAL_Delay(250U);
    (void)CE32_HJ185_ConfigureNameMinimal(&BLE_handle, xe_np_ble_name);
#endif

#if (XE_NP_APP_BLE_BOOT_CONFIG_ENABLE != 0U)
    if(CE32_HJ_Init_Slave(&BLE_handle) != 0)
    {
        CE32_ReportError(&runtime->sys_param, CE32_ERRORID_CORE_ERROR);
    }
#endif
    CE32_HJ_Upload_State(&BLE_handle);
    xe_np_ble_uart_beacon_next_tick_ms = HAL_GetTick() + XE_NP_APP_BLE_UART_BEACON_PERIOD_MS;
}

static void XE_NP_App_ServiceLegacyBleBeacon(void)
{
#if (XE_NP_APP_BLE_UART_BEACON_ENABLE != 0U)
    static const uint8_t beacon[] = "XENP_BEACON\r\n";
    UART_HandleTypeDef *huart = BLE_handle.COMM.huart;
    uint32_t now;

    if((huart == NULL) || (huart->Instance == NULL))
    {
        return;
    }
    if((BLE_handle.state & HJ580_STATE_CONFIG) != 0U)
    {
        return;
    }

    now = HAL_GetTick();
    if((xe_np_ble_uart_beacon_next_tick_ms != 0U) &&
       ((int32_t)(now - xe_np_ble_uart_beacon_next_tick_ms) < 0))
    {
        return;
    }
    if(HAL_UART_GetState(huart) != HAL_UART_STATE_READY)
    {
        return;
    }

    HAL_GPIO_WritePin(HJ_WAKE_GPIO_Port, HJ_WAKE_Pin, GPIO_PIN_RESET);
    HAL_GPIO_WritePin(HJ_MODE_GPIO_Port, HJ_MODE_Pin, GPIO_PIN_RESET);
    (void)HAL_UART_Transmit(huart, (uint8_t *)beacon, sizeof(beacon) - 1U, 50U);
    xe_np_ble_uart_beacon_next_tick_ms = now + XE_NP_APP_BLE_UART_BEACON_PERIOD_MS;
#else
    return;
#endif
}

static void XE_NP_App_InitLegacyImu(XE_NP_Runtime *runtime)
{
    if((runtime == NULL) || (runtime->hi2c_imu == NULL) || (runtime->hi2c_imu->Instance == NULL))
    {
        return;
    }

    if(CE32_IMU_BMX160_init(
           &ACC_handle,
           runtime->hi2c_imu,
           NULL,
           IMU_SDA_GPIO_Port,
           IMU_SDA_Pin,
           IMU_SCL_GPIO_Port,
           IMU_SCL_Pin,
           PMODE_Lowpower) != 0)
    {
        CE32_ReportError(&runtime->sys_param, CE32_ERRORID_ACC_ERR);
        return;
    }

    (void)CE32_IMU_BMX160_startScan(&ACC_handle, ACC_buf_raw, sizeof(ACC_buf_raw));
}

static void XE_NP_App_InitLegacyAdc(XE_NP_Runtime *runtime)
{
    uint32_t sample_rate_hz;

    if((runtime == NULL) || (runtime->hadc_batt == NULL) || (runtime->hadc_batt->Instance == NULL))
    {
        return;
    }

    sample_rate_hz = runtime->sys_param.sampling_rate[SYS_ID_SIG_ADC];
    CE32_SystemCompat_InitLegacyAdc(runtime->hadc_batt, sample_rate_hz);
}

static void XE_NP_App_ServiceLegacyImu(XE_NP_Runtime *runtime)
{
    if((runtime == NULL) || (runtime->hi2c_imu == NULL) || (runtime->hi2c_imu->Instance == NULL) ||
       (ACC_handle.hi2c == NULL) || (ACC_handle.state != CE32_BMX160_STATE_READY))
    {
        return;
    }

    if(CE32_IMU_BMX160_startScan(&ACC_handle, ACC_buf_raw, sizeof(ACC_buf_raw)) == 0)
    {
        xe_np_imu_fail_count = 0U;
        return;
    }

    if(xe_np_imu_fail_count < UINT8_MAX)
    {
        ++xe_np_imu_fail_count;
    }

    if(xe_np_imu_fail_count <= 5U)
    {
        return;
    }

    xe_np_imu_fail_count = 0U;
    if(CE32_IMU_BMX160_init(
           &ACC_handle,
           runtime->hi2c_imu,
           NULL,
           IMU_SDA_GPIO_Port,
           IMU_SDA_Pin,
           IMU_SCL_GPIO_Port,
           IMU_SCL_Pin,
           ACC_handle.PMode) != 0)
    {
        CE32_ReportError(&runtime->sys_param, CE32_ERRORID_ACC_ERR);
        return;
    }

    (void)CE32_IMU_BMX160_startScan(&ACC_handle, ACC_buf_raw, sizeof(ACC_buf_raw));
}

void XE_NP_App_ResetProbeConfigs(void)
{
    if(XE_NP_App_NpSessionActive(&xe_np_runtime) != 0)
    {
        CE32_ReportError(&xe_np_runtime.sys_param, CE32_ERRORID_PARAM_ERR);
        return;
    }

    xe_np_probe_config_store.initialized = 0U;
    XE_NP_App_EnsureProbeConfigsInitialized();
    XE_NP_App_RefreshProbeConfigExports();
}

int XE_NP_App_SetProbeShankConfig(
    uint8_t probe_index,
    const uint8_t *chain1,
    uint16_t chain1_bytes)
{
    uint8_t mask;

    if(probe_index >= XE_NP_NEUROPIXELS_MAX_PROBES)
    {
        return -1;
    }
    if(((chain1 == NULL) && (chain1_bytes != 0U)) ||
       ((chain1 != NULL) && (chain1_bytes == 0U)))
    {
        return -1;
    }
    if(XE_NP_App_NpSessionActive(&xe_np_runtime) != 0)
    {
        CE32_ReportError(&xe_np_runtime.sys_param, CE32_ERRORID_PARAM_ERR);
        return -1;
    }

    XE_NP_App_EnsureProbeConfigsInitialized();
    if(chain1_bytes!=121U || chain1==NULL)return -1;
    xe_np_probe_config_store.probe_config[probe_index].chain1=chain1;
    xe_np_probe_config_store.probe_config[probe_index].chain1_bytes=chain1_bytes;
    mask = (uint8_t)(1U << probe_index);

    if((chain1 != NULL) && (chain1_bytes != 0U))
    {
        xe_np_probe_config_store.state.shank_configured_mask |= mask;
    }
    else
    {
        xe_np_probe_config_store.state.shank_configured_mask &= (uint8_t)(~mask);
    }

    XE_NP_App_RefreshProbeConfigExports();
    return 0;
}

int XE_NP_App_SetProbeBaseConfig(
    uint8_t probe_index,
    const uint8_t *chain2,
    uint16_t chain2_bytes,
    const uint8_t *chain3,
    uint16_t chain3_bytes,
    int includes_calibration)
{
    XE_NP_NricConfig defaults;
    XE_NP_NricConfig *config;
    uint8_t mask;
    uint8_t override_active;

    if(probe_index >= XE_NP_NEUROPIXELS_MAX_PROBES)
    {
        return -1;
    }
    if(((chain2 == NULL) && (chain2_bytes != 0U)) ||
       ((chain2 != NULL) && (chain2_bytes == 0U)) ||
       ((chain3 == NULL) && (chain3_bytes != 0U)) ||
       ((chain3 != NULL) && (chain3_bytes == 0U)))
    {
        return -1;
    }
    if(XE_NP_App_NpSessionActive(&xe_np_runtime) != 0)
    {
        CE32_ReportError(&xe_np_runtime.sys_param, CE32_ERRORID_PARAM_ERR);
        return -1;
    }

    XE_NP_App_EnsureProbeConfigsInitialized();
    XE_NP_NricConfig_InitDefaults(&defaults);
    config = &xe_np_probe_config_store.probe_config[probe_index];
#if XE_NP_NRIC_USE_SR_DEFAULT_STARTUP
    config->chain2 = defaults.chain2;
    config->chain2_bytes = defaults.chain2_bytes;
    config->chain3 = defaults.chain3;
    config->chain3_bytes = defaults.chain3_bytes;
    (void)chain2;
    (void)chain2_bytes;
    (void)chain3;
    (void)chain3_bytes;
#else
    config->chain2 = (chain2 != NULL) ? chain2 : defaults.chain2;
    config->chain2_bytes = (chain2 != NULL) ? chain2_bytes : defaults.chain2_bytes;
    config->chain3 = (chain3 != NULL) ? chain3 : defaults.chain3;
    config->chain3_bytes = (chain3 != NULL) ? chain3_bytes : defaults.chain3_bytes;
#endif

    mask = (uint8_t)(1U << probe_index);
#if XE_NP_NRIC_USE_SR_DEFAULT_STARTUP
    override_active = 0U;
    (void)includes_calibration;
#else
    override_active = (uint8_t)(((chain2 != NULL) || (chain3 != NULL)) ? 1U : 0U);
#endif
    if(override_active != 0U)
    {
        xe_np_probe_config_store.state.base_override_mask |= mask;
    }
    else
    {
        xe_np_probe_config_store.state.base_override_mask &= (uint8_t)(~mask);
    }

    if((override_active != 0U) && (includes_calibration != 0))
    {
        xe_np_probe_config_store.state.calibration_override_mask |= mask;
    }
    else
    {
        xe_np_probe_config_store.state.calibration_override_mask &= (uint8_t)(~mask);
    }

    XE_NP_App_RefreshProbeConfigExports();
    return 0;
}

int XE_NP_App_SetProbeSyncValue(uint8_t probe_index, uint8_t sync_value)
{
    if(probe_index >= XE_NP_NEUROPIXELS_MAX_PROBES)
    {
        return -1;
    }
    if(XE_NP_App_NpSessionActive(&xe_np_runtime) != 0)
    {
        CE32_ReportError(&xe_np_runtime.sys_param, CE32_ERRORID_PARAM_ERR);
        return -1;
    }

    XE_NP_App_EnsureProbeConfigsInitialized();
    xe_np_probe_config_store.probe_config[probe_index].sync_value = sync_value;
    XE_NP_PsbValidator_SetExpectedSyncValue(
        &xe_np_runtime.capture.validators[probe_index],
        sync_value);
    XE_NP_App_RefreshProbeConfigExports();
    return 0;
}

XE_NP_AppProbeConfigState XE_NP_App_GetProbeConfigState(void)
{
    XE_NP_App_EnsureProbeConfigsInitialized();
    return xe_np_probe_config_store.state;
}

static int XE_NP_App_InIsrContext(void)
{
    return (__get_IPSR() != 0U) ? 1 : 0;
}

static void XE_NP_App_PostPendingError(unsigned long error_code)
{
    uint32_t primask = XE_NP_App_CriticalSection_Enter();
    xe_np_pending_error_code |= (uint32_t)error_code;
    XE_NP_App_CriticalSection_Exit(primask);
}

static void XE_NP_App_DrainPendingErrors(CE32_systemParam *sys_param)
{
    uint32_t pending;
    uint32_t primask;

    if(sys_param == NULL)
    {
        return;
    }

    primask = XE_NP_App_CriticalSection_Enter();
    pending = xe_np_pending_error_code;
    xe_np_pending_error_code = 0U;
    XE_NP_App_CriticalSection_Exit(primask);

    if(pending != 0U)
    {
        CE32_ReportError(sys_param, pending);
    }
}

static uint16_t XE_NP_App_GetRecorderBacklogBlocks(const XE_NP_CapturePort *capture_port)
{
    uint32_t backlog_bytes;

    if((capture_port == NULL) || (capture_port->storage == NULL) || (capture_port->storage_bytes == 0U))
    {
        return 0U;
    }

    backlog_bytes = dataMGR_BytesAvailable((dataMGR *)&capture_port->raw_mgr, 0U);
    return (uint16_t)(backlog_bytes / 512U);
}

static uint16_t XE_NP_App_GetLegacyFsSpeedCentiMBs(void)
{
    float speed_mbs;

    if((XE_NP_LegacyFs_IsActive() == 0) || (fs.enabled == 0U))
    {
        return 0U;
    }

    speed_mbs = fs.speed_MBs;
    if(speed_mbs <= 0.0f)
    {
        return 0U;
    }
    if(speed_mbs >= 655.35f)
    {
        return 65535U;
    }

    return (uint16_t)(speed_mbs * 100.0f);
}

static uint16_t XE_NP_App_EncodeRecorderFailDiag(uint32_t fail_cnt)
{
    uint16_t encoded;
    uint8_t bad_samples;

    encoded = (uint16_t)(fail_cnt & XE_NP_APP_CAPTURE_DIAG_RECORDER_FAIL_MASK);
    bad_samples =
        (xe_np_recorder_rate_bad_samples > 3U) ? 3U : xe_np_recorder_rate_bad_samples;
    encoded |= (uint16_t)((uint16_t)(bad_samples & 0x03U)
                          << XE_NP_APP_CAPTURE_DIAG_RECORDER_RATE_BAD_SHIFT);
    if(xe_np_recorder_rate_fault_seen != 0U)
    {
        encoded |= XE_NP_APP_CAPTURE_DIAG_RECORDER_RATE_FAULT_MASK;
    }

    return encoded;
}

static uint16_t XE_NP_App_EncodeDiagLogState(uint32_t raw_log_state, uint8_t rate_ratio_pct2)
{
    uint16_t encoded;

    encoded = (uint16_t)(raw_log_state & XE_NP_APP_CAPTURE_DIAG_LOG_STATE_MASK);
    encoded |= (uint16_t)(((uint16_t)(rate_ratio_pct2 & 0x3FU))
                          << XE_NP_APP_CAPTURE_DIAG_RATE_RATIO_SHIFT);
    return encoded;
}

static uint8_t XE_NP_App_EncodeSdModeDiag(uint32_t speed_mode)
{
    switch(speed_mode)
    {
        case SDMMC_SPEED_MODE_ULTRA_SDR104:
            return XE_NP_APP_CAPTURE_DIAG_SD_MODE_SDR104;
        case SDMMC_SPEED_MODE_ULTRA_SDR50:
            return XE_NP_APP_CAPTURE_DIAG_SD_MODE_SDR50;
        case SDMMC_SPEED_MODE_HIGH:
            return XE_NP_APP_CAPTURE_DIAG_SD_MODE_HIGH;
        case SDMMC_SPEED_MODE_DEFAULT:
        default:
            return XE_NP_APP_CAPTURE_DIAG_SD_MODE_DEFAULT;
    }
}

static uint16_t XE_NP_App_EncodeRecorderBacklogDiag(uint16_t backlog_blocks)
{
    uint16_t encoded;
    uint8_t sd_mode_code;

    encoded = (uint16_t)(backlog_blocks & XE_NP_APP_CAPTURE_DIAG_BACKLOG_BLOCKS_MASK);
    sd_mode_code = XE_NP_App_EncodeSdModeDiag(XE_NP_Board_SdNegotiatedSpeedMode());
    encoded |= (uint16_t)(((uint16_t)(sd_mode_code & 0x03U))
                          << XE_NP_APP_CAPTURE_DIAG_SD_MODE_SHIFT);
    if((xe_np_runtime.flags & XE_NP_RUNTIME_FLAG_SD_UHS_READY) != 0U)
    {
        encoded |= XE_NP_APP_CAPTURE_DIAG_SD_TRANSPORT_READY_MASK;
    }

    return encoded;
}

static void XE_NP_App_RefreshCaptureDiag(CE32_systemParam *sys_param)
{
    XE_NP_AppCaptureDiagWire diag;
    uint8_t gpioe_rate_bad_samples;
    uint8_t gpiod_rate_bad_samples;
    uint8_t active_probe_mask;
    uint8_t calibration_ready;

    if(sys_param == NULL)
    {
        return;
    }

    memset(&diag, 0, sizeof(diag));
    diag.version = 10U;
    diag.selftest_probe_mask = xe_np_capture_selftest_probe_mask;
    diag.live_decode_probe_mask = xe_np_capture_live_decode_probe_mask;
    diag.pending_peak_packed =
        (uint8_t)((xe_np_dma_gpioe.pending_peak_depth & 0x0FU) |
                  ((xe_np_dma_gpiod.pending_peak_depth & 0x0FU) << 4U));
    diag.gpioe_alignment_ticks = xe_np_runtime.capture.gpioe.frame_alignment_ticks;
    diag.gpiod_alignment_ticks = xe_np_runtime.capture.gpiod.frame_alignment_ticks;
    diag.gpioe_log_state = XE_NP_App_EncodeDiagLogState(
        xe_np_runtime.capture.gpioe.raw_mgr.logState,
        xe_np_dma_gpioe.rate_ratio_pct2);
    diag.gpiod_log_state = XE_NP_App_EncodeDiagLogState(
        xe_np_runtime.capture.gpiod.raw_mgr.logState,
        xe_np_dma_gpiod.rate_ratio_pct2);
#if (XE_NP_APP_CAPTURE_FOREGROUND_QUEUE_ENABLE != 0U)
    diag.gpioe_live_decode_ok_low = (uint16_t)(xe_np_dma_gpioe.live_decode_ok_count & 0xFFFFU);
    diag.gpiod_live_decode_ok_low = (uint16_t)(xe_np_dma_gpiod.live_decode_ok_count & 0xFFFFU);
    diag.gpioe_live_decode_fail_low = (uint16_t)(xe_np_dma_gpioe.live_decode_fail_count & 0xFFFFU);
    diag.gpiod_live_decode_fail_low = (uint16_t)(xe_np_dma_gpiod.live_decode_fail_count & 0xFFFFU);
#elif (XE_NP_APP_CAPTURE_SYNC_WATCHDOG_ENABLE != 0U)
    /*
     * In the raw-only build the live-decode queue is compiled out. Reuse the
     * same wire slots for the lightweight PSB sync watchdog so diagnostics
     * still prove that the DMA stream is staying frame-aligned.
     */
    diag.gpioe_live_decode_ok_low = (uint16_t)(xe_np_dma_gpioe.sync_watchdog_ok_count & 0xFFFFU);
    diag.gpiod_live_decode_ok_low = (uint16_t)(xe_np_dma_gpiod.sync_watchdog_ok_count & 0xFFFFU);
    diag.gpioe_live_decode_fail_low =
        (uint16_t)(xe_np_dma_gpioe.sync_watchdog_error_count & 0xFFFFU);
    diag.gpiod_live_decode_fail_low =
        (uint16_t)(xe_np_dma_gpiod.sync_watchdog_error_count & 0xFFFFU);
#endif
    diag.gpioe_windows_processed_low = XE_NP_App_EncodeRecorderBacklogDiag(
        XE_NP_App_GetRecorderBacklogBlocks(&xe_np_runtime.capture.gpioe));
    diag.gpiod_windows_processed_low =
        XE_NP_App_GetRecorderBacklogBlocks(&xe_np_runtime.capture.gpiod);
    diag.gpioe_pending_overflows_low = XE_NP_App_GetLegacyFsSpeedCentiMBs();
    diag.gpiod_pending_overflows_low = XE_NP_App_EncodeRecorderFailDiag(fs.fail_cnt);

    gpioe_rate_bad_samples =
        (xe_np_dma_gpioe.rate_bad_samples > 3U) ? 3U : xe_np_dma_gpioe.rate_bad_samples;
    gpiod_rate_bad_samples =
        (xe_np_dma_gpiod.rate_bad_samples > 3U) ? 3U : xe_np_dma_gpiod.rate_bad_samples;
    diag.live_decode_probe_mask |=
        (uint8_t)((gpioe_rate_bad_samples & 0x03U) << 4U);
    diag.live_decode_probe_mask |=
        (uint8_t)((gpiod_rate_bad_samples & 0x03U) << 6U);
    if(xe_np_dma_gpioe.rate_error_seen != 0U)
    {
        diag.selftest_probe_mask |= 0x10U;
    }
    if(xe_np_dma_gpiod.rate_error_seen != 0U)
    {
        diag.selftest_probe_mask |= 0x20U;
    }
    active_probe_mask =
        (uint8_t)(xe_np_runtime.active_np_probe_mask & XE_NP_CAPTURE_ALL_PROBES_MASK);
    calibration_ready = (XE_NP_App_ActiveProbeCalibrationReady(&xe_np_runtime) != 0) ? 1U : 0U;
    if(calibration_ready != 0U)
    {
        diag.selftest_probe_mask |= 0x40U;
    }
    else if(active_probe_mask != 0U)
    {
        diag.selftest_probe_mask |= 0x80U;
    }

    memcpy(sys_param->unassigned, &diag, sizeof(diag));
}

static void XE_NP_App_ApplySafeNpStartup(XE_NP_Runtime *runtime);
static void XE_NP_App_HandleCaptureRuntimeFault(XE_NP_Runtime *runtime);
static int XE_NP_App_LegacyFsRecordPathArmed(const XE_NP_Runtime *runtime);
static int XE_NP_App_RawSdSink_ForceStopDataPath(SD_HandleTypeDef *hsd);
static void XE_NP_App_RawSdSink_StopStageDma(XE_NP_AppRawSdSink *sink);
static void XE_NP_App_RawSdSink_ReportStorageFault(
    XE_NP_Runtime *runtime,
    XE_NP_AppRawSdSink *sink,
    unsigned long error_code);
static void XE_NP_App_ReportCaptureFault(
    XE_NP_AppDmaPort *port,
    unsigned long error_code,
    uint8_t validation_error,
    uint8_t dma_error);

static void XE_NP_App_RecomputeLiveDecodeProbeMask(void)
{
    xe_np_capture_live_decode_probe_mask =
        (uint8_t)(xe_np_dma_gpioe.live_decode_probe_mask | xe_np_dma_gpiod.live_decode_probe_mask);
}

static int XE_NP_App_GetPortProbeIds(
    const XE_NP_AppDmaPort *port,
    uint8_t *probe_ids,
    uint8_t *probe_count)
{
    uint8_t count = 0U;

    if((port == NULL) || (port->capture_port == NULL) ||
       (probe_ids == NULL) || (probe_count == NULL))
    {
        return -1;
    }

    if(port->port_index == 0U)
    {
        if((port->capture_port->active_probe_mask & XE_NP_CAPTURE_PROBE1_MASK) != 0U)
        {
            probe_ids[count++] = 0U;
        }
        if((port->capture_port->active_probe_mask & XE_NP_CAPTURE_PROBE2_MASK) != 0U)
        {
            probe_ids[count++] = 1U;
        }
    }
    else if(port->port_index == 1U)
    {
        if((port->capture_port->active_probe_mask & XE_NP_CAPTURE_PROBE3_MASK) != 0U)
        {
            probe_ids[count++] = 2U;
        }
    }
    else
    {
        return -1;
    }

    *probe_count = count;
    return (count != 0U) ? 0 : -1;
}

static void XE_NP_App_ResetPortSyncState(XE_NP_AppDmaPort *port)
{
    uint8_t probe_ids[2];
    uint8_t probe_count = 0U;

    if((port == NULL) || (port->capture_port == NULL))
    {
        return;
    }

    port->capture_port->frame_alignment_locked = 0U;
    port->capture_port->frame_alignment_ticks = 0U;
    port->capture_port->validated_probe_mask = 0U;
    port->live_decode_probe_mask = 0U;
    port->continuity_last_valid = 0U;
    XE_NP_App_RecomputeLiveDecodeProbeMask();

    if((port->capture != NULL) &&
       (XE_NP_App_GetPortProbeIds(port, probe_ids, &probe_count) == 0))
    {
        for(uint8_t idx = 0U; idx < probe_count; ++idx)
        {
            XE_NP_PsbValidator_Init(&port->capture->validators[probe_ids[idx]]);
        }
    }
}

static void XE_NP_App_ResetPortContinuityState(XE_NP_AppDmaPort *port)
{
    if(port == NULL)
    {
        return;
    }

    port->continuity_ok_count = 0U;
    port->continuity_error_count = 0U;
    port->continuity_last_expected_counter = 0U;
    port->continuity_last_observed_counter = 0U;
    port->continuity_last_frame_delta = 0U;
    port->continuity_last_abs_offset_bytes = 0ULL;
    port->continuity_last_counter = 0U;
    port->continuity_last_valid = 0U;
}

static int XE_NP_App_PostResetCaptureValidationAllowed(const XE_NP_AppDmaPort *port)
{
    if(xe_np_probe_state.initialized != 0U)
    {
#if (XE_NP_APP_CAPTURE_SYNC_WATCHDOG_RUNTIME_ENABLE != 0U)
        /*
         * In the raw-only USB bench path, the sync watchdog uses its own
         * completed-window tap and does not consume the export queue. Keep it
         * active during host streaming so PSB integrity is checked on the same
         * live session that the host is observing.
         */
        (void)port;
        return 1;
#else
        return 0;
#endif
    }

    return (xe_np_probe_state.bringup_stage == XE_NP_APP_PROBE_STAGE_WAIT_CAPTURE_LOCK) ? 1 : 0;
}

static void XE_NP_App_PreparePortForPostResetLock(XE_NP_AppDmaPort *port)
{
    uint32_t primask;

    if(port == NULL)
    {
        return;
    }

    primask = XE_NP_App_CriticalSection_Enter();
    port->pending_read_idx = port->pending_write_idx;
    port->backlog_resync_requested = 0U;
    port->sync_watchdog_pending = 0U;
    port->sync_watchdog_size_bytes = 0U;
    port->sync_watchdog_offset_bytes = 0U;
    port->sync_watchdog_total_bytes_end = 0ULL;
    XE_NP_App_CriticalSection_Exit(primask);

    port->pending_peak_depth = 0U;
    port->validation_error_seen = 0U;
    port->backlog_error_seen = 0U;
    port->usb_active_window_valid = 0U;
    port->usb_active_window_consumed_bytes = 0U;
    memset(&port->usb_active_window, 0, sizeof(port->usb_active_window));
    port->sync_watchdog_fail_count = 0U;
    port->sync_watchdog_ok_count = 0U;
    port->sync_watchdog_error_count = 0U;
    port->pending_overflow_count = 0U;
    port->windows_processed = 0U;
    port->live_decode_ok_count = 0U;
    port->live_decode_fail_count = 0U;
    port->last_live_decode_tick_ms = 0U;
    port->last_rate_sample_tick_ms = 0U;
    port->last_rate_sample_bytes = 0ULL;
    port->wrap_probe_tail_ready = 0U;
    port->wrap_probe_ready = 0U;
    port->wrap_probe_valid = 0U;
    port->wrap_probe_count = 0U;
    port->wrap_probe_tail_offset = 0U;
    port->wrap_probe_head_offset = 0U;
    port->wrap_probe_tail_total_bytes_end = 0ULL;
    port->wrap_probe_head_total_bytes_end = 0ULL;
    memset(port->wrap_probe_bytes, 0, sizeof(port->wrap_probe_bytes));
    XE_NP_App_ResetFirstBacklogResyncSnapshot(port);
    XE_NP_App_ResetPortContinuityState(port);
    port->recorder_backlog_high_since_tick_ms = 0U;
    port->recorder_backlog_fault_seen = 0U;
    port->rate_error_seen = 0U;
    port->rate_bad_samples = 0U;
    port->rate_ratio_pct2 = 0U;
    port->byte_sync_best_offset = 0U;
    port->byte_sync_best_sync0 = 0U;
    port->byte_sync_best_sync1 = 0U;
    port->byte_sync_best_flags0 = 0U;
    port->byte_sync_best_flags1 = 0U;
    port->byte_sync_best_clean_frames = 0U;
    port->byte_sync_best_sync_valid_frames = 0U;
    port->byte_sync_best_frame_skip = 0U;
    port->byte_sync_best_phase = 0U;
    port->byte_sync_sample_frames = 0U;
#if ((XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_ENABLE != 0U) && \
     (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_PUBLISH_ENABLE != 0U) && \
     (XE_NP_APP_EXPERIMENT_SYNC_WATCHDOG_ENABLE != 0U))
    xe_np_gpioe_interleaved_validation_last_pair_end = 0ULL;
    xe_np_gpioe_interleaved_validation_lane_b_offset = 0;
    xe_np_gpioe_interleaved_validation_lane_b_offset_valid = 0U;
    xe_np_gpioe_interleaved_usb_next_lane_byte = 0ULL;
    xe_np_gpioe_interleaved_usb_next_lane_byte_valid = 0U;
    xe_np_gpioe_interleaved_usb_next_lane_is_b = 0U;
    xe_np_gpioe_interleaved_usb_dma_batch_claimed = 0U;
    xe_np_gpioe_interleaved_usb_dma_batch_committed = 0U;
    xe_np_gpioe_interleaved_usb_dma_batch_lane_b_packets = 0U;
    xe_np_gpioe_interleaved_usb_dma_batch_payload_bytes = 0U;
    xe_np_gpioe_interleaved_usb_export_debug = 0U;
#endif
    XE_NP_App_ResetPortSyncState(port);
}

#if (XE_NP_APP_CAPTURE_SYNC_WATCHDOG_ENABLE != 0U)
static uint32_t XE_NP_App_RequiredSyncWatchdogOkCount(const XE_NP_AppDmaPort *port)
{
    (void)port;

    if((xe_np_probe_state.initialized == 0U) &&
       (xe_np_probe_state.bringup_stage == XE_NP_APP_PROBE_STAGE_WAIT_CAPTURE_LOCK))
    {
        return XE_NP_APP_SYNC_WATCHDOG_STARTUP_OK_WINDOWS;
    }

    return 1U;
}

static size_t XE_NP_App_SelectSyncWatchdogSampleFrames(const XE_NP_AppDmaPort *port)
{
    if((port == NULL) || (port->capture_port == NULL))
    {
        return XE_NP_APP_SYNC_WATCHDOG_SAMPLE_FRAMES_LOCKED;
    }

    if((xe_np_probe_state.initialized == 0U) &&
       (xe_np_probe_state.bringup_stage == XE_NP_APP_PROBE_STAGE_WAIT_CAPTURE_LOCK))
    {
        return XE_NP_APP_SYNC_WATCHDOG_SAMPLE_FRAMES_STARTUP;
    }

    return ((port->capture_port->frame_alignment_locked == 0U) ||
            (port->sync_watchdog_fail_count != 0U)) ?
               XE_NP_APP_SYNC_WATCHDOG_SAMPLE_FRAMES_UNLOCKED :
               XE_NP_APP_SYNC_WATCHDOG_SAMPLE_FRAMES_LOCKED;
}

static int XE_NP_App_ShouldSampleRuntimeSyncWatchdogWindow(
    const XE_NP_AppDmaPort *port,
    uint32_t size_bytes,
    uint64_t total_bytes_end)
{
    uint64_t completed_windows;

    if((port == NULL) || (port->capture_port == NULL) || (size_bytes == 0U))
    {
        return 1;
    }

    if((xe_np_probe_state.initialized == 0U) ||
       (port->capture_port->frame_alignment_locked == 0U) ||
       (port->sync_watchdog_fail_count != 0U))
    {
        return 1;
    }

    completed_windows = total_bytes_end / (uint64_t)size_bytes;
    return (((uint32_t)completed_windows &
             (XE_NP_APP_SYNC_WATCHDOG_RUNTIME_STRIDE_LOCKED - 1U)) == 0U) ?
               1 :
               0;
}

static void XE_NP_App_QueueSyncWatchdogWindowFromIsr(
    XE_NP_AppDmaPort *port,
    uint32_t offset_bytes,
    uint32_t region_bytes,
    uint64_t total_bytes_end)
{
    if((port == NULL) || (port->capture_port == NULL) || (region_bytes == 0U) ||
       ((offset_bytes + region_bytes) > XE_NP_App_GetCaptureStoragePhysicalBytes(port)))
    {
        XE_NP_App_ReportCaptureFault(port, CE32_ERRORID_CORE_ERROR, 0U, 1U);
        return;
    }

    port->sync_watchdog_offset_bytes = offset_bytes;
    port->sync_watchdog_size_bytes = region_bytes;
    port->sync_watchdog_total_bytes_end = total_bytes_end;
    __DMB();
    port->sync_watchdog_pending = 1U;
}

static int XE_NP_App_FindByteSyncWindow(
    XE_NP_AppDmaPort *port,
    const uint8_t *region,
    size_t region_bytes,
    size_t sample_frames,
    size_t *aligned_offset_bytes)
{
    XE_NP_PsbFrame frame;
    const size_t frame_bytes = XE_NP_CAPTURE_FRAME_BIT_COUNT;
    const size_t required_bytes = sample_frames * frame_bytes;
    size_t best_offset = 0U;
    size_t best_frame_skip = 0U;
    size_t best_phase = 0U;
    uint16_t best_sync0 = 0U;
    uint16_t best_sync1 = 0U;
    uint32_t best_flags0 = 0U;
    uint32_t best_flags1 = 0U;
    uint8_t best_clean_frames = 0U;
    uint8_t best_sync_valid_frames = 0U;

    if((port == NULL) || (port->capture_port == NULL) || (region == NULL) ||
       (aligned_offset_bytes == NULL) || (sample_frames == 0U) ||
       (region_bytes < frame_bytes))
    {
        return -1;
    }

    for(size_t frame_skip = 0U; frame_skip <= XE_NP_CAPTURE_FRAMES_PER_SUPERFRAME; ++frame_skip)
    {
        size_t frame_skip_bytes = frame_skip * frame_bytes;

        if(frame_skip_bytes >= region_bytes)
        {
            break;
        }

        for(size_t offset = 0U; offset < frame_bytes; ++offset)
        {
            size_t candidate = frame_skip_bytes + offset;
            uint16_t candidate_sync0 = 0U;
            uint16_t candidate_sync1 = 0U;
            uint32_t candidate_flags0 = 0U;
            uint32_t candidate_flags1 = 0U;
            uint8_t candidate_clean_frames = 0U;
            uint8_t candidate_sync_valid_frames = 0U;
            uint16_t candidate_fixed_word = 0U;
            uint32_t candidate_counter_value = 0U;
            uint8_t candidate_fixed_valid = 0U;

            if((candidate + required_bytes) > region_bytes)
            {
                break;
            }

            int candidate_ok = 1;
            for(size_t frame_index = 0U; frame_index < sample_frames; ++frame_index)
            {
                size_t frame_offset = candidate + (frame_index * frame_bytes);
                int decode_rc =
                    XE_NP_PsbFrame_Decode(&frame, &region[frame_offset], region_bytes - frame_offset);

                if(frame_index == 0U)
                {
                    candidate_sync0 = frame.sync_word;
                    candidate_flags0 = frame.flags;
                }
                else if(frame_index == 1U)
                {
                    candidate_sync1 = frame.sync_word;
                    candidate_flags1 = frame.flags;
                }

                if((frame.flags & XE_NP_PSB_FRAME_FLAG_SYNC_VALID) != 0U)
                {
                    candidate_sync_valid_frames++;
                }

                if((decode_rc != 0) ||
                   ((frame.flags & (XE_NP_PSB_FRAME_FLAG_HEARTBEAT_ERROR |
                                    XE_NP_PSB_FRAME_FLAG_FILL_ERROR)) != 0U))
                {
                    candidate_ok = 0;
                    break;
                }

                if(candidate_fixed_valid == 0U)
                {
                    candidate_fixed_word = frame.fixed_word;
                    candidate_counter_value = frame.counter_value;
                    candidate_fixed_valid = 1U;
                }
                else
                {
                    uint32_t expected_counter = (candidate_counter_value + 1U) & 0x000FFFFFUL;

                    if((frame.fixed_word != candidate_fixed_word) ||
                       (frame.counter_value != expected_counter))
                    {
                        candidate_ok = 0;
                        break;
                    }

                    candidate_counter_value = frame.counter_value;
                }

                candidate_clean_frames++;
            }

            if((candidate_clean_frames > best_clean_frames) ||
               ((candidate_clean_frames == best_clean_frames) &&
                (candidate_sync_valid_frames > best_sync_valid_frames)))
            {
                best_offset = candidate;
                best_frame_skip = frame_skip;
                best_phase = offset;
                best_sync0 = candidate_sync0;
                best_sync1 = candidate_sync1;
                best_flags0 = candidate_flags0;
                best_flags1 = candidate_flags1;
                best_clean_frames = candidate_clean_frames;
                best_sync_valid_frames = candidate_sync_valid_frames;
            }

            if(candidate_ok != 0)
            {
                *aligned_offset_bytes = candidate;
                port->capture_port->frame_alignment_ticks = (uint16_t)candidate;
                port->capture_port->frame_alignment_locked = 1U;
                port->capture_port->validated_probe_mask = port->capture_port->active_probe_mask;
                port->byte_sync_best_offset = (uint16_t)candidate;
                port->byte_sync_best_sync0 = candidate_sync0;
                port->byte_sync_best_sync1 = candidate_sync1;
                port->byte_sync_best_flags0 = candidate_flags0;
                port->byte_sync_best_flags1 = candidate_flags1;
                port->byte_sync_best_clean_frames = candidate_clean_frames;
                port->byte_sync_best_sync_valid_frames = candidate_sync_valid_frames;
                port->byte_sync_best_frame_skip = (uint8_t)frame_skip;
                port->byte_sync_best_phase = (uint8_t)offset;
                port->byte_sync_sample_frames = (uint8_t)sample_frames;
                return 0;
            }
        }
    }

    port->byte_sync_best_offset = (uint16_t)best_offset;
    port->byte_sync_best_sync0 = best_sync0;
    port->byte_sync_best_sync1 = best_sync1;
    port->byte_sync_best_flags0 = best_flags0;
    port->byte_sync_best_flags1 = best_flags1;
    port->byte_sync_best_clean_frames = best_clean_frames;
    port->byte_sync_best_sync_valid_frames = best_sync_valid_frames;
    port->byte_sync_best_frame_skip = (uint8_t)best_frame_skip;
    port->byte_sync_best_phase = (uint8_t)best_phase;
    port->byte_sync_sample_frames = (uint8_t)sample_frames;

    return -1;
}

static int XE_NP_App_ValidateLockedByteSyncWindow(
    XE_NP_AppDmaPort *port,
    const uint8_t *region,
    size_t region_bytes,
    size_t sample_frames,
    size_t *aligned_offset_bytes)
{
    XE_NP_PsbFrame frame;
    const size_t frame_bytes = XE_NP_CAPTURE_FRAME_BIT_COUNT;
    const size_t required_bytes = sample_frames * frame_bytes;
    size_t candidate;
    uint16_t candidate_sync0 = 0U;
    uint16_t candidate_sync1 = 0U;
    uint32_t candidate_flags0 = 0U;
    uint32_t candidate_flags1 = 0U;
    uint8_t candidate_clean_frames = 0U;
    uint8_t candidate_sync_valid_frames = 0U;
    uint16_t candidate_fixed_word = 0U;
    uint32_t candidate_counter_value = 0U;
    uint8_t candidate_fixed_valid = 0U;

    if((port == NULL) || (port->capture_port == NULL) || (region == NULL) ||
       (aligned_offset_bytes == NULL) || (sample_frames == 0U) ||
       (port->capture_port->frame_alignment_locked == 0U))
    {
        return -1;
    }

    candidate = (size_t)port->capture_port->frame_alignment_ticks;
    if((candidate >= region_bytes) || ((candidate + required_bytes) > region_bytes))
    {
        return -1;
    }

    for(size_t frame_index = 0U; frame_index < sample_frames; ++frame_index)
    {
        size_t frame_offset = candidate + (frame_index * frame_bytes);
        int decode_rc =
            XE_NP_PsbFrame_Decode(&frame, &region[frame_offset], region_bytes - frame_offset);

        if(frame_index == 0U)
        {
            candidate_sync0 = frame.sync_word;
            candidate_flags0 = frame.flags;
        }
        else if(frame_index == 1U)
        {
            candidate_sync1 = frame.sync_word;
            candidate_flags1 = frame.flags;
        }

        if((frame.flags & XE_NP_PSB_FRAME_FLAG_SYNC_VALID) != 0U)
        {
            candidate_sync_valid_frames++;
        }

        if((decode_rc != 0) ||
           ((frame.flags & (XE_NP_PSB_FRAME_FLAG_HEARTBEAT_ERROR |
                            XE_NP_PSB_FRAME_FLAG_FILL_ERROR)) != 0U))
        {
            return -1;
        }

        if(candidate_fixed_valid == 0U)
        {
            candidate_fixed_word = frame.fixed_word;
            candidate_counter_value = frame.counter_value;
            candidate_fixed_valid = 1U;
        }
        else
        {
            uint32_t expected_counter = (candidate_counter_value + 1U) & 0x000FFFFFUL;

            if((frame.fixed_word != candidate_fixed_word) ||
               (frame.counter_value != expected_counter))
            {
                return -1;
            }

            candidate_counter_value = frame.counter_value;
        }

        candidate_clean_frames++;
    }

    *aligned_offset_bytes = candidate;
    port->byte_sync_best_offset = (uint16_t)candidate;
    port->byte_sync_best_sync0 = candidate_sync0;
    port->byte_sync_best_sync1 = candidate_sync1;
    port->byte_sync_best_flags0 = candidate_flags0;
    port->byte_sync_best_flags1 = candidate_flags1;
    port->byte_sync_best_clean_frames = candidate_clean_frames;
    port->byte_sync_best_sync_valid_frames = candidate_sync_valid_frames;
    port->byte_sync_best_frame_skip = 0U;
    port->byte_sync_best_phase = (uint8_t)(candidate % frame_bytes);
    port->byte_sync_sample_frames = (uint8_t)sample_frames;
    return 0;
}

static int XE_NP_App_ValidateByteSyncWindowTail(
    const uint8_t *region,
    size_t region_bytes,
    size_t aligned_offset_bytes,
    size_t sample_frames)
{
    XE_NP_PsbFrame frame;
    const size_t frame_bytes = XE_NP_CAPTURE_FRAME_BIT_COUNT;
    uint16_t candidate_fixed_word = 0U;
    uint32_t candidate_counter_value = 0U;
    uint8_t candidate_fixed_valid = 0U;
    size_t available_frames;
    size_t candidate;

    if((region == NULL) || (sample_frames == 0U) ||
       (aligned_offset_bytes >= region_bytes) ||
       ((aligned_offset_bytes + frame_bytes) > region_bytes))
    {
        return -1;
    }

    available_frames = (region_bytes - aligned_offset_bytes) / frame_bytes;
    if(available_frames < sample_frames)
    {
        return -1;
    }

    candidate = aligned_offset_bytes + ((available_frames - sample_frames) * frame_bytes);
    for(size_t frame_index = 0U; frame_index < sample_frames; ++frame_index)
    {
        size_t frame_offset = candidate + (frame_index * frame_bytes);
        int decode_rc =
            XE_NP_PsbFrame_Decode(&frame, &region[frame_offset], region_bytes - frame_offset);

        if((decode_rc != 0) ||
           ((frame.flags & (XE_NP_PSB_FRAME_FLAG_SYNC_VALID |
                            XE_NP_PSB_FRAME_FLAG_HEARTBEAT_ERROR |
                            XE_NP_PSB_FRAME_FLAG_FILL_ERROR)) !=
            XE_NP_PSB_FRAME_FLAG_SYNC_VALID))
        {
            return -1;
        }

        if(candidate_fixed_valid == 0U)
        {
            candidate_fixed_word = frame.fixed_word;
            candidate_counter_value = frame.counter_value;
            candidate_fixed_valid = 1U;
        }
        else
        {
            uint32_t expected_counter = (candidate_counter_value + 1U) & 0x000FFFFFUL;

            if((frame.fixed_word != candidate_fixed_word) ||
               (frame.counter_value != expected_counter))
            {
                return -1;
            }

            candidate_counter_value = frame.counter_value;
        }
    }

    return 0;
}

static void XE_NP_App_ServiceSyncWatchdogPort(XE_NP_AppDmaPort *port)
{
    XE_NP_PsbFrame frame;
    uint32_t primask;
    uint32_t offset_bytes;
    uint32_t size_bytes;
    uint32_t inspect_size_bytes;
    uint8_t *region;
    size_t tick_count;
    size_t sample_frames;
    uint32_t validation_flags;
    uint8_t had_lock;
    size_t aligned_offset_bytes;
    uint64_t total_bytes_end;

    if((port == NULL) || (port->capture == NULL) || (port->capture_port == NULL))
    {
        return;
    }

#if ((XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_ENABLE != 0U) && \
     (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_PUBLISH_ENABLE != 0U))
    /*
     * Each physical lane contains every other PSB bit, so applying the
     * ordinary byte-stream watchdog to either lane would manufacture a
     * framing failure.  The paired watchdog below owns qualification for
     * this explicit single-probe GPIOE virtual-port layout.
     */
    if(((port->capture->flags &
         (XE_NP_CAPTURE_FLAG_GPIOE_ACTIVE | XE_NP_CAPTURE_FLAG_GPIOD_ACTIVE)) ==
        (XE_NP_CAPTURE_FLAG_GPIOE_ACTIVE | XE_NP_CAPTURE_FLAG_GPIOD_ACTIVE)) &&
       ((((port->capture->active_probe_mask & XE_NP_CAPTURE_ALL_PROBES_MASK) ==
          XE_NP_CAPTURE_PROBE1_MASK) ||
         ((port->capture->active_probe_mask & XE_NP_CAPTURE_ALL_PROBES_MASK) ==
          XE_NP_CAPTURE_PROBE2_MASK))) &&
       (port->capture_port->active_probe_mask ==
        (port->capture->active_probe_mask & XE_NP_CAPTURE_ALL_PROBES_MASK)))
    {
        primask = XE_NP_App_CriticalSection_Enter();
        port->sync_watchdog_pending = 0U;
        port->sync_watchdog_size_bytes = 0U;
        port->sync_watchdog_offset_bytes = 0U;
        port->sync_watchdog_total_bytes_end = 0ULL;
        XE_NP_App_CriticalSection_Exit(primask);
        return;
    }
#endif

    if(XE_NP_App_PostResetCaptureValidationAllowed(port) == 0)
    {
        primask = XE_NP_App_CriticalSection_Enter();
        port->sync_watchdog_pending = 0U;
        port->sync_watchdog_size_bytes = 0U;
        port->sync_watchdog_offset_bytes = 0U;
        port->sync_watchdog_total_bytes_end = 0ULL;
        XE_NP_App_CriticalSection_Exit(primask);
        return;
    }

    primask = XE_NP_App_CriticalSection_Enter();
    if(port->sync_watchdog_pending == 0U)
    {
        XE_NP_App_CriticalSection_Exit(primask);
        return;
    }

    offset_bytes = port->sync_watchdog_offset_bytes;
    size_bytes = port->sync_watchdog_size_bytes;
    total_bytes_end = port->sync_watchdog_total_bytes_end;
    port->sync_watchdog_pending = 0U;
    XE_NP_App_CriticalSection_Exit(primask);

    if(XE_NP_App_ShouldSampleRuntimeSyncWatchdogWindow(
           port,
           size_bytes,
           total_bytes_end) == 0)
    {
        return;
    }

    if((size_bytes == 0U) || ((offset_bytes + size_bytes) > XE_NP_App_GetCaptureStoragePhysicalBytes(port)))
    {
        XE_NP_App_ReportCaptureFault(port, CE32_ERRORID_CORE_ERROR, 0U, 1U);
        return;
    }

    region = &port->capture_port->storage[offset_bytes];
    inspect_size_bytes = size_bytes;
    if(inspect_size_bytes > XE_NP_APP_SYNC_WATCHDOG_MAX_PREFIX_BYTES)
    {
        inspect_size_bytes = XE_NP_APP_SYNC_WATCHDOG_MAX_PREFIX_BYTES;
    }
    XE_NP_Capture_DCacheInvalidate(region, inspect_size_bytes);
    sample_frames = XE_NP_App_SelectSyncWatchdogSampleFrames(port);
    had_lock = port->capture_port->frame_alignment_locked;
    aligned_offset_bytes = 0U;

    if(port->capture_port->bytes_per_tick == sizeof(uint8_t))
    {
        int lock_ok = -1;

        if(had_lock != 0U)
        {
            lock_ok = XE_NP_App_ValidateLockedByteSyncWindow(
                port,
                region,
                (size_t)inspect_size_bytes,
                sample_frames,
                &aligned_offset_bytes);
        }

        if((lock_ok == 0) ||
           (XE_NP_App_FindByteSyncWindow(
                port,
                region,
                (size_t)inspect_size_bytes,
                sample_frames,
                &aligned_offset_bytes) == 0))
        {
            if(XE_NP_PsbFrame_Decode(
                   &frame,
               &region[aligned_offset_bytes],
                   (size_t)inspect_size_bytes - aligned_offset_bytes) == 0)
            {
                if(XE_NP_App_ValidateByteSyncWindowTail(
                       region,
                       (size_t)inspect_size_bytes,
                       aligned_offset_bytes,
                       sample_frames) != 0)
                {
                    validation_flags = XE_NP_PSB_VALIDATOR_FLAG_SYNC_ERROR;
                    goto sync_watchdog_byte_fail;
                }

                uint64_t current_abs_offset_bytes =
                    total_bytes_end - (uint64_t)size_bytes + (uint64_t)aligned_offset_bytes;

                if(xe_np_probe_state.initialized != 0U)
                {
                    if(port->continuity_last_valid != 0U)
                    {
                        uint64_t delta_bytes =
                            current_abs_offset_bytes - port->continuity_last_abs_offset_bytes;

                        if((delta_bytes >= XE_NP_CAPTURE_FRAME_BIT_COUNT) &&
                           ((delta_bytes % XE_NP_CAPTURE_FRAME_BIT_COUNT) == 0U))
                        {
                            uint32_t delta_frames =
                                (uint32_t)(delta_bytes / XE_NP_CAPTURE_FRAME_BIT_COUNT);
                            uint32_t expected_counter =
                                (port->continuity_last_counter + delta_frames) & 0x000FFFFFUL;

                            port->continuity_last_frame_delta = delta_frames;
                            port->continuity_last_expected_counter = expected_counter;
                            port->continuity_last_observed_counter = frame.counter_value;
                            if(frame.counter_value == expected_counter)
                            {
                                port->continuity_ok_count++;
                            }
                            else
                            {
                                port->continuity_error_count++;
                            }
                        }
                    }

                    port->continuity_last_abs_offset_bytes = current_abs_offset_bytes;
                    port->continuity_last_counter = frame.counter_value;
                    port->continuity_last_valid = 1U;
                }
            }

            port->sync_watchdog_fail_count = 0U;
            port->sync_watchdog_ok_count++;
            port->windows_processed++;
            return;
        }

        validation_flags = XE_NP_PSB_VALIDATOR_FLAG_SYNC_ERROR;
sync_watchdog_byte_fail:
        ;
    }
    else
    {
        tick_count = (size_t)inspect_size_bytes / sizeof(uint16_t);
        validation_flags = XE_NP_Capture_ValidateSnapshotPrefix(
            port->capture,
            (const uint16_t *)region,
            tick_count,
            port->port_index,
            sample_frames);
    }

    if(validation_flags == 0U)
    {
        port->sync_watchdog_fail_count = 0U;
        port->sync_watchdog_ok_count++;
        port->windows_processed++;
        return;
    }

    if(xe_np_probe_state.initialized == 0U)
    {
        /*
         * Bring-up qualification needs consecutive clean windows. Do not let a
         * single early success survive across a later watchdog failure and
         * accidentally satisfy the startup lock gate.
         */
        port->sync_watchdog_ok_count = 0U;
    }
    port->sync_watchdog_error_count++;
    XE_NP_App_ResetPortSyncState(port);
    if((had_lock != 0U) ||
       (port->sync_watchdog_fail_count >= (XE_NP_APP_SYNC_WATCHDOG_FAIL_LIMIT_UNLOCKED - 1U)))
    {
        XE_NP_App_ReportCaptureFault(port, CE32_ERRORID_CORE_ERROR, 1U, 0U);
        return;
    }

    port->sync_watchdog_fail_count++;
}

#if ((XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_ENABLE != 0U) && \
     (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_PUBLISH_ENABLE != 0U))
static int XE_NP_App_InterleavedGpioeCaptureLocked(const XE_NP_Runtime *runtime)
{
    const XE_NP_AppDmaPort *lane_a = &xe_np_dma_gpioe;
    const XE_NP_AppDmaPort *lane_b = &xe_np_dma_gpiod;
    uint8_t probe_mask;

    if(runtime == NULL)
    {
        return 0;
    }
    probe_mask = runtime->capture.active_probe_mask & XE_NP_CAPTURE_ALL_PROBES_MASK;
    if(((probe_mask != XE_NP_CAPTURE_PROBE1_MASK) &&
        (probe_mask != XE_NP_CAPTURE_PROBE2_MASK)) ||
       ((runtime->capture.flags &
         (XE_NP_CAPTURE_FLAG_GPIOE_ACTIVE | XE_NP_CAPTURE_FLAG_GPIOD_ACTIVE)) !=
        (XE_NP_CAPTURE_FLAG_GPIOE_ACTIVE | XE_NP_CAPTURE_FLAG_GPIOD_ACTIVE)) ||
       (lane_a->active == 0U) || (lane_b->active == 0U) ||
       (lane_a->capture_port == NULL) || (lane_b->capture_port == NULL) ||
       (lane_a->capture_port->active_probe_mask != probe_mask) ||
       (lane_b->capture_port->active_probe_mask != probe_mask) ||
       (lane_a->capture_port->dma_windows_captured == 0U) ||
       (lane_b->capture_port->dma_windows_captured == 0U) ||
       (lane_a->capture_port->total_bytes_captured == 0ULL) ||
       (lane_b->capture_port->total_bytes_captured == 0ULL))
    {
        return 0;
    }

    return ((lane_a->capture_port->frame_alignment_locked != 0U) &&
            (lane_b->capture_port->frame_alignment_locked != 0U) &&
            ((lane_a->capture_port->validated_probe_mask & probe_mask) != 0U) &&
            ((lane_b->capture_port->validated_probe_mask & probe_mask) != 0U) &&
            (lane_a->validation_error_seen == 0U) &&
            (lane_b->validation_error_seen == 0U) &&
            (lane_a->sync_watchdog_ok_count >= XE_NP_App_RequiredSyncWatchdogOkCount(lane_a)) &&
            (lane_b->sync_watchdog_ok_count >= XE_NP_App_RequiredSyncWatchdogOkCount(lane_b))) ? 1 : 0;
}

static void XE_NP_App_InvalidateInterleavedGpioeLaneRange(
    const XE_NP_CapturePort *capture_port,
    uint32_t offset_bytes,
    uint32_t size_bytes)
{
    uint32_t first_size_bytes;

    if((capture_port == NULL) || (capture_port->storage == NULL) ||
       (capture_port->storage_bytes == 0U) ||
       (offset_bytes >= capture_port->storage_bytes) ||
       (size_bytes == 0U) || (size_bytes > capture_port->storage_bytes))
    {
        return;
    }

    first_size_bytes = capture_port->storage_bytes - offset_bytes;
    if(first_size_bytes > size_bytes)
    {
        first_size_bytes = size_bytes;
    }
    XE_NP_Capture_DCacheInvalidate(&capture_port->storage[offset_bytes], first_size_bytes);
    if(first_size_bytes < size_bytes)
    {
        XE_NP_Capture_DCacheInvalidate(
            capture_port->storage,
            size_bytes - first_size_bytes);
    }
}

static void XE_NP_App_ServiceInterleavedGpioeSyncWatchdog(void)
{
    XE_NP_AppDmaPort *lane_a = &xe_np_dma_gpioe;
    XE_NP_AppDmaPort *lane_b = &xe_np_dma_gpiod;
    XE_NP_CapturePort *capture_a;
    XE_NP_CapturePort *capture_b;
    XE_NP_PsbFrame frame;
    uint64_t total_a;
    uint64_t total_b;
    uint64_t pair_end;
    uint64_t source_start_pair;
    uint64_t current_abs_offset_bytes;
    uint32_t primask;
    uint32_t offset_a;
    uint32_t offset_b;
    uint32_t lane_b_base_offset;
    uint32_t index;
    size_t sample_frames;
    size_t aligned_offset_bytes = 0U;
    uint16_t expected_fixed_word;
    uint8_t probe_mask;
    uint8_t had_lock;
    uint8_t phase_found = 0U;
    int32_t lane_b_offset_first;
    int32_t lane_b_offset_last;
    int32_t lane_b_offset;
    int lock_ok = -1;

    if((lane_a->active == 0U) || (lane_b->active == 0U) ||
       (lane_a->capture == NULL) || (lane_b->capture == NULL) ||
       (lane_a->capture != lane_b->capture) ||
       ((lane_a->capture->flags &
         (XE_NP_CAPTURE_FLAG_GPIOE_ACTIVE | XE_NP_CAPTURE_FLAG_GPIOD_ACTIVE)) !=
        (XE_NP_CAPTURE_FLAG_GPIOE_ACTIVE | XE_NP_CAPTURE_FLAG_GPIOD_ACTIVE)))
    {
        return;
    }
    probe_mask = lane_a->capture->active_probe_mask & XE_NP_CAPTURE_ALL_PROBES_MASK;
    if((probe_mask != XE_NP_CAPTURE_PROBE1_MASK) &&
       (probe_mask != XE_NP_CAPTURE_PROBE2_MASK))
    {
        return;
    }

    capture_a = lane_a->capture_port;
    capture_b = lane_b->capture_port;
    if((capture_a == NULL) || (capture_b == NULL) ||
       (capture_a->storage == NULL) || (capture_b->storage == NULL) ||
       (capture_a->storage_bytes == 0U) ||
       (capture_a->storage_bytes != capture_b->storage_bytes) ||
       (capture_a->storage_bytes < XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_VALIDATION_LANE_BYTES) ||
       (capture_a->active_probe_mask != probe_mask) ||
       (capture_b->active_probe_mask != probe_mask) ||
       (XE_NP_App_PostResetCaptureValidationAllowed(lane_a) == 0))
    {
        return;
    }

    primask = XE_NP_App_CriticalSection_Enter();
    total_a = capture_a->total_bytes_captured;
    total_b = capture_b->total_bytes_captured;
    XE_NP_App_CriticalSection_Exit(primask);
    pair_end = (total_a < total_b) ? total_a : total_b;
    if(pair_end < (uint64_t)(XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_VALIDATION_LANE_BYTES * 2U))
    {
        return;
    }
    if(pair_end == xe_np_gpioe_interleaved_validation_last_pair_end)
    {
        return;
    }
    /*
     * The paired lane phase is qualified before USB export begins.  Do not
     * search live SRAM3 while a 15 kHz session is running: the exporter needs
     * that foreground budget to move completed bytes into private SRAM1.
     * Capture chronology is enforced by the DMA completion accounting below;
     * an already locked phase is therefore retained for this session.
     */
    if(xe_np_gpioe_interleaved_validation_lane_b_offset_valid != 0U)
    {
        return;
    }
    /*
     * Read a completed history window one validation-window behind the
     * producer.  The DMA engine is then writing the opposite half of the
     * lane, so neither source byte can be updated while it is copied.
     */
    source_start_pair = pair_end -
                        (uint64_t)(XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_VALIDATION_LANE_BYTES * 2U);
    offset_a = (uint32_t)(source_start_pair % (uint64_t)capture_a->storage_bytes);
    lane_b_base_offset = (uint32_t)(source_start_pair % (uint64_t)capture_b->storage_bytes);
    expected_fixed_word = xe_np_probe_config_store.probe_config[
        (probe_mask == XE_NP_CAPTURE_PROBE1_MASK) ? 0U : 1U].sync_value;
    XE_NP_App_InvalidateInterleavedGpioeLaneRange(
        capture_a,
        offset_a,
        XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_VALIDATION_LANE_BYTES);
    for(index = 0U;
        index < XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_VALIDATION_LANE_BYTES;
        ++index)
    {
        xe_np_gpioe_interleaved_validation_bytes[index * 2U] = capture_a->storage[offset_a];
        if(++offset_a == capture_a->storage_bytes)
        {
            offset_a = 0U;
        }
    }

    /*
     * The ordinary startup watchdog asks for an ultraframe, but the paired
     * scratch buffer deliberately holds only 10 KiB.  Eight complete frames
     * are sufficient to find and verify the inter-lane phase; requesting 156
     * made every startup search impossible before it examined one byte.
     */
    sample_frames = XE_NP_App_SelectSyncWatchdogSampleFrames(lane_a);
    if(sample_frames > XE_NP_APP_SYNC_WATCHDOG_SAMPLE_FRAMES_UNLOCKED)
    {
        sample_frames = XE_NP_APP_SYNC_WATCHDOG_SAMPLE_FRAMES_UNLOCKED;
    }
    had_lock = ((xe_np_gpioe_interleaved_validation_lane_b_offset_valid != 0U) &&
                (capture_a->frame_alignment_locked != 0U) &&
                (capture_b->frame_alignment_locked != 0U)) ? 1U : 0U;
    if(XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_FIXED_LANE_B_OFFSET != 127)
    {
        /* Still validate this measured phase against complete PSB frames; it
         * merely replaces an expensive live SRAM3 phase sweep at startup. */
        lane_b_offset_first = XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_FIXED_LANE_B_OFFSET;
        lane_b_offset_last = XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_FIXED_LANE_B_OFFSET;
    }
    else if(xe_np_gpioe_interleaved_validation_lane_b_offset_valid != 0U)
    {
        /*
         * The two GPDMA channels are phase-locked by their timer requests,
         * but a circular descriptor reload can move one physical lane by a
         * few byte times.  Re-qualify in a deliberately narrow neighbourhood
         * of the last proven offset on every completed guarded window.  The
         * exporter consumes only older bytes, so a new qualified offset is in
         * force before that window reaches USB.
         */
        lane_b_offset_first = xe_np_gpioe_interleaved_validation_lane_b_offset -
                              XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_PHASE_SEARCH_RADIUS;
        lane_b_offset_last = xe_np_gpioe_interleaved_validation_lane_b_offset +
                             XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_PHASE_SEARCH_RADIUS;
        if(lane_b_offset_first < -XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_PHASE_SEARCH_RADIUS)
        {
            lane_b_offset_first = -XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_PHASE_SEARCH_RADIUS;
        }
        if(lane_b_offset_last > XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_PHASE_SEARCH_RADIUS)
        {
            lane_b_offset_last = XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_PHASE_SEARCH_RADIUS;
        }
    }
    else
    {
        /* TIM8 CC1/UP arbitration can leave a bounded initial lane lead. */
        lane_b_offset_first = -XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_PHASE_SEARCH_RADIUS;
        lane_b_offset_last = XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_PHASE_SEARCH_RADIUS;
    }

    for(lane_b_offset = lane_b_offset_first;
        lane_b_offset <= lane_b_offset_last;
        ++lane_b_offset)
    {
        int32_t shifted_offset = (int32_t)lane_b_base_offset + lane_b_offset;

        if(shifted_offset < 0)
        {
            shifted_offset += (int32_t)capture_b->storage_bytes;
        }
        else if(shifted_offset >= (int32_t)capture_b->storage_bytes)
        {
            shifted_offset -= (int32_t)capture_b->storage_bytes;
        }
        offset_b = (uint32_t)shifted_offset;
        XE_NP_App_InvalidateInterleavedGpioeLaneRange(
            capture_b,
            offset_b,
            XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_VALIDATION_LANE_BYTES);
        for(index = 0U;
            index < XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_VALIDATION_LANE_BYTES;
            ++index)
        {
            xe_np_gpioe_interleaved_validation_bytes[(index * 2U) + 1U] =
                capture_b->storage[offset_b];
            if(++offset_b == capture_b->storage_bytes)
            {
                offset_b = 0U;
            }
        }

        lock_ok = -1;
        if(had_lock != 0U)
        {
            lock_ok = XE_NP_App_ValidateLockedByteSyncWindow(
                lane_a,
                xe_np_gpioe_interleaved_validation_bytes,
                sizeof(xe_np_gpioe_interleaved_validation_bytes),
                sample_frames,
                &aligned_offset_bytes);
        }
        if(((had_lock != 0U && lock_ok == 0) ||
            ((had_lock == 0U) &&
             (XE_NP_App_FindByteSyncWindow(
                  lane_a,
                  xe_np_gpioe_interleaved_validation_bytes,
                  sizeof(xe_np_gpioe_interleaved_validation_bytes),
                  sample_frames,
                  &aligned_offset_bytes) == 0))) &&
           (XE_NP_PsbFrame_Decode(
                &frame,
                &xe_np_gpioe_interleaved_validation_bytes[aligned_offset_bytes],
                sizeof(xe_np_gpioe_interleaved_validation_bytes) - aligned_offset_bytes) == 0) &&
           (frame.fixed_word == expected_fixed_word) &&
           (XE_NP_App_ValidateByteSyncWindowTail(
                xe_np_gpioe_interleaved_validation_bytes,
                sizeof(xe_np_gpioe_interleaved_validation_bytes),
                aligned_offset_bytes,
                sample_frames) == 0))
        {
            xe_np_gpioe_interleaved_validation_lane_b_offset = (int8_t)lane_b_offset;
            xe_np_gpioe_interleaved_validation_lane_b_offset_valid = 1U;
            phase_found = 1U;
            break;
        }

        /*
         * A rejected B-lane displacement does not invalidate the byte/frame
         * lock: the A lane and its absolute frame phase are unchanged.  Keep
         * that lock while trying the remaining displacements.  Resetting it
         * here made every candidate after the first use FindByteSyncWindow(),
         * turning a bounded per-node check into a multi-millisecond full
         * search and starving the 15 kHz USB producer.
         */
    }
    xe_np_gpioe_interleaved_validation_last_pair_end = pair_end;

    if(phase_found != 0U)
    {
        capture_b->frame_alignment_ticks = capture_a->frame_alignment_ticks;
        capture_b->frame_alignment_locked = capture_a->frame_alignment_locked;
        capture_b->validated_probe_mask = capture_b->active_probe_mask;
        lane_b->byte_sync_best_offset = lane_a->byte_sync_best_offset;
        lane_b->byte_sync_best_sync0 = lane_a->byte_sync_best_sync0;
        lane_b->byte_sync_best_sync1 = lane_a->byte_sync_best_sync1;
        lane_b->byte_sync_best_flags0 = lane_a->byte_sync_best_flags0;
        lane_b->byte_sync_best_flags1 = lane_a->byte_sync_best_flags1;
        lane_b->byte_sync_best_clean_frames = lane_a->byte_sync_best_clean_frames;
        lane_b->byte_sync_best_sync_valid_frames = lane_a->byte_sync_best_sync_valid_frames;
        lane_b->byte_sync_best_frame_skip = lane_a->byte_sync_best_frame_skip;
        lane_b->byte_sync_best_phase = lane_a->byte_sync_best_phase;
        lane_b->byte_sync_sample_frames = lane_a->byte_sync_sample_frames;
        lane_a->sync_watchdog_fail_count = 0U;
        lane_b->sync_watchdog_fail_count = 0U;
        lane_a->sync_watchdog_ok_count++;
        lane_b->sync_watchdog_ok_count++;
        lane_a->windows_processed++;
        lane_b->windows_processed++;

        if(xe_np_probe_state.initialized != 0U)
        {
                current_abs_offset_bytes = (source_start_pair * 2ULL) +
                                           (uint64_t)aligned_offset_bytes;
                if((lane_a->continuity_last_valid != 0U) &&
                   (current_abs_offset_bytes >= lane_a->continuity_last_abs_offset_bytes))
                {
                    uint64_t delta_bytes =
                        current_abs_offset_bytes - lane_a->continuity_last_abs_offset_bytes;

                    if((delta_bytes >= XE_NP_CAPTURE_FRAME_BIT_COUNT) &&
                       ((delta_bytes % XE_NP_CAPTURE_FRAME_BIT_COUNT) == 0U))
                    {
                        uint32_t delta_frames =
                            (uint32_t)(delta_bytes / XE_NP_CAPTURE_FRAME_BIT_COUNT);
                        uint32_t expected_counter =
                            (lane_a->continuity_last_counter + delta_frames) & 0x000FFFFFUL;

                        lane_a->continuity_last_frame_delta = delta_frames;
                        lane_a->continuity_last_expected_counter = expected_counter;
                        lane_a->continuity_last_observed_counter = frame.counter_value;
                        if(frame.counter_value == expected_counter)
                        {
                            lane_a->continuity_ok_count++;
                        }
                        else
                        {
                            lane_a->continuity_error_count++;
                        }
                    }
                }
                lane_a->continuity_last_abs_offset_bytes = current_abs_offset_bytes;
                lane_a->continuity_last_counter = frame.counter_value;
                lane_a->continuity_last_valid = 1U;
                lane_b->continuity_ok_count = lane_a->continuity_ok_count;
                lane_b->continuity_error_count = lane_a->continuity_error_count;
                lane_b->continuity_last_expected_counter = lane_a->continuity_last_expected_counter;
                lane_b->continuity_last_observed_counter = lane_a->continuity_last_observed_counter;
                lane_b->continuity_last_frame_delta = lane_a->continuity_last_frame_delta;
                lane_b->continuity_last_abs_offset_bytes = lane_a->continuity_last_abs_offset_bytes;
                lane_b->continuity_last_counter = lane_a->continuity_last_counter;
                lane_b->continuity_last_valid = lane_a->continuity_last_valid;
        }
        return;
    }

    if(xe_np_probe_state.initialized == 0U)
    {
        lane_a->sync_watchdog_ok_count = 0U;
        lane_b->sync_watchdog_ok_count = 0U;
    }
    lane_a->sync_watchdog_error_count++;
    lane_b->sync_watchdog_error_count++;
    /*
     * The paired history copy can occasionally straddle a just-completed
     * producer window.  A one-off foreground validation miss must not discard
     * an already qualified phase relationship: doing so reinitialised the USB
     * cursor every service pass and made a healthy dual-lane stream appear
     * permanently empty.  Retain the last proven phase and make a real fault
     * require a bounded consecutive failure streak.
     */
    lane_a->sync_watchdog_fail_count++;
    lane_b->sync_watchdog_fail_count++;
    if(lane_a->sync_watchdog_fail_count <
       XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_LOCK_FAIL_LIMIT)
    {
        return;
    }

    xe_np_gpioe_interleaved_validation_lane_b_offset_valid = 0U;
    XE_NP_App_ResetPortSyncState(lane_a);
    XE_NP_App_ResetPortSyncState(lane_b);
    XE_NP_App_ReportCaptureFault(lane_a, CE32_ERRORID_CORE_ERROR, 1U, 0U);
}
#endif
#endif

static int XE_NP_App_PortNeedsAggressiveValidation(const XE_NP_AppDmaPort *port)
{
    uint8_t active_probe_mask;

    if((port == NULL) || (port->capture_port == NULL))
    {
        return 0;
    }

    active_probe_mask = port->capture_port->active_probe_mask;
    if((port->capture_port->frame_alignment_locked == 0U) ||
       ((port->live_decode_probe_mask & active_probe_mask) != active_probe_mask) ||
       (port->validation_error_seen != 0U))
    {
        return 1;
    }

    return 0;
}

static size_t XE_NP_App_SelectValidationSampleFrames(const XE_NP_AppDmaPort *port)
{
    return (XE_NP_App_PortNeedsAggressiveValidation(port) != 0) ?
               XE_NP_APP_DMA_SAMPLE_FRAMES_AGGRESSIVE :
               XE_NP_APP_DMA_SAMPLE_FRAMES_LOCKED;
}

static int XE_NP_App_ShouldValidateWindow(const XE_NP_AppDmaPort *port)
{
    if((port == NULL) || (xe_np_capture_validation_enabled == 0U))
    {
        return 0;
    }

    if(XE_NP_App_PostResetCaptureValidationAllowed(port) == 0)
    {
        return 0;
    }

    if(XE_NP_App_PortNeedsAggressiveValidation(port) != 0)
    {
        return 1;
    }

    /*
     * Match the CE64 policy of protecting the primary producer/consumer path
     * first. Once logging is active and the raw backlog has reached at least
     * half a node in flight or one full node queued, drop sampled validation
     * entirely until the sink regains margin.
     */
    if(XE_NP_App_PortHasRawLoggingBackpressure(port) != 0)
    {
        return 0;
    }

    /*
     * Once sync is stable and each active probe has passed one live decode,
     * sample only a subset of windows. Neuropixels raw capture is the primary
     * path; continuous full-window validation is only for bring-up.
     */
    return (((port->windows_processed + 1U) &
             (XE_NP_APP_VALIDATION_STRIDE_LOCKED - 1U)) == 0U) ? 1 : 0;
}

static int XE_NP_App_FindUltraframeOffset(
    const XE_NP_AppDmaPort *port,
    const uint16_t *snapshots,
    size_t tick_count,
    uint8_t probe_index,
    size_t *ultraframe_offset)
{
    XE_NP_PsbFrame frame;
    const size_t required_ticks =
        (size_t)XE_NP_CAPTURE_FRAMES_PER_ULTRAFRAME * XE_NP_CAPTURE_FRAME_BIT_COUNT;
    const size_t superframe_span_ticks =
        (size_t)XE_NP_CAPTURE_FRAMES_PER_SUPERFRAME * XE_NP_CAPTURE_FRAME_BIT_COUNT;
    size_t aligned_offset;
    size_t search_limit;

    if((port == NULL) || (port->capture_port == NULL) || (snapshots == NULL) ||
       (ultraframe_offset == NULL) || (port->capture_port->frame_alignment_locked == 0U) ||
       (tick_count < required_ticks))
    {
        return -1;
    }

    aligned_offset = (size_t)port->capture_port->frame_alignment_ticks;
    if(aligned_offset >= tick_count)
    {
        return -1;
    }

    search_limit = tick_count - required_ticks;
    for(size_t offset = aligned_offset; offset <= search_limit;
        offset += XE_NP_CAPTURE_FRAME_BIT_COUNT)
    {
        if(XE_NP_PsbFrame_DecodeFromSnapshots(
               &frame,
               &snapshots[offset],
               tick_count - offset,
               probe_index) != 0)
        {
            continue;
        }
        if((frame.flags & (XE_NP_PSB_FRAME_FLAG_SYNC_VALID | XE_NP_PSB_FRAME_FLAG_SUPERFRAME)) !=
           (XE_NP_PSB_FRAME_FLAG_SYNC_VALID | XE_NP_PSB_FRAME_FLAG_SUPERFRAME))
        {
            continue;
        }

        for(uint8_t superframe_index = 1U;
            superframe_index < XE_NP_CAPTURE_SUPERFRAMES_PER_ULTRAFRAME;
            ++superframe_index)
        {
            size_t superframe_offset =
                offset + ((size_t)superframe_index * superframe_span_ticks);
            if(XE_NP_PsbFrame_DecodeFromSnapshots(
                   &frame,
                   &snapshots[superframe_offset],
                   tick_count - superframe_offset,
                   probe_index) != 0)
            {
                break;
            }
            if((frame.flags &
                (XE_NP_PSB_FRAME_FLAG_SYNC_VALID | XE_NP_PSB_FRAME_FLAG_SUPERFRAME)) !=
               (XE_NP_PSB_FRAME_FLAG_SYNC_VALID | XE_NP_PSB_FRAME_FLAG_SUPERFRAME))
            {
                break;
            }
            if(superframe_index == (XE_NP_CAPTURE_SUPERFRAMES_PER_ULTRAFRAME - 1U))
            {
                *ultraframe_offset = offset;
                return 0;
            }
        }
    }

    return -1;
}

static void XE_NP_App_TrySampleLiveDecode(
    XE_NP_AppDmaPort *port,
    const uint16_t *snapshots,
    size_t tick_count)
{
#if (XE_NP_APP_CAPTURE_FOREGROUND_QUEUE_ENABLE == 0U)
    (void)port;
    (void)snapshots;
    (void)tick_count;
    return;
#else
    uint8_t probe_ids[2];
    uint8_t probe_count = 0U;
    uint8_t active_probe_mask = 0U;
    uint32_t now_ms;

    if((port == NULL) || (snapshots == NULL) || (port->capture_port == NULL) ||
       (tick_count <
        ((size_t)XE_NP_CAPTURE_FRAMES_PER_ULTRAFRAME * XE_NP_CAPTURE_FRAME_BIT_COUNT)) ||
       (XE_NP_App_GetPortProbeIds(port, probe_ids, &probe_count) != 0))
    {
        return;
    }

    active_probe_mask = port->capture_port->active_probe_mask;
    if((port->live_decode_probe_mask & active_probe_mask) == active_probe_mask)
    {
        return;
    }

    now_ms = HAL_GetTick();
    if(((port->live_decode_ok_count != 0U) || (port->live_decode_fail_count != 0U)) &&
       ((uint32_t)(now_ms - port->last_live_decode_tick_ms) < XE_NP_APP_LIVE_DECODE_INTERVAL_MS))
    {
        return;
    }
    port->last_live_decode_tick_ms = now_ms;

    /*
     * Full ultraframe reconstruction is intentionally kept in the foreground
     * and sampled at a low rate. The fast DMA IRQ path only enqueues windows.
     */
    for(uint8_t probe_slot = 0U; probe_slot < probe_count; ++probe_slot)
    {
        uint8_t probe_id = probe_ids[probe_slot];
        uint8_t probe_mask = (uint8_t)(1U << probe_id);
        size_t ultraframe_offset = 0U;

        if((port->live_decode_probe_mask & probe_mask) != 0U)
        {
            continue;
        }

        if(XE_NP_App_FindUltraframeOffset(
               port,
               snapshots,
               tick_count,
               probe_id,
               &ultraframe_offset) != 0)
        {
            port->live_decode_fail_count++;
            continue;
        }

        if(XE_NP_ElectrodePacket_DecodeUltraframeFromAlignedSnapshots(
               &xe_np_live_decode_packet,
               &snapshots[ultraframe_offset],
               tick_count - ultraframe_offset,
               probe_id) == 0)
        {
            port->live_decode_ok_count++;
            port->live_decode_probe_mask |= probe_mask;
            XE_NP_App_RecomputeLiveDecodeProbeMask();
        }
        else
        {
            port->live_decode_fail_count++;
        }
    }
#endif
}

static void XE_NP_App_RunCaptureSelfTest(XE_NP_Runtime *runtime)
{
#if (XE_NP_APP_CAPTURE_SELFTEST_ENABLE == 0U)
    if(runtime == NULL)
    {
        return;
    }

    xe_np_capture_selftest_probe_mask = 0U;
    runtime->flags &= ~XE_NP_RUNTIME_FLAG_NP_CAPTURE_SELFTEST_OK;
    runtime->sys_param.system_status &= ~XE_NP_SYSTEM_STATUS_NP_CAPTURE_SELFTEST_OK;
    XE_NP_App_RefreshCaptureDiag(&runtime->sys_param);
    return;
#else
    uint8_t selftest_probe_mask = 0U;

    if(runtime == NULL)
    {
        return;
    }

    xe_np_capture_selftest_probe_mask = 0U;
    runtime->flags &= ~XE_NP_RUNTIME_FLAG_NP_CAPTURE_SELFTEST_OK;
    runtime->sys_param.system_status &= ~XE_NP_SYSTEM_STATUS_NP_CAPTURE_SELFTEST_OK;

    if(XE_NP_Capture_RunSelfTestMask(&selftest_probe_mask) == 0)
    {
        xe_np_capture_selftest_probe_mask = selftest_probe_mask;
        runtime->flags |= XE_NP_RUNTIME_FLAG_NP_CAPTURE_SELFTEST_OK;
        runtime->sys_param.system_status |= XE_NP_SYSTEM_STATUS_NP_CAPTURE_SELFTEST_OK;
    }
    else
    {
        xe_np_capture_selftest_probe_mask = selftest_probe_mask;
        CE32_ReportError(&runtime->sys_param, CE32_ERRORID_CORE_ERROR);
    }

    XE_NP_App_RefreshCaptureDiag(&runtime->sys_param);
#endif
}

static void XE_NP_App_RefreshProbeStatus(XE_NP_Runtime *runtime)
{
    uint32_t flag_mask = XE_NP_RUNTIME_FLAG_NP_I2C_READY |
                         XE_NP_RUNTIME_FLAG_NP_BASE_CONFIGURED |
                         XE_NP_RUNTIME_FLAG_NP_SHANK_DEFAULTS |
                         XE_NP_RUNTIME_FLAG_NP_INIT_FAILED |
                         XE_NP_RUNTIME_FLAG_NP_CALIBRATION_READY;
    uint32_t status_mask = XE_NP_SYSTEM_STATUS_NP_I2C_READY |
                           XE_NP_SYSTEM_STATUS_NP_BASE_CONFIGURED |
                           XE_NP_SYSTEM_STATUS_NP_SHANK_DEFAULTS |
                           XE_NP_SYSTEM_STATUS_NP_INIT_FAILED;
    uint32_t set_flags = 0U;
    uint32_t set_status = 0U;

    if(runtime == NULL)
    {
        return;
    }

    if(xe_np_probe_state.i2c_ready != 0U)
    {
        set_flags |= XE_NP_RUNTIME_FLAG_NP_I2C_READY;
        set_status |= XE_NP_SYSTEM_STATUS_NP_I2C_READY;
    }
    if(xe_np_probe_state.base_configured != 0U)
    {
        set_flags |= XE_NP_RUNTIME_FLAG_NP_BASE_CONFIGURED;
        set_status |= XE_NP_SYSTEM_STATUS_NP_BASE_CONFIGURED;
    }
    if(xe_np_probe_state.shank_defaults_active != 0U)
    {
        set_flags |= XE_NP_RUNTIME_FLAG_NP_SHANK_DEFAULTS;
        set_status |= XE_NP_SYSTEM_STATUS_NP_SHANK_DEFAULTS;
    }
    if(xe_np_probe_state.init_failed != 0U)
    {
        set_flags |= XE_NP_RUNTIME_FLAG_NP_INIT_FAILED;
        set_status |= XE_NP_SYSTEM_STATUS_NP_INIT_FAILED;
    }
    if(XE_NP_App_ActiveProbeCalibrationReady(runtime) != 0)
    {
        set_flags |= XE_NP_RUNTIME_FLAG_NP_CALIBRATION_READY;
        xe_np_probe_calibration_warning_reported = 0U;
    }
    else if((runtime->active_np_probe_mask & XE_NP_CAPTURE_ALL_PROBES_MASK) != 0U)
    {
        if(xe_np_probe_calibration_warning_reported == 0U)
        {
            CE32_ReportError(&runtime->sys_param, CE32_ERRORID_PARAM_ERR);
            xe_np_probe_calibration_warning_reported = 1U;
        }
    }
    else
    {
        xe_np_probe_calibration_warning_reported = 0U;
    }

    runtime->flags = (runtime->flags & ~flag_mask) | set_flags;
    runtime->sys_param.system_status =
        (runtime->sys_param.system_status & ~status_mask) | set_status;
}

static void XE_NP_App_SetProbePowerRails(XE_NP_Runtime *runtime, uint8_t enabled)
{
    if(runtime == NULL)
    {
        return;
    }

    /*
     * VCC_EN is the explicit Neuropixels mount rail on this board. VEE_EN is
     * brought up earlier for the SD/storage path and must stay available once
     * the card is mounted, so the NP session helper only gates VCC_EN here.
     *
     * CAL_SIGNAL is tied to the probe TEST input in the direct-bond design.
     * Force the DAC low across every rail transition so a stale manual test
     * drive cannot leak into the next powered probe session.
     */
    if(XE_NP_App_SetProbeCalDacRawInternal(runtime, 0U) != 0)
    {
        CE32_ReportError(&runtime->sys_param, CE32_ERRORID_CORE_ERROR);
    }

    uint8_t keep_camera_vcc = np_camera_power_owner;
#if NP_SD_APP
    /* Media mode raises camera VDD1 before the shared SD/I/O rail in main.
     * Generic NP reset/STOP must not undo that order after the card is up.
     * ColdPowerOff still removes both rails; neural modes still gate VCC. */
    keep_camera_vcc |= (!np_mode_mask && !np_mode_hz);
#endif
    if(enabled != 0U || !keep_camera_vcc)
        HAL_GPIO_WritePin(VCC_EN_GPIO_Port, VCC_EN_Pin, (enabled != 0U) ? GPIO_PIN_SET : GPIO_PIN_RESET);

    if(enabled != 0U)
    {
        runtime->flags |= XE_NP_RUNTIME_FLAG_NP_POWER_RAILS_ENABLED;
    }
    else
    {
        runtime->flags &= ~XE_NP_RUNTIME_FLAG_NP_POWER_RAILS_ENABLED;
    }
}

static int XE_NP_App_AllActiveProbeInterfacesReady(const XE_NP_Runtime *runtime)
{
    if(runtime == NULL)
    {
        return 0;
    }

    for(uint8_t probe_index = 0U; probe_index < XE_NP_NEUROPIXELS_MAX_PROBES; ++probe_index)
    {
        uint8_t probe_mask = (uint8_t)(1U << probe_index);
        if(((runtime->active_np_probe_mask & probe_mask) != 0U) &&
           (runtime->hi2c_np[probe_index] == NULL))
        {
            return 0;
        }
    }

    return 1;
}

static int XE_NP_App_VerifyActiveProbeRunStatus(XE_NP_Runtime *runtime)
{
    uint8_t probe_index;
    uint8_t status;

    if(runtime == NULL)
    {
        return 0;
    }

#if (XE_NP_APP_EXPERIMENT_CAPTURE_SUPPRESS_DMA_ENABLE != 0U)
    /* This transport-only image may bypass NRIC programming; readback is
     * deliberately outside the scope of this control. */
    return 1;
#endif

    XE_NP_App_EnsureProbeConfigsInitialized();
    for(probe_index = 0U; probe_index < XE_NP_NEUROPIXELS_MAX_PROBES; ++probe_index)
    {
        uint8_t probe_mask = (uint8_t)(1U << probe_index);
        uint8_t verify_flags = 0U;
        uint8_t cal_mod = 0U;
        uint8_t sync = 0U;
        uint8_t rec_mod = 0U;
        uint8_t op_mode = 0U;
        uint8_t explicit_sr_programming;
        const XE_NP_NricConfig *config = &xe_np_probe_config_store.probe_config[probe_index];

        if((runtime->active_np_probe_mask & probe_mask) == 0U)
        {
            xe_np_probe_state.last_status[probe_index] = 0U;
            xe_np_probe_state.last_cal_mod[probe_index] = 0U;
            xe_np_probe_state.last_sync[probe_index] = 0U;
            xe_np_probe_state.last_rec_mod[probe_index] = 0U;
            xe_np_probe_state.last_op_mode[probe_index] = 0U;
            xe_np_probe_state.last_verify_flags[probe_index] = 0U;
            continue;
        }

        if((runtime->hi2c_np[probe_index] == NULL) ||
           (XE_NP_Nric_ReadRegister(
                runtime->hi2c_np[probe_index],
                XE_NP_NRIC_REG_STATUS,
                &status) != HAL_OK))
        {
            xe_np_probe_state.last_status[probe_index] = 0U;
            xe_np_probe_state.last_cal_mod[probe_index] = 0U;
            xe_np_probe_state.last_sync[probe_index] = 0U;
            xe_np_probe_state.last_rec_mod[probe_index] = 0U;
            xe_np_probe_state.last_op_mode[probe_index] = 0U;
            xe_np_probe_state.last_verify_flags[probe_index] = XE_NP_APP_PROBE_VERIFY_FLAG_I2C_FAIL;
            return 0;
        }

        xe_np_probe_state.last_status[probe_index] = status;
        explicit_sr_programming =
            ((config->chain2_bytes > 0U) || (config->chain3_bytes > 0U)) ? 1U : 0U;
        (void)explicit_sr_programming;
        /*
         * SR_OUT_OK is checked at the moment each SR chain is programmed.
         * On the direct-NRIC path it is not a reliable persistent runtime
         * status bit after later register writes, so do not fail bring-up or
         * runtime verification on a subsequent STATUS readback alone.
         */

        if((XE_NP_Nric_ReadRegister(runtime->hi2c_np[probe_index], XE_NP_NRIC_REG_CAL_MOD, &cal_mod) != HAL_OK) ||
           (XE_NP_Nric_ReadRegister(runtime->hi2c_np[probe_index], XE_NP_NRIC_REG_SYNC, &sync) != HAL_OK) ||
           (XE_NP_Nric_ReadRegister(runtime->hi2c_np[probe_index], XE_NP_NRIC_REG_REC_MOD, &rec_mod) != HAL_OK) ||
           (XE_NP_Nric_ReadRegister(runtime->hi2c_np[probe_index], XE_NP_NRIC_REG_OP_MODE, &op_mode) != HAL_OK))
        {
            xe_np_probe_state.last_cal_mod[probe_index] = 0U;
            xe_np_probe_state.last_sync[probe_index] = 0U;
            xe_np_probe_state.last_rec_mod[probe_index] = 0U;
            xe_np_probe_state.last_op_mode[probe_index] = 0U;
            xe_np_probe_state.last_verify_flags[probe_index] =
                (uint8_t)(verify_flags | XE_NP_APP_PROBE_VERIFY_FLAG_I2C_FAIL);
            return 0;
        }

        xe_np_probe_state.last_cal_mod[probe_index] = cal_mod;
        xe_np_probe_state.last_sync[probe_index] = sync;
        xe_np_probe_state.last_rec_mod[probe_index] = rec_mod;
        xe_np_probe_state.last_op_mode[probe_index] = op_mode;

        if(cal_mod != config->cal_mod)
        {
            verify_flags |= XE_NP_APP_PROBE_VERIFY_FLAG_CAL_MOD_MISMATCH;
        }
        if(sync != config->sync_value)
        {
            verify_flags |= XE_NP_APP_PROBE_VERIFY_FLAG_SYNC_MISMATCH;
        }
        if(rec_mod != config->rec_mod_run)
        {
            verify_flags |= XE_NP_APP_PROBE_VERIFY_FLAG_REC_MOD_MISMATCH;
        }
        if(op_mode != config->op_mode_run)
        {
            verify_flags |= XE_NP_APP_PROBE_VERIFY_FLAG_OP_MODE_MISMATCH;
        }

        xe_np_probe_state.last_verify_flags[probe_index] = verify_flags;
        xe_np_probe_last_status_retained[probe_index] = status;
        xe_np_probe_last_verify_flags_retained[probe_index] = verify_flags;
        xe_np_probe_last_cal_mod_retained[probe_index] = cal_mod;
        xe_np_probe_last_sync_retained[probe_index] = sync;
        xe_np_probe_last_rec_mod_retained[probe_index] = rec_mod;
        xe_np_probe_last_op_mode_retained[probe_index] = op_mode;
        xe_np_probe_last_verify_valid_mask |= probe_mask;
        if(verify_flags != 0U)
        {
            return 0;
        }
    }

    return 1;
}

static int XE_NP_App_AllActiveCapturePortsLocked(const XE_NP_Runtime *runtime)
{
    const XE_NP_AppDmaPort *ports[2] = {&xe_np_dma_gpioe, &xe_np_dma_gpiod};
    uint8_t saw_active_port = 0U;

    if(runtime == NULL)
    {
        return 0;
    }

#if (XE_NP_APP_EXPERIMENT_CAPTURE_SUPPRESS_DMA_ENABLE != 0U)
    /* No capture producer exists in the USB transport-only control. */
    return 1;
#endif

#if (XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_INTERLEAVE_ENABLE != 0U)
    if((runtime->capture.flags & XE_NP_CAPTURE_FLAG_GPIOE_ACTIVE) != 0U)
    {
        return (xe_np_quad_dma_active != 0U) ? 1 : 0;
    }
#endif

#if ((XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_ENABLE != 0U) && \
     (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_PUBLISH_ENABLE != 0U) && \
     (XE_NP_APP_CAPTURE_SYNC_WATCHDOG_ENABLE != 0U))
    if(((runtime->capture.flags &
         (XE_NP_CAPTURE_FLAG_GPIOE_ACTIVE | XE_NP_CAPTURE_FLAG_GPIOD_ACTIVE)) ==
        (XE_NP_CAPTURE_FLAG_GPIOE_ACTIVE | XE_NP_CAPTURE_FLAG_GPIOD_ACTIVE)) &&
       ((((runtime->capture.active_probe_mask & XE_NP_CAPTURE_ALL_PROBES_MASK) ==
          XE_NP_CAPTURE_PROBE1_MASK) ||
         ((runtime->capture.active_probe_mask & XE_NP_CAPTURE_ALL_PROBES_MASK) ==
          XE_NP_CAPTURE_PROBE2_MASK))))
    {
        return XE_NP_App_InterleavedGpioeCaptureLocked(runtime);
    }
#endif

    for(uint32_t i = 0U; i < 2U; ++i)
    {
        const XE_NP_AppDmaPort *port = ports[i];

        if(port->active == 0U)
        {
            continue;
        }
        saw_active_port = 1U;

        /*
         * Runtime decode stays offline-first, but startup still needs a real
         * PSB qualification step so weak GPIO/timer candidates do not enter the
         * normal streaming state. Require the live sync watchdog to establish
         * alignment and produce clean windows before bring-up advances.
         */
        if((port->capture_port == NULL) ||
           (port->capture_port->dma_windows_captured == 0U) ||
           (port->capture_port->total_bytes_captured == 0ULL))
        {
            return 0;
        }
#if (XE_NP_APP_CAPTURE_SYNC_WATCHDOG_ENABLE != 0U)
        if((port->capture_port->frame_alignment_locked == 0U) ||
           ((port->capture_port->validated_probe_mask & port->capture_port->active_probe_mask) !=
            port->capture_port->active_probe_mask) ||
           (port->validation_error_seen != 0U) ||
           (port->sync_watchdog_ok_count < XE_NP_App_RequiredSyncWatchdogOkCount(port)))
        {
            return 0;
        }
#endif
    }

    return (saw_active_port != 0U) ? 1 : 0;
}

static int XE_NP_App_FindNextActiveProbe(
    uint8_t active_probe_mask,
    uint8_t start_probe_index,
    uint8_t *probe_index_out)
{
    if(probe_index_out == NULL)
    {
        return -1;
    }

    for(uint8_t probe_index = start_probe_index; probe_index < XE_NP_NEUROPIXELS_MAX_PROBES;
        ++probe_index)
    {
        if((active_probe_mask & (uint8_t)(1U << probe_index)) != 0U)
        {
            *probe_index_out = probe_index;
            return 0;
        }
    }

    *probe_index_out = 0U;
    return -1;
}

static uint64_t XE_NP_App_ReadCapturedBytesAtomic(const XE_NP_CapturePort *capture_port)
{
    uint64_t total_bytes = 0ULL;
    uint32_t primask;

    if(capture_port == NULL)
    {
        return 0ULL;
    }

    /*
     * total_bytes_captured is updated from DMA ISR context on a 32-bit MCU.
     * Take the snapshot with IRQs masked so the foreground rate monitor
     * cannot see a torn 64-bit value and report a false cadence fault.
     */
    primask = XE_NP_App_CriticalSection_Enter();
    total_bytes = capture_port->total_bytes_captured;
    XE_NP_App_CriticalSection_Exit(primask);
    return total_bytes;
}

static void XE_NP_App_UpdateCaptureRateMonitor(XE_NP_AppDmaPort *port)
{
    uint32_t now_ms;
    uint32_t dt_ms;
    uint64_t total_bytes;
    uint64_t bytes_delta;
    uint64_t expected_bytes;
    uint64_t lower_bound;
    uint64_t upper_bound;

    if((port == NULL) || (port->active == 0U) || (port->capture_port == NULL) ||
       (port->capture_port->raw_bytes_per_sec == 0U))
    {
        return;
    }

#if (XE_NP_APP_EXPERIMENT_DISABLE_CAPTURE_RATE_MONITOR != 0U)
    port->rate_bad_samples = 0U;
    port->rate_ratio_pct2 = 0U;
    return;
#endif

    now_ms = HAL_GetTick();
    total_bytes = XE_NP_App_ReadCapturedBytesAtomic(port->capture_port);
    if(port->last_rate_sample_tick_ms == 0U)
    {
        port->last_rate_sample_tick_ms = now_ms;
        port->last_rate_sample_bytes = total_bytes;
        return;
    }

    dt_ms = (uint32_t)(now_ms - port->last_rate_sample_tick_ms);
    if(dt_ms < XE_NP_APP_RATE_MONITOR_INTERVAL_MS)
    {
        return;
    }

    bytes_delta = total_bytes - port->last_rate_sample_bytes;
    port->last_rate_sample_tick_ms = now_ms;
    port->last_rate_sample_bytes = total_bytes;

    expected_bytes = 
        ((uint64_t)port->capture_port->raw_bytes_per_sec * (uint64_t)dt_ms + 500ULL) / 1000ULL;
    if(expected_bytes != 0ULL)
    {
        uint64_t ratio_pct2 =
            ((bytes_delta * 50ULL) + (expected_bytes / 2ULL)) / expected_bytes;
        if(ratio_pct2 > 63ULL)
        {
            ratio_pct2 = 63ULL;
        }
        port->rate_ratio_pct2 = (uint8_t)ratio_pct2;
    }
    else
    {
        port->rate_ratio_pct2 = 0U;
    }
    lower_bound = (expected_bytes * (100U - XE_NP_APP_RATE_MONITOR_TOLERANCE_PCT)) / 100U;
    upper_bound = (expected_bytes * (100U + XE_NP_APP_RATE_MONITOR_TOLERANCE_PCT)) / 100U;

    if((bytes_delta < lower_bound) || (bytes_delta > upper_bound))
    {
        if(port->rate_bad_samples < 0xFFU)
        {
            port->rate_bad_samples++;
        }
        if((port->rate_bad_samples >= XE_NP_APP_RATE_MONITOR_BAD_SAMPLE_LIMIT) &&
           (port->rate_error_seen == 0U))
        {
            port->rate_error_seen = 1U;
            port->capture_port->raw_mgr.logState |= MGR_STATE_TRANSERR;
            CE32_ReportError(&xe_np_runtime.sys_param, CE32_ERRORID_CORE_ERROR);
        }
    }
    else
    {
        port->rate_bad_samples = 0U;
    }
}

static void XE_NP_App_RefreshDmaStatus(void)
{
    uint32_t flag_mask = XE_NP_RUNTIME_FLAG_NP_CAPTURE_DMA_ACTIVE |
                         XE_NP_RUNTIME_FLAG_NP_CAPTURE_SYNC_LOCKED |
                         XE_NP_RUNTIME_FLAG_NP_CAPTURE_VALIDATION_ERROR |
                         XE_NP_RUNTIME_FLAG_NP_CAPTURE_DMA_ERROR |
                         XE_NP_RUNTIME_FLAG_NP_CAPTURE_BACKLOG_ACTIVE |
                         XE_NP_RUNTIME_FLAG_NP_CAPTURE_OVERRUN;
    uint32_t status_mask = XE_NP_SYSTEM_STATUS_NP_CAPTURE_DMA_ACTIVE |
                           XE_NP_SYSTEM_STATUS_NP_CAPTURE_SYNC_LOCKED |
                           XE_NP_SYSTEM_STATUS_NP_CAPTURE_VALIDATION_ERROR |
                           XE_NP_SYSTEM_STATUS_NP_CAPTURE_DMA_ERROR |
                           XE_NP_SYSTEM_STATUS_NP_CAPTURE_BACKLOG_ACTIVE |
                           XE_NP_SYSTEM_STATUS_NP_CAPTURE_OVERRUN;
    uint32_t set_flags = 0U;
    uint32_t set_status = 0U;
    uint8_t dma_active = 0U;
    uint8_t sync_locked = 1U;
    uint8_t saw_active_port = 0U;
    uint8_t validation_error = 0U;
    uint8_t backlog_active = 0U;
    uint8_t capture_overrun = 0U;
    uint8_t dma_error = xe_np_dma_start_error_seen;
    XE_NP_AppDmaPort *ports[2] = {&xe_np_dma_gpioe, &xe_np_dma_gpiod};

#if (XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_INTERLEAVE_ENABLE != 0U)
    /*
     * The four-lane stability bench does not publish completion windows or
     * enter the foreground decoder.  Keep the runtime state truthful without
     * adding any IRQ work to its request-paced DMA channels.
     */
    if(xe_np_quad_dma_active != 0U)
    {
        dma_active = 1U;
        sync_locked = 1U;
        saw_active_port = 1U;
    }
    if(xe_np_quad_dma_error_seen != 0U)
    {
        dma_error = 1U;
    }
#endif

    for(uint32_t i = 0U; i < 2U; ++i)
    {
        XE_NP_AppDmaPort *port = ports[i];

        if(port->active != 0U)
        {
            dma_active = 1U;
            saw_active_port = 1U;
            if((port->capture_port == NULL) ||
               (port->capture_port->dma_windows_captured == 0U) ||
               (port->capture_port->total_bytes_captured == 0ULL))
            {
                sync_locked = 0U;
            }
#if (XE_NP_APP_CAPTURE_SYNC_WATCHDOG_ENABLE != 0U)
            /*
             * In the raw-only build, "sync locked" should mean more than
             * "bytes are moving". Require at least one successful live
             * watchdog validation so the status bit proves the PSB timing and
             * GPIO sampling are coherent on the MCU side.
             */
            else if((port->capture_port->frame_alignment_locked == 0U) ||
                    ((port->capture_port->validated_probe_mask &
                      port->capture_port->active_probe_mask) !=
                     port->capture_port->active_probe_mask) ||
                    (port->sync_watchdog_ok_count <
                     XE_NP_App_RequiredSyncWatchdogOkCount(port)) ||
                    (port->validation_error_seen != 0U))
            {
                sync_locked = 0U;
            }
#endif
        }
        if(port->validation_error_seen != 0U)
        {
            validation_error = 1U;
        }
        if(port->dma_error_seen != 0U)
        {
            dma_error = 1U;
        }
        if(port->rate_error_seen != 0U)
        {
            dma_error = 1U;
        }
        if((port->pending_read_idx != port->pending_write_idx) ||
           (port->backlog_resync_requested != 0U) ||
           (XE_NP_App_PortHasRecorderBacklogPressure(port) != 0))
        {
            backlog_active = 1U;
        }
        if((port->capture_port != NULL) &&
           ((port->capture_port->raw_mgr.logState & MGR_STATE_TRANSOVR) != 0U))
        {
            capture_overrun = 1U;
        }
    }

    if(dma_active != 0U)
    {
        set_flags |= XE_NP_RUNTIME_FLAG_NP_CAPTURE_DMA_ACTIVE;
        set_status |= XE_NP_SYSTEM_STATUS_NP_CAPTURE_DMA_ACTIVE;
    }
    if((saw_active_port != 0U) && (sync_locked != 0U))
    {
        set_flags |= XE_NP_RUNTIME_FLAG_NP_CAPTURE_SYNC_LOCKED;
        set_status |= XE_NP_SYSTEM_STATUS_NP_CAPTURE_SYNC_LOCKED;
    }
    if(validation_error != 0U)
    {
        set_flags |= XE_NP_RUNTIME_FLAG_NP_CAPTURE_VALIDATION_ERROR;
        set_status |= XE_NP_SYSTEM_STATUS_NP_CAPTURE_VALIDATION_ERROR;
    }
    if(dma_error != 0U)
    {
        set_flags |= XE_NP_RUNTIME_FLAG_NP_CAPTURE_DMA_ERROR;
        set_status |= XE_NP_SYSTEM_STATUS_NP_CAPTURE_DMA_ERROR;
    }
    if(backlog_active != 0U)
    {
        set_flags |= XE_NP_RUNTIME_FLAG_NP_CAPTURE_BACKLOG_ACTIVE;
        set_status |= XE_NP_SYSTEM_STATUS_NP_CAPTURE_BACKLOG_ACTIVE;
    }
    if(capture_overrun != 0U)
    {
        set_flags |= XE_NP_RUNTIME_FLAG_NP_CAPTURE_OVERRUN;
        set_status |= XE_NP_SYSTEM_STATUS_NP_CAPTURE_OVERRUN;
    }

    xe_np_runtime.flags = (xe_np_runtime.flags & ~flag_mask) | set_flags;
    xe_np_runtime.sys_param.system_status =
        (xe_np_runtime.sys_param.system_status & ~status_mask) | set_status;
    XE_NP_App_RefreshCaptureDiag(&xe_np_runtime.sys_param);
}

static void XE_NP_App_DisableCapturePortFromIsr(XE_NP_AppDmaPort *port)
{
    if(port == NULL)
    {
        return;
    }

    if(port->htim != NULL)
    {
        CLEAR_BIT(port->htim->Instance->DIER, TIM_DIER_UDE);
        __HAL_TIM_DISABLE(port->htim);
        __HAL_TIM_CLEAR_FLAG(port->htim, TIM_FLAG_UPDATE);
    }

    if(port->hdma.Instance != NULL)
    {
        port->hdma.Instance->CCR &= ~XE_NP_APP_DMA_DISABLE_IT_MASK;
    }

    port->active = 0U;
}

static void XE_NP_App_ResetFirstBacklogResyncSnapshot(XE_NP_AppDmaPort *port)
{
    if(port == NULL)
    {
        return;
    }

    port->first_backlog_resync_tick_ms = 0U;
    port->first_backlog_total_bytes_low = 0U;
    port->first_backlog_last_queued_offset_bytes = 0U;
    port->first_backlog_last_dequeued_offset_bytes = 0U;
    port->first_backlog_resync_valid = 0U;
    port->first_backlog_reason = 0U;
    port->first_backlog_pending_read_idx = 0U;
    port->first_backlog_pending_write_idx = 0U;
    port->first_backlog_current_depth = 0U;
    port->first_backlog_max_pending_windows = 0U;
    port->first_backlog_usb_active_window_valid = 0U;
    port->first_backlog_stable_window_valid = 0U;
}

static void XE_NP_App_RequestBacklogResyncFromIsr(
    XE_NP_AppDmaPort *port,
    uint8_t reason,
    uint8_t current_depth,
    uint8_t max_pending_windows)
{
    if((port == NULL) || (port->capture_port == NULL))
    {
        return;
    }

    /*
     * Mirror the CE64 preview policy: if the secondary consumer falls behind,
     * drop stale diagnostic work and keep the producer path alive.
     */
    if(port->first_backlog_resync_valid == 0U)
    {
        port->first_backlog_resync_tick_ms = HAL_GetTick();
        port->first_backlog_total_bytes_low =
            (uint32_t)port->capture_port->total_bytes_captured;
        port->first_backlog_last_queued_offset_bytes = port->last_queued_offset_bytes;
        port->first_backlog_last_dequeued_offset_bytes = port->last_dequeued_offset_bytes;
        port->first_backlog_resync_valid = 1U;
        port->first_backlog_reason = reason;
        port->first_backlog_pending_read_idx = port->pending_read_idx;
        port->first_backlog_pending_write_idx = port->pending_write_idx;
        port->first_backlog_current_depth = current_depth;
        port->first_backlog_max_pending_windows = max_pending_windows;
        port->first_backlog_usb_active_window_valid = port->usb_active_window_valid;
        port->first_backlog_stable_window_valid = port->stable_window_valid;
    }

    port->backlog_error_seen = 1U;
    port->backlog_resync_requested = 1U;
    port->pending_overflow_count++;
    port->capture_port->raw_mgr.logState |= MGR_STATE_TRANSERR;
    port->capture_port->frame_alignment_locked = 0U;
    port->capture_port->frame_alignment_ticks = 0U;
    port->capture_port->validated_probe_mask = 0U;
}

static void XE_NP_App_RecordDmaFaultSnapshot(
    XE_NP_AppDmaPort *port,
    uint32_t source,
    uint32_t csr,
    uint32_t ccr,
    uint32_t error_code)
{
    DMA_Channel_TypeDef *dma;

    if((port == NULL) || (port->hdma.Instance == NULL) ||
       (source == 0U) || (port->dma_fault_source != 0U))
    {
        return;
    }

    dma = port->hdma.Instance;
    port->dma_fault_tick_ms = HAL_GetTick();
    port->dma_fault_source = source;
    port->dma_fault_csr = csr;
    port->dma_fault_ccr = ccr;
    port->dma_fault_ctr1 = dma->CTR1;
    port->dma_fault_ctr2 = dma->CTR2;
    port->dma_fault_cbr1 = dma->CBR1;
    port->dma_fault_csar = dma->CSAR;
    port->dma_fault_cdar = dma->CDAR;
    port->dma_fault_cllr = dma->CLLR;
    port->dma_fault_error_code = error_code;
}

static void XE_NP_App_ReportCaptureFault(
    XE_NP_AppDmaPort *port,
    unsigned long error_code,
    uint8_t validation_error,
    uint8_t dma_error)
{
    if(port != NULL)
    {
        if(validation_error != 0U)
        {
            port->validation_error_seen = 1U;
        }
        if(dma_error != 0U)
        {
            port->dma_error_seen = 1U;
            XE_NP_App_DisableCapturePortFromIsr(port);
        }
        if(port->capture_port != NULL)
        {
            port->capture_port->raw_mgr.logState |= MGR_STATE_TRANSERR;
        }
        if((port->sys_param != NULL) && (XE_NP_App_InIsrContext() == 0))
        {
            CE32_ReportError(port->sys_param, error_code);
        }
        else
        {
            XE_NP_App_PostPendingError(error_code);
        }
    }
    else
    {
        if(XE_NP_App_InIsrContext() == 0)
        {
            CE32_ReportError(&xe_np_runtime.sys_param, error_code);
        }
        else
        {
            XE_NP_App_PostPendingError(error_code);
        }
    }

    if(XE_NP_App_InIsrContext() == 0)
    {
        XE_NP_App_RefreshDmaStatus();
    }
}

static void XE_NP_AppDma_IrqHandlerFast(XE_NP_AppDmaPort *port)
{
    DMA_HandleTypeDef *hdma;
    DMA_Channel_TypeDef *dma;
    uint32_t csr;
    uint32_t ccr;
    uint32_t clear_flags = 0U;
    uint32_t error_code = HAL_DMA_ERROR_NONE;

    if((port == NULL) || (port->hdma.Instance == NULL))
    {
        return;
    }

    hdma = &port->hdma;
    dma = hdma->Instance;
    csr = dma->CSR;
    if(csr == 0U)
    {
        return;
    }

    if((csr & DMA_CSR_IDLEF) != 0U)
    {
        dma->CFCR = DMA_FLAG_IDLE;
        csr &= ~DMA_CSR_IDLEF;
        if(csr == 0U)
        {
            return;
        }
    }

    if(((csr & XE_NP_APP_DMA_FAST_FALLBACK_FLAGS) != 0U) ||
#if (XE_NP_APP_EXPERIMENT_DMA_MINIMAL_IRQ_DISPATCH_ENABLE == 0U)
#if (XE_NP_APP_CAPTURE_DMA_HT_IRQ_ENABLE != 0U)
       (hdma->XferHalfCpltCallback == NULL) ||
#endif
       (hdma->XferCpltCallback == NULL) ||
#endif
       (hdma->XferErrorCallback == NULL))
    {
        XE_NP_App_RecordDmaFaultSnapshot(
            port,
            XE_NP_APP_DMA_FAULT_SOURCE_FAST_FALLBACK,
            csr,
            dma->CCR,
            hdma->ErrorCode);
        HAL_DMA_IRQHandler(hdma);
        return;
    }

    ccr = dma->CCR;

    if(((csr & DMA_CSR_DTEF) != 0U) && ((ccr & DMA_IT_DTE) != 0U))
    {
        clear_flags |= DMA_FLAG_DTE;
        error_code |= HAL_DMA_ERROR_DTE;
    }
    if(((csr & DMA_CSR_ULEF) != 0U) && ((ccr & DMA_IT_ULE) != 0U))
    {
        clear_flags |= DMA_FLAG_ULE;
        error_code |= HAL_DMA_ERROR_ULE;
    }
    if(((csr & DMA_CSR_USEF) != 0U) && ((ccr & DMA_IT_USE) != 0U))
    {
        clear_flags |= DMA_FLAG_USE;
        error_code |= HAL_DMA_ERROR_USE;
    }
    if(((csr & DMA_CSR_TOF) != 0U) && ((ccr & DMA_IT_TO) != 0U))
    {
        clear_flags |= DMA_FLAG_TO;
        error_code |= HAL_DMA_ERROR_TO;
    }

    /*
     * Neuropixels capture is dominated by periodic HT/TC events. Handle that
     * path directly to avoid the extra bookkeeping in HAL_DMA_IRQHandler().
     * Fall back to HAL above for rare states we do not expect here.
     */
    if(error_code != HAL_DMA_ERROR_NONE)
    {
        XE_NP_App_RecordDmaFaultSnapshot(
            port,
            XE_NP_APP_DMA_FAULT_SOURCE_FAST_ERROR,
            csr,
            ccr,
            error_code);
        clear_flags |= (csr & XE_NP_APP_DMA_FAST_EVENT_FLAGS);
        dma->CFCR = clear_flags;
        hdma->ErrorCode |= error_code;
        dma->CCR |= DMA_CCR_RESET;
        hdma->State = HAL_DMA_STATE_READY;
        if(((hdma->Mode & DMA_LINKEDLIST) == DMA_LINKEDLIST) &&
           (hdma->LinkedListQueue != NULL))
        {
            hdma->LinkedListQueue->State = HAL_DMA_QUEUE_STATE_READY;
        }
        hdma->XferErrorCallback(hdma);
        return;
    }

#if (XE_NP_APP_CAPTURE_DMA_HT_IRQ_ENABLE != 0U)
    if(((csr & DMA_CSR_HTF) != 0U) && ((ccr & DMA_IT_HT) != 0U))
    {
        dma->CFCR = DMA_FLAG_HT;
#if (XE_NP_APP_EXPERIMENT_DMA_MINIMAL_IRQ_DISPATCH_ENABLE != 0U)
        XE_NP_AppDma_HandleHalfTransferFromIsr(port);
#else
        hdma->XferHalfCpltCallback(hdma);
#endif
    }
#endif

    if(((csr & DMA_CSR_TCF) != 0U) && ((ccr & DMA_IT_TC) != 0U))
    {
        dma->CFCR = (DMA_FLAG_TC | DMA_FLAG_HT);
#if (XE_NP_APP_EXPERIMENT_DMA_MINIMAL_IRQ_DISPATCH_ENABLE != 0U)
        XE_NP_AppDma_HandleTransferCompleteFromIsr(port);
#else
        hdma->XferCpltCallback(hdma);
#endif
    }
}

static void XE_NP_App_RecordCapturedBytesFromIsr(XE_NP_AppDmaPort *port, uint32_t region_bytes)
{
    if((port == NULL) || (port->capture == NULL) || (region_bytes == 0U))
    {
        return;
    }

    if(port->port_index == 0U)
    {
        XE_NP_Capture_RecordGpioeBytes(port->capture, region_bytes);
    }
    else
    {
        XE_NP_Capture_RecordGpiodBytes(port->capture, region_bytes);
    }
}

static uint8_t XE_NP_App_GetPendingWindowDepth(const XE_NP_AppDmaPort *port)
{
    if(port == NULL)
    {
        return 0U;
    }

    return (uint8_t)((port->pending_write_idx - port->pending_read_idx) &
                     XE_NP_APP_DMA_WINDOW_QUEUE_MASK);
}

static uint8_t XE_NP_App_GetMaxPendingWindows(const XE_NP_AppDmaPort *port)
{
    uint16_t max_pending_windows;
    uint8_t logical_node_count;

    if(port == NULL)
    {
        return 0U;
    }

    logical_node_count =
        (port->dma_queue_node_count != 0U) ? port->dma_queue_node_count : port->node_count;
    if(XE_NP_App_UsesGroupedRepeatCapture(port) != 0)
    {
        /*
         * A single 2D descriptor represents several independently completed
         * physical blocks.  USB queues those blocks, not descriptor objects.
         * Keep two blocks outside the queue: one can be live under GPDMA while
         * one is being copied into the private USB DMA buffer.
         */
        uint8_t repeated_blocks = port->dma_repeat_block_count;
        logical_node_count = (repeated_blocks > 2U) ? (uint8_t)(repeated_blocks - 2U) : 1U;
    }
    if(XE_NP_App_UsesFullNodeWindowPublication(port) != 0)
    {
        uint8_t unique_windows =
            (port->usb_packet_window_count != 0U) ? port->usb_packet_window_count : logical_node_count;
        /*
         * In shared headroom mode, one physical window is always being filled
         * by DMA and additional windows may already be owned by USB or the
         * TC-stability defer path. Keep one extra spare physical window beyond
         * those direct owners so backlog resync can drop queued references
         * before the live DMA writer wraps immediately into an in-flight USB
         * payload window.
         */
        uint8_t reserved_windows =
            (uint8_t)(2U +
                      (port->usb_active_window_valid != 0U ? 1U : 0U) +
                      (port->stable_window_valid != 0U ? 1U : 0U));

        max_pending_windows =
            (unique_windows > reserved_windows) ? (uint16_t)(unique_windows - reserved_windows) : 0U;
    }
    else
    {
#if (XE_NP_APP_EXPERIMENT_HALF_WINDOW_QUEUE_RESERVE_ENABLE != 0U)
        uint16_t unique_windows = (uint16_t)logical_node_count * 2U;
        uint8_t reserved_windows =
            (uint8_t)(2U +
                      (port->usb_active_window_valid != 0U ? 1U : 0U) +
                      (port->stable_window_valid != 0U ? 1U : 0U));

        max_pending_windows =
            (unique_windows > reserved_windows) ? (uint16_t)(unique_windows - reserved_windows) : 0U;
#else
        max_pending_windows = (XE_NP_App_UsesGroupedRepeatCapture(port) != 0) ?
            logical_node_count : (uint16_t)logical_node_count * 2U;
#endif
    }
    if(max_pending_windows > XE_NP_APP_DMA_WINDOW_QUEUE_MASK)
    {
        max_pending_windows = XE_NP_APP_DMA_WINDOW_QUEUE_MASK;
    }

    return (uint8_t)max_pending_windows;
}

static uint8_t XE_NP_App_SelectDmaServiceBudget(const XE_NP_AppDmaPort *port)
{
    uint8_t pending;
    uint8_t budget;
    uint8_t max_pending_windows;

    pending = XE_NP_App_GetPendingWindowDepth(port);
    if(pending == 0U)
    {
        return 0U;
    }

    budget = XE_NP_APP_DMA_WINDOWS_PER_SERVICE;
    max_pending_windows = XE_NP_App_GetMaxPendingWindows(port);
    if((xe_np_raw_sd_sink.active == 0U) ||
       (port->backlog_resync_requested != 0U) ||
       (port->validation_error_seen != 0U) ||
       ((port->capture_port != NULL) && (port->capture_port->frame_alignment_locked == 0U)))
    {
        budget = pending;
    }
    else if((max_pending_windows != 0U) && (pending > (max_pending_windows / 2U)))
    {
        /*
         * The queue is bounded to the DMA ring's unique half-windows. If it is
         * already more than half full, drain it in one pass instead of leaving
         * easy backlog behind and forcing avoidable resync later.
         */
        budget = pending;
    }

    if((max_pending_windows != 0U) && (budget > max_pending_windows))
    {
        budget = max_pending_windows;
    }

    return budget;
}

static void XE_NP_App_ServiceWrapProbePort(XE_NP_AppDmaPort *port)
{
    uint64_t tail_total_bytes_end;
    uint64_t head_total_bytes_end;
    uint32_t storage_bytes;
    uint32_t side_storage_bytes;
    uint32_t tail_offset;
    uint32_t head_offset;
    uint32_t primask;
    uint8_t *storage;
    uint8_t bytes_per_tick;

    if((port == NULL) || (port->usb_packet_headroom_enabled != 0U) ||
       (port->capture_port == NULL) ||
       (port->capture_port->storage == NULL) ||
       ((port->capture_port->bytes_per_tick != sizeof(uint8_t)) &&
        (port->capture_port->bytes_per_tick != sizeof(uint16_t))))
    {
        return;
    }

    bytes_per_tick = (uint8_t)port->capture_port->bytes_per_tick;
    side_storage_bytes = XE_NP_APP_CAPTURE_WRAP_PROBE_SIDE_BYTES * (uint32_t)bytes_per_tick;
    if(port->capture_port->storage_bytes < side_storage_bytes)
    {
        return;
    }

    primask = XE_NP_App_CriticalSection_Enter();
    if(port->wrap_probe_ready == 0U)
    {
        XE_NP_App_CriticalSection_Exit(primask);
        return;
    }

    port->wrap_probe_ready = 0U;
    tail_total_bytes_end = port->wrap_probe_tail_total_bytes_end;
    head_total_bytes_end = port->wrap_probe_head_total_bytes_end;
    tail_offset = port->wrap_probe_tail_offset;
    head_offset = port->wrap_probe_head_offset;
    XE_NP_App_CriticalSection_Exit(primask);

    storage = port->capture_port->storage;
    storage_bytes = port->capture_port->storage_bytes;

    if((head_offset == 0U) && ((tail_offset + side_storage_bytes) == storage_bytes))
    {
        XE_NP_Capture_DCacheInvalidate(
            &storage[tail_offset],
            side_storage_bytes);
        XE_NP_Capture_DCacheInvalidate(
            storage,
            side_storage_bytes);

        if(bytes_per_tick == sizeof(uint8_t))
        {
            memcpy(
                &port->wrap_probe_bytes[0],
                &storage[tail_offset],
                XE_NP_APP_CAPTURE_WRAP_PROBE_SIDE_BYTES);
            memcpy(
                &port->wrap_probe_bytes[XE_NP_APP_CAPTURE_WRAP_PROBE_SIDE_BYTES],
                storage,
                XE_NP_APP_CAPTURE_WRAP_PROBE_SIDE_BYTES);
        }
        else
        {
            for(uint32_t i = 0U; i < XE_NP_APP_CAPTURE_WRAP_PROBE_SIDE_BYTES; ++i)
            {
                port->wrap_probe_bytes[i] = storage[tail_offset + (i * sizeof(uint16_t)) + 1U];
                port->wrap_probe_bytes[XE_NP_APP_CAPTURE_WRAP_PROBE_SIDE_BYTES + i] =
                    storage[(i * sizeof(uint16_t)) + 1U];
            }
        }
    }
    else if((head_offset == (tail_offset + side_storage_bytes)) &&
            ((head_offset + side_storage_bytes) <= storage_bytes))
    {
        XE_NP_Capture_DCacheInvalidate(
            &storage[tail_offset],
            side_storage_bytes * 2U);

        if(bytes_per_tick == sizeof(uint8_t))
        {
            memcpy(
                &port->wrap_probe_bytes[0],
                &storage[tail_offset],
                XE_NP_APP_CAPTURE_WRAP_PROBE_SIDE_BYTES);
            memcpy(
                &port->wrap_probe_bytes[XE_NP_APP_CAPTURE_WRAP_PROBE_SIDE_BYTES],
                &storage[head_offset],
                XE_NP_APP_CAPTURE_WRAP_PROBE_SIDE_BYTES);
        }
        else
        {
            for(uint32_t i = 0U; i < XE_NP_APP_CAPTURE_WRAP_PROBE_SIDE_BYTES; ++i)
            {
                port->wrap_probe_bytes[i] = storage[tail_offset + (i * sizeof(uint16_t)) + 1U];
                port->wrap_probe_bytes[XE_NP_APP_CAPTURE_WRAP_PROBE_SIDE_BYTES + i] =
                    storage[head_offset + (i * sizeof(uint16_t)) + 1U];
            }
        }
    }
    else
    {
        return;
    }

    port->wrap_probe_tail_total_bytes_end = tail_total_bytes_end;
    port->wrap_probe_head_total_bytes_end = head_total_bytes_end;
    port->wrap_probe_count++;
    port->wrap_probe_valid = 1U;
}

static int XE_NP_App_CaptureWrapProbeImmediateFromIsr(
    XE_NP_AppDmaPort *port,
    uint32_t tail_offset,
    uint32_t head_offset,
    uint64_t tail_total_bytes_end,
    uint64_t head_total_bytes_end)
{
    uint8_t *storage;
    uint32_t storage_bytes;
    uint32_t side_storage_bytes;
    uint8_t bytes_per_tick;

    if((port == NULL) || (port->capture_port == NULL) || (port->capture_port->storage == NULL) ||
       ((port->capture_port->bytes_per_tick != sizeof(uint8_t)) &&
        (port->capture_port->bytes_per_tick != sizeof(uint16_t))))
    {
        return 0;
    }

    storage = port->capture_port->storage;
    storage_bytes = port->capture_port->storage_bytes;
    bytes_per_tick = (uint8_t)port->capture_port->bytes_per_tick;
    side_storage_bytes = XE_NP_APP_CAPTURE_WRAP_PROBE_SIDE_BYTES * (uint32_t)bytes_per_tick;
    if((storage_bytes < side_storage_bytes) ||
       ((tail_offset + side_storage_bytes) > storage_bytes) ||
       ((head_offset + side_storage_bytes) > storage_bytes))
    {
        return 0;
    }

    if((head_offset == 0U) && ((tail_offset + side_storage_bytes) == storage_bytes))
    {
        XE_NP_Capture_DCacheInvalidate(&storage[tail_offset], side_storage_bytes);
        XE_NP_Capture_DCacheInvalidate(storage, side_storage_bytes);
    }
    else if((head_offset == (tail_offset + side_storage_bytes)) &&
            ((head_offset + side_storage_bytes) <= storage_bytes))
    {
        XE_NP_Capture_DCacheInvalidate(&storage[tail_offset], side_storage_bytes * 2U);
    }
    else
    {
        return 0;
    }

    if(bytes_per_tick == sizeof(uint8_t))
    {
        memcpy(
            &port->wrap_probe_bytes[0],
            &storage[tail_offset],
            XE_NP_APP_CAPTURE_WRAP_PROBE_SIDE_BYTES);
        memcpy(
            &port->wrap_probe_bytes[XE_NP_APP_CAPTURE_WRAP_PROBE_SIDE_BYTES],
            &storage[head_offset],
            XE_NP_APP_CAPTURE_WRAP_PROBE_SIDE_BYTES);
    }
    else
    {
        for(uint32_t i = 0U; i < XE_NP_APP_CAPTURE_WRAP_PROBE_SIDE_BYTES; ++i)
        {
            port->wrap_probe_bytes[i] = storage[tail_offset + (i * sizeof(uint16_t)) + 1U];
            port->wrap_probe_bytes[XE_NP_APP_CAPTURE_WRAP_PROBE_SIDE_BYTES + i] =
                storage[head_offset + (i * sizeof(uint16_t)) + 1U];
        }
    }

    port->wrap_probe_valid = 1U;
    port->wrap_probe_tail_total_bytes_end = tail_total_bytes_end;
    port->wrap_probe_head_total_bytes_end = head_total_bytes_end;
    port->wrap_probe_tail_offset = tail_offset;
    port->wrap_probe_head_offset = head_offset;
    port->wrap_probe_count++;
    port->wrap_probe_ready = 0U;
    port->wrap_probe_tail_ready = 0U;
    return 1;
}

static void XE_NP_App_ArmWrapProbeFromIsr(
    XE_NP_AppDmaPort *port,
    uint32_t offset_bytes,
    uint64_t total_bytes_end)
{
    uint32_t storage_bytes;
    uint32_t side_storage_bytes;
#if (XE_NP_APP_CAPTURE_HALF_BOUNDARY_PROBE_ENABLE != 0U)
    uint32_t half_boundary_offset;
#endif

    if((port == NULL) || (port->usb_packet_headroom_enabled != 0U) ||
       (port->capture_port == NULL) ||
       (port->capture_port->storage_bytes <
        (XE_NP_APP_CAPTURE_WRAP_PROBE_SIDE_BYTES * port->capture_port->bytes_per_tick)))
    {
        return;
    }

    /*
     * Diagnostic holdoff: preserve the first pending/full wrap probe until the
     * foreground service copies it into the bench struct. At NP2 full rate the
     * ring can wrap again in about 11 ms, which is fast enough to overwrite an
     * unconsumed seam snapshot before SWD can inspect it.
     */
    if((port->wrap_probe_ready != 0U) ||
       (port->wrap_probe_tail_ready != 0U) ||
       (port->wrap_probe_valid != 0U))
    {
        return;
    }

    storage_bytes = port->capture_port->storage_bytes;
    side_storage_bytes =
        XE_NP_APP_CAPTURE_WRAP_PROBE_SIDE_BYTES * port->capture_port->bytes_per_tick;
#if (XE_NP_APP_CAPTURE_HALF_BOUNDARY_PROBE_ENABLE != 0U)
    half_boundary_offset = storage_bytes / 2U;
    if((storage_bytes != 0U) &&
       (((uint32_t)(total_bytes_end % (uint64_t)storage_bytes)) == half_boundary_offset) &&
       (half_boundary_offset >= side_storage_bytes) &&
       ((half_boundary_offset + side_storage_bytes) <= storage_bytes))
    {
        (void)offset_bytes;
#if (XE_NP_APP_EXPERIMENT_HALF_BOUNDARY_PROBE_ISR_SNAPSHOT_ENABLE != 0U)
        if(XE_NP_App_CaptureWrapProbeImmediateFromIsr(
               port,
               half_boundary_offset - side_storage_bytes,
               half_boundary_offset,
               total_bytes_end,
               total_bytes_end) != 0)
        {
            return;
        }
#endif
        port->wrap_probe_valid = 0U;
        port->wrap_probe_tail_total_bytes_end = total_bytes_end;
        port->wrap_probe_head_total_bytes_end = total_bytes_end;
        port->wrap_probe_tail_offset = half_boundary_offset - side_storage_bytes;
        port->wrap_probe_head_offset = half_boundary_offset;
        port->wrap_probe_tail_ready = 0U;
        __DMB();
        port->wrap_probe_ready = 1U;
        return;
    }

    /*
     * Diagnostic-only mode: when half-boundary probing is enabled, preserve the
     * first half-window seam after USB arm instead of letting an earlier full
     * wrap snapshot consume the single probe slot.
     */
    return;
#endif
    if((storage_bytes == 0U) ||
       (((uint32_t)(total_bytes_end % (uint64_t)storage_bytes)) != 0U))
    {
        return;
    }

    port->wrap_probe_valid = 0U;
    port->wrap_probe_tail_total_bytes_end = total_bytes_end;
    port->wrap_probe_tail_offset = storage_bytes - side_storage_bytes;
    port->wrap_probe_tail_ready = 1U;
    port->wrap_probe_ready = 0U;
}

static void XE_NP_App_CompleteWrapProbeFromIsr(
    XE_NP_AppDmaPort *port,
    uint32_t offset_bytes,
    uint64_t total_bytes_end)
{
    if((port == NULL) || (port->usb_packet_headroom_enabled != 0U) ||
       (port->wrap_probe_tail_ready == 0U) || (offset_bytes != 0U))
    {
        return;
    }

    port->wrap_probe_head_total_bytes_end = total_bytes_end;
    port->wrap_probe_head_offset = offset_bytes;
    port->wrap_probe_tail_ready = 0U;
    __DMB();
    port->wrap_probe_ready = 1U;
}

static void XE_NP_App_QueueDmaRegionFromIsr(
    XE_NP_AppDmaPort *port,
    uint8_t node_index,
    uint8_t half_index,
    uint32_t offset_bytes,
    uint32_t region_bytes,
    uint64_t total_bytes_end)
{
    uint8_t write_idx;
    uint8_t read_idx;
    uint8_t next_write_idx;
    uint8_t current_depth;
    uint8_t depth;
    uint8_t max_pending_windows;

#if (XE_NP_APP_CAPTURE_FOREGROUND_QUEUE_ENABLE == 0U)
    if(XE_NP_App_UsbWindowExportArmedForPort(port) == 0)
    {
        return;
    }

    if(port->usb_window_warmup_remaining != 0U)
    {
        port->usb_window_warmup_remaining--;
        return;
    }
#endif

    if((port == NULL) || (port->capture_port == NULL) || (region_bytes == 0U) ||
       ((offset_bytes + region_bytes) > XE_NP_App_GetCaptureStoragePhysicalBytes(port)))
    {
        XE_NP_App_ReportCaptureFault(port, CE32_ERRORID_CORE_ERROR, 0U, 1U);
        return;
    }

    write_idx = port->pending_write_idx;
    read_idx = port->pending_read_idx;
    current_depth = (uint8_t)((write_idx - read_idx) & XE_NP_APP_DMA_WINDOW_QUEUE_MASK);
    max_pending_windows = XE_NP_App_GetMaxPendingWindows(port);
    if((max_pending_windows == 0U) || (current_depth >= max_pending_windows))
    {
        XE_NP_App_RequestBacklogResyncFromIsr(port, 1U, current_depth, max_pending_windows);
        return;
    }

    next_write_idx = (uint8_t)((write_idx + 1U) & XE_NP_APP_DMA_WINDOW_QUEUE_MASK);
    if(next_write_idx == read_idx)
    {
        XE_NP_App_RequestBacklogResyncFromIsr(port, 2U, current_depth, max_pending_windows);
        return;
    }

    port->pending_windows[write_idx].offset_bytes = offset_bytes;
    port->pending_windows[write_idx].size_bytes = region_bytes;
    port->pending_windows[write_idx].node_index = node_index;
    port->pending_windows[write_idx].half_index = half_index;
    port->pending_windows[write_idx].total_bytes_end = total_bytes_end;
    port->last_queued_offset_bytes = offset_bytes;
    port->last_queued_size_bytes = region_bytes;
    if((port->queued_window_count == 0U) || (region_bytes < port->queued_size_min_bytes))
    {
        port->queued_size_min_bytes = region_bytes;
    }
    if(region_bytes > port->queued_size_max_bytes)
    {
        port->queued_size_max_bytes = region_bytes;
    }
    port->queued_window_count++;
    __DMB();
    port->pending_write_idx = next_write_idx;

    depth = (uint8_t)((next_write_idx - read_idx) & XE_NP_APP_DMA_WINDOW_QUEUE_MASK);
    if(depth > port->pending_peak_depth)
    {
        port->pending_peak_depth = depth;
    }
}

static void XE_NP_App_StoreDeferredUsbWindowFromIsr(
    XE_NP_AppDmaPort *port,
    uint8_t node_index,
    uint8_t half_index,
    uint32_t offset_bytes,
    uint32_t region_bytes,
    uint64_t total_bytes_end)
{
    if((port == NULL) || (port->capture_port == NULL) || (region_bytes == 0U) ||
       ((offset_bytes + region_bytes) > XE_NP_App_GetCaptureStoragePhysicalBytes(port)))
    {
        XE_NP_App_ReportCaptureFault(port, CE32_ERRORID_CORE_ERROR, 0U, 1U);
        return;
    }

    port->stable_window.offset_bytes = offset_bytes;
    port->stable_window.size_bytes = region_bytes;
    port->stable_window.node_index = node_index;
    port->stable_window.half_index = half_index;
    port->stable_window.total_bytes_end = total_bytes_end;
    port->stable_window_reserved = 0U;
    __DMB();
    port->stable_window_valid = 1U;
}

static void XE_NP_App_FlushDeferredUsbWindowFromIsr(XE_NP_AppDmaPort *port)
{
    XE_NP_AppDmaWindow window;

    if((port == NULL) || (port->stable_window_valid == 0U))
    {
        return;
    }

    window = port->stable_window;
    port->stable_window_valid = 0U;
    port->stable_window_reserved = 0U;
    __DMB();

    XE_NP_App_QueueDmaRegionFromIsr(
        port,
        window.node_index,
        window.half_index,
        window.offset_bytes,
        window.size_bytes,
        window.total_bytes_end);
}

static int XE_NP_App_DeferredUsbWindowReady(const XE_NP_AppDmaPort *port)
{
    uint32_t dma_addr;
    uint32_t base_addr;
    uint32_t storage_bytes;
    uint32_t dma_offset;

    if((port == NULL) || (port->capture_port == NULL) || (port->capture_port->storage == NULL) ||
       (port->hdma.Instance == NULL))
    {
        return 0;
    }

    storage_bytes = XE_NP_App_GetCaptureStoragePhysicalBytes(port);
    if(storage_bytes == 0U)
    {
        return 0;
    }

    if(port->direct_repeat_mode == 0U)
    {
#if (XE_NP_APP_CAPTURE_DMA_STABLE_WINDOW_DEFER_ENABLE != 0U)
        uint32_t release_bytes = XE_NP_APP_CAPTURE_DMA_STABLE_WINDOW_RELEASE_BYTES;
        uint32_t window_end_offset;
        uint32_t progressed_bytes;

        if((release_bytes == 0U) ||
           (port->stable_window_valid == 0U) ||
           (port->stable_window.size_bytes == 0U))
        {
            return 1;
        }

        dma_addr = port->hdma.Instance->CDAR;
        base_addr = (uint32_t)(void *)port->capture_port->storage;
        if((dma_addr < base_addr) || (dma_addr >= (base_addr + storage_bytes)))
        {
            return 0;
        }

        dma_offset = dma_addr - base_addr;
        window_end_offset = port->stable_window.offset_bytes + port->stable_window.size_bytes;
        if(window_end_offset >= storage_bytes)
        {
            window_end_offset %= storage_bytes;
        }
        progressed_bytes =
            (uint32_t)(((uint64_t)dma_offset + (uint64_t)storage_bytes - (uint64_t)window_end_offset) %
                       (uint64_t)storage_bytes);
        return (progressed_bytes >= release_bytes) ? 1 : 0;
#else
        return 1;
#endif
    }

    dma_addr = port->hdma.Instance->CDAR;
    base_addr = (uint32_t)(void *)port->capture_port->storage;
    if((dma_addr < base_addr) || (dma_addr >= (base_addr + storage_bytes)))
    {
        return 0;
    }

    dma_offset = dma_addr - base_addr;
    return (dma_offset >= XE_NP_APP_CAPTURE_DMA_TC_RELEASE_OFFSET_BYTES) ? 1 : 0;
}

static void XE_NP_App_ServiceDeferredUsbWindow(XE_NP_AppDmaPort *port)
{
    XE_NP_AppDmaWindow window;
    uint32_t primask;

    if((port == NULL) || (port->stable_window_valid == 0U))
    {
        return;
    }

    if(port->stable_window_reserved != 0U)
    {
        return;
    }

    if(XE_NP_App_DeferredUsbWindowReady(port) == 0)
    {
        return;
    }

    primask = XE_NP_App_CriticalSection_Enter();
    if((port->stable_window_valid == 0U) ||
       (port->stable_window_reserved != 0U) ||
       (XE_NP_App_DeferredUsbWindowReady(port) == 0))
    {
        XE_NP_App_CriticalSection_Exit(primask);
        return;
    }

    window = port->stable_window;
    port->stable_window_valid = 0U;
    port->stable_window_reserved = 0U;
    XE_NP_App_CriticalSection_Exit(primask);

    XE_NP_App_QueueDmaRegionFromIsr(
        port,
        window.node_index,
        window.half_index,
        window.offset_bytes,
        window.size_bytes,
        window.total_bytes_end);
}

static void XE_NP_App_PublishStableDmaRegionFromIsr(
    XE_NP_AppDmaPort *port,
    uint8_t node_index,
    uint8_t half_index,
    uint32_t offset_bytes,
    uint32_t region_bytes,
    uint64_t total_bytes_end)
{
#if (XE_NP_APP_CAPTURE_FOREGROUND_QUEUE_ENABLE == 0U)
    if(XE_NP_App_UsbWindowExportArmedForPort(port) == 0)
    {
        if(port != NULL)
        {
            port->stable_window_valid = 0U;
            port->stable_window_reserved = 0U;
        }
        return;
    }
#endif

    if((port == NULL) || (port->capture_port == NULL) || (region_bytes == 0U) ||
       ((offset_bytes + region_bytes) > XE_NP_App_GetCaptureStoragePhysicalBytes(port)))
    {
        XE_NP_App_ReportCaptureFault(port, CE32_ERRORID_CORE_ERROR, 4U, 1U);
        return;
    }

#if (XE_NP_APP_CAPTURE_DMA_STABLE_WINDOW_DEFER_ENABLE == 0U)
    XE_NP_App_QueueDmaRegionFromIsr(
        port,
        node_index,
        half_index,
        offset_bytes,
        region_bytes,
        total_bytes_end);
    port->stable_window_valid = 0U;
    port->stable_window_reserved = 0U;
    return;
#endif

    if(port->stable_window_valid != 0U)
    {
        XE_NP_App_QueueDmaRegionFromIsr(
            port,
            port->stable_window.node_index,
            port->stable_window.half_index,
            port->stable_window.offset_bytes,
            port->stable_window.size_bytes,
            port->stable_window.total_bytes_end);
    }

    port->stable_window.offset_bytes = offset_bytes;
    port->stable_window.size_bytes = region_bytes;
    port->stable_window.node_index = node_index;
    port->stable_window.half_index = half_index;
    port->stable_window.total_bytes_end = total_bytes_end;
    port->stable_window_reserved = 0U;
    __DMB();
    port->stable_window_valid = 1U;
}

static void XE_NP_App_ProcessQueuedDmaRegion(
    XE_NP_AppDmaPort *port,
    const XE_NP_AppDmaWindow *window)
{
    uint8_t *region;
    size_t sample_frames;
    size_t tick_count;
    uint32_t validation_flags;

    if((port == NULL) || (window == NULL) || (port->capture == NULL) ||
       (port->capture_port == NULL) || (window->size_bytes == 0U) ||
       ((window->offset_bytes + window->size_bytes) > XE_NP_App_GetCaptureStoragePhysicalBytes(port)))
    {
        XE_NP_App_ReportCaptureFault(port, CE32_ERRORID_CORE_ERROR, 1U, 0U);
        return;
    }

    if(XE_NP_App_ShouldValidateWindow(port) == 0)
    {
        port->windows_processed++;
        return;
    }

    region = &port->capture_port->storage[window->offset_bytes];
    XE_NP_Capture_DCacheInvalidate(region, window->size_bytes);
    tick_count = window->size_bytes / sizeof(uint16_t);
    sample_frames = XE_NP_App_SelectValidationSampleFrames(port);

    validation_flags = XE_NP_Capture_ValidateSnapshotPrefix(
        port->capture,
        (const uint16_t *)region,
        tick_count,
        port->port_index,
        sample_frames);
    if(validation_flags != 0U)
    {
        XE_NP_App_ResetPortSyncState(port);
        XE_NP_App_ReportCaptureFault(port, CE32_ERRORID_CORE_ERROR, 1U, 0U);
    }
    else
    {
        port->windows_processed++;
        XE_NP_App_TrySampleLiveDecode(port, (const uint16_t *)region, tick_count);
    }
}

static void XE_NP_App_ServiceDmaPort(XE_NP_AppDmaPort *port, uint8_t window_budget)
{
#if (XE_NP_APP_CAPTURE_FOREGROUND_QUEUE_ENABLE == 0U)
    (void)window_budget;
#if ((XE_NP_APP_CAPTURE_DMA_TC_WINDOW_DEFER_ENABLE != 0U) || \
     (XE_NP_APP_CAPTURE_DMA_STABLE_WINDOW_DEFER_ENABLE != 0U))
    XE_NP_App_ServiceDeferredUsbWindow(port);
#endif
    XE_NP_App_ServiceWrapProbePort(port);
#if (XE_NP_APP_CAPTURE_SYNC_WATCHDOG_ENABLE != 0U)
    XE_NP_App_ServiceSyncWatchdogPort(port);
#endif
#if (XE_NP_APP_CAPTURE_RATE_MONITOR_ENABLE != 0U)
    XE_NP_App_UpdateCaptureRateMonitor(port);
#else
    (void)port;
#endif
    return;
#else
    uint8_t windows_processed = 0U;

#if ((XE_NP_APP_CAPTURE_DMA_TC_WINDOW_DEFER_ENABLE != 0U) || \
     (XE_NP_APP_CAPTURE_DMA_STABLE_WINDOW_DEFER_ENABLE != 0U))
    XE_NP_App_ServiceDeferredUsbWindow(port);
#endif
    XE_NP_App_ServiceWrapProbePort(port);

    while((port != NULL) && (windows_processed < window_budget))
    {
        if(port->backlog_resync_requested != 0U)
        {
            uint32_t primask = XE_NP_App_CriticalSection_Enter();
            port->pending_read_idx = port->pending_write_idx;
            port->backlog_resync_requested = 0U;
            XE_NP_App_CriticalSection_Exit(primask);
            XE_NP_App_ResetPortSyncState(port);
        }

        if(port->pending_read_idx == port->pending_write_idx)
        {
            break;
        }

        XE_NP_AppDmaWindow window;
        uint8_t read_idx = port->pending_read_idx;

        window = port->pending_windows[read_idx];
        __DMB();
        port->pending_read_idx = (uint8_t)((read_idx + 1U) & XE_NP_APP_DMA_WINDOW_QUEUE_MASK);
        XE_NP_App_ProcessQueuedDmaRegion(port, &window);
        windows_processed++;
    }

#if (XE_NP_APP_CAPTURE_RATE_MONITOR_ENABLE != 0U)
    XE_NP_App_UpdateCaptureRateMonitor(port);
#else
    (void)port;
#endif
#endif
}

static void XE_NP_App_ResolveDmaWindowFromTotalBytes(
    const XE_NP_AppDmaPort *port,
    uint64_t total_bytes_end,
    uint32_t region_bytes,
    uint32_t *offset_bytes,
    uint8_t *node_index,
    uint8_t *half_index)
{
    uint32_t storage_bytes = 0U;
    uint32_t window_start = 0U;
    uint32_t physical_window_start = 0U;
    uint32_t node_offset = 0U;
    uint32_t logical_node_bytes = 0U;

    if(offset_bytes != NULL)
    {
        *offset_bytes = 0U;
    }
    if(node_index != NULL)
    {
        *node_index = 0U;
    }
    if(half_index != NULL)
    {
        *half_index = 0U;
    }

    if((port == NULL) || (port->capture_port == NULL) || (region_bytes == 0U))
    {
        return;
    }

    storage_bytes = port->capture_port->storage_bytes;
    if(storage_bytes == 0U)
    {
        return;
    }

    /*
     * The single 2D-node path reuses one hardware descriptor across the whole
     * ring. Derive the exported window position from the absolute captured-byte
     * count so the software queue follows the true DMA wrap point instead of a
     * synthetic node index that can drift at full-ring restart.
     */
    window_start = (uint32_t)((total_bytes_end - (uint64_t)region_bytes) % (uint64_t)storage_bytes);
    physical_window_start = XE_NP_App_MapRawOffsetToPhysicalOffset(port, window_start);
    logical_node_bytes = XE_NP_App_GetLogicalDmaNodeSpanBytes(port);
    node_offset = (logical_node_bytes != 0U) ? (window_start % logical_node_bytes) : 0U;

    if(offset_bytes != NULL)
    {
        *offset_bytes = physical_window_start;
    }
    if(node_index != NULL)
    {
        *node_index = (logical_node_bytes != 0U) ? (uint8_t)(window_start / logical_node_bytes) : 0U;
    }
    if(half_index != NULL)
    {
        if((logical_node_bytes != 0U) && (region_bytes != 0U) && (region_bytes < logical_node_bytes))
        {
            *half_index = (uint8_t)(node_offset / region_bytes);
        }
        else
        {
            *half_index = 0U;
        }
    }
}

static uint32_t XE_NP_App_GetCompletedRegionBytes(
    const XE_NP_AppDmaPort *port)
{
    uint32_t logical_node_bytes;

    if((port == NULL) || (port->capture_port == NULL))
    {
        return 0U;
    }

    logical_node_bytes = XE_NP_App_GetLogicalDmaNodeSpanBytes(port);
    if(logical_node_bytes == 0U)
    {
        return 0U;
    }

    /*
     * The one-node 2D circular path has a descriptor spanning the whole ring,
     * but it is configured for a transfer-complete event after each repeated
     * physical block.  Publishing the descriptor span here treated every
     * 61,440-byte TC as a completed 720 KiB ring and made USB reread stale
     * (occasionally still-zero) bytes.  The completed producer region is one
     * repeated block; ResolveDmaWindowFromTotalBytes maps it around the ring.
     */
    if(XE_NP_App_UsesGroupedRepeatCapture(port) != 0)
    {
        /*
         * A single 2D lane node still raises HT at its physical midpoint and
         * TC at its end.  Treating both events as a complete repeated node
         * advances the absolute producer by two full rings per hardware
         * cycle, which makes the paired exporter report a false overrun
         * before it can submit its first USB packet.  Multi-node lanes keep
         * HT disabled and continue to publish one complete node per TC.
         */
#if (XE_NP_APP_CAPTURE_DMA_HT_IRQ_ENABLE != 0U)
        if(port->node_count == 1U)
        {
            return port->node_bytes / 2U;
        }
#endif
        return port->node_bytes;
    }

    if(XE_NP_App_UsesFullNodeWindowPublication(port) != 0)
    {
        return logical_node_bytes;
    }

#if (XE_NP_APP_CAPTURE_DMA_HT_IRQ_ENABLE != 0U)
    return logical_node_bytes / 2U;
#else
    return logical_node_bytes;
#endif
}

static void XE_NP_AppDma_RecordIrqJitterFromIsr(XE_NP_AppDmaPort *port, uint8_t irq_event_type)
{
#if (XE_NP_APP_EXPERIMENT_DMA_IRQ_JITTER_DETECT_ENABLE != 0U)
    uint32_t now;
    uint32_t last_cnt;
    uint32_t prev_delta;
    uint32_t delta;
    uint32_t *last_cnt_ptr;
    uint32_t *prev_delta_ptr;
    uint32_t *delta_min_ptr;
    uint32_t *delta_max_ptr;
    uint32_t *event_count_ptr;
    uint32_t *glitch_count_ptr;

    if((port == NULL) || (port->htim == NULL) || (port->htim->Instance == NULL))
    {
        return;
    }
    if(irq_event_type >= XE_NP_APP_DMA_IRQ_JITTER_EVENT_COUNT)
    {
        return;
    }

    now = port->htim->Instance->CNT;
    if(irq_event_type == XE_NP_APP_DMA_IRQ_JITTER_EVENT_HT)
    {
        last_cnt_ptr = &port->dma_irq_jitter_last_cnt_ht;
        prev_delta_ptr = &port->dma_irq_jitter_prev_delta_ht;
        delta_min_ptr = &port->dma_irq_jitter_delta_min_ht;
        delta_max_ptr = &port->dma_irq_jitter_delta_max_ht;
        event_count_ptr = &port->dma_irq_jitter_event_count_ht;
        glitch_count_ptr = &port->dma_irq_jitter_glitch_count_ht;
    }
    else
    {
        last_cnt_ptr = &port->dma_irq_jitter_last_cnt_tc;
        prev_delta_ptr = &port->dma_irq_jitter_prev_delta_tc;
        delta_min_ptr = &port->dma_irq_jitter_delta_min_tc;
        delta_max_ptr = &port->dma_irq_jitter_delta_max_tc;
        event_count_ptr = &port->dma_irq_jitter_event_count_tc;
        glitch_count_ptr = &port->dma_irq_jitter_glitch_count_tc;
    }

    last_cnt = *last_cnt_ptr;
    if(last_cnt == 0U)
    {
        *last_cnt_ptr = now;
        return;
    }

    delta = now - last_cnt;
    *last_cnt_ptr = now;
    prev_delta = *prev_delta_ptr;
    *prev_delta_ptr = delta;
    if(*event_count_ptr == 0U)
    {
        *delta_min_ptr = delta;
        *delta_max_ptr = delta;
    }
    else
    {
        if(delta < *delta_min_ptr)
        {
            *delta_min_ptr = delta;
        }
        if(delta > *delta_max_ptr)
        {
            *delta_max_ptr = delta;
        }
        if((prev_delta != 0U) &&
           (((delta > prev_delta) ? (delta - prev_delta) : (prev_delta - delta)) > 2U))
        {
            ++(*glitch_count_ptr);
        }
    }
    ++(*event_count_ptr);
#else
    (void)port;
    (void)irq_event_type;
#endif
}

static void XE_NP_AppDma_HandleHalfTransferFromIsr(XE_NP_AppDmaPort *port)
{
    uint32_t offset_bytes;
    uint32_t region_bytes;
    uint8_t node_index;
    uint8_t half_index;
    uint64_t total_bytes_end;

    if((port == NULL) || (port->capture_port == NULL))
    {
        return;
    }

    if(XE_NP_App_UsesFullNodeWindowPublication(port) != 0)
    {
        return;
    }

    region_bytes = XE_NP_App_GetCompletedRegionBytes(port);
    if(region_bytes == 0U)
    {
        return;
    }

    XE_NP_App_RecordCapturedBytesFromIsr(port, region_bytes);
    total_bytes_end = port->capture_port->total_bytes_captured;
#if (XE_NP_APP_RAW_SD_DEDICATED_CAPTURE_ENABLE != 0U)
    /* No queueing, validation, cache maintenance, or live SRAM3 reads in the
     * capture IRQ. The writer stages the completed bytes into SRAM1 later. */
    (void)total_bytes_end;
    return;
#endif
#if (XE_NP_APP_CAPTURE_HALF_BOUNDARY_PROBE_ENABLE != 0U)
    XE_NP_App_ArmWrapProbeFromIsr(port, 0U, total_bytes_end);
#endif
    XE_NP_App_ResolveDmaWindowFromTotalBytes(
        port,
        total_bytes_end,
        region_bytes,
        &offset_bytes,
        &node_index,
        &half_index);
    XE_NP_App_CompleteWrapProbeFromIsr(port, offset_bytes, total_bytes_end);
    port->current_node_index = node_index;
#if (XE_NP_APP_CAPTURE_DMA_DIRECT_REPEAT_FULLSPAN_DEFER_ENABLE != 0U)
    if(XE_NP_App_UsesGroupedRepeatCapture(port) != 0)
    {
        if(port->stable_window_valid != 0U)
        {
            XE_NP_App_RequestBacklogResyncFromIsr(
                port,
                3U,
                XE_NP_App_GetPendingWindowDepth(port),
                XE_NP_App_GetMaxPendingWindows(port));
            return;
        }

        port->stable_window.offset_bytes = offset_bytes;
        port->stable_window.size_bytes = region_bytes;
        port->stable_window.node_index = node_index;
        port->stable_window.half_index = half_index;
        port->stable_window.total_bytes_end = total_bytes_end;
        port->stable_window_reserved = 1U;
        __DMB();
        port->stable_window_valid = 1U;
        return;
    }
#endif
#if (XE_NP_APP_CAPTURE_SYNC_WATCHDOG_ENABLE != 0U)
    XE_NP_App_QueueSyncWatchdogWindowFromIsr(
        port,
        offset_bytes,
        region_bytes,
        total_bytes_end);
#endif
#if (XE_NP_APP_CAPTURE_DMA_TC_WINDOW_DEFER_ENABLE != 0U)
    if((port->direct_repeat_mode != 0U) && (port->stable_window_valid != 0U))
    {
        /*
         * Full-ring TC can arrive a few bytes before the tail of the completed
         * half is externally stable. Flush that deferred TC half only once the
         * next HT fires, which guarantees an additional half-ring of slack
         * without reordering packet 2 behind packet 3.
         */
        XE_NP_App_FlushDeferredUsbWindowFromIsr(port);
    }
#endif
#if (XE_NP_APP_CAPTURE_DMA_HT_WINDOW_DEFER_ENABLE != 0U)
    if((port->direct_repeat_mode != 0U) &&
       (XE_NP_APP_CAPTURE_DMA_FORCE_SELF_RELOAD_MASK_ENABLE != 0U))
    {
        XE_NP_App_StoreDeferredUsbWindowFromIsr(
            port,
            node_index,
            half_index,
            offset_bytes,
            region_bytes,
            total_bytes_end);
        return;
    }
#endif
    XE_NP_App_PublishStableDmaRegionFromIsr(
        port,
        node_index,
        half_index,
        offset_bytes,
        region_bytes,
        total_bytes_end);
}

static void XE_NP_AppDma_HalfTransferCallback(DMA_HandleTypeDef *hdma)
{
    XE_NP_AppDmaPort *port;

    if(hdma == NULL)
    {
        return;
    }

    port = (XE_NP_AppDmaPort *)hdma->Parent;
    XE_NP_AppDma_HandleHalfTransferFromIsr(port);
}

static void XE_NP_AppDma_HandleTransferCompleteFromIsr(XE_NP_AppDmaPort *port)
{
    uint32_t offset_bytes;
    uint32_t region_bytes;
    uint32_t completed_blocks;
    uint8_t node_index;
    uint8_t half_index;
    uint64_t total_bytes_end;

    if((port == NULL) || (port->capture_port == NULL))
    {
        return;
    }

    /*
     * This U5 GPDMA reports TC for each physical block of a 2D repeated
     * transfer.  The ring advances by the complete node only after all
     * repeats.  Publish that completed node once; counting every 180-byte
     * event as a 46,080-byte node created the false USB overrun at 15 kHz.
     */
    if((XE_NP_App_UsesGroupedRepeatCapture(port) != 0) &&
       (port->dma_repeat_event_target > 1U))
    {
        port->dma_repeat_event_count++;
        if(port->dma_repeat_event_count < port->dma_repeat_event_target)
        {
            return;
        }
        port->dma_repeat_event_count = 0U;
    }

    region_bytes = XE_NP_App_GetCompletedRegionBytes(port);
    if(region_bytes == 0U)
    {
        return;
    }

    XE_NP_App_RecordCapturedBytesFromIsr(port, region_bytes);
    total_bytes_end = port->capture_port->total_bytes_captured;
#if (XE_NP_APP_RAW_SD_DEDICATED_CAPTURE_ENABLE != 0U)
    /* See the HT counterpart: the producer must not run USB/preview
     * bookkeeping in a dedicated SD recording session. */
    (void)total_bytes_end;
    return;
#endif
    XE_NP_App_ResolveDmaWindowFromTotalBytes(
        port,
        total_bytes_end,
        region_bytes,
        &offset_bytes,
        &node_index,
        &half_index);
    XE_NP_App_ArmWrapProbeFromIsr(port, offset_bytes, total_bytes_end);
#if (XE_NP_APP_CAPTURE_DMA_DIRECT_REPEAT_FULLSPAN_DEFER_ENABLE != 0U)
    if(XE_NP_App_UsesGroupedRepeatCapture(port) != 0)
    {
        if((port->stable_window_valid != 0U) && (port->stable_window_reserved != 0U))
        {
            XE_NP_App_FlushDeferredUsbWindowFromIsr(port);
        }
#if (XE_NP_APP_CAPTURE_SYNC_WATCHDOG_ENABLE != 0U)
        XE_NP_App_QueueSyncWatchdogWindowFromIsr(
            port,
            offset_bytes,
            region_bytes,
            total_bytes_end);
#endif
        XE_NP_App_PublishStableDmaRegionFromIsr(
            port,
            node_index,
            half_index,
            offset_bytes,
            region_bytes,
            total_bytes_end);
        return;
    }
#endif
#if (XE_NP_APP_CAPTURE_SYNC_WATCHDOG_ENABLE != 0U)
    XE_NP_App_QueueSyncWatchdogWindowFromIsr(
        port,
        offset_bytes,
        region_bytes,
        total_bytes_end);
#endif
#if (XE_NP_APP_CAPTURE_DMA_HT_WINDOW_DEFER_ENABLE != 0U)
    if(port->direct_repeat_mode != 0U)
    {
        XE_NP_App_FlushDeferredUsbWindowFromIsr(port);
    }
#endif
#if (XE_NP_APP_CAPTURE_DMA_TC_WINDOW_DEFER_ENABLE != 0U)
    if(port->direct_repeat_mode != 0U)
    {
        XE_NP_App_StoreDeferredUsbWindowFromIsr(
            port,
            node_index,
            half_index,
            offset_bytes,
            region_bytes,
            total_bytes_end);
    }
    else
#endif
    XE_NP_App_PublishStableDmaRegionFromIsr(
        port,
        node_index,
        half_index,
        offset_bytes,
        region_bytes,
        total_bytes_end);
    if(port->node_count != 0U)
    {
        completed_blocks = (port->node_bytes != 0U) ? (region_bytes / port->node_bytes) : 1U;
        if(completed_blocks == 0U)
        {
            completed_blocks = 1U;
        }
        port->current_node_index =
            (uint8_t)((node_index + completed_blocks) % port->node_count);
    }
}

static void XE_NP_AppDma_TransferCompleteCallback(DMA_HandleTypeDef *hdma)
{
    XE_NP_AppDmaPort *port;

    if(hdma == NULL)
    {
        return;
    }

    port = (XE_NP_AppDmaPort *)hdma->Parent;
    XE_NP_AppDma_HandleTransferCompleteFromIsr(port);
}

static void XE_NP_AppDma_ErrorCallback(DMA_HandleTypeDef *hdma)
{
    XE_NP_AppDmaPort *port = (XE_NP_AppDmaPort *)hdma->Parent;

    if((port != NULL) && (port->hdma.Instance != NULL))
    {
        XE_NP_App_RecordDmaFaultSnapshot(
            port,
            XE_NP_APP_DMA_FAULT_SOURCE_ERROR_CALLBACK,
            port->hdma.Instance->CSR,
            port->hdma.Instance->CCR,
            hdma->ErrorCode);
    }

    XE_NP_App_ReportCaptureFault(port, CE32_ERRORID_CORE_ERROR, 0U, 1U);
}

static void XE_NP_App_BindGeneratedPeripherals(XE_NP_Runtime *runtime)
{
    if(runtime == NULL)
    {
        return;
    }

    runtime->huart_ble = &huart1;
#if (XE_NP_BOARD_EXPERIMENT_DISABLE_SD_INIT == 0U)
    runtime->hsd_card = &hsd1;
#else
    runtime->hsd_card = NULL;
#endif
    runtime->hi2c_imu = &hi2c5;
    runtime->hi2c_temp = &hi2c6;
    runtime->hi2c_np[0] = &hi2c3;
    runtime->hi2c_np[1] = &hi2c4;
    runtime->hi2c_np[2] = &hi2c2;
#if (XE_NP_APP_GPIOE_CAPTURE_USE_PA0_TIM2_CH1 != 0U)
    /*
     * USB384_NP ties NP_PSB_CLK to PA0 and PA5, but the NP2 bench unit only
     * showed edges on PA0. Use TIM2_CH1 capture DMA for GPIOE sampling;
     * PA0/EXTI0 was proven to overrun at full PSB rate.
     */
    runtime->htim_np_capture_gpioe = &htim2;
#else
    runtime->htim_np_capture_gpioe = &htim2;
#endif
    runtime->htim_np_capture_gpiod = &htim8;
    runtime->hadc_batt = &hadc1;
    runtime->hdac_np_cal = &hdac1;
}

static void XE_NP_App_UpdateCapabilityFlags(XE_NP_Runtime *runtime)
{
    uint32_t flags = 0U;
    uint8_t active_mask;
    uint8_t interfaces_ready = 1U;

    if(runtime == NULL)
    {
        return;
    }

    if(runtime->huart_ble != NULL)
    {
        flags |= XE_NP_RUNTIME_FLAG_BLE_PRESENT;
    }

    if(runtime->hi2c_imu != NULL)
    {
        flags |= XE_NP_RUNTIME_FLAG_IMU_PRESENT;
    }

    active_mask = (uint8_t)(runtime->active_np_probe_mask & XE_NP_CAPTURE_ALL_PROBES_MASK);
    if(active_mask == 0U)
    {
        active_mask = XE_NP_CAPTURE_PROBE2_MASK;
    }

    for(uint8_t probe_index = 0U; probe_index < XE_NP_NEUROPIXELS_MAX_PROBES; ++probe_index)
    {
        uint8_t probe_mask = (uint8_t)(1U << probe_index);

        if((active_mask & probe_mask) == 0U)
        {
            continue;
        }

        if(runtime->hi2c_np[probe_index] == NULL)
        {
            interfaces_ready = 0U;
            break;
        }
    }

    if(interfaces_ready != 0U)
    {
        flags |= XE_NP_RUNTIME_FLAG_NP_INTERFACES_READY;
    }

    runtime->flags |= flags;
}

static int XE_NP_App_CanRetrySdInit(const XE_NP_Runtime *runtime)
{
    if((runtime == NULL) || (runtime->hsd_card == NULL))
    {
        return 0;
    }

    if((xe_np_raw_sd_sink.active != 0U) || (xe_np_raw_sd_sink.write_in_flight != 0U))
    {
        return 0;
    }

    /*
     * Background SD host/card recovery can block for far longer than the
     * Neuropixels foreground validator budget. Defer automatic retries while
     * any Neuropixels session is armed or running.
     */
    if(XE_NP_App_NpSessionActive(runtime) != 0)
    {
        return 0;
    }

    return 1;
}

static int XE_NP_App_TryRecoverSdHost(XE_NP_Runtime *runtime)
{
#if (XE_NP_APP_AUTO_SD_HOST_RECOVERY_ENABLE == 0U)
    (void)runtime;
    return -1;
#else
    if(XE_NP_App_CanRetrySdInit(runtime) == 0)
    {
        return -1;
    }

    if(XE_NP_Board_RetrySdInit() != 0)
    {
        return -1;
    }

    xe_np_sd_init_failed = 0U;
    xe_np_sd_error_reported = 0U;
    xe_np_sd_capacity_cached = 0U;
    return 0;
#endif
}

static int XE_NP_App_TryRecoverSdTransport(XE_NP_Runtime *runtime)
{
#if (XE_NP_APP_AUTO_SD_UHS_RECOVERY_ENABLE == 0U)
    (void)runtime;
    return -1;
#else
    if((runtime == NULL) || (runtime->hsd_card == NULL))
    {
        return -1;
    }

    /*
     * If the card mounted but stayed in a non-UHS fallback mode, re-run the
     * full host/tuning sequence while the system is still idle. This keeps the
     * dynamic-rail SD path from getting permanently stuck on an early cold-
     * boot negotiation miss.
     */
    if((xe_np_raw_sd_sink.write_in_flight != 0U) || (XE_NP_App_NpSessionActive(runtime) != 0))
    {
        return -1;
    }

    return XE_NP_App_TryRecoverSdHost(runtime);
#endif
}

static int XE_NP_App_SdCardStateIsOperational(HAL_SD_CardStateTypeDef card_state)
{
    if(card_state == HAL_SD_CARD_TRANSFER)
    {
        return 1;
    }

    if((xe_np_raw_sd_sink.write_in_flight != 0U) &&
       ((card_state == HAL_SD_CARD_SENDING) ||
        (card_state == HAL_SD_CARD_RECEIVING) ||
        (card_state == HAL_SD_CARD_PROGRAMMING)))
    {
        return 1;
    }

    return 0;
}

static uint32_t XE_NP_App_ExpectedRawBytesPerSecForProbeMask(uint8_t active_probe_mask)
{
    active_probe_mask &= XE_NP_CAPTURE_ALL_PROBES_MASK;
    return XE_NP_Capture_MinimumBytesPerTick(active_probe_mask) *
           XE_NP_Board_GetRuntimeExpectedPsbClkHz();
}

static int XE_NP_App_SdTransportReadyForNpRaw(const XE_NP_Runtime *runtime)
{
    uint32_t raw_bytes_per_sec;
    uint32_t transport_budget_bytes_per_sec;

    if((runtime == NULL) || (runtime->hsd_card == NULL))
    {
        return 0;
    }

    /*
     * One active direct-capture Neuropixels port is already about 46.8 MB/s,
     * so this mitigation build requires both:
     * - the real 1.8 V UHS logic-signaling path on VCCIO_SD
     * - a negotiated SD mode with a conservative sustained-write budget that
     *   still clears the current capture-rate demand
     */
    if(XE_NP_Board_SdUhsReadyForNpRaw() == 0)
    {
        return 0;
    }

    raw_bytes_per_sec = runtime->capture.raw_bytes_per_sec;
    if(raw_bytes_per_sec == 0U)
    {
        raw_bytes_per_sec =
            XE_NP_App_ExpectedRawBytesPerSecForProbeMask(runtime->active_np_probe_mask);
        if(raw_bytes_per_sec == 0U)
        {
            return 0;
        }
    }

    transport_budget_bytes_per_sec = XE_NP_Board_SdEstimatedWriteBudgetBytesPerSec();
    return (raw_bytes_per_sec <= transport_budget_bytes_per_sec) ? 1 : 0;
}

static int XE_NP_App_NpRecordingStorageReady(const XE_NP_Runtime *runtime)
{
    if(runtime == NULL)
    {
        return 0;
    }

    if(XE_NP_App_UsbCaptureStorageBypassActive(runtime) != 0)
    {
        return 1;
    }

#if (XE_NP_APP_EXPERIMENT_ALLOW_UNQUALIFIED_SD_RAW != 0U)
    /* Do not enable this in a recording image.  It exists solely to expose
     * SDMMC traffic and its effect on the 30 kHz capture lanes. */
    return 1;
#endif

#if (XE_NP_APP_BENCH_CAPTURE_WITHOUT_STORAGE != 0U)
    return 1;
#else
#if (XE_NP_APP_LEGACY_FS_ENABLE != 0U) || (XE_NP_APP_RAW_SD_SINK_ENABLE != 0U)
    if((runtime->hsd_card == NULL) ||
       ((runtime->flags & XE_NP_RUNTIME_FLAG_SD_PRESENT) == 0U) ||
       ((runtime->flags & XE_NP_RUNTIME_FLAG_SD_UHS_READY) == 0U) ||
       (xe_np_sd_init_failed != 0U) ||
       ((runtime->sys_param.error_code &
         (CE32_ERRORID_SD_CARDFULL | CE32_ERRORID_SD_FORMATERR)) != 0U))
    {
        return 0;
    }

    return (XE_NP_App_SdTransportReadyForNpRaw(runtime) != 0) ? 1 : 0;
#else
    return 1;
#endif
#endif
}

static void XE_NP_App_RefreshStorageStatus(XE_NP_Runtime *runtime)
{
    HAL_SD_CardInfoTypeDef card_info;
    HAL_SD_CardStateTypeDef card_state;
    uint32_t now_ms;
    uint32_t sd_capacity = 0U;
    uint8_t sd_present = 0U;
    uint8_t sd_uhs_ready = 0U;
    unsigned long error_code = 0UL;

    if(runtime == NULL)
    {
        return;
    }

    now_ms = HAL_GetTick();
    if((xe_np_sd_next_refresh_tick_ms != 0U) &&
       ((int32_t)(now_ms - xe_np_sd_next_refresh_tick_ms) < 0))
    {
        return;
    }

    XE_NP_Board_EnsureSdPowerRail();

    if((runtime->hsd_card == NULL) ||
       ((xe_np_sd_init_failed != 0U) && (XE_NP_App_TryRecoverSdHost(runtime) != 0)))
    {
        error_code = CE32_ERRORID_SD_NOTPRESENT;
        xe_np_sd_capacity_cached = 0U;
    }
    else if(xe_np_raw_sd_sink.write_in_flight != 0U)
    {
        card_state = HAL_SD_GetCardState(runtime->hsd_card);

        if(XE_NP_App_SdCardStateIsOperational(card_state) != 0)
        {
            sd_present = 1U;
            sd_capacity = xe_np_sd_capacity_cached;
        }
        else
        {
            error_code = CE32_ERRORID_SD_ERR;
        }
    }
    else if((card_state = HAL_SD_GetCardState(runtime->hsd_card),
             XE_NP_App_SdCardStateIsOperational(card_state) == 0))
    {
        if((xe_np_raw_sd_sink.write_in_flight == 0U) &&
           ((card_state == HAL_SD_CARD_SENDING) ||
            (card_state == HAL_SD_CARD_RECEIVING) ||
            (card_state == HAL_SD_CARD_PROGRAMMING)))
        {
            XE_NP_App_RawSdSink_ForceStopDataPath(runtime->hsd_card);
            card_state = HAL_SD_GetCardState(runtime->hsd_card);
        }

        if(XE_NP_App_SdCardStateIsOperational(card_state) != 0)
        {
            if(HAL_SD_GetCardInfo(runtime->hsd_card, &card_info) != HAL_OK)
            {
                error_code = CE32_ERRORID_SD_ERR;
            }
            else
            {
                sd_present = 1U;
                sd_capacity = card_info.LogBlockNbr;
                xe_np_sd_capacity_cached = sd_capacity;
            }
        }
        else if(XE_NP_App_TryRecoverSdHost(runtime) != 0)
        {
            error_code = CE32_ERRORID_SD_ERR;
        }
        else
        {
            card_state = HAL_SD_GetCardState(runtime->hsd_card);
            if(XE_NP_App_SdCardStateIsOperational(card_state) == 0)
            {
                error_code = CE32_ERRORID_SD_ERR;
            }
            else if(HAL_SD_GetCardInfo(runtime->hsd_card, &card_info) != HAL_OK)
            {
                error_code = CE32_ERRORID_SD_ERR;
            }
            else
            {
                sd_present = 1U;
                sd_capacity = card_info.LogBlockNbr;
                xe_np_sd_capacity_cached = sd_capacity;
            }
        }
    }
    else if(HAL_SD_GetCardInfo(runtime->hsd_card, &card_info) != HAL_OK)
    {
        if(XE_NP_App_TryRecoverSdHost(runtime) != 0)
        {
            error_code = CE32_ERRORID_SD_ERR;
        }
        else if(HAL_SD_GetCardInfo(runtime->hsd_card, &card_info) != HAL_OK)
        {
            error_code = CE32_ERRORID_SD_ERR;
        }
        else
        {
            HAL_SD_CardStateTypeDef card_state = HAL_SD_GetCardState(runtime->hsd_card);

            if(XE_NP_App_SdCardStateIsOperational(card_state) != 0)
            {
                sd_present = 1U;
                sd_capacity = card_info.LogBlockNbr;
                xe_np_sd_capacity_cached = sd_capacity;
            }
            else
            {
                error_code = CE32_ERRORID_SD_ERR;
            }
        }
    }
    else
    {
        sd_present = 1U;
        sd_capacity = card_info.LogBlockNbr;
        xe_np_sd_capacity_cached = sd_capacity;
    }

    if(sd_present != 0U)
    {
        if(XE_NP_App_SdTransportReadyForNpRaw(runtime) != 0)
        {
            sd_uhs_ready = 1U;
        }
        else if((runtime->active_np_probe_mask & XE_NP_CAPTURE_ALL_PROBES_MASK) != 0U)
        {
            if(XE_NP_App_TryRecoverSdTransport(runtime) == 0)
            {
                card_state = HAL_SD_GetCardState(runtime->hsd_card);
                if((XE_NP_App_SdCardStateIsOperational(card_state) != 0) &&
                   (HAL_SD_GetCardInfo(runtime->hsd_card, &card_info) == HAL_OK))
                {
                    sd_present = 1U;
                    sd_capacity = card_info.LogBlockNbr;
                    xe_np_sd_capacity_cached = sd_capacity;
                    if(XE_NP_App_SdTransportReadyForNpRaw(runtime) != 0)
                    {
                        sd_uhs_ready = 1U;
                    }
                }
            }

            if(sd_uhs_ready == 0U)
            {
                error_code = CE32_ERRORID_SD_ERR;
            }
        }
    }

    runtime->flags &= ~(XE_NP_RUNTIME_FLAG_SD_PRESENT | XE_NP_RUNTIME_FLAG_SD_UHS_READY);
    runtime->sys_param.SD_capacity = sd_capacity;

    if(sd_present != 0U)
    {
        runtime->flags |= XE_NP_RUNTIME_FLAG_SD_PRESENT;
        if(sd_uhs_ready != 0U)
        {
            runtime->flags |= XE_NP_RUNTIME_FLAG_SD_UHS_READY;
            xe_np_sd_error_reported = 0U;
            xe_np_sd_next_refresh_tick_ms = now_ms + XE_NP_APP_SD_REFRESH_INTERVAL_MS;
        }
        else
        {
            xe_np_sd_next_refresh_tick_ms = now_ms + XE_NP_APP_SD_UHS_RETRY_INTERVAL_MS;
        }
    }

    if((error_code != 0UL) && (xe_np_sd_error_reported == 0U))
    {
        CE32_ReportError(&runtime->sys_param, error_code);
        xe_np_sd_error_reported = 1U;
    }
    if(sd_present == 0U)
    {
        xe_np_sd_next_refresh_tick_ms = now_ms + XE_NP_APP_SD_RETRY_INTERVAL_MS;
    }

    XE_NP_App_RefreshCaptureDiag(&runtime->sys_param);
}

static void XE_NP_App_RecordRawSdDiag(
    uint32_t reason,
    const XE_NP_Runtime *runtime,
    const XE_NP_AppRawSdSink *sink,
    const XE_NP_AppRawSdPort *port,
    uint8_t port_index,
    HAL_StatusTypeDef hal_status)
{
    XE_NP_AppRawSdDiag diag;
    SD_HandleTypeDef *hsd = (runtime != NULL) ? runtime->hsd_card : NULL;

    memset(&diag, 0, sizeof(diag));
    diag.magic = XE_NP_APP_RAW_SD_DIAG_MAGIC;
    diag.version = XE_NP_APP_RAW_SD_DIAG_VERSION;
    diag.tick_ms = HAL_GetTick();
    diag.reason = reason;
    diag.hal_status = (uint32_t)hal_status;
    diag.port_index = port_index;
    diag.sd_speed_mode = XE_NP_Board_SdNegotiatedSpeedMode();
    diag.sd_budget_bytes_per_sec = XE_NP_Board_SdEstimatedWriteBudgetBytesPerSec();

    if(runtime != NULL)
    {
        diag.runtime_flags = runtime->flags;
        diag.capture_flags = runtime->capture.flags;
        diag.raw_bytes_per_sec = runtime->capture.raw_bytes_per_sec;
        diag.gpioe_total_bytes_low =
            (uint32_t)runtime->capture.gpioe.total_bytes_captured;
        diag.gpiod_total_bytes_low =
            (uint32_t)runtime->capture.gpiod.total_bytes_captured;
        diag.gpioe_raw_log_state = runtime->capture.gpioe.raw_mgr.logState;
        diag.gpiod_raw_log_state = runtime->capture.gpiod.raw_mgr.logState;
        diag.gpioe_active_out_mask = runtime->capture.gpioe.raw_mgr.activeOutMask;
        diag.gpiod_active_out_mask = runtime->capture.gpiod.raw_mgr.activeOutMask;
        diag.gpioe_in_ptr = runtime->capture.gpioe.raw_mgr.inPTR;
        diag.gpiod_in_ptr = runtime->capture.gpiod.raw_mgr.inPTR;
        diag.gpioe_out_ptr = runtime->capture.gpioe.raw_mgr.outPTR[0U];
        diag.gpiod_out_ptr = runtime->capture.gpiod.raw_mgr.outPTR[0U];
        diag.gpioe_buffer_used = runtime->capture.gpioe.raw_mgr.bufferUsed[0U];
        diag.gpiod_buffer_used = runtime->capture.gpiod.raw_mgr.bufferUsed[0U];
    }
    if(xe_np_dma_gpioe.hdma.Instance != NULL)
    {
        diag.gpioe_dma_csr = xe_np_dma_gpioe.hdma.Instance->CSR;
        diag.gpioe_dma_ccr = xe_np_dma_gpioe.hdma.Instance->CCR;
        diag.gpioe_dma_cbr1 = xe_np_dma_gpioe.hdma.Instance->CBR1;
        diag.gpioe_dma_cllr = xe_np_dma_gpioe.hdma.Instance->CLLR;
    }
    if(xe_np_dma_gpiod.hdma.Instance != NULL)
    {
        diag.gpiod_dma_csr = xe_np_dma_gpiod.hdma.Instance->CSR;
        diag.gpiod_dma_ccr = xe_np_dma_gpiod.hdma.Instance->CCR;
        diag.gpiod_dma_cbr1 = xe_np_dma_gpiod.hdma.Instance->CBR1;
        diag.gpiod_dma_cllr = xe_np_dma_gpiod.hdma.Instance->CLLR;
    }
    diag.tim2_cnt = TIM2->CNT;
    diag.tim2_dier = TIM2->DIER;
    diag.tim8_cnt = TIM8->CNT;
    diag.tim8_dier = TIM8->DIER;
    diag.capture_shutdown_reason = xe_np_capture_shutdown_reason;
    if(hsd != NULL)
    {
        diag.card_state = (uint32_t)HAL_SD_GetCardState(hsd);
        diag.hal_state = (uint32_t)hsd->State;
        diag.hal_context = hsd->Context;
        if(hsd->Instance != NULL)
        {
            diag.sdmmc_sta = hsd->Instance->STA;
            diag.sdmmc_dctrl = hsd->Instance->DCTRL;
        }
    }
    if(sink != NULL)
    {
        diag.total_blocks_written = sink->total_blocks_written;
        diag.total_blocks_dropped = sink->total_blocks_dropped;
        diag.in_flight_bytes = sink->in_flight_bytes;
        diag.in_flight_blocks = sink->in_flight_blocks;
        diag.active = sink->active;
        diag.write_in_flight = sink->write_in_flight;
        diag.write_complete = sink->write_complete;
        diag.write_error = sink->write_error;
        diag.in_flight_port_index = sink->in_flight_port_index;
        diag.stage_bytes = sink->staged_bytes;
        diag.stage_blocks = sink->staged_blocks;
        diag.stage_busy = sink->stage_busy;
        diag.stage_complete = sink->stage_complete;
        diag.stage_error = sink->stage_error;
        diag.stage_port_index = sink->staged_port_index;
        diag.service_call_count = sink->service_call_count;
        diag.service_state = sink->service_state;
#if (XE_NP_APP_RAW_SD_LINKED_STREAM_ENABLE != 0U)
#if (XE_NP_APP_RAW_SD_LINKED_RETARGET_ENABLE != 0U)
        diag.linked_batch_bytes = (sink->linked_active != 0U) ?
                                      sink->in_flight_bytes :
                                      XE_NP_APP_RAW_SD_RT_BATCH_BYTES;
#else
        diag.linked_batch_bytes = (sink->linked_active != 0U) ?
                                      sink->in_flight_bytes :
                                      XE_NP_APP_RAW_SD_LL_BATCH_BYTES;
#endif
        diag.linked_batches_completed = sink->linked_batches_completed;
        diag.linked_nodes_retired = sink->linked_nodes_retired;
        diag.linked_timing = ((uint32_t)sink->linked_active) |
                             ((uint32_t)sink->linked_node_count << 8U) |
                             ((uint32_t)sink->linked_nodes_complete << 16U) |
                             ((uint32_t)sink->linked_nodes_released << 24U);
        diag.linked_elapsed = (sink->linked_last_batch_ms & 0xFFFFU) |
                              ((sink->linked_worst_batch_ms & 0xFFFFU) << 16U);
#endif
        if((sink->stage_initialized != 0U) &&
           (sink->stage_dma.Instance != NULL))
        {
            diag.stage_dma_csr = sink->stage_dma.Instance->CSR;
            diag.stage_dma_ccr = sink->stage_dma.Instance->CCR;
            diag.stage_dma_ctr1 = sink->stage_dma.Instance->CTR1;
            diag.stage_dma_ctr2 = sink->stage_dma.Instance->CTR2;
            diag.stage_dma_cbr1 = sink->stage_dma.Instance->CBR1;
            diag.stage_dma_csar = sink->stage_dma.Instance->CSAR;
            diag.stage_dma_cdar = sink->stage_dma.Instance->CDAR;
            diag.stage_dma_cllr = sink->stage_dma.Instance->CLLR;
        }
        if(sink->gpioe.mgr != NULL)
        {
            diag.gpioe_available_bytes =
                dataMGR_BytesAvailable(sink->gpioe.mgr, sink->gpioe.out_idx);
        }
        if(sink->gpiod.mgr != NULL)
        {
            diag.gpiod_available_bytes =
                dataMGR_BytesAvailable(sink->gpiod.mgr, sink->gpiod.out_idx);
        }
    }
    if(port != NULL)
    {
        diag.port_blocks_written = port->blocks_written;
        diag.port_blocks_dropped = port->blocks_dropped;
        diag.port_next_block = port->next_block;
        diag.port_end_block = port->end_block;
    }

    xe_np_raw_sd_diag = diag;
    /* SWD validation reads this diagnostic without executing cache maintenance. */
    XE_NP_App_DCacheClean(
        (const void *)&xe_np_raw_sd_diag,
        (uint32_t)sizeof(xe_np_raw_sd_diag));
}

static int XE_NP_App_RawSdSink_ForceStopDataPath(SD_HandleTypeDef *hsd)
{
    uint32_t card_state;
    uint32_t start_tick;
    uint32_t stop_error = HAL_SD_ERROR_NONE;
    uint32_t sdmmc_irq_was_enabled;

    if((hsd == NULL) || (hsd->Instance == NULL))
    {
        return -1;
    }

    sdmmc_irq_was_enabled = NVIC_GetEnableIRQ(SDMMC1_IRQn);
    HAL_NVIC_DisableIRQ(SDMMC1_IRQn);
    card_state = (uint32_t)HAL_SD_GetCardState(hsd);

    __HAL_SD_DISABLE_IT(
        hsd,
        SDMMC_IT_DATAEND | SDMMC_IT_DCRCFAIL | SDMMC_IT_DTIMEOUT |
            SDMMC_IT_TXUNDERR | SDMMC_IT_RXOVERR);
    __SDMMC_CMDTRANS_DISABLE(hsd->Instance);
    hsd->Instance->IDMACTRL = SDMMC_DISABLE_IDMA;
    hsd->Instance->DLEN = 0U;
    hsd->Instance->DCTRL |= SDMMC_DCTRL_FIFORST;
    hsd->Instance->DCTRL = 0U;
    __HAL_SD_CLEAR_FLAG(hsd, SDMMC_FLAG_BUSYD0END);
    __HAL_SD_CLEAR_FLAG(hsd, SDMMC_STATIC_DATA_FLAGS);

    if((card_state == HAL_SD_CARD_RECEIVING) || (card_state == HAL_SD_CARD_SENDING))
    {
        stop_error = SDMMC_CmdStopTransfer(hsd->Instance);
        if((stop_error != HAL_SD_ERROR_NONE) && (stop_error != HAL_SD_ERROR_CMD_RSP_TIMEOUT))
        {
            hsd->ErrorCode |= stop_error;
        }
    }

    __HAL_SD_DISABLE_IT(
        hsd,
        SDMMC_IT_DATAEND | SDMMC_IT_DCRCFAIL | SDMMC_IT_DTIMEOUT |
            SDMMC_IT_TXUNDERR | SDMMC_IT_RXOVERR);
    __SDMMC_CMDTRANS_DISABLE(hsd->Instance);
    hsd->Instance->IDMACTRL = SDMMC_DISABLE_IDMA;
    hsd->Instance->DLEN = 0U;
    hsd->Instance->DCTRL |= SDMMC_DCTRL_FIFORST;
    hsd->Instance->DCTRL = 0U;
    __HAL_SD_CLEAR_FLAG(hsd, SDMMC_FLAG_BUSYD0END);
    __HAL_SD_CLEAR_FLAG(hsd, SDMMC_STATIC_DATA_FLAGS);

    hsd->State = HAL_SD_STATE_READY;
    hsd->Context = SD_CONTEXT_NONE;

    start_tick = HAL_GetTick();
    do
    {
        card_state = (uint32_t)HAL_SD_GetCardState(hsd);
        if((card_state == HAL_SD_CARD_TRANSFER) &&
           ((hsd->Instance->STA & SDMMC_FLAG_DPSMACT) == 0U))
        {
            if(sdmmc_irq_was_enabled != 0U)
            {
                HAL_NVIC_EnableIRQ(SDMMC1_IRQn);
            }
            return 0;
        }
    } while((HAL_GetTick() - start_tick) < XE_NP_APP_RAW_SD_RECOVER_TIMEOUT_MS);

    if(sdmmc_irq_was_enabled != 0U)
    {
        HAL_NVIC_EnableIRQ(SDMMC1_IRQn);
    }
    return -1;
}

static void XE_NP_App_RawSdSink_Reset(XE_NP_AppRawSdSink *sink)
{
    if(sink == NULL)
    {
        return;
    }

    if((sink->write_in_flight != 0U) && (xe_np_runtime.hsd_card != NULL))
    {
        XE_NP_App_RawSdSink_ForceStopDataPath(xe_np_runtime.hsd_card);
    }
    XE_NP_App_RawSdSink_StopStageDma(sink);
    HAL_NVIC_DisableIRQ(SDMMC1_IRQn);

    if(sink->gpioe.mgr != NULL)
    {
        dataMGR_DisarmConsumer(sink->gpioe.mgr, sink->gpioe.out_idx);
        sink->gpioe.mgr->logState &= ~MGR_STATE_LOGGING;
    }
    if(sink->gpiod.mgr != NULL)
    {
        dataMGR_DisarmConsumer(sink->gpiod.mgr, sink->gpiod.out_idx);
        sink->gpiod.mgr->logState &= ~MGR_STATE_LOGGING;
    }

    memset(sink, 0, sizeof(*sink));
}

static uint32_t XE_NP_App_MinU32(uint32_t a, uint32_t b)
{
    return (a < b) ? a : b;
}

static void XE_NP_App_RawSdSink_StageComplete(DMA_HandleTypeDef *hdma)
{
#if (XE_NP_APP_RAW_SD_DMA_STAGING_ENABLE != 0U)
    (void)hdma;
    xe_np_raw_sd_sink.stage_busy = 0U;
    xe_np_raw_sd_sink.stage_complete = 1U;
#else
    (void)hdma;
#endif
}

static void XE_NP_App_RawSdSink_StageError(DMA_HandleTypeDef *hdma)
{
#if (XE_NP_APP_RAW_SD_DMA_STAGING_ENABLE != 0U)
    (void)hdma;
    xe_np_raw_sd_sink.stage_busy = 0U;
    xe_np_raw_sd_sink.stage_error = 1U;
#else
    (void)hdma;
#endif
}

static int XE_NP_App_RawSdSink_InitStageDma(XE_NP_AppRawSdSink *sink)
{
#if (XE_NP_APP_RAW_SD_DMA_STAGING_ENABLE != 0U)
    DMA_DataHandlingConfTypeDef data_handling;

    if(sink == NULL)
    {
        return -1;
    }
    if(sink->stage_initialized != 0U)
    {
        return 0;
    }

    /* GPIO samples are written into SRAM3 through port 1.  The SDMMC IDMA
     * source is private SRAM1 on port 0.  This deliberately avoids a CPU read
     * of the live producer ring and leaves the capture DMA at higher NVIC
     * priority than this bulk staging completion. */
    __HAL_RCC_GPDMA1_CLK_ENABLE();
    memset(&sink->stage_dma, 0, sizeof(sink->stage_dma));
    sink->stage_dma.Instance = GPDMA1_Channel14;
    sink->stage_dma.Init.Request = DMA_REQUEST_SW;
    /* DMA_REQUEST_SW supplies one request at HAL_DMA_Start_IT().  It must
     * cover the complete bulk copy; SINGLE_BURST would transfer one byte and
     * then wait forever for another software request.  The GPIO capture
     * channels remain independently hardware-paced by their timer compares. */
    sink->stage_dma.Init.BlkHWRequest = DMA_BREQ_BLOCK;
    sink->stage_dma.Init.Direction = DMA_MEMORY_TO_MEMORY;
    sink->stage_dma.Init.SrcInc = DMA_SINC_INCREMENTED;
    sink->stage_dma.Init.DestInc = DMA_DINC_INCREMENTED;
    /* Raw-ring positions and the SRAM1 scratch area are cache-line aligned
     * and this recorder moves integral 512-byte SD blocks.  Use native words
     * here: the capture DMA remains byte-wide, but a bulk byte-at-a-time
     * copier cannot drain even one 32 KiB window before the live lane wraps. */
    sink->stage_dma.Init.SrcDataWidth = DMA_SRC_DATAWIDTH_WORD;
    sink->stage_dma.Init.DestDataWidth = DMA_DEST_DATAWIDTH_WORD;
    /* The two GPIO capture channels are hardware-requested at the sample
     * instants.  The bulk SRAM3->SRAM1 copy is software-requested, but it
     * must nevertheless obtain GPDMA service before a 46,080-byte raw lane
     * wraps.  Give it the high arbitration class for this 30 kHz diagnostic;
     * its one-byte bursts and separate SRAM ports still bound each individual
     * bus occupation. */
    sink->stage_dma.Init.Priority = DMA_HIGH_PRIORITY;
    /* A bounded 32-word (128-byte) bulk burst is short relative to the GPIO
     * sample period but avoids paying GPDMA arbitration for every byte. */
    sink->stage_dma.Init.SrcBurstLength = 32U;
    sink->stage_dma.Init.DestBurstLength = 32U;
    sink->stage_dma.Init.TransferAllocatedPort =
        DMA_SRC_ALLOCATED_PORT1 | DMA_DEST_ALLOCATED_PORT0;
    sink->stage_dma.Init.TransferEventMode = DMA_TCEM_BLOCK_TRANSFER;
    sink->stage_dma.Init.Mode = DMA_NORMAL;
    if(HAL_DMA_Init(&sink->stage_dma) != HAL_OK)
    {
        return -1;
    }
    memset(&data_handling, 0, sizeof(data_handling));
    data_handling.DataExchange = DMA_EXCHANGE_NONE;
    data_handling.DataAlignment = DMA_DATA_RIGHTALIGN_ZEROPADDED;
    if((HAL_DMAEx_ConfigDataHandling(&sink->stage_dma, &data_handling) != HAL_OK) ||
       (HAL_DMA_RegisterCallback(&sink->stage_dma, HAL_DMA_XFER_CPLT_CB_ID,
                                 XE_NP_App_RawSdSink_StageComplete) != HAL_OK) ||
       (HAL_DMA_RegisterCallback(&sink->stage_dma, HAL_DMA_XFER_ERROR_CB_ID,
                                 XE_NP_App_RawSdSink_StageError) != HAL_OK))
    {
        (void)HAL_DMA_DeInit(&sink->stage_dma);
        return -1;
    }
    HAL_NVIC_SetPriority(GPDMA1_Channel14_IRQn, 6U, 0U);
    HAL_NVIC_EnableIRQ(GPDMA1_Channel14_IRQn);
    sink->stage_initialized = 1U;
    return 0;
#else
    (void)sink;
    return -1;
#endif
}

static void XE_NP_App_RawSdSink_StopStageDma(XE_NP_AppRawSdSink *sink)
{
#if (XE_NP_APP_RAW_SD_DMA_STAGING_ENABLE != 0U)
    if((sink != NULL) && (sink->stage_busy != 0U))
    {
        (void)HAL_DMA_Abort(&sink->stage_dma);
    }
#else
    (void)sink;
#endif
}

static int XE_NP_App_RawSdSink_StartStage(
    XE_NP_AppRawSdSink *sink,
    const uint8_t *src,
    uint32_t bytes,
    uint8_t port_index,
    uint16_t blocks)
{
#if (XE_NP_APP_RAW_SD_DMA_STAGING_ENABLE != 0U)
    if((sink == NULL) || (src == NULL) || (bytes == 0U) ||
       (bytes > sizeof(sink->scratch)) || (blocks == 0U) ||
       (sink->stage_busy != 0U) || (sink->stage_complete != 0U) ||
       (XE_NP_App_RawSdSink_InitStageDma(sink) != 0))
    {
        return -1;
    }

    /* The destination can contain data from the preceding SD operation. Make
     * it clean before GPDMA overwrites it; never invalidate/read the live
     * SRAM3 source from foreground code. */
    XE_NP_App_DCacheClean(sink->scratch, bytes);
    XE_NP_Capture_DCacheInvalidate(sink->scratch, bytes);
    sink->staged_port_index = port_index;
    sink->staged_blocks = blocks;
    sink->staged_bytes = bytes;
    sink->stage_error = 0U;
    sink->stage_busy = 1U;
    if(HAL_DMA_Start_IT(&sink->stage_dma, (uint32_t)src,
                        (uint32_t)sink->scratch, bytes) != HAL_OK)
    {
        sink->stage_busy = 0U;
        sink->stage_error = 1U;
        return -1;
    }
    return 0;
#else
    (void)sink;
    (void)src;
    (void)bytes;
    (void)port_index;
    (void)blocks;
    return -1;
#endif
}

static void XE_NP_App_RawSdSink_ClearInflight(XE_NP_AppRawSdSink *sink)
{
    if(sink == NULL)
    {
        return;
    }

    sink->write_in_flight = 0U;
    sink->write_complete = 0U;
    sink->write_error = 0U;
    sink->in_flight_port_index = 0U;
    sink->in_flight_blocks = 0U;
    sink->in_flight_bytes = 0U;
    sink->in_flight_start_tick_ms = 0U;
}

static int XE_NP_App_RawSdSink_LaunchStagedWrite(
    XE_NP_Runtime *runtime,
    XE_NP_AppRawSdSink *sink)
{
#if (XE_NP_APP_RAW_SD_DMA_STAGING_ENABLE != 0U)
    XE_NP_AppRawSdPort *ports[2] = {&sink->gpioe, &sink->gpiod};
    XE_NP_AppRawSdPort *port;
    HAL_StatusTypeDef status;
    uint8_t port_index;

    if((runtime == NULL) || (sink == NULL) || (sink->stage_complete == 0U) ||
       (sink->stage_busy != 0U) || (sink->staged_blocks == 0U) ||
       (sink->staged_bytes != ((uint32_t)sink->staged_blocks * XE_NP_APP_RAW_SD_BLOCK_SIZE)))
    {
        return -1;
    }
    port_index = sink->staged_port_index;
    if(port_index >= 2U)
    {
        return -1;
    }
    port = ports[port_index];
    if((port->active == 0U) || (runtime->hsd_card == NULL))
    {
        return -1;
    }

    XE_NP_App_RecordRawSdDiag(XE_NP_APP_RAW_SD_DIAG_REASON_STAGE_COMPLETE,
                              runtime, sink, port, port_index, HAL_OK);
    /* From here the SDMMC controller sees only private SRAM1. The completed
     * source range has already been copied by Channel 14 and can be retired
     * only after SD IDMA reports completion. */
    XE_NP_App_DCacheClean(sink->scratch, sink->staged_bytes);
    XE_NP_Board_EnsureSdPowerRail();
    status = HAL_SD_WriteBlocks_DMA(runtime->hsd_card, sink->scratch,
                                    port->next_block, sink->staged_blocks);
    if(status == HAL_BUSY)
    {
        XE_NP_App_RecordRawSdDiag(XE_NP_APP_RAW_SD_DIAG_REASON_LAUNCH_BUSY,
                                  runtime, sink, port, port_index, status);
        return 0;
    }
    if(status != HAL_OK)
    {
        XE_NP_App_RecordRawSdDiag(XE_NP_APP_RAW_SD_DIAG_REASON_LAUNCH_FAIL,
                                  runtime, sink, port, port_index, status);
        port->error_seen = 1U;
        XE_NP_App_RawSdSink_ReportStorageFault(runtime, sink, CE32_ERRORID_SD_ERR);
        return -1;
    }

    sink->write_in_flight = 1U;
    sink->write_complete = 0U;
    sink->write_error = 0U;
    sink->in_flight_port_index = port_index;
    sink->in_flight_blocks = sink->staged_blocks;
    sink->in_flight_bytes = sink->staged_bytes;
    sink->in_flight_start_tick_ms = HAL_GetTick();
    sink->stage_complete = 0U;
    sink->staged_blocks = 0U;
    sink->staged_bytes = 0U;
    XE_NP_App_RecordRawSdDiag(XE_NP_APP_RAW_SD_DIAG_REASON_LAUNCH_OK,
                              runtime, sink, port, port_index, status);
    return (int)sink->in_flight_blocks;
#else
    (void)runtime;
    (void)sink;
    return -1;
#endif
}

static void XE_NP_App_RawSdSink_ReportStorageFault(
    XE_NP_Runtime *runtime,
    XE_NP_AppRawSdSink *sink,
    unsigned long error_code)
{
    if((runtime != NULL) && (error_code != 0UL) &&
       ((sink == NULL) || (sink->error_reported == 0U)))
    {
        CE32_ReportError(&runtime->sys_param, error_code);
    }

    if(sink != NULL)
    {
        sink->error_reported = 1U;
        XE_NP_App_RecordRawSdDiag(
            XE_NP_APP_RAW_SD_DIAG_REASON_STORAGE_FAULT,
            runtime,
            sink,
            NULL,
            0xFFU,
            HAL_ERROR);
    }
}

static int XE_NP_App_RawSdSink_BacklogAtLossRisk(const XE_NP_AppRawSdPort *port)
{
    uint32_t available;
    uint32_t high_water;
    uint32_t guard_bytes;

    if((port == NULL) || (port->mgr == NULL))
    {
        return 0;
    }

    available = dataMGR_BytesAvailable(port->mgr, port->out_idx);
    /* raw_mgr is per physical DDR lane. Do not apply the full, two-lane
     * capture half-ring here: on the 30 kHz layout it equals the complete
     * lane capacity and turns the loss guard into an immediate false fault.
     * Preserve one quarter of this lane for an in-flight bulk stage/write. */
    guard_bytes = (uint32_t)port->mgr->dataSize / 4U;
    if(guard_bytes < (XE_NP_APP_RAW_SD_BLOCK_SIZE * XE_NP_APP_RAW_SD_MAX_BLOCKS_PER_WRITE))
    {
        guard_bytes = XE_NP_APP_RAW_SD_BLOCK_SIZE * XE_NP_APP_RAW_SD_MAX_BLOCKS_PER_WRITE;
    }
    high_water = ((uint32_t)port->mgr->dataSize > guard_bytes) ?
        ((uint32_t)port->mgr->dataSize - guard_bytes) :
        ((uint32_t)port->mgr->dataSize / 2U);
    return (available >= high_water) ? 1 : 0;
}

#if (XE_NP_APP_RAW_SD_LINKED_STREAM_ENABLE != 0U)
/*
 * HAL_SDEx_DMALinkedList_WriteBlocks always enables the DATAEND IRQ, whose
 * generic multi-block handler emits CMD12.  That command/IRQ path costs
 * enough at recorder-sized writes to fall below 30 kHz.  CMD23 already bounds
 * this CMD25 transfer, so start the same IDMA state machine without CMD12 and
 * poll its terminal flags from the low-priority foreground service.
 */
static HAL_StatusTypeDef XE_NP_App_RawSdSink_StartLinkedNoCmd12(
    SD_HandleTypeDef *hsd,
    const SD_DMALinkedListTypeDef *list,
    uint32_t block_address,
    uint32_t block_count)
{
    SDMMC_DataInitTypeDef config;
    uint32_t address = block_address;
    uint32_t errorstate;

    if((hsd == NULL) || (hsd->Instance == NULL) || (list == NULL) ||
       (list->pHeadNode == NULL))
    {
        return HAL_ERROR;
    }
    if(hsd->State != HAL_SD_STATE_READY)
    {
        return HAL_BUSY;
    }
    if((block_count == 0U) || ((address + block_count) > hsd->SdCard.LogBlockNbr))
    {
        hsd->ErrorCode |= HAL_SD_ERROR_ADDR_OUT_OF_RANGE;
        return HAL_ERROR;
    }

    hsd->Instance->IDMABASER = (uint32_t)list->pHeadNode->IDMABASER;
    hsd->Instance->IDMABSIZE = (uint32_t)list->pHeadNode->IDMABSIZE;
    hsd->Instance->IDMABAR = (uint32_t)list->pHeadNode;
    hsd->Instance->IDMALAR = (uint32_t)(SDMMC_IDMALAR_ABR | SDMMC_IDMALAR_ULS |
                                         SDMMC_IDMALAR_ULA |
                                         sizeof(SDMMC_DMALinkNodeTypeDef));
    if((hsd->Instance->IDMABSIZE == 0U) || (hsd->Instance->IDMABASER == 0U) ||
       (hsd->Instance->IDMABAR == 0U))
    {
        hsd->ErrorCode |= HAL_SD_ERROR_ADDR_OUT_OF_RANGE;
        return HAL_ERROR;
    }

    __HAL_SD_DISABLE_IT(hsd, SDMMC_IT_DCRCFAIL | SDMMC_IT_DTIMEOUT |
                             SDMMC_IT_TXUNDERR | SDMMC_IT_DATAEND |
                             SDMMC_IT_IDMABTC);
    __HAL_SD_CLEAR_FLAG(hsd, SDMMC_STATIC_FLAGS);
    hsd->Instance->DCTRL = 0U;
    hsd->ErrorCode = HAL_SD_ERROR_NONE;
    hsd->State = HAL_SD_STATE_BUSY;
    if(hsd->SdCard.CardType != CARD_SDHC_SDXC)
    {
        address *= BLOCKSIZE;
    }

    config.DataTimeOut = SDMMC_DATATIMEOUT;
    config.DataLength = BLOCKSIZE * block_count;
    config.DataBlockSize = SDMMC_DATABLOCK_SIZE_512B;
    config.TransferDir = SDMMC_TRANSFER_DIR_TO_CARD;
    config.TransferMode = SDMMC_TRANSFER_MODE_BLOCK;
    config.DPSM = SDMMC_DPSM_DISABLE;
    (void)SDMMC_ConfigData(hsd->Instance, &config);
    __SDMMC_CMDTRANS_ENABLE(hsd->Instance);
    hsd->Instance->IDMACTRL = SDMMC_ENABLE_IDMA_DOUBLE_BUFF0;
    hsd->Context = (SD_CONTEXT_WRITE_MULTIPLE_BLOCK | SD_CONTEXT_DMA);
    errorstate = SDMMC_CmdWriteMultiBlock(hsd->Instance, address);
    if(errorstate != HAL_SD_ERROR_NONE)
    {
        __SDMMC_CMDTRANS_DISABLE(hsd->Instance);
        hsd->Instance->IDMACTRL = SDMMC_DISABLE_IDMA;
        hsd->State = HAL_SD_STATE_READY;
        hsd->Context = SD_CONTEXT_NONE;
        hsd->ErrorCode |= errorstate;
        return HAL_ERROR;
    }
    return HAL_OK;
}

static void XE_NP_App_RawSdSink_FinishLinkedNoCmd12(SD_HandleTypeDef *hsd)
{
    if((hsd == NULL) || (hsd->Instance == NULL))
    {
        return;
    }
    __HAL_SD_DISABLE_IT(hsd, SDMMC_IT_DCRCFAIL | SDMMC_IT_DTIMEOUT |
                             SDMMC_IT_TXUNDERR | SDMMC_IT_DATAEND |
                             SDMMC_IT_IDMABTC);
    __SDMMC_CMDTRANS_DISABLE(hsd->Instance);
    hsd->Instance->DLEN = 0U;
    hsd->Instance->DCTRL = 0U;
    hsd->Instance->IDMACTRL = SDMMC_DISABLE_IDMA;
    __HAL_SD_CLEAR_FLAG(hsd, SDMMC_STATIC_DATA_FLAGS);
    hsd->State = HAL_SD_STATE_READY;
    hsd->Context = SD_CONTEXT_NONE;
}

static int XE_NP_App_RawSdSink_LaunchLinkedBatch(
    XE_NP_Runtime *runtime,
    XE_NP_AppRawSdSink *sink,
    XE_NP_AppRawSdPort *port,
    uint8_t port_index)
{
    uint32_t available;
    uint32_t remaining;
    uint32_t offset;
    uint32_t node_count = 0U;
    uint32_t address;
    HAL_StatusTypeDef status;

    if((runtime == NULL) || (runtime->hsd_card == NULL) || (sink == NULL) ||
       (port == NULL) || (port->mgr == NULL) || (port->active == 0U) ||
       (sink->linked_active != 0U) ||
       ((port->next_block + XE_NP_APP_RAW_SD_LL_BATCH_BLOCKS) > port->end_block))
    {
        return -1;
    }

    available = dataMGR_BytesAvailable(port->mgr, port->out_idx);
    if(available < XE_NP_APP_RAW_SD_LL_BATCH_BYTES)
    {
        return 0;
    }

    memset(sink->linked_nodes, 0, sizeof(sink->linked_nodes));
    memset(&sink->linked_list, 0, sizeof(sink->linked_list));
    remaining = XE_NP_APP_RAW_SD_LL_BATCH_BYTES;
    offset = (uint32_t)port->mgr->outPTR[port->out_idx];
    while(remaining != 0U)
    {
        SD_DMALinkNodeConfTypeDef node_conf;
        uint32_t until_wrap = (uint32_t)port->mgr->dataSize - offset;
        uint32_t node_bytes = XE_NP_App_MinU32(remaining, XE_NP_APP_RAW_SD_LL_NODE_BYTES);

        node_bytes = XE_NP_App_MinU32(node_bytes, until_wrap);
        if((node_bytes == 0U) || ((node_bytes % XE_NP_APP_RAW_SD_BLOCK_SIZE) != 0U) ||
           (node_count >= XE_NP_APP_RAW_SD_LL_NODE_COUNT_MAX))
        {
            return -1;
        }
        node_conf.BufferAddress = (uint32_t)&port->mgr->dataPtr[offset];
        node_conf.BufferSize = node_bytes;
        if((HAL_SDEx_DMALinkedList_BuildNode(&sink->linked_nodes[node_count], &node_conf) != HAL_OK) ||
           (HAL_SDEx_DMALinkedList_InsertNode(&sink->linked_list,
                                              sink->linked_list.pTailNode,
                                              &sink->linked_nodes[node_count]) != HAL_OK))
        {
            return -1;
        }
        sink->linked_node_port[node_count] = port;
        sink->linked_node_bytes[node_count] = node_bytes;
        remaining -= node_bytes;
        offset += node_bytes;
        if(offset >= (uint32_t)port->mgr->dataSize)
        {
            offset = 0U;
        }
        ++node_count;
    }

    XE_NP_App_DCacheClean(sink->linked_nodes, (uint32_t)sizeof(sink->linked_nodes));
    XE_NP_App_DCacheClean(&sink->linked_list, (uint32_t)sizeof(sink->linked_list));
    XE_NP_Board_EnsureSdPowerRail();
    /* ACMD23 lets the card prepare the exact programmed region; CMD23 then
     * makes CMD25 self-terminating, eliminating CMD12 from the real-time
     * capture path. */
    address = (uint32_t)(runtime->hsd_card->SdCard.RelCardAdd << 16U);
    if((SDMMC_CmdAppCommand(runtime->hsd_card->Instance, address) != HAL_SD_ERROR_NONE) ||
       (SDMMC_CmdBlockCount(runtime->hsd_card->Instance,
                            XE_NP_APP_RAW_SD_LL_BATCH_BLOCKS) != HAL_SD_ERROR_NONE) ||
       (SDMMC_CmdBlockCount(runtime->hsd_card->Instance,
                            XE_NP_APP_RAW_SD_LL_BATCH_BLOCKS) != HAL_SD_ERROR_NONE))
    {
        return -1;
    }

    sink->linked_node_count = (uint8_t)node_count;
    sink->linked_nodes_complete = 0U;
    sink->linked_nodes_released = 0U;
    sink->linked_data_end_seen = 0U;
    sink->linked_active = 1U;
    status = XE_NP_App_RawSdSink_StartLinkedNoCmd12(runtime->hsd_card,
                                                     &sink->linked_list,
                                                     port->next_block,
                                                     XE_NP_APP_RAW_SD_LL_BATCH_BLOCKS);
    if(status != HAL_OK)
    {
        sink->linked_active = 0U;
        return (status == HAL_BUSY) ? 0 : -1;
    }

    sink->write_in_flight = 1U;
    sink->write_complete = 0U;
    sink->write_error = 0U;
    sink->in_flight_port_index = port_index;
    sink->in_flight_blocks = XE_NP_APP_RAW_SD_LL_BATCH_BLOCKS;
    sink->in_flight_bytes = XE_NP_APP_RAW_SD_LL_BATCH_BYTES;
    sink->in_flight_start_tick_ms = HAL_GetTick();
    XE_NP_App_RecordRawSdDiag(XE_NP_APP_RAW_SD_DIAG_REASON_LAUNCH_OK,
                              runtime, sink, port, port_index, HAL_OK);
    return (int)XE_NP_APP_RAW_SD_LL_BATCH_BLOCKS;
}

#if (XE_NP_APP_RAW_SD_LINKED_RETARGET_ENABLE != 0U)
static int XE_NP_App_RawSdSink_LaunchLinkedRetargetBatch(
    XE_NP_Runtime *runtime,
    XE_NP_AppRawSdSink *sink,
    XE_NP_AppRawSdPort *port,
    uint8_t port_index)
{
    uint32_t available;
    uint32_t offset;
    uint32_t address;
    HAL_StatusTypeDef status;

    if((runtime == NULL) || (runtime->hsd_card == NULL) || (sink == NULL) ||
       (port == NULL) || (port->mgr == NULL) || (port->active == 0U) ||
       (sink->linked_active != 0U) ||
       ((port->next_block + XE_NP_APP_RAW_SD_RT_BATCH_BLOCKS) > port->end_block) ||
       ((uint32_t)port->mgr->dataSize % XE_NP_APP_RAW_SD_RT_NODE_BYTES) != 0U)
    {
        return -1;
    }
    available = dataMGR_BytesAvailable(port->mgr, port->out_idx);
    offset = (uint32_t)port->mgr->outPTR[port->out_idx];
    if(available < (XE_NP_APP_RAW_SD_RT_NODE_COUNT * XE_NP_APP_RAW_SD_RT_NODE_BYTES))
    {
        return 0;
    }
    /* The capture producer and the 61,440-byte SD nodes share a geometry.
     * Reject a phase drift instead of constructing a descriptor that spans the
     * physical SRAM3 ring wrap. */
    if((offset % XE_NP_APP_RAW_SD_RT_NODE_BYTES) != 0U)
    {
        return -1;
    }

    memset(sink->linked_nodes, 0, sizeof(sink->linked_nodes));
    memset(&sink->linked_list, 0, sizeof(sink->linked_list));
    for(uint32_t i = 0U; i < XE_NP_APP_RAW_SD_RT_NODE_COUNT; ++i)
    {
        SD_DMALinkNodeConfTypeDef node_conf;

        node_conf.BufferAddress = (uint32_t)&port->mgr->dataPtr[offset];
        node_conf.BufferSize = XE_NP_APP_RAW_SD_RT_NODE_BYTES;
        if((HAL_SDEx_DMALinkedList_BuildNode(&sink->linked_nodes[i], &node_conf) != HAL_OK) ||
           (HAL_SDEx_DMALinkedList_InsertNode(&sink->linked_list,
                                              sink->linked_list.pTailNode,
                                              &sink->linked_nodes[i]) != HAL_OK))
        {
            return -1;
        }
        sink->linked_node_port[i] = port;
        sink->linked_node_bytes[i] = XE_NP_APP_RAW_SD_RT_NODE_BYTES;
        offset += XE_NP_APP_RAW_SD_RT_NODE_BYTES;
        if(offset >= (uint32_t)port->mgr->dataSize)
        {
            offset = 0U;
        }
    }
    if(HAL_SDEx_DMALinkedList_EnableCircularMode(&sink->linked_list) != HAL_OK)
    {
        return -1;
    }
    XE_NP_App_DCacheClean(sink->linked_nodes, (uint32_t)sizeof(sink->linked_nodes));
    XE_NP_App_DCacheClean(&sink->linked_list, (uint32_t)sizeof(sink->linked_list));
    XE_NP_Board_EnsureSdPowerRail();
    address = (uint32_t)(runtime->hsd_card->SdCard.RelCardAdd << 16U);
    /* ACMD23 is the card pre-erase hint; CMD23 is the one actual transfer
     * block count.  Sending CMD23 twice can make an otherwise valid CMD25
     * transaction card-dependent and is especially harmful on the circular
     * retarget path where command timing is intentionally deterministic. */
    if((SDMMC_CmdAppCommand(runtime->hsd_card->Instance, address) != HAL_SD_ERROR_NONE) ||
       (SDMMC_CmdBlockCount(runtime->hsd_card->Instance,
                            XE_NP_APP_RAW_SD_RT_BATCH_BLOCKS) != HAL_SD_ERROR_NONE))
    {
        return -1;
    }

    sink->linked_node_count = XE_NP_APP_RAW_SD_RT_NODE_COUNT;
    sink->linked_nodes_complete = 0U;
    sink->linked_nodes_released = 0U;
    sink->linked_nodes_rearmed = 0U;
    sink->linked_rearm_mask = 0U;
    sink->linked_reserved_bytes = XE_NP_APP_RAW_SD_RT_NODE_COUNT *
                                  XE_NP_APP_RAW_SD_RT_NODE_BYTES;
    sink->linked_data_end_seen = 0U;
    sink->linked_active = 1U;
    status = XE_NP_App_RawSdSink_StartLinkedNoCmd12(runtime->hsd_card,
                                                     &sink->linked_list,
                                                     port->next_block,
                                                     XE_NP_APP_RAW_SD_RT_BATCH_BLOCKS);
    if(status != HAL_OK)
    {
        sink->linked_active = 0U;
        return (status == HAL_BUSY) ? 0 : -1;
    }
    /* DATAEND remains masked to suppress HAL's CMD12 path.  Btc is a tiny
     * counter-only ISR; it guarantees a 61,440-byte completion is never lost
     * if foreground service is briefly delayed by the probe control loop. */
    __HAL_SD_ENABLE_IT(runtime->hsd_card, SDMMC_IT_IDMABTC);
    sink->write_in_flight = 1U;
    sink->write_complete = 0U;
    sink->write_error = 0U;
    sink->in_flight_port_index = port_index;
    sink->in_flight_blocks = XE_NP_APP_RAW_SD_RT_BATCH_BLOCKS;
    sink->in_flight_bytes = XE_NP_APP_RAW_SD_RT_BATCH_BYTES;
    sink->in_flight_start_tick_ms = HAL_GetTick();
    XE_NP_App_RecordRawSdDiag(XE_NP_APP_RAW_SD_DIAG_REASON_LAUNCH_OK,
                              runtime, sink, port, port_index, HAL_OK);
    return (int)XE_NP_APP_RAW_SD_RT_BATCH_BLOCKS;
}
#endif
#endif

static int XE_NP_App_RawSdSink_LaunchWrite(
    XE_NP_Runtime *runtime,
    XE_NP_AppRawSdSink *sink,
    XE_NP_AppRawSdPort *port,
    uint8_t port_index,
    uint32_t block_budget)
{
    uint8_t *src;
    uint32_t available;
    uint32_t contiguous;
    uint32_t contiguous_blocks;
    uint32_t available_blocks;
    uint32_t remaining_blocks;
    uint32_t blocks_to_write;
    uint32_t bytes_to_write;
    uint32_t tail_bytes;
    HAL_StatusTypeDef status;

    if((runtime == NULL) || (sink == NULL) || (port == NULL) ||
       ((port->mgr == NULL) && (sink->stress_writer == 0U)))
    {
        return -1;
    }

    available = (sink->stress_writer != 0U) ?
        (uint32_t)sizeof(sink->scratch) :
        dataMGR_BytesAvailable(port->mgr, port->out_idx);
    available_blocks = available / XE_NP_APP_RAW_SD_BLOCK_SIZE;
    if(available_blocks == 0U)
    {
        return 0;
    }
    if(port->next_block >= port->end_block)
    {
        port->error_seen = 1U;
        XE_NP_App_RawSdSink_ReportStorageFault(runtime, sink, CE32_ERRORID_SD_CARDFULL);
        return -2;
    }

    remaining_blocks = port->end_block - port->next_block;
    if(sink->stress_writer != 0U)
    {
        contiguous = available;
        contiguous_blocks = available_blocks;
        src = sink->scratch;
    }
    else
    {
        contiguous = dataMGR_ContiguousBytesAvailable(port->mgr, port->out_idx);
        contiguous_blocks = contiguous / XE_NP_APP_RAW_SD_BLOCK_SIZE;
        src = dataMGR_returnPTR(port->mgr, port->out_idx);

        if((src == NULL) || (contiguous == 0U))
        {
            return -1;
        }
    }

    blocks_to_write = available_blocks;
    blocks_to_write = XE_NP_App_MinU32(blocks_to_write, remaining_blocks);
    blocks_to_write = XE_NP_App_MinU32(blocks_to_write, block_budget);
    blocks_to_write = XE_NP_App_MinU32(blocks_to_write, XE_NP_APP_RAW_SD_MAX_BLOCKS_PER_WRITE);
#if ((XE_NP_APP_RAW_SD_DMA_STAGING_ENABLE != 0U) || \
     (XE_NP_APP_RAW_SD_LINKED_STREAM_ENABLE != 0U))
    if(sink->stress_writer == 0U)
    {
        /* Channel 14 cannot safely span a physical ring wrap.  Wait for the
         * next contiguous producer region instead of restoring the former
         * CPU memcpy across SRAM3. */
        blocks_to_write = XE_NP_App_MinU32(blocks_to_write, contiguous_blocks);
    }
#endif
    if(blocks_to_write == 0U)
    {
        return 0;
    }

    bytes_to_write = blocks_to_write * XE_NP_APP_RAW_SD_BLOCK_SIZE;
    if(bytes_to_write > sizeof(sink->scratch))
    {
        return -1;
    }

#if (XE_NP_APP_RAW_SD_LINKED_STREAM_ENABLE != 0U)
    if(sink->stress_writer == 0U)
    {
#if (XE_NP_APP_RAW_SD_LINKED_RETARGET_ENABLE != 0U)
        return XE_NP_App_RawSdSink_LaunchLinkedRetargetBatch(runtime, sink, port, port_index);
#else
        return XE_NP_App_RawSdSink_LaunchLinkedBatch(runtime, sink, port, port_index);
#endif
    }
#endif

    if(sink->stress_writer == 0U)
    {
#if (XE_NP_APP_RAW_SD_DMA_STAGING_ENABLE != 0U)
        if(XE_NP_App_RawSdSink_StartStage(
               sink, src, bytes_to_write, port_index, (uint16_t)blocks_to_write) != 0)
        {
            XE_NP_App_RecordRawSdDiag(
                XE_NP_APP_RAW_SD_DIAG_REASON_LAUNCH_FAIL,
                runtime, sink, port, port_index, HAL_ERROR);
            port->error_seen = 1U;
            XE_NP_App_RawSdSink_ReportStorageFault(runtime, sink, CE32_ERRORID_SD_ERR);
            return -1;
        }
        XE_NP_App_RecordRawSdDiag(
            XE_NP_APP_RAW_SD_DIAG_REASON_STAGE_START,
            runtime, sink, port, port_index, HAL_OK);
        /* The raw manager remains owned by this consumer until SD IDMA has
         * completed.  A successful staging launch is intentionally reported
         * as pending, not as a dequeued recording block. */
        return 0;
#else
        /*
         * Always snapshot into the sink scratch buffer before SD DMA. The raw
         * capture ring remains live while the card transfer is in flight; if
         * SD DMA reads directly from the ring, a later producer wrap can
         * overwrite that memory before overrun is noticed.
         */
        if(contiguous_blocks >= blocks_to_write)
        {
            XE_NP_Capture_DCacheInvalidate(src, bytes_to_write);
            memcpy(sink->scratch, src, bytes_to_write);
        }
        else
        {
            tail_bytes = bytes_to_write - contiguous;

            XE_NP_Capture_DCacheInvalidate(src, contiguous);
            XE_NP_Capture_DCacheInvalidate((const void *)&port->mgr->dataPtr[0], tail_bytes);
            memcpy(sink->scratch, src, contiguous);
            memcpy(
                &sink->scratch[contiguous],
                (const void *)&port->mgr->dataPtr[0],
                tail_bytes);
        }
#endif
    }
    XE_NP_App_DCacheClean(sink->scratch, bytes_to_write);

    XE_NP_Board_EnsureSdPowerRail();
    status = HAL_SD_WriteBlocks_DMA(
        runtime->hsd_card,
        sink->scratch,
        port->next_block,
        blocks_to_write);
raw_sd_write_started:
    if(status == HAL_BUSY)
    {
        XE_NP_App_RecordRawSdDiag(
            XE_NP_APP_RAW_SD_DIAG_REASON_LAUNCH_BUSY,
            runtime,
            sink,
            port,
            port_index,
            status);
        return 0;
    }
    if(status != HAL_OK)
    {
        XE_NP_App_RecordRawSdDiag(
            XE_NP_APP_RAW_SD_DIAG_REASON_LAUNCH_FAIL,
            runtime,
            sink,
            port,
            port_index,
            status);
        port->error_seen = 1U;
        XE_NP_App_RawSdSink_ReportStorageFault(runtime, sink, CE32_ERRORID_SD_ERR);
        return -1;
    }

    sink->write_in_flight = 1U;
    sink->write_complete = 0U;
    sink->write_error = 0U;
    sink->in_flight_port_index = port_index;
    sink->in_flight_blocks = (uint16_t)blocks_to_write;
    sink->in_flight_bytes = bytes_to_write;
    sink->in_flight_start_tick_ms = HAL_GetTick();
    XE_NP_App_RecordRawSdDiag(
        XE_NP_APP_RAW_SD_DIAG_REASON_LAUNCH_OK,
        runtime,
        sink,
        port,
        port_index,
        status);
    return (int)blocks_to_write;
}

static void XE_NP_App_RawSdSink_FinalizeInflightSuccess(
    XE_NP_AppRawSdSink *sink,
    XE_NP_AppRawSdPort *port)
{
    if((sink == NULL) || (port == NULL) ||
       ((port->mgr == NULL) && (sink->stress_writer == 0U)))
    {
        return;
    }

    if(sink->stress_writer == 0U)
    {
        dataMGR_deQueue(port->mgr, sink->in_flight_bytes, port->out_idx);
    }
    port->next_block += sink->in_flight_blocks;
    port->blocks_written += sink->in_flight_blocks;
    sink->total_blocks_written += sink->in_flight_blocks;
    XE_NP_App_RawSdSink_ClearInflight(sink);
}

static int XE_NP_App_RawSdSink_ServiceInflight(
    XE_NP_Runtime *runtime,
    XE_NP_AppRawSdSink *sink)
{
    XE_NP_AppRawSdPort *ports[2] = {&sink->gpioe, &sink->gpiod};
    XE_NP_AppRawSdPort *port;
    uint32_t sdmmc_flags;

    if((runtime == NULL) || (sink == NULL) || (sink->write_in_flight == 0U))
    {
        return 0;
    }

    if(sink->in_flight_port_index >= 2U)
    {
        XE_NP_App_RawSdSink_ClearInflight(sink);
        return 0;
    }

#if (XE_NP_APP_RAW_SD_LINKED_STREAM_ENABLE != 0U)
    if(sink->linked_active != 0U)
    {
        const uint32_t error_flags = SDMMC_FLAG_DCRCFAIL | SDMMC_FLAG_DTIMEOUT |
                                     SDMMC_FLAG_TXUNDERR | SDMMC_FLAG_IDMATE;

        port = ports[sink->in_flight_port_index];
        if((runtime->hsd_card == NULL) || (runtime->hsd_card->Instance == NULL) ||
           (port == NULL))
        {
            XE_NP_App_RawSdSink_ClearInflight(sink);
            sink->linked_active = 0U;
            XE_NP_App_RawSdSink_ReportStorageFault(runtime, sink, CE32_ERRORID_SD_ERR);
            return 0;
        }
        sdmmc_flags = runtime->hsd_card->Instance->STA;
        if((sdmmc_flags & error_flags) != 0U)
        {
            runtime->hsd_card->ErrorCode |= HAL_SD_ERROR_DMA;
            (void)XE_NP_App_RawSdSink_ForceStopDataPath(runtime->hsd_card);
            XE_NP_App_RawSdSink_ClearInflight(sink);
            sink->linked_active = 0U;
            port->error_seen = 1U;
            XE_NP_App_RawSdSink_ReportStorageFault(runtime, sink, CE32_ERRORID_SD_ERR);
            return 0;
        }
        if((sdmmc_flags & SDMMC_FLAG_IDMABTC) != 0U)
        {
            __HAL_SD_CLEAR_FLAG(runtime->hsd_card, SDMMC_FLAG_IDMABTC);
#if (XE_NP_APP_RAW_SD_LINKED_RETARGET_ENABLE != 0U)
            if(sink->linked_nodes_complete < (sink->linked_node_count * 2U))
#else
            if(sink->linked_nodes_complete < sink->linked_node_count)
#endif
            {
                ++sink->linked_nodes_complete;
            }
        }
        /* A node is retired only once IDMA has advanced beyond it.  This
         * promptly gives its SRAM3 span back to the GPIO producer while the
         * following node is transferred. */
        while(sink->linked_nodes_released < sink->linked_nodes_complete)
        {
            uint8_t node_index =
#if (XE_NP_APP_RAW_SD_LINKED_RETARGET_ENABLE != 0U)
                (uint8_t)(sink->linked_nodes_released % sink->linked_node_count);
#else
                sink->linked_nodes_released;
#endif
            XE_NP_AppRawSdPort *node_port = sink->linked_node_port[node_index];

            if((node_port == NULL) || (node_port->mgr == NULL))
            {
                (void)XE_NP_App_RawSdSink_ForceStopDataPath(runtime->hsd_card);
                XE_NP_App_RawSdSink_ClearInflight(sink);
                sink->linked_active = 0U;
                XE_NP_App_RawSdSink_ReportStorageFault(runtime, sink, CE32_ERRORID_SD_ERR);
                return 0;
            }
            dataMGR_deQueue(node_port->mgr, sink->linked_node_bytes[node_index],
                             node_port->out_idx);
#if (XE_NP_APP_RAW_SD_LINKED_RETARGET_ENABLE != 0U)
            if(sink->linked_reserved_bytes < sink->linked_node_bytes[node_index])
            {
                (void)XE_NP_App_RawSdSink_ForceStopDataPath(runtime->hsd_card);
                XE_NP_App_RawSdSink_ClearInflight(sink);
                sink->linked_active = 0U;
                XE_NP_App_RawSdSink_ReportStorageFault(runtime, sink, CE32_ERRORID_SD_ERR);
                return 0;
            }
            sink->linked_reserved_bytes -= sink->linked_node_bytes[node_index];
            if(sink->linked_nodes_released < sink->linked_node_count)
            {
                sink->linked_rearm_mask |= (uint8_t)(1U << node_index);
            }
#endif
            ++sink->linked_nodes_released;
            ++sink->linked_nodes_retired;
        }
#if (XE_NP_APP_RAW_SD_LINKED_RETARGET_ENABLE != 0U)
        /* Refill first-pass descriptors in chronological order.  The raw
         * manager's tail is outPTR plus the outstanding scheduled spans; no
         * live GPIO byte is read by the CPU.  Every descriptor is 61,440 B,
         * so a source span can never cross the 368,640-B physical ring. */
        for(uint32_t node_index = 0U;
            node_index < sink->linked_node_count;
            ++node_index)
        {
            uint8_t mask = (uint8_t)(1U << node_index);
            uint32_t available;
            uint32_t offset;

            if((sink->linked_rearm_mask & mask) == 0U)
            {
                continue;
            }
            available = dataMGR_BytesAvailable(port->mgr, port->out_idx);
            if(available < (sink->linked_reserved_bytes +
                            sink->linked_node_bytes[node_index]))
            {
                continue;
            }
            offset = ((uint32_t)port->mgr->outPTR[port->out_idx] +
                      sink->linked_reserved_bytes) % (uint32_t)port->mgr->dataSize;
            if((offset % XE_NP_APP_RAW_SD_RT_NODE_BYTES) != 0U)
            {
                (void)XE_NP_App_RawSdSink_ForceStopDataPath(runtime->hsd_card);
                XE_NP_App_RawSdSink_ClearInflight(sink);
                sink->linked_active = 0U;
                XE_NP_App_RawSdSink_ReportStorageFault(runtime, sink, CE32_ERRORID_SD_ERR);
                return 0;
            }
            sink->linked_nodes[node_index].IDMABASER =
                (uint32_t)&port->mgr->dataPtr[offset];
            sink->linked_nodes[node_index].IDMABSIZE = sink->linked_node_bytes[node_index];
            XE_NP_App_DCacheClean(&sink->linked_nodes[node_index],
                                  (uint32_t)sizeof(sink->linked_nodes[node_index]));
            sink->linked_reserved_bytes += sink->linked_node_bytes[node_index];
            sink->linked_rearm_mask &= (uint8_t)~mask;
            ++sink->linked_nodes_rearmed;
        }
#endif
        if(((sdmmc_flags & SDMMC_FLAG_DATAEND) != 0U) &&
           (sink->linked_data_end_seen == 0U))
        {
            __HAL_SD_CLEAR_FLAG(runtime->hsd_card, SDMMC_FLAG_DATAEND);
            XE_NP_App_RawSdSink_FinishLinkedNoCmd12(runtime->hsd_card);
            sink->linked_data_end_seen = 1U;
        }
        if(sink->linked_data_end_seen == 0U)
        {
            if(XE_NP_App_TimeReached(HAL_GetTick(), sink->in_flight_start_tick_ms +
                                     XE_NP_APP_RAW_SD_WRITE_TIMEOUT_MS) != 0)
            {
                (void)XE_NP_App_RawSdSink_ForceStopDataPath(runtime->hsd_card);
                XE_NP_App_RawSdSink_ClearInflight(sink);
                sink->linked_active = 0U;
                port->error_seen = 1U;
                XE_NP_App_RawSdSink_ReportStorageFault(runtime, sink, CE32_ERRORID_SD_ERR);
                return 0;
            }
            return 1;
        }
        if(HAL_SD_GetCardState(runtime->hsd_card) != HAL_SD_CARD_TRANSFER)
        {
            return 1;
        }
#if (XE_NP_APP_RAW_SD_LINKED_RETARGET_ENABLE != 0U)
        /* A retarget batch is valid only if each circular descriptor was
         * replaced once, then retired once more.  Never turn a missed Btc or
         * a late descriptor rewrite into duplicated on-card samples. */
        if((sink->linked_nodes_released != (sink->linked_node_count * 2U)) ||
           (sink->linked_nodes_rearmed != sink->linked_node_count) ||
           (sink->linked_rearm_mask != 0U) || (sink->linked_reserved_bytes != 0U))
        {
            port->error_seen = 1U;
            sink->linked_active = 0U;
            XE_NP_App_RawSdSink_ClearInflight(sink);
            XE_NP_App_RawSdSink_ReportStorageFault(runtime, sink, CE32_ERRORID_SD_ERR);
            return 0;
        }
#else
        /* DATAEND may arrive in the same foreground pass as the final Btc. */
        while(sink->linked_nodes_released < sink->linked_node_count)
        {
            uint8_t node_index = sink->linked_nodes_released;
            dataMGR_deQueue(port->mgr, sink->linked_node_bytes[node_index], port->out_idx);
            ++sink->linked_nodes_released;
            ++sink->linked_nodes_retired;
        }
#endif
        port->next_block += sink->in_flight_blocks;
        port->blocks_written += sink->in_flight_blocks;
        sink->total_blocks_written += sink->in_flight_blocks;
        sink->linked_last_batch_ms = HAL_GetTick() - sink->in_flight_start_tick_ms;
        if(sink->linked_last_batch_ms > sink->linked_worst_batch_ms)
        {
            sink->linked_worst_batch_ms = sink->linked_last_batch_ms;
        }
        ++sink->linked_batches_completed;
        XE_NP_App_RecordRawSdDiag(XE_NP_APP_RAW_SD_DIAG_REASON_WRITE_COMPLETE,
                                  runtime, sink, port, sink->in_flight_port_index, HAL_OK);
        sink->linked_active = 0U;
        XE_NP_App_RawSdSink_ClearInflight(sink);
        return 0;
    }
#endif

    if((runtime->hsd_card != NULL) && (runtime->hsd_card->Instance != NULL))
    {
        sdmmc_flags = runtime->hsd_card->Instance->STA;
        if((sdmmc_flags & (SDMMC_FLAG_DATAEND | SDMMC_FLAG_IDMABTC |
                           SDMMC_FLAG_DCRCFAIL | SDMMC_FLAG_DTIMEOUT |
                           SDMMC_FLAG_RXOVERR | SDMMC_FLAG_TXUNDERR)) != 0U)
        {
            HAL_SD_IRQHandler(runtime->hsd_card);
        }
    }

    if(XE_NP_App_TimeReached(
           HAL_GetTick(),
           sink->in_flight_start_tick_ms + XE_NP_APP_RAW_SD_WRITE_TIMEOUT_MS) != 0)
    {
        port = ports[sink->in_flight_port_index];
        XE_NP_App_RecordRawSdDiag(
            XE_NP_APP_RAW_SD_DIAG_REASON_TIMEOUT,
            runtime,
            sink,
            port,
            sink->in_flight_port_index,
            HAL_TIMEOUT);
        if(runtime->hsd_card != NULL)
        {
            XE_NP_Board_EnsureSdPowerRail();
            XE_NP_App_RawSdSink_ForceStopDataPath(runtime->hsd_card);
        }
        if(port != NULL)
        {
            port->error_seen = 1U;
            XE_NP_App_RawSdSink_ClearInflight(sink);
        }
        else
        {
            XE_NP_App_RawSdSink_ClearInflight(sink);
        }
        XE_NP_App_RawSdSink_ReportStorageFault(runtime, sink, CE32_ERRORID_SD_ERR);
        return 0;
    }

    if(sink->write_error != 0U)
    {
        port = ports[sink->in_flight_port_index];
        XE_NP_App_RecordRawSdDiag(
            XE_NP_APP_RAW_SD_DIAG_REASON_WRITE_ERROR,
            runtime,
            sink,
            port,
            sink->in_flight_port_index,
            HAL_ERROR);
        if(port != NULL)
        {
            port->error_seen = 1U;
            XE_NP_App_RawSdSink_ClearInflight(sink);
        }
        else
        {
            XE_NP_App_RawSdSink_ClearInflight(sink);
        }
        XE_NP_App_RawSdSink_ReportStorageFault(runtime, sink, CE32_ERRORID_SD_ERR);
        return 0;
    }

    if(sink->write_complete == 0U)
    {
        return 1;
    }

    port = ports[sink->in_flight_port_index];
    XE_NP_App_RecordRawSdDiag(
        XE_NP_APP_RAW_SD_DIAG_REASON_WRITE_COMPLETE,
        runtime,
        sink,
        port,
        sink->in_flight_port_index,
        HAL_OK);
    XE_NP_App_RawSdSink_FinalizeInflightSuccess(sink, port);
    return 0;
}

int XE_NP_App_StartRawSdSink(void)
{
    HAL_SD_CardInfoTypeDef card_info;
    XE_NP_AppRawSdPort *ports[2] = {&xe_np_raw_sd_sink.gpioe, &xe_np_raw_sd_sink.gpiod};
    XE_NP_CapturePort *capture_ports[2] = {
        &xe_np_runtime.capture.gpioe,
        &xe_np_runtime.capture.gpiod};
    XE_NP_AppDmaPort *dma_ports[2] = {&xe_np_dma_gpioe, &xe_np_dma_gpiod};
    uint32_t active_port_count = 0U;
    uint32_t total_raw_bytes_per_sec = 0U;
    uint32_t remaining_rate = 0U;
    uint32_t start_block = DATA_ADDR;
    uint32_t remaining_blocks;
    uint32_t active_port_idx = 0U;
    HAL_SD_CardStateTypeDef card_state;

    XE_NP_App_RawSdSink_Reset(&xe_np_raw_sd_sink);
    XE_NP_App_RecordRawSdDiag(
        XE_NP_APP_RAW_SD_DIAG_REASON_START_ENTER,
        &xe_np_runtime,
        &xe_np_raw_sd_sink,
        NULL,
        0xFFU,
        HAL_OK);

    if(XE_NP_Runtime_HasValidNpMclk(&xe_np_runtime) == 0)
    {
        XE_NP_App_RecordRawSdDiag(
            XE_NP_APP_RAW_SD_DIAG_REASON_START_FAIL_PRECONDITION,
            &xe_np_runtime,
            &xe_np_raw_sd_sink,
            NULL,
            0xFFU,
            HAL_ERROR);
        return -1;
    }
    if((xe_np_runtime.flags & XE_NP_RUNTIME_FLAG_NP_GLOBAL_RESET_RELEASED) == 0U)
    {
        XE_NP_App_RecordRawSdDiag(
            XE_NP_APP_RAW_SD_DIAG_REASON_START_FAIL_PRECONDITION,
            &xe_np_runtime,
            &xe_np_raw_sd_sink,
            NULL,
            0xFFU,
            HAL_ERROR);
        return -1;
    }
#if (XE_NP_APP_RAW_SD_DEDICATED_CAPTURE_ENABLE == 0U)
    if((xe_np_runtime.flags & XE_NP_RUNTIME_FLAG_NP_RECORD_RESET_RELEASED) == 0U)
    {
        XE_NP_App_RecordRawSdDiag(
            XE_NP_APP_RAW_SD_DIAG_REASON_START_FAIL_PRECONDITION,
            &xe_np_runtime,
            &xe_np_raw_sd_sink,
            NULL,
            0xFFU,
            HAL_ERROR);
        return -1;
    }
#if (XE_NP_APP_RAW_SD_DEDICATED_CAPTURE_ENABLE == 0U)
    if(xe_np_probe_state.initialized == 0U)
    {
        XE_NP_App_RecordRawSdDiag(
            XE_NP_APP_RAW_SD_DIAG_REASON_START_FAIL_PRECONDITION,
            &xe_np_runtime,
            &xe_np_raw_sd_sink,
            NULL,
            0xFFU,
            HAL_ERROR);
        return -1;
    }
#endif
    if(XE_NP_App_CaptureProducersRunning(&xe_np_runtime) == 0)
    {
        XE_NP_App_RecordRawSdDiag(
            XE_NP_APP_RAW_SD_DIAG_REASON_START_FAIL_PRECONDITION,
            &xe_np_runtime,
            &xe_np_raw_sd_sink,
            NULL,
            0xFFU,
            HAL_ERROR);
        return -1;
    }
#endif
    if(((xe_np_runtime.flags & XE_NP_RUNTIME_FLAG_SD_PRESENT) == 0U) ||
#if (XE_NP_APP_EXPERIMENT_ALLOW_UNQUALIFIED_SD_RAW == 0U)
       ((xe_np_runtime.flags & XE_NP_RUNTIME_FLAG_SD_UHS_READY) == 0U) ||
#endif
       (xe_np_sd_init_failed != 0U))
    {
        xe_np_sd_next_refresh_tick_ms = 0U;
        XE_NP_App_RefreshStorageStatus(&xe_np_runtime);
        if(((xe_np_runtime.flags & XE_NP_RUNTIME_FLAG_SD_PRESENT) == 0U) ||
#if (XE_NP_APP_EXPERIMENT_ALLOW_UNQUALIFIED_SD_RAW == 0U)
           ((xe_np_runtime.flags & XE_NP_RUNTIME_FLAG_SD_UHS_READY) == 0U) ||
#endif
           (xe_np_sd_init_failed != 0U))
        {
            XE_NP_App_RecordRawSdDiag(
                XE_NP_APP_RAW_SD_DIAG_REASON_START_FAIL_STORAGE,
                &xe_np_runtime,
                &xe_np_raw_sd_sink,
                NULL,
                0xFFU,
                HAL_ERROR);
            return -1;
        }
    }
    XE_NP_Board_EnsureSdPowerRail();
    if((xe_np_runtime.hsd_card == NULL) ||
       (card_state = HAL_SD_GetCardState(xe_np_runtime.hsd_card),
        ((card_state == HAL_SD_CARD_SENDING) ||
         (card_state == HAL_SD_CARD_RECEIVING) ||
         (card_state == HAL_SD_CARD_PROGRAMMING))))
    {
        if(xe_np_runtime.hsd_card != NULL)
        {
            XE_NP_App_RawSdSink_ForceStopDataPath(xe_np_runtime.hsd_card);
            card_state = HAL_SD_GetCardState(xe_np_runtime.hsd_card);
        }
    }
    if((xe_np_runtime.hsd_card == NULL) ||
       (card_state != HAL_SD_CARD_TRANSFER) ||
       (HAL_SD_GetCardInfo(xe_np_runtime.hsd_card, &card_info) != HAL_OK) ||
       (card_info.LogBlockNbr <= start_block))
    {
        XE_NP_App_RecordRawSdDiag(
            XE_NP_APP_RAW_SD_DIAG_REASON_START_FAIL_CARD,
            &xe_np_runtime,
            &xe_np_raw_sd_sink,
            NULL,
            0xFFU,
            HAL_ERROR);
        return -1;
    }

#if (XE_NP_APP_EXPERIMENT_SD_STRESS_WRITER_ENABLE != 0U)
    /*
     * This is intentionally not a recorder.  Its buffer lives in private
     * SRAM1 and is never populated from either live SRAM3 capture lane.  A
     * clean capture while this runs establishes that SDMMC traffic alone is
     * not the cause of a sampling-time failure.
     */
    remaining_blocks = card_info.LogBlockNbr - start_block;
    if(remaining_blocks < XE_NP_APP_RAW_SD_MAX_BLOCKS_PER_WRITE)
    {
        XE_NP_App_RecordRawSdDiag(
            XE_NP_APP_RAW_SD_DIAG_REASON_START_FAIL_CARD,
            &xe_np_runtime,
            &xe_np_raw_sd_sink,
            NULL,
            0xFFU,
            HAL_ERROR);
        return -1;
    }
    for(uint32_t i = 0U; i < sizeof(xe_np_raw_sd_sink.scratch); ++i)
    {
        xe_np_raw_sd_sink.scratch[i] = (uint8_t)(i ^ (i >> 8U) ^ 0xA5U);
    }
    xe_np_raw_sd_sink.stress_writer = 1U;
    ports[0]->next_block = start_block;
    ports[0]->end_block = card_info.LogBlockNbr;
    ports[0]->active = 1U;
    goto raw_sd_sink_start;
#endif

    for(uint32_t i = 0U; i < 2U; ++i)
    {
        if((dma_ports[i]->active != 0U) &&
           (capture_ports[i]->storage != NULL) &&
           (capture_ports[i]->raw_bytes_per_sec != 0U))
        {
            active_port_count++;
            total_raw_bytes_per_sec += capture_ports[i]->raw_bytes_per_sec;
        }
    }
    if((active_port_count == 0U) || (total_raw_bytes_per_sec == 0U))
    {
        XE_NP_App_RecordRawSdDiag(
            XE_NP_APP_RAW_SD_DIAG_REASON_START_FAIL_NO_PORT,
            &xe_np_runtime,
            &xe_np_raw_sd_sink,
            NULL,
            0xFFU,
            HAL_ERROR);
        return -1;
    }

    remaining_blocks = card_info.LogBlockNbr - start_block;
    if(remaining_blocks < active_port_count)
    {
        XE_NP_App_RecordRawSdDiag(
            XE_NP_APP_RAW_SD_DIAG_REASON_START_FAIL_CARD,
            &xe_np_runtime,
            &xe_np_raw_sd_sink,
            NULL,
            0xFFU,
            HAL_ERROR);
        return -1;
    }
    remaining_rate = total_raw_bytes_per_sec;

    for(uint32_t i = 0U; i < 2U; ++i)
    {
        XE_NP_CapturePort *capture_port = capture_ports[i];
        XE_NP_AppRawSdPort *port = ports[i];
        uint32_t port_blocks;
        uint32_t port_rate;

        if((dma_ports[i]->active == 0U) ||
           (capture_port->storage == NULL) ||
           (capture_port->raw_bytes_per_sec == 0U))
        {
            continue;
        }

        port_rate = capture_port->raw_bytes_per_sec;
        active_port_idx++;
        if((active_port_idx >= active_port_count) || (remaining_rate <= port_rate))
        {
            port_blocks = remaining_blocks;
        }
        else
        {
            uint64_t weighted_blocks =
                ((uint64_t)remaining_blocks * (uint64_t)port_rate) / (uint64_t)remaining_rate;
            port_blocks = (uint32_t)weighted_blocks;
        }
        if(port_blocks == 0U)
        {
            XE_NP_App_RecordRawSdDiag(
                XE_NP_APP_RAW_SD_DIAG_REASON_START_FAIL_CARD,
                &xe_np_runtime,
                &xe_np_raw_sd_sink,
                port,
                (uint8_t)i,
                HAL_ERROR);
            return -1;
        }

        port->mgr = &capture_port->raw_mgr;
        port->out_idx = 0U;
        port->next_block = start_block;
        port->end_block = start_block + port_blocks;
        port->active = 1U;
        /* The dedicated raw-SD image pre-arms this consumer before capture
         * starts. Preserve its producer-relative pointer at sink hand-off. */
        if((port->mgr->activeOutMask & (1UL << port->out_idx)) == 0U)
        {
            dataMGR_ArmConsumer(port->mgr, port->out_idx);
        }
        port->mgr->logState |= MGR_STATE_LOGGING;
        start_block = port->end_block;
        remaining_blocks -= port_blocks;
        remaining_rate -= port_rate;
    }

raw_sd_sink_start:
    HAL_NVIC_SetPriority(SDMMC1_IRQn, XE_NP_APP_SD_IRQ_PREEMPT_PRIO, XE_NP_APP_SD_IRQ_SUBPRIO);
    HAL_NVIC_EnableIRQ(SDMMC1_IRQn);
    xe_np_raw_sd_sink.active = 1U;
    xe_np_raw_sd_sink.error_reported = 0U;
    xe_np_raw_sd_sink.next_port_index = 0U;
    XE_NP_App_RawSdSink_ClearInflight(&xe_np_raw_sd_sink);
    XE_NP_App_RecordRawSdDiag(
        XE_NP_APP_RAW_SD_DIAG_REASON_START_OK,
        &xe_np_runtime,
        &xe_np_raw_sd_sink,
        NULL,
        0xFFU,
        HAL_OK);
    return 0;
}

void XE_NP_App_StopRawSdSink(void)
{
    XE_NP_App_RawSdSink_Reset(&xe_np_raw_sd_sink);
}

int XE_NP_App_IsRawSdSinkActive(void)
{
    return (xe_np_raw_sd_sink.active != 0U) ? 1 : 0;
}

static void XE_NP_App_ServiceRawSdSink(XE_NP_Runtime *runtime)
{
    XE_NP_AppRawSdPort *ports[2] = {&xe_np_raw_sd_sink.gpioe, &xe_np_raw_sd_sink.gpiod};
    uint32_t blocks_remaining = XE_NP_APP_RAW_SD_BLOCKS_PER_SERVICE;
    uint8_t start_port_index;

#if (XE_NP_APP_RAW_SD_LINKED_STREAM_ENABLE != 0U)
    /* A linked CMD23 batch is deliberately larger than the ordinary
     * scheduler quantum.  It owns SDMMC until DATAEND, so never let the
     * generic two-port loop interpret its 508 blocks as a 128-block budget
     * subtraction and attempt a second concurrent launch. */
    /* The circular retarget transfer consumes two 4-node passes (960 blocks),
     * not the finite linked-list batch's 508 blocks.  This budget is only a
     * scheduler ownership token, but keeping it exact prevents an unsigned
     * underflow from masking a future control-flow change. */
#if (XE_NP_APP_RAW_SD_LINKED_RETARGET_ENABLE != 0U)
    blocks_remaining = XE_NP_APP_RAW_SD_RT_BATCH_BLOCKS;
#else
    blocks_remaining = XE_NP_APP_RAW_SD_LL_BATCH_BLOCKS;
#endif
#endif

    if((runtime == NULL) || (xe_np_raw_sd_sink.active == 0U))
    {
        return;
    }
    xe_np_raw_sd_sink.service_call_count++;
    xe_np_raw_sd_sink.service_state = 1U; /* entered */

    if((xe_np_probe_state.initialized == 0U) ||
       ((runtime->flags & XE_NP_RUNTIME_FLAG_NP_GLOBAL_RESET_RELEASED) == 0U) ||
       ((runtime->flags & XE_NP_RUNTIME_FLAG_NP_RECORD_RESET_RELEASED) == 0U) ||
       (XE_NP_Runtime_HasValidNpMclk(runtime) == 0) ||
       (XE_NP_App_CaptureProducersRunning(runtime) == 0))
    {
        xe_np_raw_sd_sink.service_state = 2U; /* producer precondition */
        /* Preserve the actual reason and raw-ring occupancy before reset.
         * Previously a Start-OK diagnostic could be the last observable state
         * even when no GPIO producer ever generated a full SD batch. */
        XE_NP_App_RecordRawSdDiag(XE_NP_APP_RAW_SD_DIAG_REASON_PRODUCER_STOP,
                                  runtime, &xe_np_raw_sd_sink, NULL, 0xFFU, HAL_ERROR);
        XE_NP_App_StopRawSdSink();
        return;
    }

    if(XE_NP_App_RawSdSink_ServiceInflight(runtime, &xe_np_raw_sd_sink) != 0)
    {
        xe_np_raw_sd_sink.service_state = 3U; /* SD IDMA active */
        return;
    }
    if(xe_np_raw_sd_sink.active == 0U)
    {
        xe_np_raw_sd_sink.service_state = 4U; /* sink stopped */
        return;
    }

#if (XE_NP_APP_RAW_SD_DMA_STAGING_ENABLE != 0U)
    if(xe_np_raw_sd_sink.stage_error != 0U)
    {
        xe_np_raw_sd_sink.service_state = 5U; /* stage DMA error */
        XE_NP_App_RawSdSink_ReportStorageFault(
            runtime, &xe_np_raw_sd_sink, CE32_ERRORID_SD_ERR);
        return;
    }
    if(xe_np_raw_sd_sink.stage_busy != 0U)
    {
        xe_np_raw_sd_sink.service_state = 6U; /* stage DMA active */
        return;
    }
    if(xe_np_raw_sd_sink.stage_complete != 0U)
    {
        xe_np_raw_sd_sink.service_state = 7U; /* stage ready for SDMMC */
        /* Do not start SD IDMA from the GPDMA ISR. Launch it here after the
         * staging completion is visible and cache-cleaned. */
        int stage_rc = XE_NP_App_RawSdSink_LaunchStagedWrite(
            runtime, &xe_np_raw_sd_sink);
        if(stage_rc < 0)
        {
            XE_NP_App_RawSdSink_ReportStorageFault(
                runtime, &xe_np_raw_sd_sink, CE32_ERRORID_SD_ERR);
        }
        return;
    }
#endif

    if((xe_np_raw_sd_sink.write_in_flight == 0U) &&
       ((xe_np_raw_sd_sink.last_telemetry_tick_ms == 0U) ||
        ((HAL_GetTick() - xe_np_raw_sd_sink.last_telemetry_tick_ms) >= 250U)))
    {
        xe_np_raw_sd_sink.last_telemetry_tick_ms = HAL_GetTick();
        XE_NP_App_RecordRawSdDiag(XE_NP_APP_RAW_SD_DIAG_REASON_WAIT_DATA,
                                  runtime, &xe_np_raw_sd_sink, NULL, 0xFFU, HAL_OK);
    }

    if((runtime->hsd_card == NULL) ||
       ((runtime->flags & XE_NP_RUNTIME_FLAG_SD_PRESENT) == 0U) ||
#if (XE_NP_APP_EXPERIMENT_ALLOW_UNQUALIFIED_SD_RAW == 0U)
       ((runtime->flags & XE_NP_RUNTIME_FLAG_SD_UHS_READY) == 0U) ||
#endif
       (xe_np_sd_init_failed != 0U))
    {
        xe_np_raw_sd_sink.service_state = 8U; /* SD precondition */
        XE_NP_App_RawSdSink_ReportStorageFault(runtime, &xe_np_raw_sd_sink, CE32_ERRORID_SD_ERR);
        return;
    }

    if(HAL_SD_GetCardState(runtime->hsd_card) != HAL_SD_CARD_TRANSFER)
    {
        xe_np_raw_sd_sink.service_state = 9U; /* card busy */
        return;
    }

    start_port_index = (uint8_t)(xe_np_raw_sd_sink.next_port_index & 0x01U);
    xe_np_raw_sd_sink.service_state = 10U; /* launching/staging */
    xe_np_raw_sd_sink.next_port_index ^= 0x01U;

    for(uint32_t i = 0U; (i < 2U) && (blocks_remaining > 0U); ++i)
    {
        uint32_t port_index = (uint32_t)((start_port_index + i) & 0x01U);
        XE_NP_AppRawSdPort *port = ports[port_index];

        while((port->active != 0U) && (blocks_remaining > 0U))
        {
            if(XE_NP_App_RawSdSink_BacklogAtLossRisk(port) != 0)
            {
                port->error_seen = 1U;
                XE_NP_App_RawSdSink_ReportStorageFault(
                    runtime,
                    &xe_np_raw_sd_sink,
                    CE32_ERRORID_SD_ERR);
                return;
            }

            int rc = XE_NP_App_RawSdSink_LaunchWrite(
                runtime,
                &xe_np_raw_sd_sink,
                port,
                (uint8_t)port_index,
                blocks_remaining);

            if(rc == 0)
            {
#if (XE_NP_APP_RAW_SD_DMA_STAGING_ENABLE != 0U)
                /* A successful raw-lane launch consumes the sole Channel 14
                 * staging engine. Do not fall through to the other lane in
                 * this pass: StartStage would correctly reject the busy
                 * channel, but that is normal back-pressure rather than a
                 * storage failure. The next service pass launches SD once
                 * this stage completes. */
                if((xe_np_raw_sd_sink.stage_busy != 0U) ||
                   (xe_np_raw_sd_sink.stage_complete != 0U))
                {
                    return;
                }
#endif
                break;
            }
            if(rc == -2)
            {
                port->error_seen = 1U;
                XE_NP_App_RawSdSink_ReportStorageFault(
                    runtime,
                    &xe_np_raw_sd_sink,
                    CE32_ERRORID_SD_CARDFULL);
                return;
            }
            if(rc < 0)
            {
                port->error_seen = 1U;
                XE_NP_App_RawSdSink_ReportStorageFault(
                    runtime,
                    &xe_np_raw_sd_sink,
                    CE32_ERRORID_SD_ERR);
                return;
            }

            blocks_remaining -= (uint32_t)rc;
            xe_np_raw_sd_sink.next_port_index = (uint8_t)((port_index + 1U) & 0x01U);
#if (XE_NP_APP_RAW_SD_LINKED_STREAM_ENABLE != 0U)
            return;
#endif
            break;
        }
    }

    if((xe_np_raw_sd_sink.gpioe.active == 0U) && (xe_np_raw_sd_sink.gpiod.active == 0U))
    {
        xe_np_raw_sd_sink.active = 0U;
    }
}

static void XE_NP_App_StopCaptureDmaPort(XE_NP_AppDmaPort *port)
{
    if(port == NULL)
    {
        return;
    }

    if(port->htim != NULL)
    {
        uint32_t dma_mask = (port->timer_dma_mask != 0U) ?
            port->timer_dma_mask :
            (TIM_DMA_UPDATE | TIM_DMA_CC1);
        __HAL_TIM_DISABLE_DMA(port->htim, dma_mask);
        (void)HAL_TIM_Base_Stop(port->htim);
        __HAL_TIM_DISABLE(port->htim);
        __HAL_TIM_CLEAR_FLAG(port->htim, TIM_FLAG_UPDATE | TIM_FLAG_CC1 | TIM_FLAG_CC2);
    }

    if(port->hdma.Instance != NULL)
    {
        HAL_NVIC_DisableIRQ(port->irqn);
        if(((port->hdma.Mode & DMA_LINKEDLIST) == 0U) &&
           (port->direct_repeat_mode != 0U))
        {
            (void)HAL_DMA_DeInit(&port->hdma);
        }
        else
        {
            (void)HAL_DMAEx_List_DeInit(&port->hdma);
        }
    }

    XE_NP_App_ResetCapturePortRuntimeState(port->capture_port);

    memset(port, 0, sizeof(*port));
}

#if (XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_INTERLEAVE_ENABLE != 0U)
/* NP_CAPTURE_HARD_STOP */
#include "np_capture_timer_stop.inc"
#if (XE_NP_APP_EXPERIMENT_GPIO_FULLPORT_QUAD_STRESS_ENABLE != 0U)
static void XE_NP_App_StopQuadDmaAuxTimers(void)
{
    if(xe_np_quad_dma_aux_mask & 2U)
    {
        TIM3->DIER = 0U;
        TIM3->CR1 &= ~TIM_CR1_CEN;
    }
    if(xe_np_quad_dma_aux_mask & 4U)
    {
        TIM4->DIER = 0U;
        TIM4->CR1 &= ~TIM_CR1_CEN;
    }
    xe_np_quad_dma_aux_mask = 0U;
}
#endif
static void XE_NP_App_StopGpioeQuadDmaInterleave(void)
{
    /* Stop DMA request generation before resetting any channel descriptor. */
    __HAL_TIM_DISABLE_DMA(
        &htim8,
        TIM_DMA_UPDATE | TIM_DMA_CC1 | TIM_DMA_CC2 | TIM_DMA_CC3 | TIM_DMA_CC4);
    np_capture_timer_stop(htim8.Instance);
    (void)HAL_TIM_Base_Stop(&htim8);
    __HAL_TIM_DISABLE(&htim8);
    __HAL_TIM_CLEAR_FLAG(
        &htim8,
        TIM_FLAG_UPDATE | TIM_FLAG_CC1 | TIM_FLAG_CC2 | TIM_FLAG_CC3 | TIM_FLAG_CC4);
    __HAL_TIM_DISABLE_DMA(&htim2, TIM_DMA_UPDATE | TIM_DMA_CC1 | TIM_DMA_CC2 | TIM_DMA_CC3);
    np_capture_timer_stop(htim2.Instance);
    (void)HAL_TIM_Base_Stop(&htim2);
    __HAL_TIM_DISABLE(&htim2);
    __HAL_TIM_CLEAR_FLAG(&htim2, TIM_FLAG_UPDATE | TIM_FLAG_CC1 | TIM_FLAG_CC2 | TIM_FLAG_CC3);
#if (XE_NP_APP_EXPERIMENT_GPIO_FULLPORT_QUAD_STRESS_ENABLE != 0U)
    XE_NP_App_StopQuadDmaAuxTimers();
#endif

    for(uint32_t lane = 0U; lane < XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT;
        ++lane)
    {
        if(xe_np_quad_dma_lanes[lane].hdma.Instance != NULL)
        {
            (void)HAL_DMA_Abort(&xe_np_quad_dma_lanes[lane].hdma);
            (void)HAL_DMAEx_List_DeInit(&xe_np_quad_dma_lanes[lane].hdma);
        }
    }
    memset(xe_np_quad_dma_lanes, 0, sizeof(xe_np_quad_dma_lanes));
#if (XE_NP_APP_EXPERIMENT_GPIO_FULLPORT_QUAD_STRESS_ENABLE != 0U)
    for(uint32_t lane = 0U; lane < XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT;
        ++lane)
    {
        if(xe_np_fullport_quad_dma_gpioe_high_lanes[lane].hdma.Instance != NULL)
        {
            (void)HAL_DMA_Abort(&xe_np_fullport_quad_dma_gpioe_high_lanes[lane].hdma);
            (void)HAL_DMAEx_List_DeInit(&xe_np_fullport_quad_dma_gpioe_high_lanes[lane].hdma);
        }
        if(xe_np_fullport_quad_dma_gpiod_high_lanes[lane].hdma.Instance != NULL)
        {
            (void)HAL_DMA_Abort(&xe_np_fullport_quad_dma_gpiod_high_lanes[lane].hdma);
            (void)HAL_DMAEx_List_DeInit(&xe_np_fullport_quad_dma_gpiod_high_lanes[lane].hdma);
        }
    }
    memset(xe_np_fullport_quad_dma_gpioe_high_lanes, 0,
           sizeof(xe_np_fullport_quad_dma_gpioe_high_lanes));
    memset(xe_np_fullport_quad_dma_gpiod_high_lanes, 0,
           sizeof(xe_np_fullport_quad_dma_gpiod_high_lanes));
#endif
#if (XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_TIMED_SHADOW_ENABLE != 0U)
    for(uint32_t lane = 0U; lane < XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT;
        ++lane)
    {
        if(xe_np_quad_dma_timed_shadow_lanes[lane].hdma.Instance != NULL)
        {
            (void)HAL_DMA_Abort(&xe_np_quad_dma_timed_shadow_lanes[lane].hdma);
            (void)HAL_DMAEx_List_DeInit(&xe_np_quad_dma_timed_shadow_lanes[lane].hdma);
        }
    }
    memset(xe_np_quad_dma_timed_shadow_lanes, 0,
           sizeof(xe_np_quad_dma_timed_shadow_lanes));
#endif
    xe_np_quad_dma_active = 0U;
#if (XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_INTERLEAVE_USB_ENABLE != 0U)
    xe_np_quad_dma_usb_cursor_valid = 0U;
    xe_np_quad_dma_usb_next_lane = 0U;
    xe_np_quad_dma_usb_batch_claimed = 0U;
#endif
}

#if (XE_NP_APP_EXPERIMENT_FREEZE_QUIESCE_ONLY != 0U)
static void XE_NP_App_QuiesceGpioeQuadDmaInterleaveForSnapshot(void)
{
    /* Gate the request divider first. Disabling DIER alone does not drain
     * an already accepted DMA transaction. Suspend without RESET, wait for
     * hardware acknowledgement, and only then read heads. The last partial
     * timer cycle is not a complete chronological quartet: trim it offline. */
    uint32_t freeze_state = 1U;
    CLEAR_BIT(htim8.Instance->CR1, TIM_CR1_CEN);
    CLEAR_BIT(htim2.Instance->CR1, TIM_CR1_CEN);
    __HAL_TIM_DISABLE_DMA(
        &htim8,
        TIM_DMA_UPDATE | TIM_DMA_CC1 | TIM_DMA_CC2 | TIM_DMA_CC3 | TIM_DMA_CC4);
    __DSB();
    xe_np_quad_dma_freeze_diag[0] = 0x51314633UL; /* Q1F3 */
    xe_np_quad_dma_freeze_diag[1] = 0U;
    xe_np_quad_dma_freeze_diag[2] = htim8.Instance->CNT;
    for(uint32_t lane = 0U;
        lane < XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT; ++lane)
    {
        DMA_Channel_TypeDef *channel = xe_np_quad_dma_lanes[lane].hdma.Instance;
        if(channel != NULL) { SET_BIT(channel->CCR, DMA_CCR_SUSP); }
    }
    for(uint32_t lane = 0U;
        lane < XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT;
        ++lane)
    {
        DMA_Channel_TypeDef *channel = xe_np_quad_dma_lanes[lane].hdma.Instance;
        uint32_t budget = 100000U; /* bounded even with interrupts disabled */
        if(channel != NULL)
        {
            while(((channel->CSR & DMA_CSR_SUSPF) == 0U) && (--budget != 0U)) {}
            if(budget == 0U) { freeze_state = 2U; }
            if((channel->CSR & (DMA_CSR_DTEF | DMA_CSR_ULEF |
                                DMA_CSR_USEF | DMA_CSR_TOF)) != 0U)
            { freeze_state = 3U; }
            __DSB();
            xe_np_quad_dma_freeze_diag[3U + lane * 4U] = channel->CDAR;
            xe_np_quad_dma_freeze_diag[4U + lane * 4U] = channel->CBR1;
            xe_np_quad_dma_freeze_diag[5U + lane * 4U] = channel->CSR;
            xe_np_quad_dma_freeze_diag[6U + lane * 4U] = channel->CLLR;
            xe_np_quad_dma_freeze_diag[23U + lane] = channel->CTR1;
            xe_np_quad_dma_freeze_diag[3U + 4U * XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT + lane] =
                xe_np_quad_dma_initial_offsets[lane];
        }
        else { freeze_state = 3U; }
        xe_np_quad_dma_head_snapshot[lane] =
            (xe_np_quad_dma_lanes[lane].hdma.Instance != NULL) ?
                xe_np_quad_dma_lanes[lane].hdma.Instance->CDAR : 0U;
#if (XE_NP_APP_EXPERIMENT_GPIO_FULLPORT_QUAD_STRESS_ENABLE != 0U)
        xe_np_fullport_quad_dma_gpioe_high_head_snapshot[lane] =
            (xe_np_fullport_quad_dma_gpioe_high_lanes[lane].hdma.Instance != NULL) ?
                xe_np_fullport_quad_dma_gpioe_high_lanes[lane].hdma.Instance->CDAR : 0U;
        xe_np_fullport_quad_dma_gpiod_high_head_snapshot[lane] =
            (xe_np_fullport_quad_dma_gpiod_high_lanes[lane].hdma.Instance != NULL) ?
                xe_np_fullport_quad_dma_gpiod_high_lanes[lane].hdma.Instance->CDAR : 0U;
#endif
    }
    (void)HAL_TIM_Base_Stop(&htim8);
    __HAL_TIM_DISABLE(&htim8);
    __HAL_TIM_CLEAR_FLAG(
        &htim8,
        TIM_FLAG_UPDATE | TIM_FLAG_CC1 | TIM_FLAG_CC2 | TIM_FLAG_CC3 | TIM_FLAG_CC4);
    __HAL_TIM_DISABLE_DMA(
        &htim2,
        TIM_DMA_UPDATE | TIM_DMA_CC1 | TIM_DMA_CC2 | TIM_DMA_CC3 | TIM_DMA_CC4);
    (void)HAL_TIM_Base_Stop(&htim2);
    __HAL_TIM_DISABLE(&htim2);
    __HAL_TIM_CLEAR_FLAG(
        &htim2,
        TIM_FLAG_UPDATE | TIM_FLAG_CC1 | TIM_FLAG_CC2 | TIM_FLAG_CC3 | TIM_FLAG_CC4);
    __DMB();
    xe_np_quad_dma_freeze_diag[1] = freeze_state;
    xe_np_quad_dma_active = 0U;
}
#endif
#endif

static void XE_NP_App_StopCaptureDma(void)
{
    XE_NP_App_StopCaptureDmaPort(&xe_np_dma_gpioe);
    XE_NP_App_StopCaptureDmaPort(&xe_np_dma_gpiod);
#if (XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_INTERLEAVE_ENABLE != 0U)
    XE_NP_App_StopGpioeQuadDmaInterleave();
#endif
#if (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_ENABLE != 0U)
    /* TIM2 is the edge-locked master in the dual-lane bench, not a DMA port. */
    __HAL_TIM_DISABLE_DMA(&htim2, TIM_DMA_UPDATE | TIM_DMA_CC1 | TIM_DMA_CC2 | TIM_DMA_CC3);
    (void)HAL_TIM_Base_Stop(&htim2);
    __HAL_TIM_DISABLE(&htim2);
    __HAL_TIM_CLEAR_FLAG(&htim2, TIM_FLAG_UPDATE | TIM_FLAG_CC1 | TIM_FLAG_CC2 | TIM_FLAG_CC3);
#endif
    XE_NP_App_ResetCaptureRuntimeState(&xe_np_runtime.capture);
    xe_np_dma_start_error_seen = 0U;
    xe_np_capture_validation_enabled = 0U;
    xe_np_capture_live_decode_probe_mask = 0U;
    XE_NP_App_RefreshDmaStatus();
}

static HAL_StatusTypeDef XE_NP_App_PrepareTimerForUpdateCapture(TIM_HandleTypeDef *htim)
{
    if(htim == NULL)
    {
        return HAL_ERROR;
    }

    __HAL_TIM_DISABLE_DMA(htim, TIM_DMA_UPDATE | TIM_DMA_CC1 | TIM_DMA_CC2);
    (void)HAL_TIM_Base_Stop(htim);
    __HAL_TIM_DISABLE(htim);
    __HAL_TIM_SET_COUNTER(htim, 0U);
    __HAL_TIM_SET_AUTORELOAD(htim, 0U);
    SET_BIT(htim->Instance->CR1, TIM_CR1_URS);
    htim->Instance->EGR = TIM_EGR_UG;
    __HAL_TIM_CLEAR_FLAG(htim, TIM_FLAG_UPDATE | TIM_FLAG_CC1 | TIM_FLAG_CC2);
    return HAL_OK;
}

static HAL_StatusTypeDef XE_NP_App_PrepareTimerForTim2Ch1Capture(TIM_HandleTypeDef *htim)
{
    TIM_TypeDef *tim;
    const uint32_t capture_delay_ticks = (np_mode_hz >= 20000U) ? 2U :
        (((np_mode_hz == 15000U) && (np_mode_mask & 4U) && (np_mode_mask & 3U)) ?
            XE_NP_APP_GPIOE_CAPTURE_TIM2_DELAY_TICKS : 7U);
#if (XE_NP_APP_GPIOE_CAPTURE_TIM2_DELAYED_DMA_ENABLE == 0U) && \
    (XE_NP_APP_EXPERIMENT_TIM2_PSB_ARMED_FREERUN_ENABLE == 0U) && \
    (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_ENABLE == 0U)
    TIM_ClockConfigTypeDef clock_config;
#endif

    if((htim == NULL) || (htim->Instance != TIM2))
    {
        return HAL_ERROR;
    }

    XE_NP_App_ConfigureNp2PsbBusPull();
#if (XE_NP_APP_EXPERIMENT_GPIOE_CAPTURE_TIM2_PSB_ETR_ENABLE != 0U)
    XE_NP_App_ConfigurePsbPa5ForTim2Etr();
#else
    XE_NP_App_ConfigurePsbPa0ForTim2Ch1();
#endif
    __HAL_TIM_DISABLE_DMA(
        htim,
        TIM_DMA_UPDATE | TIM_DMA_CC1 | TIM_DMA_CC2 | TIM_DMA_CC3 | TIM_DMA_CC4);
    (void)HAL_TIM_Base_Stop(htim);
    __HAL_TIM_DISABLE(htim);
    tim = htim->Instance;
    np_capture_timer_stop(tim);

#if (XE_NP_APP_EXPERIMENT_TIM2_PSB_ARMED_FREERUN_ENABLE != 0U) || \
    (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_ENABLE != 0U) || \
    (XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_INTERLEAVE_ENABLE != 0U)
#if (((XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_ENABLE != 0U) && \
      (XE_NP_APP_EXPERIMENT_TIM2_PSB_ARMED_FREERUN_ENABLE == 0U)) || \
     (XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_INTERLEAVE_ENABLE != 0U) || \
     (XE_NP_APP_EXPERIMENT_TIM2_RESET_EACH_PSB_EDGE_ENABLE != 0U))
    /*
     * Keep the proven per-edge TIM2 reset timing. TIM8 counts RISING OC2REF
     * edges: PWM1 rises when TIM2 resets (CCR2 changes pulse width, NOT that
     * sampling edge). PWM2 rises at compare and is the real delay sweep.
     * Keep PWM1 as the historical control; do not interpret it as a phase sweep.
     */
    tim->DIER &= ~(TIM_DIER_UDE | TIM_DIER_CC1DE | TIM_DIER_CC2DE |
                   TIM_DIER_CC3DE | TIM_DIER_CC4DE);
    tim->CCER &= ~(TIM_CCER_CC1E | TIM_CCER_CC2E | TIM_CCER_CC3E | TIM_CCER_CC4E);
    tim->SMCR = 0U;
    MODIFY_REG(tim->CR2, TIM_CR2_MMS, TIM_TRGO_OC2REF);
    tim->PSC = 0U;
    tim->ARR = 0xFFFFU;
    tim->CNT = 0U;
    tim->CCR1 = 0U;
    tim->CCR2 = capture_delay_ticks;
#if (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_PHASE_PROBE_ENABLE != 0U)
    /* Both compare requests share the PA0-locked timer epoch, but have
     * independently calibrated sampling positions inside the DDR eye. */
    tim->CCR2 = XE_NP_APP_GPIOE_DDR_PHASE_A_DELAY_TICKS;
    tim->CCR3 = XE_NP_APP_GPIOE_DDR_PHASE_B_DELAY_TICKS;
#else
    tim->CCR3 = 0U;
#endif
    MODIFY_REG(
        tim->CCMR1,
        TIM_CCMR1_CC1S | TIM_CCMR1_IC1PSC | TIM_CCMR1_IC1F |
            TIM_CCMR1_CC2S | TIM_CCMR1_OC2M | TIM_CCMR1_OC2PE | TIM_CCMR1_OC2FE,
        TIM_CCMR1_CC1S_0 |
#if (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_DELAYED_PWM2_ENABLE != 0U)
            TIM_CCMR1_OC2M_2 | TIM_CCMR1_OC2M_1 | TIM_CCMR1_OC2M_0 |
#else
            TIM_CCMR1_OC2M_2 | TIM_CCMR1_OC2M_1 |
#endif
            ((xe_np_gpioe_capture_timing_filter & 0x0FU) << TIM_CCMR1_IC1F_Pos));
    MODIFY_REG(
        tim->CCER,
        TIM_CCER_CC1P | TIM_CCER_CC1NP | TIM_CCER_CC2P | TIM_CCER_CC2NP,
        (xe_np_gpioe_capture_timing_rising_edge != 0U) ? 0U : TIM_CCER_CC1P);
#if (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_PHASE_PROBE_ENABLE != 0U)
    if((XE_NP_APP_GPIOE_DDR_PHASE_A_DELAY_TICKS >= 16U) ||
       (XE_NP_APP_GPIOE_DDR_PHASE_B_DELAY_TICKS >= 16U))
#else
    if(XE_NP_APP_GPIOE_CAPTURE_TIM2_DELAY_TICKS >= 16U)
#endif
    {
        return HAL_ERROR;
    }
    tim->SMCR = TIM_SLAVEMODE_RESET;
#if (XE_NP_APP_EXPERIMENT_GPIOE_CAPTURE_TIM2_PSB_ETR_ENABLE != 0U)
    tim->SMCR |= TIM_TS_ETRF;
    SET_BIT(tim->CCER, TIM_CCER_CC2E);
#else
    tim->SMCR |= TIM_TS_TI1FP1;
    SET_BIT(tim->CCER, TIM_CCER_CC1E | TIM_CCER_CC2E
#if (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_PHASE_PROBE_ENABLE != 0U)
            | TIM_CCER_CC3E
#endif
    );
#endif
#else
    /*
     * Lock once to PA0, then generate the sampling pulse from TIM2's local
     * clock.  In the dual-lane configuration TIM8 still divides that pulse
     * stream between its two DMA requests.  The previous preprocessor branch
     * selected the reset-on-every-edge setup whenever dual DMA was enabled,
     * silently defeating this jitter-isolation experiment.
     */
    tim->DIER &= ~(TIM_DIER_UDE | TIM_DIER_CC1DE | TIM_DIER_CC2DE | TIM_DIER_CC3DE);
    tim->CCER &= ~(TIM_CCER_CC1E | TIM_CCER_CC2E | TIM_CCER_CC3E);
    tim->SMCR = 0U;
    MODIFY_REG(
        tim->CR2,
        TIM_CR2_MMS,
#if (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_ENABLE != 0U)
        TIM_TRGO_OC2REF
#else
        TIM_TRGO_RESET
#endif
    );
    tim->PSC = 0U;
    /*
     * At the 15 kHz test clock, the PA0 PSB period is sixteen TIM2 kernel
     * ticks (187.2 MHz / 11.7 MHz). One OC2REF pulse per cell lets TIM8
     * alternate complete cells between the two DMA lanes.
     */
#if (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_ENABLE != 0U)
    tim->ARR = 15U;
#else
    tim->ARR = 7U;
#endif
    tim->CNT = 0U;
    tim->CCR1 = 0U;
    tim->CCR2 = capture_delay_ticks;
    tim->CCR3 = 0U;
    MODIFY_REG(
        tim->CCMR1,
        TIM_CCMR1_CC1S | TIM_CCMR1_IC1PSC | TIM_CCMR1_IC1F |
            TIM_CCMR1_CC2S | TIM_CCMR1_OC2M | TIM_CCMR1_OC2PE | TIM_CCMR1_OC2FE,
        TIM_CCMR1_CC1S_0 |
#if (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_ENABLE != 0U)
            TIM_CCMR1_OC2M_2 | TIM_CCMR1_OC2M_1 |
#endif
            ((xe_np_gpioe_capture_timing_filter & 0x0FU) << TIM_CCMR1_IC1F_Pos));
    MODIFY_REG(
        tim->CCER,
        TIM_CCER_CC1P | TIM_CCER_CC1NP | TIM_CCER_CC2P | TIM_CCER_CC2NP,
        ((xe_np_gpioe_capture_timing_rising_edge != 0U) ? 0U : TIM_CCER_CC1P) |
#if (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_ENABLE != 0U)
            TIM_CCER_CC2E
#else
            0U
#endif
    );
    /*
     * Bench-only jitter isolation: the first PSB edge arms TIM2, then the
     * internal 187.2 MHz timer free-runs with an 8-tick period. TIM2_CH2
     * generates one DMA request per period at CCR2, avoiding per-edge
     * slave-reset jitter while keeping 1/8-cycle phase control.
     */
#if (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_ENABLE != 0U)
    if(XE_NP_APP_GPIOE_CAPTURE_TIM2_DELAY_TICKS >= 16U)
#else
    if(XE_NP_APP_GPIOE_CAPTURE_TIM2_DELAY_TICKS >= 8U)
#endif
    {
        return HAL_ERROR;
    }
    tim->SMCR = TIM_SLAVEMODE_TRIGGER;
#if (XE_NP_APP_EXPERIMENT_GPIOE_CAPTURE_TIM2_PSB_ETR_ENABLE != 0U)
    tim->SMCR |= TIM_TS_ETRF;
    SET_BIT(tim->CCER, TIM_CCER_CC2E
#if (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_ENABLE != 0U)
            | TIM_CCER_CC3E
#endif
    );
#else
    tim->SMCR |= TIM_TS_TI1FP1;
    SET_BIT(tim->CCER, TIM_CCER_CC1E | TIM_CCER_CC2E
#if (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_ENABLE != 0U)
            | TIM_CCER_CC3E
#endif
    );
#endif
#endif
#elif (XE_NP_APP_GPIOE_CAPTURE_TIM2_DELAYED_DMA_ENABLE != 0U)
    tim->DIER &= ~(TIM_DIER_UDE | TIM_DIER_CC1DE | TIM_DIER_CC2DE);
    tim->CCER &= ~(TIM_CCER_CC1E | TIM_CCER_CC2E);
    tim->SMCR = 0U;
    MODIFY_REG(tim->CR2, TIM_CR2_MMS, TIM_TRGO_RESET);
    tim->PSC = 0U;
    tim->ARR = 0xFFFFU;
    tim->CNT = 0U;
    tim->CCR1 = 0U;
    tim->CCR2 = capture_delay_ticks;
    MODIFY_REG(
        tim->CCMR1,
        TIM_CCMR1_CC1S | TIM_CCMR1_IC1PSC | TIM_CCMR1_IC1F |
            TIM_CCMR1_CC2S | TIM_CCMR1_OC2M | TIM_CCMR1_OC2PE | TIM_CCMR1_OC2FE,
        TIM_CCMR1_CC1S_0 |
            ((xe_np_gpioe_capture_timing_filter & 0x0FU) << TIM_CCMR1_IC1F_Pos));
    MODIFY_REG(
        tim->CCER,
        TIM_CCER_CC1P | TIM_CCER_CC1NP | TIM_CCER_CC2P | TIM_CCER_CC2NP,
        (xe_np_gpioe_capture_timing_rising_edge != 0U) ? 0U : TIM_CCER_CC1P);
    /*
     * Bench-only phase control: reset TIM2 on each PA0 PSB clock edge, then
     * let the internal 187.2 MHz timer clock advance to CCR2 before issuing
     * the DMA request from TIM2_CH2. With NP2 PSB at 23.4 MHz, one PSB cycle
     * is exactly 8 timer ticks, so delay 1..7 samples inside the eye. Allow
     * delay 0 as a bench-only edge-time experiment, and allow larger bench
     * delays as a diagnostic to see whether multi-cycle compare offsets ever
     * survive the next PSB edge in reset-slave mode.
     */
    if(XE_NP_APP_GPIOE_CAPTURE_TIM2_DELAY_TICKS >= 16U)
    {
        return HAL_ERROR;
    }
    tim->SMCR = TIM_SLAVEMODE_RESET;
#if (XE_NP_APP_EXPERIMENT_GPIOE_CAPTURE_TIM2_PSB_ETR_ENABLE != 0U)
    tim->SMCR |= TIM_TS_ETRF;
    SET_BIT(tim->CCER, TIM_CCER_CC2E);
#else
    tim->SMCR |= TIM_TS_TI1FP1;
    SET_BIT(tim->CCER, TIM_CCER_CC1E | TIM_CCER_CC2E);
#endif
#else
    tim->DIER &= ~(TIM_DIER_UDE | TIM_DIER_CC1DE | TIM_DIER_CC2DE);
    tim->CCER &= ~(TIM_CCER_CC1E | TIM_CCER_CC2E);
    tim->SMCR = 0U;
    MODIFY_REG(tim->CR2, TIM_CR2_MMS, TIM_TRGO_RESET);
    tim->PSC = 0U;
#if (XE_NP_APP_GPIOE_CAPTURE_TIM2_UPDATE_DMA_ENABLE != 0U)
    tim->ARR = 1U;
#else
    /*
     * Direct CC1 capture is paced only by the input edge.  Do not also make
     * that edge roll the counter over: an ARR of zero creates an unused
     * update event for every sample, adding timer work to the GPDMA request
     * path. Input capture and its DMA request remain edge-driven while TIM2
     * free-runs between its very infrequent 32-bit overflows.
     */
    tim->ARR = 0xFFFFFFFFUL;
#endif
    tim->CNT = 0U;
    tim->CCR1 = 0U;
    tim->CCR2 = 0U;
    MODIFY_REG(
        tim->CCMR1,
        TIM_CCMR1_CC1S | TIM_CCMR1_IC1PSC | TIM_CCMR1_IC1F,
        TIM_CCMR1_CC1S_0 |
            ((xe_np_gpioe_capture_timing_filter & 0x0FU) << TIM_CCMR1_IC1F_Pos));
    MODIFY_REG(
        tim->CCER,
        TIM_CCER_CC1P | TIM_CCER_CC1NP,
        (xe_np_gpioe_capture_timing_rising_edge != 0U) ? 0U : TIM_CCER_CC1P);
    memset(&clock_config, 0, sizeof(clock_config));
    clock_config.ClockSource = TIM_CLOCKSOURCE_TI1;
    clock_config.ClockPolarity = (xe_np_gpioe_capture_timing_rising_edge != 0U) ?
                                     TIM_CLOCKPOLARITY_RISING :
                                     TIM_CLOCKPOLARITY_FALLING;
    clock_config.ClockPrescaler = TIM_CLOCKPRESCALER_DIV1;
    clock_config.ClockFilter = xe_np_gpioe_capture_timing_filter;
    if(HAL_TIM_ConfigClockSource(htim, &clock_config) != HAL_OK)
    {
        return HAL_ERROR;
    }
    SET_BIT(tim->CCER, TIM_CCER_CC1E);
#endif
    tim->EGR = TIM_EGR_UG;
    __HAL_TIM_CLEAR_FLAG(htim, TIM_FLAG_UPDATE | TIM_FLAG_CC1 | TIM_FLAG_CC2 | TIM_FLAG_CC3);
    return HAL_OK;
}

static HAL_StatusTypeDef XE_NP_App_PrepareTimerForCapture(const XE_NP_AppDmaPort *port)
{
    if((port == NULL) || (port->htim == NULL))
    {
        return HAL_ERROR;
    }

#if (XE_NP_APP_USB_CAPTURE_INTERNAL_TIMER_ENABLE != 0U)
    if(port->port_index == 0U)
    {
        return XE_NP_App_PrepareTimerForInternalUpdateCapture(
            port->htim,
            XE_NP_APP_USB_CAPTURE_INTERNAL_TIMER_HZ);
    }
#endif

#if (XE_NP_APP_GPIOE_CAPTURE_USE_PA0_TIM2_CH1 != 0U)
    if(port->port_index == 0U)
    {
        return XE_NP_App_PrepareTimerForTim2Ch1Capture(port->htim);
    }
    if(port->port_index == 1U)
    {
        XE_NP_App_ConfigurePsbPa0ForTim8Etr();
        if(XE_NP_App_ConfigureTimerEtrMode2Falling(port->htim) != HAL_OK)
        {
            return HAL_ERROR;
        }
    }
#endif

    return XE_NP_App_PrepareTimerForUpdateCapture(port->htim);
}

static HAL_StatusTypeDef XE_NP_App_StartPreparedTimerForCapture(XE_NP_AppDmaPort *port)
{
    TIM_HandleTypeDef *htim;

    if((port == NULL) || (port->htim == NULL))
    {
        return HAL_ERROR;
    }

    htim = port->htim;
    if(port->timer_dma_mask == 0U)
    {
        return HAL_OK;
    }

#if (XE_NP_APP_GPIOE_CAPTURE_USE_PA0_TIM2_CH1 != 0U) && \
    (XE_NP_APP_GPIOE_CAPTURE_TIM2_DELAYED_DMA_ENABLE == 0U) && \
    (XE_NP_APP_GPIOE_CAPTURE_TIM2_START_DELAY_TICKS != 0U)
    if((port->port_index == 0U) && (htim->Instance == TIM2) &&
       (xe_np_capture_start_phase_delay_armed != 0U))
    {
        uint32_t start_cnt;
        uint32_t start_ms;

        xe_np_capture_start_phase_delay_armed = 0U;
        __HAL_TIM_DISABLE_DMA(htim, port->timer_dma_mask);
        __HAL_TIM_SET_COUNTER(htim, 0U);
        __HAL_TIM_CLEAR_FLAG(htim, TIM_FLAG_UPDATE | TIM_FLAG_CC1 | TIM_FLAG_CC2);
        if(HAL_TIM_Base_Start(htim) != HAL_OK)
        {
            return HAL_ERROR;
        }

        start_cnt = __HAL_TIM_GET_COUNTER(htim);
        start_ms = HAL_GetTick();
        while((uint32_t)(__HAL_TIM_GET_COUNTER(htim) - start_cnt) <
              XE_NP_APP_GPIOE_CAPTURE_TIM2_START_DELAY_TICKS)
        {
            if((uint32_t)(HAL_GetTick() - start_ms) >
               XE_NP_APP_GPIOE_CAPTURE_TIM2_START_DELAY_TIMEOUT_MS)
            {
                return HAL_ERROR;
            }
        }

        __HAL_TIM_CLEAR_FLAG(htim, TIM_FLAG_UPDATE | TIM_FLAG_CC1 | TIM_FLAG_CC2);
        __HAL_TIM_ENABLE_DMA(htim, port->timer_dma_mask);
        return HAL_OK;
    }
#endif

    __HAL_TIM_ENABLE_DMA(htim, port->timer_dma_mask);
    if(HAL_TIM_Base_Start(htim) != HAL_OK)
    {
        return HAL_ERROR;
    }
    return HAL_OK;
}

static HAL_StatusTypeDef XE_NP_App_BuildCaptureNode(
    DMA_NodeTypeDef *node,
    uint32_t src_address,
    uint32_t data_width,
    uint8_t *dst,
    uint32_t data_size_bytes,
    uint32_t repeat_block_count,
    int32_t blk_dest_addr_offset,
    uint32_t request,
    uint32_t trigger_selection,
    uint32_t trigger_polarity)
{
    DMA_NodeConfTypeDef node_config;

    if((node == NULL) || (src_address == 0U) || (dst == NULL))
    {
        return HAL_ERROR;
    }

    memset(&node_config, 0, sizeof(node_config));
    node_config.NodeType =
        (repeat_block_count > 1U) ? DMA_GPDMA_2D_NODE : DMA_GPDMA_LINEAR_NODE;
    node_config.Init.Request = request;
    node_config.Init.BlkHWRequest = DMA_BREQ_SINGLE_BURST;
    node_config.Init.Direction = DMA_PERIPH_TO_MEMORY;
    node_config.Init.SrcInc = DMA_SINC_FIXED;
    node_config.Init.DestInc = DMA_DINC_INCREMENTED;
    node_config.Init.SrcDataWidth = data_width;
    node_config.Init.DestDataWidth =
        (data_width == DMA_SRC_DATAWIDTH_BYTE) ? DMA_DEST_DATAWIDTH_BYTE : DMA_DEST_DATAWIDTH_HALFWORD;
    node_config.Init.Priority = DMA_HIGH_PRIORITY;
    node_config.Init.SrcBurstLength = 1U;
    node_config.Init.DestBurstLength = XE_NP_APP_CAPTURE_DMA_DEST_BURST_LENGTH;
    node_config.Init.TransferAllocatedPort =
        DMA_SRC_ALLOCATED_PORT0 | XE_NP_APP_CAPTURE_DMA_DEST_ALLOCATED_PORT;
    node_config.Init.TransferEventMode =
        (repeat_block_count > 1U) ?
            DMA_TCEM_REPEATED_BLOCK_TRANSFER :
            DMA_TCEM_BLOCK_TRANSFER;
    node_config.Init.Mode = DMA_NORMAL;
    node_config.DataHandlingConfig.DataExchange = DMA_EXCHANGE_NONE;
    node_config.DataHandlingConfig.DataAlignment = DMA_DATA_RIGHTALIGN_ZEROPADDED;
    node_config.TriggerConfig.TriggerMode = DMA_TRIGM_SINGLE_BURST_TRANSFER;
    node_config.TriggerConfig.TriggerPolarity = trigger_polarity;
    node_config.TriggerConfig.TriggerSelection = trigger_selection;
    node_config.SrcAddress = src_address;
    node_config.DstAddress = (uint32_t)dst;
    node_config.DataSize = data_size_bytes;
    node_config.RepeatBlockConfig.RepeatCount = repeat_block_count;
    node_config.RepeatBlockConfig.SrcAddrOffset = 0;
    node_config.RepeatBlockConfig.DestAddrOffset = 0;
    node_config.RepeatBlockConfig.BlkSrcAddrOffset = 0;
    node_config.RepeatBlockConfig.BlkDestAddrOffset = blk_dest_addr_offset;
    if(HAL_DMAEx_List_BuildNode(&node_config, node) != HAL_OK)
    {
        return HAL_ERROR;
    }

    /*
     * The U5 HAL linked-list builder normally propagates these fields from
     * Init.  Some compact-channel circular images, however, retained the
     * byte-width reset value in the serialized node.  That turns a nominal
     * GPIO halfword capture into a one-byte transfer.  Encode the requested
     * width directly in the node as the final authoritative descriptor value.
     */
    if(data_width == DMA_SRC_DATAWIDTH_HALFWORD)
    {
        MODIFY_REG(
            node->LinkRegisters[NODE_CTR1_DEFAULT_OFFSET],
            DMA_CTR1_SDW_LOG2 | DMA_CTR1_DDW_LOG2,
            DMA_SRC_DATAWIDTH_HALFWORD | DMA_DEST_DATAWIDTH_HALFWORD);
    }
    return HAL_OK;
}

static uint32_t XE_NP_App_GetMinimal2dSelfReloadMask(void)
{
    return DMA_CLLR_UT1 | DMA_CLLR_UT2 | DMA_CLLR_UB1 | DMA_CLLR_USA |
           DMA_CLLR_UDA | DMA_CLLR_UT3 | DMA_CLLR_UB2 | DMA_CLLR_ULL;
}

static HAL_StatusTypeDef XE_NP_App_StartCaptureDmaPortDirect(
    XE_NP_AppDmaPort *port,
    uint32_t gpio_source_addr,
    uint32_t data_width,
    uint32_t trigger_selection,
    uint32_t trigger_polarity)
{
    DMA_DataHandlingConfTypeDef data_handling_config;
    DMA_RepeatBlockConfTypeDef repeat_config;
    DMA_TriggerConfTypeDef trigger_config;

    if((port == NULL) || (port->capture_port == NULL) || (port->capture_port->storage == NULL) ||
       (port->node_count == 0U) || (port->node_bytes == 0U))
    {
        return HAL_ERROR;
    }

#if (XE_NP_APP_CAPTURE_DMA_HT_IRQ_ENABLE != 0U)
    if(((port->capture_port->storage_bytes & 1U) != 0U) ||
       ((port->node_count & 1U) != 0U))
    {
        return HAL_ERROR;
    }
#endif

    port->direct_repeat_mode = 1U;
    /*
     * Hardware executes one self-linked 2D descriptor, but software still
     * needs the logical NP packet window count so publication, watchdog, and
     * USB export all keep using the original 4-window headroom geometry.
     */
    port->dma_queue_node_count = port->node_count;
    port->dma_repeat_block_count =
        (port->node_count > 0xFFU) ? 0xFFU : port->node_count;

    port->hdma.Parent = port;
    port->hdma.Init.Request = port->dma_request;
    port->hdma.Init.BlkHWRequest = DMA_BREQ_SINGLE_BURST;
    port->hdma.Init.Direction = DMA_PERIPH_TO_MEMORY;
    port->hdma.Init.SrcInc = DMA_SINC_FIXED;
    port->hdma.Init.DestInc = DMA_DINC_INCREMENTED;
    port->hdma.Init.SrcDataWidth = data_width;
    port->hdma.Init.DestDataWidth =
        (data_width == DMA_SRC_DATAWIDTH_BYTE) ? DMA_DEST_DATAWIDTH_BYTE : DMA_DEST_DATAWIDTH_HALFWORD;
    port->hdma.Init.Priority = DMA_HIGH_PRIORITY;
    port->hdma.Init.SrcBurstLength = 1U;
    port->hdma.Init.DestBurstLength = XE_NP_APP_CAPTURE_DMA_DEST_BURST_LENGTH;
    port->hdma.Init.TransferAllocatedPort =
        DMA_SRC_ALLOCATED_PORT0 | DMA_DEST_ALLOCATED_PORT1;
    port->hdma.Init.TransferEventMode = XE_NP_APP_CAPTURE_DMA_REPEAT_EVENT_MODE;
    port->hdma.Init.Mode = DMA_NORMAL;

    if(HAL_DMA_Init(&port->hdma) != HAL_OK)
    {
        return HAL_ERROR;
    }

    memset(&data_handling_config, 0, sizeof(data_handling_config));
    data_handling_config.DataExchange = DMA_EXCHANGE_NONE;
    data_handling_config.DataAlignment = DMA_DATA_RIGHTALIGN_ZEROPADDED;
    if(HAL_DMAEx_ConfigDataHandling(&port->hdma, &data_handling_config) != HAL_OK)
    {
        return HAL_ERROR;
    }

    memset(&trigger_config, 0, sizeof(trigger_config));
    trigger_config.TriggerMode = DMA_TRIGM_SINGLE_BURST_TRANSFER;
    trigger_config.TriggerPolarity = trigger_polarity;
    trigger_config.TriggerSelection = trigger_selection;
    if(HAL_DMAEx_ConfigTrigger(&port->hdma, &trigger_config) != HAL_OK)
    {
        return HAL_ERROR;
    }

    if(
#if (XE_NP_APP_CAPTURE_DMA_HT_IRQ_ENABLE != 0U)
       (HAL_DMA_RegisterCallback(
            &port->hdma,
            HAL_DMA_XFER_HALFCPLT_CB_ID,
            XE_NP_AppDma_HalfTransferCallback) != HAL_OK) ||
#endif
       (HAL_DMA_RegisterCallback(
            &port->hdma,
            HAL_DMA_XFER_CPLT_CB_ID,
            XE_NP_AppDma_TransferCompleteCallback) != HAL_OK) ||
       (HAL_DMA_RegisterCallback(
            &port->hdma,
            HAL_DMA_XFER_ERROR_CB_ID,
            XE_NP_AppDma_ErrorCallback) != HAL_OK))
    {
        return HAL_ERROR;
    }

    memset(&repeat_config, 0, sizeof(repeat_config));
    repeat_config.RepeatCount = port->node_count;
    repeat_config.SrcAddrOffset = 0;
    repeat_config.DestAddrOffset = 0;
    repeat_config.BlkSrcAddrOffset = 0;
    repeat_config.BlkDestAddrOffset = 0;
    if(HAL_DMAEx_ConfigRepeatBlock(&port->hdma, &repeat_config) != HAL_OK)
    {
        return HAL_ERROR;
    }

    if(XE_NP_App_PrepareTimerForCapture(port) != HAL_OK)
    {
        return HAL_ERROR;
    }

    HAL_NVIC_SetPriority(port->irqn, XE_NP_APP_DMA_IRQ_PREEMPT_PRIO, XE_NP_APP_DMA_IRQ_SUBPRIO);
    HAL_NVIC_EnableIRQ(port->irqn);

    if(HAL_DMA_Start_IT(
           &port->hdma,
           gpio_source_addr,
           (uint32_t)port->capture_port->storage,
           port->node_bytes) != HAL_OK)
    {
        return HAL_ERROR;
    }
#if (XE_NP_APP_CAPTURE_DMA_HT_IRQ_ENABLE == 0U)
    __HAL_DMA_DISABLE_IT(&port->hdma, DMA_IT_HT);
#endif

    if(XE_NP_App_StartPreparedTimerForCapture(port) != HAL_OK)
    {
        return HAL_ERROR;
    }

    port->active = 1U;
    XE_NP_App_RefreshDmaStatus();
    return HAL_OK;
}

#if ((XE_NP_APP_CAPTURE_DMA_DAR_ONLY_CIRCULAR_ENABLE != 0U) || \
     (XE_NP_APP_DUAL_DMA_DAR_ONLY_CIRCULAR_ENABLE != 0U))
static HAL_StatusTypeDef XE_NP_App_StartCaptureDmaPortDarOnlyCircular(
    XE_NP_AppDmaPort *port,
    uint32_t gpio_source_addr,
    uint32_t data_width,
    uint32_t trigger_selection,
    uint32_t trigger_polarity,
    uint8_t start_timer)
{
    DMA_DataHandlingConfTypeDef data_handling_config;
    DMA_TriggerConfTypeDef trigger_config;
    uint32_t reload_node_address;

    if((port == NULL) || (port->capture_port == NULL) ||
       (port->capture_port->storage == NULL) || (port->node_count != 1U) ||
       (port->node_bytes == 0U) || (port->node_bytes > 65535U))
    {
        return HAL_ERROR;
    }

    /* A direct linear block owns the transfer registers. At transfer complete
     * the channel fetches only LinkRegisters[CDAR] below and starts the same
     * block again; CLLR itself deliberately remains unchanged. */
    port->direct_repeat_mode = 0U;
    port->dma_queue_node_count = 1U;
    port->dma_repeat_block_count = 1U;
    port->hdma.Parent = port;
    port->hdma.Init.Request = port->dma_request;
    port->hdma.Init.BlkHWRequest = DMA_BREQ_SINGLE_BURST;
    port->hdma.Init.Direction = DMA_PERIPH_TO_MEMORY;
    port->hdma.Init.SrcInc = DMA_SINC_FIXED;
    port->hdma.Init.DestInc = DMA_DINC_INCREMENTED;
    port->hdma.Init.SrcDataWidth = data_width;
    port->hdma.Init.DestDataWidth =
        (data_width == DMA_SRC_DATAWIDTH_BYTE) ? DMA_DEST_DATAWIDTH_BYTE : DMA_DEST_DATAWIDTH_HALFWORD;
    port->hdma.Init.Priority = DMA_HIGH_PRIORITY;
    port->hdma.Init.SrcBurstLength = 1U;
    port->hdma.Init.DestBurstLength = XE_NP_APP_CAPTURE_DMA_DEST_BURST_LENGTH;
    port->hdma.Init.TransferAllocatedPort =
        DMA_SRC_ALLOCATED_PORT0 | DMA_DEST_ALLOCATED_PORT1;
    port->hdma.Init.TransferEventMode = DMA_TCEM_BLOCK_TRANSFER;
    port->hdma.Init.Mode = DMA_NORMAL;
    if(HAL_DMA_Init(&port->hdma) != HAL_OK)
    {
        return HAL_ERROR;
    }

    memset(&data_handling_config, 0, sizeof(data_handling_config));
    data_handling_config.DataExchange = DMA_EXCHANGE_NONE;
    data_handling_config.DataAlignment = DMA_DATA_RIGHTALIGN_ZEROPADDED;
    if(HAL_DMAEx_ConfigDataHandling(&port->hdma, &data_handling_config) != HAL_OK)
    {
        return HAL_ERROR;
    }
    memset(&trigger_config, 0, sizeof(trigger_config));
    trigger_config.TriggerMode = DMA_TRIGM_SINGLE_BURST_TRANSFER;
    trigger_config.TriggerPolarity = trigger_polarity;
    trigger_config.TriggerSelection = trigger_selection;
    if(HAL_DMAEx_ConfigTrigger(&port->hdma, &trigger_config) != HAL_OK)
    {
        return HAL_ERROR;
    }
    if(
#if (XE_NP_APP_CAPTURE_DMA_HT_IRQ_ENABLE != 0U)
       (HAL_DMA_RegisterCallback(
            &port->hdma,
            HAL_DMA_XFER_HALFCPLT_CB_ID,
            XE_NP_AppDma_HalfTransferCallback) != HAL_OK) ||
#endif
       (HAL_DMA_RegisterCallback(
            &port->hdma,
            HAL_DMA_XFER_CPLT_CB_ID,
            XE_NP_AppDma_TransferCompleteCallback) != HAL_OK) ||
       (HAL_DMA_RegisterCallback(
            &port->hdma,
            HAL_DMA_XFER_ERROR_CB_ID,
            XE_NP_AppDma_ErrorCallback) != HAL_OK))
    {
        return HAL_ERROR;
    }
    if((start_timer != 0U) &&
       (XE_NP_App_PrepareTimerForCapture(port) != HAL_OK))
    {
        return HAL_ERROR;
    }
    HAL_NVIC_SetPriority(port->irqn, XE_NP_APP_DMA_IRQ_PREEMPT_PRIO, XE_NP_APP_DMA_IRQ_SUBPRIO);
    HAL_NVIC_EnableIRQ(port->irqn);
    if(HAL_DMA_Start_IT(
           &port->hdma,
           gpio_source_addr,
           (uint32_t)port->capture_port->storage,
           port->node_bytes) != HAL_OK)
    {
        return HAL_ERROR;
    }

    /* HAL_DMA_Start_IT arms the channel immediately. There is no timer request
     * yet, so suspend it before programming the linked-list registers; CLLR
     * configuration while EN=1 is not a valid U5 register-file update. */
    __HAL_DMA_DISABLE(&port->hdma);

    memset(&port->nodes[0], 0, sizeof(port->nodes[0]));
    port->nodes[0].LinkRegisters[NODE_CDAR_DEFAULT_OFFSET] =
        (uint32_t)port->capture_port->storage;
    XE_NP_App_DCacheClean(&port->nodes[0], (uint32_t)sizeof(port->nodes[0]));
    /* With UDA as the only update bit, the LLI address denotes the one
     * destination-address word itself (not the beginning of an 8-word node). */
    reload_node_address =
        (uint32_t)&port->nodes[0].LinkRegisters[NODE_CDAR_DEFAULT_OFFSET];
    port->hdma.Instance->CLBAR = reload_node_address & DMA_CLBAR_LBA;
    port->hdma.Instance->CLLR = (reload_node_address & DMA_CLLR_LA) | DMA_CLLR_UDA;
    __HAL_DMA_ENABLE(&port->hdma);

    if((start_timer != 0U) &&
       (XE_NP_App_StartPreparedTimerForCapture(port) != HAL_OK))
    {
        return HAL_ERROR;
    }
    port->active = 1U;
    XE_NP_App_RefreshDmaStatus();
    return HAL_OK;
}
#endif

static HAL_StatusTypeDef XE_NP_App_StartCaptureDmaPortMinimalSelfLink(
    XE_NP_AppDmaPort *port,
    uint32_t gpio_source_addr,
    uint32_t data_width,
    uint32_t trigger_selection,
    uint32_t trigger_polarity)
{
    HAL_DMA_StateTypeDef dma_state;
    uint32_t ccr_value;
    uint32_t cllr_mask;

    if((port == NULL) || (port->capture_port == NULL) || (port->capture_port->storage == NULL) ||
       (port->node_count < 2U) || (port->node_bytes == 0U))
    {
        return HAL_ERROR;
    }

    port->direct_repeat_mode = 1U;
    /*
     * Hardware owns one self-linked descriptor, but it repeats the original
     * logical capture block across the complete ring.  Keep that logical
     * block count for producer-window address resolution; reporting one
     * 720 KiB window made every 61,440-byte TC reuse the wrong source range
     * after the first two blocks.
     */
    port->dma_queue_node_count = port->node_count;
    port->dma_repeat_block_count =
        (port->node_count > 0xFFU) ? 0xFFU : port->node_count;

    port->hdma.Parent = port;
    port->hdma.InitLinkedList.Priority = DMA_HIGH_PRIORITY;
    port->hdma.InitLinkedList.LinkStepMode = DMA_LSM_FULL_EXECUTION;
    port->hdma.InitLinkedList.LinkAllocatedPort = XE_NP_APP_DMA_LINK_ALLOCATED_PORT_VALUE;
    port->hdma.InitLinkedList.TransferEventMode = XE_NP_APP_CAPTURE_DMA_REPEAT_EVENT_MODE;
    port->hdma.InitLinkedList.LinkedListMode = DMA_LINKEDLIST_NORMAL;

    if(HAL_DMAEx_List_Init(&port->hdma) != HAL_OK)
    {
        return HAL_ERROR;
    }

    if(
#if (XE_NP_APP_CAPTURE_DMA_HT_IRQ_ENABLE != 0U)
       (HAL_DMA_RegisterCallback(
            &port->hdma,
            HAL_DMA_XFER_HALFCPLT_CB_ID,
            XE_NP_AppDma_HalfTransferCallback) != HAL_OK) ||
#endif
       (HAL_DMA_RegisterCallback(
            &port->hdma,
            HAL_DMA_XFER_CPLT_CB_ID,
            XE_NP_AppDma_TransferCompleteCallback) != HAL_OK) ||
       (HAL_DMA_RegisterCallback(
            &port->hdma,
            HAL_DMA_XFER_ERROR_CB_ID,
            XE_NP_AppDma_ErrorCallback) != HAL_OK))
    {
        return HAL_ERROR;
    }

    memset(&port->queue, 0, sizeof(port->queue));
    if(XE_NP_App_BuildCaptureNode(
           &port->nodes[0],
           gpio_source_addr,
           data_width,
           &port->capture_port->storage[
               (port->usb_packet_headroom_enabled != 0U) ? port->usb_packet_header_bytes : 0U],
           port->node_bytes,
           port->node_count,
           (port->usb_packet_headroom_enabled != 0U) ? (int32_t)port->usb_packet_header_bytes : 0,
           port->dma_request,
           trigger_selection,
           trigger_polarity) != HAL_OK)
    {
        return HAL_ERROR;
    }

    /*
     * Full-ring 2D execution proved clean without any node reload. Keep that
     * same 2D node, but at wrap only restore the registers that actually
     * change over one ring: block count/size, destination address, the 2D
     * repeat-address configuration, and the self-link CLLR value itself.
     * Leave CTR1/CTR2/CSAR resident in the channel so the hardware does not
     * refetch the full descriptor image.
     */
    /*
     * A 2D descriptor must reload CTR1 as well as its repeat-block and
     * destination state.  Omitting UT1 leaves the first transfer unarmed on
     * this U5 GPDMA revision, so the supposedly minimal self-link never
     * receives a peripheral request.  Use the proven full 2D reload set while
     * this path is qualified; it still avoids any intervening descriptor.
     */
    cllr_mask = XE_NP_App_GetMinimal2dSelfReloadMask();
    port->nodes[0].LinkRegisters[NODE_CLLR_2D_DEFAULT_OFFSET] =
        ((uint32_t)&port->nodes[0] & DMA_CLLR_LA) | cllr_mask;
    port->queue.Head = &port->nodes[0];
    port->queue.NodeNumber = 1U;
    port->queue.FirstCircularNode = NULL;
    port->queue.State = HAL_DMA_QUEUE_STATE_READY;
    port->queue.ErrorCode = HAL_DMA_QUEUE_ERROR_NONE;
    port->queue.Type = QUEUE_TYPE_STATIC;

    if(HAL_DMAEx_List_LinkQ(&port->hdma, &port->queue) != HAL_OK)
    {
        return HAL_ERROR;
    }

    if(XE_NP_App_PrepareTimerForCapture(port) != HAL_OK)
    {
        return HAL_ERROR;
    }

    XE_NP_App_DCacheClean(&port->nodes[0], (uint32_t)sizeof(port->nodes[0]));

    HAL_NVIC_SetPriority(port->irqn, XE_NP_APP_DMA_IRQ_PREEMPT_PRIO, XE_NP_APP_DMA_IRQ_SUBPRIO);
    HAL_NVIC_EnableIRQ(port->irqn);

    dma_state = port->hdma.State;
    ccr_value = port->hdma.Instance->CCR & DMA_CCR_LSM;
    if((dma_state != HAL_DMA_STATE_READY) &&
       !((dma_state == HAL_DMA_STATE_BUSY) && (ccr_value != 0U)))
    {
        port->hdma.ErrorCode = HAL_DMA_ERROR_BUSY;
        return HAL_ERROR;
    }

    if(port->hdma.State == HAL_DMA_STATE_READY)
    {
        __HAL_LOCK(&port->hdma);
        port->hdma.State = HAL_DMA_STATE_BUSY;
        port->queue.State = HAL_DMA_QUEUE_STATE_BUSY;
        port->hdma.ErrorCode = HAL_DMA_ERROR_NONE;
        port->queue.ErrorCode = HAL_DMA_QUEUE_ERROR_NONE;

        __HAL_DMA_ENABLE_IT(
            &port->hdma,
            (DMA_IT_TC | DMA_IT_DTE | DMA_IT_ULE | DMA_IT_USE | DMA_IT_TO));
#if (XE_NP_APP_CAPTURE_DMA_HT_IRQ_ENABLE != 0U)
        __HAL_DMA_ENABLE_IT(&port->hdma, DMA_IT_HT);
#endif
        port->hdma.Instance->CLBAR = ((uint32_t)&port->nodes[0] & DMA_CLBAR_LBA);
        port->hdma.Instance->CLLR = ((uint32_t)&port->nodes[0] & DMA_CLLR_LA) | cllr_mask;
    }

    __HAL_DMA_ENABLE(&port->hdma);
#if (XE_NP_APP_CAPTURE_DMA_HT_IRQ_ENABLE == 0U)
    __HAL_DMA_DISABLE_IT(&port->hdma, DMA_IT_HT);
#endif

    if(XE_NP_App_StartPreparedTimerForCapture(port) != HAL_OK)
    {
        return HAL_ERROR;
    }

    port->active = 1U;
    XE_NP_App_RefreshDmaStatus();
    return HAL_OK;
}

static HAL_StatusTypeDef XE_NP_App_StartCaptureDmaPort(
    XE_NP_AppDmaPort *port,
    XE_NP_CaptureState *capture,
    CE32_systemParam *sys_param,
    uint8_t port_index)
{
    DMA_Channel_TypeDef *dma_instance;
    XE_NP_CapturePort *capture_port;
    TIM_HandleTypeDef *htim;
    volatile uint32_t *gpio_idr;
    IRQn_Type irqn;
    uint32_t request;
    uint32_t trigger_selection;
    uint32_t trigger_polarity;
    uint32_t timer_dma_mask;
    uint32_t gpio_source_addr;
    uint32_t data_width;
    uint8_t dma_node_count;
    uint32_t repeat_block_count;

    if((port == NULL) || (capture == NULL) || (sys_param == NULL))
    {
        return HAL_ERROR;
    }

    if(port_index == 0U)
    {
        capture_port = &capture->gpioe;
        htim = capture->gpioe.htim;
        gpio_idr = &GPIOE->IDR;
        dma_instance = GPDMA1_Channel12;
        irqn = GPDMA1_Channel12_IRQn;
#if (XE_NP_APP_USB_CAPTURE_INTERNAL_TIMER_ENABLE != 0U)
        request = GPDMA1_REQUEST_TIM2_UP;
        trigger_selection = 0U;
        trigger_polarity = DMA_TRIG_POLARITY_MASKED;
        timer_dma_mask = TIM_DMA_UPDATE;
#elif (XE_NP_APP_GPIOE_CAPTURE_USE_PA0_TIM2_CH1 != 0U)
#if (XE_NP_APP_EXPERIMENT_TIM2_PSB_ARMED_FREERUN_ENABLE != 0U)
        request = GPDMA1_REQUEST_TIM2_CH2;
        timer_dma_mask = TIM_DMA_CC2;
#elif (XE_NP_APP_GPIOE_CAPTURE_TIM2_UPDATE_DMA_ENABLE != 0U)
        request = GPDMA1_REQUEST_TIM2_UP;
        timer_dma_mask = TIM_DMA_UPDATE;
#else
        request = (XE_NP_APP_GPIOE_CAPTURE_TIM2_DELAYED_DMA_ENABLE != 0U) ?
            GPDMA1_REQUEST_TIM2_CH2 :
            GPDMA1_REQUEST_TIM2_CH1;
        timer_dma_mask = (XE_NP_APP_GPIOE_CAPTURE_TIM2_DELAYED_DMA_ENABLE != 0U) ?
            TIM_DMA_CC2 :
            TIM_DMA_CC1;
#endif
        trigger_selection = 0U;
        trigger_polarity = DMA_TRIG_POLARITY_MASKED;
#else
        request = GPDMA1_REQUEST_TIM2_UP;
        trigger_selection = 0U;
        trigger_polarity = DMA_TRIG_POLARITY_MASKED;
        timer_dma_mask = TIM_DMA_UPDATE;
#endif
    }
    else if(port_index == 1U)
    {
        capture_port = &capture->gpiod;
        htim = capture->gpiod.htim;
        gpio_idr = &GPIOD->IDR;
        dma_instance = GPDMA1_Channel13;
        irqn = GPDMA1_Channel13_IRQn;
        request = GPDMA1_REQUEST_TIM8_UP;
        trigger_selection = 0U;
        trigger_polarity = DMA_TRIG_POLARITY_MASKED;
        timer_dma_mask = TIM_DMA_UPDATE;
    }
    else
    {
        return HAL_ERROR;
    }

    if((capture_port->storage == NULL) || (capture_port->storage_bytes == 0U) ||
       ((capture_port->storage_bytes % XE_NP_APP_DMA_NODE_BYTES) != 0U))
    {
        return HAL_ERROR;
    }
    if((capture_port->bytes_per_tick != sizeof(uint8_t)) &&
       (capture_port->bytes_per_tick != sizeof(uint16_t)))
    {
        return HAL_ERROR;
    }

    gpio_source_addr = (uint32_t)gpio_idr;
    data_width = DMA_SRC_DATAWIDTH_HALFWORD;
    {
        uint8_t probe_mask = capture_port->active_probe_mask & XE_NP_CAPTURE_ALL_PROBES_MASK;

        if((port_index == 0U) && (probe_mask == XE_NP_CAPTURE_PROBE2_MASK))
        {
#if (XE_NP_APP_CAPTURE_FORCE_NP2_HALFWORD_DIAG != 0U)
            capture_port->bytes_per_tick = sizeof(uint16_t);
            data_width = DMA_SRC_DATAWIDTH_HALFWORD;
            gpio_source_addr = (uint32_t)&GPIOE->IDR;
#else
            capture_port->bytes_per_tick = sizeof(uint8_t);
            data_width = DMA_SRC_DATAWIDTH_BYTE;
            gpio_source_addr = ((uint32_t)&GPIOE->IDR) + 1U;
#endif
        }
        else if(capture_port->bytes_per_tick == sizeof(uint8_t))
        {
            data_width = DMA_SRC_DATAWIDTH_BYTE;
            if((port_index == 0U) && (probe_mask == XE_NP_CAPTURE_PROBE1_MASK))
            {
                gpio_source_addr = (uint32_t)&GPIOE->IDR;
            }
            else if((port_index == 1U) && (probe_mask == XE_NP_CAPTURE_PROBE3_MASK))
            {
                gpio_source_addr = ((uint32_t)&GPIOD->IDR) + 1U;
            }
            else
            {
                return HAL_ERROR;
            }
        }
    }

    memset(port, 0, sizeof(*port));
    port->capture = capture;
    port->capture_port = capture_port;
    port->sys_param = sys_param;
    port->htim = htim;
    port->gpio_idr = gpio_idr;
    port->irqn = irqn;
    port->dma_request = request;
    port->timer_dma_mask = timer_dma_mask;
    port->node_bytes = XE_NP_APP_DMA_NODE_BYTES;
    port->port_index = port_index;
    port->capture_timing_flags =
        (port_index == 0U) ? XE_NP_App_GetGpioeCaptureTimingFlags() : 0U;
    port->capture_bytes_per_tick = capture_port->bytes_per_tick;
    port->usb_packet_headroom_enabled =
        (uint8_t)(XE_NP_App_CanUseUsbRingPacketHeadroom(port) ? 1U : 0U);
    port->usb_packet_header_bytes =
        (port->usb_packet_headroom_enabled != 0U) ? XE_NP_APP_USB_RING_PACKET_HEADER_BYTES : 0U;
    port->usb_packet_window_count =
        (port->usb_packet_headroom_enabled != 0U) ? XE_NP_APP_USB_RING_PACKET_WINDOW_COUNT : 0U;
    port->usb_packet_window_payload_bytes =
        (port->usb_packet_headroom_enabled != 0U) ?
            (capture_port->storage_bytes / XE_NP_APP_USB_RING_PACKET_WINDOW_COUNT) :
            0U;
    capture_port->storage_physical_bytes = capture_port->storage_bytes;
    if((port->usb_packet_headroom_enabled != 0U) &&
       (port->usb_packet_window_payload_bytes != 0U))
    {
        capture_port->storage_physical_bytes =
            capture_port->storage_bytes +
            ((uint32_t)port->usb_packet_header_bytes * (uint32_t)port->usb_packet_window_count);
    }
    port->dma_source_width_bytes =
        (uint8_t)((data_width == DMA_SRC_DATAWIDTH_BYTE) ? sizeof(uint8_t) : sizeof(uint16_t));
    port->gpio_source_addr_lsb = (uint8_t)(gpio_source_addr & 0xFFU);
    port->node_bytes =
        (port->usb_packet_headroom_enabled != 0U) ?
            port->usb_packet_window_payload_bytes :
            XE_NP_APP_DMA_NODE_BYTES;
    port->node_count = (uint8_t)(capture_port->storage_bytes / port->node_bytes);
    if((port->node_count == 0U) || (port->node_count > XE_NP_APP_DMA_NODE_COUNT_MAX))
    {
        return HAL_ERROR;
    }
    dma_node_count = port->node_count;
    repeat_block_count = 1U;
#if (XE_NP_APP_CAPTURE_DMA_SINGLE_2D_NODE_ENABLE != 0U)
    /*
     * Group the logical ring into larger repeated-block nodes so the DMA
     * reload cadence stays low without stretching HT/TC publication out to a
     * full multi-megabyte half-ring.
     */
    if((capture_port->bytes_per_tick == sizeof(uint8_t)) &&
       (port->node_count > 1U) &&
       (port->usb_packet_headroom_enabled == 0U))
    {
        repeat_block_count = port->node_count;
        if(repeat_block_count > XE_NP_APP_CAPTURE_DMA_REPEAT_GROUP_MAX_NODES)
        {
            repeat_block_count = XE_NP_APP_CAPTURE_DMA_REPEAT_GROUP_MAX_NODES;
            while((repeat_block_count > 1U) &&
                  ((port->node_count % repeat_block_count) != 0U))
            {
                repeat_block_count >>= 1U;
            }
        }

        if(repeat_block_count > 1U)
        {
            dma_node_count = (uint8_t)(port->node_count / repeat_block_count);
        }
    }
#endif
    port->dma_queue_node_count = dma_node_count;
    port->dma_repeat_block_count =
        (repeat_block_count > 0xFFU) ? 0xFFU : (uint8_t)repeat_block_count;
    port->hdma.Instance = dma_instance;
    port->direct_repeat_mode =
        ((capture_port->bytes_per_tick == sizeof(uint8_t)) &&
         (dma_node_count == 1U) &&
         (repeat_block_count > 1U)) ? 1U : 0U;

    XE_NP_App_DCachePrepare(capture_port->storage, capture_port->storage_physical_bytes);

#if (XE_NP_APP_CAPTURE_DMA_DAR_ONLY_CIRCULAR_ENABLE != 0U)
    if((capture_port->bytes_per_tick == sizeof(uint8_t)) &&
       (port->node_count == 1U))
    {
        if(XE_NP_App_StartCaptureDmaPortDarOnlyCircular(
               port,
               gpio_source_addr,
               data_width,
               trigger_selection,
               trigger_polarity,
               1U) != HAL_OK)
        {
            XE_NP_App_StopCaptureDmaPort(port);
            return HAL_ERROR;
        }
        return HAL_OK;
    }
#endif

#if ((XE_NP_APP_CAPTURE_DMA_DIRECT_REPEAT_DIAG_ENABLE != 0U) && \
     (XE_NP_APP_CAPTURE_DMA_MINIMAL_SELF_LINK_ENABLE == 0U))
    if((capture_port->bytes_per_tick == sizeof(uint8_t)) && (port->node_count > 1U))
    {
        if(XE_NP_App_StartCaptureDmaPortDirect(
               port,
               gpio_source_addr,
               data_width,
               trigger_selection,
               trigger_polarity) != HAL_OK)
        {
            XE_NP_App_StopCaptureDmaPort(port);
            return HAL_ERROR;
        }
        return HAL_OK;
    }
#endif

#if (XE_NP_APP_CAPTURE_DMA_MINIMAL_SELF_LINK_ENABLE != 0U)
    if((capture_port->bytes_per_tick == sizeof(uint8_t)) && (port->node_count > 1U))
    {
        if(XE_NP_App_StartCaptureDmaPortMinimalSelfLink(
               port,
               gpio_source_addr,
               data_width,
               trigger_selection,
               trigger_polarity) != HAL_OK)
        {
            XE_NP_App_StopCaptureDmaPort(port);
            return HAL_ERROR;
        }
        return HAL_OK;
    }
#endif

    port->hdma.Parent = port;
    port->hdma.InitLinkedList.Priority = DMA_HIGH_PRIORITY;
    port->hdma.InitLinkedList.LinkStepMode = DMA_LSM_FULL_EXECUTION;
    port->hdma.InitLinkedList.LinkAllocatedPort = XE_NP_APP_DMA_LINK_ALLOCATED_PORT_VALUE;
    port->hdma.InitLinkedList.TransferEventMode =
        (repeat_block_count > 1U) ?
            DMA_TCEM_REPEATED_BLOCK_TRANSFER :
            DMA_TCEM_BLOCK_TRANSFER;
    port->hdma.InitLinkedList.LinkedListMode = DMA_LINKEDLIST_CIRCULAR;

    if(HAL_DMAEx_List_Init(&port->hdma) != HAL_OK)
    {
        XE_NP_App_StopCaptureDmaPort(port);
        return HAL_ERROR;
    }

    if(
#if (XE_NP_APP_CAPTURE_DMA_HT_IRQ_ENABLE != 0U)
       (HAL_DMA_RegisterCallback(
            &port->hdma,
            HAL_DMA_XFER_HALFCPLT_CB_ID,
            XE_NP_AppDma_HalfTransferCallback) != HAL_OK) ||
#endif
       (HAL_DMA_RegisterCallback(
            &port->hdma,
            HAL_DMA_XFER_CPLT_CB_ID,
            XE_NP_AppDma_TransferCompleteCallback) != HAL_OK) ||
       (HAL_DMA_RegisterCallback(
            &port->hdma,
            HAL_DMA_XFER_ERROR_CB_ID,
            XE_NP_AppDma_ErrorCallback) != HAL_OK))
    {
        XE_NP_App_StopCaptureDmaPort(port);
        return HAL_ERROR;
    }

    memset(&port->queue, 0, sizeof(port->queue));
    for(uint8_t node_index = 0U; node_index < dma_node_count; ++node_index)
    {
        if((XE_NP_App_BuildCaptureNode(
                &port->nodes[node_index],
                gpio_source_addr,
                data_width,
                &capture_port->storage[
                    (port->usb_packet_headroom_enabled != 0U) ?
                        ((uint32_t)port->usb_packet_header_bytes +
                         ((uint32_t)node_index * port->node_bytes * repeat_block_count) +
                         ((uint32_t)node_index * port->usb_packet_header_bytes)) :
                        ((uint32_t)node_index * XE_NP_APP_DMA_NODE_BYTES * repeat_block_count)],
                port->node_bytes,
                repeat_block_count,
                (port->usb_packet_headroom_enabled != 0U) ? (int32_t)port->usb_packet_header_bytes : 0,
                port->dma_request,
                trigger_selection,
                trigger_polarity) != HAL_OK) ||
           (HAL_DMAEx_List_InsertNode_Tail(&port->queue, &port->nodes[node_index]) != HAL_OK))
        {
            XE_NP_App_StopCaptureDmaPort(port);
            return HAL_ERROR;
        }
    }

    if(HAL_DMAEx_List_SetCircularMode(&port->queue) != HAL_OK)
    {
        XE_NP_App_StopCaptureDmaPort(port);
        return HAL_ERROR;
    }

    if(port->direct_repeat_mode != 0U)
    {
        /*
         * HAL circularization rewrites the tail-node CLLR. Reapply the
         * selected 2D self-link update mask afterward so experiments can
         * adjust wrap-time reload behavior from one place.
         */
        port->nodes[0].LinkRegisters[NODE_CLLR_2D_DEFAULT_OFFSET] =
            ((uint32_t)&port->nodes[0] & DMA_CLLR_LA) |
            XE_NP_App_GetMinimal2dSelfReloadMask();
    }

#if (XE_NP_APP_CAPTURE_DMA_DYNAMIC_QUEUE_ENABLE != 0U)
    /*
     * The NP2 corruption currently tracks linked-list item boundaries. Keep
     * the same ring/callback model, but convert the queue to dynamic format so
     * the GPDMA only fetches the node deltas at each boundary instead of the
     * full static descriptor image.
     */
    if((dma_node_count > 1U) &&
       (HAL_DMAEx_List_ConvertQToDynamic(&port->queue) != HAL_OK))
    {
        XE_NP_App_StopCaptureDmaPort(port);
        return HAL_ERROR;
    }
#endif

    if((HAL_DMAEx_List_LinkQ(&port->hdma, &port->queue) != HAL_OK) ||
       (XE_NP_App_PrepareTimerForCapture(port) != HAL_OK))
    {
        XE_NP_App_StopCaptureDmaPort(port);
        return HAL_ERROR;
    }

    XE_NP_App_DmaDescriptorDCacheClean(port);

    HAL_NVIC_SetPriority(port->irqn, XE_NP_APP_DMA_IRQ_PREEMPT_PRIO, XE_NP_APP_DMA_IRQ_SUBPRIO);
    HAL_NVIC_EnableIRQ(port->irqn);

    if(HAL_DMAEx_List_Start_IT(&port->hdma) != HAL_OK)
    {
        XE_NP_App_StopCaptureDmaPort(port);
        return HAL_ERROR;
    }
    if(port->direct_repeat_mode != 0U)
    {
        port->hdma.Instance->CLLR =
            ((uint32_t)&port->nodes[0] & DMA_CLLR_LA) |
            XE_NP_App_GetMinimal2dSelfReloadMask();
    }
#if (XE_NP_APP_CAPTURE_DMA_HT_IRQ_ENABLE == 0U)
    __HAL_DMA_DISABLE_IT(&port->hdma, DMA_IT_HT);
#else
    if(XE_NP_App_UsesFullNodeWindowPublication(port) != 0)
    {
        __HAL_DMA_DISABLE_IT(&port->hdma, DMA_IT_HT);
    }
#endif

    if(XE_NP_App_StartPreparedTimerForCapture(port) != HAL_OK)
    {
        XE_NP_App_StopCaptureDmaPort(port);
        return HAL_ERROR;
    }

    port->active = 1U;
    XE_NP_App_RefreshDmaStatus();
    return HAL_OK;
}

#if (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_ENABLE != 0U)
static HAL_StatusTypeDef XE_NP_App_PrepareTimerForDualDmaDivider(TIM_HandleTypeDef *htim)
{
    TIM_TypeDef *tim;

    if((htim == NULL) || (htim->Instance != TIM8))
    {
        return HAL_ERROR;
    }

    __HAL_TIM_DISABLE_DMA(
        htim,
        TIM_DMA_UPDATE | TIM_DMA_CC1 | TIM_DMA_CC2 | TIM_DMA_CC3 | TIM_DMA_CC4);
    (void)HAL_TIM_Base_Stop(htim);
    __HAL_TIM_DISABLE(htim);
    tim = htim->Instance;
    tim->DIER &= ~(TIM_DIER_UDE | TIM_DIER_CC1DE | TIM_DIER_CC2DE |
                   TIM_DIER_CC3DE | TIM_DIER_CC4DE);
    tim->CCER &= ~(TIM_CCER_CC1E | TIM_CCER_CC2E | TIM_CCER_CC3E | TIM_CCER_CC4E);
    tim->SMCR = 0U;
    MODIFY_REG(tim->CR2, TIM_CR2_MMS, TIM_TRGO_RESET);
    tim->PSC = 0U;
    tim->ARR = 1U;
    tim->RCR = 0U;
    tim->CNT = 0U;
    tim->CCR1 = 1U;
    tim->CCR2 = 0U;
    MODIFY_REG(
        tim->CCMR1,
        TIM_CCMR1_CC1S | TIM_CCMR1_OC1M | TIM_CCMR1_OC1PE | TIM_CCMR1_OC1FE |
            TIM_CCMR1_CC2S | TIM_CCMR1_OC2M | TIM_CCMR1_OC2PE | TIM_CCMR1_OC2FE,
        0U);
    tim->SMCR = TIM_SLAVEMODE_EXTERNAL1 | TIM_TS_ITR1;
    tim->EGR = TIM_EGR_UG;
    __HAL_TIM_CLEAR_FLAG(htim, TIM_FLAG_UPDATE | TIM_FLAG_CC1 | TIM_FLAG_CC2 | TIM_FLAG_CC3);
    return HAL_OK;
}

static HAL_StatusTypeDef XE_NP_App_StartGpioeInterleavedLane(
    XE_NP_AppDmaPort *port,
    XE_NP_CaptureState *capture,
    CE32_systemParam *sys_param,
    DMA_Channel_TypeDef *dma_instance,
    IRQn_Type irqn,
    uint32_t request,
    TIM_HandleTypeDef *htim,
    uint32_t timer_dma_mask,
    uint32_t lane_offset_bytes)
{
    XE_NP_CapturePort *capture_port;
    uint32_t lane_bytes;
    uint32_t node_count;
    uint32_t node_index;
    uint32_t block_span_bytes;
    uint32_t gpio_source_addr;

    if((port == NULL) || (capture == NULL) || (sys_param == NULL) ||
       (dma_instance == NULL) || (htim == NULL))
    {
        return HAL_ERROR;
    }

    capture_port = &capture->gpioe;
#if (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_PUBLISH_ENABLE != 0U)
    /*
     * In storage mode, each physical DMA channel owns a distinct dataMGR
     * ring. The nonzero offset is only the caller's lane selector; the second
     * port's storage pointer already starts at its own lane base.
     */
    if(lane_offset_bytes != 0U)
    {
        capture_port = &capture->gpiod;
        lane_offset_bytes = 0U;
    }
#endif
    if((capture_port->storage == NULL) ||
       (capture_port->bytes_per_tick != sizeof(uint8_t)) ||
       ((capture_port->storage_bytes % XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_STRIDE_BYTES) != 0U))
    {
        return HAL_ERROR;
    }

#if (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_PUBLISH_ENABLE != 0U)
    lane_bytes = capture_port->storage_bytes;
#else
    lane_bytes = capture_port->storage_bytes / XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_STRIDE_BYTES;
#endif
    if(lane_bytes == 0U)
    {
        return HAL_ERROR;
    }
#if (XE_NP_APP_DUAL_DMA_DAR_ONLY_CIRCULAR_ENABLE == 0U)
    if((lane_bytes % (XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_BLOCK_BYTES *
                      XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_REPEAT_COUNT)) != 0U)
    {
        return HAL_ERROR;
    }
#endif
    if((lane_offset_bytes != 0U) && (lane_offset_bytes != lane_bytes))
    {
        return HAL_ERROR;
    }
    node_count =
#if (XE_NP_APP_DUAL_DMA_DAR_ONLY_CIRCULAR_ENABLE != 0U)
        1U;
#else
        lane_bytes /
        (XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_BLOCK_BYTES *
         XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_REPEAT_COUNT);
#endif
    if((node_count == 0U) || (node_count > XE_NP_APP_DMA_NODE_COUNT_MAX))
    {
        return HAL_ERROR;
    }
    block_span_bytes =
#if (XE_NP_APP_DUAL_DMA_DAR_ONLY_CIRCULAR_ENABLE != 0U)
        lane_bytes;
#else
        XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_BLOCK_BYTES *
        XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_REPEAT_COUNT;
#endif
    if(capture_port->active_probe_mask == XE_NP_CAPTURE_PROBE1_MASK)
    {
        /* NP1 occupies GPIOE[7:0]. */
        gpio_source_addr = (uint32_t)&GPIOE->IDR;
    }
    else if(capture_port->active_probe_mask == XE_NP_CAPTURE_PROBE2_MASK)
    {
        /* NP2 occupies GPIOE[15:8]. */
        gpio_source_addr = ((uint32_t)&GPIOE->IDR) + 1U;
    }
    else
    {
        return HAL_ERROR;
    }

    memset(port, 0, sizeof(*port));
    port->capture = capture;
    port->capture_port = capture_port;
    port->sys_param = sys_param;
    port->htim = htim;
    port->gpio_idr = &GPIOE->IDR;
    port->irqn = irqn;
    port->dma_request = request;
    port->timer_dma_mask = timer_dma_mask;
    /* The 2D DMA node transfers BlockBytes on every repeat.  All completion
     * accounting must use that complete physical span, not just the repeat
     * count; otherwise HT/TC publish 1/180th of the completed lane and the
     * paired exporter manufactures a lane-skew/backlog fault. */
    port->node_bytes = block_span_bytes;
    port->port_index =
#if (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_PUBLISH_ENABLE != 0U)
        (capture_port == &capture->gpiod) ? 1U : 0U;
#else
        0U;
#endif
    port->capture_timing_flags = XE_NP_App_GetGpioeCaptureTimingFlags();
    port->capture_bytes_per_tick = sizeof(uint8_t);
    port->dma_source_width_bytes = sizeof(uint8_t);
    port->gpio_source_addr_lsb = (uint8_t)(gpio_source_addr & 0xFFU);
    port->node_count = (uint8_t)node_count;
    port->dma_queue_node_count = (uint8_t)node_count;
    port->dma_repeat_block_count = 0xFFU;
    /* The circular linked-list path reports one completion per full 2D node. */
    port->dma_repeat_event_target = 1U;
    port->dma_repeat_event_count = 0U;
    port->hdma.Instance = dma_instance;
    port->hdma.Parent = port;
    port->hdma.InitLinkedList.Priority = XE_NP_APP_CAPTURE_LINKED_LIST_PRIORITY;
    port->hdma.InitLinkedList.LinkStepMode = DMA_LSM_FULL_EXECUTION;
    port->hdma.InitLinkedList.LinkAllocatedPort = XE_NP_APP_DMA_LINK_ALLOCATED_PORT_VALUE;
    /*
     * A circular queue has no terminal list item.  LAST_LL_ITEM therefore
     * never publishes HT/TC on this one-node ring.  EACH_LL_ITEM still gives
     * exactly one HT and one TC for the physical 2D lane, but continues to do
     * so after every circular wrap.
     */
    port->hdma.InitLinkedList.TransferEventMode = DMA_TCEM_EACH_LL_ITEM_TRANSFER;
    port->hdma.InitLinkedList.LinkedListMode = DMA_LINKEDLIST_CIRCULAR;

#if (XE_NP_APP_DUAL_DMA_DAR_ONLY_CIRCULAR_ENABLE != 0U)
    /* TIM2/TIM8 are armed once both alternating lanes are ready below. */
    if(XE_NP_App_StartCaptureDmaPortDarOnlyCircular(
           port,
           gpio_source_addr,
           DMA_SRC_DATAWIDTH_BYTE,
           0U,
           DMA_TRIG_POLARITY_MASKED,
           0U) != HAL_OK)
    {
        return HAL_ERROR;
    }
    return HAL_OK;
#endif

    if(HAL_DMAEx_List_Init(&port->hdma) != HAL_OK)
    {
        return HAL_ERROR;
    }
#if (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_PUBLISH_ENABLE != 0U)
    if((HAL_DMA_RegisterCallback(
            &port->hdma,
            HAL_DMA_XFER_HALFCPLT_CB_ID,
            XE_NP_AppDma_HalfTransferCallback) != HAL_OK) ||
       (HAL_DMA_RegisterCallback(
            &port->hdma,
            HAL_DMA_XFER_CPLT_CB_ID,
            XE_NP_AppDma_TransferCompleteCallback) != HAL_OK) ||
       (HAL_DMA_RegisterCallback(
            &port->hdma,
            HAL_DMA_XFER_ERROR_CB_ID,
            XE_NP_AppDma_ErrorCallback) != HAL_OK))
    {
        return HAL_ERROR;
    }
#endif
    if(HAL_DMAEx_List_ResetQ(&port->queue) != HAL_OK)
    {
        return HAL_ERROR;
    }

    for(node_index = 0U; node_index < node_count; node_index++)
    {
        uint8_t *node_dst = capture_port->storage + lane_offset_bytes +
                            (node_index * block_span_bytes);

        if((XE_NP_App_BuildCaptureNode(
                &port->nodes[node_index],
                gpio_source_addr,
                DMA_SRC_DATAWIDTH_BYTE,
                node_dst,
                XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_BLOCK_BYTES,
                XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_REPEAT_COUNT,
                0,
                request,
                0U,
                DMA_TRIG_POLARITY_MASKED) != HAL_OK) ||
           (HAL_DMAEx_List_InsertNode_Tail(&port->queue, &port->nodes[node_index]) != HAL_OK))
        {
            return HAL_ERROR;
        }

        /*
         * A multi-node circular lane needs TC at every list transition.
         * A single full-ring 2D node instead retains the native repeated-block
         * event mode built above: it exposes HT at the stable half-ring and
         * TC at the end of the ring.  Forcing EACH_LL_ITEM on that one-node
         * path hid the half-ring publication on this GPDMA revision, leaving
         * the USB producer no safe history until the ring was already full.
         */
        if(node_count > 1U)
        {
            MODIFY_REG(
                port->nodes[node_index].LinkRegisters[NODE_CTR2_DEFAULT_OFFSET],
                DMA_CTR2_TCEM,
                DMA_TCEM_EACH_LL_ITEM_TRANSFER);
        }
    }

    if((HAL_DMAEx_List_SetCircularMode(&port->queue) != HAL_OK) ||
       ((node_count > 1U) && (HAL_DMAEx_List_ConvertQToDynamic(&port->queue) != HAL_OK)) ||
       (HAL_DMAEx_List_LinkQ(&port->hdma, &port->queue) != HAL_OK))
    {
        return HAL_ERROR;
    }

    XE_NP_App_DmaDescriptorDCacheClean(port);
    HAL_NVIC_SetPriority(irqn, XE_NP_APP_DMA_IRQ_PREEMPT_PRIO, XE_NP_APP_DMA_IRQ_SUBPRIO);
    HAL_NVIC_EnableIRQ(irqn);
    if(HAL_DMAEx_List_Start_IT(&port->hdma) != HAL_OK)
    {
        return HAL_ERROR;
    }

#if (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_PUBLISH_ENABLE != 0U)
    /*
     * With multiple repeated 2D nodes, a TC marks one complete, unambiguous
     * lane span.  The GPDMA HT indication is not tied to an individual list
     * node, so publishing it would double-count or expose a live node.  The
     * smaller TC node period is sufficient to keep the USB paired exporter
     * well inside the SRAM3 ring guard.
     */
    if(node_count == 1U)
    {
        __HAL_DMA_ENABLE_IT(&port->hdma, DMA_IT_HT);
    }
    else
    {
        __HAL_DMA_DISABLE_IT(&port->hdma, DMA_IT_HT);
    }
#else
    __HAL_DMA_DISABLE_IT(&port->hdma, DMA_IT_TC | DMA_IT_HT);
#endif
    port->active = 1U;
    return HAL_OK;
}

static HAL_StatusTypeDef XE_NP_App_StartGpioeDualDmaInterleave(XE_NP_Runtime *runtime)
{
    XE_NP_CapturePort *capture_port;
    TIM_HandleTypeDef *divider_htim;

    if((runtime == NULL) || (runtime->capture.gpioe.htim == NULL) ||
       (runtime->capture.gpiod.htim == NULL))
    {
        return HAL_ERROR;
    }
    capture_port = &runtime->capture.gpioe;
    if((capture_port->storage == NULL) ||
       (capture_port->bytes_per_tick != sizeof(uint8_t)))
    {
        return HAL_ERROR;
    }
    divider_htim = runtime->capture.gpiod.htim;

    XE_NP_App_DCachePrepare(
        runtime->capture.raw_storage,
        runtime->capture.raw_storage_bytes);
    if(XE_NP_App_PrepareTimerForTim2Ch1Capture(capture_port->htim) != HAL_OK)
    {
        return HAL_ERROR;
    }
#if (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_PHASE_PROBE_ENABLE == 0U)
    if(XE_NP_App_PrepareTimerForDualDmaDivider(divider_htim) != HAL_OK)
    {
        return HAL_ERROR;
    }
#endif
    if(XE_NP_App_StartGpioeInterleavedLane(
           &xe_np_dma_gpioe,
           &runtime->capture,
           &runtime->sys_param,
           GPDMA1_Channel12,
           GPDMA1_Channel12_IRQn,
            (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_PHASE_PROBE_ENABLE != 0U) ?
                GPDMA1_REQUEST_TIM2_CH2 : GPDMA1_REQUEST_TIM8_CH1,
            (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_PHASE_PROBE_ENABLE != 0U) ?
                capture_port->htim : divider_htim,
            (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_PHASE_PROBE_ENABLE != 0U) ?
                TIM_DMA_CC2 : TIM_DMA_CC1,
           0U) != HAL_OK)
    {
        XE_NP_App_StopCaptureDmaPort(&xe_np_dma_gpioe);
        return HAL_ERROR;
    }
    if(XE_NP_App_StartGpioeInterleavedLane(
           &xe_np_dma_gpiod,
           &runtime->capture,
           &runtime->sys_param,
           GPDMA1_Channel13,
           GPDMA1_Channel13_IRQn,
            (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_PHASE_PROBE_ENABLE != 0U) ?
                GPDMA1_REQUEST_TIM2_CH3 : GPDMA1_REQUEST_TIM8_UP,
            (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_PHASE_PROBE_ENABLE != 0U) ?
                capture_port->htim : divider_htim,
            (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_PHASE_PROBE_ENABLE != 0U) ?
                TIM_DMA_CC3 : TIM_DMA_UPDATE,
           capture_port->storage_bytes / XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_STRIDE_BYTES) != HAL_OK)
    {
        XE_NP_App_StopCaptureDmaPort(&xe_np_dma_gpioe);
        XE_NP_App_StopCaptureDmaPort(&xe_np_dma_gpiod);
        return HAL_ERROR;
    }

#if (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_PHASE_PROBE_ENABLE != 0U)
    __HAL_TIM_ENABLE_DMA(capture_port->htim, TIM_DMA_CC2 | TIM_DMA_CC3);
    if(np_capture_anchor_start_root(capture_port->htim,0) != HAL_OK)
#else
    __HAL_TIM_ENABLE_DMA(divider_htim, TIM_DMA_CC1 | TIM_DMA_UPDATE);
    if((HAL_TIM_Base_Start(divider_htim) != HAL_OK) ||
       (np_capture_anchor_start_root(capture_port->htim,0) != HAL_OK))
#endif
    {
        XE_NP_App_StopCaptureDmaPort(&xe_np_dma_gpioe);
        XE_NP_App_StopCaptureDmaPort(&xe_np_dma_gpiod);
        return HAL_ERROR;
    }
    XE_NP_App_RefreshDmaStatus();
    return HAL_OK;
}
#endif

#if (XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_INTERLEAVE_ENABLE != 0U)
static HAL_StatusTypeDef XE_NP_App_PrepareTimerForQuadDmaDivider(TIM_HandleTypeDef *htim)
{
    TIM_TypeDef *tim;

    if((htim == NULL) || ((htim->Instance != TIM8)
#if (XE_NP_APP_EXPERIMENT_GPIO_FULLPORT_QUAD_STRESS_ENABLE != 0U)
        && (htim->Instance != TIM3) && (htim->Instance != TIM4)
#endif
       ))
    {
        return HAL_ERROR;
    }

    __HAL_TIM_DISABLE_DMA(
        htim,
        TIM_DMA_UPDATE | TIM_DMA_CC1 | TIM_DMA_CC2 | TIM_DMA_CC3 | TIM_DMA_CC4);
    (void)HAL_TIM_Base_Stop(htim);
    __HAL_TIM_DISABLE(htim);
    tim = htim->Instance;
    tim->DIER &= ~(TIM_DIER_UDE | TIM_DIER_CC1DE | TIM_DIER_CC2DE |
                   TIM_DIER_CC3DE | TIM_DIER_CC4DE);
    tim->CCER &= ~(TIM_CCER_CC1E | TIM_CCER_CC2E | TIM_CCER_CC3E | TIM_CCER_CC4E);
    tim->SMCR = 0U;
    MODIFY_REG(tim->CR2, TIM_CR2_MMS, TIM_TRGO_RESET);
    tim->PSC = 0U;
    /* Consecutive TIM2 sample pulses are routed to distinct DMA lanes. */
#if (XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT == 3U)
    tim->ARR = 2U;
#else
    tim->ARR = 3U;
#endif
    if(tim == TIM8) { tim->RCR = 0U; }
    tim->CNT = 0U;
    tim->CCR1 = 1U;
    tim->CCR2 = 2U;
#if (XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT == 3U)
    tim->CCR3 = 0U;
    tim->CCR4 = 0U;
#else
    tim->CCR3 = 3U;
    /*
     * Keep the fourth request on a dedicated compare line.  TIM8 update is
     * coincident with the modulo wrap and has shown a lane-local timing fault
     * in the frozen 15 kHz ring; CC4 at zero is asserted at that same fourth
     * external-clock cell without using the update DMA handshake.
     */
    tim->CCR4 = 0U;
#endif
    tim->CCMR1 = 0U;
    tim->CCMR2 = 0U;
    tim->SMCR = TIM_SLAVEMODE_EXTERNAL1 | TIM_TS_ITR1;
#if (XE_NP_APP_QUAD_DMA_NO_PROBE_DRYRUN != 0U)
    /* Internal sample events, not an emulation of the NP data-valid eye. */
    tim->SMCR = 0U;
    tim->PSC = 7U;
#endif
    tim->EGR = TIM_EGR_UG;
    __HAL_TIM_CLEAR_FLAG(
        htim,
        TIM_FLAG_UPDATE | TIM_FLAG_CC1 | TIM_FLAG_CC2 | TIM_FLAG_CC3 | TIM_FLAG_CC4);
    return HAL_OK;
}

#if (XE_NP_APP_EXPERIMENT_GPIO_FULLPORT_QUAD_STRESS_ENABLE != 0U)
static uint32_t XE_NP_App_QuadDmaAuxMask(void)
{
    return (((np_mode_mask & 3U) == 3U) ? 2U : 0U) |
           (((np_mode_mask & 4U) && (np_mode_mask & 3U)) ? 4U : 0U);
}

static uint32_t XE_NP_App_QuadDmaRequest(uint32_t probe, uint32_t lane)
{
    static const uint32_t requests[3][4] = {
        {GPDMA1_REQUEST_TIM8_CH1, GPDMA1_REQUEST_TIM8_CH2,
         GPDMA1_REQUEST_TIM8_CH3, GPDMA1_REQUEST_TIM8_CH4},
        {GPDMA1_REQUEST_TIM3_CH1, GPDMA1_REQUEST_TIM3_CH2,
         GPDMA1_REQUEST_TIM3_CH3, GPDMA1_REQUEST_TIM3_CH4},
        {GPDMA1_REQUEST_TIM4_CH1, GPDMA1_REQUEST_TIM4_CH2,
         GPDMA1_REQUEST_TIM4_CH3, GPDMA1_REQUEST_TIM4_CH4}};
    if((probe > 2U) || (lane > 3U)) { return 0xFFFFFFFFUL; }
    const uint32_t auxiliary = XE_NP_App_QuadDmaAuxMask();
    const uint32_t timer = ((probe == 1U) && (auxiliary & 2U)) ? 1U :
                           ((probe == 2U) && (auxiliary & 4U)) ? 2U : 0U;
    return requests[timer][lane];
}

static HAL_StatusTypeDef XE_NP_App_PrepareQuadDmaAuxTimers(void)
{
    /* RM0456 p716 forbids sharing one hardware request across ready DMA
     * channels. TIM3/4 ITR1, like TIM8 ITR1, receive TIM2_TRGO (tables557/532).
     * Keep the existing primary/single-probe TIM8 path and raw-ring layout. */
    xe_np_quad_dma_aux_mask = XE_NP_App_QuadDmaAuxMask();
    if(xe_np_quad_dma_aux_mask & 2U)
    {
        __HAL_RCC_TIM3_CLK_ENABLE();
        __HAL_RCC_TIM3_FORCE_RESET();
        __HAL_RCC_TIM3_RELEASE_RESET();
        if(XE_NP_App_PrepareTimerForQuadDmaDivider(&xe_np_quad_dma_probe2_timer) != HAL_OK)
        { return HAL_ERROR; }
    }
    if(xe_np_quad_dma_aux_mask & 4U)
    {
        __HAL_RCC_TIM4_CLK_ENABLE();
        __HAL_RCC_TIM4_FORCE_RESET();
        __HAL_RCC_TIM4_RELEASE_RESET();
        if(XE_NP_App_PrepareTimerForQuadDmaDivider(&xe_np_quad_dma_probe3_timer) != HAL_OK)
        { return HAL_ERROR; }
    }
    return HAL_OK;
}

static void XE_NP_App_StartQuadDmaAuxTimers(void)
{
    const uint32_t requests = TIM_DIER_CC1DE | TIM_DIER_CC2DE |
                              TIM_DIER_CC3DE | TIM_DIER_CC4DE;
    if(xe_np_quad_dma_aux_mask & 2U)
    {
        TIM3->DIER = requests;
        TIM3->CR1 |= TIM_CR1_CEN;
    }
    if(xe_np_quad_dma_aux_mask & 4U)
    {
        TIM4->DIER = requests;
        TIM4->CR1 |= TIM_CR1_CEN;
    }
    /* All counters are armed before TIM2 emits the first sample event. */
    __DSB();
}
#endif

#if XE_NP_APP_P12315_PAIRED_GPIOE && \
    (!NP_SD_APP || !XE_NP_APP_QUAD_DMA_DAR_ONLY_ENABLE || \
     !XE_NP_APP_QUAD_DMA_PACK_WORDS || !XE_NP_APP_QUAD_DMA_LLI_SRAM2)
#error "P123 paired GPIOE requires the SD word-packed SRAM2-LLI capture path"
#endif
static HAL_StatusTypeDef XE_NP_App_StartGpioeQuadDmaLane(
    XE_NP_AppQuadDmaLane *lane,
    DMA_Channel_TypeDef *dma_instance,
    uint32_t request,
    uint32_t gpio_source_addr,
    uint32_t data_width,
    uint8_t *dst)
{
    if((lane == NULL) || (dma_instance == NULL) || (gpio_source_addr == 0U) ||
       ((data_width != DMA_SRC_DATAWIDTH_BYTE) &&
        (data_width != DMA_SRC_DATAWIDTH_HALFWORD)) ||
       (dst == NULL))
    {
        return HAL_ERROR;
    }

    memset(lane, 0, sizeof(*lane));
    lane->hdma.Instance = dma_instance;
#if (XE_NP_APP_QUAD_DMA_DAR_ONLY_ENABLE != 0U)
#if ((XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_PINGPONG_ENABLE != 0U) || \
     (XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_TIMED_SHADOW_ENABLE != 0U) || \
     ((XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_INTERLEAVE_USB_ENABLE != 0U) && !XE_NP_APP_QUAD_USB_WORD_EXPORT))
#error "Qualify quad DAR-only in isolated single-probe capture before enabling a consumer."
#endif
    {
        const uint32_t lane_bytes = XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_BLOCK_BYTES *
            XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_REPEAT_COUNT;
        uint32_t reload_address;
        uint32_t first_offset = 0U;
        uint32_t initial_link;
        uint32_t lane_index=12;
        for(unsigned i=0;i<4;i++) {
            if(lane==&xe_np_quad_dma_lanes[i])lane_index=i;
            if(lane==&xe_np_fullport_quad_dma_gpioe_high_lanes[i])lane_index=4+i;
            if(lane==&xe_np_fullport_quad_dma_gpiod_high_lanes[i])lane_index=8+i;
        }
        const uint32_t paired_lane = NP_P12315_PAIRED_GPIOE() && lane_index < 4U;
#if XE_NP_APP_P12315_PAIRED_GPIOE
        if(NP_P12315_PAIRED_GPIOE() &&
           ((lane_index >= 4U && lane_index < 8U) ||
            (paired_lane && data_width != DMA_SRC_DATAWIDTH_HALFWORD)))return HAL_ERROR;
#endif
        const uint32_t capture_source_port =
            (XE_NP_APP_P3_MULTI15_SOURCE_PORT1 && (lane_index >= 8U) &&
             (lane_index < 12U) && (np_mode_hz == 15000U) &&
             (np_mode_mask & 4U) && (np_mode_mask & 3U)) ?
                DMA_SRC_ALLOCATED_PORT1 : DMA_SRC_ALLOCATED_PORT0;
        uint32_t *steady_words = &lane->nodes[0].LinkRegisters[0];
        uint32_t *bootstrap_words = &lane->nodes[1].LinkRegisters[0];
#if (XE_NP_APP_QUAD_DMA_LLI_SRAM2 != 0U)
        if(lane_index >= 12U) { return HAL_ERROR; }
        steady_words = &xe_np_quad_dma_reload_words[lane_index][0];
        bootstrap_words = &xe_np_quad_dma_reload_words[lane_index][1];
        if((uint32_t)steady_words < 0x200c0000UL ||
           (uint32_t)(bootstrap_words + 3) > 0x200d0000UL) { return HAL_ERROR; }
#endif
        _Static_assert(XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_BLOCK_BYTES *
                       XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_REPEAT_COUNT <= 65535U,
                       "DAR-only uses a linear 16-bit byte count, not 2D repeats");
        _Static_assert(XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_BLOCK_BYTES *
                       XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_REPEAT_COUNT > 0U,
                       "Empty DMA lane");
        /* Timers must still be gated: install the minimal link BEFORE the
         * first EN write. Unlike __HAL_DMA_DISABLE, this never resets an
         * armed channel or edits CLLR while EN is set. */
        if((data_width != DMA_SRC_DATAWIDTH_BYTE
#if NP_SD_APP
            && data_width != DMA_SRC_DATAWIDTH_HALFWORD
#endif
           ) ||
           ((htim8.Instance->DIER & (TIM_DMA_CC1 | TIM_DMA_CC2 |
                                    TIM_DMA_CC3 | TIM_DMA_CC4 | TIM_DMA_UPDATE)) != 0U))
        { return HAL_ERROR; }
        lane->hdma.Init.Request = request;
        lane->hdma.Init.BlkHWRequest = DMA_BREQ_SINGLE_BURST;
        lane->hdma.Init.Direction = DMA_PERIPH_TO_MEMORY;
        lane->hdma.Init.SrcInc = DMA_SINC_FIXED;
        lane->hdma.Init.DestInc = DMA_DINC_INCREMENTED;
        lane->hdma.Init.SrcDataWidth = DMA_SRC_DATAWIDTH_BYTE;
        lane->hdma.Init.DestDataWidth = DMA_DEST_DATAWIDTH_BYTE;
#if NP_SD_APP
        lane->hdma.Init.SrcDataWidth = data_width;
#endif
#if (XE_NP_APP_QUAD_DMA_PACK_WORDS != 0U)
        /* One BYTE read per request remains unchanged. The FIFO combines
         * four chronological bytes into one aligned SRAM3 WORD write. */
        lane->hdma.Init.DestDataWidth = DMA_DEST_DATAWIDTH_WORD;
        if(((uint32_t)dst & 3U) || (lane_bytes & 3U)) { return HAL_ERROR; }
#endif
        lane->hdma.Init.Priority = DMA_HIGH_PRIORITY;
        lane->hdma.Init.SrcBurstLength = 1U;
        lane->hdma.Init.DestBurstLength = 1U;
        lane->hdma.Init.TransferAllocatedPort = capture_source_port | DMA_DEST_ALLOCATED_PORT1;
        lane->hdma.Init.TransferEventMode = DMA_TCEM_BLOCK_TRANSFER;
        lane->hdma.Init.Mode = DMA_NORMAL;
        if(HAL_DMA_Init(&lane->hdma) != HAL_OK) { return HAL_ERROR; }
#if (XE_NP_APP_QUAD_DMA_PACK_WORDS != 0U)
        MODIFY_REG(dma_instance->CTR1, DMA_CTR1_PAM, DMA_DATA_PACK);
#endif
        if(lane_index >= 12U) { return HAL_ERROR; }
        _Static_assert(XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT == 4U, "Four phase lanes required");
#if (XE_NP_APP_QUAD_DMA_STAGGER_ENABLE != 0U)
        _Static_assert((XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_BLOCK_BYTES *
                        XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_REPEAT_COUNT) % 4U == 0U,
                       "Stagger requires quarter-lane integer offsets");
        first_offset = lane_index * (lane_bytes / 12U);
#endif
        if(paired_lane)first_offset *= 2U; /* Physical bytes; logical phase is unchanged. */
#if (XE_NP_APP_QUAD_DMA_PACK_WORDS != 0U)
        if(first_offset & 3U) { return HAL_ERROR; }
#endif
        xe_np_quad_dma_initial_offsets[lane_index] = first_offset;
        if(paired_lane)
        {
            /* Two adjacent 62400-byte blocks retain the original 62400
             * logical samples. Both immutable LLIs fit this lane's eight
             * existing SRAM2 words; no capture IRQ or extra buffer. */
            uint32_t *node0 = steady_words;
            uint32_t *node1 = steady_words + 4U;
            const uint32_t updates = DMA_CLLR_UB1 | DMA_CLLR_UDA | DMA_CLLR_ULL;
            if(first_offset >= lane_bytes ||
               ((uint32_t)node0 & DMA_CLBAR_LBA) !=
               ((uint32_t)(node1 + 3U) & DMA_CLBAR_LBA))return HAL_ERROR;
            node0[0] = lane_bytes; node0[1] = (uint32_t)dst;
            node0[2] = ((uint32_t)node1 & DMA_CLLR_LA) | updates;
            node1[0] = lane_bytes; node1[1] = (uint32_t)dst + lane_bytes;
            node1[2] = ((uint32_t)node0 & DMA_CLLR_LA) | updates;
            XE_NP_App_DCacheClean(node0, 8U * sizeof(uint32_t));
            reload_address = (uint32_t)node0;
            initial_link = ((uint32_t)node1 & DMA_CLLR_LA) | updates;
        }
        else
        {
            steady_words[0] = (uint32_t)dst;
            XE_NP_App_DCacheClean(steady_words, sizeof(uint32_t));
            reload_address = (uint32_t)steady_words;
            initial_link = (reload_address & DMA_CLLR_LA) | DMA_CLLR_UDA;
            if(first_offset != 0U)
            {
                /* One short first block rotates physical storage without delaying
                 * ANY timer request. One bootstrap LLI installs full BNDT/CDAR and
                 * the permanent UDA-only link. Thereafter each lane reloads once
                 * per full ring, at a different quarter-ring time. */
                const uint32_t bootstrap = (uint32_t)bootstrap_words;
                if((bootstrap & DMA_CLBAR_LBA) != (reload_address & DMA_CLBAR_LBA) ||
                   ((bootstrap + 11U) & DMA_CLBAR_LBA) != (reload_address & DMA_CLBAR_LBA))
                { return HAL_ERROR; }
                bootstrap_words[0] = lane_bytes; /* UB1: CBR1 */
                bootstrap_words[1] = (uint32_t)dst; /* UDA: CDAR */
                bootstrap_words[2] = initial_link; /* ULL: CLLR */
                XE_NP_App_DCacheClean(bootstrap_words, 3U * sizeof(uint32_t));
                initial_link = (bootstrap & DMA_CLLR_LA) | DMA_CLLR_UB1 | DMA_CLLR_UDA | DMA_CLLR_ULL;
            }
        }
        /* The byte path retains its UDA-only reload; paired E uses the two
         * immutable block descriptors above. Both arm before timer release. */
        dma_instance->CLBAR = reload_address & DMA_CLBAR_LBA;
        dma_instance->CLLR = initial_link;
        /* Direct arm, no HAL callback/interrupt path. Configuration writes all
         * precede EN; the master timer is released only after ALL lanes arm. */
        dma_instance->CBR1 = lane_bytes - first_offset;
        dma_instance->CSAR = gpio_source_addr;
        dma_instance->CDAR = (uint32_t)dst + first_offset;
        dma_instance->CFCR = DMA_FLAG_TC | DMA_FLAG_HT | DMA_FLAG_DTE | DMA_FLAG_ULE |
            DMA_FLAG_USE | DMA_FLAG_SUSP | DMA_FLAG_TO;
        dma_instance->CCR = DMA_HIGH_PRIORITY; /* LAP0, full execution, ALL IRQs off */
        lane->hdma.State = HAL_DMA_STATE_BUSY;
        __DSB();
        dma_instance->CCR = DMA_HIGH_PRIORITY | DMA_CCR_EN;
        return HAL_OK;
    }
#else
    lane->hdma.InitLinkedList.Priority = DMA_HIGH_PRIORITY;
    lane->hdma.InitLinkedList.LinkStepMode = DMA_LSM_FULL_EXECUTION;
    /* The per-lane queue/node objects are in ordinary .bss at 0x2000xxxx
     * (SRAM1 / GPDMA port 0), whereas the GPIO capture destinations are in
     * SRAM3 / port 1.  Fetching the circular-link descriptor through port 1
     * made every 46,080-byte reload compete with the destination write path.
     * Keep that rare but timing-sensitive descriptor fetch on its real bank. */
    lane->hdma.InitLinkedList.LinkAllocatedPort = DMA_LINK_ALLOCATED_PORT0;
    /* No transfer interrupt is enabled: sampling is entirely peripheral paced. */
    lane->hdma.InitLinkedList.TransferEventMode = DMA_TCEM_EACH_LL_ITEM_TRANSFER;
    lane->hdma.InitLinkedList.LinkedListMode = DMA_LINKEDLIST_CIRCULAR;
    if((HAL_DMAEx_List_Init(&lane->hdma) != HAL_OK) ||
       (HAL_DMAEx_List_ResetQ(&lane->queue) != HAL_OK) ||
       (XE_NP_App_BuildCaptureNode(
             &lane->nodes[0],
             gpio_source_addr,
             data_width,
             dst,
#if (XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_LINEAR_CHANNELS_ENABLE != 0U)
             XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_BLOCK_BYTES *
                 XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_REPEAT_COUNT,
             1U,
#else
             XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_BLOCK_BYTES,
             XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_REPEAT_COUNT,
#endif
             0,
             request,
             0U,
             DMA_TRIG_POLARITY_MASKED) != HAL_OK) ||
       (HAL_DMAEx_List_InsertNode_Tail(&lane->queue, &lane->nodes[0]) != HAL_OK))
    {
        return HAL_ERROR;
    }

#if (XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_PINGPONG_ENABLE != 0U)
    /* The second node is a complete alternate epoch, never an overlapping
     * live range.  Its transition is the ownership hand-off used by the USB
     * path; both nodes retain the same edge-triggered GPIO transaction. */
    {
        const uint32_t epoch_bytes =
            XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_BLOCK_BYTES *
            XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_REPEAT_COUNT;

        if((XE_NP_App_BuildCaptureNode(
                &lane->nodes[1],
                gpio_source_addr,
                data_width,
                dst + epoch_bytes,
                epoch_bytes,
                1U,
                0,
                request,
                0U,
                DMA_TRIG_POLARITY_MASKED) != HAL_OK) ||
           (HAL_DMAEx_List_InsertNode_Tail(&lane->queue, &lane->nodes[1]) != HAL_OK))
        {
            return HAL_ERROR;
        }
    }
#endif
    if((HAL_DMAEx_List_SetCircularMode(&lane->queue) != HAL_OK) ||
       (HAL_DMAEx_List_LinkQ(&lane->hdma, &lane->queue) != HAL_OK))
    {
        return HAL_ERROR;
    }

    XE_NP_App_DCacheClean(&lane->queue, (uint32_t)sizeof(lane->queue));
    XE_NP_App_DCacheClean(&lane->nodes[0], (uint32_t)sizeof(lane->nodes[0]));
#if (XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_PINGPONG_ENABLE != 0U)
    XE_NP_App_DCacheClean(&lane->nodes[1], (uint32_t)sizeof(lane->nodes[1]));
#endif
    return HAL_DMAEx_List_Start(&lane->hdma);
#endif
}

#if (XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_TIMED_SHADOW_ENABLE != 0U)
static HAL_StatusTypeDef XE_NP_App_BuildTimedShadowNode(
    DMA_NodeTypeDef *node,
    const uint8_t *src,
    uint8_t *dst,
    uint32_t request)
{
    DMA_NodeConfTypeDef config;

    if((node == NULL) || (src == NULL) || (dst == NULL))
    {
        return HAL_ERROR;
    }
    memset(&config, 0, sizeof(config));
    config.NodeType = DMA_GPDMA_2D_NODE;
    config.Init.Request = request;
    config.Init.BlkHWRequest = DMA_BREQ_SINGLE_BURST;
    config.Init.Direction = DMA_MEMORY_TO_MEMORY;
    config.Init.SrcInc = DMA_SINC_INCREMENTED;
    config.Init.DestInc = DMA_DINC_INCREMENTED;
    config.Init.SrcDataWidth = DMA_SRC_DATAWIDTH_BYTE;
    config.Init.DestDataWidth = DMA_DEST_DATAWIDTH_BYTE;
    config.Init.Priority = DMA_LOW_PRIORITY_LOW_WEIGHT;
    config.Init.SrcBurstLength = 1U;
    config.Init.DestBurstLength = 1U;
    /* The source is live SRAM3 (GPDMA port 1); the private shadow is in
     * ordinary SRAM1 (port 0). */
    config.Init.TransferAllocatedPort = DMA_SRC_ALLOCATED_PORT1 | DMA_DEST_ALLOCATED_PORT0;
    config.Init.TransferEventMode = DMA_TCEM_REPEATED_BLOCK_TRANSFER;
    config.Init.Mode = DMA_NORMAL;
    config.DataHandlingConfig.DataExchange = DMA_EXCHANGE_NONE;
    config.DataHandlingConfig.DataAlignment = DMA_DATA_RIGHTALIGN_ZEROPADDED;
    config.TriggerConfig.TriggerMode = DMA_TRIGM_SINGLE_BURST_TRANSFER;
    config.TriggerConfig.TriggerPolarity = DMA_TRIG_POLARITY_MASKED;
    config.TriggerConfig.TriggerSelection = 0U;
    config.SrcAddress = (uint32_t)src;
    config.DstAddress = (uint32_t)dst;
    config.DataSize = XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_BLOCK_BYTES;
    config.RepeatBlockConfig.RepeatCount =
        XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_REPEAT_COUNT;
    config.RepeatBlockConfig.SrcAddrOffset = 0;
    config.RepeatBlockConfig.DestAddrOffset = 0;
    config.RepeatBlockConfig.BlkSrcAddrOffset = 0;
    config.RepeatBlockConfig.BlkDestAddrOffset = 0;
    return HAL_DMAEx_List_BuildNode(&config, node);
}

static HAL_StatusTypeDef XE_NP_App_StartGpioeQuadTimedShadowLane(
    XE_NP_AppQuadDmaLane *lane,
    DMA_Channel_TypeDef *instance,
    uint32_t request,
    const uint8_t *src,
    uint8_t *dst)
{
    if((lane == NULL) || (instance == NULL) || (src == NULL) || (dst == NULL))
    {
        return HAL_ERROR;
    }
    memset(lane, 0, sizeof(*lane));
    lane->hdma.Instance = instance;
    lane->hdma.InitLinkedList.Priority = DMA_LOW_PRIORITY_LOW_WEIGHT;
    lane->hdma.InitLinkedList.LinkStepMode = DMA_LSM_FULL_EXECUTION;
    lane->hdma.InitLinkedList.LinkAllocatedPort = DMA_LINK_ALLOCATED_PORT0;
    lane->hdma.InitLinkedList.TransferEventMode = DMA_TCEM_EACH_LL_ITEM_TRANSFER;
    lane->hdma.InitLinkedList.LinkedListMode = DMA_LINKEDLIST_CIRCULAR;
    if((HAL_DMAEx_List_Init(&lane->hdma) != HAL_OK) ||
       (HAL_DMAEx_List_ResetQ(&lane->queue) != HAL_OK) ||
       (XE_NP_App_BuildTimedShadowNode(&lane->nodes[0], src, dst, request) != HAL_OK) ||
       (HAL_DMAEx_List_InsertNode_Tail(&lane->queue, &lane->nodes[0]) != HAL_OK) ||
       (HAL_DMAEx_List_SetCircularMode(&lane->queue) != HAL_OK) ||
       (HAL_DMAEx_List_LinkQ(&lane->hdma, &lane->queue) != HAL_OK))
    {
        return HAL_ERROR;
    }
    XE_NP_App_DCacheClean(&lane->queue, (uint32_t)sizeof(lane->queue));
    XE_NP_App_DCacheClean(&lane->nodes[0], (uint32_t)sizeof(lane->nodes[0]));
    return HAL_DMAEx_List_Start(&lane->hdma);
}
#endif

static HAL_StatusTypeDef XE_NP_App_StartGpioeQuadDmaInterleave(XE_NP_Runtime *runtime)
{
    XE_NP_CapturePort *capture_port;
    static DMA_Channel_TypeDef *const dma_instances[
        XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT] = {
#if (XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT == 3U)
        GPDMA1_Channel12, GPDMA1_Channel13, GPDMA1_Channel14};
    static const uint32_t requests[XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT] = {
        GPDMA1_REQUEST_TIM8_CH1,
        GPDMA1_REQUEST_TIM8_CH2,
        GPDMA1_REQUEST_TIM8_UP};
#else
 #if (XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_LINEAR_CHANNELS_ENABLE != 0U)
        GPDMA1_Channel0, GPDMA1_Channel1, GPDMA1_Channel2, GPDMA1_Channel3};
 #else
        GPDMA1_Channel12, GPDMA1_Channel13, GPDMA1_Channel14, GPDMA1_Channel15};
 #endif
    static const uint32_t requests[XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT] = {
        GPDMA1_REQUEST_TIM8_CH1,
        GPDMA1_REQUEST_TIM8_CH2,
        GPDMA1_REQUEST_TIM8_CH3,
        GPDMA1_REQUEST_TIM8_CH4};
#if (XE_NP_APP_EXPERIMENT_GPIO_FULLPORT_QUAD_STRESS_ENABLE != 0U)
    static DMA_Channel_TypeDef *const gpioe_high_dma_instances[
        XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT] = {
        GPDMA1_Channel4, GPDMA1_Channel5, GPDMA1_Channel6, GPDMA1_Channel7};
    static DMA_Channel_TypeDef *const gpiod_high_dma_instances[
        XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT] = {
        GPDMA1_Channel8, GPDMA1_Channel9, GPDMA1_Channel10, GPDMA1_Channel11};
#endif
#if (XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_TIMED_SHADOW_ENABLE != 0U)
    static DMA_Channel_TypeDef *const shadow_dma_instances[
        XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT] = {
        /* The shadow is bandwidth work and needs 2D addressing; retain the
         * large-FIFO channels 12..15 for it, leaving the compact channels
         * 0..3 exclusively for the timer-latency-critical GPIO reads. */
        GPDMA1_Channel12, GPDMA1_Channel13, GPDMA1_Channel14, GPDMA1_Channel15};
    /* A shadow must not read the same SRAM3 byte on the same edge that
     * capture writes it.  This qualification point keeps three complete TIM8
     * phases between write and source read: CH1 -> CH4, ... CH4 -> next CH3. */
    static const uint32_t shadow_requests[
        XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT] = {
        GPDMA1_REQUEST_TIM8_CH4,
        GPDMA1_REQUEST_TIM8_CH1,
        GPDMA1_REQUEST_TIM8_CH2,
        GPDMA1_REQUEST_TIM8_CH3};
#endif
#endif
    uint32_t gpio_source_addr;
    uint32_t lane_bytes;
    uint32_t gpio_data_width;
#if (XE_NP_APP_EXPERIMENT_GPIO_FULLPORT_QUAD_STRESS_ENABLE != 0U)
    uint32_t gpioe_high_source_addr;
    uint32_t gpiod_high_source_addr;
    uint8_t *gpioe_high_storage;
    uint8_t *gpiod_high_storage;
#endif

    np3_reset();
    xe_np_quad_dma_start_stage = 1U;
    if((runtime == NULL) || (runtime->capture.gpioe.htim == NULL) ||
       (runtime->capture.gpiod.htim == NULL))
    {
        return HAL_ERROR;
    }
    capture_port = &runtime->capture.gpioe;
    xe_np_quad_dma_start_stage = 2U;
    if(runtime->capture.raw_storage == NULL)
    {
        xe_np_quad_dma_start_stage = 21U;
        return HAL_ERROR;
    }
    if((capture_port->bytes_per_tick != sizeof(uint8_t)) &&
       (capture_port->bytes_per_tick != sizeof(uint16_t)))
    {
        xe_np_quad_dma_start_stage = 22U;
        return HAL_ERROR;
    }
#if (XE_NP_APP_EXPERIMENT_GPIO_FULLPORT_QUAD_STRESS_ENABLE != 0U)
    if((runtime->capture.raw_storage_bytes %
        (XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT * 3U)) != 0U)
    {
        xe_np_quad_dma_start_stage = 23U;
        return HAL_ERROR;
    }
    if(runtime->capture.raw_storage_bytes <
       XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT * 3U *
       XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_BLOCK_BYTES *
       XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_REPEAT_COUNT)
    {xe_np_quad_dma_start_stage=23U;return HAL_ERROR;}
    lane_bytes = XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_BLOCK_BYTES *
                 XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_REPEAT_COUNT;
#else
    if((runtime->capture.raw_storage_bytes %
        XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT) != 0U)
    {
        xe_np_quad_dma_start_stage = 23U;
        return HAL_ERROR;
    }
    if(runtime->capture.raw_storage_bytes <
       XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT *
       XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_BLOCK_BYTES *
       XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_REPEAT_COUNT)
    {xe_np_quad_dma_start_stage=23U;return HAL_ERROR;}
    lane_bytes = XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_BLOCK_BYTES *
                 XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_REPEAT_COUNT;
#endif
    if(lane_bytes != (XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_BLOCK_BYTES *
                      XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_REPEAT_COUNT
#if (XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_PINGPONG_ENABLE != 0U)
                      * 2U
#endif
                      ))
    {
        xe_np_quad_dma_start_stage = 24U;
        return HAL_ERROR;
    }
#if (XE_NP_APP_EXPERIMENT_GPIO_FULLPORT_QUAD_STRESS_ENABLE != 0U)
    /* Preserve one-byte peripheral transactions at the proven DDR phase.
     * Three independently stored groups exactly cover the wired NP1, NP2 and
     * NP3 buses; GPIOD[7:0] is not connected on this board. */
    gpio_source_addr = (uint32_t)&GPIOE->IDR;
    gpio_data_width = NP_P12315_PAIRED_GPIOE() ? DMA_SRC_DATAWIDTH_HALFWORD : DMA_SRC_DATAWIDTH_BYTE;
    gpioe_high_source_addr = ((uint32_t)&GPIOE->IDR) + 1U;
    gpiod_high_source_addr = ((uint32_t)&GPIOD->IDR) + 1U;
    gpioe_high_storage = runtime->capture.raw_storage +
                    (XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT * lane_bytes);
    gpiod_high_storage = gpioe_high_storage +
                    (XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT * lane_bytes);
#else
    if(capture_port->bytes_per_tick == sizeof(uint16_t))
    {
        /* The aligned GPIOE diagnostic and the eventual NP1+NP2 port both
         * use a true halfword peripheral access. */
        gpio_source_addr = (uint32_t)&GPIOE->IDR;
        gpio_data_width = DMA_SRC_DATAWIDTH_HALFWORD;
    }
    else if(capture_port->active_probe_mask == XE_NP_CAPTURE_PROBE1_MASK)
    {
        gpio_source_addr = (uint32_t)&GPIOE->IDR;
        gpio_data_width = DMA_SRC_DATAWIDTH_BYTE;
    }
    else if(capture_port->active_probe_mask == XE_NP_CAPTURE_PROBE2_MASK)
    {
        gpio_source_addr = ((uint32_t)&GPIOE->IDR) + 1U;
        gpio_data_width = DMA_SRC_DATAWIDTH_BYTE;
    }
    else
    {
        return HAL_ERROR;
    }
#endif

    xe_np_quad_dma_active = 0U;
    xe_np_quad_dma_error_seen = 0U;
#if (XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_INTERLEAVE_USB_ENABLE != 0U)
    xe_np_quad_dma_usb_cursor_valid = 0U;
    xe_np_quad_dma_usb_next_lane = 0U;
    xe_np_quad_dma_usb_export_debug = 0U;
    xe_np_quad_dma_usb_batch_claimed = 0U;
#if (XE_NP_APP_QUAD_USB_WORD_EXPORT != 0U)
    memset((void *)xe_np_quad_usb_word_diag, 0, sizeof(xe_np_quad_usb_word_diag));
    xe_np_quad_usb_word_diag[0] = 0x51555831UL;
    xe_np_quad_usb_start_ms = HAL_GetTick();
    xe_np_quad_usb_last_poll_ms = xe_np_quad_usb_start_ms;
    xe_np_quad_usb_history_valid = 0U;
#if NP_SD_APP
    memset(xe_np_sd_ring_clock,0,sizeof(xe_np_sd_ring_clock));
    memset((void*)xe_np_sd_ring_clock_diag,0,sizeof(xe_np_sd_ring_clock_diag));
    CoreDebug->DEMCR|=CoreDebug_DEMCR_TRCENA_Msk;
    DWT->CTRL|=DWT_CTRL_CYCCNTENA_Msk;
#endif
#endif
#endif
    XE_NP_App_DCachePrepare(runtime->capture.raw_storage, runtime->capture.raw_storage_bytes);
#if (XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_TIMED_SHADOW_ENABLE != 0U)
    if(sizeof(xe_np_quad_dma_timed_shadow_storage) != runtime->capture.raw_storage_bytes)
    {
        xe_np_quad_dma_start_stage = 25U;
        return HAL_ERROR;
    }
    XE_NP_App_DCachePrepare(
        xe_np_quad_dma_timed_shadow_storage,
        (uint32_t)sizeof(xe_np_quad_dma_timed_shadow_storage));
#endif
    xe_np_quad_dma_start_stage = 3U;
    if((XE_NP_App_PrepareTimerForTim2Ch1Capture(capture_port->htim) != HAL_OK) ||
       (XE_NP_App_PrepareTimerForQuadDmaDivider(runtime->capture.gpiod.htim) != HAL_OK))
    {
        return HAL_ERROR;
    }
#if (XE_NP_APP_EXPERIMENT_GPIO_FULLPORT_QUAD_STRESS_ENABLE != 0U)
    if(XE_NP_App_PrepareQuadDmaAuxTimers() != HAL_OK)
    {
        XE_NP_App_StopGpioeQuadDmaInterleave();
        return HAL_ERROR;
    }
#endif

    for(uint32_t lane = 0U; lane < XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT;
        ++lane)
    {
        if(!(np_mode_mask&1U))continue;
        xe_np_quad_dma_start_stage = 10U + lane;
        if(XE_NP_App_StartGpioeQuadDmaLane(
               &xe_np_quad_dma_lanes[lane],
               dma_instances[(lane + XE_NP_APP_QUAD_DMA_CHANNEL_ROTATION) & 3U],
               requests[lane],
               gpio_source_addr,
               gpio_data_width,
               runtime->capture.raw_storage + (lane * lane_bytes *
                   (NP_P12315_PAIRED_GPIOE() ? 2U : 1U))) != HAL_OK)
        {
            xe_np_quad_dma_error_seen = 1U;
            XE_NP_App_StopGpioeQuadDmaInterleave();
            return HAL_ERROR;
        }
    }
#if (XE_NP_APP_EXPERIMENT_GPIO_FULLPORT_QUAD_STRESS_ENABLE != 0U)
    /* Additional probes use distinct timer requests, never hardware-request
     * fanout. Their SRAM3 lanes and logical phase order remain unchanged. */
    for(uint32_t lane = 0U; lane < XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT;
        ++lane)
    {
        if(NP_P12315_PAIRED_GPIOE() || !(np_mode_mask&2U))continue;
        xe_np_quad_dma_start_stage = 14U + lane;
        if(XE_NP_App_StartGpioeQuadDmaLane(
               &xe_np_fullport_quad_dma_gpioe_high_lanes[lane],
               gpioe_high_dma_instances[lane],
               XE_NP_App_QuadDmaRequest(1U, lane),
               gpioe_high_source_addr,
               DMA_SRC_DATAWIDTH_BYTE,
               gpioe_high_storage + (lane * lane_bytes)) != HAL_OK)
        {
            xe_np_quad_dma_error_seen = 1U;
            XE_NP_App_StopGpioeQuadDmaInterleave();
            return HAL_ERROR;
        }
    }
    for(uint32_t lane = 0U; lane < XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT;
        ++lane)
    {
        if(!(np_mode_mask&4U))continue;
        xe_np_quad_dma_start_stage = 18U + lane;
        if(XE_NP_App_StartGpioeQuadDmaLane(
               &xe_np_fullport_quad_dma_gpiod_high_lanes[lane],
               gpiod_high_dma_instances[lane],
               XE_NP_App_QuadDmaRequest(2U, lane),
               gpiod_high_source_addr,
               DMA_SRC_DATAWIDTH_BYTE,
               gpiod_high_storage + (lane * lane_bytes)) != HAL_OK)
        {
            xe_np_quad_dma_error_seen = 1U;
            XE_NP_App_StopGpioeQuadDmaInterleave();
            return HAL_ERROR;
        }
    }
#endif
#if (XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_TIMED_SHADOW_ENABLE != 0U)
    /* Arm all shadows before releasing TIM8.  The matching high-priority
     * capture channel always wins the shared compare request; the shadow has
     * the rest of that lane's four-phase period to finish its one-byte copy. */
    for(uint32_t lane = 0U; lane < XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT;
        ++lane)
    {
        xe_np_quad_dma_start_stage = 30U + lane;
        if(XE_NP_App_StartGpioeQuadTimedShadowLane(
               &xe_np_quad_dma_timed_shadow_lanes[lane],
               shadow_dma_instances[lane],
               shadow_requests[lane],
               runtime->capture.raw_storage + (lane * lane_bytes),
               xe_np_quad_dma_timed_shadow_storage + (lane * lane_bytes)) != HAL_OK)
        {
            xe_np_quad_dma_error_seen = 1U;
            XE_NP_App_StopGpioeQuadDmaInterleave();
            return HAL_ERROR;
        }
    }
#endif

    __HAL_TIM_ENABLE_DMA(
        runtime->capture.gpiod.htim,
        TIM_DMA_CC1 | TIM_DMA_CC2
#if (XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT == 4U)
        | TIM_DMA_CC3 | TIM_DMA_CC4
#else
        | TIM_DMA_UPDATE
#endif
    );
    xe_np_quad_dma_start_stage = 20U;
#if (XE_NP_APP_QUAD_USB_PACED != 0U)
    /* A separate compare request, not a duplicate of a capture request.
     * One word per raw sample event provides 4x nominal export headroom. */
    /* NP15 P3 phase tuning must not also move SD staging from its six-tick
     * baseline. Other profiles retain their existing export timing. */
    capture_port->htim->Instance->CCR3 =
        ((np_mode_hz == 15000U) && (np_mode_mask & 4U) && (np_mode_mask & 3U)) ?
            2U : (capture_port->htim->Instance->CCR2 + 4U) & 7U;
    capture_port->htim->Instance->CCMR2 &= ~(TIM_CCMR2_CC3S | TIM_CCMR2_OC3M);
    capture_port->htim->Instance->DIER |= TIM_DMA_CC3;
#endif
#if (XE_NP_APP_EXPERIMENT_GPIO_FULLPORT_QUAD_STRESS_ENABLE != 0U)
    XE_NP_App_StartQuadDmaAuxTimers();
#endif
    if((HAL_TIM_Base_Start(runtime->capture.gpiod.htim) != HAL_OK) ||
       (np_capture_anchor_start_root(capture_port->htim,NP_CaptureAnchor_Prearmed()) != HAL_OK))
    {
        xe_np_quad_dma_error_seen = 1U;
        XE_NP_App_StopGpioeQuadDmaInterleave();
        return HAL_ERROR;
    }

    xe_np_quad_dma_active = 1U;
    xe_np_quad_dma_start_stage = 0U;
    XE_NP_App_RefreshDmaStatus();
    return HAL_OK;
}
#endif

static HAL_StatusTypeDef XE_NP_App_StartCaptureDma(XE_NP_Runtime *runtime)
{
    if(runtime == NULL)
    {
        return HAL_ERROR;
    }

#if (XE_NP_APP_EXPERIMENT_FREEZE_CAPTURE_AFTER_MS != 0U)
    xe_np_capture_freeze_deadline_tick_ms = 0U;
    xe_np_capture_freeze_done = 0U;
#endif

    xe_np_capture_shutdown_reason = 0U;
    XE_NP_App_StopCaptureDma();
    np_capture_anchor_arm();
    __HAL_RCC_GPDMA1_CLK_ENABLE();

#if (XE_NP_APP_EXPERIMENT_CAPTURE_SUPPRESS_DMA_ENABLE != 0U)
    /* Bring-up isolation only: preserve the complete NP power/reset/MCLK
     * state while deliberately not arming a hardware capture request. */
    if((runtime->capture.flags & XE_NP_CAPTURE_FLAG_GPIOE_ACTIVE) != 0U)
    {
        xe_np_dma_gpioe.active = 1U;
    }
    if((runtime->capture.flags & XE_NP_CAPTURE_FLAG_GPIOD_ACTIVE) != 0U)
    {
        xe_np_dma_gpiod.active = 1U;
    }
    XE_NP_App_RefreshDmaStatus();
    return HAL_OK;
#endif

#if (XE_NP_APP_GPIOE_CAPTURE_USE_PA0_TIM2_CH1 != 0U)
    if(((runtime->capture.flags & XE_NP_CAPTURE_FLAG_GPIOE_ACTIVE) != 0U) &&
       ((runtime->capture.flags & XE_NP_CAPTURE_FLAG_GPIOD_ACTIVE) != 0U) &&
       (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_ENABLE == 0U) &&
       (XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_INTERLEAVE_ENABLE == 0U))
    {
        return HAL_ERROR;
    }
#endif

    if((runtime->capture.flags & XE_NP_CAPTURE_FLAG_GPIOE_ACTIVE) != 0U)
    {
#if (XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_INTERLEAVE_ENABLE != 0U)
        if(XE_NP_App_StartGpioeQuadDmaInterleave(runtime) != HAL_OK)
        {
            XE_NP_App_StopCaptureDma();
            return HAL_ERROR;
        }
#elif (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_ENABLE != 0U)
        if(XE_NP_App_StartGpioeDualDmaInterleave(runtime) != HAL_OK)
        {
            XE_NP_App_StopCaptureDma();
            return HAL_ERROR;
        }
#else
        if(XE_NP_App_StartCaptureDmaPort(
               &xe_np_dma_gpioe,
               &runtime->capture,
               &runtime->sys_param,
               0U) != HAL_OK)
        {
            XE_NP_App_StopCaptureDma();
            return HAL_ERROR;
        }
#endif
    }

#if ((XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_ENABLE == 0U) && \
     (XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_INTERLEAVE_ENABLE == 0U))
    if((runtime->capture.flags & XE_NP_CAPTURE_FLAG_GPIOD_ACTIVE) != 0U)
    {
        if(XE_NP_App_StartCaptureDmaPort(
               &xe_np_dma_gpiod,
               &runtime->capture,
               &runtime->sys_param,
               1U) != HAL_OK)
        {
            XE_NP_App_StopCaptureDma();
            return HAL_ERROR;
        }
    }
#endif

    XE_NP_App_RefreshDmaStatus();
#if (XE_NP_APP_EXPERIMENT_FREEZE_CAPTURE_AFTER_MS != 0U)
    xe_np_capture_freeze_deadline_tick_ms =
        HAL_GetTick() + XE_NP_APP_EXPERIMENT_FREEZE_CAPTURE_AFTER_MS;
#endif
    return HAL_OK;
}

static void XE_NP_App_ConfigureCapture(XE_NP_Runtime *runtime)
{
    uint8_t *capture_storage;
    uint32_t capture_storage_bytes;
    uint32_t psb_clk_hz;
    uint32_t capture_status_mask;
    uint32_t capture_status;
    uint32_t runtime_flag_mask;
    uint32_t capture_flags;
    uint32_t blocking_flags;

    if(runtime == NULL)
    {
        return;
    }

    capture_storage = XE_NP_App_SelectCaptureStorage(&capture_storage_bytes);
#if (XE_NP_APP_CAPTURE_DMA_DAR_ONLY_CIRCULAR_ENABLE != 0U)
    /* The DAR-only LLI can restore one hardware block, not an arbitrarily
     * large 2D ring.  The backing allocation remains in SRAM3; only this
     * qualification producer uses its first, frame-aligned block. */
    if(capture_storage_bytes < XE_NP_APP_CAPTURE_DMA_DAR_ONLY_RING_BYTES)
    {
        return;
    }
    capture_storage_bytes = XE_NP_APP_CAPTURE_DMA_DAR_ONLY_RING_BYTES;
#endif
    psb_clk_hz = XE_NP_Board_GetRuntimeExpectedPsbClkHz();
    XE_NP_Capture_Init(
        &runtime->capture,
        capture_storage,
        capture_storage_bytes);
    XE_NP_Capture_BindTimers(
        &runtime->capture,
        runtime->htim_np_capture_gpioe,
        runtime->htim_np_capture_gpiod);
    (void)XE_NP_Capture_Configure(
        &runtime->capture,
        XE_NP_CAPTURE_PROBE2_MASK, /* timer pair only; twelve lanes own physical buses */
        psb_clk_hz);

    /*
     * The board may currently have either GPIOE probe populated.  Keep normal
     * operation byte-wide, while allowing the explicit diagnostic to acquire
     * an aligned GPIOE halfword regardless of which physical half is active.
     */
    if((((runtime->active_np_probe_mask & XE_NP_CAPTURE_ALL_PROBES_MASK) == XE_NP_CAPTURE_PROBE1_MASK) ||
        ((runtime->active_np_probe_mask & XE_NP_CAPTURE_ALL_PROBES_MASK) == XE_NP_CAPTURE_PROBE2_MASK)) &&
        (runtime->capture.layout == XE_NP_CAPTURE_LAYOUT_GPIOE_ONLY) &&
        ((runtime->capture.gpioe.active_probe_mask == XE_NP_CAPTURE_PROBE1_MASK) ||
         (runtime->capture.gpioe.active_probe_mask == XE_NP_CAPTURE_PROBE2_MASK)))
    {
#if (XE_NP_APP_CAPTURE_FORCE_NP2_HALFWORD_DIAG != 0U)
        runtime->capture.bytes_per_tick = sizeof(uint16_t);
        runtime->capture.gpioe.bytes_per_tick = sizeof(uint16_t);
        runtime->capture.raw_bytes_per_sec = psb_clk_hz * 2U;
        runtime->capture.gpioe.raw_bytes_per_sec = psb_clk_hz * 2U;
#else
        runtime->capture.bytes_per_tick = sizeof(uint8_t);
        runtime->capture.gpioe.bytes_per_tick = sizeof(uint8_t);
        runtime->capture.raw_bytes_per_sec = psb_clk_hz;
        runtime->capture.gpioe.raw_bytes_per_sec = psb_clk_hz;
#endif
        runtime->capture.raw_bytes_per_frame =
            runtime->capture.bytes_per_tick * XE_NP_CAPTURE_FRAME_BIT_COUNT;
        runtime->capture.raw_bytes_per_superframe =
            runtime->capture.raw_bytes_per_frame * XE_NP_CAPTURE_FRAMES_PER_SUPERFRAME;
        runtime->capture.raw_bytes_per_ultraframe =
             runtime->capture.raw_bytes_per_superframe * XE_NP_CAPTURE_SUPERFRAMES_PER_ULTRAFRAME;
    }

#if (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_PUBLISH_ENABLE != 0U)
    if(XE_NP_Capture_EnableGpioeInterleavedLanes(&runtime->capture) != 0)
    {
        runtime->capture.flags |= XE_NP_CAPTURE_FLAG_STORAGE_LAYOUT_UNSUPPORTED;
    }
#endif

    capture_status_mask = XE_NP_SYSTEM_STATUS_NP_CAPTURE_TIMERS_READY |
                          XE_NP_SYSTEM_STATUS_NP_CAPTURE_RAW_ONLY |
                          XE_NP_SYSTEM_STATUS_NP_CAPTURE_BUFFER_TIGHT |
                          XE_NP_SYSTEM_STATUS_NP_CAPTURE_BUFFER_SHORT |
                          XE_NP_SYSTEM_STATUS_NP_CAPTURE_DMA_STRESSED |
                          XE_NP_SYSTEM_STATUS_NP_CAPTURE_DMA_ACTIVE |
                          XE_NP_SYSTEM_STATUS_NP_CAPTURE_SYNC_LOCKED |
                          XE_NP_SYSTEM_STATUS_NP_CAPTURE_VALIDATION_ERROR |
                          XE_NP_SYSTEM_STATUS_NP_CAPTURE_DMA_ERROR |
                          XE_NP_SYSTEM_STATUS_NP_CAPTURE_BACKLOG_ACTIVE |
                          XE_NP_SYSTEM_STATUS_NP_CAPTURE_OVERRUN;
    runtime_flag_mask = XE_NP_RUNTIME_FLAG_NP_CAPTURE_TIMERS_READY |
                        XE_NP_RUNTIME_FLAG_NP_CAPTURE_RAW_ONLY |
                        XE_NP_RUNTIME_FLAG_NP_CAPTURE_BUFFER_TIGHT |
                        XE_NP_RUNTIME_FLAG_NP_CAPTURE_BUFFER_SHORT |
                        XE_NP_RUNTIME_FLAG_NP_CAPTURE_DMA_ACTIVE |
                        XE_NP_RUNTIME_FLAG_NP_CAPTURE_SYNC_LOCKED |
                        XE_NP_RUNTIME_FLAG_NP_CAPTURE_VALIDATION_ERROR |
                        XE_NP_RUNTIME_FLAG_NP_CAPTURE_DMA_ERROR |
                        XE_NP_RUNTIME_FLAG_NP_CAPTURE_BACKLOG_ACTIVE |
                        XE_NP_RUNTIME_FLAG_NP_CAPTURE_OVERRUN;

    capture_status = runtime->sys_param.system_status & ~capture_status_mask;
    runtime->flags &= ~runtime_flag_mask;
    capture_flags = runtime->capture.flags;

    if((capture_flags & XE_NP_CAPTURE_FLAG_TIMERS_READY) != 0U)
    {
        runtime->flags |= XE_NP_RUNTIME_FLAG_NP_CAPTURE_TIMERS_READY;
        capture_status |= XE_NP_SYSTEM_STATUS_NP_CAPTURE_TIMERS_READY;
    }
    if((capture_flags & XE_NP_CAPTURE_FLAG_RAW_ONLY) != 0U)
    {
        runtime->flags |= XE_NP_RUNTIME_FLAG_NP_CAPTURE_RAW_ONLY;
        capture_status |= XE_NP_SYSTEM_STATUS_NP_CAPTURE_RAW_ONLY;
    }
    if((capture_flags & XE_NP_CAPTURE_FLAG_BUFFER_TIGHT) != 0U)
    {
        runtime->flags |= XE_NP_RUNTIME_FLAG_NP_CAPTURE_BUFFER_TIGHT;
        capture_status |= XE_NP_SYSTEM_STATUS_NP_CAPTURE_BUFFER_TIGHT;
    }
    if((capture_flags & XE_NP_CAPTURE_FLAG_BUFFER_SHORT) != 0U)
    {
        runtime->flags |= XE_NP_RUNTIME_FLAG_NP_CAPTURE_BUFFER_SHORT;
        capture_status |= XE_NP_SYSTEM_STATUS_NP_CAPTURE_BUFFER_SHORT;
    }
    if((capture_flags & XE_NP_CAPTURE_FLAG_DMA_RATE_STRESSED) != 0U)
    {
        capture_status |= XE_NP_SYSTEM_STATUS_NP_CAPTURE_DMA_STRESSED;
    }

    runtime->sys_param.system_status = capture_status;
    XE_NP_App_StopCaptureDma();

    blocking_flags = XE_NP_APP_CAPTURE_BLOCKING_FLAGS;
    if((capture_flags & blocking_flags) != 0U)
    {
        xe_np_probe_state.initialized = 0U;
        xe_np_probe_state.init_failed = 1U;
        xe_np_probe_state.i2c_ready = 0U;
        xe_np_probe_state.base_configured = 0U;
        xe_np_probe_state.shank_defaults_active = 0U;
        CE32_ReportError(&runtime->sys_param, CE32_ERRORID_PARAM_ERR);
        XE_NP_App_RefreshProbeStatus(runtime);
        XE_NP_App_RefreshDmaStatus();
        return;
    }

    XE_NP_App_RefreshDmaStatus();
}

static void XE_NP_App_SetProbeStartupFailureState(
    XE_NP_Runtime *runtime,
    uint8_t i2c_ready,
    uint8_t base_configured)
{
    xe_np_capture_shutdown_reason = 1U; /* probe bring-up/lock failure */
#if (XE_NP_APP_BENCH_HOLD_PROBE_ON_FAILURE == 0U)
    XE_NP_App_ApplySafeNpStartup(runtime);
#endif

    xe_np_probe_state.initialized = 0U;
    xe_np_probe_state.init_failed = 1U;
    xe_np_probe_state.i2c_ready = i2c_ready;
    xe_np_probe_state.base_configured = base_configured;
    xe_np_probe_state.shank_defaults_active = 0U;
    xe_np_probe_state.bringup_stage = XE_NP_APP_PROBE_STAGE_IDLE;
    xe_np_probe_state.active_probe_index = 0U;
    xe_np_probe_state.stage_deadline_tick_ms = 0U;
    XE_NP_App_RefreshProbeStatus(runtime);
}

#include "np_continuous_clock_reset.inc"
static void XE_NP_App_ServiceProbeBringUp(XE_NP_Runtime *runtime)
{
    uint32_t now_ms;
    uint8_t probe_index;

    if(runtime == NULL || (!np_mode_mask && !np_mode_hz))
    {
        return;
    }
    now_ms = HAL_GetTick();
#if (XE_NP_APP_BENCH_HOLD_PROBE_ON_FAILURE != 0U)
    if((xe_np_probe_state.init_failed != 0U) &&
       (xe_np_probe_state.next_retry_tick_ms == 0U))
    {
        return;
    }
#endif
    if((xe_np_probe_state.initialized != 0U) ||
       ((xe_np_probe_state.next_retry_tick_ms != 0U) &&
        (XE_NP_App_TimeReached(now_ms, xe_np_probe_state.next_retry_tick_ms) == 0)))
    {
        return;
    }

    if(NP_StartupPowerStep(runtime,now_ms))return;

    if(((XE_NP_Runtime_HasValidNpMclk(runtime) == 0) &&
        (xe_np_probe_state.bringup_stage != XE_NP_APP_PROBE_STAGE_IDLE)) ||
#if (XE_NP_APP_CAPTURE_SELFTEST_ENABLE != 0U)
       ((runtime->flags & XE_NP_RUNTIME_FLAG_NP_CAPTURE_SELFTEST_OK) == 0U) ||
#endif
#if (XE_NP_APP_LEGACY_FS_ENABLE != 0U)
       (runtime->hsd_card == NULL) ||
       ((runtime->flags & XE_NP_RUNTIME_FLAG_SD_PRESENT) == 0U) ||
       ((runtime->sys_param.error_code &
         (CE32_ERRORID_SD_CARDFULL | CE32_ERRORID_SD_FORMATERR)) != 0U) ||
#endif
       (XE_NP_App_AllActiveProbeInterfacesReady(runtime) == 0) ||
       ((runtime->capture.flags & XE_NP_APP_CAPTURE_BLOCKING_FLAGS) != 0U))
    {
        return;
    }

    XE_NP_App_EnsureProbeConfigsInitialized();
    if(XE_NP_App_ActiveProbeConfigsReady(runtime) == 0)
    {
        xe_np_probe_state.init_failed = 1U;
        xe_np_probe_state.i2c_ready = 0U;
        xe_np_probe_state.base_configured = 0U;
        xe_np_probe_state.shank_defaults_active = 0U;
        XE_NP_App_UpdateProbeDiag(
            runtime,
            NULL,
            xe_np_probe_state.bringup_stage,
            xe_np_probe_state.active_probe_index,
            XE_NP_APP_PROBE_DIAG_FAILURE_CONFIG_NOT_READY,
            HAL_ERROR);
        if(xe_np_probe_state.init_error_reported == 0U)
        {
            CE32_ReportError(&runtime->sys_param, CE32_ERRORID_PARAM_ERR);
            xe_np_probe_state.init_error_reported = 1U;
        }
        XE_NP_App_RefreshProbeStatus(runtime);
        return;
    }

    switch((XE_NP_AppProbeBringUpStage)xe_np_probe_state.bringup_stage)
    {
        case XE_NP_APP_PROBE_STAGE_IDLE:
        {
            XE_NP_App_BeginProbeBringUpFromIdle(runtime, now_ms);
            return;
        }

        case XE_NP_APP_PROBE_STAGE_WAIT_POWER_RAILS:
        {
            if(XE_NP_App_TimeReached(now_ms, xe_np_probe_state.stage_deadline_tick_ms) == 0)
            {
                return;
            }

            NP_ModeReleaseReset(NP_NRST_GPIO_Port, NP_NRST_Pin);
            runtime->flags |= XE_NP_RUNTIME_FLAG_NP_GLOBAL_RESET_RELEASED;
            runtime->flags &= ~XE_NP_RUNTIME_FLAG_NP_RECORD_RESET_RELEASED;
            xe_np_probe_state.stage_deadline_tick_ms = now_ms + XE_NP_APP_PROBE_RESET_SETTLE_MS;
            xe_np_probe_state.bringup_stage = XE_NP_APP_PROBE_STAGE_WAIT_GLOBAL_RESET;
            XE_NP_App_RefreshProbeStatus(runtime);
            return;
        }

        case XE_NP_APP_PROBE_STAGE_WAIT_GLOBAL_RESET:
        {
            if(XE_NP_App_TimeReached(now_ms, xe_np_probe_state.stage_deadline_tick_ms) == 0)
            {
                return;
            }

#if (XE_NP_APP_EXPERIMENT_RELEASE_RECORD_RESET_BEFORE_NRIC_CONFIG != 0U)
            if((runtime->flags & XE_NP_RUNTIME_FLAG_NP_RECORD_RESET_RELEASED) == 0U)
            {
                NP_ModeReleaseReset(NP_REC_NRST_GPIO_Port, NP_REC_NRST_Pin);
                runtime->flags |= XE_NP_RUNTIME_FLAG_NP_RECORD_RESET_RELEASED;
                xe_np_probe_state.stage_deadline_tick_ms =
                    now_ms + XE_NP_APP_PROBE_RESET_SETTLE_MS;
                XE_NP_App_RefreshProbeStatus(runtime);
                return;
            }
#endif

            xe_np_probe_state.active_probe_index = 0U;
            xe_np_probe_state.bringup_stage = XE_NP_APP_PROBE_STAGE_PING_PROBES;
            return;
        }

        case XE_NP_APP_PROBE_STAGE_PING_PROBES:
        {
            HAL_StatusTypeDef ping_status;

#if (XE_NP_APP_EXPERIMENT_SKIP_NRIC_CONFIG_ENABLE != 0U)
            xe_np_probe_state.i2c_ready = 1U;
            xe_np_probe_state.base_configured = 1U;
            xe_np_probe_state.active_probe_index = 0U;
            xe_np_probe_state.bringup_stage = XE_NP_APP_PROBE_STAGE_START_CAPTURE;
            XE_NP_App_RefreshProbeStatus(runtime);
            return;
#endif

            if(XE_NP_App_FindNextActiveProbe(
                   runtime->active_np_probe_mask,
                   xe_np_probe_state.active_probe_index,
                   &probe_index) != 0)
            {
                xe_np_probe_state.i2c_ready = 1U;
                xe_np_probe_state.active_probe_index = 0U;
                xe_np_probe_state.bringup_stage = XE_NP_APP_PROBE_STAGE_PROGRAM_PROBES;
                XE_NP_App_RefreshProbeStatus(runtime);
                return;
            }

            ping_status = XE_NP_Nric_Ping(runtime->hi2c_np[probe_index]);
#if (XE_NP_APP_BENCH_I2C_RECOVERY_ON_PING_FAIL != 0U)
            if(ping_status != HAL_OK)
            {
                if(XE_NP_App_RecoverProbeI2cBus(
                       runtime->hi2c_np[probe_index],
                       probe_index) == HAL_OK)
                {
                    ping_status = XE_NP_Nric_Ping(runtime->hi2c_np[probe_index]);
                }
            }
#endif

            if(ping_status != HAL_OK)
            {
                XE_NP_App_UpdateProbeDiag(
                    runtime,
                    runtime->hi2c_np[probe_index],
                    XE_NP_APP_PROBE_STAGE_PING_PROBES,
                    probe_index,
                    XE_NP_APP_PROBE_DIAG_FAILURE_PING_FAIL,
                    ping_status);
#if (XE_NP_APP_BENCH_I2C_SCAN_ON_PING_FAIL != 0U)
                for(uint8_t scan_probe = 0U; scan_probe < XE_NP_NEUROPIXELS_MAX_PROBES; ++scan_probe)
                {
#if (XE_NP_APP_BENCH_I2C_RECOVERY_ON_PING_FAIL != 0U)
                    (void)XE_NP_App_RecoverProbeI2cBus(
                        runtime->hi2c_np[scan_probe],
                        scan_probe);
#endif
                    XE_NP_App_RunProbeI2cScan(runtime->hi2c_np[scan_probe], scan_probe);
                }
#endif
                XE_NP_App_SetProbeStartupFailureState(
                    runtime,
                    xe_np_probe_state.i2c_ready,
                    xe_np_probe_state.base_configured);
                break;
            }

            xe_np_probe_state.active_probe_index = (uint8_t)(probe_index + 1U);
            return;
        }

        case XE_NP_APP_PROBE_STAGE_PROGRAM_PROBES:
        {
            if(XE_NP_App_FindNextActiveProbe(
                   runtime->active_np_probe_mask,
                   xe_np_probe_state.active_probe_index,
                   &probe_index) != 0)
            {
                /* No I2C/shift-register transaction runs at the 93.6 MHz MCLK.
                 * Switch only after every probe passed the strict readback. */
                if (XE_NP_Board_GetRuntimeMcoDivider() != (np_mode_hz>=20000U?2U:60000U/np_mode_hz))
                {
                    np_staged_clock_diag[0]++;
                    np_staged_clock_diag[3] = runtime->capture.flags;
                    if (XE_NP_Board_SetRuntimeMcoDivider(np_mode_hz>=20000U?2U:60000U/np_mode_hz) != 0)
                    {
                        np_staged_clock_diag[7] = 1U;
                        np_staged_clock_diag[1] = XE_NP_Board_GetRuntimeMcoDivider();
                        np_staged_clock_diag[2] = XE_NP_Board_GetRuntimeMcoLastApplyStatus();
                        np_staged_clock_diag[5] = RCC->CFGR1;
                        XE_NP_App_SetProbeStartupFailureState(runtime, 1U, 0U);
                        return;
                    }
                    /* HAL_RCC_MCOConfig reapplies PA8 GPIO defaults; restore
                     * the board-qualified drive and PF4 analog isolation. */
                    XE_NP_App_ApplySensitivePinSafety();
                    np_mco_hw_diag[0] = RCC->CFGR1;
                    np_mco_hw_diag[1] = RCC->PLL1DIVR;
                    np_mco_hw_diag[2] = RCC->PLL1CFGR;
                    np_mco_hw_diag[3] = GPIOA->OSPEEDR;
                    np_mco_hw_diag[4] = GPIOA->MODER;
                    np_mco_hw_diag[5] = RCC->CFGR2;
                    np_mco_hw_diag[6] = HAL_RCC_GetSysClockFreq();
                    np_mco_hw_diag[7] = HAL_RCC_GetPCLK2Freq();
                    /* Private HIL only: let the probe's internal clock tree
                     * settle before releasing record/reset at the new MCLK. */
                    HAL_Delay(50U);
                    xe_np_mclk_next_refresh_tick_ms = 0U;
                    if (XE_NP_App_ServiceNpMclkStatus(runtime) != 0)
                    {
                        np_staged_clock_diag[7] = 2U;
                        np_staged_clock_diag[1] = XE_NP_Board_GetRuntimeMcoDivider();
                        np_staged_clock_diag[2] = XE_NP_Board_GetRuntimeExpectedMcoHz();
                        np_staged_clock_diag[4] = runtime->flags;
                        np_staged_clock_diag[5] = RCC->CFGR1;
                        np_staged_clock_diag[6] = runtime->np_mclk_hz;
                        XE_NP_App_SetProbeStartupFailureState(runtime, 1U, 0U);
                        return;
                    }
                    XE_NP_App_ConfigureCapture(runtime);
                    np_staged_clock_diag[1] = XE_NP_Board_GetRuntimeMcoDivider();
                    np_staged_clock_diag[2] = XE_NP_Board_GetRuntimeExpectedMcoHz();
                    np_staged_clock_diag[4] = runtime->capture.flags;
                    np_staged_clock_diag[5] = runtime->flags;
                    np_staged_clock_diag[6] = runtime->np_mclk_hz;
                    np_staged_clock_diag[7] = 3U;
                }
                xe_np_probe_state.base_configured = 1U;
                xe_np_probe_state.active_probe_index = 0U;
                xe_np_probe_state.bringup_stage = XE_NP_APP_PROBE_STAGE_START_CAPTURE;
                XE_NP_App_RefreshProbeStatus(runtime);
                return;
            }

            if(XE_NP_AutoConfigure(probe_index,
                   runtime->hi2c_np[probe_index],
                   &xe_np_probe_config_store.probe_config[probe_index],
                   &xe_np_probe_state.last_status[probe_index]) != HAL_OK)
            {
                XE_NP_App_UpdateProbeDiag(
                    runtime,
                    runtime->hi2c_np[probe_index],
                    XE_NP_APP_PROBE_STAGE_PROGRAM_PROBES,
                    probe_index,
                    XE_NP_APP_PROBE_DIAG_FAILURE_APPLY_FAIL,
                    HAL_ERROR);
                XE_NP_App_SetProbeStartupFailureState(
                    runtime,
                    xe_np_probe_state.i2c_ready,
                    xe_np_probe_state.base_configured);
                break;
            }

            xe_np_probe_state.active_probe_index = (uint8_t)(probe_index + 1U);
            return;
        }

        case XE_NP_APP_PROBE_STAGE_START_CAPTURE:
        {
            if(XE_NP_App_NpRecordingStorageReady(runtime) == 0)
            {
                /*
                 * Keep the probe powered/configured with REC_NRESET asserted
                 * while waiting for a storage mode that can satisfy the
                 * no-loss raw Neuropixels recording contract. This permits
                 * bench validation of NRIC power/I2C/config on non-UHS cards
                 * without releasing the data stream into an undersized sink.
                 */
                xe_np_sd_next_refresh_tick_ms = 0U;
                XE_NP_App_RefreshStorageStatus(runtime);
                if(XE_NP_App_NpRecordingStorageReady(runtime) == 0)
                {
                    XE_NP_App_UpdateProbeDiag(
                        runtime,
                        NULL,
                        XE_NP_APP_PROBE_STAGE_START_CAPTURE,
                        0U,
                        XE_NP_APP_PROBE_DIAG_FAILURE_STORAGE_WAIT,
                        HAL_OK);
                    xe_np_probe_state.next_retry_tick_ms =
                        now_ms + XE_NP_APP_SD_UHS_RETRY_INTERVAL_MS;
                    XE_NP_App_RefreshProbeStatus(runtime);
                    return;
                }
            }

#if (XE_NP_APP_BENCH_PSB_CLOCK_PROBE_ENABLE != 0U)
            XE_NP_App_RunPsbClockProbe(runtime);
#endif

            int gate=NP_SD_BleArmGate(HAL_GetTick());
            if(!gate)return;
            if(gate<0){XE_NP_App_SetProbeStartupFailureState(runtime,xe_np_probe_state.i2c_ready,xe_np_probe_state.base_configured);return;}
            if(XE_NP_App_StartCaptureDma(runtime) != HAL_OK)
            {
                xe_np_dma_start_error_seen = 1U;
                XE_NP_App_UpdateProbeDiag(
                    runtime,
                    NULL,
                    XE_NP_APP_PROBE_STAGE_START_CAPTURE,
                    0U,
                    XE_NP_APP_PROBE_DIAG_FAILURE_DMA_START_FAIL,
                    HAL_ERROR);
                XE_NP_App_SetProbeStartupFailureState(
                    runtime,
                    xe_np_probe_state.i2c_ready,
                    xe_np_probe_state.base_configured);
                break;
            }
            XE_NP_UsbStream_RearmConsumer();

#if (XE_NP_APP_LEGACY_FS_ENABLE != 0U) && (XE_NP_APP_BENCH_CAPTURE_WITHOUT_STORAGE == 0U)
            /*
             * In the post-hoc raw-log build, the legacy CE64 recorder is the
             * primary data sink. Arm it before releasing REC_NRESET so the
             * first Neuropixels raw bytes do not outrun file/session setup.
             */
            CE32_SystemCompat_Service();
            XE_NP_LegacyFs_Service(runtime);
            CE32_File_FileStream_ServiceDeferredSignals();
            if(XE_NP_LegacyFs_IsActive() == 0)
            {
                XE_NP_App_UpdateProbeDiag(
                    runtime,
                    NULL,
                    XE_NP_APP_PROBE_STAGE_START_CAPTURE,
                    0U,
                    XE_NP_APP_PROBE_DIAG_FAILURE_LEGACY_FS_FAIL,
                    HAL_ERROR);
                XE_NP_App_SetProbeStartupFailureState(
                    runtime,
                    xe_np_probe_state.i2c_ready,
                    xe_np_probe_state.base_configured);
                break;
            }
#endif

            if((runtime->flags & XE_NP_RUNTIME_FLAG_NP_RECORD_RESET_RELEASED) == 0U)
            {
#if (XE_NP_APP_RAW_SD_SINK_ENABLE != 0U) && \
    (XE_NP_APP_RAW_SD_DEDICATED_CAPTURE_ENABLE != 0U)
                /* DMA is already armed, but the probe still holds its data
                 * output in reset. Arm the raw consumer in this gap so the
                 * first valid DDR block has an owner. */
                if((XE_NP_App_IsRawSdSinkActive() == 0) &&
                   (XE_NP_App_StartRawSdSink() != 0))
                {
                    XE_NP_App_SetProbeStartupFailureState(
                        runtime,
                        xe_np_probe_state.i2c_ready,
                        xe_np_probe_state.base_configured);
                    break;
                }
#endif
                NP_ModeReleaseReset(NP_REC_NRST_GPIO_Port, NP_REC_NRST_Pin);
                NP_SD_BleArmStamp(HAL_GetTick());
                runtime->flags |= XE_NP_RUNTIME_FLAG_NP_RECORD_RESET_RELEASED;
            }
            xe_np_probe_state.stage_deadline_tick_ms = now_ms + XE_NP_APP_PROBE_RESET_SETTLE_MS;
            xe_np_probe_state.bringup_stage = XE_NP_APP_PROBE_STAGE_WAIT_RECORD_RESET;
            return;
        }

        case XE_NP_APP_PROBE_STAGE_WAIT_RECORD_RESET:
        {
            if(XE_NP_App_TimeReached(now_ms, xe_np_probe_state.stage_deadline_tick_ms) == 0)
            {
                return;
            }

            if(XE_NP_App_CapturePathFaulted(runtime) != 0)
            {
                XE_NP_App_LatchCaptureBenchDiag(
                    runtime,
                    XE_NP_APP_CAPTURE_BENCH_DIAG_REASON_PATH_FAULT);
                XE_NP_App_UpdateProbeDiag(
                    runtime,
                    NULL,
                    XE_NP_APP_PROBE_STAGE_WAIT_RECORD_RESET,
                    0U,
                    XE_NP_APP_PROBE_DIAG_FAILURE_CAPTURE_PATH_FAULT,
                    HAL_ERROR);
                if(XE_NP_App_AdvanceGpioeCaptureTimingCandidate(runtime, now_ms) != 0)
                {
                    return;
                }
                XE_NP_App_SetProbeStartupFailureState(
                    runtime,
                    xe_np_probe_state.i2c_ready,
                    xe_np_probe_state.base_configured);
                break;
            }

            if(XE_NP_App_VerifyActiveProbeRunStatus(runtime) == 0)
            {
                XE_NP_App_UpdateProbeDiag(
                    runtime,
                    NULL,
                    XE_NP_APP_PROBE_STAGE_WAIT_RECORD_RESET,
                    0U,
                    XE_NP_APP_PROBE_DIAG_FAILURE_VERIFY_RUN_FAIL,
                    HAL_ERROR);
                XE_NP_App_SetProbeStartupFailureState(
                    runtime,
                    xe_np_probe_state.i2c_ready,
                    xe_np_probe_state.base_configured);
                break;
            }

            XE_NP_App_PreparePortForPostResetLock(&xe_np_dma_gpioe);
            XE_NP_App_PreparePortForPostResetLock(&xe_np_dma_gpiod);
            xe_np_probe_state.stage_deadline_tick_ms =
                now_ms + XE_NP_APP_CAPTURE_SYNC_LOCK_TIMEOUT_MS;
            xe_np_probe_state.bringup_stage = XE_NP_APP_PROBE_STAGE_WAIT_CAPTURE_LOCK;
            XE_NP_App_RefreshProbeStatus(runtime);
            return;
        }

        case XE_NP_APP_PROBE_STAGE_WAIT_CAPTURE_LOCK:
        {
            if(XE_NP_App_CapturePathFaulted(runtime) != 0)
            {
                XE_NP_App_LatchCaptureBenchDiag(
                    runtime,
                    XE_NP_APP_CAPTURE_BENCH_DIAG_REASON_PATH_FAULT);
                XE_NP_App_UpdateProbeDiag(
                    runtime,
                    NULL,
                    XE_NP_APP_PROBE_STAGE_WAIT_CAPTURE_LOCK,
                    0U,
                    XE_NP_APP_PROBE_DIAG_FAILURE_CAPTURE_PATH_FAULT,
                    HAL_ERROR);
                if(XE_NP_App_AdvanceGpioeCaptureTimingCandidate(runtime, now_ms) != 0)
                {
                    return;
                }
                XE_NP_App_SetProbeStartupFailureState(
                    runtime,
                    xe_np_probe_state.i2c_ready,
                    xe_np_probe_state.base_configured);
                break;
            }

            if((XE_NP_App_AllActiveCapturePortsLocked(runtime) == 0) &&
               (XE_NP_APP_EXPERIMENT_ALLOW_UNQUALIFIED_SD_CAPTURE_LOCK == 0U))
            {
                if(XE_NP_App_UsbCaptureStorageBypassActive(runtime) != 0)
                {
                    /*
                     * USB diagnostic capture is allowed to stream raw lane
                     * bytes even when the NRIC output is not yet a valid
                     * Neuropixels frame stream. This keeps the probe-output
                     * investigation observable without weakening SD recording.
                     */
                }
                else
                {
                    if(XE_NP_App_TimeReached(now_ms, xe_np_probe_state.stage_deadline_tick_ms) == 0)
                    {
                        return;
                    }

                    XE_NP_App_LatchCaptureBenchDiag(
                        runtime,
                        XE_NP_APP_CAPTURE_BENCH_DIAG_REASON_LOCK_TIMEOUT);
                    XE_NP_App_UpdateProbeDiag(
                        runtime,
                        NULL,
                        XE_NP_APP_PROBE_STAGE_WAIT_CAPTURE_LOCK,
                        0U,
                        XE_NP_APP_PROBE_DIAG_FAILURE_CAPTURE_LOCK_TIMEOUT,
                        HAL_ERROR);
                    if(XE_NP_App_AdvanceGpioeCaptureTimingCandidate(runtime, now_ms) != 0)
                    {
                        return;
                    }
                    XE_NP_App_SetProbeStartupFailureState(
                        runtime,
                        xe_np_probe_state.i2c_ready,
                        xe_np_probe_state.base_configured);
                    break;
                }
            }

#if (XE_NP_APP_GPIOE_CAPTURE_USE_PA0_TIM2_CH1 != 0U) && \
    (XE_NP_APP_GPIOE_CAPTURE_TIM2_START_DELAY_TICKS != 0U)
            if((xe_np_capture_start_phase_aligned == 0U) &&
               ((runtime->capture.flags & XE_NP_CAPTURE_FLAG_GPIOE_ACTIVE) != 0U))
            {
                xe_np_capture_start_phase_aligned = 1U;
                xe_np_capture_start_phase_delay_armed = 1U;
                if(XE_NP_App_StartCaptureDma(runtime) != HAL_OK)
                {
                    xe_np_capture_start_phase_delay_armed = 0U;
                    xe_np_dma_start_error_seen = 1U;
                    XE_NP_App_UpdateProbeDiag(
                        runtime,
                        NULL,
                        XE_NP_APP_PROBE_STAGE_WAIT_CAPTURE_LOCK,
                        0U,
                        XE_NP_APP_PROBE_DIAG_FAILURE_DMA_START_FAIL,
                        HAL_ERROR);
                    XE_NP_App_SetProbeStartupFailureState(
                        runtime,
                        xe_np_probe_state.i2c_ready,
                        xe_np_probe_state.base_configured);
                    break;
                }
                XE_NP_App_PreparePortForPostResetLock(&xe_np_dma_gpioe);
                XE_NP_App_PreparePortForPostResetLock(&xe_np_dma_gpiod);
                xe_np_probe_state.stage_deadline_tick_ms =
                    now_ms + XE_NP_APP_CAPTURE_SYNC_LOCK_TIMEOUT_MS;
                XE_NP_App_RefreshProbeStatus(runtime);
                return;
            }
#endif

            #include "np_capture_admit.inc"
            xe_np_capture_validation_enabled =
#if (XE_NP_APP_RAW_SD_DEDICATED_CAPTURE_ENABLE != 0U)
                0U;
#else
                (XE_NP_APP_CAPTURE_VALIDATION_ENABLE != 0U) ? 1U : 0U;
#endif
            XE_NP_App_ResetPortContinuityState(&xe_np_dma_gpioe);
            XE_NP_App_ResetPortContinuityState(&xe_np_dma_gpiod);
            xe_np_probe_state.initialized = 1U;
            xe_np_probe_state.init_failed = 0U;
            xe_np_probe_state.init_error_reported = 0U;
            xe_np_probe_state.next_retry_tick_ms = 0U;
            xe_np_probe_state.i2c_ready = 1U;
            xe_np_probe_state.base_configured = 1U;
            xe_np_probe_state.shank_defaults_active = 0U;
            xe_np_probe_next_verify_tick_ms = now_ms + XE_NP_APP_PROBE_RUNTIME_VERIFY_INTERVAL_MS;
            xe_np_probe_state.bringup_stage = XE_NP_APP_PROBE_STAGE_IDLE;
            xe_np_probe_state.active_probe_index = 0U;
            xe_np_probe_state.stage_deadline_tick_ms = 0U;
#if (XE_NP_APP_RAW_SD_SINK_ENABLE != 0U) && \
    ((XE_NP_APP_BENCH_CAPTURE_WITHOUT_STORAGE == 0U) || \
     (XE_NP_APP_EXPERIMENT_SD_STRESS_WRITER_ENABLE != 0U))
            if((XE_NP_App_UsbCaptureStorageBypassActive(runtime) == 0) &&
               (XE_NP_App_IsRawSdSinkActive() == 0) &&
               (XE_NP_App_StartRawSdSink() != 0))
            {
                CE32_ReportError(&runtime->sys_param, CE32_ERRORID_SD_ERR);
                XE_NP_App_UpdateProbeDiag(
                    runtime,
                    NULL,
                    XE_NP_APP_PROBE_STAGE_WAIT_CAPTURE_LOCK,
                    0U,
                    XE_NP_APP_PROBE_DIAG_FAILURE_RAW_SD_SINK_FAIL,
                    HAL_ERROR);
            }
#endif
            XE_NP_App_UpdateProbeDiag(
                runtime,
                NULL,
                XE_NP_APP_PROBE_STAGE_IDLE,
                0U,
                XE_NP_APP_PROBE_DIAG_FAILURE_NONE,
                HAL_OK);
            XE_NP_App_RefreshProbeStatus(runtime);
            XE_NP_App_RefreshDmaStatus();
            return;
        }

        default:
        {
            xe_np_probe_state.bringup_stage = XE_NP_APP_PROBE_STAGE_IDLE;
            xe_np_probe_state.active_probe_index = 0U;
            xe_np_probe_state.stage_deadline_tick_ms = 0U;
            return;
        }
    }

    if(xe_np_probe_state.init_error_reported == 0U)
    {
        CE32_ReportError(&runtime->sys_param, CE32_ERRORID_CORE_ERROR);
        xe_np_probe_state.init_error_reported = 1U;
    }
#if (XE_NP_APP_BENCH_HOLD_PROBE_ON_FAILURE != 0U)
    xe_np_probe_state.next_retry_tick_ms = 0U;
#else
    xe_np_probe_state.next_retry_tick_ms = now_ms + XE_NP_APP_PROBE_RETRY_DELAY_MS;
#endif
    XE_NP_App_RefreshProbeStatus(runtime);
}

static void XE_NP_App_ApplySafeNpStartup(XE_NP_Runtime *runtime)
{
    if(runtime == NULL)
    {
        return;
    }

    /*
     * Safe reset state before the probe-init state machine runs. NRST and
     * REC_NRESET are only released after MCLK validation and successful NRIC
     * configuration on every active probe.
     */
    XE_NP_App_StopRawSdSink();
    XE_NP_LegacyFs_Abort();
    /*
     * CAL_SIGNAL is tied to the probe TEST input in the direct-bond design.
     * Safe reset must return that shared analog control line to 0 V so a prior
     * calibration/test request cannot leak into the next bring-up attempt.
     */
    (void)XE_NP_App_SetProbeCalDacRawInternal(runtime, 0U);
    memset(&xe_np_probe_state, 0, sizeof(xe_np_probe_state));
    xe_np_capture_validation_enabled = 0U;
    XE_NP_App_StopCaptureDma();
    HAL_GPIO_WritePin(NP_NRST_GPIO_Port, NP_NRST_Pin, GPIO_PIN_RESET);
    HAL_GPIO_WritePin(NP_REC_NRST_GPIO_Port, NP_REC_NRST_Pin, GPIO_PIN_RESET);
    NP_StartupParkInputs();
    XE_NP_App_SetProbePowerRails(runtime, 0U);
    xe_np_probe_next_verify_tick_ms = 0U;
    runtime->flags &= ~(XE_NP_RUNTIME_FLAG_NP_GLOBAL_RESET_RELEASED |
                        XE_NP_RUNTIME_FLAG_NP_RECORD_RESET_RELEASED);
    XE_NP_App_RefreshProbeStatus(runtime);
    XE_NP_App_RefreshDmaStatus();
}

static void XE_NP_App_HandleRuntimeMclkFault(XE_NP_Runtime *runtime)
{
    uint32_t active_flags;

    if((runtime == NULL) || (XE_NP_Runtime_HasValidNpMclk(runtime) != 0))
    {
        return;
    }

    active_flags = XE_NP_RUNTIME_FLAG_NP_GLOBAL_RESET_RELEASED |
                   XE_NP_RUNTIME_FLAG_NP_RECORD_RESET_RELEASED |
                   XE_NP_RUNTIME_FLAG_NP_CAPTURE_DMA_ACTIVE;
    if((xe_np_probe_state.initialized == 0U) && ((runtime->flags & active_flags) == 0U))
    {
        return;
    }

    XE_NP_App_UpdateProbeDiag(
        runtime,
        NULL,
        xe_np_probe_state.bringup_stage,
        xe_np_probe_state.active_probe_index,
        XE_NP_APP_PROBE_DIAG_FAILURE_RUNTIME_MCLK_FAULT,
        HAL_ERROR);
    xe_np_capture_shutdown_reason = 2U; /* MCLK supervision */
    XE_NP_App_ApplySafeNpStartup(runtime);
    xe_np_probe_state.next_retry_tick_ms = HAL_GetTick() + XE_NP_APP_PROBE_RETRY_DELAY_MS;
}

static void XE_NP_App_HandleCaptureRuntimeFault(XE_NP_Runtime *runtime)
{
    uint32_t now_ms;

    if((runtime == NULL) || (xe_np_probe_state.initialized == 0U))
    {
        return;
    }

    if(XE_NP_App_CapturePathFaulted(runtime) == 0)
    {
        return;
    }

    XE_NP_App_LatchCaptureBenchDiag(
        runtime,
        XE_NP_APP_CAPTURE_BENCH_DIAG_REASON_RUNTIME_CAPTURE_FAULT);
    XE_NP_App_UpdateProbeDiag(
        runtime,
        NULL,
        xe_np_probe_state.bringup_stage,
        xe_np_probe_state.active_probe_index,
        XE_NP_APP_PROBE_DIAG_FAILURE_RUNTIME_CAPTURE_FAULT,
        HAL_ERROR);
    now_ms = HAL_GetTick();
    /*
     * In USB raw-bypass capture mode, keep the probe session alive after a
     * runtime capture fault so the host can still observe the lane stream and
     * diagnose timing/validation issues without the firmware resetting the
     * capture pipeline out from under it.
     */
    if(XE_NP_App_UsbCaptureStorageBypassActive(runtime) != 0)
    {
        return;
    }
#if (XE_NP_APP_BENCH_CAPTURE_WITHOUT_STORAGE != 0U)
    if(XE_NP_App_AdvanceGpioeCaptureTimingCandidate(runtime, now_ms) != 0)
    {
        return;
    }
    return;
#endif
    xe_np_capture_shutdown_reason = 3U; /* capture/DMA/validation path */
    XE_NP_App_ApplySafeNpStartup(runtime);
    xe_np_probe_state.next_retry_tick_ms = HAL_GetTick() + XE_NP_APP_PROBE_RETRY_DELAY_MS;
}

static void XE_NP_App_HandleProbeRuntimeControlFault(XE_NP_Runtime *runtime)
{
    uint32_t now_ms;

    if((runtime == NULL) || (xe_np_probe_state.initialized == 0U))
    {
        return;
    }

    /*
     * Do not inject periodic NRIC I2C reads into an active capture session.
     * The live data path should be passive once recording is running; debug
     * register verification can resume after capture stops.
     */
    if((runtime->flags & XE_NP_RUNTIME_FLAG_NP_CAPTURE_DMA_ACTIVE) != 0U)
    {
        return;
    }

    now_ms = HAL_GetTick();
    if((xe_np_probe_next_verify_tick_ms != 0U) &&
       (XE_NP_App_TimeReached(now_ms, xe_np_probe_next_verify_tick_ms) == 0))
    {
        return;
    }

    xe_np_probe_next_verify_tick_ms = now_ms + XE_NP_APP_PROBE_RUNTIME_VERIFY_INTERVAL_MS;
    if(XE_NP_App_VerifyActiveProbeRunStatus(runtime) != 0)
    {
        return;
    }

    CE32_ReportError(&runtime->sys_param, CE32_ERRORID_CORE_ERROR);
    XE_NP_App_UpdateProbeDiag(
        runtime,
        NULL,
        xe_np_probe_state.bringup_stage,
        xe_np_probe_state.active_probe_index,
        XE_NP_APP_PROBE_DIAG_FAILURE_RUNTIME_VERIFY_FAULT,
        HAL_ERROR);
#if (XE_NP_APP_BENCH_CAPTURE_WITHOUT_STORAGE != 0U)
    return;
#endif
    xe_np_capture_shutdown_reason = 4U; /* post-start probe verification */
    XE_NP_App_ApplySafeNpStartup(runtime);
    xe_np_probe_state.next_retry_tick_ms = HAL_GetTick() + XE_NP_APP_PROBE_RETRY_DELAY_MS;
}

static void XE_NP_App_HandleCaptureOverrun(XE_NP_Runtime *runtime)
{
    if((runtime == NULL) || (XE_NP_App_CaptureOverrunLatched(runtime) == 0))
    {
        return;
    }
    if((XE_NP_App_UsbCaptureStorageBypassActive(runtime) != 0) ||
#if (XE_NP_APP_BENCH_CAPTURE_WITHOUT_STORAGE != 0U)
       1
#else
       0
#endif
       )
    {
        return;
    }

    /*
     * A raw-ring overrun means the no-loss recording contract has already been
     * violated. Treat it as a session fault, not just a sink fault, so the
     * next logging attempt starts from a clean probe/DMA state.
     */
    CE32_ReportError(&runtime->sys_param, CE32_ERRORID_CORE_ERROR);
    XE_NP_App_RecordRawSdDiag(
        XE_NP_APP_RAW_SD_DIAG_REASON_CAPTURE_OVERRUN,
        runtime,
        &xe_np_raw_sd_sink,
        NULL,
        0xFFU,
        HAL_ERROR);
    xe_np_capture_shutdown_reason = 5U; /* recorder ring overrun */
    XE_NP_App_ApplySafeNpStartup(runtime);
    xe_np_probe_state.next_retry_tick_ms = HAL_GetTick() + XE_NP_APP_PROBE_RETRY_DELAY_MS;
}

#if (XE_NP_APP_LEGACY_FS_ENABLE != 0U)
static void XE_NP_App_HandleLegacyFsFault(XE_NP_Runtime *runtime)
{
    uint16_t legacy_error;

    if((runtime == NULL) || (XE_NP_App_LegacyFsPathFaulted(runtime) == 0))
    {
        return;
    }

    legacy_error = CE32_SystemCompat_GetLegacyFsError();
    if(legacy_error == 0U)
    {
        /*
         * A running NP session without an active legacy recorder is invalid in
         * the current post-hoc raw-log build. Surface it as an SD path error
         * even if the filesystem state machine did not emit a specific code.
         */
        CE32_ReportError(&runtime->sys_param, CE32_ERRORID_SD_ERR);
    }

    CE32_SystemCompat_ClearLegacyFsError();
    xe_np_capture_shutdown_reason = 7U; /* legacy filesystem failure */
    XE_NP_App_ApplySafeNpStartup(runtime);
    xe_np_probe_state.next_retry_tick_ms = HAL_GetTick() + XE_NP_APP_PROBE_RETRY_DELAY_MS;
}
#endif

#if (XE_NP_APP_RAW_SD_SINK_ENABLE != 0U)
static void XE_NP_App_HandleRawSdSinkFault(XE_NP_Runtime *runtime)
{
    if((runtime == NULL) || (XE_NP_App_RawSdSinkPathFaulted(runtime) == 0))
    {
        return;
    }

    CE32_ReportError(&runtime->sys_param, CE32_ERRORID_SD_ERR);
    XE_NP_App_UpdateProbeDiag(
        runtime,
        NULL,
        xe_np_probe_state.bringup_stage,
        xe_np_probe_state.active_probe_index,
        XE_NP_APP_PROBE_DIAG_FAILURE_RAW_SD_SINK_FAIL,
        HAL_ERROR);
    xe_np_capture_shutdown_reason = 6U; /* raw SD sink failure */
    XE_NP_App_ApplySafeNpStartup(runtime);
    xe_np_probe_state.next_retry_tick_ms = HAL_GetTick() + XE_NP_APP_PROBE_RETRY_DELAY_MS;
}
#endif

void XE_NP_App_Init(void)
{
#if (XE_NP_APP_QUAD_DMA_NO_PROBE_DRYRUN != 0U)
    xe_np_quad_dryrun_diag[0] = 0x51445231UL;
    xe_np_quad_dryrun_diag[1] = 1U;
    XE_NP_Runtime_Init(&xe_np_runtime);
    XE_NP_App_BindGeneratedPeripherals(&xe_np_runtime);
    XE_NP_App_ApplySensitivePinSafety();
    XE_NP_App_ApplySafeNpStartup(&xe_np_runtime);
    xe_np_runtime.active_np_probe_mask = XE_NP_CAPTURE_PROBE2_MASK;
    XE_NP_App_ConfigureCapture(&xe_np_runtime);
    memset(xe_np_runtime.capture.raw_storage, 0xa5, xe_np_runtime.capture.raw_storage_bytes);
    xe_np_quad_dryrun_diag[3] = HAL_RCC_GetHCLKFreq();
    xe_np_quad_dryrun_diag[4] = HAL_GetTick();
    xe_np_quad_dryrun_diag[5] = (GPIOE->IDR >> 8U) & 255U;
    xe_np_quad_dryrun_diag[2] = XE_NP_App_StartCaptureDma(&xe_np_runtime);
    xe_np_quad_dryrun_diag[1] = (xe_np_quad_dryrun_diag[2] == HAL_OK) ? 2U : 4U;
    return; /* Never try NP configuration, power-up or recording services. */
#endif
    xe_np_probe_next_verify_tick_ms = 0U;
    XE_NP_App_ApplyGpioeCaptureTimingCandidate(np_mode_hz>=20000U?0U:1U);
    XE_NP_App_EnsureProbeConfigsInitialized();
    XE_NP_GeneratedProbeConfigs_Bind();
    XE_NP_Runtime_Init(&xe_np_runtime);
    XE_NP_App_BindGeneratedPeripherals(&xe_np_runtime);
    XE_NP_App_InitLegacyBleTransport(&xe_np_runtime);
#if !NP_SD_APP
    XE_NP_App_InitLegacyImu(&xe_np_runtime);
    XE_NP_App_InitLegacyAdc(&xe_np_runtime);
#endif
    XE_NP_App_SetWakeLed(1U);
    XE_NP_App_ApplySensitivePinSafety();
    XE_NP_App_InitializeProbeCalLine(&xe_np_runtime);
    XE_NP_App_UpdateCapabilityFlags(&xe_np_runtime);
    XE_NP_App_RefreshStorageStatus(&xe_np_runtime);
    (void)XE_NP_App_ServiceNpMclkStatus(&xe_np_runtime);
    XE_NP_App_RunCaptureSelfTest(&xe_np_runtime);
    XE_NP_App_ConfigureCapture(&xe_np_runtime);
    XE_NP_App_ApplySafeNpStartup(&xe_np_runtime);
#if (XE_NP_APP_LEGACY_FS_ENABLE != 0U)
    XE_NP_LegacyFs_Init(&xe_np_runtime);
#endif
#if (XE_NP_BOARD_USB_SYNTHETIC_CLOCK_BENCH_ISOLATION == 0U)
#if (XE_NP_APP_EXPERIMENT_HOST_TRIGGERED_BRINGUP_ONLY == 0U)
    XE_NP_App_ServiceProbeBringUp(&xe_np_runtime);
#endif
#endif
    XE_NP_App_UpdateStreamDiag(&xe_np_runtime);
}

#if (XE_NP_APP_QUAD_DMA_CPU_GUARD != 0U)
#if (XE_NP_APP_EXPERIMENT_CAPTURE_SRAM1_ENABLE != 0U)
#error "CPU guard requires the capture bank in SRAM3."
#endif
/* This is an allowlist, NOT an AP=no-access region (Armv8-M has no such AP).
 * In this private quiet interval all CPU state/stack is SRAM1, the descriptors
 * may use SRAM2, and code/vectors are internal flash. No SRAM3 or aliases are
 * mapped, and PRIVDEFENA is clear. The MPU does not constrain DMA masters.
 * Save/restore every region: never leave the temporary layout in a recorder. */
static struct {
    uint32_t ctrl, rnr, mair0, mair1, shcsr, count;
    uint32_t rbar[16], rlar[16];
} xe_np_quad_cpu_mpu_saved;
/* QCG1: magic,state(1=active,2=restored,3=fault,4=rejected,5=negative escaped),
 * CFSR,HFSR,MMFAR,guard CTRL,region count,intentional address,escaped value. */
static volatile uint32_t xe_np_quad_cpu_guard_diag[9];

static uint32_t XE_NP_App_QuadCpuGuardEnter(void)
{
    const uint32_t count = (MPU->TYPE & MPU_TYPE_DREGION_Msk) >> MPU_TYPE_DREGION_Pos;
    const uint32_t sp = __get_MSP();
    xe_np_quad_cpu_guard_diag[0] = 0x51434731UL;
    xe_np_quad_cpu_guard_diag[1] = 4U;
    xe_np_quad_cpu_guard_diag[6] = count;
    if(count < 4U || count > 16U || (__get_CONTROL() & 3U) != 0U ||
       sp < 0x20000000UL || sp >= 0x200c0000UL ||
       SCB->VTOR < 0x08000000UL || SCB->VTOR >= 0x08200000UL)
    { return 0U; }
    xe_np_quad_cpu_mpu_saved.ctrl = MPU->CTRL;
    xe_np_quad_cpu_mpu_saved.rnr = MPU->RNR;
    xe_np_quad_cpu_mpu_saved.mair0 = MPU->MAIR0;
    xe_np_quad_cpu_mpu_saved.mair1 = MPU->MAIR1;
    xe_np_quad_cpu_mpu_saved.shcsr = SCB->SHCSR;
    xe_np_quad_cpu_mpu_saved.count = count;
    for(uint32_t i = 0U; i < count; ++i)
    {
        MPU->RNR = i;
        xe_np_quad_cpu_mpu_saved.rbar[i] = MPU->RBAR;
        xe_np_quad_cpu_mpu_saved.rlar[i] = MPU->RLAR;
    }
    __DMB(); MPU->CTRL = 0U; __DSB(); __ISB();
    for(uint32_t i = 0U; i < count; ++i) { MPU->RNR = i; MPU->RLAR = 0U; }
    /* Attr0: normal WB RA/WA. Attr1: device nGnRnE. Non-overlapping ranges.
     * SRAM3 begins at 0x200d0000; the SRAM allowlist ends one byte before it. */
    MPU->MAIR0 = 0x000000ffUL;
    MPU->MAIR1 = 0U;
    MPU->RNR = 0U; MPU->RBAR = 0x08000000UL | (2U << MPU_RBAR_AP_Pos);
    MPU->RLAR = 0x081fffe0UL | MPU_RLAR_EN_Msk;
    MPU->RNR = 1U; MPU->RBAR = 0x20000000UL | MPU_RBAR_XN_Msk;
    MPU->RLAR = 0x200cffe0UL | MPU_RLAR_EN_Msk;
    MPU->RNR = 2U; MPU->RBAR = 0x40000000UL | MPU_RBAR_XN_Msk;
    MPU->RLAR = 0x5fffffe0UL | (1U << MPU_RLAR_AttrIndx_Pos) | MPU_RLAR_EN_Msk;
    MPU->RNR = 3U; MPU->RBAR = 0xe0000000UL | MPU_RBAR_XN_Msk;
    MPU->RLAR = 0xe00fffe0UL | (1U << MPU_RLAR_AttrIndx_Pos) | MPU_RLAR_EN_Msk;
    SCB->SHCSR |= SCB_SHCSR_MEMFAULTENA_Msk;
    /* Keep protection in fault/NMI handlers too. The diagnostic stop routine
     * and all of its data are within the same allowlist. */
    MPU->CTRL = MPU_CTRL_ENABLE_Msk | MPU_CTRL_HFNMIENA_Msk;
    __DSB(); __ISB();
    xe_np_quad_cpu_guard_diag[5] = MPU->CTRL;
    xe_np_quad_cpu_guard_diag[1] = 1U;
    return 1U;
}

static void XE_NP_App_QuadCpuGuardLeave(void)
{
    __DMB(); MPU->CTRL = 0U; __DSB(); __ISB();
    for(uint32_t i = 0U; i < xe_np_quad_cpu_mpu_saved.count; ++i)
    {
        MPU->RNR = i;
        MPU->RBAR = xe_np_quad_cpu_mpu_saved.rbar[i];
        MPU->RLAR = xe_np_quad_cpu_mpu_saved.rlar[i];
    }
    MPU->MAIR0 = xe_np_quad_cpu_mpu_saved.mair0;
    MPU->MAIR1 = xe_np_quad_cpu_mpu_saved.mair1;
    MPU->RNR = xe_np_quad_cpu_mpu_saved.rnr;
    SCB->SHCSR = xe_np_quad_cpu_mpu_saved.shcsr;
    MPU->CTRL = xe_np_quad_cpu_mpu_saved.ctrl;
    __DSB(); __ISB();
    if(xe_np_quad_cpu_guard_diag[1] == 1U) { xe_np_quad_cpu_guard_diag[1] = 2U; }
}

void XE_NP_App_QuadCpuGuardFault(void)
{
    __disable_irq();
    xe_np_quad_cpu_guard_diag[2] = SCB->CFSR;
    xe_np_quad_cpu_guard_diag[3] = SCB->HFSR;
    xe_np_quad_cpu_guard_diag[4] = SCB->MMFAR;
    if(xe_np_quad_dma_active) { XE_NP_App_QuiesceGpioeQuadDmaInterleaveForSnapshot(); }
    xe_np_quad_dma_freeze_diag[1] = 5U;
    xe_np_capture_freeze_done = 1U;
    xe_np_quad_cpu_guard_diag[1] = 3U;
    /* Never return to a faulting instruction or silently resume recording. */
    for(;;) { __DSB(); __WFI(); }
}
#endif

#if (XE_NP_APP_QUAD_DMA_QUIET_ENABLE != 0U)
#if ((XE_NP_APP_EXPERIMENT_FREEZE_QUIESCE_ONLY == 0U) || \
     (XE_NP_APP_EXPERIMENT_FREEZE_CAPTURE_AFTER_MS == 0U))
#error "Quiet mode is finite frozen-capture only."
#endif
static void XE_NP_App_QuadQuietCapture(void)
{
    DMA_Channel_TypeDef *const channels[] = {
        GPDMA1_Channel0, GPDMA1_Channel1, GPDMA1_Channel2, GPDMA1_Channel3,
        GPDMA1_Channel4, GPDMA1_Channel5, GPDMA1_Channel6, GPDMA1_Channel7,
        GPDMA1_Channel8, GPDMA1_Channel9, GPDMA1_Channel10, GPDMA1_Channel11,
        GPDMA1_Channel12, GPDMA1_Channel13, GPDMA1_Channel14, GPDMA1_Channel15,
        LPDMA1_Channel0, LPDMA1_Channel1, LPDMA1_Channel2, LPDMA1_Channel3};
    uint32_t enabled[8];
    const uint32_t banks = (SCnSCB->ICTR & 15U) + 1U;
    uint32_t rejected = 0U;
    uint32_t masked = 0U;
#if (XE_NP_APP_QUAD_DMA_CPU_GUARD != 0U)
    uint32_t guarded = 0U;
#endif
    if(banks > 8U || __get_PRIMASK() || __get_BASEPRI() ||
       (SCB->SCR & (SCB_SCR_SLEEPDEEP_Msk | SCB_SCR_SLEEPONEXIT_Msk)) ||
       ((SysTick->CTRL & (SysTick_CTRL_ENABLE_Msk | SysTick_CTRL_TICKINT_Msk)) !=
        (SysTick_CTRL_ENABLE_Msk | SysTick_CTRL_TICKINT_Msk))) { rejected |= 8U; }
    if(!rejected)
    {
        for(uint32_t i = 0U; i < banks; ++i)
        { enabled[i] = NVIC->ISER[i]; NVIC->ICER[i] = 0xffffffffUL; }
        __DSB(); __ISB();
        masked = 1U;
    }
    /* Refuse a false isolation result. Do not abort someone else's DMA. */
    for(uint32_t i = 0U; i < sizeof(channels)/sizeof(channels[0]); ++i)
    {
        uint32_t capture = 0U;
        for(uint32_t lane = 0U; lane < 4U; ++lane)
        { capture |= (channels[i] == xe_np_quad_dma_lanes[lane].hdma.Instance); }
        if(!capture && (channels[i]->CCR & DMA_CCR_EN)) { rejected |= 1U; }
    }
    if(USB_OTG_HS->GAHBCFG & USB_OTG_GAHBCFG_DMAEN) { rejected |= 2U; }
    if((SDMMC1->IDMACTRL | SDMMC2->IDMACTRL) & SDMMC_IDMA_IDMAEN) { rejected |= 4U; }
#if (XE_NP_APP_QUAD_DMA_CPU_GUARD != 0U)
    if(!rejected)
    {
        guarded = XE_NP_App_QuadCpuGuardEnter();
        if(!guarded) { rejected |= 16U; }
    }
#if (XE_NP_APP_QUAD_DMA_CPU_GUARD_NEGATIVE != 0U)
    if(guarded)
    {
        xe_np_quad_cpu_guard_diag[7] = 0x200d0000UL;
        __DSB();
        xe_np_quad_cpu_guard_diag[8] = *(volatile uint8_t *)0x200d0000UL;
        xe_np_quad_cpu_guard_diag[1] = 5U; /* MUST fault, never qualify this image. */
        rejected |= 32U;
    }
#endif
#endif
    xe_np_quad_dma_quiet_diag[0] = rejected;
    xe_np_quad_dma_quiet_diag[1] = HAL_GetTick();
    xe_np_quad_dma_quiet_diag[3] = 0U;
    if(!rejected)
    {
        /* No ring reads, cache maintenance, USB service, IMU, BLE or DMA
         * register polling. SysTick alone wakes the CPU once per millisecond.
         * This is a contention A/B control, not a transport-enabled recorder. */
        while((int32_t)(HAL_GetTick() - xe_np_capture_freeze_deadline_tick_ms) < 0)
        { __DSB(); __WFI(); ++xe_np_quad_dma_quiet_diag[3]; }
    }
    XE_NP_App_QuiesceGpioeQuadDmaInterleaveForSnapshot();
    xe_np_capture_freeze_done = 1U;
    xe_np_quad_dma_quiet_diag[2] = HAL_GetTick();
    if(rejected) { xe_np_quad_dma_freeze_diag[1] = 4U; }
#if (XE_NP_APP_QUAD_DMA_CPU_GUARD != 0U)
    if(guarded) { XE_NP_App_QuadCpuGuardLeave(); }
#endif
    if(masked)
    {
        for(uint32_t i = 0U; i < banks; ++i) { NVIC->ISER[i] = enabled[i]; }
        __DSB(); __ISB();
    }
}
#endif

void XE_NP_App_Service(void)
{
#if NP_SD_APP
    XE_NP_App_SdLoggerService();
    return;
#endif
    XE_NP_AppDmaPort *ports[2] = {&xe_np_dma_gpioe, &xe_np_dma_gpiod};
    uint8_t start_port_index = (uint8_t)(xe_np_dma_service_next_port & 0x1U);
    uint8_t first_budget;
    uint8_t second_budget;
    uint8_t usb_streaming;

#if (XE_NP_APP_QUAD_USB_WORD_EXPORT != 0U)
    /* Dedicated USB qualifier: the periodic MCLK measurement and unrelated
     * acquisition services can block beyond this 7.88-ms ring. Bring-up runs
     * normally, but once initialized, only the USB producer services capture.
     * STOP still calls XE_NP_App_StopUsbCapture directly. */
    if(xe_np_usb_capture_requested && xe_np_quad_dma_active && xe_np_probe_state.initialized)
    { return; }
#endif

#if (XE_NP_APP_QUAD_DMA_QUIET_ENABLE != 0U)
    if(xe_np_quad_dma_active && !xe_np_capture_freeze_done && xe_np_capture_freeze_deadline_tick_ms)
    { XE_NP_App_QuadQuietCapture(); return; }
#endif
#if (XE_NP_APP_QUAD_DMA_NO_PROBE_DRYRUN != 0U)
    if(xe_np_capture_freeze_done) { xe_np_quad_dryrun_diag[1] = 3U; }
    __WFI();
    return; /* Stay frozen until reset, even without a probe. */
#endif
    XE_NP_App_RefreshStorageStatus(&xe_np_runtime);
    (void)XE_NP_App_ServiceNpMclkStatus(&xe_np_runtime);
#if (XE_NP_APP_EXPERIMENT_FREEZE_CAPTURE_AFTER_MS != 0U)
    if(xe_np_capture_freeze_done != 0U)
    {
        /* The test snapshot deliberately stopped capture; do not let the
         * normal active-capture watchdog tear it down or restart it. */
        XE_NP_App_UpdateStreamDiag(&xe_np_runtime);
        return;
    }
#endif
#if (XE_NP_BOARD_USB_SYNTHETIC_CLOCK_BENCH_ISOLATION != 0U)
    XE_NP_App_UpdateStreamDiag(&xe_np_runtime);
    return;
#endif

    usb_streaming = (XE_NP_UsbStream_IsStreaming() != 0) ? 1U : 0U;
    if((XE_NP_UsbStream_IsConfigured() != 0) &&
       (XE_NP_APP_EXPERIMENT_SD_SINK_IGNORE_USB_CONFIG == 0U) &&
       (XE_NP_App_IsRawSdSinkActive() != 0))
    {
        /* A configured PC is the selected recorder. Fully stop the SD sink,
         * rather than merely delaying filesystem service, so SDMMC DMA can no
         * longer arbitrate against GPIO capture. */
        XE_NP_App_StopRawSdSink();
    }
    XE_NP_App_HandleRuntimeMclkFault(&xe_np_runtime);
    XE_NP_App_HandleCaptureRuntimeFault(&xe_np_runtime);
    if((usb_streaming == 0U) &&
       ((XE_NP_APP_EXPERIMENT_HOST_TRIGGERED_BRINGUP_ONLY == 0U) ||
        (xe_np_usb_capture_requested != 0U)))
    {
        XE_NP_App_HandleProbeRuntimeControlFault(&xe_np_runtime);
        XE_NP_App_ServiceProbeBringUp(&xe_np_runtime);
        XE_NP_App_ServiceLegacyImu(&xe_np_runtime);
        XE_NP_App_ServiceLegacyBleBeacon();
    }
#if (XE_NP_APP_LEGACY_FS_ENABLE != 0U)
    if(usb_streaming == 0U)
    {
        CE32_SystemCompat_Service();
        XE_NP_LegacyFs_Service(&xe_np_runtime);
        CE32_File_FileStream_ServiceDeferredSignals();
        XE_NP_App_ServiceRecorderBacklogWatchdogs(&xe_np_runtime);
        XE_NP_App_HandleLegacyFsFault(&xe_np_runtime);
    }
#endif
    /*
     * Raw SD logging is the primary no-loss path. Service it before the
     * foreground validator/decoder so diagnostic work cannot steal the last
     * milliseconds of ring margin from sustained capture.
     */
#if (XE_NP_APP_RAW_SD_DEDICATED_CAPTURE_ENABLE != 0U)
    /* This image has rejected USB capture START requests and its raw recorder
     * owns the only active consumers.  Keep the storage service live even if
     * the USB control plane is in a pending/streaming state. */
    XE_NP_App_ServiceRawSdSink(&xe_np_runtime);
#else
    if((usb_streaming == 0U) &&
       ((XE_NP_UsbStream_IsConfigured() == 0) ||
        (XE_NP_APP_EXPERIMENT_SD_SINK_IGNORE_USB_CONFIG != 0U)))
    {
        XE_NP_App_ServiceRawSdSink(&xe_np_runtime);
    }
#endif
#if (XE_NP_APP_RAW_SD_SINK_ENABLE != 0U)
    if((usb_streaming == 0U) &&
       ((XE_NP_UsbStream_IsConfigured() == 0) ||
        (XE_NP_APP_EXPERIMENT_SD_SINK_IGNORE_USB_CONFIG != 0U)))
    {
        XE_NP_App_HandleRawSdSinkFault(&xe_np_runtime);
    }
#endif
#if ((XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_ENABLE != 0U) && \
     (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_PUBLISH_ENABLE != 0U) && \
     (XE_NP_APP_CAPTURE_SYNC_WATCHDOG_ENABLE != 0U))
    if((XE_NP_APP_RAW_SD_DEDICATED_CAPTURE_ENABLE == 0U) ||
       (XE_NP_App_IsRawSdSinkActive() == 0))
    {
        XE_NP_App_ServiceInterleavedGpioeSyncWatchdog();
    }
#endif
    first_budget = XE_NP_App_SelectDmaServiceBudget(ports[start_port_index]);
    second_budget = XE_NP_App_SelectDmaServiceBudget(ports[(start_port_index + 1U) & 0x1U]);
    XE_NP_App_ServiceDmaPort(ports[start_port_index], first_budget);
    XE_NP_App_ServiceDmaPort(ports[(start_port_index + 1U) & 0x1U], second_budget);
    xe_np_dma_service_next_port = (uint8_t)((start_port_index + 1U) & 0x1U);
    XE_NP_App_DrainPendingErrors(&xe_np_runtime.sys_param);
#if (XE_NP_APP_EXPERIMENT_FREEZE_CAPTURE_AFTER_MS != 0U)
    if((xe_np_capture_freeze_done == 0U) &&
       (xe_np_capture_freeze_deadline_tick_ms != 0U) &&
       ((xe_np_dma_gpioe.active != 0U) || (xe_np_dma_gpiod.active != 0U)
#if (XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_INTERLEAVE_ENABLE != 0U)
        || (xe_np_quad_dma_active != 0U)
#endif
        ) &&
       ((int32_t)(HAL_GetTick() - xe_np_capture_freeze_deadline_tick_ms) >= 0))
    {
        /*
         * Test-only path: keep a whole hardware-produced raw ring resident,
         * then stop the producer before SWD reads it. This avoids creating
         * false frame errors by inspecting bytes while GPDMA is updating them.
         */
        XE_NP_App_LatchCaptureBenchDiag(
            &xe_np_runtime,
            XE_NP_APP_CAPTURE_BENCH_DIAG_REASON_FROZEN_SNAPSHOT);
        /* A frozen SWD snapshot must not leave SDMMC IDMA active against the
         * same bus fabric.  Stop it before halting the capture producers so
         * the resulting ring is a bounded, inspectable experiment. */
        XE_NP_App_StopRawSdSink();
#if ((XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_INTERLEAVE_ENABLE != 0U) && \
     (XE_NP_APP_EXPERIMENT_FREEZE_QUIESCE_ONLY != 0U))
        XE_NP_App_QuiesceGpioeQuadDmaInterleaveForSnapshot();
#else
        XE_NP_App_StopCaptureDma();
#endif
        xe_np_capture_freeze_done = 1U;
    }
#endif
    XE_NP_App_RefreshDmaStatus();
    XE_NP_App_HandleCaptureOverrun(&xe_np_runtime);
    XE_NP_App_UpdateStreamDiag(&xe_np_runtime);
#if (XE_NP_APP_BENCH_CAPTURE_WITHOUT_STORAGE != 0U)
#if (XE_NP_APP_EXPERIMENT_FREEZE_CAPTURE_AFTER_MS != 0U)
    if(xe_np_capture_freeze_done == 0U)
    {
#endif
    XE_NP_App_LatchCaptureBenchDiag(&xe_np_runtime, xe_np_capture_bench_diag.reason);
#if (XE_NP_APP_EXPERIMENT_FREEZE_CAPTURE_AFTER_MS != 0U)
    }
#endif
#endif
}

const XE_NP_Runtime *XE_NP_App_GetRuntime(void)
{
    return &xe_np_runtime;
}

int XE_NP_App_SetRuntimeMcoDivider(uint32_t divider)
{
    /*
     * Allow divider changes before a host capture starts, even if the
     * background NP bring-up path already armed DMA. In that case, first
     * force the session back to the normal safe-reset state, then reprogram
     * PA8/MCO and let the usual bring-up logic restart on the new clock.
     *
     * A live USB stream is different: once bulk payload is flowing, changing
     * MCLK underneath the active capture would invalidate the in-flight data
     * path, so keep rejecting that case.
     */
    if(XE_NP_UsbStream_IsStreaming() != 0)
    {
        CE32_ReportError(&xe_np_runtime.sys_param, CE32_ERRORID_PARAM_ERR);
        return -1;
    }
    if(XE_NP_App_NpSessionActive(&xe_np_runtime) != 0)
    {
        XE_NP_App_ApplySafeNpStartup(&xe_np_runtime);
    }

    if(XE_NP_Board_SetRuntimeMcoDivider(divider) != 0)
    {
        CE32_ReportError(&xe_np_runtime.sys_param, CE32_ERRORID_PARAM_ERR);
        return -1;
    }

    XE_NP_App_ApplySensitivePinSafety();
    XE_NP_App_ConfigureCapture(&xe_np_runtime);
    XE_NP_App_RefreshStorageStatus(&xe_np_runtime);
    (void)XE_NP_App_ServiceNpMclkStatus(&xe_np_runtime);
    XE_NP_App_UpdateStreamDiag(&xe_np_runtime);
    return 0;
}

#if (XE_NP_APP_QUAD_USB_WORD_EXPORT != 0U)
uint32_t XE_NP_App_UsbWordFault(void) { return xe_np_quad_usb_word_diag[1]; }

void XE_NP_App_ResetUsbWordSession(void)
{
    /* Foreground only, after USB/staging disarm. Preserve clock/configuration
     * choices, but fully unwind the prior producer and probe session. Normal
     * bring-up re-arms all four lanes and clears QUX1 only for this new START. */
    xe_np_usb_capture_requested = 0U;
    xe_np_usb_window_export_enabled = 0U;
    XE_NP_App_ApplySafeNpStartup(&xe_np_runtime);
    XE_NP_GeneratedProbeConfigs_Bind();
    xe_np_runtime.active_np_probe_mask=(uint8_t)np_mode_mask;
    /* NP_PORT2_CONFIG_DIV4: 46.8 MHz at the full-speed core; scaled20 retains its existing 31.2 MHz configuration clock. */
    (void)XE_NP_Board_SetRuntimeMcoDivider(4U);
    XE_NP_App_ApplySensitivePinSafety();xe_np_mclk_next_refresh_tick_ms=0U;
    (void)XE_NP_App_ServiceNpMclkStatus(&xe_np_runtime);
    XE_NP_App_ApplyGpioeCaptureTimingCandidate(np_mode_hz>=20000U?0U:1U);
    XE_NP_App_ConfigureCapture(&xe_np_runtime);
    xe_np_quad_dma_usb_cursor_valid = 0U;
    xe_np_quad_dma_usb_batch_claimed = 0U;
    xe_np_quad_usb_history_valid = 0U;
}
#endif

int XE_NP_App_StartUsbCapture(uint8_t port_index)
{
    uint32_t required_port_flag;

#if (XE_NP_APP_RAW_SD_DEDICATED_CAPTURE_ENABLE != 0U)
    /* A raw-SD characterization/recording image has exclusive ownership of
     * both capture rings.  A connected console may still issue its normal USB
     * START request; rejecting it here keeps that request from arming a second
     * consumer or suppressing the SD foreground writer. */
    (void)port_index;
    return -1;
#endif

    if(port_index == 0U)
    {
        required_port_flag = XE_NP_CAPTURE_FLAG_GPIOE_ACTIVE;
    }
    else if(port_index == 1U)
    {
        required_port_flag = XE_NP_CAPTURE_FLAG_GPIOD_ACTIVE;
    }
    else
    {
        return -1;
    }

    if(port_index == 0U)
    {
        XE_NP_App_ApplyGpioeCaptureTimingCandidate(np_mode_hz>=20000U?0U:1U);
    }

    xe_np_usb_capture_port_index = port_index;
    if(!xe_np_usb_capture_requested)np_capture_anchor_invalidate();
    xe_np_usb_capture_requested = 1U;
    xe_np_probe_state.next_retry_tick_ms = 0U;

    if(((xe_np_runtime.capture.flags & required_port_flag) == 0U) ||
       ((xe_np_runtime.capture.flags & XE_NP_APP_CAPTURE_BLOCKING_FLAGS) != 0U))
    {
        return -1;
    }

    /*
     * In bench mode, host Stop freezes probe bring-up by latching
     * init_failed=1 with no retry deadline so SRAM contents can be inspected
     * over SWD. A later host Start must explicitly release that hold and let
     * probe bring-up re-arm capture again; the capture layout flags alone do
     * not mean the DMA producers are running.
     */
    if((xe_np_probe_state.init_failed != 0U) &&
       (xe_np_probe_state.next_retry_tick_ms == 0U) &&
       (xe_np_probe_state.bringup_stage == XE_NP_APP_PROBE_STAGE_IDLE))
    {
        /*
         * Bench-hold failures can preserve stale DMA active flags so SWD can
         * inspect the last ring contents. A later host START must force a full
         * safe-state unwind before re-arming probe bring-up; otherwise the
         * stale producer flags suppress the normal restart path and leave USB
         * stuck in START_PENDING forever.
         */
        XE_NP_App_ApplySafeNpStartup(&xe_np_runtime);
        xe_np_probe_state.init_failed = 0U;
    }

    /*
     * Host START should actively kick the bring-up state machine when the app
     * is sitting in the safe reset state between short bench captures. Relying
     * on passive background service alone can leave the session parked at
     * IDLE even though the USB control request was accepted.
     */
    if(xe_np_probe_state.initialized == 0U)
    {
        xe_np_mclk_next_refresh_tick_ms = 0U;
        (void)XE_NP_App_ServiceNpMclkStatus(&xe_np_runtime);
        if((xe_np_probe_state.bringup_stage == XE_NP_APP_PROBE_STAGE_IDLE) &&
           (xe_np_probe_state.next_retry_tick_ms == 0U))
        {
            XE_NP_App_BeginProbeBringUpFromIdle(&xe_np_runtime, HAL_GetTick());
        }
        XE_NP_App_ServiceProbeBringUp(&xe_np_runtime);
    }

    return 0;
}

int XE_NP_App_IsUsbCaptureReady(uint8_t port_index)
{
    uint32_t required_port_flag;
    uint32_t required_runtime_flags;

    if(port_index == 0U)
    {
        required_port_flag = XE_NP_CAPTURE_FLAG_GPIOE_ACTIVE;
    }
    else if(port_index == 1U)
    {
        required_port_flag = XE_NP_CAPTURE_FLAG_GPIOD_ACTIVE;
    }
    else
    {
        return 0;
    }

    if(((xe_np_runtime.capture.flags & required_port_flag) == 0U) ||
       ((xe_np_runtime.capture.flags & XE_NP_APP_CAPTURE_BLOCKING_FLAGS) != 0U))
    {
        return 0;
    }

    required_runtime_flags =
        XE_NP_RUNTIME_FLAG_NP_GLOBAL_RESET_RELEASED |
        XE_NP_RUNTIME_FLAG_NP_RECORD_RESET_RELEASED;
    if((xe_np_runtime.flags & required_runtime_flags) != required_runtime_flags)
    {
        return 0;
    }

    if((xe_np_probe_state.initialized == 0U) ||
       (XE_NP_App_CaptureProducersRunning(&xe_np_runtime) == 0))
    {
#if (XE_NP_APP_EXPERIMENT_CAPTURE_SUPPRESS_DMA_ENABLE != 0U)
        /* The diagnostic deliberately has no producer.  The preceding
         * rail/reset/initialisation checks remain mandatory, making this a
         * valid control for real-session USB transport only. */
        return (xe_np_probe_state.initialized != 0U) ? 1 : 0;
#else
        return 0;
#endif
    }

    return 1;
}

void XE_NP_App_StopUsbCapture(void)
{
    uint8_t was_requested = xe_np_usb_capture_requested;
#if (XE_NP_APP_QUAD_USB_WORD_EXPORT != 0U)
    xe_np_usb_capture_requested = 0U;
    xe_np_usb_window_export_enabled = 0U;
#endif

#if (XE_NP_APP_USB_CAPTURE_KEEP_SESSION_ON_HOST_STOP != 0U)
    if(was_requested == 0U)
    {
        return;
    }

    /*
     * In USB bench mode, host Stop means "detach the PC reader", not "reset
     * the probe". Keep the NP/capture session alive so a later host Start can
     * re-arm the consumer at the current DMA producer position without
     * disturbing the real-time stream.
     */
    XE_NP_App_UpdateStreamDiag(&xe_np_runtime);
#else
    xe_np_usb_capture_requested = 0U;
    xe_np_usb_capture_port_index = 0U;

    if(was_requested == 0U)
    {
        return;
    }

    XE_NP_App_ApplySafeNpStartup(&xe_np_runtime);
#if (XE_NP_APP_USB_CAPTURE_FREEZE_DEBUG_ON_HOST_STOP != 0U)
    /*
     * Bench mode normally auto-restarts probe bring-up immediately after a
     * safe startup reset. Hold it in a stopped state here so short host-driven
     * captures can be compared against frozen SRAM contents over SWD.
     */
    xe_np_probe_state.init_failed = 1U;
    xe_np_probe_state.next_retry_tick_ms = 0U;
#endif
    XE_NP_App_UpdateStreamDiag(&xe_np_runtime);
#endif
}

XE_NP_CapturePort *XE_NP_App_GetCapturePortForUsb(uint8_t port_index)
{
    if(port_index == 0U)
    {
        return &xe_np_runtime.capture.gpioe;
    }
    if(port_index == 1U)
    {
        return &xe_np_runtime.capture.gpiod;
    }
    return NULL;
}

int XE_NP_App_EnableUsbWindowExport(uint8_t port_index)
{
    XE_NP_AppDmaPort *port = XE_NP_App_GetDmaPortForUsb(port_index);
    uint32_t primask;

    if((port == NULL) || (port->capture_port == NULL) || (port->capture_port->storage == NULL) ||
       (port->capture_port->storage_bytes == 0U) || (port->active == 0U))
    {
        return -1;
    }

    primask = XE_NP_App_CriticalSection_Enter();
    xe_np_usb_window_export_enabled = 0U;
    xe_np_usb_window_export_port_index = port_index;
    port->pending_read_idx = port->pending_write_idx;
    port->stable_window_valid = 0U;
    port->stable_window_reserved = 0U;
    port->usb_window_warmup_remaining = XE_NP_APP_USB_WINDOW_WARMUP_WINDOWS;
    port->stable_window_release_tick_ms = 0U;
    port->pending_peak_depth = 0U;
    port->backlog_resync_requested = 0U;
    port->usb_active_window_valid = 0U;
    port->usb_active_window_consumed_bytes = 0U;
    memset(&port->usb_active_window, 0, sizeof(port->usb_active_window));
    port->wrap_probe_tail_ready = 0U;
    port->wrap_probe_ready = 0U;
    port->wrap_probe_valid = 0U;
    port->wrap_probe_count = 0U;
    port->wrap_probe_tail_offset = 0U;
    port->wrap_probe_head_offset = 0U;
    port->wrap_probe_tail_total_bytes_end = 0ULL;
    port->wrap_probe_head_total_bytes_end = 0ULL;
    memset(port->wrap_probe_bytes, 0, sizeof(port->wrap_probe_bytes));
    XE_NP_App_ResetFirstBacklogResyncSnapshot(port);
    xe_np_usb_window_export_enabled = 1U;
    XE_NP_App_CriticalSection_Exit(primask);
    return 0;
}

void XE_NP_App_DisableUsbWindowExport(void)
{
    XE_NP_AppDmaPort *port;
    uint32_t primask;

    primask = XE_NP_App_CriticalSection_Enter();
    port = XE_NP_App_GetDmaPortForUsb(xe_np_usb_window_export_port_index);
    xe_np_usb_window_export_enabled = 0U;
#if ((XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_ENABLE != 0U) && \
     (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_PUBLISH_ENABLE != 0U) && \
     (XE_NP_APP_EXPERIMENT_SYNC_WATCHDOG_ENABLE != 0U))
    /* The paired exporter owns an absolute lane cursor rather than a normal
     * dataMGR consumer.  A STOP/START (including the logger's first-packet
     * recovery) must begin from the current guarded writer, never resume a
     * stale cursor and declare a false ring overrun while qualification is
     * still in progress. */
    xe_np_gpioe_interleaved_usb_next_lane_byte = 0ULL;
    xe_np_gpioe_interleaved_usb_next_lane_byte_valid = 0U;
    xe_np_gpioe_interleaved_usb_next_lane_is_b = 0U;
    xe_np_gpioe_interleaved_usb_dma_batch_claimed = 0U;
    xe_np_gpioe_interleaved_usb_dma_batch_committed = 0U;
    xe_np_gpioe_interleaved_usb_dma_batch_lane_b_packets = 0U;
    xe_np_gpioe_interleaved_usb_dma_batch_payload_bytes = 0U;
#endif
    if(port != NULL)
    {
        port->pending_read_idx = port->pending_write_idx;
        port->stable_window_valid = 0U;
        port->stable_window_reserved = 0U;
        port->usb_window_warmup_remaining = 0U;
        port->stable_window_release_tick_ms = 0U;
        port->backlog_resync_requested = 0U;
        port->usb_active_window_valid = 0U;
        port->usb_active_window_consumed_bytes = 0U;
        memset(&port->usb_active_window, 0, sizeof(port->usb_active_window));
    }
    XE_NP_App_CriticalSection_Exit(primask);
}

int XE_NP_App_DequeueUsbCaptureWindow(uint8_t port_index, XE_NP_AppUsbCaptureWindow *window)
{
    return XE_NP_App_DequeueUsbCaptureWindowLimited(port_index, window, 0U);
}

int XE_NP_App_DequeueUsbCaptureWindowLimited(
    uint8_t port_index,
    XE_NP_AppUsbCaptureWindow *window,
    uint32_t max_bytes)
{
    XE_NP_AppDmaPort *port = XE_NP_App_GetDmaPortForUsb(port_index);
    XE_NP_AppDmaWindow active_window;
    uint32_t active_consumed_bytes;
    uint32_t storage_bytes;
    uint32_t chunk_bytes;
    uint32_t segment_offset;
    uint32_t segment_bytes;
    uint32_t remaining_bytes;
    uint32_t contiguous_bytes;
    uint8_t read_idx;
    uint32_t primask;

    if((port == NULL) || (window == NULL) || (XE_NP_App_UsbWindowExportArmedForPort(port) == 0))
    {
        return 0;
    }

    chunk_bytes = XE_NP_App_GetUsbExportChunkBytes(port);
    storage_bytes = XE_NP_App_GetCaptureStoragePhysicalBytes(port);
    if((storage_bytes == 0U) || (chunk_bytes == 0U))
    {
        return -1;
    }

    primask = XE_NP_App_CriticalSection_Enter();
    if(port->backlog_resync_requested != 0U)
    {
        port->pending_read_idx = port->pending_write_idx;
        port->backlog_resync_requested = 0U;
        port->pending_peak_depth = 0U;
        port->usb_active_window_valid = 0U;
        port->usb_active_window_consumed_bytes = 0U;
        XE_NP_App_CriticalSection_Exit(primask);
        return 0;
    }

    if(port->usb_active_window_valid == 0U)
    {
        if(port->pending_read_idx == port->pending_write_idx)
        {
            XE_NP_App_CriticalSection_Exit(primask);
            return 0;
        }

        read_idx = port->pending_read_idx;
        port->usb_active_window = port->pending_windows[read_idx];
        port->usb_active_window_consumed_bytes = 0U;
        port->usb_active_window_valid = 1U;
        __DMB();
        port->pending_read_idx = (uint8_t)((read_idx + 1U) & XE_NP_APP_DMA_WINDOW_QUEUE_MASK);
    }

    active_window = port->usb_active_window;
    active_consumed_bytes = port->usb_active_window_consumed_bytes;
    XE_NP_App_CriticalSection_Exit(primask);

    if((port->capture_port == NULL) || (port->capture_port->storage == NULL) ||
       (active_window.size_bytes == 0U))
    {
        XE_NP_App_ReportCaptureFault(port, CE32_ERRORID_CORE_ERROR, 1U, 0U);
        return -1;
    }
    if(active_consumed_bytes >= active_window.size_bytes)
    {
        /*
         * The current ring-backed USB packet has already consumed the whole
         * window logically, but the payload still lives in capture SRAM until
         * the USB IN transfer completes. Hold dequeue here until the USB
         * stream path explicitly releases the active window on completion.
         */
        return 0;
    }

    remaining_bytes = active_window.size_bytes - active_consumed_bytes;
    segment_bytes = (remaining_bytes > chunk_bytes) ? chunk_bytes : remaining_bytes;
    if((max_bytes != 0U) && (segment_bytes > max_bytes))
    {
        segment_bytes = max_bytes;
    }
    segment_offset = active_window.offset_bytes + active_consumed_bytes;
    if(segment_offset >= storage_bytes)
    {
        segment_offset %= storage_bytes;
    }
    contiguous_bytes = storage_bytes - segment_offset;
    if(segment_bytes > contiguous_bytes)
    {
        segment_bytes = contiguous_bytes;
    }
    if(segment_bytes == 0U)
    {
        XE_NP_App_ReportCaptureFault(port, CE32_ERRORID_CORE_ERROR, 1U, 0U);
        return -1;
    }

    window->data = &port->capture_port->storage[segment_offset];
    window->size_bytes = segment_bytes;
    window->ring_offset_bytes = segment_offset;
    window->total_bytes_end =
        active_window.total_bytes_end - (uint64_t)(active_window.size_bytes -
                                                   (active_consumed_bytes + segment_bytes));
    port->last_dequeued_offset_bytes = segment_offset;
    port->last_dequeued_size_bytes = segment_bytes;
    if((port->dequeued_window_count == 0U) ||
       (segment_bytes < port->dequeued_size_min_bytes))
    {
        port->dequeued_size_min_bytes = segment_bytes;
    }
    if(segment_bytes > port->dequeued_size_max_bytes)
    {
        port->dequeued_size_max_bytes = segment_bytes;
    }
    port->dequeued_window_count++;

    primask = XE_NP_App_CriticalSection_Enter();
    if(port->usb_active_window_valid != 0U)
    {
        port->usb_active_window_consumed_bytes = active_consumed_bytes + segment_bytes;
    }
    XE_NP_App_CriticalSection_Exit(primask);
    return 1;
}

int XE_NP_App_IsUsbInterleavedCapture(void)
{
#if ((XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_ENABLE != 0U) && \
     (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_PUBLISH_ENABLE != 0U) && \
     (XE_NP_APP_EXPERIMENT_SYNC_WATCHDOG_ENABLE != 0U))
    uint8_t probe_mask = xe_np_runtime.capture.active_probe_mask & XE_NP_CAPTURE_ALL_PROBES_MASK;

    return (((probe_mask == XE_NP_CAPTURE_PROBE1_MASK) ||
              (probe_mask == XE_NP_CAPTURE_PROBE2_MASK)) &&
             ((xe_np_runtime.capture.flags & XE_NP_CAPTURE_FLAG_GPIOE_ACTIVE) != 0U) &&
             (xe_np_dma_gpioe.active != 0U) &&
             (xe_np_dma_gpiod.active != 0U)) ? 1 : 0;
#else
    return 0;
#endif
}

int XE_NP_App_IsUsbQuadInterleavedCapture(void)
{
#if ((XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_INTERLEAVE_ENABLE != 0U) && \
     (XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_INTERLEAVE_USB_ENABLE != 0U))
    uint8_t probe_mask = xe_np_runtime.capture.active_probe_mask & XE_NP_CAPTURE_ALL_PROBES_MASK;

    return (((probe_mask == XE_NP_CAPTURE_PROBE1_MASK) ||
              (probe_mask == XE_NP_CAPTURE_PROBE2_MASK)
#if NP_SD_APP
              || (probe_mask == (XE_NP_CAPTURE_PROBE1_MASK | XE_NP_CAPTURE_PROBE2_MASK))
#endif
              ) &&
            ((xe_np_runtime.capture.flags & XE_NP_CAPTURE_FLAG_GPIOE_ACTIVE) != 0U) &&
            (xe_np_quad_dma_active != 0U) &&
            (xe_np_quad_dma_error_seen == 0U)) ? 1 : 0;
#else
    return 0;
#endif
}

int XE_NP_App_IsUsbQuadInterleavedCaptureConfigured(void)
{
#if ((XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_INTERLEAVE_ENABLE != 0U) && \
     (XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_INTERLEAVE_USB_ENABLE != 0U))
    return 1;
#else
    return 0;
#endif
}

#include "np3_producer.inc"

#if NP_SD_APP
#include "np_sd_firmware.inc"
#endif

uint32_t XE_NP_App_CopyUsbQuadInterleavedLanePayload(
    uint8_t *dst,
    uint32_t max_bytes,
    uint8_t *lane_index)
{
#if ((XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_INTERLEAVE_ENABLE != 0U) && \
     (XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_INTERLEAVE_USB_ENABLE != 0U))
    uint32_t lane_bytes;
    uint32_t lane;
    uint32_t head;
    uint32_t cursor;
    uint32_t available;
    uint32_t first_bytes;
    uint8_t *lane_base;

    if((dst == NULL) || (lane_index == NULL) || (max_bytes == 0U) ||
       (XE_NP_App_IsUsbQuadInterleavedCapture() == 0) ||
       (xe_np_runtime.capture.raw_storage == NULL) ||
       ((xe_np_runtime.capture.raw_storage_bytes %
         XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT) != 0U))
    {
        xe_np_quad_dma_usb_export_debug = 1U;
        return 0U;
    }

    lane_bytes = XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_BLOCK_BYTES *
                 XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_REPEAT_COUNT;
    if(xe_np_runtime.capture.raw_storage_bytes <
       lane_bytes * XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT ||
       (lane_bytes <= XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_USB_GUARD_BYTES) ||
       (max_bytes > (lane_bytes - XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_USB_GUARD_BYTES)))
    {
        xe_np_quad_dma_usb_export_debug = 2U;
        return 0U;
    }

    /* Snapshot all four producer heads once.  Each physical cursor is anchored
     * to its own writer rather than to a software service cadence; this is the
     * live equivalent of the CDAR-head rotation that validated the frozen ring. */
    if(xe_np_quad_dma_usb_cursor_valid == 0U)
    {
        for(lane = 0U; lane < XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_LANE_COUNT; ++lane)
        {
            uint32_t base = (uint32_t)(xe_np_runtime.capture.raw_storage + (lane * lane_bytes));
            uint32_t lane_head = xe_np_quad_dma_lanes[lane].hdma.Instance->CDAR;

            if((lane_head < base) || (lane_head > (base + lane_bytes)))
            {
                xe_np_quad_dma_usb_export_debug = 3U;
                return 0U;
            }
            lane_head = (lane_head == (base + lane_bytes)) ? 0U : (lane_head - base);
            xe_np_quad_dma_usb_cursor[lane] =
                (lane_head >= XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_USB_GUARD_BYTES) ?
                    (lane_head - XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_USB_GUARD_BYTES) :
                    (lane_head + lane_bytes - XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_USB_GUARD_BYTES);
        }
        xe_np_quad_dma_usb_next_lane = 0U;
        xe_np_quad_dma_usb_cursor_valid = 1U;
        xe_np_quad_dma_usb_export_debug = 4U;
        return 0U;
    }

    lane = xe_np_quad_dma_usb_next_lane;
    lane_base = xe_np_runtime.capture.raw_storage + (lane * lane_bytes);
    head = xe_np_quad_dma_lanes[lane].hdma.Instance->CDAR;
    if(((uint32_t)head < (uint32_t)lane_base) ||
       ((uint32_t)head > ((uint32_t)lane_base + lane_bytes)))
    {
        xe_np_quad_dma_usb_export_debug = 3U;
        return 0U;
    }
    head = (head == ((uint32_t)lane_base + lane_bytes)) ? 0U :
        (head - (uint32_t)lane_base);
    cursor = xe_np_quad_dma_usb_cursor[lane];
    available = (head >= cursor) ? (head - cursor) : (head + lane_bytes - cursor);

    /* A reader is admitted only after the writer has moved a full guard beyond
     * the requested packet.  A near-full distance is an unambiguous wrap risk:
     * stop the stream rather than substitute overwritten chronology. */
    if(available <= (XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_USB_GUARD_BYTES + max_bytes))
    {
        xe_np_quad_dma_usb_export_debug = 4U;
        return 0U;
    }
    if(available >= (lane_bytes - XE_NP_APP_GPIOE_QUAD_DMA_INTERLEAVE_USB_GUARD_BYTES))
    {
        xe_np_quad_dma_usb_export_debug = 5U;
        return 0U;
    }

    first_bytes = lane_bytes - cursor;
    if(first_bytes > max_bytes)
    {
        first_bytes = max_bytes;
    }
    XE_NP_Capture_DCacheInvalidate(&lane_base[cursor], first_bytes);
    memcpy(dst, &lane_base[cursor], first_bytes);
    if(first_bytes < max_bytes)
    {
        XE_NP_Capture_DCacheInvalidate(lane_base, max_bytes - first_bytes);
        memcpy(&dst[first_bytes], lane_base, max_bytes - first_bytes);
    }
    cursor += max_bytes;
    if(cursor >= lane_bytes)
    {
        cursor -= lane_bytes;
    }
    xe_np_quad_dma_usb_cursor[lane] = cursor;
    xe_np_quad_dma_usb_next_lane = (uint8_t)((lane + 1U) & 0x3U);
    *lane_index = (uint8_t)lane;
    xe_np_quad_dma_usb_export_debug = 7U;
    return max_bytes;
#else
    (void)dst;
    (void)max_bytes;
    (void)lane_index;
    return 0U;
#endif
}

uint32_t XE_NP_App_CopyUsbInterleavedPayload(uint8_t *dst, uint32_t max_bytes)
{
#if ((XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_ENABLE != 0U) && \
     (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_PUBLISH_ENABLE != 0U) && \
     (XE_NP_APP_EXPERIMENT_SYNC_WATCHDOG_ENABLE != 0U))
    XE_NP_CapturePort *capture_a = &xe_np_runtime.capture.gpioe;
    XE_NP_CapturePort *capture_b = &xe_np_runtime.capture.gpiod;
    uint64_t total_a;
    uint64_t total_b;
    uint64_t safe_end;
    uint64_t available_lane_bytes;
    uint32_t lane_bytes;
    uint32_t lane_count;
    uint32_t offset_a;
    uint32_t offset_b;
    uint32_t index;
    int32_t shifted_offset_b;
    uint32_t primask;

    if((dst == NULL) || (max_bytes < 2U) ||
       (XE_NP_App_IsUsbInterleavedCapture() == 0) ||
       (xe_np_gpioe_interleaved_validation_lane_b_offset_valid == 0U) ||
       (capture_a->storage == NULL) || (capture_b->storage == NULL) ||
       (capture_a->storage_bytes == 0U) ||
       (capture_a->storage_bytes != capture_b->storage_bytes))
    {
        xe_np_gpioe_interleaved_usb_export_debug = 1U;
        return 0U;
    }

    primask = XE_NP_App_CriticalSection_Enter();
    total_a = capture_a->total_bytes_captured;
    total_b = capture_b->total_bytes_captured;
    XE_NP_App_CriticalSection_Exit(primask);
    safe_end = (total_a < total_b) ? total_a : total_b;
    /* Stay one paired validation window behind the producer. The GPIO DMA has
     * published only complete half-windows at this point, so this region cannot
     * be rewritten while the foreground makes its private USB copy. */
    if(safe_end <= (uint64_t)(XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_VALIDATION_LANE_BYTES * 2U))
    {
        xe_np_gpioe_interleaved_usb_export_debug = 2U;
        return 0U;
    }
    safe_end -= (uint64_t)(XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_VALIDATION_LANE_BYTES * 2U);
    lane_bytes = capture_a->storage_bytes;

    if(xe_np_gpioe_interleaved_usb_next_lane_byte_valid == 0U)
    {
        /*
         * Qualification has already copied a complete history window behind
         * the writers.  Start at that guarded history rather than at its tail:
         * the paired DMA counters can remain unchanged until the next large
         * repeated-block event, and starting at safe_end discarded the only
         * publishable data in the meantime.
         */
        xe_np_gpioe_interleaved_usb_next_lane_byte =
            (safe_end > (XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_VALIDATION_LANE_BYTES * 2U)) ?
                (safe_end - (XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_VALIDATION_LANE_BYTES * 2U)) :
                0ULL;
        xe_np_gpioe_interleaved_usb_next_lane_byte_valid = 1U;
        xe_np_gpioe_interleaved_usb_export_debug = 3U;
    }
    if(safe_end <= xe_np_gpioe_interleaved_usb_next_lane_byte)
    {
        xe_np_gpioe_interleaved_usb_export_debug = 4U;
        return 0U;
    }

    available_lane_bytes = safe_end - xe_np_gpioe_interleaved_usb_next_lane_byte;
    lane_bytes = capture_a->storage_bytes;
    if(available_lane_bytes >= (uint64_t)(lane_bytes -
                                          (XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_VALIDATION_LANE_BYTES * 2U)))
    {
        /* Host starvation exceeded the capture-ring margin. Resynchronise at
         * the newest safe pair; do not read bytes that DMA may already reuse. */
        xe_np_gpioe_interleaved_usb_next_lane_byte = safe_end;
        xe_np_gpioe_interleaved_usb_export_debug = 5U;
        return 0U;
    }

    lane_count = max_bytes / 2U;
    if((uint64_t)lane_count > available_lane_bytes)
    {
        lane_count = (uint32_t)available_lane_bytes;
    }
    if(lane_count == 0U)
    {
        xe_np_gpioe_interleaved_usb_export_debug = 6U;
        return 0U;
    }

    offset_a = (uint32_t)(xe_np_gpioe_interleaved_usb_next_lane_byte % lane_bytes);
    shifted_offset_b = (int32_t)offset_a + xe_np_gpioe_interleaved_validation_lane_b_offset;
    if(shifted_offset_b < 0)
    {
        shifted_offset_b += (int32_t)lane_bytes;
    }
    else if(shifted_offset_b >= (int32_t)lane_bytes)
    {
        shifted_offset_b -= (int32_t)lane_bytes;
    }
    offset_b = (uint32_t)shifted_offset_b;
    XE_NP_App_InvalidateInterleavedGpioeLaneRange(capture_a, offset_a, lane_count);
    XE_NP_App_InvalidateInterleavedGpioeLaneRange(capture_b, offset_b, lane_count);
    for(index = 0U; index < lane_count; ++index)
    {
        dst[index * 2U] = capture_a->storage[offset_a];
        dst[(index * 2U) + 1U] = capture_b->storage[offset_b];
        if(++offset_a == lane_bytes)
        {
            offset_a = 0U;
        }
        if(++offset_b == lane_bytes)
        {
            offset_b = 0U;
        }
    }
    xe_np_gpioe_interleaved_usb_next_lane_byte += lane_count;
    xe_np_gpioe_interleaved_usb_export_debug = 7U;
    return lane_count * 2U;
#else
    (void)dst;
    (void)max_bytes;
    return 0U;
#endif
}

uint32_t XE_NP_App_CopyUsbInterleavedLanePayload(
    uint8_t *dst,
    uint32_t max_bytes,
    uint8_t *lane_b)
{
#if ((XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_ENABLE != 0U) && \
     (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_PUBLISH_ENABLE != 0U) && \
     (XE_NP_APP_EXPERIMENT_SYNC_WATCHDOG_ENABLE != 0U))
    XE_NP_CapturePort *capture_a = &xe_np_runtime.capture.gpioe;
    XE_NP_CapturePort *capture_b = &xe_np_runtime.capture.gpiod;
    uint64_t total_a;
    uint64_t total_b;
    uint64_t safe_end;
    uint64_t available_lane_bytes;
    uint32_t lane_bytes;
    uint32_t offset;
    uint32_t first_bytes;
    int32_t shifted_offset;
    uint8_t copy_lane_b;

    if((dst == NULL) || (max_bytes == 0U) || (lane_b == NULL) ||
       (XE_NP_App_IsUsbInterleavedCapture() == 0) ||
       (xe_np_gpioe_interleaved_validation_lane_b_offset_valid == 0U) ||
       (capture_a->storage == NULL) || (capture_b->storage == NULL) ||
       (capture_a->storage_bytes == 0U) ||
       (capture_a->storage_bytes != capture_b->storage_bytes))
    {
        xe_np_gpioe_interleaved_usb_export_debug = 1U;
        return 0U;
    }

    uint32_t primask = XE_NP_App_CriticalSection_Enter();
    total_a = capture_a->total_bytes_captured;
    total_b = capture_b->total_bytes_captured;
    XE_NP_App_CriticalSection_Exit(primask);
    safe_end = (total_a < total_b) ? total_a : total_b;
    if(safe_end <= (uint64_t)(XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_VALIDATION_LANE_BYTES * 2U))
    {
        xe_np_gpioe_interleaved_usb_export_debug = 2U;
        return 0U;
    }
    safe_end -= (uint64_t)(XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_VALIDATION_LANE_BYTES * 2U);
    lane_bytes = capture_a->storage_bytes;

    if(xe_np_gpioe_interleaved_usb_next_lane_byte_valid == 0U)
    {
        /*
         * A USB recording starts at its command boundary, not at the oldest
         * capture history retained while the paired phase search was running.
         * Start exactly at the current guarded producer position and wait for
         * the next completed pair.  Subtracting nearly one full lane ring here
         * makes the exporter begin at stale pre-start history and gives it
         * only one ring period to drain that entire backlog; at 30 kS/s this
         * inevitably trips the lossless-wrap guard before USB can catch up.
         */
        xe_np_gpioe_interleaved_usb_next_lane_byte = safe_end;
        xe_np_gpioe_interleaved_usb_next_lane_byte_valid = 1U;
        xe_np_gpioe_interleaved_usb_next_lane_is_b = 0U;
    }
    if(safe_end <= xe_np_gpioe_interleaved_usb_next_lane_byte)
    {
        xe_np_gpioe_interleaved_usb_export_debug = 4U;
        return 0U;
    }
    available_lane_bytes = safe_end - xe_np_gpioe_interleaved_usb_next_lane_byte;
    if(available_lane_bytes > (uint64_t)(lane_bytes -
                                         (XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_VALIDATION_LANE_BYTES * 2U)))
    {
        xe_np_gpioe_interleaved_usb_next_lane_byte = safe_end;
        xe_np_gpioe_interleaved_usb_next_lane_is_b = 0U;
        xe_np_gpioe_interleaved_usb_export_debug = 5U;
        return 0U;
    }
    if((uint64_t)max_bytes > available_lane_bytes)
    {
        xe_np_gpioe_interleaved_usb_export_debug = 6U;
        return 0U;
    }

    copy_lane_b = xe_np_gpioe_interleaved_usb_next_lane_is_b;
    offset = (uint32_t)(xe_np_gpioe_interleaved_usb_next_lane_byte % lane_bytes);
    if(copy_lane_b != 0U)
    {
        shifted_offset = (int32_t)offset + xe_np_gpioe_interleaved_validation_lane_b_offset;
        if(shifted_offset < 0)
        {
            shifted_offset += (int32_t)lane_bytes;
        }
        else if(shifted_offset >= (int32_t)lane_bytes)
        {
            shifted_offset -= (int32_t)lane_bytes;
        }
        offset = (uint32_t)shifted_offset;
        XE_NP_App_InvalidateInterleavedGpioeLaneRange(capture_b, offset, max_bytes);
    }
    else
    {
        XE_NP_App_InvalidateInterleavedGpioeLaneRange(capture_a, offset, max_bytes);
    }
    first_bytes = lane_bytes - offset;
    if(first_bytes > max_bytes)
    {
        first_bytes = max_bytes;
    }
    if (copy_lane_b != 0U)
    {
        memcpy(dst, &capture_b->storage[offset], first_bytes);
    }
    else
    {
        memcpy(dst, &capture_a->storage[offset], first_bytes);
    }
    if(first_bytes < max_bytes)
    {
        memcpy(&dst[first_bytes],
               (copy_lane_b != 0U) ? capture_b->storage : capture_a->storage,
               max_bytes - first_bytes);
    }
    if(copy_lane_b != 0U)
    {
        xe_np_gpioe_interleaved_usb_next_lane_byte += max_bytes;
        xe_np_gpioe_interleaved_usb_next_lane_is_b = 0U;
    }
    else
    {
        xe_np_gpioe_interleaved_usb_next_lane_is_b = 1U;
    }
    *lane_b = copy_lane_b;
    xe_np_gpioe_interleaved_usb_export_debug = 7U;
    return max_bytes;
#else
    (void)dst;
    (void)max_bytes;
    (void)lane_b;
    return 0U;
#endif
}

int XE_NP_App_BeginUsbInterleavedDmaBatch(
    uint32_t payload_bytes_per_packet,
    uint32_t max_packet_count,
    XE_NP_AppUsbInterleavedDmaBatch *batch)
{
#if ((XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_ENABLE != 0U) && \
     (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_PUBLISH_ENABLE != 0U) && \
     (XE_NP_APP_EXPERIMENT_SYNC_WATCHDOG_ENABLE != 0U))
    XE_NP_CapturePort *capture_a = &xe_np_runtime.capture.gpioe;
    XE_NP_CapturePort *capture_b = &xe_np_runtime.capture.gpiod;
    uint64_t total_a;
    uint64_t total_b;
    uint64_t safe_end;
    uint64_t cursor;
    uint32_t lane_bytes;
    uint32_t packet_count;
    uint32_t lane_a_packets;
    uint32_t lane_b_packets;
    uint32_t lane_a_offset;
    uint32_t lane_b_offset;
    int32_t shifted_offset;
    uint8_t starts_with_lane_b;

    if((batch == NULL) || (payload_bytes_per_packet == 0U) ||
       (max_packet_count == 0U) ||
       (XE_NP_App_IsUsbInterleavedCapture() == 0) ||
       (capture_a->storage == NULL) || (capture_b->storage == NULL) ||
       (capture_a->storage_bytes == 0U) ||
       (capture_a->storage_bytes != capture_b->storage_bytes))
    {
        xe_np_gpioe_interleaved_usb_export_debug = 1U;
        return -1;
    }
    /* A host may change MCO immediately before START.  That deliberately
     * restarts the two capture lanes, so their phase relationship is not
     * qualified yet.  This is a normal pending state, not an export fault:
     * keep USB armed and publish nothing until the independent lane-pair
     * watchdog has found a verified offset. */
    if(xe_np_gpioe_interleaved_validation_lane_b_offset_valid == 0U)
    {
        xe_np_gpioe_interleaved_usb_export_debug = 2U;
        return 0;
    }
    if(xe_np_gpioe_interleaved_usb_dma_batch_claimed != 0U)
    {
        xe_np_gpioe_interleaved_usb_export_debug = 8U;
        return 0;
    }

    memset(batch, 0, sizeof(*batch));
    {
        uint32_t primask = XE_NP_App_CriticalSection_Enter();
        total_a = capture_a->total_bytes_captured;
        total_b = capture_b->total_bytes_captured;
        XE_NP_App_CriticalSection_Exit(primask);
    }
    safe_end = (total_a < total_b) ? total_a : total_b;
    if(safe_end <= (uint64_t)(XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_VALIDATION_LANE_BYTES * 2U))
    {
        xe_np_gpioe_interleaved_usb_export_debug = 2U;
        return 0;
    }
    safe_end -= (uint64_t)(XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_VALIDATION_LANE_BYTES * 2U);
    lane_bytes = capture_a->storage_bytes;

    if(xe_np_gpioe_interleaved_usb_next_lane_byte_valid == 0U)
    {
        /* Do not retroactively drain qualification history.  The next call
         * claims only bytes that arrive after USB has asked to record. */
        xe_np_gpioe_interleaved_usb_next_lane_byte = safe_end;
        xe_np_gpioe_interleaved_usb_next_lane_byte_valid = 1U;
        xe_np_gpioe_interleaved_usb_next_lane_is_b = 0U;
    }
    cursor = xe_np_gpioe_interleaved_usb_next_lane_byte;
    if(safe_end <= cursor)
    {
        xe_np_gpioe_interleaved_usb_export_debug = 4U;
        return 0;
    }
    if((safe_end - cursor) >= (uint64_t)(lane_bytes -
                                         (XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_VALIDATION_LANE_BYTES * 2U)))
    {
        xe_np_gpioe_interleaved_usb_export_debug = 5U;
        /* No committed USB byte exists yet, so an old/late cursor is only
         * pre-recording history.  Re-anchor at the guarded producer rather
         * than turning normal phase qualification into a fatal loss report.
         * After the first committed batch this remains a hard fault: a wrap
         * would then mean emitted data can no longer be proven continuous. */
        if(xe_np_gpioe_interleaved_usb_dma_batch_committed != 0U)
        {
            return -1;
        }
        /* Reserve enough finished lane bytes to build the requested batch in
         * this same foreground visit.  Re-anchoring exactly at safe_end then
         * returning made the first usable bytes arrive only on the next
         * service pass, which can be later than a compact 30 kHz lane ring.
         */
        {
            /* A first batch that starts on lane A contains ceil(N / 2) A
             * packets and floor(N / 2) B packets.  Retain the larger count
             * from each producer so a full odd-sized USB batch is eligible
             * immediately instead of falling back to N - 1 packets. */
            uint32_t initial_lane_b_packets = (max_packet_count + 1U) / 2U;
            uint64_t initial_tail_bytes =
                (uint64_t)initial_lane_b_packets * payload_bytes_per_packet;

            if((initial_tail_bytes == 0ULL) || (safe_end <= initial_tail_bytes))
            {
                xe_np_gpioe_interleaved_usb_next_lane_byte = safe_end;
                xe_np_gpioe_interleaved_usb_next_lane_is_b = 0U;
                return 0;
            }
            cursor = safe_end - initial_tail_bytes;
            xe_np_gpioe_interleaved_usb_next_lane_byte = cursor;
            xe_np_gpioe_interleaved_usb_next_lane_is_b = 0U;
        }
    }

    starts_with_lane_b = xe_np_gpioe_interleaved_usb_next_lane_is_b;
    for(packet_count = max_packet_count; packet_count > 0U; --packet_count)
    {
        lane_a_packets = (starts_with_lane_b != 0U) ?
            (packet_count / 2U) : ((packet_count + 1U) / 2U);
        lane_b_packets = (starts_with_lane_b != 0U) ?
            ((packet_count + 1U) / 2U) : (packet_count / 2U);

        /* When a preceding batch ended with lane A, the first byte in this
         * batch is lane B for that same chronological position. */
        if((cursor + ((starts_with_lane_b != 0U) ? payload_bytes_per_packet : 0U) +
            ((uint64_t)lane_a_packets * payload_bytes_per_packet) > safe_end) ||
           (cursor + ((uint64_t)lane_b_packets * payload_bytes_per_packet) > safe_end))
        {
            continue;
        }

        lane_a_offset = (uint32_t)((cursor +
            ((starts_with_lane_b != 0U) ? payload_bytes_per_packet : 0U)) % lane_bytes);
        lane_b_offset = (uint32_t)(cursor % lane_bytes);
        shifted_offset = (int32_t)lane_b_offset + xe_np_gpioe_interleaved_validation_lane_b_offset;
        if(shifted_offset < 0)
        {
            shifted_offset += (int32_t)lane_bytes;
        }
        else if(shifted_offset >= (int32_t)lane_bytes)
        {
            shifted_offset -= (int32_t)lane_bytes;
        }
        lane_b_offset = (uint32_t)shifted_offset;

        if(((lane_a_packets == 0U) ||
            ((lane_a_offset + (lane_a_packets * payload_bytes_per_packet)) <= lane_bytes)) &&
           ((lane_b_packets == 0U) ||
            ((lane_b_offset + (lane_b_packets * payload_bytes_per_packet)) <= lane_bytes)))
        {
            break;
        }
    }
    if(packet_count == 0U)
    {
        /* A physical-ring wrap is pending.  Wait for a smaller contiguous
         * batch rather than falling back to a CPU memcpy across SRAM3. */
        xe_np_gpioe_interleaved_usb_export_debug = 6U;
        return 0;
    }

    batch->lane_a = (lane_a_packets != 0U) ? &capture_a->storage[lane_a_offset] : NULL;
    batch->lane_b = (lane_b_packets != 0U) ? &capture_b->storage[lane_b_offset] : NULL;
    batch->lane_a_packet_count = lane_a_packets;
    batch->lane_b_packet_count = lane_b_packets;
    batch->packet_count = packet_count;
    batch->payload_bytes_per_packet = payload_bytes_per_packet;
    batch->starts_with_lane_b = starts_with_lane_b;

    xe_np_gpioe_interleaved_usb_dma_batch_claimed = 1U;
    xe_np_gpioe_interleaved_usb_dma_batch_lane_b_packets = lane_b_packets;
    xe_np_gpioe_interleaved_usb_dma_batch_payload_bytes = payload_bytes_per_packet;
    xe_np_gpioe_interleaved_usb_export_debug = 9U;
    return 1;
#else
    (void)payload_bytes_per_packet;
    (void)max_packet_count;
    (void)batch;
    return -1;
#endif
}

int XE_NP_App_CommitUsbInterleavedDmaBatch(
    const XE_NP_AppUsbInterleavedDmaBatch *batch)
{
#if ((XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_ENABLE != 0U) && \
     (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_PUBLISH_ENABLE != 0U) && \
     (XE_NP_APP_EXPERIMENT_SYNC_WATCHDOG_ENABLE != 0U))
    if((batch == NULL) || (xe_np_gpioe_interleaved_usb_dma_batch_claimed == 0U) ||
       (batch->payload_bytes_per_packet != xe_np_gpioe_interleaved_usb_dma_batch_payload_bytes) ||
       (batch->lane_b_packet_count != xe_np_gpioe_interleaved_usb_dma_batch_lane_b_packets))
    {
        xe_np_gpioe_interleaved_usb_export_debug = 1U;
        return -1;
    }
    xe_np_gpioe_interleaved_usb_next_lane_byte +=
        (uint64_t)batch->lane_b_packet_count * batch->payload_bytes_per_packet;
    xe_np_gpioe_interleaved_usb_next_lane_is_b =
        (uint8_t)(batch->starts_with_lane_b ^ (batch->packet_count & 1U));
    xe_np_gpioe_interleaved_usb_dma_batch_claimed = 0U;
    xe_np_gpioe_interleaved_usb_dma_batch_committed = 1U;
    xe_np_gpioe_interleaved_usb_dma_batch_lane_b_packets = 0U;
    xe_np_gpioe_interleaved_usb_dma_batch_payload_bytes = 0U;
    xe_np_gpioe_interleaved_usb_export_debug = 7U;
    return 0;
#else
    (void)batch;
    return -1;
#endif
}

void XE_NP_App_CancelUsbInterleavedDmaBatch(void)
{
#if ((XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_ENABLE != 0U) && \
     (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_PUBLISH_ENABLE != 0U) && \
     (XE_NP_APP_EXPERIMENT_SYNC_WATCHDOG_ENABLE != 0U))
    xe_np_gpioe_interleaved_usb_dma_batch_claimed = 0U;
    xe_np_gpioe_interleaved_usb_dma_batch_lane_b_packets = 0U;
    xe_np_gpioe_interleaved_usb_dma_batch_payload_bytes = 0U;
#endif
}

uint32_t XE_NP_App_CopyUsbInterleavedPacketPayloads(
    uint8_t *packet_base,
    uint32_t packet_count,
    uint32_t packet_bytes,
    uint32_t header_bytes)
{
#if ((XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_ENABLE != 0U) && \
     (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_PUBLISH_ENABLE != 0U) && \
     (XE_NP_APP_EXPERIMENT_SYNC_WATCHDOG_ENABLE != 0U))
    XE_NP_CapturePort *capture_a = &xe_np_runtime.capture.gpioe;
    XE_NP_CapturePort *capture_b = &xe_np_runtime.capture.gpiod;
    uint64_t total_a;
    uint64_t total_b;
    uint64_t safe_end;
    uint64_t available_lane_bytes;
    uint32_t payload_bytes;
    uint32_t lane_bytes;
    uint32_t lane_count;
    uint32_t offset_a;
    uint32_t offset_b;
    uint32_t index;
    uint32_t packet_index;
    uint32_t packet_payload_remaining;
    uint8_t *packet_payload;
    int32_t shifted_offset_b;
    uint32_t primask;

    if((packet_base == NULL) || (packet_count == 0U) ||
       (packet_bytes <= header_bytes) ||
       (((packet_bytes - header_bytes) & 1U) != 0U) ||
       (packet_count > (UINT32_MAX / (packet_bytes - header_bytes))) ||
       (XE_NP_App_IsUsbInterleavedCapture() == 0) ||
       (xe_np_gpioe_interleaved_validation_lane_b_offset_valid == 0U) ||
       (capture_a->storage == NULL) || (capture_b->storage == NULL) ||
       (capture_a->storage_bytes == 0U) ||
       (capture_a->storage_bytes != capture_b->storage_bytes))
    {
        xe_np_gpioe_interleaved_usb_export_debug = 1U;
        return 0U;
    }

    payload_bytes = packet_count * (packet_bytes - header_bytes);
    if((payload_bytes < 2U) || ((payload_bytes & 1U) != 0U))
    {
        xe_np_gpioe_interleaved_usb_export_debug = 1U;
        return 0U;
    }
    lane_count = payload_bytes / 2U;

    primask = XE_NP_App_CriticalSection_Enter();
    total_a = capture_a->total_bytes_captured;
    total_b = capture_b->total_bytes_captured;
    XE_NP_App_CriticalSection_Exit(primask);
    safe_end = (total_a < total_b) ? total_a : total_b;
    if(safe_end <= (uint64_t)(XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_VALIDATION_LANE_BYTES * 2U))
    {
        xe_np_gpioe_interleaved_usb_export_debug = 2U;
        return 0U;
    }
    safe_end -= (uint64_t)(XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_VALIDATION_LANE_BYTES * 2U);

    if(xe_np_gpioe_interleaved_usb_next_lane_byte_valid == 0U)
    {
        /* The first private USB transfer must fit wholly behind the paired
         * DMA guard.  Starting only one validation window behind the writer
         * can leave less than one transfer available; retrying then reaches
         * a ring wrap and produces a false overrun. */
        xe_np_gpioe_interleaved_usb_next_lane_byte =
            (safe_end > lane_count) ?
                (safe_end - lane_count) :
                0ULL;
        xe_np_gpioe_interleaved_usb_next_lane_byte_valid = 1U;
        xe_np_gpioe_interleaved_usb_export_debug = 3U;
    }
    if(safe_end <= xe_np_gpioe_interleaved_usb_next_lane_byte)
    {
        xe_np_gpioe_interleaved_usb_export_debug = 4U;
        return 0U;
    }

    available_lane_bytes = safe_end - xe_np_gpioe_interleaved_usb_next_lane_byte;
    lane_bytes = capture_a->storage_bytes;
    if(available_lane_bytes >= (uint64_t)(lane_bytes -
                                          (XE_NP_APP_GPIOE_DUAL_DMA_INTERLEAVE_VALIDATION_LANE_BYTES * 2U)))
    {
        xe_np_gpioe_interleaved_usb_next_lane_byte = safe_end;
        xe_np_gpioe_interleaved_usb_export_debug = 5U;
        return 0U;
    }

    /* A transfer is all-or-nothing: partial packetised transfers would leave
     * a stale cursor and conceal a producer/exporter timing failure. */
    if((uint64_t)lane_count > available_lane_bytes)
    {
        xe_np_gpioe_interleaved_usb_export_debug = 6U;
        return 0U;
    }

    offset_a = (uint32_t)(xe_np_gpioe_interleaved_usb_next_lane_byte % lane_bytes);
    shifted_offset_b = (int32_t)offset_a + xe_np_gpioe_interleaved_validation_lane_b_offset;
    if(shifted_offset_b < 0)
    {
        shifted_offset_b += (int32_t)lane_bytes;
    }
    else if(shifted_offset_b >= (int32_t)lane_bytes)
    {
        shifted_offset_b -= (int32_t)lane_bytes;
    }
    offset_b = (uint32_t)shifted_offset_b;

    /* The live SRAM3 lanes are invalidated exactly once each for a whole USB
     * transfer.  Endpoint DMA sees only the packet_base in private SRAM1. */
    XE_NP_App_InvalidateInterleavedGpioeLaneRange(capture_a, offset_a, lane_count);
    XE_NP_App_InvalidateInterleavedGpioeLaneRange(capture_b, offset_b, lane_count);
    packet_index = 0U;
    packet_payload_remaining = packet_bytes - header_bytes;
    packet_payload = &packet_base[header_bytes];
    for(index = 0U; index < lane_count; ++index)
    {
        packet_payload[0] = capture_a->storage[offset_a];
        packet_payload[1] = capture_b->storage[offset_b];
        packet_payload += 2U;
        packet_payload_remaining -= 2U;
        if(packet_payload_remaining == 0U)
        {
            packet_index++;
            if(packet_index < packet_count)
            {
                packet_payload = &packet_base[(packet_index * packet_bytes) + header_bytes];
                packet_payload_remaining = packet_bytes - header_bytes;
            }
        }
        if(++offset_a == lane_bytes)
        {
            offset_a = 0U;
        }
        if(++offset_b == lane_bytes)
        {
            offset_b = 0U;
        }
    }
    xe_np_gpioe_interleaved_usb_next_lane_byte += lane_count;
    xe_np_gpioe_interleaved_usb_export_debug = 7U;
    return payload_bytes;
#else
    (void)packet_base;
    (void)packet_count;
    (void)packet_bytes;
    (void)header_bytes;
    return 0U;
#endif
}

uint32_t XE_NP_App_GetUsbInterleavedExportDebug(void)
{
#if ((XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_INTERLEAVE_ENABLE != 0U) && \
     (XE_NP_APP_EXPERIMENT_GPIOE_QUAD_DMA_INTERLEAVE_USB_ENABLE != 0U))
    if(XE_NP_App_IsUsbQuadInterleavedCapture() != 0)
    {
        return xe_np_quad_dma_usb_export_debug;
    }
#endif
#if ((XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_ENABLE != 0U) && \
     (XE_NP_APP_EXPERIMENT_GPIOE_DUAL_DMA_INTERLEAVE_PUBLISH_ENABLE != 0U) && \
     (XE_NP_APP_EXPERIMENT_SYNC_WATCHDOG_ENABLE != 0U))
    return xe_np_gpioe_interleaved_usb_export_debug;
#else
    return 0U;
#endif
}

void XE_NP_App_ReleaseUsbActiveWindow(uint8_t port_index)
{
    XE_NP_AppDmaPort *port = XE_NP_App_GetDmaPortForUsb(port_index);
    uint32_t primask;

    if(port == NULL)
    {
        return;
    }

    primask = XE_NP_App_CriticalSection_Enter();
    if((port->usb_active_window_valid != 0U) &&
       (port->usb_active_window.size_bytes != 0U) &&
       (port->usb_active_window_consumed_bytes >= port->usb_active_window.size_bytes))
    {
        port->usb_active_window_valid = 0U;
        port->usb_active_window_consumed_bytes = 0U;
        memset(&port->usb_active_window, 0, sizeof(port->usb_active_window));
    }
    XE_NP_App_CriticalSection_Exit(primask);
}

uint32_t XE_NP_App_GetUsbWindowPendingBytes(uint8_t port_index)
{
    XE_NP_AppDmaPort *port = XE_NP_App_GetDmaPortForUsb(port_index);
    uint8_t read_idx;
    uint8_t write_idx;
    uint32_t pending_bytes = 0U;
    uint32_t primask;

    if((port == NULL) || (XE_NP_App_UsbWindowExportArmedForPort(port) == 0))
    {
        return 0U;
    }

    primask = XE_NP_App_CriticalSection_Enter();
    if((port->usb_active_window_valid != 0U) &&
       (port->usb_active_window_consumed_bytes < port->usb_active_window.size_bytes))
    {
        pending_bytes +=
            port->usb_active_window.size_bytes - port->usb_active_window_consumed_bytes;
    }
    read_idx = port->pending_read_idx;
    write_idx = port->pending_write_idx;
    while(read_idx != write_idx)
    {
        pending_bytes += port->pending_windows[read_idx].size_bytes;
        read_idx = (uint8_t)((read_idx + 1U) & XE_NP_APP_DMA_WINDOW_QUEUE_MASK);
    }
    XE_NP_App_CriticalSection_Exit(primask);

    return pending_bytes;
}

uint32_t XE_NP_App_GetUsbWindowPacketHeaderBytes(uint8_t port_index)
{
    XE_NP_AppDmaPort *port = XE_NP_App_GetDmaPortForUsb(port_index);

    if((port == NULL) || (port->usb_packet_headroom_enabled == 0U))
    {
        return 0U;
    }

    return port->usb_packet_header_bytes;
}

void XE_NP_App_GetUsbExportDiag(uint8_t port_index, XE_NP_AppUsbExportDiag *diag)
{
    XE_NP_AppDmaPort *port;
    uint32_t primask;

    if(diag == NULL)
    {
        return;
    }

    memset(diag, 0, sizeof(*diag));
    diag->capture_requested = xe_np_usb_capture_requested;
    diag->capture_port_index = xe_np_usb_capture_port_index;
    diag->export_enabled = xe_np_usb_window_export_enabled;
    diag->export_port_index = xe_np_usb_window_export_port_index;

    port = XE_NP_App_GetDmaPortForUsb(port_index);
    if(port == NULL)
    {
        return;
    }

    primask = XE_NP_App_CriticalSection_Enter();
    diag->port_active = port->active;
    diag->pending_read_idx = port->pending_read_idx;
    diag->pending_write_idx = port->pending_write_idx;
    diag->usb_active_window_valid = port->usb_active_window_valid;
    diag->stable_window_valid = port->stable_window_valid;
    diag->active_probe_mask =
        (port->capture_port != NULL) ? port->capture_port->active_probe_mask : 0U;
    XE_NP_App_CriticalSection_Exit(primask);
}

void XE_NP_App_GpioeDmaIrqHandler(void)
{
    if(xe_np_dma_gpioe.hdma.Instance != NULL)
    {
        XE_NP_AppDma_IrqHandlerFast(&xe_np_dma_gpioe);
    }
}

void XE_NP_App_GpiodDmaIrqHandler(void)
{
    if(xe_np_dma_gpiod.hdma.Instance != NULL)
    {
        XE_NP_AppDma_IrqHandlerFast(&xe_np_dma_gpiod);
    }
}

int XE_NP_App_SetProbeCalDacRaw(uint16_t dac_code)
{
    /*
     * CAL_SIGNAL is tied to the probe TEST input on this board. Reject
     * non-zero manual drive while a Neuropixels session is active so test
     * injection cannot corrupt live capture or recorder validation.
     */
    if((dac_code != 0U) && (XE_NP_App_NpSessionActive(&xe_np_runtime) != 0))
    {
        CE32_ReportError(&xe_np_runtime.sys_param, CE32_ERRORID_PARAM_ERR);
        return -1;
    }

    return XE_NP_App_SetProbeCalDacRawInternal(&xe_np_runtime, dac_code);
}

uint16_t XE_NP_App_GetProbeCalDacRaw(void)
{
    return xe_np_probe_cal_dac_code;
}

void XE_NP_App_SdStageDmaIrqHandler(void)
{
#if (XE_NP_APP_RAW_SD_DMA_STAGING_ENABLE != 0U)
    if(xe_np_raw_sd_sink.stage_initialized != 0U)
    {
        HAL_DMA_IRQHandler(&xe_np_raw_sd_sink.stage_dma);
    }
#endif
}

void XE_NP_App_SdLinkedListNodeComplete(SD_HandleTypeDef *hsd)
{
#if (XE_NP_APP_RAW_SD_LINKED_STREAM_ENABLE != 0U)
    if((hsd == xe_np_runtime.hsd_card) &&
       (xe_np_raw_sd_sink.linked_active != 0U))
    {
        /* The no-CMD12 recorder polls Btc in the foreground, but retain this
         * callback for diagnostic images that elect to enable SDMMC IRQs. */
#if (XE_NP_APP_RAW_SD_LINKED_RETARGET_ENABLE != 0U)
        if(xe_np_raw_sd_sink.linked_nodes_complete <
           (xe_np_raw_sd_sink.linked_node_count * 2U))
#else
        if(xe_np_raw_sd_sink.linked_nodes_complete <
           xe_np_raw_sd_sink.linked_node_count)
#endif
        {
            ++xe_np_raw_sd_sink.linked_nodes_complete;
        }
    }
#else
    (void)hsd;
#endif
}

void HAL_UART_TxCpltCallback(UART_HandleTypeDef *huart)
{
    if((huart == BLE_handle.COMM.huart) && (BLE_handle.COMM.UART != NULL))
    {
        (void)CE32_INTERCOM_TX_DMA_ISR((CE32_INTERCOM_Handle *)&BLE_handle);
    }
}

void HAL_UART_ErrorCallback(UART_HandleTypeDef *huart)
{
    CE32_INTERCOM_Handle *ble_ic = (CE32_INTERCOM_Handle *)&BLE_handle;

    if((huart == BLE_handle.COMM.huart) && (BLE_handle.COMM.UART != NULL) &&
       ((ble_ic->state & INTERCOM_STATE_TX_DMABUSY) != 0U))
    {
        CE32_INTERCOM_UART_TX_DMA_Disable(ble_ic);
        CE32_INTERCOM_TX_restart(ble_ic);
    }
}

void HAL_SD_TxCpltCallback(SD_HandleTypeDef *hsd)
{
    HD32_SD_TxCpltCallback(hsd);
#if (XE_NP_APP_RAW_SD_SINK_ENABLE != 0U)
    if((hsd == xe_np_runtime.hsd_card) && (xe_np_raw_sd_sink.write_in_flight != 0U))
    {
        xe_np_raw_sd_sink.write_complete = 1U;
    }
#endif
}

void HAL_SD_RxCpltCallback(SD_HandleTypeDef *hsd)
{
    HD32_SD_RxCpltCallback(hsd);
}

void HAL_SD_ErrorCallback(SD_HandleTypeDef *hsd)
{
    HD32_SD_ErrorCallback(hsd);
#if (XE_NP_APP_LEGACY_FS_ENABLE != 0U)
    XE_NP_LegacyFs_OnSdError();
#endif
#if (XE_NP_APP_RAW_SD_SINK_ENABLE != 0U)
    if((hsd == xe_np_runtime.hsd_card) && (xe_np_raw_sd_sink.write_in_flight != 0U))
    {
        xe_np_raw_sd_sink.write_error = 1U;
    }
#endif
}

unsigned NP_ConfigActiveMask(void){return np_mode_mask;}
