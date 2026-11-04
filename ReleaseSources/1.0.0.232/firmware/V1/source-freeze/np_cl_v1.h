#ifndef NP_CL_V1_H
#define NP_CL_V1_H

#include "np_cl_kernel.h"

enum {
    NP_CL_V1_LINEAR8 = 0,
    NP_CL_V1_SD_QUAD8,
    NP_CL_V1_SD_QUAD16,
    NP_CL_V1_USB_QUAD8,
    NP_CL_V1_USB_QUAD16
};
enum {
    NP_CL_V1_AP_15K = 15000U,
    NP_CL_V1_AP_20K = 20000U,
    NP_CL_V1_TICKS_PER_AP = 780U,
    NP_CL_V1_SOURCE_STEP = 9360U
};

/* Internal private-view proof, never a kernel-input flag or wire allocation.
 * Acquisition alone may attach it for a fresh REC_NRESET in this capture
 * generation/probe mask, before 20-bit counter half-epoch ambiguity. */
#define NP_CL_V1_RESET_VALID 0x100U

/* A completed, CPU-visible, privately owned source batch. The caller must
 * attest an exact first_tick in source_epoch, not infer elapsed samples from
 * a USB batch count after an unknown source loss. Flags use NP_CL_INPUT_*;
 * VALID means source anchor and ownership/continuity are known. Initial phase
 * admission additionally requires acquisition-owned RESET_VALID proof.
 * No pointer is retained. Kernel/config changes require decoder re-init. */
typedef struct {
    const uint8_t *data;
    uint64_t first_tick;
    uint32_t bytes, ticks, layout, probe, source_epoch, flags;
} NpClV1View;

/* One selected native LFP channel. Allocate one or two independent instances;
 * no state is allocated here, and no live neural/sensor buffer is borrowed.
 * The 59-byte tail only completes a selected 60-tick frame across batches. */
typedef struct {
    NpClLane *lane;
    uint64_t expected_first, next_frame, available_tick;
    uint32_t session, probe, channel, source_epoch;
    uint32_t phase, counter_origin, fixed, word, bit;
    uint32_t phase_valid, have_end, tail_size, initialized;
    uint32_t samples, invalid_frames, skipped_batches;
    uint8_t tail[59];
} NpClV1Decoder;

/* 15 and 20 kHz AP supply native LFP every 9360 source ticks.
 * The detector uses the native sample values and ticks directly. */
int np_cl_v1_init(NpClV1Decoder *decoder, NpClLane *lane, uint32_t source_epoch);

/* Startup-only shortcut for an acquisition-owned, strict framing/origin proof
 * over THIS SAME completed private view, source epoch, probe and unchanged
 * configuration. Never supply a preview, live-ring or merely plausible phase.
 * The owner must have validated framing/counter continuity; this function does
 * not repeat that scan or independently prove the supplied counter. It checks
 * the private view, exact VALID|RESET_VALID flags, pristine decoder, startup
 * bounds and the fixed word (10 source-byte reads), then installs the same
 * phase/counter/native-slot state as the ordinary origin search. No source or
 * capture ownership/counter is changed. Call tap on this same view afterward.
 * Returns1 on success,-1 on rejection without changing decoder/lane state.
 * Unseeded tap behavior is unchanged. */
int np_cl_v1_seed(NpClV1Decoder *decoder, const NpClV1View *view,
                  uint32_t first_phase, uint32_t counter);

/* Foreground-only, after recording/transport priority work. Bound: <=61440
 * ticks and <=65536 private bytes per call; at most7 selected LFP samples.
 * Phase search requires >=1560 ticks and fresh RESET_VALID proof. Contiguous
 * later private batches may retry after no sync; an actual late first view is
 * allowed only with that same acquisition proof, never a preview-derived
 * anchor. A proven first superframe counter must be divisible by13 and below
 * 0x80000. Its native LFP slot is (counter/13)%12, not necessarily slot0.
 * An absolute startup origin must also be below 0x80000*60 source ticks.
 * Already-locked continuous phase does not require proof after half-epoch.
 *
 * Each selected60-tick frame is checked for sync/fixed/counter/fill/heartbeat;
 * structural checks cannot detect every electrical error in neural data bits.
 * Any invalid sample/gap/pressure resets training, never delays recording.
 * available_tick is the current private batch's last source tick, NOT a CPU
 * event timestamp. The sink must separately tag epoch/config and decision
 * timing, enqueue without waiting, and discard a cancelled arm generation.
 *
 * Returns processed sample count,0 for a discarded/no-sample batch,-1 for an
 * invalid view/config. Missing reset proof, ambiguous startup counter or a
 * noncontiguous pre-lock retry disarms optional decoding until re-init. */
int np_cl_v1_tap(NpClV1Decoder *decoder, const NpClV1View *view);

/* Same decoder with a caller-local source-byte read budget, including startup
 * scans, selected-frame validation and view/tail reads. max_reads==0 means
 * unlimited (the compatibility tap above). Expiry emits a PRESSURE GAP,
 * discards this optional batch and disables this decoder until re-init; it
 * never writes the source or changes recording. Earlier optional samples in
 * that batch may precede the GAP. Origin-not-found instead emits INVALID and
 * leaves the decoder initialized/untrained, with exact expected_first/end
 * retained for a contiguous proven retry. Missing/ambiguous proof is distinct
 * from no-sync and cannot produce valid detector outputs.
 *
 * Native tests may define NP_CL_V1_TEST_READS and supply
 * void np_cl_v1_test_read(void), called once per actual source-byte read. This
 * hook has no production state/call overhead when the define is absent. This
 * budget bounds source reads, not CPU cycles or interrupt-inclusive WCET. */
int np_cl_v1_tap_bounded(NpClV1Decoder *decoder, const NpClV1View *view,
                         uint32_t max_reads);

#endif
