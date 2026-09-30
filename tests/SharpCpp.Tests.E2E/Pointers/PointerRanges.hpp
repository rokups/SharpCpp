#pragma once

#include <cstring>

// [[sharpcpp::ended_by(end)]] on a begin pointer parameter (mirrors Clang `__ended_by`), or a
// consumer-set MetadataKeys.EndedBy, binds `(T* begin, T* end)` as ONE managed span parameter:
// `ReadOnlySpan<T>` for a const pointee, `Span<T>` for a mutable one. `end` is hidden from the
// managed signature; the wrapper passes `begin` = the pinned pointer and `end` = `begin +
// span.Length`. An empty span passes null/null, a valid empty range. There is no name-based
// default: an untagged `(begin, end)` pair stays two raw pointers.

inline int sum_range([[sharpcpp::ended_by(end)]] const int* begin, const int* end)
{
    int result = 0;
    for (const int* it = begin; it != end; ++it)
        result += *it;
    return result;
}

inline void fill_range([[sharpcpp::ended_by(end)]] int* begin, int* end)
{
    int value = 1;
    for (int* it = begin; it != end; ++it)
        *it = value++;
}

// The end pointer keeps its native position, so parameters after it keep theirs.
inline int sum_range_then_flag([[sharpcpp::ended_by(end)]] const int* begin, const int* end, bool inside)
{
    int result = 0;
    for (const int* it = begin; it != end; ++it)
        result += *it;
    return inside ? result : -result;
}

inline int sum_range_scaled([[sharpcpp::ended_by(end)]] const int* begin, const int* end, int scale = 1)
{
    int result = 0;
    for (const int* it = begin; it != end; ++it)
        result += *it * scale;
    return result;
}

// The consumer generator tags this one (a header it cannot edit).
inline int sum_configured_range(const int* begin, const int* end)
{
    int result = 0;
    for (const int* it = begin; it != end; ++it)
        result += *it;
    return result;
}

// An untagged pair is not a range.
inline int sum_untagged_pair(const int* begin, const int* end)
{
    return static_cast<int>(end - begin);
}

struct range_holder
{
    range_holder([[sharpcpp::ended_by(end)]] const int* begin, const int* end)
        : total_(static_cast<int>(end - begin))
    {
    }

    int total_;
};

// ended_by that cannot be honoured degrades the function with a diagnostic.
inline int bad_ended_unknown([[sharpcpp::ended_by(missing)]] const int* begin, const int* end)
{
    return static_cast<int>(end - begin);
}

inline int bad_ended_empty([[sharpcpp::ended_by()]] const int* begin, const int* end)
{
    return static_cast<int>(end - begin);
}

inline int bad_ended_type([[sharpcpp::ended_by(end)]] const int* begin, const float* end)
{
    return static_cast<int>(reinterpret_cast<const char*>(end) - reinterpret_cast<const char*>(begin));
}

inline int bad_ended_scalar([[sharpcpp::ended_by(end)]] int begin, int end)
{
    return end - begin;
}

inline int bad_ended_gap([[sharpcpp::ended_by(end)]] const int* begin, int gap, const int* end)
{
    return static_cast<int>(end - begin) + gap;
}

inline int bad_ended_counted([[sharpcpp::ended_by(end)]] [[sharpcpp::counted_by(n)]] const int* begin, const int* end, int n)
{
    return static_cast<int>(end - begin) + n;
}

inline int bad_ended_chain(
    [[sharpcpp::ended_by(mid)]] const int* begin,
    [[sharpcpp::ended_by(end)]] const int* mid,
    const int* end)
{
    return static_cast<int>(end - begin) + static_cast<int>(mid - begin);
}

inline int bad_ended_end_counted([[sharpcpp::ended_by(end)]] const int* begin, [[sharpcpp::counted_by(n)]] const int* end, int n)
{
    return static_cast<int>(end - begin) + n;
}

struct range_item
{
    int value;
};

inline int bad_ended_record([[sharpcpp::ended_by(end)]] const range_item* begin, const range_item* end)
{
    return static_cast<int>(end - begin);
}

// A template parameter cannot be the pointee (the parser does not expose `const T*`), so the
// range is over `int` and the function is generic through another parameter.
template <typename T>
inline int bad_ended_template([[sharpcpp::ended_by(end)]] const int* begin, const int* end, T extra)
{
    return static_cast<int>(end - begin) + static_cast<int>(extra);
}

template int bad_ended_template<int>(const int* begin, const int* end, int extra);

// A native default on begin or end cannot be kept: variants could cut `end` off the call.
inline int bad_ended_begin_default([[sharpcpp::ended_by(end)]] const int* begin = nullptr, const int* end = nullptr)
{
    return static_cast<int>(end - begin);
}

inline int bad_ended_end_default([[sharpcpp::ended_by(end)]] const int* begin, const int* end = nullptr)
{
    return static_cast<int>(end - begin);
}

// A class template member is cloned per instantiation without parameter metadata.
template <typename T>
struct
[[sharpcpp::instantiate("int")]]
range_template_holder
{
    T echo(T value) const
    {
        return value;
    }

    int bad_ended_member([[sharpcpp::ended_by(end)]] const int* begin, const int* end) const
    {
        return static_cast<int>(end - begin);
    }
};

// A secondary base's methods are projected onto the derived wrapper and its interface.
struct range_left
{
    int left_marker() const
    {
        return 1;
    }
};

struct range_right
{
    int sum_right_range([[sharpcpp::ended_by(end)]] const int* begin, const int* end) const
    {
        int result = 0;
        for (const int* it = begin; it != end; ++it)
            result += *it;
        return result;
    }
};

struct range_both : range_left, range_right
{
    range_both() = default;
};

// Enum elements bind as `ReadOnlySpan<T>` / `Span<T>` like integers.
enum class range_color : int
{
    red = 1,
    green = 2,
    blue = 4
};

inline int sum_color_range([[sharpcpp::ended_by(end)]] const range_color* begin, const range_color* end)
{
    int result = 0;
    for (const range_color* it = begin; it != end; ++it)
        result += static_cast<int>(*it);
    return result;
}

inline void fill_color_range([[sharpcpp::ended_by(end)]] range_color* begin, range_color* end)
{
    for (range_color* it = begin; it != end; ++it)
        *it = range_color::blue;
}

// Record pointer ranges bind as `ReadOnlySpan<T?>`, like a counted record pointer array; a
// null entry stays null.
struct range_widget
{
    explicit range_widget(int value)
        : value_(value)
    {
    }

    int get_value() const
    {
        return value_;
    }

private:
    int value_{};
};

inline int sum_widget_range([[sharpcpp::ended_by(end)]] range_widget** begin, range_widget** end)
{
    int result = 0;
    for (range_widget** it = begin; it != end; ++it)
        result += *it == nullptr ? 100 : (*it)->get_value();
    return result;
}

inline int sum_const_widget_range([[sharpcpp::ended_by(end)]] range_widget* const* begin, range_widget* const* end)
{
    int result = 0;
    for (range_widget* const* it = begin; it != end; ++it)
        result += *it == nullptr ? 100 : (*it)->get_value();
    return result;
}

// A char** range is an array of NUL-terminated UTF-8 pointers, as for a counted char**.
inline int measure_char_range([[sharpcpp::ended_by(end)]] char** begin, char** end)
{
    int result = 0;
    for (char** it = begin; it != end; ++it)
        result += static_cast<int>(std::strlen(*it));
    return result;
}

inline int measure_const_char_range([[sharpcpp::ended_by(end)]] const char* const* begin, const char* const* end)
{
    int result = 0;
    for (const char* const* it = begin; it != end; ++it)
        result += static_cast<int>(std::strlen(*it));
    return result;
}

// `void* const*` is a raw pointer array (the element is a handle), like a counted one.
inline int count_address_range([[sharpcpp::ended_by(end)]] void* const* begin, void* const* end)
{
    int result = 0;
    for (void* const* it = begin; it != end; ++it)
        result += *it != nullptr ? 1 : 0;
    return result;
}

// An explicit single/out shape on begin, or any explicit shape on end, cannot be honoured.
inline int bad_ended_widget_out([[sharpcpp::out]] [[sharpcpp::ended_by(end)]] range_widget** begin, range_widget** end)
{
    return static_cast<int>(end - begin);
}

inline int bad_ended_widget_end_out([[sharpcpp::ended_by(end)]] range_widget** begin, [[sharpcpp::out]] range_widget** end)
{
    return static_cast<int>(end - begin);
}
