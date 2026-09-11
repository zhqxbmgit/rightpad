#pragma once
#include <stdint.h>
#ifdef RIGHTPAD_VHID_EXPORTS
#define RP_API __declspec(dllexport)
#else
#define RP_API __declspec(dllimport)
#endif
#ifdef __cplusplus
extern "C" {
#endif
// ABI 1: x64, cdecl, opaque owning handle, caller-owned NUL-terminated error buffers.
// All calls return 0 on success; destroy consumes the handle even on failure.
RP_API int rightpad_vhid_abi_version(void);
RP_API int rightpad_vhid_create(void** handle, char* identity, int identity_size, char* error, int error_size);
RP_API int rightpad_vhid_move(void* handle, int32_t dx, int32_t dy, char* error, int error_size);
RP_API int rightpad_vhid_left_down(void* handle, char* error, int error_size);
RP_API int rightpad_vhid_left_up(void* handle, char* error, int error_size);
RP_API int rightpad_vhid_destroy(void* handle, char* error, int error_size);
#ifdef __cplusplus
}
#endif
