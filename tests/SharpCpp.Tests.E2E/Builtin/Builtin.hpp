#pragma once

#include <cstdint>

struct SampleContext
{
    int Value;
};

inline void* opaque_token()
{
    static int token = 42;
    return &token;
}

inline int add_numbers(int lhs, int rhs)
{
    return lhs + rhs;
}

inline SampleContext* create_context()
{
    static SampleContext context { 42 };
    return &context;
}

inline void* roundtrip_void_ptr(void* value)
{
    return value;
}

inline const void* echo_in_void_ptr(const void* value)
{
    return value;
}

inline void replace_ref_void_ptr(void*& value)
{
    value = opaque_token();
}

inline void write_out_void_ptr(void** value)
{
    *value = opaque_token();
}

inline const void* echo_marked_in_void_ptr([[sharpcpp::in]] void* value)
{
    return value;
}

inline void write_marked_out_void_ptr([[sharpcpp::out]] void*& value)
{
    value = opaque_token();
}

#define SHARPCPP_SAMPLE_BUILTIN(NAME, TYPE, OUT_VALUE) \
inline TYPE roundtrip_##NAME(TYPE value) \
{ \
    return value; \
} \
inline TYPE echo_in_##NAME(const TYPE& value) \
{ \
    return value; \
} \
inline void increment_ref_##NAME(TYPE& value) \
{ \
    value = static_cast<TYPE>(value + 1); \
} \
inline void write_out_##NAME(TYPE* value) \
{ \
    *value = static_cast<TYPE>(OUT_VALUE); \
}

#define SHARPCPP_SAMPLE_POINTER_KIND_BUILTIN(NAME, TYPE, REF_VALUE, OUT_VALUE) \
inline TYPE echo_marked_in_##NAME([[sharpcpp::in]] TYPE* value) \
{ \
    return *value; \
} \
inline void replace_marked_ref_##NAME([[sharpcpp::ref]] TYPE* value) \
{ \
    *value = static_cast<TYPE>(REF_VALUE); \
} \
inline void write_marked_out_##NAME([[sharpcpp::out]] TYPE& value) \
{ \
    value = static_cast<TYPE>(OUT_VALUE); \
}

inline bool roundtrip_bool(bool value)
{
    return value;
}

inline bool echo_in_bool(const bool& value)
{
    return value;
}

inline void toggle_ref_bool(bool& value)
{
    value = !value;
}

inline void write_out_bool(bool* value)
{
    *value = true;
}

inline bool echo_marked_in_bool([[sharpcpp::in]] bool* value)
{
    return *value;
}

inline void replace_marked_ref_bool([[sharpcpp::ref]] bool* value)
{
    *value = true;
}

inline void write_marked_out_bool([[sharpcpp::out]] bool& value)
{
    value = true;
}

SHARPCPP_SAMPLE_BUILTIN(char, unsigned char, 90)
SHARPCPP_SAMPLE_BUILTIN(int8, signed char, -12)
SHARPCPP_SAMPLE_BUILTIN(uint8, unsigned char, 240)
SHARPCPP_SAMPLE_BUILTIN(int16, short, -1234)
SHARPCPP_SAMPLE_BUILTIN(uint16, unsigned short, 45678)
SHARPCPP_SAMPLE_BUILTIN(int32, int, -123456789)
SHARPCPP_SAMPLE_BUILTIN(uint32, unsigned int, 3456789012u)
SHARPCPP_SAMPLE_BUILTIN(int64, long long, -1234567890123456789ll)
SHARPCPP_SAMPLE_BUILTIN(uint64, unsigned long long, 12345678901234567890ull)
SHARPCPP_SAMPLE_BUILTIN(single, float, 6.5f)
SHARPCPP_SAMPLE_BUILTIN(double, double, 9.25)

SHARPCPP_SAMPLE_POINTER_KIND_BUILTIN(char, unsigned char, 42, 90)
SHARPCPP_SAMPLE_POINTER_KIND_BUILTIN(int8, signed char, 42, -12)
SHARPCPP_SAMPLE_POINTER_KIND_BUILTIN(uint8, unsigned char, 42, 240)
SHARPCPP_SAMPLE_POINTER_KIND_BUILTIN(int16, short, 42, -1234)
SHARPCPP_SAMPLE_POINTER_KIND_BUILTIN(uint16, unsigned short, 42, 45678)
SHARPCPP_SAMPLE_POINTER_KIND_BUILTIN(int32, int, 42, -123456789)
SHARPCPP_SAMPLE_POINTER_KIND_BUILTIN(uint32, unsigned int, 42, 3456789012u)
SHARPCPP_SAMPLE_POINTER_KIND_BUILTIN(int64, long long, 42, -1234567890123456789ll)
SHARPCPP_SAMPLE_POINTER_KIND_BUILTIN(uint64, unsigned long long, 42, 12345678901234567890ull)
SHARPCPP_SAMPLE_POINTER_KIND_BUILTIN(single, float, 41.75f, 6.5f)
SHARPCPP_SAMPLE_POINTER_KIND_BUILTIN(double, double, 40.5, 9.25)

#undef SHARPCPP_SAMPLE_BUILTIN
#undef SHARPCPP_SAMPLE_POINTER_KIND_BUILTIN
