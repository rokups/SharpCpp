#pragma once

#include <cstddef>
#include <cstring>

namespace alias_spelled_eastl
{
/// A consumer string view reached through a namespace alias and an alias of its char
/// specialization; the marshaller is spelled the same way.
template <typename T>
class basic_string_view
{
public:
    using size_type = std::size_t;

    constexpr basic_string_view() = default;

    constexpr basic_string_view(const T* value)
        : value_(value)
        , size_(value != nullptr ? std::strlen(value) : 0)
    {
    }

    constexpr basic_string_view(const T* value, size_type size)
        : value_(value)
        , size_(size)
    {
    }

    constexpr const T* data() const noexcept { return value_; }
    constexpr size_type size() const noexcept { return size_; }

private:
    const T* value_{};
    size_type size_{};
};

using string_view = basic_string_view<char>;
}

namespace alias_spelled_ea = alias_spelled_eastl;

namespace alias_spelled
{
struct value3
{
    float x;
    float y;
    float z;
};

/// A const-qualified alias and a plain alias of a configured value struct.
using value3_arg = const value3;
using value3_alias = value3;

struct value3_box
{
    value3_alias value;
};

inline int measure_view(alias_spelled_ea::string_view value)
{
    return static_cast<int>(value.size());
}

inline int measure_default_view(alias_spelled_ea::string_view value = "default")
{
    return static_cast<int>(value.size()) + 100;
}

inline alias_spelled_ea::string_view get_view()
{
    return "alias view";
}

inline float consume_value3(value3_arg value)
{
    return value.x + value.y * 10 + value.z * 100;
}
}
