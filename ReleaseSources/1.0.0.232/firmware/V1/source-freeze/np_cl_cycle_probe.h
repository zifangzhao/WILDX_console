#ifndef NP_CL_CYCLE_PROBE_H
#define NP_CL_CYCLE_PROBE_H

#include <stdint.h>

/* Candidate instrumentation only. Enabling this does not enable/reset DWT,
 * alter clocks, mask interrupts, allocate storage, or perform any I/O. */
#ifndef NP_CL_CYCLE_PROBE
#define NP_CL_CYCLE_PROBE 0
#endif

#define NP_CL_CYCLE_LANES 2U
#define NP_CL_CYCLE_MAX_MS 100U
#define NP_CL_CYCLE_INVALID_PORT 1U
#define NP_CL_CYCLE_INVALID_CLOCK 2U
#define NP_CL_CYCLE_INVALID_SPAN 4U
#define NP_CL_CYCLE_INVALID_ALIAS 8U
#define NP_CL_CYCLE_INVALID_SUBTRACTION 16U
#define NP_CL_CYCLE_INVALID_ARGUMENT 32U
#define NP_CL_CYCLE_SATURATED 64U

typedef struct {
    uint64_t kernel_total, decode_total, service_total;
    uint32_t kernel_calls, samples, services, max_service, invalid_spans;
} NpClCycleLane;

/* 128 bytes with 8-byte uint64_t alignment. Caller owns initialization/lifetime;
 * zero once per recording, freeze after Stop, inspect RAM outside acquisition.
 * valid_samples/invalid_samples count service observations only, not nested
 * kernel spans twice. They are instrumentation counts, not recording losses. */
typedef struct {
    NpClCycleLane lane[NP_CL_CYCLE_LANES];
    uint64_t bridge_total;
    uint32_t bridge_calls, max_bridge;
    uint32_t core_hz, flags, valid_samples, invalid_samples;
} NpClCycleState;

typedef struct {
    uint64_t kernel_total, bridge_invalid_total;
    uint32_t counter, ms, hz, valid, invalid_spans, flags;
} NpClCycleStamp;

/* Supplied by the candidate port. Valid must reject disabled/non-DWT/debug
 * proxies or clocks lacking a trustworthy coarse-time witness. No helper here
 * assumes it can discover an arbitrary debugger halt with both clocks frozen.
 * StateGet may expose storage only while recording; direct state is passed to
 * helpers so the caller can retain the final frozen metrics after Stop. */
NpClCycleState *NP_ClCycleStateGet(void);
uint32_t NP_ClCycleCounter(void);
uint32_t NP_ClCycleMs(void);
uint32_t NP_ClCycleHz(void);
uint32_t NP_ClCycleValid(void);

#if NP_CL_CYCLE_PROBE

static inline void np_cl_cycle_add32(NpClCycleState *s, uint32_t *value, uint32_t n)
{
    if (n > UINT32_MAX - *value) {
        *value = UINT32_MAX;
        s->flags |= NP_CL_CYCLE_SATURATED;
    } else *value += n;
}

static inline void np_cl_cycle_add64(NpClCycleState *s, uint64_t *value, uint64_t n)
{
    if (n > UINT64_MAX - *value) {
        *value = UINT64_MAX;
        s->flags |= NP_CL_CYCLE_SATURATED;
    } else *value += n;
}

static inline NpClCycleStamp np_cl_cycle_begin(NpClCycleState *s, uint32_t lane)
{
    NpClCycleStamp stamp = {0};
    if (s == 0) return stamp;
    if (lane >= NP_CL_CYCLE_LANES) {
        s->flags |= NP_CL_CYCLE_INVALID_ARGUMENT;
        return stamp;
    }
    stamp.kernel_total = s->lane[lane].kernel_total;
    stamp.invalid_spans = s->lane[lane].invalid_spans;
    if (!NP_ClCycleValid()) stamp.flags |= NP_CL_CYCLE_INVALID_PORT;
    stamp.hz = NP_ClCycleHz();
    stamp.ms = NP_ClCycleMs();
    stamp.counter = NP_ClCycleCounter();
    if (!NP_ClCycleValid()) stamp.flags |= NP_CL_CYCLE_INVALID_PORT;
    if (stamp.hz == 0U || (s->core_hz != 0U && s->core_hz != stamp.hz))
        stamp.flags |= NP_CL_CYCLE_INVALID_CLOCK;
    if (stamp.flags == 0U) {
        if (s->core_hz == 0U) s->core_hz = stamp.hz;
        stamp.valid = 1U;
    }
    s->flags |= stamp.flags;
    return stamp;
}

static inline uint32_t np_cl_cycle_measure(NpClCycleState *s, NpClCycleStamp stamp,
                                           uint32_t *cycles)
{
    uint32_t hz, ms, now, elapsed_ms, flags = stamp.flags;
    uint64_t lower, upper;
    if (!stamp.valid) return flags != 0U ? flags : NP_CL_CYCLE_INVALID_ARGUMENT;
    if (!NP_ClCycleValid()) flags |= NP_CL_CYCLE_INVALID_PORT;
    hz = NP_ClCycleHz();
    ms = NP_ClCycleMs();
    now = NP_ClCycleCounter();
    if (!NP_ClCycleValid()) flags |= NP_CL_CYCLE_INVALID_PORT;
    if (hz == 0U || hz != stamp.hz || hz != s->core_hz)
        flags |= NP_CL_CYCLE_INVALID_CLOCK;
    elapsed_ms = ms - stamp.ms;
    if (elapsed_ms > NP_CL_CYCLE_MAX_MS) flags |= NP_CL_CYCLE_INVALID_SPAN;
    *cycles = now - stamp.counter;
    if (flags == 0U) {
        /* Unsigned one-wrap DWT difference is admissible only inside this
         * <=100ms window. Millisecond quantization permits +/-1ms. Both
         * bounds detect reset/halted/proxy counters where observable; they do
         * not claim unbounded wall time or more-than-one-wrap recovery. */
        lower = elapsed_ms > 1U ? (uint64_t)(elapsed_ms - 1U) * hz / 1000U : 0U;
        upper = ((uint64_t)(elapsed_ms + 1U) * hz + 999U) / 1000U;
        if (*cycles < lower || *cycles > upper) flags |= NP_CL_CYCLE_INVALID_ALIAS;
    }
    return flags;
}

static inline void np_cl_cycle_invalid(NpClCycleState *s, uint32_t lane,
                                       uint32_t flags, uint32_t processed,
                                       uint32_t service)
{
    s->flags |= flags;
    np_cl_cycle_add32(s, &s->lane[lane].invalid_spans, 1U);
    if (service) np_cl_cycle_add32(s, &s->invalid_samples, processed);
}

/* Kernel/service times include interrupts and the counter/harness read costs.
 * Never run these helpers under an IRQ mask to manufacture a faster result. */
static inline void np_cl_cycle_kernel_end(NpClCycleState *s, uint32_t lane,
                                          NpClCycleStamp stamp, uint32_t processed)
{
    uint32_t cycles = 0U, flags;
    if (s == 0) return;
    if (lane >= NP_CL_CYCLE_LANES) { s->flags |= NP_CL_CYCLE_INVALID_ARGUMENT; return; }
    flags = np_cl_cycle_measure(s, stamp, &cycles);
    if (flags) { np_cl_cycle_invalid(s, lane, flags, processed, 0U); return; }
    np_cl_cycle_add64(s, &s->lane[lane].kernel_total, cycles);
    np_cl_cycle_add32(s, &s->lane[lane].kernel_calls, 1U);
    np_cl_cycle_add32(s, &s->lane[lane].samples, processed);
}

/* Nest kernel spans inside exactly one service span per lane. The difference
 * is exclusive decode/service overhead, not a claim about interrupt-free CPU
 * execution. Failed nested measurements invalidate subtraction, not recording. */
static inline void np_cl_cycle_service_end(NpClCycleState *s, uint32_t lane,
                                           NpClCycleStamp stamp, uint32_t processed)
{
    uint32_t cycles = 0U, flags;
    uint64_t kernel;
    NpClCycleLane *m;
    if (s == 0) return;
    if (lane >= NP_CL_CYCLE_LANES) { s->flags |= NP_CL_CYCLE_INVALID_ARGUMENT; return; }
    m = &s->lane[lane];
    flags = np_cl_cycle_measure(s, stamp, &cycles);
    if (m->invalid_spans != stamp.invalid_spans || m->invalid_spans == UINT32_MAX ||
        m->kernel_total < stamp.kernel_total || m->kernel_total == UINT64_MAX)
        flags |= NP_CL_CYCLE_INVALID_SUBTRACTION;
    kernel = m->kernel_total - stamp.kernel_total;
    if (kernel > cycles) flags |= NP_CL_CYCLE_INVALID_SUBTRACTION;
    if (flags) { np_cl_cycle_invalid(s, lane, flags, processed, 1U); return; }
    np_cl_cycle_add64(s, &m->service_total, cycles);
    np_cl_cycle_add64(s, &m->decode_total, cycles - kernel);
    np_cl_cycle_add32(s, &m->services, 1U);
    np_cl_cycle_add32(s, &s->valid_samples, processed);
    if (cycles > m->max_service) m->max_service = cycles;
}

/* Separate aggregate for a whole optional bridge, including packing/CRC/cache
 * work in its caller-selected scope. It does not add kernel/service/sample
 * counters again. Do not include the existing IRQ-guarded journal commit in
 * this measurement or move that commit to obtain an instrumentation number. */
static inline NpClCycleStamp np_cl_cycle_bridge_begin(NpClCycleState *s)
{
    NpClCycleStamp stamp = np_cl_cycle_begin(s, 0U);
    if (s != 0) stamp.bridge_invalid_total =
        (uint64_t)s->lane[0].invalid_spans + s->lane[1].invalid_spans;
    return stamp;
}

static inline void np_cl_cycle_bridge_end(NpClCycleState *s, NpClCycleStamp stamp)
{
    uint32_t cycles = 0U, flags;
    uint64_t invalid_total;
    if (s == 0) return;
    flags = np_cl_cycle_measure(s, stamp, &cycles);
    invalid_total = (uint64_t)s->lane[0].invalid_spans + s->lane[1].invalid_spans;
    if (invalid_total != stamp.bridge_invalid_total ||
        s->lane[0].invalid_spans == UINT32_MAX || s->lane[1].invalid_spans == UINT32_MAX)
        flags |= NP_CL_CYCLE_INVALID_SUBTRACTION;
    if (flags) { s->flags |= flags; return; }
    np_cl_cycle_add64(s, &s->bridge_total, cycles);
    np_cl_cycle_add32(s, &s->bridge_calls, 1U);
    if (cycles > s->max_bridge) s->max_bridge = cycles;
}

#else

static inline NpClCycleStamp np_cl_cycle_begin(NpClCycleState *s, uint32_t lane)
{
    NpClCycleStamp stamp = {0};
    (void)s; (void)lane;
    return stamp;
}
static inline void np_cl_cycle_kernel_end(NpClCycleState *s, uint32_t lane,
                                          NpClCycleStamp stamp, uint32_t processed)
{ (void)s; (void)lane; (void)stamp; (void)processed; }
static inline void np_cl_cycle_service_end(NpClCycleState *s, uint32_t lane,
                                           NpClCycleStamp stamp, uint32_t processed)
{ (void)s; (void)lane; (void)stamp; (void)processed; }
static inline NpClCycleStamp np_cl_cycle_bridge_begin(NpClCycleState *s)
{
    NpClCycleStamp stamp = {0};
    (void)s;
    return stamp;
}
static inline void np_cl_cycle_bridge_end(NpClCycleState *s, NpClCycleStamp stamp)
{ (void)s; (void)stamp; }

#endif
#endif
