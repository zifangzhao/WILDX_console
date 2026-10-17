#ifndef NP_PROBE_IDENTITY_H
#define NP_PROBE_IDENTITY_H
#include <stdint.h>
#include <stddef.h>
#include <string.h>

enum { NP_MODEL_UNKNOWN=0, NP_MODEL_NRIC1384=1, NP_MODEL_FULL_NP1=2 };
enum { NP_ID_OK=0, NP_ID_MISSING=1, NP_ID_IO=2, NP_ID_UNSTABLE=3,
       NP_ID_INVALID=4, NP_ID_UNSUPPORTED=5, NP_ID_CONFLICT=6 };
typedef struct {
    uint32_t model, reason, source; /* 0 unknown, 1 EEPROM, 2 explicit, 3 absent-default */
    uint32_t serial_lo, serial_hi;
    uint8_t version, revision;
    char flex[21], part[41];
} NP_ProbeIdentity;
/* Published only after the two-read EEPROM qualification in auto_model.inc.
 * Consumers treat it as cached telemetry and must not initiate I2C while an
 * acquisition is running. */
extern volatile NP_ProbeIdentity np_probe_identity[3];

static int np_id_string(char *out,const uint8_t *in,size_t n) {
    size_t end=0;
    while(end<n && in[end]!=0 && in[end]!=0xff) {
        if(in[end]<32 || in[end]>126)return 0;
        out[end]=(char)in[end];end++;
    }
    out[end]=0;
    return end!=0;
}

/* User-approved policy: confirmed EEPROM absence defaults to NRIC.
   IO faults, erased/unknown EEPROM, flex label, ASIC ACK or failed NP1
   configuration must not select that fallback. Two reads must agree. */
static NP_ProbeIdentity np_identify(const uint8_t *a,const uint8_t *b,
                                  uint32_t read_reason,uint32_t explicit_model) {
    NP_ProbeIdentity id;memset(&id,0,sizeof(id));id.reason=read_reason;
    if(read_reason==NP_ID_MISSING) {
        id.model=explicit_model ? explicit_model : NP_MODEL_NRIC1384;
        if(id.model!=NP_MODEL_NRIC1384 && id.model!=NP_MODEL_FULL_NP1) {
            id.model=NP_MODEL_UNKNOWN;id.reason=NP_ID_INVALID;return id;
        }
        id.source=explicit_model ? 2 : 3;id.reason=NP_ID_OK;
        return id;
    }
    if(read_reason!=NP_ID_OK || !a || !b)return id;
    if(memcmp(a,b,80)){id.reason=NP_ID_UNSTABLE;return id;}
    for(unsigned i=0;i<4;i++) {
        id.serial_lo|=(uint32_t)a[i]<<(8*i);
        id.serial_hi|=(uint32_t)a[4+i]<<(8*i);
    }
    id.version=a[10];id.revision=a[11];
    if((!id.serial_lo && !id.serial_hi) ||
       (id.serial_lo==UINT32_MAX && id.serial_hi==UINT32_MAX) ||
       !np_id_string(id.flex,a+20,20) || !np_id_string(id.part,a+40,40)) {
        id.reason=NP_ID_INVALID;return id;
    }
    /* Official NP1 metal/silicon-cap variants share the configuration profile.
       Keep exact identities; never guess from a family prefix. */
    if(!strcmp(id.part,"PRB_1_4_0480_1_C") ||
       !strcmp(id.part,"PRB_1_4_0480_1"))id.model=NP_MODEL_FULL_NP1;
    else {id.reason=NP_ID_UNSUPPORTED;return id;}
    if(explicit_model && explicit_model!=id.model) {
        id.model=NP_MODEL_UNKNOWN;id.reason=NP_ID_CONFLICT;return id;
    }
    id.reason=NP_ID_OK;id.source=1;return id;
}
#endif
