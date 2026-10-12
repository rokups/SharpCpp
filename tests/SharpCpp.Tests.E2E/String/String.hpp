#pragma once

#include <cstdlib>
#include <cstring>
#include <string>
#include <string_view>

#include <SharpCpp/TaskMemory.hpp>

#include "ExternalText.h"

namespace e2e_std
{
template <typename T>
class basic_string_view
{
public:
    using size_type = std::size_t;

    constexpr basic_string_view() = default;

    constexpr basic_string_view(const T* value)
        : value_(value)
        , size_(value != nullptr ? std::char_traits<T>::length(value) : 0)
    {
    }

    constexpr basic_string_view(const T* value, size_type size)
        : value_(value)
        , size_(value != nullptr ? size : 0)
    {
    }

    constexpr const T* data() const noexcept { return value_; }
    constexpr size_type size() const noexcept { return size_; }

private:
    const T* value_{};
    size_type size_{};
};

using string_view = basic_string_view<char>;

class string
{
public:
    string() = default;

    std::size_t size() const
    {
        return value_.size();
    }

    const char* data() const
    {
        return value_.data();
    }

    void push_back(char value)
    {
        value_.push_back(value);
    }

    void clear()
    {
        value_.clear();
    }

private:
    std::string value_;
};
}

inline int string_length(const char* value)
{
    int count = 0;
    while (*value++)
    {
        ++count;
    }
    return count;
}

inline bool string_equals(const char* lhs, const char* rhs)
{
    while (*lhs && *rhs && *lhs == *rhs)
    {
        ++lhs;
        ++rhs;
    }
    return *lhs == *rhs;
}

inline void get_name([[sharpcpp::out]] const char** value)
{
    // An output char** is borrowed: the wrapper copies the text and never frees the pointer.
    *value = "SharpCpp";
}

inline void replace_name([[sharpcpp::ref]] char** value)
{
    const char* source = "replaced";
    // The managed side allocates and frees a `ref` string with the task allocator.
    auto* buf = static_cast<char*>(SharpCpp::AllocateTaskMemory(std::strlen(source) + 1));
    std::strcpy(buf, source);
    SharpCpp::FreeTaskMemory(*value);
    *value = buf;
}

inline const char* get_message()
{
    return "Hello from SharpCpp";
}

inline int measure_std_string(const std::string& value)
{
    return static_cast<int>(value.size());
}

inline int string_view_length(std::string_view value)
{
    return static_cast<int>(value.size());
}

inline bool string_view_equals(std::string_view lhs, std::string_view rhs)
{
    return lhs == rhs;
}

inline int string_view_embedded_null_length(std::string_view value)
{
    return static_cast<int>(value.size());
}

inline std::string_view get_string_view_message()
{
    return "Hello from string_view";
}

inline std::string_view get_string_view_with_embedded_null()
{
    static constexpr char value[] = { 'a', '\0', 'b' };
    return std::string_view(value, sizeof(value));
}

inline int custom_string_view_length(e2e_std::string_view value)
{
    return static_cast<int>(value.size());
}

inline int custom_string_view_embedded_null_length(e2e_std::string_view value)
{
    return static_cast<int>(value.size());
}

inline e2e_std::string_view get_custom_string_view_message()
{
    return "Hello from custom string_view";
}

// The char text_view marshaller is found by template head; the char16_t argument is
// unsupported, so this declaration must degrade instead of aborting generation.
inline int custom_u16_text_view_length(e2e_ext::basic_text_view<char16_t> value)
{
    return static_cast<int>(value.size());
}

inline e2e_ext::u16text_view custom_u16_text_view_empty()
{
    return {};
}

inline int custom_text_view_length(e2e_ext::text_view value)
{
    return static_cast<int>(value.size());
}

inline e2e_std::string make_custom_std_string(const char* value)
{
    e2e_std::string result;
    while (*value)
    {
        result.push_back(*value++);
    }

    return result;
}

inline int measure_custom_std_string(const e2e_std::string& value)
{
    return static_cast<int>(value.size());
}

inline int measure_custom_std_string_pointer(const e2e_std::string* value)
{
    return value != nullptr ? static_cast<int>(value->size()) : -1;
}

inline void append_custom_std_string(e2e_std::string& value, unsigned char byte)
{
    value.push_back(static_cast<char>(byte));
}
