/* Experimental startup-only common reset release. Never called while an
 * established recording is streaming. Stop the external MCO so the shared
 * reset level is settled before the first clock edge seen by all probes.
 * Do not change PLL, divider, pad configuration or acquisition DMA routing. */
volatile uint32_t np_clocked_reset_diag[4];
static void NP_ModeReleaseReset(GPIO_TypeDef *port,uint16_t pin)
{
    uint32_t irq=__get_PRIMASK();
    __disable_irq();
    uint32_t source=RCC->CFGR1 & RCC_CFGR1_MCOSEL;
    CLEAR_BIT(RCC->CFGR1,RCC_CFGR1_MCOSEL);
    __DSB();
    for(unsigned n=0;n<64U;n++)__NOP();
    port->BSRR=pin;
    __DSB();
    for(unsigned n=0;n<64U;n++)__NOP();
    MODIFY_REG(RCC->CFGR1,RCC_CFGR1_MCOSEL,source);
    __DSB();
    np_clocked_reset_diag[0]++;
    np_clocked_reset_diag[1]=source;
    np_clocked_reset_diag[2]=pin;
    np_clocked_reset_diag[3]=RCC->CFGR1;
    __set_PRIMASK(irq);
}
