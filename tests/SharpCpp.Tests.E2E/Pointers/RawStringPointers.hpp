#pragma once

#include <cstring>

// [[sharpcpp::shape(pointers)]] on a char** parameter (or a consumer-set PointerPointerShape
// metadata value "pointers") keeps the raw binding of an input string array or range:
// `ReadOnlySpan<nint>` of NUL-terminated UTF-8 pointers. The caller owns the array and the
// strings, so native code may retain them; the default binding (`ReadOnlySpan<string?>`) copies
// the strings for the duration of the call only.

inline int measure_raw_names([[sharpcpp::shape(pointers)]] const char* const* names, unsigned int count)
{
    int result = 0;
    for (unsigned int i = 0; i < count; ++i)
        result += names[i] == nullptr ? 1000 : static_cast<int>(std::strlen(names[i]));
    return result;
}

// Consumer-side selection: the E2E generator sets PointerPointerShape "pointers" on `names`.
inline int measure_configured_raw_names(int total, char** names)
{
    int result = 0;
    for (int i = 0; i < total; ++i)
        result += static_cast<int>(std::strlen(names[i]));
    return result;
}

// A mutable char** keeps whatever the generic raw path yields.
inline int rewrite_raw_names([[sharpcpp::shape(pointers)]] char** names, unsigned int count)
{
    static char replacement[] = "static-name";
    if (count > 0)
        names[0] = replacement;
    return static_cast<int>(count);
}

// Native code keeps the array and the strings: valid only while the caller keeps them alive.
struct raw_name_store
{
    void retain([[sharpcpp::shape(pointers)]] const char* const* names, unsigned int count)
    {
        names_ = names;
        count_ = count;
    }

    int retained_length() const
    {
        int result = 0;
        for (unsigned int i = 0; i < count_; ++i)
            result += static_cast<int>(std::strlen(names_[i]));
        return result;
    }

    unsigned int retained_count() const
    {
        return count_;
    }

private:
    const char* const* names_{};
    unsigned int count_{};
};

// A range begin: the raw pointer elements of `[begin, end)`.
inline int measure_raw_name_range(
    [[sharpcpp::shape(pointers)]] [[sharpcpp::ended_by(end)]] const char* const* begin,
    const char* const* end)
{
    int result = 0;
    for (const char* const* it = begin; it != end; ++it)
        result += static_cast<int>(std::strlen(*it));
    return result;
}

// An untagged sibling of the range keeps the default string binding.
inline int measure_default_name_range([[sharpcpp::ended_by(end)]] const char* const* begin, const char* const* end)
{
    int result = 0;
    for (const char* const* it = begin; it != end; ++it)
        result += static_cast<int>(std::strlen(*it));
    return result;
}

// A secondary base's virtual method with a raw string array is projected onto the derived wrapper.
struct raw_names_left
{
    int left_marker() const
    {
        return 1;
    }
};

struct raw_names_right
{
    virtual ~raw_names_right() = default;

    virtual int sum_raw_names([[sharpcpp::shape(pointers)]] const char* const* names, unsigned int count) const
    {
        return measure_raw_names(names, count);
    }
};

struct raw_names_both : raw_names_left, raw_names_right
{
    raw_names_both() = default;
};

// A `pointers` shape that cannot be honoured degrades the function with a diagnostic.
struct raw_names_item
{
    int value{};
};

inline int bad_pointers_record([[sharpcpp::shape(pointers)]] raw_names_item** items, unsigned int count)
{
    return static_cast<int>(count) + (count > 0 ? items[0]->value : 0);
}

inline int bad_shape_scalar([[sharpcpp::shape(array)]] int value)
{
    return value;
}

inline int bad_shape_value([[sharpcpp::shape(sideways)]] const char* const* names, unsigned int count)
{
    return measure_raw_names(names, count);
}

inline int bad_shape_conflict_in([[sharpcpp::in]] [[sharpcpp::shape(pointers)]] const char* const* names, unsigned int count)
{
    return measure_raw_names(names, count);
}

inline void bad_shape_conflict_out([[sharpcpp::out]] [[sharpcpp::shape(pointers)]] char** value)
{
    *value = nullptr;
}
