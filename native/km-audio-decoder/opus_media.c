// SPDX-License-Identifier: GPL-3.0-only
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <opus.h>

static FILE* media;
static OpusDecoder* opus;
static int channels, frames, loop_start, loop_end, delay, position, pending, consumed, skip;
static long start, end;
static unsigned char packet[65536];
static float samples[5760 * 2];

void km_opus_close(void) {
    if (opus) opus_decoder_destroy(opus);
    if (media) fclose(media);
    opus = NULL; media = NULL;
}
int km_opus_active(void) { return opus != NULL; }
static uint32_t le32(const unsigned char* p) { return p[0] | (uint32_t)p[1] << 8 | (uint32_t)p[2] << 16 | (uint32_t)p[3] << 24; }
static int read_packet(void) {
    unsigned char header[8]; long offset = ftell(media);
    if (offset == end) return 0;
    if (offset < start || offset > end - 8 || fread(header, 1, 8, media) != 8) return -1;
    uint32_t length = (uint32_t)header[0] << 24 | (uint32_t)header[1] << 16 | (uint32_t)header[2] << 8 | header[3];
    if (!length || length > sizeof(packet) || length > (uint32_t)(end - offset - 8) || fread(packet, 1, length, media) != length) return -1;
    return (int)length;
}

// The supported media layout stores length and final range before each standard Opus packet.
int km_opus_open(void) {
    km_opus_close(); media = fopen("/selected.wem", "rb");
    if (!media) return 0;
    unsigned char header[64];
    if (fread(header, 1, 12, media) != 12 || memcmp(header, "RIFF", 4) || memcmp(header + 8, "WAVE", 4)) { km_opus_close(); return 0; }
    fseek(media, 0, SEEK_END); long size = ftell(media); long data = 0; uint32_t data_size = 0, seek_size = 0, rate = 0;
    int found = 0; frames = 0; loop_start = loop_end = 0;
    if (size > 64 * 1024 * 1024) goto invalid;
    for (long offset = 12; offset + 8 <= size;) {
        fseek(media, offset, SEEK_SET); if (fread(header, 1, 8, media) != 8) goto invalid;
        uint32_t length = le32(header + 4); if (length > (uint32_t)(size - offset - 8)) goto invalid;
        if (!memcmp(header, "fmt ", 4)) {
            if (length < 2 || fread(header, 1, length < 40 ? length : 40, media) < 2) goto invalid;
            if (header[0] != 0x39 || header[1] != 0x30) { km_opus_close(); return 0; }
            if (length != 40) goto invalid;
            channels = header[2] | header[3] << 8; rate = le32(header + 4); frames = le32(header + 24); seek_size = le32(header + 36); found = 1;
        } else if (!memcmp(header, "data", 4)) { data = offset + 8; data_size = length; }
        else if (!memcmp(header, "smpl", 4) && length >= 60) {
            if (fread(header, 1, 60, media) != 60) goto invalid;
            if (le32(header + 28) == 1) { loop_start = le32(header + 44); loop_end = le32(header + 48) + 1; }
        }
        offset += 8 + length + (length & 1);
    }
    if (!found) { km_opus_close(); return 0; }
    if (channels < 1 || channels > 2 || !data || seek_size >= data_size) goto invalid;
    start = data + seek_size; end = data + data_size; fseek(media, start, SEEK_SET);
    int length = read_packet(); if (length <= 0) goto invalid;
    int packet_frames = opus_packet_get_nb_samples(packet, length, 48000); if (packet_frames <= 0) goto invalid;
    delay = packet_frames / 8; int64_t total = packet_frames;
    while ((length = read_packet()) > 0) { int count = opus_packet_get_nb_samples(packet, length, 48000); if (count <= 0) goto invalid; total += count; if (total > 48000LL * 3600) goto invalid; }
    if (length < 0 || total <= delay) goto invalid;
    if (rate != 48000) frames = (int)(total - delay);
    if (frames <= 0 || frames > total - delay) goto invalid;
    if (loop_start < 0 || loop_end <= loop_start || loop_end > frames) loop_start = loop_end = 0;
    int error; opus = opus_decoder_create(48000, channels, &error); if (!opus || error != OPUS_OK) goto invalid;
    fseek(media, start, SEEK_SET); pending = consumed = position = 0; skip = delay; return 1;
invalid:
    km_opus_close(); return -1;
}
double km_opus_info(int field) {
    switch (field) { case 0: return channels; case 1: return 48000; case 2: return frames; case 3: return loop_start; case 4: return loop_end; default: return 0; }
}
int km_opus_render(float* output, int count) {
    if (!opus || count < 0 || count > 16384) return -1;
    int written = 0; if (count > frames - position) count = frames - position;
    while (written < count) {
        if (consumed == pending) {
            int length = read_packet(); if (length <= 0) return -1;
            pending = opus_decode_float(opus, packet, length, samples, 5760, 0); consumed = 0;
            if (pending <= 0) return -1;
        }
        if (skip) { int take = pending - consumed < skip ? pending - consumed : skip; skip -= take; consumed += take; continue; }
        int take = pending - consumed < count - written ? pending - consumed : count - written;
        if (output) memcpy(output + written * channels, samples + consumed * channels, take * channels * sizeof(float));
        consumed += take; written += take;
    }
    position += written; return written;
}
void km_opus_seek(int target) {
    if (!opus || target < 0 || target > frames) return;
    if (target < position) { opus_decoder_ctl(opus, OPUS_RESET_STATE); fseek(media, start, SEEK_SET); pending = consumed = position = 0; skip = delay; }
    while (position < target) if (km_opus_render(NULL, target - position < 16384 ? target - position : 16384) <= 0) break;
}
