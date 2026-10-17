#pragma once
#include "mouse_session.hpp"

namespace rightpad {
// Only these Windows key codes have explicit keyboard-page mappings in the pinned
// WindowsHidKeyboard. Reject everything else before the vendor fallback can run.
inline bool keyboard_key_supported(uint16_t key, uint16_t scan) {
    if (scan != 0) return key == 0x0D && scan == 0xE01C;
    if ((key >= 0x30 && key <= 0x39) || (key >= 0x41 && key <= 0x5A) ||
        (key >= 0x60 && key <= 0x69) || (key >= 0x70 && key <= 0x87) || (key >= 0xA0 && key <= 0xA5)) return true;
    switch(key) {
    case 8: case 9: case 0x0D: case 0x13: case 0x14: case 0x1B: case 0x20:
    case 0x21: case 0x22: case 0x23: case 0x24: case 0x25: case 0x26: case 0x27: case 0x28:
    case 0x2C: case 0x2D: case 0x2E: case 0x5B: case 0x5C: case 0x5D:
    case 0x6A: case 0x6B: case 0x6D: case 0x6E: case 0x6F: case 0x90: case 0x91:
    case 0xBA: case 0xBB: case 0xBC: case 0xBD: case 0xBE: case 0xBF: case 0xC0:
    case 0xDB: case 0xDC: case 0xDD: case 0xDE: case 0xE2: return true;
    default: return false;
    }
}
struct KeyboardSession {
    std::unique_ptr<lvh::Runtime> runtime;
    std::unique_ptr<lvh::Keyboard> keyboard;
    uint16_t held_key = 0, held_scan = 0;
    lvh::OperationStatus set(uint16_t key, uint16_t scan, bool down) {
        if (!keyboard_key_supported(key,scan)) return lvh::OperationStatus::failure(lvh::ErrorCode::invalid_argument,"unsupported HID keyboard key/scan code");
        if (down) { held_key=key; held_scan=scan; }
        auto status=keyboard->submit({.key_code=key,.pressed=down,.scan_code=scan});
        if(status.ok() && !down) {held_key=0;held_scan=0;}
        return status;
    }
    lvh::OperationStatus close() {
        auto status=lvh::OperationStatus::success();
        if(keyboard) {
            if(held_key) status=set(held_key,held_scan,false);
            auto closed=keyboard->close();
            if(status.ok()) status=closed;
            keyboard.reset();
        }
        runtime.reset();return status;
    }
    ~KeyboardSession() {try {close();} catch(...) {}}
};
}
