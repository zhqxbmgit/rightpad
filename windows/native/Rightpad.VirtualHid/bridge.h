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
// ABI 2: x64, cdecl, opaque owning handles, caller-owned NUL-terminated error buffers.
// All calls return 0 on success; destroy consumes the handle even on failure.
RP_API int rightpad_vhid_abi_version(void);
RP_API int rightpad_vhid_create(void** handle, char* identity, int identity_size, char* error, int error_size);
RP_API int rightpad_vhid_move(void* handle, int32_t dx, int32_t dy, char* error, int error_size);
RP_API int rightpad_vhid_left_down(void* handle, char* error, int error_size);
RP_API int rightpad_vhid_left_up(void* handle, char* error, int error_size);
RP_API int rightpad_vhid_destroy(void* handle, char* error, int error_size);

// Bridge logical flags, NOT XInput wButtons. Map explicitly to lvh::GamepadButton.
enum rightpad_gamepad_button {
    RP_GAMEPAD_A = 1 << 0, RP_GAMEPAD_B = 1 << 1, RP_GAMEPAD_X = 1 << 2, RP_GAMEPAD_Y = 1 << 3,
    RP_GAMEPAD_BACK = 1 << 4, RP_GAMEPAD_START = 1 << 5, RP_GAMEPAD_GUIDE = 1 << 6,
    RP_GAMEPAD_LEFT_STICK = 1 << 7, RP_GAMEPAD_RIGHT_STICK = 1 << 8,
    RP_GAMEPAD_LEFT_SHOULDER = 1 << 9, RP_GAMEPAD_RIGHT_SHOULDER = 1 << 10,
    RP_GAMEPAD_DPAD_UP = 1 << 11, RP_GAMEPAD_DPAD_DOWN = 1 << 12,
    RP_GAMEPAD_DPAD_LEFT = 1 << 13, RP_GAMEPAD_DPAD_RIGHT = 1 << 14
};
// 12 bytes, alignment 2. All-zero is Neutral. Every submission replaces every field.
typedef struct rightpad_gamepad_state {
    uint16_t buttons;
    uint8_t left_trigger, right_trigger;
    int16_t left_thumb_x, left_thumb_y, right_thumb_x, right_thumb_y;
} rightpad_gamepad_state;
RP_API int rightpad_vhid_gamepad_create(void** handle, char* identity, int identity_size, char* error, int error_size);
RP_API int rightpad_vhid_gamepad_set_state(void* handle, const rightpad_gamepad_state* state, char* error, int error_size);
RP_API int rightpad_vhid_gamepad_destroy(void* handle, char* error, int error_size);
#ifdef __cplusplus
}
#endif
