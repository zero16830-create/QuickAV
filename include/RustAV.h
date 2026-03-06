#pragma once

#include <stdbool.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef struct RustAVFrameMeta {
    int32_t width;
    int32_t height;
    int32_t format;
    int32_t stride;
    int32_t data_size;
    double time_sec;
    int64_t frame_index;
} RustAVFrameMeta;

int32_t GetPlayer(const char* path, void* targetTexture);
int32_t CreatePlayerPullRGBA(const char* path, int32_t targetWidth, int32_t targetHeight);
int32_t ReleasePlayer(int32_t id);
void ForcePlayerWrite(int32_t id);
int32_t UpdatePlayer(int32_t id);
double Duration(int32_t id);
double Time(int32_t id);
int32_t Play(int32_t id);
int32_t Stop(int32_t id);
double Seek(int32_t id, double time);
int32_t SetLoop(int32_t id, bool loopValue);
int32_t GetFrameMetaRGBA(int32_t id, RustAVFrameMeta* outMeta);
int32_t CopyFrameRGBA(int32_t id, uint8_t* destination, int32_t destinationLength);
void UnityPluginLoad(void* interfaces);
void UnityPluginUnload(void);
void (*GetRenderEventFunc(void))(int32_t eventId);

#ifdef __cplusplus
}
#endif
