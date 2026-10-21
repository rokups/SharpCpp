#pragma once

#include <array>
#include <functional>
#include <vector>

#include "../Callback/EaFunction.h"

// A callback crosses the ABI as a callback handle with its own lifetime rules. A container element
// crosses as a value or a handle of the element, so a container of callbacks is not bound. The
// generator reports a reason for each member.
namespace callback_containers
{
inline int count_callbacks(const std::vector<std::function<void()>>& callbacks)
{
    return static_cast<int>(callbacks.size());
}

inline std::vector<std::function<int(int)>> make_callbacks()
{
    return {};
}

inline int count_ea_callbacks(const std::vector<ea::function<void()>>& callbacks)
{
    return static_cast<int>(callbacks.size());
}

inline int count_fixed_callbacks(const std::array<std::function<void()>, 2>& callbacks)
{
    return static_cast<int>(callbacks.size());
}

struct callback_list
{
    std::vector<std::function<void()>> callbacks;
    int value{};
};
}
