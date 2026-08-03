#pragma once

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

inline void consume_dependency_action(dependency_action callback)
{
    callback();
}

inline int consume_dependency_transform(dependency_transform callback, int value)
{
    return callback(value) + 1;
}

inline int consume_dependency_box_alias(const dependency_int_box_alias* value)
{
    return value == nullptr ? -1 : value->get_value();
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
}
