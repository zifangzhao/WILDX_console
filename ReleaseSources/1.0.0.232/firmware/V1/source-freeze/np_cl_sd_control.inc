/* Included after npl_idle; the shared mailbox demux is foreground-only. */
#if NP_CL_ENABLE
int NP_ClosedLoopControl(const uint8_t request[64],uint8_t reply[64])
{
    NpClControlRequest q;uint32_t result=NP_CL_CONTROL_OK;
    if(np_cl_control_parse(request,64U,&q) && q.op==NP_CL_CONTROL_CONFIGURE){
        if(!npl_idle() || npl_mode_target || npl_online_pending || npl_delete_phase ||
           np_sd_mailbox.command || np_sd_readback.command)result=NP_CL_CONTROL_BUSY;
        else if(!np_cl_workspace_request_valid(request,np_mode_hz,np_mode_mask))result=NP_CL_CONTROL_SOURCE;
        else if(!npl_cl_configure(request,q.id))result=NP_CL_CONTROL_MEMORY;
    }
    return np_cl_workspace_reply(npl_cl_workspace(),request,reply,np_mode_hz,np_mode_mask,result);
}
#endif
