#pragma once
#include <libvirtualhid/libvirtualhid.hpp>
#include <algorithm>
#include <memory>
#include <stdexcept>

namespace rightpad {
// At the pinned commit WindowsMouse (SendInput) exposes no nodes. WindowsHidMouse
// exposes the broker-created device path. Check before allowing ANY input report.
inline bool driver_mouse(const lvh::Mouse& mouse) {
    const auto nodes = mouse.device_nodes();
    return std::ranges::any_of(nodes, [](const auto& node) { return !node.path.empty(); });
}

struct MouseSession {
    std::unique_ptr<lvh::Runtime> runtime;
    std::unique_ptr<lvh::Mouse> mouse;
    bool held = false;

    lvh::OperationStatus move(int32_t dx, int32_t dy) { return mouse->move_relative(dx, dy); }
    lvh::OperationStatus button(bool down) {
        if (down) held = true;
        auto status = mouse->button(lvh::MouseButton::left, down);
        if (status.ok()) held = down;
        return status;
    }
    lvh::OperationStatus close() {
        auto result = lvh::OperationStatus::success();
        if (mouse) {
            if (held) result = button(false);
            auto closed = mouse->close();
            if (result.ok()) result = closed;
            mouse.reset();
        }
        runtime.reset();
        return result;
    }
    ~MouseSession() { try { close(); } catch (...) { /* C++ destruction must not cross the ABI. */ } }
};
}
