#pragma once

#include <cstring>

// String ranges through virtual methods. A director callback receives the range as managed copies
// (null for a null element); a non-director virtual method forwards through the vtable like any
// direct call. A counted char** director callback is not supported (its parameter is not an in-value).
struct [[sharpcpp::director]] string_range_visitor
{
    virtual ~string_range_visitor() = default;

    virtual int visit([[sharpcpp::ended_by(end)]] const char* const* begin, const char* const* end) = 0;
};

inline int invoke_string_range_visitor(string_range_visitor* visitor)
{
    const char* const items[3] = { "ab", nullptr, "cde" };
    return visitor->visit(items, items + 3);
}

inline int invoke_empty_string_range_visitor(string_range_visitor* visitor)
{
    return visitor->visit(nullptr, nullptr);
}

struct names_shape
{
    virtual ~names_shape() = default;

    virtual int sum_counted(const char* const* names, unsigned int count) = 0;
    virtual int sum_range([[sharpcpp::ended_by(end)]] const char* const* begin, const char* const* end) = 0;
};

struct total_length_names_shape final : names_shape
{
    int sum_counted(const char* const* names, unsigned int count) override
    {
        int result = 0;
        for (unsigned int i = 0; i < count; ++i)
            result += static_cast<int>(std::strlen(names[i]));
        return result;
    }

    int sum_range([[sharpcpp::ended_by(end)]] const char* const* begin, const char* const* end) override
    {
        return sum_counted(begin, static_cast<unsigned int>(end - begin));
    }
};

inline names_shape* create_total_length_names_shape()
{
    return new total_length_names_shape();
}
