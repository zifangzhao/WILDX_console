#ifndef NP_CL_JOURNAL_H
#define NP_CL_JOURNAL_H

#include "np_cl_event_wire.h"

#define NP_CL_JOURNAL_CAPACITY 8U

typedef struct {
    uint8_t entries[8][64];
    uint32_t generation, head, count, next_sequence, dropped, tail_dropped;
} NpClJournal;

typedef struct { uint32_t generation, head, sequence; } NpClClaim;

static inline uint32_t np_cl_journal_read_u32(const uint8_t *p)
{
    return (uint32_t)p[0] | (uint32_t)p[1] << 8U |
           (uint32_t)p[2] << 16U | (uint32_t)p[3] << 24U;
}

static inline void np_cl_journal_add(uint32_t *value, uint32_t increment)
{
    *value = increment > UINT32_MAX - *value ? UINT32_MAX : *value + increment;
}

/* One foreground owner; not an ISR/concurrent queue. Initialize fresh backing
 * storage with a lifetime-unique nonzero generation. Do not reinitialize an
 * existing lifetime with an old generation: stop() invalidates leases safely.
 * The producer owns global nonzero wire sequences and may leave gaps on loss;
 * next_sequence is shared producer bookkeeping, not a continuity admission gate. */
static inline int np_cl_journal_init(NpClJournal *q, uint32_t generation)
{
    if (q == NULL) return 0;
    memset(q, 0, sizeof(*q));
    q->generation = generation;
    q->next_sequence = 1U;
    return generation != 0U;
}

/* Drop new, never replace an older event. A full/invalid push increments
 * dropped, without changing producer-advanced next_sequence. Accepted packet
 * sequence+1 is retained (zero means sequence space exhausted). Wire sequence
 * uniqueness within a generation is the producer's lease/ABA contract. */
static inline int np_cl_journal_push(NpClJournal *q, const uint8_t event[64])
{
    uint32_t tail, sequence;
    if (q == NULL) return 0;
    if (event == NULL || q->generation == 0U || q->head >= 8U || q->count >= 8U ||
        memcmp(event, "NPCL", 4U) != 0 || event[4] != 1U ||
        np_cl_journal_read_u32(event + 12U) == 0U ||
        np_cl_journal_read_u32(event + 60U) != np_cl_wire_crc(event, 60U)) {
        np_cl_journal_add(&q->dropped, 1U);
        return 0;
    }
    tail = (q->head + q->count) % 8U;
    memcpy(q->entries[tail], event, 64U);
    ++q->count;
    sequence = np_cl_journal_read_u32(event + 12U);
    q->next_sequence = sequence == UINT32_MAX ? 0U : sequence + 1U;
    return 1;
}

/* Peek only: retry/cancellation leaves the same head until a matching commit.
 * Copy out before handing bytes to another bounded carrier; retain the token
 * only until its generation stops. Neither claim nor commit performs I/O. */
static inline int np_cl_journal_claim(const NpClJournal *q, uint8_t out[64],
                                      NpClClaim *token)
{
    if (q == NULL || out == NULL || token == NULL || q->generation == 0U ||
        q->head >= 8U || q->count == 0U || q->count > 8U) return 0;
    memcpy(out, q->entries[q->head], 64U);
    token->generation = q->generation;
    token->head = q->head;
    token->sequence = np_cl_journal_read_u32(out + 12U);
    return 1;
}

static inline int np_cl_journal_commit(NpClJournal *q, const NpClClaim *token)
{
    if (q == NULL || token == NULL || q->generation == 0U || q->head >= 8U ||
        q->count == 0U || q->count > 8U || token->generation != q->generation ||
        token->head != q->head ||
        token->sequence != np_cl_journal_read_u32(q->entries[q->head] + 12U)) return 0;
    q->head = (q->head + 1U) % 8U;
    --q->count;
    return 1;
}

/* No drain/wait on Stop. Count the unsent tail and invalidate every old claim.
 * Exhausted generation space disables the journal instead of silently wrapping
 * to a live generation. A disabled journal cannot be revived by another stop. */
static inline void np_cl_journal_stop(NpClJournal *q)
{
    if (q == NULL) return;
    np_cl_journal_add(&q->dropped, q->count);
    np_cl_journal_add(&q->tail_dropped, q->count);
    q->head = 0U;
    q->count = 0U;
    if (q->generation != 0U)
        q->generation = q->generation == UINT32_MAX ? 0U : q->generation + 1U;
}

#endif
