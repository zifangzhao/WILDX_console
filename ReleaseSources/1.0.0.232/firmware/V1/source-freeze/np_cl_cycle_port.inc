/* Read-only candidate instrumentation. DWT is owned/enabled by the existing
 * capture anchor; never enable/reset it or change clocks here. */
#if NP_CL_CYCLE_PROBE
NpClCycleState *NP_ClCycleStateGet(void)
{NpClWorkspace *w=NP_CL_CYCLE_WORKSPACE();return w && w->recording?&w->cycles:0;}
uint32_t NP_ClCycleCounter(void){return DWT->CYCCNT;}
uint32_t NP_ClCycleMs(void){return HAL_GetTick();}
uint32_t NP_ClCycleHz(void){return SystemCoreClock;}
uint32_t NP_ClCycleValid(void)
{
    return SystemCoreClock && !(CoreDebug->DHCSR&CoreDebug_DHCSR_C_DEBUGEN_Msk) &&
        (CoreDebug->DEMCR&CoreDebug_DEMCR_TRCENA_Msk) &&
        (DWT->CTRL&DWT_CTRL_CYCCNTENA_Msk) && !(DWT->CTRL&DWT_CTRL_NOCYCCNT_Msk);
}
#endif
