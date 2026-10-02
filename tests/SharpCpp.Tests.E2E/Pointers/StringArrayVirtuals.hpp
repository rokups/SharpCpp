#pragma once

#include <cstring>

// String ranges through virtual methods. A director callback receives the range as managed copies
// (null for a null element); a non-director virtual method forwards through the vtable like any
// direct call. A counted char** director callback receives the array as managed copies too; its count
// (counted_by, or the adjacent count parameter) stays an ordinary parameter. A char** director callback
// without an identifiable count, or a raw `pointers` array, ignores the director with a reason.
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

struct [[sharpcpp::director]] counted_string_visitor
{
    virtual ~counted_string_visitor() = default;

    virtual int after(const char* const* names, unsigned int count) = 0;
    virtual int before(int count, const char* const* names) = 0;
    virtual int tagged([[sharpcpp::counted_by(total)]] const char* const* names, int tag, int total) = 0;
};

inline int invoke_counted_string_visitor(counted_string_visitor* visitor)
{
    const char* const items[3] = { "ab", nullptr, "cde" };
    return visitor->after(items, 3) + visitor->before(3, items) + visitor->tagged(items, 7, 3);
}

inline int invoke_empty_counted_string_visitor(counted_string_visitor* visitor)
{
    return visitor->after(nullptr, 0) + visitor->before(0, nullptr) + visitor->tagged(nullptr, 7, 0);
}

// Directors that cannot bind a char** callback: no identifiable count, and a raw `pointers` array.
struct [[sharpcpp::director]] uncounted_string_visitor
{
    virtual ~uncounted_string_visitor() = default;

    virtual int visit(const char* const* names) = 0;
};

struct [[sharpcpp::director]] unrelated_count_string_visitor
{
    virtual ~unrelated_count_string_visitor() = default;

    virtual int visit(const char* const* names, int flags) = 0;
};

struct [[sharpcpp::director]] raw_counted_string_visitor
{
    virtual ~raw_counted_string_visitor() = default;

    virtual int visit([[sharpcpp::shape(pointers)]] const char* const* names, unsigned int count) = 0;
};
