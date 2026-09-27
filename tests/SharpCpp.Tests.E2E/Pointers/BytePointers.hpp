#pragma once

namespace byte_pointers
{
/// Byte buffers stay raw pointers in every cv form; `out` writes the pointer.
inline int consume_unsigned_buffer(unsigned char* value) { return value[0]; }
inline int consume_volatile_unsigned_buffer(volatile unsigned char* value) { return value[0]; }
inline int consume_const_volatile_unsigned_buffer(const volatile unsigned char* value) { return value[0]; }
inline int consume_signed_buffer(signed char* value) { return value[0]; }
inline int consume_volatile_signed_buffer(volatile signed char* value) { return value[0]; }
inline int consume_const_volatile_signed_buffer(const volatile signed char* value) { return value[0]; }

inline unsigned char* unsigned_storage()
{
    static unsigned char value[2] = {200, 1};
    return value;
}

inline signed char* signed_storage()
{
    static signed char value[2] = {-5, 1};
    return value;
}

inline void write_unsigned_pointer([[sharpcpp::out]] unsigned char* value)
{
    *value = 42;
}

inline void write_signed_pointer([[sharpcpp::out]] signed char* value)
{
    *value = -42;
}
}
