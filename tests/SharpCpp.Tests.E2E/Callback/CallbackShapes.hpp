#pragma once

#include <functional>

namespace callback_shapes
{
struct point2
{
    int x;
    int y;
};

inline float for_each(std::function<void(int, float)> callback)
{
    callback(1, 0.5f);
    callback(2, 1.5f);
    return 2.0f;
}

inline int notify(std::function<void()> callback)
{
    callback();
    return 1;
}

/// A defaulted null callback is a managed null default.
inline unsigned invoke_optional(std::function<unsigned()> callback = nullptr)
{
    return callback ? callback() : 99u;
}

/// A configured value struct argument crosses by value; by const reference it crosses as
/// a pointer to the value, and both shapes coexist.
inline int use_point_value_callback(std::function<int(point2)> callback)
{
    return callback(point2{5, 6});
}

inline int use_point_callback(std::function<int(const point2&)> callback)
{
    return callback(point2{3, 4});
}

/// A non-const reference to a configured value struct is a managed ref parameter.
inline int invoke_point_ref_callback(std::function<void(point2&)> callback, int x, int y)
{
    point2 value{x, y};
    callback(value);
    return value.x * 1000 + value.y;
}

/// Configured value parameters and returns spelled unqualified inside their namespace.
inline int point_sum(const point2& value)
{
    return value.x + value.y;
}

inline point2 make_point(int x, int y)
{
    return point2{x, y};
}
}
