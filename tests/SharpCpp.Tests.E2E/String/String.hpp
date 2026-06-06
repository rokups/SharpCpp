#pragma once

extern "C"
{
    unsigned long strlen(const char* s);
    char* strcpy(char* dest, const char* src);
    void* malloc(unsigned long size);
    void free(void* ptr);
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

inline void get_name(const char** value)
{
    const char* source = "SharpCpp";
    auto* buf = (char*)malloc(strlen(source) + 1);
    strcpy(buf, source);
    *value = buf;
}

inline void replace_name([[sharpcpp::ref]] char** value)
{
    const char* source = "replaced";
    auto* buf = (char*)malloc(strlen(source) + 1);
    strcpy(buf, source);
    free(*value);
    *value = buf;
}
