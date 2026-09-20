#include "bridge.h"
#include "mouse_session.hpp"
#include "gamepad_session.hpp"
#include <climits>
#include <iostream>
#include <stdexcept>

int checks = 0;
void check(bool condition) { ++checks; if (!condition) throw std::runtime_error("native fake API check failed #" + std::to_string(checks)); }
int main() {
    try {
        rightpad::MouseSession session;
        session.runtime = lvh::Runtime::create({.backend = lvh::BackendKind::fake});
        auto result = session.runtime->create_mouse();
        check(result.status.ok());
        session.mouse = std::move(result.mouse);
        check(!rightpad::driver_mouse(*session.mouse)); // A successful create alone cannot admit a backend.
        check(session.move(INT_MIN, INT_MAX).ok());
        auto event = session.mouse->last_submitted_event();
        check(event.x == INT_MIN && event.y == INT_MAX && event.kind == lvh::MouseEventKind::relative_motion);
        check(session.button(true).ok() && session.held);
        event = session.mouse->last_submitted_event();
        check(event.kind == lvh::MouseEventKind::button && event.button == lvh::MouseButton::left && event.pressed);
        check(session.button(false).ok() && !session.held);
        check(!session.mouse->last_submitted_event().pressed);
        check(session.button(true).ok());
        check(session.close().ok() && !session.held && !session.mouse && !session.runtime);
        check(session.close().ok());
        char error[128]{};
        check(rightpad_vhid_abi_version() == 2);
        check(rightpad_vhid_move(nullptr, 1, 2, error, sizeof(error)) != 0 && error[0]);
        check(rightpad_vhid_left_down(nullptr, error, sizeof(error)) != 0);
        check(rightpad_vhid_left_up(nullptr, error, sizeof(error)) != 0);
        check(rightpad_vhid_destroy(nullptr, error, sizeof(error)) == 0 && !error[0]);
        check(rightpad_vhid_create(nullptr, nullptr, 0, error, sizeof(error)) != 0);
        void* handle = nullptr;
        char identity[256]{};
        check(rightpad_vhid_gamepad_create(&handle, identity, sizeof(identity), error, sizeof(error)) == 0);
        check(handle && identity[0]);
        auto& gamepad = *static_cast<rightpad::GamepadSession*>(handle);
        auto* device = gamepad.adapter->gamepad();
        check(device->profile().gamepad_kind == lvh::GamepadProfileKind::xbox_360);
        check(device->metadata().stable_id == rightpad::gamepad_stable_id);
        check(device->metadata().global_index == 0 && device->metadata().client_relative_index == 0);
        check(device->metadata().client_type == lvh::ClientControllerType::xbox);
        check(device->last_submitted_state().buttons.raw_bits() == 0);
        rightpad_gamepad_state state{};
        for (const auto& [mask, button] : rightpad::button_mapping) {
            state.buttons = static_cast<uint16_t>(mask);
            check(rightpad_vhid_gamepad_set_state(handle, &state, error, sizeof(error)) == 0);
            const auto actual = device->last_submitted_state();
            check(actual.buttons.test(button));
            for (const auto& [other_mask, other_button] : rightpad::button_mapping)
                if (mask != other_mask) check(!actual.buttons.test(other_button));
        }
        state.buttons = RP_GAMEPAD_B;
        check(rightpad_vhid_gamepad_set_state(handle, &state, error, sizeof(error)) == 0);
        const auto before = device->submit_count();
        state.buttons = RP_GAMEPAD_Y;
        check(rightpad_vhid_gamepad_set_state(handle, &state, error, sizeof(error)) == 0);
        check(device->submit_count() == before + 1); // One complete report, no edge pair.
        check(!device->last_submitted_state().buttons.test(lvh::GamepadButton::b));
        check(device->last_submitted_state().buttons.test(lvh::GamepadButton::y));
        state = {.buttons = RP_GAMEPAD_A, .left_trigger = 255, .right_trigger = 127,
            .left_thumb_x = INT16_MIN, .left_thumb_y = INT16_MAX, .right_thumb_x = 1234, .right_thumb_y = -5678};
        check(rightpad_vhid_gamepad_set_state(handle, &state, error, sizeof(error)) == 0);
        const auto actual = device->last_submitted_state();
        check(actual.left_trigger == 1.0F && actual.right_trigger == 127 / 255.0F);
        check(actual.left_stick.x == -1.0F && actual.left_stick.y == 1.0F);
        check(actual.right_stick.x == 1234 / 32767.0F && actual.right_stick.y == -5678 / 32768.0F);
        state = {};
        check(rightpad_vhid_gamepad_set_state(handle, &state, error, sizeof(error)) == 0);
        check(device->last_submitted_state().buttons.raw_bits() == 0 && device->last_submitted_state().left_trigger == 0);
        state.buttons = 0x8000U;
        check(rightpad_vhid_gamepad_set_state(handle, &state, error, sizeof(error)) != 0 && error[0]);
        state.buttons = RP_GAMEPAD_B;
        check(rightpad_vhid_gamepad_set_state(handle, &state, error, sizeof(error)) == 0);
        bool neutral_seen = false;
        static bool* observed;
        observed = &neutral_seen;
        gamepad.after_neutral = [](const lvh::Gamepad& d) {
            const auto s = d.last_submitted_state();
            *observed = d.is_open() && s.buttons.raw_bits() == 0 && s.left_trigger == 0 && s.right_trigger == 0
                && s.left_stick.x == 0 && s.left_stick.y == 0 && s.right_stick.x == 0 && s.right_stick.y == 0;
        };
        check(rightpad_vhid_gamepad_destroy(handle, error, sizeof(error)) == 0 && neutral_seen);
        check(rightpad_vhid_gamepad_set_state(handle, &state, error, sizeof(error)) != 0);
        check(rightpad_vhid_gamepad_destroy(handle, error, sizeof(error)) != 0);
        check(rightpad_vhid_gamepad_destroy(nullptr, error, sizeof(error)) == 0);
        check(rightpad_vhid_gamepad_set_state(nullptr, &state, error, sizeof(error)) != 0);
        check(rightpad_vhid_gamepad_set_state(&session, &state, error, sizeof(error)) != 0);
        check(rightpad_vhid_gamepad_create(nullptr, identity, sizeof(identity), error, sizeof(error)) != 0);
        check(rightpad_vhid_gamepad_create(&handle, nullptr, 0, error, sizeof(error)) != 0 && !handle);
        check(rightpad_vhid_gamepad_create(&handle, identity, sizeof(identity), error, sizeof(error)) == 0);
        check(rightpad_vhid_gamepad_set_state(handle, nullptr, error, sizeof(error)) != 0);
        auto& failed = *static_cast<rightpad::GamepadSession*>(handle);
        check(failed.adapter->gamepad()->close().ok());
        check(rightpad_vhid_gamepad_set_state(handle, &state, error, sizeof(error)) != 0 && error[0]);
        check(rightpad_vhid_gamepad_destroy(handle, error, sizeof(error)) != 0);
        check(rightpad_vhid_gamepad_destroy(handle, error, sizeof(error)) != 0); // Failure still consumes ownership.
        std::cout << "PASS native fake mouse + Xbox360 ABI2 full-state/lifecycle: " << checks << " checks\n";
        return 0;
    } catch (const std::exception& e) { std::cerr << e.what() << '\n'; return 1; }
}
