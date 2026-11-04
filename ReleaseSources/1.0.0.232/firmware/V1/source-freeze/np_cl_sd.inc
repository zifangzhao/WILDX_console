/* Optional foreground SD adapter. Its only backing is the leased preview
 * prefix; no capture, queued chunk or sensor buffer is borrowed. */
#define NPL_CL_OWNER 0x80000000U
#if NP_CL_ENABLE
#if !NP_SD_CE64 || !NP_SD_ELASTIC || NP_SD_DIRECT_PSRAM
#error Closed-loop SD requires the reviewed elastic CE64 private-SRAM staging path
#endif
#if !NP_FIRST_SYNC_ENABLE
#error Closed-loop SD requires the current first-source capture binding
#endif
/* Immutable START association. Final generation/profile/reset proof is owned
 * by the common first-source bootstrap, not a second adapter reset ledger. */
static uint32_t npl_cl_binding[4] __attribute__((section("NP_SD_TRACE"),aligned(4)));
void NP_ClCaptureBinding(uint32_t out[4])
{memcpy(out,npl_cl_binding,sizeof(npl_cl_binding));}
static NpClWorkspace *npl_cl_workspace(void)
{return (npl_preview_origin_mask&NPL_CL_OWNER)?&npl_preview_storage.cl:0;}
NpClSourceState *NP_ClCaptureState(void)
{
    NpClWorkspace *w=npl_cl_workspace();
    if(!w || (!w->recording && w->armed!=2U))return 0;
    return &w->source;
}
#define NP_CL_CYCLE_WORKSPACE() npl_cl_workspace()
#include "np_cl_cycle_port.inc"
#undef NP_CL_CYCLE_WORKSPACE
static int npl_cl_stopped(void)
{return (np_sd_log.state==NP_SD_IDLE || np_sd_finalized) && !np_sd_mailbox.command && !xe_np_quad_dma_active;}
static int npl_cl_release(void)
{
    if(!npl_cl_stopped())return 0;
    np_ble_preview_stop(&npl_preview);memset(&npl_preview_storage,0,sizeof(npl_preview_storage));
    npl_preview_origin_mask=0U;return 1;
}
static int npl_cl_configure(const uint8_t wire[64],uint32_t generation)
{
    if(!npl_cl_stopped() || !generation || !np_cl_workspace_request_valid(wire,np_mode_hz,np_mode_mask))return 0;
    if(!np_cl_control_u32(wire+32U))return npl_cl_release();
    np_ble_preview_stop(&npl_preview);memset(&npl_preview_storage,0,sizeof(npl_preview_storage));
    if(!np_cl_workspace_configure(&npl_preview_storage.cl,wire,np_mode_hz,np_mode_mask,generation))return 0;
    npl_preview_origin_mask=NPL_CL_OWNER;npl_preview_storage.cl.flags|=NP_CL_NO_PREVIEW;return 1;
}
static void npl_cl_record_begin(void)
{
    NpClWorkspace *w=npl_cl_workspace();if(!w)return;
    NP_FirstCaptureContext(npl_cl_binding);
    if(npl_cl_binding[0]!=np_sd_log.config.session || !npl_cl_binding[2] ||
       !np_cl_workspace_record_begin(w,npl_cl_binding[0],np_sd_log.config.ap_hz,
        np_sd_log.config.probe_mask,npl_cl_binding[2]))np_cl_workspace_stop(w);
}
static void npl_cl_record_attempt(void)
{
    NpClWorkspace *w=npl_cl_workspace();
    memset(npl_cl_binding,0,sizeof(npl_cl_binding));
    if(w)(void)np_cl_workspace_attempt(w);
}
static void npl_cl_cancel_arm(void)
{
    NpClWorkspace *w=npl_cl_workspace();if(!w)return;
    w->armed=0U;
    /* Only a bound live prefix may retire its private copy before final stop. */
    if(!w->recording || !w->source.valid || !w->source.seeded ||
       w->source.reset.stage!=NP_CL_RESET_BOUND)np_cl_reset_invalidate(&w->source.reset);
}
static void npl_cl_config_saved(int saved)
{
    NpClWorkspace *w=npl_cl_workspace();if(!w || !w->carrier)return;
    if(saved)w->flags|=NP_CL_CONFIG_SAVED;
    else{w->flags&=~NP_CL_CONFIG_SAVED;np_cl_workspace_stop(w);}
}
static void npl_cl_record_header(uint8_t *b,uint32_t kind)
{
    NpClWorkspace *w=npl_cl_workspace();(void)kind;
    /* Presence survives optional runtime failure. Only a retired private
     * stage claims events; early launch/header composition is status-only. */
    if(w)np_cl_workspace_status(w,b+452U);
}
static void npl_cl_stage_commit(unsigned slot)
{NpClWorkspace *w=npl_cl_workspace();if(w)(void)np_cl_workspace_commit(w,slot);}
static void npl_cl_tap_inner(uint8_t *data,unsigned slot)
{
    NpClWorkspace *w=npl_cl_workspace();if(!w)return;
    if(w->recording && (w->flags&NP_CL_CONFIG_SAVED)){
        const NpClSourceReceipt *r=&np_sd_batch.cl_source;
        /* This retired private stage already passed retention/ownership.
         * Healthy queued chunks are flow control, not missing source data. */
        for(uint32_t probe=1U;probe<=3U;probe++)if(r->probe_mask&(1U<<(probe-1U)))
            np_cl_workspace_private(w,r,data,65536U,61440U,NP_CL_V1_SD_QUAD8,probe,0);
    }
    if(w->carrier){
        uint8_t *b=data+61632U;
        np_cl_workspace_claim(w,b+388U,slot);np_cl_workspace_status(w,b+452U);
        np_put32(b+508U,np_sd_metadata_crc_fast(b,508U));
    }
    XE_NP_App_DCacheClean(data+61632U,512U);
}
static void npl_cl_tap(uint8_t *data,unsigned slot)
{
#if NP_CL_CYCLE_PROBE
    NpClCycleState *state=NP_ClCycleStateGet();NpClCycleStamp stamp=np_cl_cycle_bridge_begin(state);
    npl_cl_tap_inner(data,slot);np_cl_cycle_bridge_end(state,stamp);
#else
    npl_cl_tap_inner(data,slot);
#endif
}
static void npl_cl_stop(void)
{NpClWorkspace *w=npl_cl_workspace();if(w)np_cl_workspace_stop(w);}
static int npl_cl_preview_unavailable(const uint8_t request[64])
{
    if(!npl_cl_workspace() || xe_np_online_word(request)!=NP1_PREVIEW_MAGIC)return 0;
    if(xe_np_online_word(request+4U)==1U && xe_np_online_word(request+12U) &&
       xe_np_online_word(request+60U)==xe_np_online_crc(request)){
        uint8_t body[224]={0};
        xe_np_online_put(body,NP1_PREVIEW_MAGIC);xe_np_online_put(body+4U,xe_np_online_word(request+12U));
        xe_np_online_put(body+8U,xe_np_online_word(request+28U));xe_np_online_put(body+12U,np_mode_hz);
        xe_np_online_put(body+28U,xe_np_online_word(request+16U)|(6U<<8U)|
            (xe_np_online_word(request+32U)<<12U)|(xe_np_online_word(request+20U)<<16U));
        xe_np_online_put(body+208U,1U);xe_np_online_put(body+220U,np_sd_metadata_crc_fast(body,220U));
        (void)npl_ce32_frame(0xbaU,body,sizeof(body));
    }
    return 1;
}
#endif
