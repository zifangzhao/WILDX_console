/* Read-only NPSQ/NPSR v1, matching the console's existing V2 query.
 * Foreground owns publication and this view; IRQs never publish snapshots.
 * APP's existing trace allocation is separate from all waveform/CL storage.
 * Align independently from its512-byte metadata DMA buffer's cache lines. */
#define NPL_SENSOR_QUERY 0x5153504eU
#define NPL_SENSOR_REPLY 0x5253504eU
#if NP_SD_SENSORS
#define NPL_SENSOR_CAPABILITY 0x10000000U
#ifndef NPL_SENSOR_STORAGE
#define NPL_SENSOR_STORAGE __attribute__((section("NP_SD_TRACE"),aligned(32)))
#endif
static struct {
    XE_NP_SensorSnapshot snapshot;
    uint32_t token,expires,session,mask,hz,state,active;
} npl_sensor_view NPL_SENSOR_STORAGE;
_Static_assert(sizeof(XE_NP_SensorSnapshot)==140U,"SNS1 v1 must remain140 bytes");
static int npl_sensor_lease_live(uint32_t now)
{
    if(!npl_sensor_view.active)return 0;
    if((int32_t)(now-npl_sensor_view.expires)>=0 ||
       HAL_GPIO_ReadPin(HJ_STATE_GPIO_Port,HJ_STATE_Pin)!=GPIO_PIN_SET ||
       npl_sensor_view.session!=np_sd_log.config.session ||
       npl_sensor_view.hz!=np_mode_hz || npl_sensor_view.mask!=np_mode_mask ||
       npl_sensor_view.state!=np_sd_log.state || npl_mode_target){
        npl_sensor_view.active=0U;return 0;
    }
    return 1;
}
#else
#define NPL_SENSOR_CAPABILITY 0U
static int npl_sensor_lease_live(uint32_t now){(void)now;return 0;}
#endif
static int npl_sensor_command(const uint8_t *request,uint8_t *reply)
{
    if(npl_u32(request)!=NPL_SENSOR_QUERY)return 0;
    uint32_t op=npl_u32(request+8),id=npl_u32(request+12);
    uint32_t token=npl_u32(request+16),offset=npl_u32(request+20),error=0U;
    if(npl_u32(request+4)!=1U || !id || op>1U ||
       npl_u32(request+60)!=npl_crc(request,60U) ||
       (!op?(token || offset):(!token || !offset || offset>=140U || offset%28U)))error=1U;
    for(unsigned i=24U;i<60U;i++)if(request[i])error=1U;
    memset(reply,0,64U);
    npl_put(reply,NPL_SENSOR_REPLY);npl_put(reply+4,1U);
    npl_put(reply+8,op);npl_put(reply+12,id);npl_put(reply+24,offset);
#if NP_SD_SENSORS
    uint32_t now=HAL_GetTick();
    int live=npl_sensor_lease_live(now);
    if(!error){
        if(HAL_GPIO_ReadPin(HJ_STATE_GPIO_Port,HJ_STATE_Pin)!=GPIO_PIN_SET ||
           np_sd_log.count>=16U || np_sd_mailbox.command || np_sd_readback.command ||
           npl_mode_target || npl_delete_phase || NP_ConfigTransferPending()
           )error=4U;
        else if(!op){
            const XE_NP_SensorSnapshot *published=XE_NP_SensorsPort_GetSnapshot();
            if(!published->magic && !published->version)error=4U; /* Cache not initialized yet. */
            else if(published->magic!=0x31534e53U || published->version!=1U)error=2U;
            else if(npl_sensor_view.token==UINT32_MAX)error=4U; /* Never alias an old view. */
            else {
                npl_sensor_view.snapshot=*published;
                XE_NP_Sensors_RefreshValidity(&npl_sensor_view.snapshot,now);
                npl_sensor_view.token++;npl_sensor_view.active=1U;
                npl_sensor_view.session=np_sd_log.config.session;
                npl_sensor_view.hz=np_mode_hz;npl_sensor_view.mask=np_mode_mask;
                npl_sensor_view.state=np_sd_log.state;npl_sensor_view.expires=now+1000U;
            }
        }else if(!live || token!=npl_sensor_view.token)error=3U;
        if(!error){
            npl_put(reply+20,npl_sensor_view.token);npl_put(reply+28,140U);
            memcpy(reply+32,(const uint8_t *)&npl_sensor_view.snapshot+offset,28U);
            /* Last fragment is now copied into the independent reply mailbox. */
            if(offset==112U)npl_sensor_view.active=0U;
        }
    }
#else
    if(!error)error=2U;
#endif
    npl_put(reply+16,error);npl_put(reply+60,npl_crc(reply,60U));return 1;
}
