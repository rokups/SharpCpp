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
}
