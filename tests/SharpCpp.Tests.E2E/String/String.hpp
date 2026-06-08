#pragma once

#include <cstdlib>
#include <cstring>
#include <string>

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

inline void get_name(const char** value)
{
    const char* source = "SharpCpp";
    auto* buf = (char*)std::malloc(std::strlen(source) + 1);
    std::strcpy(buf, source);
    *value = buf;
}

inline void replace_name([[sharpcpp::ref]] char** value)
{
    const char* source = "replaced";
    auto* buf = (char*)std::malloc(std::strlen(source) + 1);
    std::strcpy(buf, source);
    std::free(*value);
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
