#pragma once

namespace default_expressions
{
struct clock
{
    static unsigned system_time()
    {
        return 41;
    }
};

using time_source = unsigned (*)();

/// The lambda default is only expressible natively; its tokens must stay intact.
inline unsigned read_time(time_source source = [] { return clock::system_time(); })
{
    return source() + 1;
}

enum class default_mode
{
    idle,
    active
};

inline int use_mode(default_mode mode = default_mode::active)
{
    return static_cast<int>(mode);
}

inline double sum_floating_defaults(float delay = -1.f, float alpha = .5f, double ratio = 1.25)
{
    return static_cast<double>(delay) + static_cast<double>(alpha) + ratio;
}
}

#include <cfloat>
#include <cstddef>

namespace default_expressions
{
/// A NULL macro default is a managed null pointer.
inline int accept_pointer(const void* value = NULL)
{
    return value == nullptr ? 0 : 1;
}

/// Macros that expand to expressions C# cannot spell are applied natively.
inline int plot_value(float scale_min = FLT_MAX, float scale_max = FLT_MAX)
{
    return scale_min == FLT_MAX && scale_max == FLT_MAX ? 2 : (scale_min == FLT_MAX ? 1 : 0);
}

struct offset3
{
    offset3() = default;

    offset3(int x, int y, int z)
        : x(x), y(y), z(z)
    {
    }

    int x{};
    int y{};
    int z{};
};

/// A constructor-expression default is applied natively.
inline int move_by(offset3 offset = offset3(1, 2, 3))
{
    return offset.x + offset.y * 10 + offset.z * 100;
}
}
