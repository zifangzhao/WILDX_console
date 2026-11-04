/* Default-disabled USB4F foreground integration. Ordinary BSS is deliberate:
 * exact USB4F RW_IRAM1 has14640B slack; no capture/queued/DMA buffer alias. */
#include "np_cl_control_port.h"
#if NP_CL_ENABLE
#include "np_cl_workspace.h"
static NpClWorkspace np_usb_cl_workspace;
static uint32_t np_usb_cl_metadata;
static uint32_t np_usb_cl_start_request;
#if NP_FIRST_SYNC_ENABLE
static uint32_t np_usb_cl_binding[4];
void NP_ClCaptureBinding(uint32_t out[4])
{memcpy(out,np_usb_cl_binding,sizeof(np_usb_cl_binding));}
#endif
NpClSourceState *NP_ClCaptureState(void)
{
    NpClWorkspace *w=&np_usb_cl_workspace;
    if(!w->recording && w->armed!=2U)return 0;
#if NP_FIRST_SYNC_ENABLE
    if(w->source.seeded && (w->source.reset.ap_hz!=np_mode_hz ||
       w->source.reset.probe_mask!=np_mode_mask))np_cl_source_loss(&w->source);
#else
    if(w->source.reset.token && !np_cl_reset_matches(&w->source.reset,
        xe_np_usb_start_request_count,np_mode_hz,np_mode_mask))np_cl_reset_invalidate(&w->source.reset);
#endif
    return &w->source;
}
#define NP_CL_CYCLE_WORKSPACE() (&np_usb_cl_workspace)
#include "np_cl_cycle_port.inc"
#undef NP_CL_CYCLE_WORKSPACE
_Static_assert(sizeof(np_usb_cl_workspace)+sizeof(np_usb_cl_metadata)<2048U,"USB CL workspace budget");

int NP_ClosedLoopControl(const uint8_t request[64],uint8_t reply[64])
{
    NpClControlRequest q;uint32_t result=NP_CL_CONTROL_OK;
    const uint32_t start_request=xe_np_usb_start_request_count;
    if(np_cl_control_parse(request,64U,&q) && q.op==NP_CL_CONTROL_CONFIGURE){
        /* Do not call mode_busy(): it includes this mailbox's own pending bit. */
        if(!NP_USB_IsReady() || NP_USB_IsDownload() || np_v1_mode.target ||
           np_usb_catalog_owned() || np_v1_sd_reset_busy() || np_usb_sd_prepare_pending ||
           xe_np_usb_state.streaming || xe_np_usb_state.real_start_pending ||
           xe_np_usb_state.tx_busy || xe_np_usb_state.tx_pending || NP_USB20_SelectionPending())result=NP_CL_CONTROL_BUSY;
        else if(!np_cl_workspace_request_valid(request,np_mode_hz,np_mode_mask))result=NP_CL_CONTROL_SOURCE;
        else if(!np_cl_workspace_configure(&np_usb_cl_workspace,request,np_mode_hz,np_mode_mask,q.id))result=NP_CL_CONTROL_MEMORY;
        else np_usb_cl_start_request=start_request;
    }
    return np_cl_workspace_reply(&np_usb_cl_workspace,request,reply,np_mode_hz,np_mode_mask,result);
}
#if NP_FIRST_SYNC_ENABLE
static void NP_ClUsbSeedNone(const uint8_t *data,const NpClSourceReceipt *receipt)
{(void)data;(void)receipt;}
static void (*np_usb_cl_seed_first)(const uint8_t *,const NpClSourceReceipt *)=NP_ClUsbSeedNone;
#if NP_CL_CYCLE_PROBE
static volatile uint32_t np_usb_cl_seed_diag[4]; /* attempts, admitted mask, rejected, generation */
#endif
static void NP_ClUsbSeedFirst(const uint8_t *data,const NpClSourceReceipt *receipt)
{
    np_usb_cl_seed_first=NP_ClUsbSeedNone;
    NpClWorkspace *w=&np_usb_cl_workspace;
    if(!w->recording || !(w->flags&NP_CL_ACTIVE) || !receipt || receipt->first_source_tick ||
       receipt->flags!=3U || receipt->capture_generation!=w->source.capture_generation ||
       receipt->source_epoch!=w->source_epoch)return;
    uint32_t layout=data[2]==0x77U?NP_CL_V1_USB_QUAD16:NP_CL_V1_USB_QUAD8;
    for(uint32_t n=0U;n<w->lane_count;n++){
        NpClV1Decoder *d=&w->decoders[n];uint32_t phase[2];
        NpClV1View v={data,0U,XE_NP_USB_STREAM_QUAD_STAGING_PACKETS_PER_BATCH*512U,
            receipt->source_tick_count,layout,d->probe,w->source_epoch,NP_CL_INPUT_VALID|NP_CL_V1_RESET_VALID};
#if NP_CL_CYCLE_PROBE
        np_usb_cl_seed_diag[0]++;np_usb_cl_seed_diag[3]=receipt->capture_generation;
#endif
        int admitted=NP_ClCaptureFirstPhase(receipt,d->probe,phase) && np_cl_v1_seed(d,&v,phase[0],phase[1])==1;
#if NP_CL_CYCLE_PROBE
        if(admitted)np_usb_cl_seed_diag[1]|=1U<<n;else np_usb_cl_seed_diag[2]++;
#else
        (void)admitted;
#endif
    }
}
#endif
static void NP_ClUsbLifecycle(uint32_t active)
{
    NpClWorkspace *w=&np_usb_cl_workspace;
    /* Observe the existing IRQ-owned START counter only in foreground. A
     * cancelled/invalid START, or a second START, cannot inherit the opt-in. */
    if(w->armed && xe_np_usb_start_request_count!=np_usb_cl_start_request){
        if(xe_np_usb_start_request_count!=np_usb_cl_start_request+1U ||
           (!active && !xe_np_usb_state.real_start_pending))np_cl_workspace_stop(w);
        else if(w->armed==1U && np_cl_workspace_attempt(w)){
#if NP_FIRST_SYNC_ENABLE
            memset(np_usb_cl_binding,0,sizeof(np_usb_cl_binding));
            np_usb_cl_seed_first=NP_ClUsbSeedFirst;
#if NP_CL_CYCLE_PROBE
            memset((void *)np_usb_cl_seed_diag,0,sizeof(np_usb_cl_seed_diag));
#endif
#else
            (void)np_cl_reset_arm(&w->source.reset,xe_np_usb_start_request_count,np_mode_hz,np_mode_mask);
#endif
        }
    }
#if NP_FIRST_SYNC_ENABLE
    if(active && w->lane_count && !w->recording){
        NP_FirstCaptureContext(np_usb_cl_binding);
        if(!np_usb_cl_binding[0] || !np_usb_cl_binding[1] || !np_usb_cl_binding[2] ||
           !np_cl_workspace_record_begin(w,np_usb_cl_binding[0],np_mode_hz,np_mode_mask,np_usb_cl_binding[2]))
            np_cl_workspace_stop(w);
    }
#else
    if(active && w->lane_count && !w->recording && xe_np_usb_v1_session_id!=w->session){
        uint32_t epoch=w->source_epoch==UINT32_MAX?0U:w->source_epoch+1U;
        (void)np_cl_workspace_record_begin(w,xe_np_usb_v1_session_id,np_mode_hz,np_mode_mask,epoch);
    }
#endif
    if(!active && w->recording){np_cl_workspace_stop(w);np_usb_cl_metadata=0U;
#if NP_FIRST_SYNC_ENABLE
        np_usb_cl_seed_first=NP_ClUsbSeedNone;
#endif
    }
}
static void NP_ClUsbPrivateInner(const uint8_t *data,uint32_t slot,uint32_t generation,
                            const NpClSourceReceipt *receipt)
{
    NpClWorkspace *w=&np_usb_cl_workspace;uint32_t first,last,probe,layout,ticks;
    if(!w->recording)return;
    w->claim_slot=slot;w->have_claim=0U;np_usb_cl_metadata=0U;
    /* Carrier retirement still owns this current slot after detection fails. */
    if(!(w->flags&NP_CL_ACTIVE))return;
#if NP_FIRST_SYNC_ENABLE
    np_usb_cl_seed_first(data,receipt);
#endif
    layout=data[2]==0x77U?NP_CL_V1_USB_QUAD16:NP_CL_V1_USB_QUAD8;
    ticks=XE_NP_USB_STREAM_QUAD_STAGING_PACKETS_PER_BATCH*(data[2]==0x77U?252U:506U);
    first=data[2]==0x77U?1U:(data[3]&3U);last=data[2]==0x77U?2U:first;
    for(probe=first;probe<=last && probe<=3U;probe++){
        if(generation!=xe_np_usb_disarm_count){np_cl_workspace_stop(w);return;}
        /* This copy passed acquisition retention/ownership and the queue
         * scheduler has already admitted next capture work. Published TX
         * occupancy is flow control, not missing source data. Actual source
         * loss still invalidates its ledger; never fabricate a pressure GAP. */
        np_cl_workspace_private(w,receipt,data,XE_NP_USB_STREAM_QUAD_STAGING_PACKETS_PER_BATCH*512U,
            ticks,layout,probe,0);
    }
}
static void NP_ClUsbPrivate(const uint8_t *data,uint32_t slot,uint32_t generation,const NpClSourceReceipt *receipt)
{
#if NP_CL_CYCLE_PROBE
    NpClCycleState *state=NP_ClCycleStateGet();NpClCycleStamp stamp=np_cl_cycle_bridge_begin(state);
    NP_ClUsbPrivateInner(data,slot,generation,receipt);np_cl_cycle_bridge_end(state,stamp);
#else
    NP_ClUsbPrivateInner(data,slot,generation,receipt);
#endif
}
static int NP_ClUsbNeeds(void){return np_usb_cl_workspace.recording && np_usb_cl_workspace.carrier;}
static void NP_ClUsbSlotInner(uint8_t *packet,uint32_t n)
{
    NpClWorkspace *w=&np_usb_cl_workspace;uint8_t *b=packet+6U;
    if(NP_ClUsbNeeds()){
        memset(b+300U,0,202U);
        if(!n)(void)np_cl_workspace_claim(w,b+300U,w->claim_slot);
        memcpy(b+364U,w->confirmed,64U);np_cl_workspace_status(w,b+428U);
#if NP_FIRST_SYNC_ENABLE
        /* Ordinary CL carrier only; grid/phase companions never enter here. */
        np_cl_wire_u32(b+176U,w->session);
#endif
        np_usb_cl_metadata=1U;
    }
    /* The current CL serializer leaves the ordinary outer CRC unsealed.
     * Seal once, after all optional fields, including when CL is off. */
    np_cl_wire_u32(b+502U,np_misc_crc(b,502U));
}
static void NP_ClUsbSlot(uint8_t *packet,uint32_t n)
{
#if NP_CL_CYCLE_PROBE
    NpClCycleState *state=NP_ClCycleStateGet();NpClCycleStamp stamp=np_cl_cycle_bridge_begin(state);
    NP_ClUsbSlotInner(packet,n);np_cl_cycle_bridge_end(state,stamp);
#else
    NP_ClUsbSlotInner(packet,n);
#endif
}
static void NP_ClUsbCommit(uint32_t slot)
{
    NpClWorkspace *w=&np_usb_cl_workspace;
    if(!np_usb_cl_metadata)return;
    (void)np_cl_workspace_commit(w,slot);w->flags|=NP_CL_CONFIG_SAVED;np_usb_cl_metadata=0U;
}
#endif
