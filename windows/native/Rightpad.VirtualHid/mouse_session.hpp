#pragma once
#include <libvirtualhid/libvirtualhid.hpp>
#include <algorithm>
#include <memory>
#include <optional>
#include <stdexcept>

namespace rightpad {
// At the pinned commit WindowsMouse (SendInput) exposes no nodes. WindowsHidMouse
// exposes the broker-created device path. Check before allowing ANY input report.
inline bool driver_mouse(const lvh::Mouse& mouse) {
    const auto nodes = mouse.device_nodes();
    return std::ranges::any_of(nodes, [](const auto& node) { return !node.path.empty(); });
}

// Bridge button codes are rightpad's own ABI, not the library declaration order.
// 0 = left, 1 = right, 2 = middle (wheel press).
inline std::optional<lvh::MouseButton> mapped_button(int32_t button) {
    switch (button) {
        case 0: return lvh::MouseButton::left;
        case 1: return lvh::MouseButton::right;
        case 2: return lvh::MouseButton::middle;
        default: return std::nullopt;
    }
}

struct MouseSession {
    std::unique_ptr<lvh::Runtime> runtime;
    std::unique_ptr<lvh::Mouse> mouse;
    // Bit per bridge button code, so close() releases exactly what may be down.
    unsigned held = 0;

    lvh::OperationStatus move(int32_t dx, int32_t dy) { return mouse->move_relative(dx, dy); }
    lvh::OperationStatus button(int32_t button, bool down) {
        const auto mapped = mapped_button(button);
        if (!mapped) return lvh::OperationStatus::failure(lvh::ErrorCode::invalid_argument, "invalid mouse button code");
        const unsigned bit = 1U << static_cast<unsigned>(button);
        if (down) held |= bit; // An ambiguous submission still requires a cleanup release.
        auto status = mouse->button(*mapped, down);
        if (status.ok()) { if (down) held |= bit; else held &= ~bit; }
        return status;
    }
    // Preserved ABI-2 entry point used by the existing left-button callers.
    lvh::OperationStatus button(bool down) { return button(0, down); }
    lvh::OperationStatus close() {
        auto result = lvh::OperationStatus::success();
        if (mouse) {
            for (int32_t button = 2; button >= 0; --button) {
                if ((held & (1U << static_cast<unsigned>(button))) == 0) continue;
                auto released = this->button(button, false);
                if (result.ok()) result = released;
            }
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
