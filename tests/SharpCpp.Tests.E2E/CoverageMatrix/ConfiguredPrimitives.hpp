#pragma once

#include <cstdint>

struct configured_bool { bool value{}; };
struct configured_sbyte { std::int8_t value{}; };
struct configured_byte { std::uint8_t value{}; };
struct configured_short { std::int16_t value{}; };
struct configured_ushort { std::uint16_t value{}; };
struct configured_uint { std::uint32_t value{}; };
struct configured_long { std::int64_t value{}; };
struct configured_ulong { std::uint64_t value{}; };
struct configured_float { float value{}; };
struct configured_double { double value{}; };
struct configured_nint { std::intptr_t value{}; };
struct configured_nuint { std::uintptr_t value{}; };

inline configured_bool configured_bool_round_trip(configured_bool value) { return value; }
inline configured_sbyte configured_sbyte_round_trip(configured_sbyte value) { return value; }
inline configured_byte configured_byte_round_trip(configured_byte value) { return value; }
inline configured_short configured_short_round_trip(configured_short value) { return value; }
inline configured_ushort configured_ushort_round_trip(configured_ushort value) { return value; }
inline configured_uint configured_uint_round_trip(configured_uint value) { return value; }
inline configured_long configured_long_round_trip(configured_long value) { return value; }
inline configured_ulong configured_ulong_round_trip(configured_ulong value) { return value; }
inline configured_float configured_float_round_trip(configured_float value) { return value; }
inline configured_double configured_double_round_trip(configured_double value) { return value; }
inline configured_nint configured_nint_round_trip(configured_nint value) { return value; }
inline configured_nuint configured_nuint_round_trip(configured_nuint value) { return value; }
