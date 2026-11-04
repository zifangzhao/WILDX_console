#include "np_cl_v1.h"
#include "np_cl_cycle_probe.h"

#include <stddef.h>
#include <string.h>

static void increment(uint32_t *n) { if (*n != UINT32_MAX) ++*n; }

static int same_rate(NpClRate a, uint32_t n, uint32_t d)
{ return (uint64_t)a.num * d == (uint64_t)n * a.den; }

typedef struct {
    const NpClV1Decoder *decoder;
    const NpClV1View *view;
    uint32_t max_reads, reads, exhausted;
} NpClV1Reader;

#ifdef NP_CL_V1_TEST_READS
extern void np_cl_v1_test_read(void);
#endif

int np_cl_v1_init(NpClV1Decoder *d, NpClLane *lane, uint32_t epoch)
{
    uint32_t adc;
    if (!d) return -1;
    memset(d, 0, sizeof(*d));
    if (!lane || !lane->initialized || lane->config.probe < 1U ||
        lane->config.probe > 3U || lane->config.channel >= 384U ||
        lane->config.source_step != NP_CL_V1_SOURCE_STEP) return -1;
    if (!(same_rate(lane->config.source_rate, NP_CL_V1_AP_15K * NP_CL_V1_TICKS_PER_AP, 1U) &&
          same_rate(lane->config.sample_rate, 1250U, 1U)) &&
        !(same_rate(lane->config.source_rate, NP_CL_V1_AP_20K * NP_CL_V1_TICKS_PER_AP, 1U) &&
          same_rate(lane->config.sample_rate, 5000U, 3U))) return -1;
    d->lane = lane; d->session = lane->config.session;
    d->probe = lane->config.probe; d->channel = lane->config.channel;
    d->source_epoch = epoch;
    adc = (d->channel / 24U) * 2U + (d->channel & 1U);
    d->word = (adc < 30U ? adc % 5U : adc - 30U) * 10U;
    d->bit = adc < 30U ? adc / 5U + 1U : 7U;
    if (lane->have_tick || lane->state) np_cl_reset(lane, NP_CL_REASON_RESET);
    d->initialized = 1U;
    return 1;
}

static int view_valid(const NpClV1View *v)
{
    uint32_t required;
    if (!v || !v->data || !v->ticks || v->ticks > 61440U ||
        v->bytes > 65536U || v->first_tick > UINT64_MAX - v->ticks ||
        v->probe < 1U || v->probe > 3U) return 0;
    switch (v->layout) {
    case NP_CL_V1_LINEAR8: required = v->ticks; break;
    case NP_CL_V1_SD_QUAD8:
        if (v->ticks & 3U) return 0;
        required = v->ticks; break;
    case NP_CL_V1_SD_QUAD16:
        if ((v->ticks & 3U) || v->probe > 2U) return 0;
        required = v->ticks * 2U; break;
    case NP_CL_V1_USB_QUAD8:
        if (v->ticks % 2024U) return 0;
        required = (v->ticks / 2024U) * 2048U; break;
    case NP_CL_V1_USB_QUAD16:
        if (v->ticks % 1008U || v->probe > 2U) return 0;
        required = (v->ticks / 1008U) * 2048U; break;
    default: return 0;
    }
    return required <= v->bytes;
}

static uint8_t byte_at(const NpClV1View *v, uint32_t t)
{
    uint32_t q = t >> 2, lane = t & 3U;
    switch (v->layout) {
    case NP_CL_V1_LINEAR8: return v->data[t];
    case NP_CL_V1_SD_QUAD8: return v->data[lane * (v->ticks / 4U) + q];
    case NP_CL_V1_SD_QUAD16:
        return v->data[lane * (v->ticks / 2U) + q * 2U + v->probe - 1U];
    case NP_CL_V1_USB_QUAD8:
        return v->data[((q / 506U) * 4U + lane) * 512U + 6U + q % 506U];
    default: /* Validated paired0x77 format:8-byte header,504-byte payload. */
        return v->data[((q / 252U) * 4U + lane) * 512U +
                       8U + 2U * (q % 252U) + v->probe - 1U];
    }
}

static uint8_t source_byte(NpClV1Reader *r, uint64_t tick)
{
    const NpClV1Decoder *d = r->decoder;
    const NpClV1View *v = r->view;
    if (r->exhausted) return 0U;
    if (r->max_reads && r->reads >= r->max_reads) {
        r->exhausted = 1U;
        return 0U;
    }
    ++r->reads;
#ifdef NP_CL_V1_TEST_READS
    np_cl_v1_test_read();
#endif
    if (tick < v->first_tick)
        return d->tail[tick - (v->first_tick - d->tail_size)];
    return byte_at(v, (uint32_t)(tick - v->first_tick));
}

static uint32_t word(NpClV1Reader *r, uint64_t tick, uint32_t bit)
{
    uint32_t n, value = 0U;
    for (n = 0U; n < 10U && !r->exhausted; ++n)
        value = (value << 1) | ((source_byte(r, tick + n) >> bit) & 1U);
    return value;
}

int np_cl_v1_seed(NpClV1Decoder *d, const NpClV1View *v,
                  uint32_t first_phase, uint32_t counter)
{
    uint32_t fixed, initial_slot, target_slot;
    NpClV1Reader reader = {d, v, 0U, 0U, 0U};
    if (!d || !d->initialized || !d->lane || !d->lane->initialized ||
        d->phase_valid || d->have_end || d->samples || !view_valid(v) ||
        v->probe != d->probe || v->source_epoch != d->source_epoch ||
        d->lane->config.session != d->session || d->lane->config.probe != d->probe ||
        d->lane->config.channel != d->channel ||
        v->flags != (NP_CL_INPUT_VALID | NP_CL_V1_RESET_VALID) ||
        v->ticks < 1560U || first_phase < v->first_tick ||
        first_phase - v->first_tick >= 780U || first_phase >= 0x80000U * 60U ||
        counter >= 0x80000U || counter % 13U) return -1;
    fixed = word(&reader, (uint64_t)first_phase + 20U, 7U);
    if (fixed != 0x11U && fixed != 0x22U) return -1;
    initial_slot = (counter / 13U) % 12U;
    target_slot = (d->channel % 24U) / 2U;
    d->phase = first_phase;
    d->counter_origin = counter; d->fixed = fixed;
    d->next_frame = (uint64_t)first_phase + ((target_slot + 12U - initial_slot) % 12U) * 780U;
    d->phase_valid = 1U;
    return 1;
}

static int frame_valid(NpClV1Reader *r, uint64_t first, uint32_t sync,
                        uint32_t fixed, uint32_t counter)
{
    uint32_t n, changes = 0U;
    uint8_t previous;
    if (word(r, first, 0U) != sync || word(r, first + 20U, 7U) != fixed ||
        ((word(r, first + 30U, 7U) << 10) |
          word(r, first + 40U, 7U)) != (counter & 0xfffffU) || r->exhausted) return 0;
    previous = source_byte(r, first + 10U) & 1U;
    for (n = 11U; n < 60U && !r->exhausted; ++n) {
        uint8_t current = source_byte(r, first + n);
        changes += (current & 1U) != previous;
        previous = current & 1U;
        if (n >= 50U && (current & 0xfeU)) return 0;
    }
    return !r->exhausted && changes <= 1U;
}

static int origin(NpClV1Decoder *d, const NpClV1View *v, NpClV1Reader *r)
{
    uint32_t at, n, sync = 0U;
    if (v->ticks < 1560U) return 0;
    /* Same 780 candidate offsets and MSB-first 10-bit sync word, with nine
     * priming reads and one new bit per offset instead of 780 ten-byte words. */
    for (n = 0U; n < 9U && !r->exhausted; ++n)
        sync = (sync << 1) | (source_byte(r, v->first_tick + n) & 1U);
    for (at = 0U; at < 780U && !r->exhausted; ++at) {
        uint32_t fixed, counter, initial_slot, target_slot;
        uint64_t absolute = v->first_tick + at;
        sync = ((sync << 1) | (source_byte(r, absolute + 9U) & 1U)) & 0x3ffU;
        if (r->exhausted || sync != 0x330U) continue;
        fixed = word(r, absolute + 20U, 7U);
        if (fixed != 0x11U && fixed != 0x22U) continue;
        counter = (word(r, absolute + 30U, 7U) << 10) | word(r, absolute + 40U, 7U);
        if (r->exhausted) return 0;
        for (n = 0U; n < 13U; ++n)
            if (!frame_valid(r, absolute + n * 60U, n ? 0xcfU : 0x330U,
                             fixed, counter + n)) break;
        if (n != 13U) continue;
        if (counter % 13U || counter >= 0x80000U || absolute >= 0x80000U * 60U)
            return -1;
        initial_slot = (counter / 13U) % 12U;
        target_slot = (d->channel % 24U) / 2U;
        d->phase = (uint32_t)absolute;
        d->counter_origin = counter; d->fixed = fixed;
        d->next_frame = absolute + ((target_slot + 12U - initial_slot) % 12U) * 780U;
        d->phase_valid = 1U;
        return 1;
    }
    return 0;
}

static void discard(NpClV1Decoder *d, uint64_t tick, uint32_t flags)
{
    NpClInput bad = {tick, 0.0f, flags};
    (void)np_cl_input(d->lane, &bad);
    d->have_end = d->tail_size = 0U;
    increment(&d->skipped_batches);
}

static int np_cl_v1_tap_inner(NpClV1Decoder *d, const NpClV1View *v,
                              uint32_t max_reads)
{
    uint64_t end, floor;
    uint32_t n, processed = 0U, flags;
    int found;
    NpClV1Reader reader = {d, v, max_reads, 0U, 0U};
    if (!d || !d->initialized || !d->lane || !d->lane->initialized) return -1;
    if (!view_valid(v) || v->probe != d->probe ||
        d->lane->config.session != d->session || d->lane->config.channel != d->channel ||
        d->lane->config.probe != d->probe || v->source_epoch != d->source_epoch) {
        discard(d, v ? v->first_tick : 0U, NP_CL_INPUT_FAULT);
        d->phase_valid = d->initialized = 0U;
        return -1;
    }
    flags = v->flags & ~NP_CL_V1_RESET_VALID;
    if (flags != NP_CL_INPUT_VALID) {
        discard(d, v->first_tick, flags);
        if (!d->phase_valid || !(flags & NP_CL_INPUT_VALID))
            d->phase_valid = d->initialized = 0U; /* Unknown source origin. */
        return 0;
    }
    end = v->first_tick + v->ticks;
    d->available_tick = end - 1U;
    if (d->have_end && d->expected_first != v->first_tick) {
        discard(d, v->first_tick, NP_CL_INPUT_DISCONTINUITY);
        if (!d->phase_valid) { d->initialized = 0U; return 0; }
    }
    if (!d->phase_valid) {
        if (!(v->flags & NP_CL_V1_RESET_VALID) || v->first_tick >= 0x80000U * 60U) {
            discard(d, v->first_tick, NP_CL_INPUT_FAULT);
            d->initialized = 0U;
            return 0;
        }
        found = origin(d, v, &reader);
        if (reader.exhausted) goto pressure;
        if (found < 0) {
            discard(d, v->first_tick, NP_CL_INPUT_FAULT);
            d->initialized = 0U;
            return 0;
        }
        if (!found) {
            discard(d, v->first_tick, 0U);
            d->expected_first = end; d->have_end = 1U;
            return 0;
        }
    }
    floor = v->first_tick - d->tail_size;
    if (d->next_frame < floor) {
        uint64_t distance = floor - d->next_frame;
        uint64_t skipped = distance / NP_CL_V1_SOURCE_STEP + (distance % NP_CL_V1_SOURCE_STEP != 0U);
        if (skipped > (UINT64_MAX - d->next_frame) / NP_CL_V1_SOURCE_STEP) {
            discard(d, v->first_tick, NP_CL_INPUT_FAULT);
            d->initialized = 0U;
            return -1;
        }
        d->next_frame += skipped * NP_CL_V1_SOURCE_STEP;
    }
    while (d->next_frame <= end && end - d->next_frame >= 60U) {
        uint64_t frame = d->next_frame;
        uint32_t counter = d->counter_origin + (uint32_t)((frame - d->phase) / 60U);
        NpClInput input = {frame + d->word, 0.0f, NP_CL_INPUT_VALID};
        if (frame_valid(&reader, frame, 0x330U, d->fixed, counter)) {
            int32_t sample = 512 - (int32_t)word(&reader, input.source_tick, d->bit);
            if (reader.exhausted) goto pressure;
            input.value = (float)sample;
            if (np_cl_input(d->lane, &input) == NP_CL_OK) {
                ++processed; increment(&d->samples);
            }
        } else {
            if (reader.exhausted) goto pressure;
            input.flags = NP_CL_INPUT_FAULT;
            (void)np_cl_input(d->lane, &input);
            increment(&d->invalid_frames);
        }
        if (d->next_frame > UINT64_MAX - NP_CL_V1_SOURCE_STEP) {
            discard(d, input.source_tick, NP_CL_INPUT_FAULT);
            d->initialized = 0U;
            return -1;
        }
        d->next_frame += NP_CL_V1_SOURCE_STEP;
    }
    d->tail_size = v->ticks < sizeof(d->tail) ? v->ticks : sizeof(d->tail);
    for (n = 0U; n < d->tail_size; ++n) {
        uint8_t value = source_byte(&reader, end - d->tail_size + n);
        if (reader.exhausted) goto pressure;
        d->tail[n] = value;
    }
    d->expected_first = end; d->have_end = 1U;
    return (int)processed;
pressure:
    discard(d, v->first_tick, NP_CL_INPUT_PRESSURE);
    d->phase_valid = d->initialized = 0U;
    return 0;
}
int np_cl_v1_tap_bounded(NpClV1Decoder *d, const NpClV1View *v, uint32_t max_reads)
{
#if NP_CL_CYCLE_PROBE
    uint32_t index=d && d->lane?d->lane->config.lane:NP_CL_CYCLE_LANES;
    NpClCycleState *state=index<NP_CL_CYCLE_LANES?NP_ClCycleStateGet():0;
    NpClCycleStamp stamp=np_cl_cycle_begin(state,index);
    int result=np_cl_v1_tap_inner(d,v,max_reads);
    np_cl_cycle_service_end(state,index,stamp,result>0?(uint32_t)result:0U);
    return result;
#else
    return np_cl_v1_tap_inner(d,v,max_reads);
#endif
}

int np_cl_v1_tap(NpClV1Decoder *d, const NpClV1View *v)
{ return np_cl_v1_tap_bounded(d, v, 0U); }
