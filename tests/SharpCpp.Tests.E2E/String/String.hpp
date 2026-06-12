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

class ea_compat_string
{
public:
    ea_compat_string() = default;

    std::size_t size() const
    {
        return value_.size();
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

inline int measure_ea_string(const ea_compat_string& value)
{
    return static_cast<int>(value.size());
}
