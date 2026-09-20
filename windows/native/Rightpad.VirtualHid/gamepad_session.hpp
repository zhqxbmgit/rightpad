#pragma once
#include "bridge.h"
#include <libvirtualhid/libvirtualhid.hpp>
#include <array>
#include <cstddef>
#include <memory>

namespace rightpad {
static_assert(sizeof(rightpad_gamepad_state) == 12);
static_assert(offsetof(rightpad_gamepad_state, left_thumb_x) == 4);
static_assert(offsetof(rightpad_gamepad_state, right_thumb_y) == 10);
inline constexpr const char* gamepad_stable_id = "rightpad.gamepad.xbox360";
inline constexpr std::array button_mapping {
    std::pair{RP_GAMEPAD_A, lvh::GamepadButton::a}, std::pair{RP_GAMEPAD_B, lvh::GamepadButton::b},
    std::pair{RP_GAMEPAD_X, lvh::GamepadButton::x}, std::pair{RP_GAMEPAD_Y, lvh::GamepadButton::y},
    std::pair{RP_GAMEPAD_BACK, lvh::GamepadButton::back}, std::pair{RP_GAMEPAD_START, lvh::GamepadButton::start},
    std::pair{RP_GAMEPAD_GUIDE, lvh::GamepadButton::guide}, std::pair{RP_GAMEPAD_LEFT_STICK, lvh::GamepadButton::left_stick},
    std::pair{RP_GAMEPAD_RIGHT_STICK, lvh::GamepadButton::right_stick},
    std::pair{RP_GAMEPAD_LEFT_SHOULDER, lvh::GamepadButton::left_shoulder},
    std::pair{RP_GAMEPAD_RIGHT_SHOULDER, lvh::GamepadButton::right_shoulder},
    std::pair{RP_GAMEPAD_DPAD_UP, lvh::GamepadButton::dpad_up}, std::pair{RP_GAMEPAD_DPAD_DOWN, lvh::GamepadButton::dpad_down},
    std::pair{RP_GAMEPAD_DPAD_LEFT, lvh::GamepadButton::dpad_left}, std::pair{RP_GAMEPAD_DPAD_RIGHT, lvh::GamepadButton::dpad_right}
};
inline lvh::GamepadState gamepad_state(const rightpad_gamepad_state& input) {
    lvh::GamepadState state{};
    for (const auto& [mask, button] : button_mapping) state.buttons.set(button, (input.buttons & mask) != 0);
    const auto axis = [](int16_t value) { return value / (value < 0 ? 32768.0F : 32767.0F); };
    state.left_stick = {axis(input.left_thumb_x), axis(input.left_thumb_y)};
    state.right_stick = {axis(input.right_thumb_x), axis(input.right_thumb_y)};
    state.left_trigger = input.left_trigger / 255.0F;
    state.right_trigger = input.right_trigger / 255.0F;
    return state;
}

struct GamepadSession {
    std::unique_ptr<lvh::Runtime> runtime;
    std::unique_ptr<lvh::GamepadStateAdapter> adapter;
#ifdef RIGHTPAD_VHID_TEST
    void (*after_neutral)(const lvh::Gamepad&) = nullptr;
#endif

    lvh::OperationStatus open(lvh::BackendKind backend) {
        runtime = lvh::Runtime::create({.backend = backend});
        if (!runtime || (backend == lvh::BackendKind::platform_default && !runtime->capabilities().supports_virtual_hid))
            return lvh::OperationStatus::failure(lvh::ErrorCode::backend_unavailable, "Xbox360 Virtual HID runtime unavailable");
        auto created = lvh::GamepadStateAdapter::create(*runtime, {
            .profile = lvh::profiles::xbox_360(),
            .metadata = {.global_index = 0, .client_relative_index = 0,
                .client_type = lvh::ClientControllerType::xbox, .stable_id = gamepad_stable_id}});
        if (!created.status.ok()) return created.status;
        adapter = std::move(created.adapter);
        if (!adapter || !adapter->is_open())
            return lvh::OperationStatus::failure(lvh::ErrorCode::backend_unavailable, "Xbox360 adapter unavailable");
        if (backend == lvh::BackendKind::platform_default) {
            const auto nodes = adapter->gamepad()->device_nodes();
            if (nodes.empty() || nodes.front().path.empty())
                return lvh::OperationStatus::failure(lvh::ErrorCode::backend_unavailable, "Xbox360 device node absent");
        }
        return adapter->set_state({});
    }
    lvh::OperationStatus set(const rightpad_gamepad_state& state) {
        if ((state.buttons & 0x8000U) != 0)
            return lvh::OperationStatus::failure(lvh::ErrorCode::invalid_argument, "unknown gamepad button bits");
        if (!adapter) return lvh::OperationStatus::failure(lvh::ErrorCode::device_closed, "gamepad closed");
        return adapter->set_state(gamepad_state(state));
    }
    lvh::OperationStatus close() {
        auto result = lvh::OperationStatus::success();
        // Retain ownership locally so a throwing neutral operation cannot prevent device destruction.
        auto owned = std::move(adapter);
        if (owned) {
            try { result = owned->set_state({}); }
            catch (...) {
                try { static_cast<void>(owned->gamepad()->close()); } catch (...) { }
                runtime.reset();
                throw;
            }
#ifdef RIGHTPAD_VHID_TEST
            if (after_neutral) after_neutral(*owned->gamepad());
#endif
            auto closed = owned->gamepad()->close();
            if (result.ok()) result = closed;
        }
        runtime.reset();
        return result;
    }
    ~GamepadSession() { try { close(); } catch (...) { } }
};
}
