#ifndef NP_CL_CONTROL_PORT_H
#define NP_CL_CONTROL_PORT_H
#include <stdint.h>
#ifndef NP_CL_ENABLE
#define NP_CL_ENABLE 0
#endif
#if NP_CL_ENABLE
/* Foreground magic-demux only, under the existing mailbox owner/generation.
 * No new BLE/USB command number; never called from EP0/USART callbacks. */
int NP_ClosedLoopControl(const uint8_t request[64],uint8_t reply[64]);
#endif
#endif
