#pragma once

#include <cstring>

// [[sharpcpp::null_terminated]] on an input `char**` / `const char* const*` parameter (or a
// consumer-set NullTerminatedStringArray metadata flag) marks a list that native code reads up
// to a NULL element. The managed wrapper (`ReadOnlySpan<string?>`) appends that terminator
// itself, so the caller passes only the strings. A count parameter, if there is one, is passed
// through unchanged (the terminator is not counted). An empty span still passes a null pointer.

// Walks to the terminating NULL; -1 (and an element count of -1) for a null list.
inline int measure_names_until_null(const char* const* names, int* element_count)
{
    if (names == nullptr)
    {
        *element_count = -1;
        return -1;
    }

    int result = 0;
    int elements = 0;
    for (; names[elements] != nullptr; ++elements)
        result += static_cast<int>(std::strlen(names[elements]));
    *element_count = elements;
    return result;
}

inline int measure_terminated_names([[sharpcpp::null_terminated]] const char* const* names, int* element_count)
{
    return measure_names_until_null(names, element_count);
}

// Consumer-side tag: the E2E generator sets NullTerminatedStringArray on `names`.
inline int measure_configured_terminated_names(const char* const* names, int* element_count)
{
    return measure_names_until_null(names, element_count);
}

// An untagged sibling: the wrapper passes exactly the strings, so the caller appends the NULL.
inline int measure_untagged_names(const char* const* names, int* element_count)
{
    return measure_names_until_null(names, element_count);
}

// A count and a terminator: native checks that names[count] is NULL.
inline int measure_counted_terminated_names(
    [[sharpcpp::null_terminated]] const char* const* names,
    unsigned int count,
    int* terminated)
{
    *terminated = names != nullptr && names[count] == nullptr ? 1 : 0;
    int result = 0;
    for (unsigned int i = 0; i < count; ++i)
        result += static_cast<int>(std::strlen(names[i]));
    return result;
}

// A mutable array: native rewrites the first element pointer; the wrapper frees only its own copies.
inline int rewrite_terminated_names([[sharpcpp::in]] [[sharpcpp::null_terminated]] char** names, int* element_count)
{
    static char replacement[] = "static-name";
    if (names == nullptr)
    {
        *element_count = -1;
        return -1;
    }

    int elements = 0;
    while (names[elements] != nullptr)
        ++elements;
    if (elements > 0)
        names[0] = replacement;
    *element_count = elements;
    return elements;
}

// A secondary base's virtual method is projected onto the derived wrapper.
struct terminated_names_left
{
    int left_marker() const
    {
        return 1;
    }
};

struct terminated_names_right
{
    virtual ~terminated_names_right() = default;

    virtual int measure_names([[sharpcpp::null_terminated]] const char* const* names, int* element_count) const
    {
        return measure_names_until_null(names, element_count);
    }
};

struct terminated_names_both : terminated_names_left, terminated_names_right
{
    terminated_names_both() = default;
};

// A tag that cannot be honoured degrades the function with a diagnostic.
inline int bad_terminated_ints([[sharpcpp::null_terminated]] const int* values, unsigned int count)
{
    return count > 0 ? values[0] : 0;
}

// A mutable char** with no count is a single output: the tag does not change the shape.
inline void bad_terminated_single([[sharpcpp::null_terminated]] char** value)
{
    *value = nullptr;
}

inline int bad_terminated_range(
    [[sharpcpp::null_terminated]] [[sharpcpp::ended_by(end)]] const char* const* begin,
    const char* const* end)
{
    return static_cast<int>(end - begin);
}

inline int bad_terminated_raw([[sharpcpp::null_terminated]] [[sharpcpp::shape(pointers)]] const char* const* names)
{
    return names == nullptr ? 0 : 1;
}
