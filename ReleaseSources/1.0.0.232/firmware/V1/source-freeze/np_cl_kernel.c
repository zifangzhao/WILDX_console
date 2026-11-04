#include "np_cl_kernel.h"
#include "np_cl_cycle_probe.h"

#include <math.h>
#include <string.h>

static void increment(uint32_t *count)
{
    if (*count != UINT32_MAX) ++*count;
}

static int rate_valid(NpClRate rate)
{
    return rate.num != 0U && rate.den != 0U;
}

static int rate_equal(NpClRate a, NpClRate b)
{
    return (uint64_t)a.num * b.den == (uint64_t)b.num * a.den;
}

static int rate_step_valid(const NpClConfig *config)
{
    /* Two 32-bit factors fit uint64_t. Divide before comparing source_step,
     * avoiding an overflowing source_step * numerator * denominator product. */
    uint64_t numerator = (uint64_t)config->source_rate.num * config->sample_rate.den;
    uint64_t denominator = (uint64_t)config->source_rate.den * config->sample_rate.num;
    return config->source_step != 0U && numerator % denominator == 0U &&
           numerator / denominator == config->source_step;
}

static uint64_t milliseconds_to_samples(NpClRate rate, uint32_t ms)
{
    return ((uint64_t)rate.num * ms) / ((uint64_t)rate.den * 1000U);
}

static int sections_valid(const NpClSection *sections, uint32_t count)
{
    uint32_t i, j;
    if (sections == NULL || count == 0U || count > NP_CL_MAX_SECTIONS) return 0;
    for (i = 0U; i < count; ++i) {
        const NpClSection *s = &sections[i];
        if (s->taps < 1U || s->taps > 3U || s->a[0] != 1.0f) return 0;
        for (j = 0U; j < 3U; ++j) {
            if (!isfinite(s->a[j]) || !isfinite(s->b[j])) return 0;
            if (j >= s->taps && (s->a[j] != 0.0f || s->b[j] != 0.0f)) return 0;
        }
        if (s->taps == 2U && fabsf(s->a[1]) >= 1.0f) return 0;
        /* Strict Jury conditions for normalized real second-order sections.
         * Use double only for admission so near-unit float poles are not
         * rejected through cancellation in the coefficient check. */
        if (s->taps == 3U &&
            (fabsf(s->a[2]) >= 1.0f ||
             1.0 + (double)s->a[1] + s->a[2] <= 0.0 ||
             1.0 - (double)s->a[1] + s->a[2] <= 0.0)) return 0;
    }
    return 1;
}

static void emit(NpClLane *lane, uint64_t tick, uint32_t kind, float input)
{
    NpClEvent event;
    event.source_tick = tick;
    event.session = lane->config.session;
    event.probe = lane->config.probe;
    event.channel = lane->config.channel;
    event.lane = lane->config.lane;
    event.kind = kind;
    event.input = input;
    event.envelope = lane->envelope;
    event.threshold = lane->threshold;
    if (!lane->sink(lane->sink_context, &event)) increment(&lane->event_dropped);
}

static void clear_detection(NpClLane *lane)
{
    memset(&lane->main_state, 0, sizeof(lane->main_state));
    memset(&lane->envelope_state, 0, sizeof(lane->envelope_state));
    lane->training_count = 0U;
    lane->training_mean = 0.0f;
    lane->envelope = 0.0f;
    lane->threshold = 0.0f;
    lane->state = 0U;
    lane->last_tick = 0U;
    lane->have_tick = 0U;
}

static void gap(NpClLane *lane, uint64_t tick, uint32_t reason)
{
    clear_detection(lane);
    lane->last_gap_reason = reason;
    increment(&lane->gap_count);
    emit(lane, tick, NP_CL_EVENT_GAP, 0.0f);
}

int np_cl_init(NpClLane *lane, const NpClConfig *config,
               const NpClCoefficients *coefficients, NpClSink sink, void *context)
{
    uint64_t start, duration;
    NpClConfig config_copy;
    NpClCoefficients coefficients_copy;
    if (lane == NULL) return NP_CL_ERROR_ARGUMENT;
    if (config == NULL || coefficients == NULL || sink == NULL) {
        memset(lane, 0, sizeof(*lane));
        return NP_CL_ERROR_ARGUMENT;
    }
    /* Reinitialization may use this lane's observable descriptor fields. */
    config_copy = *config;
    coefficients_copy = *coefficients;
    config = &config_copy;
    coefficients = &coefficients_copy;
    memset(lane, 0, sizeof(*lane));
    if (config->rectify > 1U || !isfinite(config->threshold_gain) ||
        config->threshold_gain < 0.0f) return NP_CL_ERROR_ARGUMENT;
    if (!rate_valid(config->sample_rate) || !rate_valid(config->source_rate) ||
        !rate_valid(coefficients->rate) ||
        !rate_equal(config->sample_rate, coefficients->rate) ||
        !rate_step_valid(config)) return NP_CL_ERROR_RATE;
    if (!sections_valid(coefficients->main, coefficients->main_count) ||
        !sections_valid(coefficients->envelope, coefficients->envelope_count))
        return NP_CL_ERROR_COEFFICIENTS;
    start = milliseconds_to_samples(config->sample_rate, config->training_start_ms);
    duration = milliseconds_to_samples(config->sample_rate, config->training_duration_ms);
    if (duration == 0U || start > UINT64_MAX - duration)
        return NP_CL_ERROR_TRAINING;
    lane->config = *config;
    lane->coefficients = *coefficients;
    lane->sink = sink;
    lane->sink_context = context;
    lane->training_start = start;
    lane->training_samples = duration;
    lane->initialized = 1U;
    return NP_CL_OK;
}

void np_cl_reset(NpClLane *lane, uint32_t reason)
{
    if (lane != NULL && lane->initialized != 0U)
        gap(lane, lane->have_tick ? lane->last_tick : 0U,
            reason != 0U ? reason : NP_CL_REASON_RESET);
}

static float filter(const NpClSection *sections, uint32_t count,
                    NpClFilterState *state, float value)
{
    uint32_t i, j;
    /* Deliberately retain CE64 DF_IIR_inputData's multiply/add order and ring
     * chronology, including one-tap gain rows; do not substitute DF2 here. */
    for (i = 0U; i < count; ++i) {
        float old = value;
        int ptr = state->ptr[i];
        value = sections[i].b[0] * value;
        for (j = 1U; j < sections[i].taps; ++j) {
            value += sections[i].b[j] * state->x[i][ptr] -
                     sections[i].a[j] * state->y[i][ptr];
            if (--ptr < 0) ptr += (int)sections[i].taps;
        }
        if (!isfinite(value)) return value;
        if (++state->ptr[i] >= sections[i].taps) state->ptr[i] = 0U;
        state->x[i][state->ptr[i]] = old;
        state->y[i][state->ptr[i]] = value;
    }
    return value;
}

static int np_cl_input_inner(NpClLane *lane, const NpClInput *input)
{
    float value;
    uint32_t reason = 0U;
    if (lane == NULL || input == NULL || lane->initialized == 0U)
        return NP_CL_ERROR_ARGUMENT;
    if (input->flags != NP_CL_INPUT_VALID) {
        if (input->flags & NP_CL_INPUT_FAULT) reason = NP_CL_REASON_FAULT;
        else if (input->flags & NP_CL_INPUT_PRESSURE) reason = NP_CL_REASON_PRESSURE;
        else if (input->flags & NP_CL_INPUT_DISCONTINUITY) reason = NP_CL_REASON_DISCONTINUITY;
        else reason = NP_CL_REASON_INVALID;
    } else if (!isfinite(input->value)) reason = NP_CL_REASON_NONFINITE;
    else if (lane->have_tick &&
             (lane->last_tick > UINT64_MAX - lane->config.source_step ||
              input->source_tick != lane->last_tick + lane->config.source_step))
        reason = NP_CL_REASON_TICK;
    if (reason != 0U) {
        gap(lane, input->source_tick, reason);
        return NP_CL_SKIPPED;
    }
    lane->last_tick = input->source_tick;
    lane->have_tick = 1U;
    value = filter(lane->coefficients.main, lane->coefficients.main_count,
                   &lane->main_state, input->value);
    if (lane->config.rectify) value = fabsf(value);
    lane->envelope = filter(lane->coefficients.envelope,
                            lane->coefficients.envelope_count,
                            &lane->envelope_state, value);
    if (!isfinite(value) || !isfinite(lane->envelope)) {
        gap(lane, input->source_tick, NP_CL_REASON_NONFINITE);
        return NP_CL_SKIPPED;
    }
    if (!(lane->state & NP_CL_STATE_READY)) {
        if (lane->state & NP_CL_STATE_TRAINING) {
            if (lane->training_count < lane->training_start + lane->training_samples) {
                lane->training_mean += lane->envelope / (float)lane->training_samples;
                if (!isfinite(lane->training_mean)) {
                    gap(lane, input->source_tick, NP_CL_REASON_NONFINITE);
                    return NP_CL_SKIPPED;
                }
            } else {
                lane->threshold = lane->training_mean * lane->config.threshold_gain;
                if (!isfinite(lane->threshold)) {
                    gap(lane, input->source_tick, NP_CL_REASON_NONFINITE);
                    return NP_CL_SKIPPED;
                }
                lane->state = NP_CL_STATE_READY;
                emit(lane, input->source_tick, NP_CL_EVENT_READY, input->value);
            }
        } else if (lane->training_count >= lane->training_start) {
            lane->training_mean = 0.0f;
            lane->state = NP_CL_STATE_TRAINING;
            emit(lane, input->source_tick, NP_CL_EVENT_TRAINING, input->value);
        }
        ++lane->training_count;
    } else if (!(lane->state & NP_CL_STATE_HIGH)) {
        if (lane->envelope > lane->threshold) {
            lane->state |= NP_CL_STATE_HIGH;
            emit(lane, input->source_tick, NP_CL_EVENT_RISE, input->value);
        }
    } else if (lane->envelope < lane->threshold) {
        lane->state &= ~NP_CL_STATE_HIGH;
        emit(lane, input->source_tick, NP_CL_EVENT_FALL, input->value);
    }
    return NP_CL_OK;
}
int np_cl_input(NpClLane *lane,const NpClInput *input)
{
#if NP_CL_CYCLE_PROBE
    uint32_t index=lane?lane->config.lane:NP_CL_CYCLE_LANES;
    NpClCycleState *state=index<NP_CL_CYCLE_LANES?NP_ClCycleStateGet():0;
    NpClCycleStamp stamp=np_cl_cycle_begin(state,index);
    int result=np_cl_input_inner(lane,input);
    np_cl_cycle_kernel_end(state,index,stamp,result==NP_CL_OK?1U:0U);
    return result;
#else
    return np_cl_input_inner(lane,input);
#endif
}
