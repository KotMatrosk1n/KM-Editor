// SPDX-License-Identifier: GPL-3.0-only
#include <emscripten/emscripten.h>
#include <stdlib.h>
#include "libvgmstream.h"

static libvgmstream_t* decoder;
static float pcm[16384 * 8];
int km_opus_open(void);
int km_opus_active(void);
void km_opus_close(void);
double km_opus_info(int field);
int km_opus_render(float* output, int count);
void km_opus_seek(int target);

EMSCRIPTEN_KEEPALIVE void km_audio_close(void) {
    if (decoder) libvgmstream_free(decoder);
    decoder = NULL;
    km_opus_close();
}

EMSCRIPTEN_KEEPALIVE int km_audio_open(int wave) {
    km_audio_close();
    if (!wave) { int result = km_opus_open(); if (result) return result > 0; }
    libstreamfile_t* file = libstreamfile_open_from_stdio(wave ? "/selected.wav" : "/selected.wem");
    if (!file) return 0;
    libvgmstream_config_t config = {0};
    config.ignore_loop = true;
    config.disable_config_override = true;
    config.force_sfmt = LIBVGMSTREAM_SFMT_FLOAT;
    decoder = libvgmstream_create(file, 0, &config);
    libstreamfile_close(file);
    if (!decoder) return 0;
    if (decoder->format->channels < 1 || decoder->format->channels > 8 ||
        decoder->format->sample_rate < 4000 || decoder->format->sample_rate > 192000 ||
        decoder->format->stream_samples <= 0 || decoder->format->stream_samples > 192000LL * 3600) {
        km_audio_close(); return 0;
    }
    return 1;
}

EMSCRIPTEN_KEEPALIVE double km_audio_info(int field) {
    if (km_opus_active()) return km_opus_info(field);
    if (!decoder) return 0;
    const libvgmstream_format_t* info = decoder->format;
    switch (field) {
        case 0: return info->channels;
        case 1: return info->sample_rate;
        case 2: return (double)info->stream_samples;
        case 3: return (double)info->loop_start;
        case 4: return (double)info->loop_end;
        case 5: return info->channel_layout;
        default: return 0;
    }
}

EMSCRIPTEN_KEEPALIVE void km_audio_seek(int sample) {
    if (km_opus_active()) { km_opus_seek(sample); return; }
    if (decoder && sample >= 0) libvgmstream_seek(decoder, sample);
}

EMSCRIPTEN_KEEPALIVE float* km_audio_buffer(void) { return pcm; }

EMSCRIPTEN_KEEPALIVE int km_audio_render(int count) {
    if (km_opus_active()) return km_opus_render(pcm, count);
    if (!decoder || count < 1 || count > 16384) return -1;
    if (libvgmstream_fill(decoder, pcm, count) < 0) return -1;
    return decoder->decoder->buf_samples;
}
