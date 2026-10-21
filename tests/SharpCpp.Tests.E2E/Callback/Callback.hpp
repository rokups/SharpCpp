#pragma once

#include <array>
#include <functional>
#include <string>

struct callback_box
{
    int value_{};

    int get_value()
    {
        return value_;
    }

    void set_value(int value)
    {
        value_ = value;
    }
};

inline void invoke_void_callback(std::function<void(int)> callback, int value)
{
    callback(value);
}

inline int invoke_int_callback(std::function<int(int, int)> callback, int lhs, int rhs)
{
    return callback(lhs, rhs);
}

inline int invoke_copied_callback(std::function<int(int)> callback, int value)
{
    auto copied = callback;
    return copied(value);
}

inline std::function<int(int)> retained_callback;

inline void retain_callback(std::function<int(int)> callback)
{
    retained_callback = std::move(callback);
}

inline int invoke_retained_callback(int value)
{
    return retained_callback(value);
}

inline void clear_retained_callback()
{
    retained_callback = {};
}

inline int invoke_object_pointer_callback(std::function<void(callback_box*)> callback, int value)
{
    static callback_box box{};
    box.set_value(value);
    callback(&box);
    return box.get_value();
}

inline int invoke_object_reference_callback(std::function<void(callback_box&)> callback, int value)
{
    static callback_box box{};
    box.set_value(value);
    callback(box);
    return box.get_value();
}

inline int invoke_primitive_pointer_callback(std::function<void(int*)> callback, int value)
{
    int local = value;
    callback(&local);
    return local;
}

inline int invoke_primitive_reference_callback(std::function<void(int&)> callback, int value)
{
    int local = value;
    callback(local);
    return local;
}

inline int invoke_string_reference_callback(std::function<int(const std::string&)> callback)
{
    static const std::string value = "SharpCpp";
    return callback(value);
}

inline void invoke_named_delegate_callback([[sharpcpp::delegate("ValueCallback")]] std::function<void(int)> callback, int value)
{
    callback(value);
}

inline int invoke_generated_delegate_callback([[sharpcpp::delegate]] std::function<int(int)> callback, int value)
{
    return callback(value);
}

inline int invoke_external_delegate_callback(
    [[sharpcpp::delegate("SharpCpp.Tests.E2E.Callback.ExternalValueCallback")]] std::function<int(int)> callback,
    int value)
{
    return callback(value);
}

// Shared wrapper with a non-type template argument crossing into a managed callback.
inline int invoke_array_callback(std::function<int(std::array<int, 2>&)> callback)
{
    std::array<int, 2> values{ 3, 4 };
    return callback(values);
}

// A noexcept native function takes a function object. std::function has no noexcept signature,
// so the noexcept belongs to the native function.
inline int invoke_noexcept_function_callback(std::function<int(int)> callback, int value) noexcept
{
    return callback(value);
}

inline int invoke_noexcept_function_reference_callback(const std::function<int(int)>& callback, int value) noexcept
{
    return callback(value);
}
