#include "bridge.h"
#include "mouse_session.hpp"
#include <cstdio>
#include <cstring>
#include <exception>

namespace {
void copy(char* target, int size, const char* text) noexcept {
    if (target && size > 0) std::snprintf(target, static_cast<size_t>(size), "%s", text);
}
int status_result(const lvh::OperationStatus& status, char* error, int size) noexcept {
    if (status.ok()) { copy(error, size, ""); return 0; }
    copy(error, size, status.message().c_str());
    return static_cast<int>(status.code());
}
template<class F> int boundary(char* error, int size, F&& operation) noexcept {
    try { return status_result(operation(), error, size); }
    catch (const std::exception&) {
        // Do not forward arbitrary exception payloads from licensing/provider code.
        copy(error, size, "native bridge exception during libvirtualhid operation"); return 100;
    }
    catch (...) { copy(error, size, "unknown native bridge exception"); return 100; }
}
auto failure(const char* message) { return lvh::OperationStatus::failure(lvh::ErrorCode::backend_unavailable, message); }
}

int rightpad_vhid_abi_version() { return 1; }
int rightpad_vhid_create(void** handle, char* identity, int identity_size, char* error, int error_size) {
    if (handle) *handle = nullptr;
    copy(identity, identity_size, "");
    return boundary(error, error_size, [&] {
        if (!handle || !identity || identity_size < 2) return failure("invalid create buffer/handle argument");
        // Public local status read only: never activate, validate remotely, or print customer fields.
        const auto license = lvh::get_license_status();
        if (!license.status.ok() || !license.license.service_available)
            return failure("broker unavailable or broker protocol/version mismatch (local license status unavailable)");
        if (!license.license.licensed())
            return lvh::OperationStatus::failure(lvh::ErrorCode::license_required, "license unavailable: machine license is not active");
        auto session = std::make_unique<rightpad::MouseSession>();
        session->runtime = lvh::Runtime::create({.backend = lvh::BackendKind::platform_default});
        if (!session->runtime || !session->runtime->capabilities().supports_virtual_hid)
            return failure("driver unavailable or driver protocol/version mismatch: platform runtime has no Virtual HID capability");
        auto created = session->runtime->create_mouse({.profile = lvh::profiles::mouse(), .stable_id = "rightpad.mouse.poc"});
        if (!created.status.ok()) return created.status;
        session->mouse = std::move(created.mouse);
        if (!session->mouse || !rightpad::driver_mouse(*session->mouse))
            return failure("mouse creation failed: driver-backed device node absent; libvirtualhid SendInput fallback rejected before input");
        const auto node = session->mouse->device_nodes().front().path;
        if (node.size() >= static_cast<size_t>(identity_size)) return failure("mouse device identity buffer too small");
        copy(identity, identity_size, node.c_str());
        *handle = session.release();
        return lvh::OperationStatus::success();
    });
}
int rightpad_vhid_move(void* handle, int32_t dx, int32_t dy, char* error, int size) {
    return boundary(error, size, [&] { return handle ? static_cast<rightpad::MouseSession*>(handle)->move(dx, dy) : failure("mouse handle is null"); });
}
int rightpad_vhid_left_down(void* handle, char* error, int size) {
    return boundary(error, size, [&] { return handle ? static_cast<rightpad::MouseSession*>(handle)->button(true) : failure("mouse handle is null"); });
}
int rightpad_vhid_left_up(void* handle, char* error, int size) {
    return boundary(error, size, [&] { return handle ? static_cast<rightpad::MouseSession*>(handle)->button(false) : failure("mouse handle is null"); });
}
int rightpad_vhid_destroy(void* handle, char* error, int size) {
    return boundary(error, size, [&] {
        std::unique_ptr<rightpad::MouseSession> session(static_cast<rightpad::MouseSession*>(handle));
        return session ? session->close() : lvh::OperationStatus::success();
    });
}
