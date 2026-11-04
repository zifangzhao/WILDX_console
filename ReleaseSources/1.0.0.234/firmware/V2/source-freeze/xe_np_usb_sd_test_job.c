#include "xe_np_usb_sd_test_job.h"
#if XE_NP_USB_SD_TEST_ENABLE
#include "xe_np_hal.h"
#include "xe_np_boot_contract.h"
#include "np_v2_usb_sd_retained.h"
#include <string.h>

/* BKPSRAM is a separate peripheral address space. This cold control journal
 * does not consume a neural/AUX payload pool or require a linker carveout. */
_Static_assert(NP_V2_SD_RETAINED_BANK_BYTES<=512U,"bounded retained control state");
static uint8_t cached_report[256] __attribute__((aligned(32)));
static int app_dispatch,job_active,boot_result,local_control,baseline_fresh;
static uint32_t image_role;
#ifndef XE_NP_USB_SD_TEST_APP_CRC32
#define XE_NP_USB_SD_TEST_APP_CRC32 0U
#endif
uint32_t XE_NP_UsbSdTest_Capability(void)
{
    const XE_NP_BootManifest *app=(const XE_NP_BootManifest *)XE_NP_BOOT_MANIFEST_BASE;
    return XE_NP_USB_SD_TEST_APP_CRC32 && app->image_crc32==XE_NP_USB_SD_TEST_APP_CRC32?
        NP_USB_SD_TEST_CAP_PROPOSED:0U;
}
static void barrier(void *unused){(void)unused;__DSB();}
static const NpV2SdRetained store={(volatile uint8_t *)BKPSRAM_BASE,barrier,0};

static uint32_t open_bank(void)
{
    uint32_t enabled=RCC->AHB1ENR&RCC_AHB1ENR_BKPSRAMEN;
    __HAL_RCC_PWR_CLK_ENABLE();
    HAL_PWR_EnableBkUpAccess();
    __HAL_RCC_BKPSRAM_CLK_ENABLE();__DSB();
    return enabled;
}
static void close_bank(uint32_t enabled)
{__DSB();if(!enabled)__HAL_RCC_BKPSRAM_CLK_DISABLE();}

static uint32_t reset_cause(uint32_t *raw,uint32_t *cleared)
{
    uint32_t result=0U;*raw=RCC->CSR;
    if(__HAL_RCC_GET_FLAG(RCC_FLAG_SFTRST))result|=NP_V2_SD_RESET_SOFTWARE;
    if(__HAL_RCC_GET_FLAG(RCC_FLAG_BORRST))result|=NP_V2_SD_RESET_POWER;
    /* On this U595, the observed normal SYSRESETREQ path sets SFTRSTF and
     * PINRSTF together (startup CSR 0x14004400). PIN accompanies that software
     * class; PIN alone remains external reset. All other reset classes are
     * retained below and still make the one-shot handoff fail closed. */
    if(__HAL_RCC_GET_FLAG(RCC_FLAG_PINRST) && !(result&NP_V2_SD_RESET_SOFTWARE))
        result|=NP_V2_SD_RESET_PIN;
    if(__HAL_RCC_GET_FLAG(RCC_FLAG_IWDGRST) || __HAL_RCC_GET_FLAG(RCC_FLAG_WWDGRST))
        result|=NP_V2_SD_RESET_WATCHDOG;
    if(__HAL_RCC_GET_FLAG(RCC_FLAG_OBLRST) || __HAL_RCC_GET_FLAG(RCC_FLAG_LPWRRST))
        result|=NP_V2_SD_RESET_OTHER;
#ifdef RCC_FLAG_FWRRST
    if(__HAL_RCC_GET_FLAG(RCC_FLAG_FWRRST))result|=NP_V2_SD_RESET_OTHER;
#endif
    if(*raw&0x01000000U)result|=NP_V2_SD_RESET_OTHER;
    if(!result)result=NP_V2_SD_RESET_OTHER;
    /* Retire sticky history once, before any future expected software handoff.
     * A mixed reset is never silently reduced to its software component. */
    __HAL_RCC_CLEAR_RESET_FLAGS();__DSB();*cleared=RCC->CSR;return result;
}
static void reset_diagnostic(uint32_t role,uint32_t raw,uint32_t cause,uint32_t cleared,int result)
{
    /* Outside the 416-byte integrity-protected job record: retain both the
     * last USB baseline and APP observation without replacing raw evidence. */
    volatile uint32_t *d=(volatile uint32_t *)(BKPSRAM_BASE+416U);
    if(d[0]!=0x3152444EU){for(unsigned i=0U;i<10U;i++)d[i]=UINT32_MAX;d[1]=1U;}
    unsigned at=role==NP_V2_SD_ROLE_APP?6U:2U;
    d[at]=raw;d[at+1U]=cause;d[at+2U]=cleared;d[at+3U]=(uint32_t)result;
    __DSB();d[0]=0x3152444EU;__DSB();
}

static void base_report(NpUsbSdTestReport *r,int interrupted)
{
    np_usb_sd_report_init(r);
    r->words[3]=XE_NP_UsbSdTest_Capability();
    r->words[4]=interrupted?NP_USB_SD_PHASE_INTERRUPTED_UNKNOWN:NP_USB_SD_PHASE_NONE;
    r->words[5]=interrupted?NP_USB_SD_OUTCOME_INTERRUPTED:NP_USB_SD_OUTCOME_PENDING;
    r->words[7]=interrupted?1U:0U;
    if(!interrupted)r->words[8]=r->words[9]=r->words[10]=r->words[11]=0U;
    r->words[12]=2U;
    if(!interrupted){r->words[13]=r->words[14]=r->words[15]=0U;r->words[16]=UINT32_MAX;}
    r->words[17]=HAL_GetUIDw0();r->words[18]=HAL_GetUIDw1();r->words[19]=HAL_GetUIDw2();
    r->words[27]=0xF000F0F0U; /* admission, not performance qualification */
    r->words[28]=NP_USB_SD_TEST_MAX_SECONDS;
    if(!interrupted)r->words[29]=UINT32_MAX;
}

void XE_NP_UsbSdTest_CacheReport(const NpUsbSdTestReport *r)
{
    uint8_t wire[256];
    if(!np_usb_sd_report_encode(wire,sizeof(wire),r))return;
    uint32_t key=__get_PRIMASK();__disable_irq();
    memcpy(cached_report,wire,sizeof(wire));__DMB();__set_PRIMASK(key);
}
const uint8_t *XE_NP_UsbSdTest_CachedReport(void){return cached_report;}

void XE_NP_UsbSdTest_Boot(uint32_t role)
{
    uint32_t raw,cleared,cause=reset_cause(&raw,&cleared),clock=open_bank();
    image_role=role;app_dispatch=job_active=local_control=0;
    baseline_fresh=role==NP_V2_SD_ROLE_USB && !(cleared&0xFF000000U);
    boot_result=np_v2_sd_retained_boot(&store,cause,role);
    reset_diagnostic(role,raw,cause,cleared,boot_result);
    app_dispatch=boot_result==NP_V2_SD_BOOT_DISPATCH;
    job_active=app_dispatch;
    close_bank(clock);
    NpUsbSdTestReport report;
    (void)XE_NP_UsbSdTest_ReadReport(&report);
    report.words[6]&=~(NP_USB_SD_VALID_IDENTITY|NP_USB_SD_VALID_CATALOG|
        NP_USB_SD_VALID_SOURCE_QUIET|NP_USB_SD_VALID_SD_QUIET|NP_USB_SD_VALID_APPEND);
    for(unsigned i=20U;i<=26U;i++)report.words[i]=UINT32_MAX;
    XE_NP_UsbSdTest_CacheReport(&report);
}

int XE_NP_UsbSdTest_ReadReport(NpUsbSdTestReport *r)
{
    NpV2SdSnapshot s;uint32_t clock=open_bank();
    int result=np_v2_sd_retained_inspect(&store,&s);close_bank(clock);
    if(!r)return 0;
    if(result==NP_V2_SD_VALID && np_usb_sd_report_decode(r,s.report,sizeof(s.report))){
        /* MCU identity is always physical, even before card initialization. */
        r->words[12]=2U;r->words[17]=HAL_GetUIDw0();r->words[18]=HAL_GetUIDw1();r->words[19]=HAL_GetUIDw2();
        if(image_role==NP_V2_SD_ROLE_USB)r->words[3]=XE_NP_UsbSdTest_Capability();
        return 1;
    }
    base_report(r,result==NP_V2_SD_CORRUPT || boot_result<0);return 1;
}

int XE_NP_UsbSdTest_TakeAppPlan(uint8_t plan[128])
{
    if(image_role!=NP_V2_SD_ROLE_APP || !plan)return 0;
    if(!app_dispatch)return boot_result==NP_V2_SD_BOOT_INTERRUPTED?-1:0;
    app_dispatch=0; /* permission is one-shot even if the caller rejects */
    NpV2SdSnapshot s;uint32_t clock=open_bank();
    int valid=np_v2_sd_retained_inspect(&store,&s)==NP_V2_SD_VALID && s.phase==NP_USB_SD_PHASE_RUNNING;
    if(valid)memcpy(plan,s.plan,128U);
    close_bank(clock);
    return valid?1:-1;
}
int XE_NP_UsbSdTest_Active(void){return job_active;}
void XE_NP_UsbSdTest_LocalControl(int local){local_control=local!=0;}
int XE_NP_UsbSdTest_ControlBlocked(void){return job_active && !local_control;}

int XE_NP_UsbSdTest_QueueUsb(const uint8_t plan[128],const NpUsbSdTestReport *r)
{
    uint8_t wire[256];NpUsbSdTestReport owned=*r;
    if(image_role!=NP_V2_SD_ROLE_USB || !baseline_fresh || !XE_NP_UsbSdTest_Capability())return NP_V2_SD_INVALID;
    owned.words[6]|=NP_USB_SD_VALID_HANDOFF;
    if(!np_usb_sd_report_encode(wire,sizeof(wire),&owned))return NP_V2_SD_INVALID;
    uint32_t clock=open_bank();
    int result=(np_usb_sd_u32(plan+56U)&1U)?
        np_v2_sd_retained_submit_reconciled(&store,plan,wire,1):
        np_v2_sd_retained_submit(&store,plan,wire);
    close_bank(clock);
    if(result==NP_V2_SD_NEW){local_control=0;job_active=1;XE_NP_UsbSdTest_CacheReport(&owned);}
    return result;
}
int XE_NP_UsbSdTest_ExactDuplicate(const uint8_t plan[128])
{
    NpV2SdSnapshot s;uint32_t clock=open_bank();
    int same=np_v2_sd_retained_inspect(&store,&s)==NP_V2_SD_VALID && !memcmp(plan,s.plan,128U);
    close_bank(clock);
    if(same){NpUsbSdTestReport r;(void)XE_NP_UsbSdTest_ReadReport(&r);XE_NP_UsbSdTest_CacheReport(&r);}
    return same;
}
int XE_NP_UsbSdTest_AcceptUsb(void)
{
    if(!baseline_fresh)return 0;
    uint32_t clock=open_bank();int result=np_v2_sd_retained_accept(&store);close_bank(clock);
    if(result){NpUsbSdTestReport r;(void)XE_NP_UsbSdTest_ReadReport(&r);XE_NP_UsbSdTest_CacheReport(&r);}
    return result;
}
int XE_NP_UsbSdTest_RejectUsb(const NpUsbSdTestReport *r)
{
    uint8_t wire[256];if(!np_usb_sd_report_encode(wire,sizeof(wire),r))return 0;
    uint32_t clock=open_bank();int result=np_v2_sd_retained_reject(&store,wire);close_bank(clock);
    if(result){job_active=local_control=0;XE_NP_UsbSdTest_CacheReport(r);}return result;
}
int XE_NP_UsbSdTest_FinishApp(const NpUsbSdTestReport *r,int return_usb)
{
    uint8_t wire[256];if(image_role!=NP_V2_SD_ROLE_APP || !np_usb_sd_report_encode(wire,sizeof(wire),r))return 0;
    uint32_t clock=open_bank();
    int result=np_v2_sd_retained_finish(&store,wire,return_usb);close_bank(clock);
    if(!result && !job_active && r->words[4]==NP_USB_SD_PHASE_INTERRUPTED_UNKNOWN &&
       (r->words[6]&(NP_USB_SD_VALID_SOURCE_QUIET|NP_USB_SD_VALID_SD_QUIET|NP_USB_SD_VALID_REJECTED_UNMUTATED))==
       (NP_USB_SD_VALID_SOURCE_QUIET|NP_USB_SD_VALID_SD_QUIET|NP_USB_SD_VALID_REJECTED_UNMUTATED)){
        /* Preserve the old interrupted/corrupt bank. This APP did not START;
         * returning exposes the unknown receipt, it never resolves/replays it. */
        result=1;
    }
    if(!result)return 0;
    job_active=local_control=0;XE_NP_UsbSdTest_CacheReport(r);
    if(return_usb){XE_NP_Boot_RequestDownload();__DSB();NVIC_SystemReset();}
    return 1;
}
#endif
