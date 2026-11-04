#ifndef NP_CL_KERNEL_H
#define NP_CL_KERNEL_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* Portable, event-only CE64 SINGLE/DOUBLE envelope lane. These constants are
 * local API values, not wire IDs. Use separate lanes for DOUBLE operation. */
/* Standard CE64 envelope presets need gain, active SOS, and unity rows only.
 * Custom/Hilbert filters are not part of this initial portable adapter. */
#define NP_CL_MAX_SECTIONS 3U
#define NP_CL_INPUT_VALID 1U
#define NP_CL_INPUT_DISCONTINUITY 2U
#define NP_CL_INPUT_PRESSURE 4U
#define NP_CL_INPUT_FAULT 8U

enum {
    NP_CL_EVENT_TRAINING = 1,
    NP_CL_EVENT_READY,
    NP_CL_EVENT_RISE,
    NP_CL_EVENT_FALL,
    NP_CL_EVENT_GAP
};

enum {
    NP_CL_REASON_RESET = 1,
    NP_CL_REASON_INVALID,
    NP_CL_REASON_DISCONTINUITY,
    NP_CL_REASON_PRESSURE,
    NP_CL_REASON_FAULT,
    NP_CL_REASON_TICK,
    NP_CL_REASON_NONFINITE
};

enum {
    NP_CL_OK = 1,
    NP_CL_SKIPPED = 0,
    NP_CL_ERROR_ARGUMENT = -1,
    NP_CL_ERROR_RATE = -2,
    NP_CL_ERROR_COEFFICIENTS = -3,
    NP_CL_ERROR_TRAINING = -4
};

#define NP_CL_STATE_TRAINING 1U
#define NP_CL_STATE_READY 2U
#define NP_CL_STATE_HIGH 4U

typedef struct { uint32_t num, den; } NpClRate;
typedef struct { float b[3], a[3]; uint32_t taps; } NpClSection;
typedef struct {
    NpClRate rate;
    const NpClSection *main, *envelope;
    uint32_t main_count, envelope_count;
} NpClCoefficients;
typedef struct {
    NpClRate sample_rate, source_rate;
    uint64_t source_step;
    uint32_t session, probe, channel, lane;
    uint32_t training_start_ms, training_duration_ms;
    float threshold_gain;
    uint32_t rectify;
} NpClConfig;
typedef struct {
    uint64_t source_tick;
    float value;
    uint32_t flags;
} NpClInput;
typedef struct {
    uint64_t source_tick;
    uint32_t session, probe, channel, lane, kind;
    float input, envelope, threshold;
} NpClEvent;

/* Bounded, nonblocking enqueue only: 1 accepted, 0 full. Never perform I/O or
 * recursively call this lane from a sink. A full sink only increments drops. */
typedef int (*NpClSink)(void *context, const NpClEvent *event);

typedef struct {
    float x[NP_CL_MAX_SECTIONS][3], y[NP_CL_MAX_SECTIONS][3];
    uint8_t ptr[NP_CL_MAX_SECTIONS];
} NpClFilterState;

/* Caller-allocated state. Fields are observable diagnostics, not setters.
 * Coefficient arrays must remain const and alive for this lane's lifetime. */
typedef struct {
    NpClConfig config;
    NpClCoefficients coefficients;
    NpClSink sink;
    void *sink_context;
    NpClFilterState main_state, envelope_state;
    uint64_t training_start, training_samples, training_count, last_tick;
    float training_mean, envelope, threshold;
    uint32_t state, gap_count, event_dropped, last_gap_reason;
    uint32_t initialized, have_tick;
} NpClLane;

/* Rates are positive exact rationals; source_rate/sample_rate == source_step.
 * Sections use normalized a[0] == 1 and taps 1..3. No resampling is performed.
 * Training milliseconds are floored to sample counts; zero duration/count and
 * nonfinite/negative threshold gains are rejected. CE64's first training sample
 * starts, but does not enter, the mean.
 * init returns NP_CL_OK or a negative validation code, leaving a failed lane
 * disabled. Arrays are borrowed; config and coefficient descriptors are copied. */
int np_cl_init(NpClLane *lane, const NpClConfig *config,
               const NpClCoefficients *coefficients, NpClSink sink, void *context);

/* Reset clears filters/training/tick continuity and emits one GAP. Counters
 * survive reset and saturate. No FALL is synthesized across a gap. */
void np_cl_reset(NpClLane *lane, uint32_t reason);

/* Returns NP_CL_OK for one processed sample, NP_CL_SKIPPED for a gap sample,
 * or NP_CL_ERROR_ARGUMENT for a disabled/null lane. Zero/unknown flags,
 * discontinuity/pressure/fault, nonfinite values and non-exact tick steps reset
 * and discard that sample. The next clean sample begins fresh training. */
int np_cl_input(NpClLane *lane, const NpClInput *input);

#ifdef __cplusplus
}
#endif
#endif
