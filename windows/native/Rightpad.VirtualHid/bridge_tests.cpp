#include "bridge.h"
#include "mouse_session.hpp"
#include <climits>
#include <iostream>
#include <stdexcept>

void check(bool condition) { if (!condition) throw std::runtime_error("native fake API check failed"); }
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
        check(rightpad_vhid_abi_version() == 1);
        check(rightpad_vhid_move(nullptr, 1, 2, error, sizeof(error)) != 0 && error[0]);
        check(rightpad_vhid_left_down(nullptr, error, sizeof(error)) != 0);
        check(rightpad_vhid_left_up(nullptr, error, sizeof(error)) != 0);
        check(rightpad_vhid_destroy(nullptr, error, sizeof(error)) == 0 && !error[0]);
        check(rightpad_vhid_create(nullptr, nullptr, 0, error, sizeof(error)) != 0);
        std::cout << "PASS native fake lifecycle, int32, left mapping, fallback rejection, ABI errors\n";
        return 0;
    } catch (const std::exception& e) { std::cerr << e.what() << '\n'; return 1; }
}
