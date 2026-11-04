#ifndef NP_CL_WORKSPACE_H
#define NP_CL_WORKSPACE_H
#include "np_cl_v1.h"
#include "np_cl_journal.h"
#include "np_cl_control_wire.h"
#include "np_cl_source.h"
#include "np_cl_cycle_probe.h"

#ifndef NP_CL_ENABLE
#define NP_CL_ENABLE 0
#endif
#define NP_CL_STATUS_MAGIC 0x434c0100U
enum { NP_CL_ACTIVE=1U, NP_CL_STOPPED=2U, NP_CL_TIME_UNKNOWN=4U,
       NP_CL_CONFIG_SAVED=8U, NP_CL_TAIL_DROPPED=16U, NP_CL_NO_PREVIEW=32U };

typedef struct NpClWorkspace NpClWorkspace;
typedef struct { NpClWorkspace *workspace; uint32_t lane; } NpClSinkContext;
struct NpClWorkspace {
    NpClLane lanes[2];
    NpClV1Decoder decoders[2];
    NpClSourceState source;
#if NP_CL_CYCLE_PROBE
    NpClCycleState cycles;
#endif
    NpClJournal journal;
    NpClSinkContext sinks[2];
    uint8_t confirmed[64];
    NpClClaim claim;
    uint64_t input_end_tick;
    uint32_t config_crc, lane_count, session, source_epoch, flags;
    uint32_t claim_slot, have_claim, time_valid, recording;
    uint32_t armed, carrier; /* 1=staged, 2=bound to one START attempt. */
#if NP_CL_CYCLE_PROBE
    /* First selected-view rejection only; retained through Stop, no wire I/O.
     * 1=source, 2=reset stage, 3=reset generation, 4=receipt,
     * 0x100|gap_reason=decoder disarm after successful source admission. */
    uint32_t first_reject_reason, first_reject_stage;
    uint32_t first_reject_reset_generation, first_reject_source_generation;
#endif
};
_Static_assert(sizeof(NpClWorkspace)<2048U,"Closed-loop workspace exceeded reviewed USB/preview budget");

/* Pure admission; caller must hold stopped configuration ownership. No flash
 * persistence. A rejected request preserves the previous confirmed state. */
int np_cl_workspace_request_valid(const uint8_t[64],uint32_t ap_hz,uint32_t probe_mask);
int np_cl_workspace_configure(NpClWorkspace *,const uint8_t[64],uint32_t ap_hz,
                              uint32_t probe_mask,uint32_t generation);
int np_cl_workspace_attempt(NpClWorkspace *);
int np_cl_workspace_record_begin(NpClWorkspace *,uint32_t session,uint32_t ap_hz,
                                 uint32_t probe_mask,uint32_t source_epoch);
void np_cl_workspace_tap(NpClWorkspace *,const NpClV1View *);
void np_cl_workspace_private(NpClWorkspace *,const NpClSourceReceipt *,const uint8_t *,
                             uint32_t bytes,uint32_t ticks,uint32_t layout,uint32_t probe,int pressure);
void np_cl_workspace_stop(NpClWorkspace *);
void np_cl_workspace_status(const NpClWorkspace *,uint8_t[12]);
int np_cl_workspace_claim(NpClWorkspace *,uint8_t[64],uint32_t slot);
int np_cl_workspace_commit(NpClWorkspace *,uint32_t slot);
void np_cl_workspace_sd(NpClWorkspace *,uint8_t descriptor[512],uint32_t slot);
/* Compose before the existing outer sector CRC. Marker144..159 binds an
 * exact NPCC snapshot80..143; all-zero legacy reserved bytes stay untouched. */
void np_cl_workspace_snapshot(const NpClWorkspace *,uint8_t sector[512]);
int np_cl_workspace_reply(const NpClWorkspace *,const uint8_t[64],uint8_t[64],
                          uint32_t ap_hz,uint32_t probe_mask,uint32_t result);
#endif
