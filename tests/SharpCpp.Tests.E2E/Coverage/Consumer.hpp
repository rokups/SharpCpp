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

inline int consume_dependency_box_alias(const dependency_int_box_alias* value)
{
    return value == nullptr ? -1 : value->get_value();
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
