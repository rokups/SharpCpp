#pragma once

struct SampleContext
{
    int Value;
};

inline int add_numbers(int lhs, int rhs)
{
    return lhs + rhs;
}

inline SampleContext* create_context()
{
    static SampleContext context { 42 };
    return &context;
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

#undef SHARPCPP_SAMPLE_BUILTIN
