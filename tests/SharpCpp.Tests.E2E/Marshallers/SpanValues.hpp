#pragma once

#include <cstddef>
#include <cstdint>
#include <span>

namespace span_values
{
/// A blittable value that the marshaller configures as a managed struct. Its abi_type is the
/// struct itself, so a span gives managed code the native elements in place.
struct point
{
    float x{};
    float y{};
    float z{};
};

/// A value whose abi_type has a different layout. A span of it cannot cross in place.
struct swapped
{
    float a{};
    float b{};
};

enum class shade : std::uint8_t
{
    dark = 1,
    mid = 2,
    light = 3
};

/// A class that crosses the ABI as a handle. A span of it cannot cross in place.
class item
{
public:
    int value{};
};

inline int span_sum_ints(std::span<const int> values)
{
    int sum = 0;
    for (int value : values)
        sum += value;
    return sum;
}

inline void span_fill_ints(std::span<int> values, int start)
{
    for (int& value : values)
        value = start++;
}

inline std::size_t span_count_bytes(const std::span<const std::uint8_t>& bytes, std::uint8_t match)
{
    std::size_t count = 0;
    for (std::uint8_t value : bytes)
        count += value == match ? 1 : 0;
    return count;
}

inline void span_increment_bytes(const std::span<std::uint8_t>& bytes)
{
    for (std::uint8_t& value : bytes)
        ++value;
}

inline float span_sum_points(std::span<const point> points)
{
    float sum = 0.0f;
    for (const point& value : points)
        sum += value.x + value.y + value.z;
    return sum;
}

inline void span_scale_points(std::span<point> points, float factor)
{
    for (point& value : points)
    {
        value.x *= factor;
        value.y *= factor;
        value.z *= factor;
    }
}

inline int span_sum_shades(std::span<const shade> shades)
{
    int sum = 0;
    for (shade value : shades)
        sum += static_cast<int>(value);
    return sum;
}

inline int span_sum_fixed(std::span<const int, 3> values)
{
    return values[0] + values[1] + values[2];
}

inline void span_fill_fixed(std::span<int, 2> values)
{
    values[0] = 11;
    values[1] = 22;
}

/// The span parameter is followed by a parameter with a default value.
inline int span_sum_with_offset(std::span<const int> values, int offset = 100)
{
    return span_sum_ints(values) + offset;
}

inline std::size_t span_point_size()
{
    return sizeof(point);
}

inline std::size_t span_shade_size()
{
    return sizeof(shade);
}

inline std::span<int> span_mutable_storage()
{
    static int storage[4] = {1, 2, 3, 4};
    return storage;
}

inline int span_storage_value(std::size_t index)
{
    return span_mutable_storage()[index];
}

inline std::span<const point> span_const_points()
{
    static const point points[2] = {{1.0f, 2.0f, 3.0f}, {4.0f, 5.0f, 6.0f}};
    return points;
}

inline std::span<const int> span_empty_view()
{
    return {};
}

inline std::span<const int, 3> span_fixed_view()
{
    static const int values[3] = {7, 8, 9};
    return std::span<const int, 3>(values);
}

/// Excluded: the element is a handle.
inline int span_sum_items(std::span<const item> items)
{
    int sum = 0;
    for (const item& value : items)
        sum += value.value;
    return sum;
}

/// Excluded: the element has a different ABI layout.
inline float span_sum_swapped(std::span<const swapped> values)
{
    float sum = 0.0f;
    for (const swapped& value : values)
        sum += value.a - value.b;
    return sum;
}

/// A class with span members.
class byte_buffer
{
public:
    explicit byte_buffer(std::span<const std::uint8_t> data)
    {
        for (std::size_t index = 0; index < data.size() && index < 4; ++index)
            storage_[index] = data[index];
        size_ = data.size() < 4 ? data.size() : 4;
    }

    std::size_t size() const
    {
        return size_;
    }

    std::span<std::uint8_t> bytes()
    {
        return std::span<std::uint8_t>(storage_, size_);
    }

    std::span<const std::uint8_t> view() const
    {
        return std::span<const std::uint8_t>(storage_, size_);
    }

private:
    std::uint8_t storage_[4]{};
    std::size_t size_{};
};
}
