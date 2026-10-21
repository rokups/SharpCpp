#pragma once

#include <cstring>

#include "Dependency.hpp"

namespace sharpcpp_coverage
{
inline int consume_dependency_value(const dependency_alias* value)
{
    return value == nullptr ? -1 : value->get_value();
}

inline dependency_state consume_dependency_state(const dependency_alias* value)
{
    return value == nullptr ? dependency_state::idle : value->get_state();
}

inline int consume_dependency_handle_alias(dependency_handle_alias value)
{
    return value == nullptr ? -1 : value->get_value();
}

inline dependency_state_alias consume_dependency_state_alias(dependency_state_alias value)
{
    return value;
}

inline dependency_unsigned_alias consume_dependency_unsigned_alias(dependency_unsigned_alias value)
{
    return value + 1;
}

inline dependency_signed_alias consume_dependency_signed_alias(dependency_signed_alias value)
{
    return increment_dependency_signed_alias(value);
}

inline unsigned short consume_dependency_small_state_alias(
    unsigned short value)
{
    return static_cast<unsigned short>(round_trip_dependency_small_state_alias(
        static_cast<dependency_small_state_alias>(value)));
}

inline int consume_dependency_rebound_handle_alias(
    dependency_rebound_handle_alias value)
{
    return read_dependency_rebound_handle_alias(value);
}

inline void consume_dependency_action(dependency_action callback)
{
    callback();
}

inline int consume_dependency_transform(dependency_transform callback, int value)
{
    return callback(value) + 1;
}

inline void consume_dependency_ea_action(dependency_ea_action callback)
{
    callback();
}

inline int consume_dependency_ea_transform(dependency_ea_transform callback, int value)
{
    return callback(value) + 1;
}

inline int consume_dependency_box_alias(const dependency_int_box_alias* value)
{
    return value == nullptr ? -1 : value->get_value();
}

inline int consume_dependency_value_box_alias(const dependency_value_box_alias* value)
{
    return read_dependency_value_box_alias(
        dependency_import_identity(value));
}

inline int consume_dependency_fixed_alias(const dependency_fixed_alias& value)
{
    return value[0] + value[1] + value[2] + value[3];
}

inline int consume_dependency_dynamic_alias(const dependency_dynamic_alias& value)
{
    int result = 0;
    for (int item : value)
        result += item;
    return result;
}

struct consumer_derived : dependency_base
{
    explicit consumer_derived(int value)
        : dependency_base(value)
    {
    }

    int doubled() const
    {
        return get_base_value() * 2;
    }
};

/// Allocated with the dependency's `new char[]` convention and freed by its release function.
[[sharpcpp::freed_by(release_dependency_text)]] inline char* make_consumer_dependency_text()
{
    char* text = new char[14];
    std::memcpy(text, "consumer text", 14);
    return text;
}

inline void get_consumer_dependency_label([[sharpcpp::freed_by(release_dependency_text)]] char** value)
{
    *value = new char[15];
    std::memcpy(*value, "consumer label", 15);
}

inline int consume_alpha_context(const alpha::context* value)
{
    return value == nullptr ? -1 : value->get_value();
}

inline int consume_beta_context(const beta::context* value)
{
    return value == nullptr ? -1 : value->get_value() + 1000;
}

inline int consume_alpha_mode(alpha::mode value)
{
    return static_cast<int>(value);
}

inline int consume_beta_mode(beta::mode value)
{
    return static_cast<int>(value);
}
}
