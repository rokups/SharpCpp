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
