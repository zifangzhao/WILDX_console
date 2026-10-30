#ifndef XE_NP_NP2_USB_WIRE_H
#define XE_NP_NP2_USB_WIRE_H
#include <stdint.h>
#include <string.h>
#ifndef XE_NP_NP2_USB_SINGLE_LANE_3BIT
#define XE_NP_NP2_USB_SINGLE_LANE_3BIT 0U
#endif
#ifndef XE_NP_NP2_USB_V3_ENABLE
#define XE_NP_NP2_USB_V3_ENABLE 0U
#endif
#ifndef XE_NP_NP2_USB_PACKED_DUAL
/* Opt-in until device throughput/retention is qualified. No wire loss: only
 * the two unselected high PSSI bits are omitted for connectors 1+2. */
#define XE_NP_NP2_USB_PACKED_DUAL 0U
#endif
#ifndef XE_NP_NP2_CAPTURE_16BIT_PSSI
#define XE_NP_NP2_CAPTURE_16BIT_PSSI 0U
#endif
#ifndef XE_NP_NP2_USB_V3_PROBE_MASK
#define XE_NP_NP2_USB_V3_PROBE_MASK 2U
#endif
#ifndef XE_NP_NP2_USB_V3_LIVE_PROBE_MASK
#define XE_NP_NP2_USB_V3_LIVE_PROBE_MASK XE_NP_NP2_USB_V3_PROBE_MASK
#endif
#define XE_NP_NP2_USB_HEADER_BYTES 512U
#if (XE_NP_NP2_USB_V3_ENABLE != 0U)
#if (XE_NP_NP2_USB_V3_PROBE_MASK == 0U) || (XE_NP_NP2_USB_V3_PROBE_MASK > 7U) || \
    ((XE_NP_NP2_USB_V3_LIVE_PROBE_MASK & ~XE_NP_NP2_USB_V3_PROBE_MASK) != 0U)
#error "NP2USB03 requires a valid stored/live connector mask"
#endif
#define XE_NP_NP2_USB_V3_PROBE_COUNT \
    (((XE_NP_NP2_USB_V3_PROBE_MASK&1U)!=0U)+((XE_NP_NP2_USB_V3_PROBE_MASK&2U)!=0U)+ \
     ((XE_NP_NP2_USB_V3_PROBE_MASK&4U)!=0U))
#define XE_NP_NP2_USB_PAYLOAD_BYTES 46080U
#define XE_NP_NP2_USB_FORMAT_VERSION 3U
#if (XE_NP_NP2_USB_V3_PROBE_COUNT == 1U)
#define XE_NP_NP2_USB_INPUT_TICKS 122880U
#define XE_NP_NP2_USB_STORED_BITS 3U
#define XE_NP_NP2_USB_CODEC 4U
#elif (XE_NP_NP2_USB_V3_PROBE_MASK == 3U)
#if (XE_NP_NP2_USB_PACKED_DUAL != 0U)
#define XE_NP_NP2_USB_INPUT_TICKS 61440U
#define XE_NP_NP2_USB_STORED_BITS 6U
#define XE_NP_NP2_USB_CODEC 4U
#else
#define XE_NP_NP2_USB_INPUT_TICKS 46080U
#define XE_NP_NP2_USB_STORED_BITS 8U
#define XE_NP_NP2_USB_CODEC 5U
#endif
#elif (XE_NP_NP2_USB_V3_PROBE_MASK == 7U)
#define XE_NP_NP2_USB_INPUT_TICKS 40960U
#define XE_NP_NP2_USB_STORED_BITS 9U
#define XE_NP_NP2_USB_CODEC 6U
#else
#error "NP2 performance profile supports one probe, U$1+U$3, or all three"
#endif
#define XE_NP_NP2_USB_MAGIC "NP2USB03"
#elif (XE_NP_NP2_USB_SINGLE_LANE_3BIT != 0U)
#define XE_NP_NP2_USB_INPUT_TICKS 122880U
#define XE_NP_NP2_USB_PAYLOAD_BYTES 46080U
#define XE_NP_NP2_USB_FORMAT_VERSION 2U
#define XE_NP_NP2_USB_STORED_BITS 3U
#define XE_NP_NP2_USB_CODEC 3U
#define XE_NP_NP2_USB_MAGIC "NP2USB02"
#else
#define XE_NP_NP2_USB_INPUT_TICKS 61440U
#define XE_NP_NP2_USB_PAYLOAD_BYTES 46080U
#define XE_NP_NP2_USB_FORMAT_VERSION 1U
#define XE_NP_NP2_USB_STORED_BITS 6U
#define XE_NP_NP2_USB_CODEC 2U
#define XE_NP_NP2_USB_MAGIC "NP2USB01"
#endif
#define XE_NP_NP2_USB_BLOCK_BYTES 46592U
#define XE_NP_NP2_USB_SLOTS 8U
#define XE_NP_NP2_USB_STATUS_MAGIC 0x3155324eU /* N2U1 */
#define XE_NP_NP2_USB_PROFILE_MAGIC 0x3143324eU /* N2C1 */
#define XE_NP_NP2_USB_RATE_20K_MASK 2U
#define XE_NP_NP2_USB_RATE_15K_MASK 1U
#define XE_NP_NP2_USB_RATE_30K_MASK 4U
#define XE_NP_NP2_USB_V3_VERSION 3U
#define XE_NP_NP2_USB_V3_CODEC_PACKED 4U /* selected 3-bit connector groups */
#define XE_NP_NP2_USB_V3_CODEC_RAW8 5U /* two connectors: PSSI D[7:0] verbatim */
#define XE_NP_NP2_USB_V3_CODEC_PSSI8_SPI_D8 6U /* three: raw PSSI then SPI-packed D8 */
#define XE_NP_NP2_USB_V3_CODEC XE_NP_NP2_USB_V3_CODEC_PACKED
#define XE_NP_NP2_USB_V3_MCLK_HZ 32000000U
#define XE_NP_NP2_USB_V3_SAMPLE_HZ 20000U
#define XE_NP_NP2_USB_V3_FLAG_UNCALIBRATED_RAW 1U
#define XE_NP_NP2_USB_V3_FLAG_PARTIAL_LIVE 2U
#define XE_NP_NP2_USB_V3_FLAG_SYNTHETIC 4U
#define XE_NP_NP2_USB_V3_FLAG_NO_PAYLOAD_CRC 8U
#define XE_NP_NP2_USB_V3_FLAG_HOST_SYNC_CHECK 16U
#define XE_NP_NP2_USB_V3_FLAG_NO_HEADER_CRC 32U
#define XE_NP_NP2_USB_V3_FLAG_RAW_D8_PHASE 64U /* codec 6 SPI plane stays unshifted */
#define XE_NP_NP2_USB_V3_FLAG_SENSOR_SNAPSHOT 128U /* negotiated sparse SNS1, never EP0 during RAW_IO */
#define XE_NP_NP2_USB_V3_FLAG_SOURCE_GAPS 256U /* SD best-effort: dense stored sequence, absolute source gaps */
#define XE_NP_NP2_USB_V3_FLAG_TIMING 512U /* stopped-negotiated NPTB R2, never implicit */
#define XE_NP_NP2_USB_V3_FLAG_CLOSED_LOOP 1024U /* negotiated copied-route NPCL metadata */
#define XE_NP_NP2_USB_V3_FLAG_SD_SENSOR_TAIL 2048U /* SD-only sparse SNS2, sensor CRC only */
/* Bit 4096 is reserved for historical sensor-fine recordings; never emitted. */
#define XE_NP_NP2_USB_SD_SENSOR_OFFSET 396U
#define XE_NP_NP2_USB_SD_SENSOR_BYTES 116U
#ifndef XE_NP_NP2_SD_SENSOR_SNAPSHOT_ENABLE
#define XE_NP_NP2_SD_SENSOR_SNAPSHOT_ENABLE 0U
#endif
#if XE_NP_NP2_SD_SENSOR_SNAPSHOT_ENABLE!=0U && XE_NP_NP2_SD_SENSOR_SNAPSHOT_ENABLE!=1U
#error "SD sensor snapshot must be an explicit boolean"
#endif
#define XE_NP_NP2_USB_TIMING_OFFSET 396U
#define XE_NP_NP2_USB_TIMING_BYTES 108U
#define XE_NP_NP2_USB_SENSOR_OFFSET 256U
#define XE_NP_NP2_USB_SENSOR_BYTES 140U
/* NP2USB01 is raw six-wire packing; NP2USB02 is lossless U$3-only PSSI[5:3]
 * packing. Neither is the NP1 A512 stream nor the SD v9 container. Frames are
 * not rebased, corrected or called neural.
 * LE header offsets: seq64@24, absolute source tick64@32, ticks@40,
 * MCLK@44, active wire mask@48, flags@52, optional payload CRC32@56,
 * SuperSync[12]@64, serial[8]@80, model[8]@88, profile@96 (ground bank0=1),
 * stored bits@100, codec@104.
 * Legacy header CRC32@508 covers bytes0..507. Performance profiles set both
 * NO_PAYLOAD_CRC and NO_HEADER_CRC and store zero in both CRC fields, avoiding
 * every application CRC memory pass; native USB/SD link CRC remains active.
 * Each packet is exactly91 HS packets; START requests a bounded block count. */
static inline void XE_NP_Np2Usb_Put32(uint8_t *p,uint32_t x)
{p[0]=(uint8_t)x;p[1]=(uint8_t)(x>>8);p[2]=(uint8_t)(x>>16);p[3]=(uint8_t)(x>>24);}
static inline void XE_NP_Np2Usb_Put64(uint8_t *p,uint64_t x)
{XE_NP_Np2Usb_Put32(p,(uint32_t)x);XE_NP_Np2Usb_Put32(p+4,(uint32_t)(x>>32));}
static inline void XE_NP_Np2Usb_Header(uint8_t *p,uint64_t seq,uint64_t start,
    uint32_t payload_crc,const uint8_t serial[8],const uint8_t model[8])
{
    static const uint8_t sync[12]={0xD3,0x6A,0x59,0xC5,0x3E,0xA7,0x1D,0xE4,0xB2,0x4F,0x78,0x96};
    memset(p,0,XE_NP_NP2_USB_HEADER_BYTES);memcpy(p,XE_NP_NP2_USB_MAGIC,8);
    XE_NP_Np2Usb_Put32(p+8,XE_NP_NP2_USB_FORMAT_VERSION);XE_NP_Np2Usb_Put32(p+12,XE_NP_NP2_USB_HEADER_BYTES);
    XE_NP_Np2Usb_Put32(p+16,XE_NP_NP2_USB_BLOCK_BYTES);XE_NP_Np2Usb_Put32(p+20,XE_NP_NP2_USB_PAYLOAD_BYTES);
    XE_NP_Np2Usb_Put64(p+24,seq);XE_NP_Np2Usb_Put64(p+32,start);
    XE_NP_Np2Usb_Put32(p+40,XE_NP_NP2_USB_INPUT_TICKS);XE_NP_Np2Usb_Put32(p+44,48000000U);
    XE_NP_Np2Usb_Put32(p+48,0x38U);XE_NP_Np2Usb_Put32(p+52,1U);
    XE_NP_Np2Usb_Put32(p+56,payload_crc);memcpy(p+64,sync,12);
    memcpy(p+80,serial,8);memcpy(p+88,model,8);XE_NP_Np2Usb_Put32(p+96,1);
    XE_NP_Np2Usb_Put32(p+100,XE_NP_NP2_USB_STORED_BITS);XE_NP_Np2Usb_Put32(p+104,XE_NP_NP2_USB_CODEC);
}

typedef struct {
    uint32_t mclk_hz,sample_hz;
    uint32_t probe_mask,live_probe_mask,flags,ingress_bytes_per_tick;
    uint32_t input_ticks,stored_bits,codec;
    uint8_t serial[3][8],model[3][8];
    uint32_t profile_id[3],phase[3];
    uint32_t d8_phase_bits,d8_lookahead_bytes;
} XE_NP_Np2UsbV3Config;

/* Performance profile used by both the firmware and host:
 *   one probe:  122,880 ticks, selected 3-bit packing (12 MB/s at 20 kS/s)
 *   two probes:  46,080 ticks, raw PSSI D[7:0] (32 MB/s, no bit packer)
 *                opt-in packed dual: 61,440 ticks, selected six bits
 *                (24 MB/s at 20 kS/s; 36 MB/s at 30 kS/s).
 *   three:       40,960 ticks, 40,960 raw PSSI bytes followed by 5,120
 *                SPI2-packed D8 bytes (36 MB/s, no 9-bit CPU packer).
 * Two-probe raw8 is deliberately limited to U$1+U$3: any pair containing U$4
 * needs D8 and therefore uses the three-probe split capture topology. */
static inline int XE_NP_Np2UsbV3_PerformanceProfile(uint32_t probe_mask,
    uint32_t *ticks,uint32_t *stored_bits,uint32_t *codec)
{
    uint32_t probes=((probe_mask&1U)!=0U)+((probe_mask&2U)!=0U)+((probe_mask&4U)!=0U);
    if(!ticks || !stored_bits || !codec || probe_mask==0U || (probe_mask&~7U)!=0U)return -1;
    if(probes==1U && (probe_mask&4U)==0U) {
        *ticks=122880U;*stored_bits=3U;*codec=XE_NP_NP2_USB_V3_CODEC_PACKED;return 0;
    }
    if(probe_mask==3U) {
#if (XE_NP_NP2_USB_PACKED_DUAL != 0U)
        *ticks=61440U;*stored_bits=6U;*codec=XE_NP_NP2_USB_V3_CODEC_PACKED;return 0;
#else
        *ticks=46080U;*stored_bits=8U;*codec=XE_NP_NP2_USB_V3_CODEC_RAW8;return 0;
#endif
    }
    if(probe_mask==7U) {
        *ticks=40960U;*stored_bits=9U;*codec=XE_NP_NP2_USB_V3_CODEC_PSSI8_SPI_D8;return 0;
    }
    return -1;
}

static inline uint32_t XE_NP_Np2UsbV3_WireMask(uint32_t probe_mask)
{
    uint32_t result=0U;
    if(probe_mask&1U)result|=0x007U;
    if(probe_mask&2U)result|=0x038U;
    if(probe_mask&4U)result|=0x1C0U;
    return result;
}

/* NP2USB03 LE header additions: sample rate@60, connector probe mask@76,
 * independently verified/live probe mask@80, PSSI-ring bytes/tick@84 (one for
 * the production PSSI8+SPI2 path; two only for the legacy 16-bit fallback),
 * serial[3][8]@96, model[3][8]@120, profile IDs@144, stored bits@156,
 * codec@160 and independently acquired phases@164. Header flags remain @52:
 * raw=bit0, stored-but-not-live diagnostic lanes=bit1, generated source=bit2.
 * Unselected identity/profile/phase entries must be zero. Opt-in flag128 permits
 * an opaque SNS1 snapshot at256..395 (magic0 means absent, other bytes may be
 * stale). It is incompatible with raw D8 lookahead64 and requires NO_HEADER_CRC.
 * All legacy clients retain zero reserved bytes unless stopped OUT0x7C enables
 * this extension. Packet size, payload, codec and native link CRC are unchanged. */
/* Stopped0x80 negotiation additionally permits NPTB R2 at396..503 under flag512.
 * It works without SNS1;504..507 stay zero. Raw-D8 metadata is not compatible.
 * Unnegotiated bytes and flags retain the original contract. */
/* SD-only flag2048 permits SNS2 at396..511:112B retagged SNS1 prefix and
 * IEEE CRC32 of those112B at508. An absent snapshot is all116B zero. This
 * requires NO_HEADER_CRC, excludes SNS1/NPTB, and preserves raw-D8/NPCL areas.
 * No physical1250Hz sensor sampling is implied; the host may hold native
 * timestamped observations on its1250Hz analogin grid. */
/* SD-only bit256 explicitly permits forward whole-input-block source gaps.
 * Stored sequence stays dense; source@32 never rebases. No per-block counter
 * field is added. Optional stopped UTC statistics retain the loss counters. */
static inline int XE_NP_Np2Usb_HeaderV3(uint8_t *p,uint64_t seq,uint64_t start,
    uint32_t payload_crc,const XE_NP_Np2UsbV3Config *config)
{
    static const uint8_t sync[12]={0xD3,0x6A,0x59,0xC5,0x3E,0xA7,0x1D,0xE4,0xB2,0x4F,0x78,0x96};
    uint32_t expected_ticks,expected_bits,expected_codec;
    const uint32_t allowed_flags=767U
#if XE_NP_NP2_SD_DRAIN_OVERFLOW_TEST
        |XE_NP_NP2_USB_V3_FLAG_SOURCE_GAPS
#endif
#if XE_NP_NP2_CLOSED_LOOP_CARRIER_ENABLE || XE_NP_NP2_CLOSED_LOOP_SD_FORMAT_ENABLE
        |XE_NP_NP2_USB_V3_FLAG_CLOSED_LOOP
#endif
#if XE_NP_NP2_SD_SENSOR_SNAPSHOT_ENABLE
        |XE_NP_NP2_USB_V3_FLAG_SD_SENSOR_TAIL
#endif
        ;
    /* USB can choose the existing lossless six-bit layout at 30 kHz while
     * retaining raw8 at lower rates. Validate its wire geometry, not the
     * compile-time default; the desktop already accepts both dual layouts. */
    int compact_dual=config && config->probe_mask==3U &&
        config->input_ticks==61440U && config->stored_bits==6U &&
        config->codec==XE_NP_NP2_USB_V3_CODEC_PACKED;
    if(!p || !config || config->probe_mask==0U || (config->probe_mask&~7U)!=0U ||
       (config->live_probe_mask&~config->probe_mask)!=0U ||
       (config->sample_hz!=15000U && config->sample_hz!=20000U && config->sample_hz!=30000U
#if XE_NP_NP2_DUAL_SD_APP_ENABLE && XE_NP_NP2_LIVE_SD_RUNTIME_PROFILES
        && config->sample_hz!=7500U
#endif
       ) ||
       config->mclk_hz!=config->sample_hz*1600U ||
       (config->ingress_bytes_per_tick!=1U && config->ingress_bytes_per_tick!=2U) ||
       (config->ingress_bytes_per_tick==1U && (config->probe_mask&4U)!=0U &&
        config->codec!=XE_NP_NP2_USB_V3_CODEC_PSSI8_SPI_D8) ||
       (config->flags&XE_NP_NP2_USB_V3_FLAG_UNCALIBRATED_RAW)==0U ||
       (config->flags&~allowed_flags)!=0U ||
       ((config->flags&XE_NP_NP2_USB_V3_FLAG_SD_SENSOR_TAIL)!=0U &&
        ((config->flags&(XE_NP_NP2_USB_V3_FLAG_SENSOR_SNAPSHOT|XE_NP_NP2_USB_V3_FLAG_TIMING))!=0U ||
         (config->flags&XE_NP_NP2_USB_V3_FLAG_NO_HEADER_CRC)==0U)) ||
#if XE_NP_NP2_CLOSED_LOOP_CARRIER_ENABLE || XE_NP_NP2_CLOSED_LOOP_SD_FORMAT_ENABLE
       ((config->flags&XE_NP_NP2_USB_V3_FLAG_CLOSED_LOOP)!=0U &&
        ((config->flags&XE_NP_NP2_USB_V3_FLAG_RAW_D8_PHASE)!=0U ||
         (config->flags&XE_NP_NP2_USB_V3_FLAG_NO_HEADER_CRC)==0U)) ||
#endif
       ((config->flags&XE_NP_NP2_USB_V3_FLAG_TIMING)!=0U &&
        ((config->flags&XE_NP_NP2_USB_V3_FLAG_RAW_D8_PHASE)!=0U ||
         (config->flags&XE_NP_NP2_USB_V3_FLAG_NO_HEADER_CRC)==0U)) ||
       ((config->flags&XE_NP_NP2_USB_V3_FLAG_SENSOR_SNAPSHOT)!=0U &&
        ((config->flags&XE_NP_NP2_USB_V3_FLAG_RAW_D8_PHASE)!=0U ||
         (config->flags&XE_NP_NP2_USB_V3_FLAG_NO_HEADER_CRC)==0U)) ||
       ((config->flags&XE_NP_NP2_USB_V3_FLAG_RAW_D8_PHASE)!=0U &&
        (config->codec!=XE_NP_NP2_USB_V3_CODEC_PSSI8_SPI_D8 ||
         config->d8_phase_bits>=1600U ||
         config->d8_lookahead_bytes!=(config->d8_phase_bits+7U)/8U)) ||
       ((config->flags&XE_NP_NP2_USB_V3_FLAG_RAW_D8_PHASE)==0U &&
        (config->d8_phase_bits!=0U || config->d8_lookahead_bytes!=0U)) ||
       (config->live_probe_mask!=config->probe_mask &&
        (config->flags&XE_NP_NP2_USB_V3_FLAG_PARTIAL_LIVE)==0U) ||
       (config->live_probe_mask==0U &&
        (config->flags&XE_NP_NP2_USB_V3_FLAG_SYNTHETIC)==0U) ||
       XE_NP_Np2UsbV3_PerformanceProfile(config->probe_mask,&expected_ticks,
                                         &expected_bits,&expected_codec)!=0 ||
       (!compact_dual && (config->input_ticks!=expected_ticks ||
        config->stored_bits!=expected_bits || config->codec!=expected_codec)) ||
       (((config->flags&XE_NP_NP2_USB_V3_FLAG_NO_PAYLOAD_CRC)!=0U) != (payload_crc==0U)))return -1;
    memset(p,0,XE_NP_NP2_USB_HEADER_BYTES);memcpy(p,"NP2USB03",8U);
    XE_NP_Np2Usb_Put32(p+8,XE_NP_NP2_USB_V3_VERSION);
    XE_NP_Np2Usb_Put32(p+12,XE_NP_NP2_USB_HEADER_BYTES);
    XE_NP_Np2Usb_Put32(p+16,XE_NP_NP2_USB_BLOCK_BYTES);
    XE_NP_Np2Usb_Put32(p+20,XE_NP_NP2_USB_PAYLOAD_BYTES);
    XE_NP_Np2Usb_Put64(p+24,seq);XE_NP_Np2Usb_Put64(p+32,start);
    XE_NP_Np2Usb_Put32(p+40,config->input_ticks);XE_NP_Np2Usb_Put32(p+44,config->mclk_hz);
    XE_NP_Np2Usb_Put32(p+48,XE_NP_Np2UsbV3_WireMask(config->probe_mask));
    XE_NP_Np2Usb_Put32(p+52,config->flags);XE_NP_Np2Usb_Put32(p+56,payload_crc);
    XE_NP_Np2Usb_Put32(p+60,config->sample_hz);memcpy(p+64,sync,sizeof(sync));
    XE_NP_Np2Usb_Put32(p+76,config->probe_mask);
    XE_NP_Np2Usb_Put32(p+80,config->live_probe_mask);
    XE_NP_Np2Usb_Put32(p+84,config->ingress_bytes_per_tick);
    memcpy(p+96,config->serial,sizeof(config->serial));
    memcpy(p+120,config->model,sizeof(config->model));
    for(uint32_t n=0U;n<3U;n++) {
        XE_NP_Np2Usb_Put32(p+144U+n*4U,config->profile_id[n]);
        XE_NP_Np2Usb_Put32(p+164U+n*4U,config->phase[n]);
    }
    XE_NP_Np2Usb_Put32(p+156,config->stored_bits);XE_NP_Np2Usb_Put32(p+160,config->codec);
    XE_NP_Np2Usb_Put32(p+176U,config->d8_phase_bits);
    XE_NP_Np2Usb_Put32(p+180U,config->d8_lookahead_bytes);
    return 0;
}
#endif
