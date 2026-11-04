#include "np_cl_workspace.h"
#include "np_cl_coefficients.h"

#if NP_CL_CYCLE_PROBE
static void rejection(NpClWorkspace *w,uint32_t reason,uint32_t probe)
{
    if(w->first_reject_reason)return;
    for(uint32_t n=0U;n<w->lane_count;n++)if(w->lanes[n].config.probe==probe){
        w->first_reject_stage=w->source.reset.stage;
        w->first_reject_reset_generation=w->source.reset.capture_generation;
        w->first_reject_source_generation=w->source.capture_generation;
        w->first_reject_reason=reason;break;
    }
}
#else
#define rejection(w,reason,probe) ((void)0)
#endif

static NpClRate detector_rate(uint32_t ap)
{
    switch(ap){
    case NP_CL_V1_AP_15K:return (NpClRate){1250U,1U};
    case NP_CL_V1_AP_20K:return (NpClRate){5000U,3U};
    default:return (NpClRate){0U,1U};
    }
}

static int admit(const NpClControlRequest *r,uint32_t ap,uint32_t mask)
{
    uint32_t n;
    NpClCoefficients coeff;
    NpClRate rate=detector_rate(ap);
    if(r->op!=NP_CL_CONTROL_CONFIGURE ||
       (r->count && (!rate.num || r->ap_hz!=ap)))return 0;
    for(n=0;n<r->count;n++)
        if(!(mask&(1U<<((r->selector[n]&255U)-1U))) ||
           !np_cl_coefficients_get(rate,(r->selector[n]>>8U)&255U,&coeff))return 0;
    return 1;
}
int np_cl_workspace_request_valid(const uint8_t wire[64],uint32_t ap,uint32_t mask)
{
    NpClControlRequest r;
    return np_cl_control_parse(wire,64U,&r) && admit(&r,ap,mask);
}
int np_cl_workspace_configure(NpClWorkspace *w,const uint8_t wire[64],uint32_t ap,
                              uint32_t mask,uint32_t generation)
{
    NpClControlRequest r;
    if(!w || w->recording || !generation || !np_cl_control_parse(wire,64U,&r) || !admit(&r,ap,mask))return 0;
    memset(w,0,sizeof(*w));
    memcpy(w->confirmed,wire,64U);w->config_crc=r.config_crc;w->lane_count=r.count;
    w->armed=r.count?1U:0U;
    np_cl_journal_init(&w->journal,generation);
    w->flags=r.count?NP_CL_TIME_UNKNOWN:0U;
    return 1;
}
int np_cl_workspace_attempt(NpClWorkspace *w)
{
    if(!w || w->recording)return 0;
    w->carrier=0U;
    w->armed=w->armed==1U?2U:0U;
    if(!w->armed)np_cl_reset_invalidate(&w->source.reset);
    return w->armed!=0U;
}
static int sink(void *context,const NpClEvent *event)
{
    NpClSinkContext *c=context;NpClWorkspace *w=c->workspace;
    uint8_t packet[64];uint32_t sequence=w->journal.next_sequence;
    if(!sequence || !w->recording){np_cl_journal_add(&w->journal.dropped,1U);return 0;}
    w->journal.next_sequence=sequence==UINT32_MAX?0U:sequence+1U;
    if(!np_cl_event_pack(packet,event,w->input_end_tick,w->config_crc,sequence,
        w->source_epoch,w->journal.dropped,event->kind==NP_CL_EVENT_GAP?w->lanes[c->lane].last_gap_reason:0U,
        (w->time_valid && w->decoders[c->lane].phase_valid)?NP_CL_EVENT_TIME_VALID:0U)){
        np_cl_journal_add(&w->journal.dropped,1U);return 0;
    }
    return np_cl_journal_push(&w->journal,packet);
}
int np_cl_workspace_record_begin(NpClWorkspace *w,uint32_t session,uint32_t ap,
                                 uint32_t mask,uint32_t epoch)
{
    NpClControlRequest r;uint32_t n,generation;
    if(!w || w->recording)return 0;
    uint32_t armed=w->armed;w->armed=0U;w->carrier=0U;
    if(armed!=2U || !session || !epoch || !w->lane_count ||
       !np_cl_control_parse(w->confirmed,64U,&r) || !admit(&r,ap,mask)){
        np_cl_reset_invalidate(&w->source.reset);return 0;
    }
    generation=w->journal.generation;
    if(!generation){np_cl_reset_invalidate(&w->source.reset);return 0;}
    np_cl_journal_init(&w->journal,generation);
    w->have_claim=0U;w->time_valid=0U;w->session=session;w->source_epoch=epoch;
    np_cl_source_record_begin(&w->source,epoch);
#if NP_CL_CYCLE_PROBE
    memset(&w->cycles,0,sizeof(w->cycles));
    w->first_reject_reason=w->first_reject_stage=0U;
    w->first_reject_reset_generation=w->first_reject_source_generation=0U;
#endif
    w->carrier=1U;
    w->flags=(w->flags&NP_CL_NO_PREVIEW)|NP_CL_ACTIVE|NP_CL_TIME_UNKNOWN;
    NpClRate rate=detector_rate(ap);
    for(n=0;n<w->lane_count;n++){
        NpClCoefficients coeff;NpClConfig c;
        memset(&c,0,sizeof(c));c.sample_rate=rate;
        c.source_rate=(NpClRate){ap*NP_CL_V1_TICKS_PER_AP,1U};
        c.source_step=NP_CL_V1_SOURCE_STEP;
        c.session=session;c.probe=r.selector[n]&255U;c.channel=r.selector[n]>>16U;c.lane=n;
        c.training_start_ms=r.training_start_ms;c.training_duration_ms=r.training_duration_ms;
        c.threshold_gain=r.gain[n];c.rectify=(r.rectify_mask>>n)&1U;
        w->sinks[n].workspace=w;w->sinks[n].lane=n;
        if(!np_cl_coefficients_get(c.sample_rate,(r.selector[n]>>8U)&255U,&coeff) ||
           np_cl_init(&w->lanes[n],&c,&coeff,sink,&w->sinks[n])!=1 ||
           np_cl_v1_init(&w->decoders[n],&w->lanes[n],epoch)!=1){
            np_cl_reset_invalidate(&w->source.reset);
            w->flags=(w->flags&NP_CL_NO_PREVIEW)|NP_CL_STOPPED|NP_CL_TIME_UNKNOWN;return 0;
        }
    }
    w->recording=1U;return 1;
}
void np_cl_workspace_tap(NpClWorkspace *w,const NpClV1View *v)
{
    uint32_t n,known=1U;
    if(!w || !v || !w->recording || !(w->flags&NP_CL_ACTIVE))return;
    w->time_valid=(v->flags&NP_CL_INPUT_VALID) && v->source_epoch==w->source_epoch &&
        v->ticks && v->first_tick<=UINT64_MAX-v->ticks;
    w->input_end_tick=w->time_valid?v->first_tick+v->ticks-1U:0U;
    for(n=0;n<w->lane_count;n++){
        if(w->lanes[n].config.probe==v->probe)
            (void)np_cl_v1_tap_bounded(&w->decoders[n],v,3000U);
        if(!w->decoders[n].initialized || !w->decoders[n].phase_valid)known=0U;
        if(!w->decoders[n].initialized){
            rejection(w,0x100U|w->lanes[n].last_gap_reason,v->probe);
            w->flags&=~NP_CL_ACTIVE;
        }
    }
    if(known && w->time_valid)w->flags&=~NP_CL_TIME_UNKNOWN;
    else w->flags|=NP_CL_TIME_UNKNOWN;
}
void np_cl_workspace_private(NpClWorkspace *w,const NpClSourceReceipt *r,const uint8_t *data,
                             uint32_t bytes,uint32_t ticks,uint32_t layout,uint32_t probe,int pressure)
{
    NpClV1View v;
    if(!w || !w->recording)return;
    memset(&v,0,sizeof(v));v.data=data;v.bytes=bytes;v.ticks=ticks;v.layout=layout;v.probe=probe;
    if(r && (r->capture_generation!=w->source.capture_generation ||
       r->source_epoch!=w->source.source_epoch)){
        rejection(w,4U,probe);np_cl_source_loss(&w->source);
    }
    if(w->source.source_epoch)w->source_epoch=w->source.source_epoch;
    v.source_epoch=w->source_epoch;
    if(r && w->source.valid && w->source.reset.token &&
       w->source.reset.stage==NP_CL_RESET_BOUND &&
       w->source.reset.capture_generation==w->source.capture_generation &&
       (r->flags&NP_CL_SOURCE_VALID) &&
       !(r->flags&~(NP_CL_SOURCE_VALID|NP_CL_SOURCE_RESET_VALID)) && r->source_tick_count==ticks &&
       r->source_epoch==w->source_epoch && r->capture_generation==w->source.capture_generation &&
       probe>=1U && probe<=3U && (r->probe_mask&(1U<<(probe-1U)))){
        v.first_tick=r->first_source_tick;v.flags=NP_CL_INPUT_VALID;
        if(r->flags&NP_CL_SOURCE_RESET_VALID)v.flags|=NP_CL_V1_RESET_VALID;
        if(pressure)v.flags|=NP_CL_INPUT_PRESSURE;
    }
    else rejection(w,!w->source.valid?1U:
        (!w->source.reset.token || w->source.reset.stage!=NP_CL_RESET_BOUND)?2U:
        w->source.reset.capture_generation!=w->source.capture_generation?3U:4U,probe);
    np_cl_workspace_tap(w,&v);
}
void np_cl_workspace_stop(NpClWorkspace *w)
{
    if(!w)return;
    w->armed=0U;
    np_cl_reset_invalidate(&w->source.reset);
    if(!w->lane_count || (w->flags&NP_CL_STOPPED))return;
    w->recording=0U;w->have_claim=0U;w->time_valid=0U;
    np_cl_journal_stop(&w->journal);
    w->flags=(w->flags&~NP_CL_ACTIVE)|NP_CL_STOPPED;
    if(w->journal.tail_dropped)w->flags|=NP_CL_TAIL_DROPPED;
}
void np_cl_workspace_status(const NpClWorkspace *w,uint8_t out[12])
{
    memset(out,0,12U);if(!w || !w->carrier)return;
    np_cl_wire_u32(out,w->config_crc);np_cl_wire_u32(out+4,w->journal.dropped);
    np_cl_wire_u32(out+8,NP_CL_STATUS_MAGIC|w->flags);
}
int np_cl_workspace_claim(NpClWorkspace *w,uint8_t out[64],uint32_t slot)
{
    memset(out,0,64U);if(!w)return 0;
    w->have_claim=np_cl_journal_claim(&w->journal,out,&w->claim);
    w->claim_slot=slot;return (int)w->have_claim;
}
int np_cl_workspace_commit(NpClWorkspace *w,uint32_t slot)
{
    int ok;if(!w || !w->have_claim || slot!=w->claim_slot)return 0;
    ok=np_cl_journal_commit(&w->journal,&w->claim);w->have_claim=0U;return ok;
}
void np_cl_workspace_sd(NpClWorkspace *w,uint8_t b[512],uint32_t slot)
{
    if(!w || !w->carrier)return;
    np_cl_workspace_claim(w,b+388U,slot);np_cl_workspace_status(w,b+452U);
    np_cl_wire_u32(b+508U,np_cl_wire_crc(b,508U));
}
void np_cl_workspace_snapshot(const NpClWorkspace *w,uint8_t b[512])
{
    if(!w || !w->carrier)return;
    memcpy(b+80U,w->confirmed,64U);memcpy(b+144U,"NPCLCFG1",8U);
    np_cl_wire_u32(b+152U,w->config_crc);np_cl_wire_u32(b+156U,1U);
}
int np_cl_workspace_reply(const NpClWorkspace *w,const uint8_t request[64],uint8_t out[64],
                          uint32_t ap,uint32_t mask,uint32_t result)
{
    NpClControlRequest q;NpClControlReply r;NpClCoefficients coeff;uint32_t i;
    memset(out,0,64U);memset(&r,0,sizeof(r));
    if(!np_cl_control_parse(request,64U,&q)){
        r.op=np_cl_control_u32(request+8U);r.id=np_cl_control_u32(request+12U);
        r.result=NP_CL_CONTROL_INVALID;
        if(r.op>2U || !r.id)return 0;
    }else{r.op=q.op;r.id=q.id;r.result=result;}
    r.flags=NP_CL_CONTROL_SUPPORTED;r.ap_hz=ap;
    NpClRate rate=detector_rate(ap);
    if(rate.num){
        r.max_lanes=2U;r.probe_mask=mask;
        for(i=0;i<10U;i++)if(np_cl_coefficients_get(rate,i,&coeff))r.filter_mask|=1U<<i;
        for(i=0;i<3U;i++)if(mask&(1U<<i))r.channel_groups|=7U<<(i*3U);
    }
    if(w && w->lane_count && (w->armed || w->carrier)){
        r.flags|=NP_CL_CONTROL_CONFIGURED;r.config_crc=w->config_crc;r.session=w->session;
        r.source_epoch=w->source_epoch;r.drops=w->journal.dropped;
        if(w->recording && (w->flags&NP_CL_ACTIVE))r.flags|=NP_CL_CONTROL_RUNNING;
        if(w->source_epoch && !(w->flags&NP_CL_TIME_UNKNOWN) && (w->flags&NP_CL_ACTIVE))r.flags|=NP_CL_CONTROL_ORIGIN_VALID;
        if(w->flags&NP_CL_CONFIG_SAVED)r.flags|=NP_CL_CONTROL_SNAPSHOT_STAGED;
    }
    return np_cl_control_reply_pack(out,&r);
}
