#pragma once

#include <functional>

#include "EaFunction.h"

struct alias_callback_payload
{
    alias_callback_payload() = default;

    explicit alias_callback_payload(int value)
        : value_(value)
    {
    }

    int get_value() const { return value_; }

private:
    int value_{};
};

using alias_callback_payload_handle = alias_callback_payload*;
using alias_callback = std::function<alias_callback_payload_handle(alias_callback_payload_handle)>;

inline alias_callback_payload_handle invoke_alias_callback(
    const alias_callback& callback,
    alias_callback_payload_handle value)
{
    return callback(value);
}

using ea_alias_callback = ea::function<alias_callback_payload_handle(alias_callback_payload_handle)>;

inline alias_callback_payload_handle ea_invoke_alias_callback(
    const ea_alias_callback& callback,
    alias_callback_payload_handle value)
{
    return callback(value);
}

struct alias_callback_payload_view
{
    alias_callback_payload** PayloadArray{};
    unsigned int PayloadCount{};
};

inline alias_callback_payload_view* make_alias_callback_payload_view(
    alias_callback_payload* first,
    alias_callback_payload* second)
{
    static alias_callback_payload* values[2];
    values[0] = first;
    values[1] = second;
    return new alias_callback_payload_view{values, 2};
}
