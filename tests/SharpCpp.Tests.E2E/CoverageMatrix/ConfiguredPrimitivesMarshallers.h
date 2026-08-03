#pragma once

#include <SharpCpp/Marshaller.hpp>

#include "ConfiguredPrimitives.hpp"

namespace SharpCpp
{
template <typename TCpp, typename TAbi>
struct ConfiguredPrimitiveMarshaller
{
    using cpp_type = TCpp;
    using abi_type = TAbi;

    abi_type ConvertToAbi(const cpp_type& value) const noexcept { return value.value; }
    cpp_type ConvertToCpp(abi_type value) const noexcept { return cpp_type{value}; }
};

template <>
struct [[sharpcpp::managed_type(public_type = "bool", marshaller_type = "bool", suppress_emission = true)]]
Marshaller<configured_bool> : ConfiguredPrimitiveMarshaller<configured_bool, bool> {};
template <> struct ReturnMarshaller<configured_bool> : Marshaller<configured_bool> {};

template <>
struct [[sharpcpp::managed_type(public_type = "sbyte", marshaller_type = "sbyte", suppress_emission = true)]]
Marshaller<configured_sbyte> : ConfiguredPrimitiveMarshaller<configured_sbyte, std::int8_t> {};
template <> struct ReturnMarshaller<configured_sbyte> : Marshaller<configured_sbyte> {};

template <>
struct [[sharpcpp::managed_type(public_type = "byte", marshaller_type = "byte", suppress_emission = true)]]
Marshaller<configured_byte> : ConfiguredPrimitiveMarshaller<configured_byte, std::uint8_t> {};
template <> struct ReturnMarshaller<configured_byte> : Marshaller<configured_byte> {};

template <>
struct [[sharpcpp::managed_type(public_type = "short", marshaller_type = "short", suppress_emission = true)]]
Marshaller<configured_short> : ConfiguredPrimitiveMarshaller<configured_short, std::int16_t> {};
template <> struct ReturnMarshaller<configured_short> : Marshaller<configured_short> {};

template <>
struct [[sharpcpp::managed_type(public_type = "ushort", marshaller_type = "ushort", suppress_emission = true)]]
Marshaller<configured_ushort> : ConfiguredPrimitiveMarshaller<configured_ushort, std::uint16_t> {};
template <> struct ReturnMarshaller<configured_ushort> : Marshaller<configured_ushort> {};

template <>
struct [[sharpcpp::managed_type(public_type = "uint", marshaller_type = "uint", suppress_emission = true)]]
Marshaller<configured_uint> : ConfiguredPrimitiveMarshaller<configured_uint, std::uint32_t> {};
template <> struct ReturnMarshaller<configured_uint> : Marshaller<configured_uint> {};

template <>
struct [[sharpcpp::managed_type(public_type = "long", marshaller_type = "long", suppress_emission = true)]]
Marshaller<configured_long> : ConfiguredPrimitiveMarshaller<configured_long, std::int64_t> {};
template <> struct ReturnMarshaller<configured_long> : Marshaller<configured_long> {};

template <>
struct [[sharpcpp::managed_type(public_type = "ulong", marshaller_type = "ulong", suppress_emission = true)]]
Marshaller<configured_ulong> : ConfiguredPrimitiveMarshaller<configured_ulong, std::uint64_t> {};
template <> struct ReturnMarshaller<configured_ulong> : Marshaller<configured_ulong> {};

template <>
struct [[sharpcpp::managed_type(public_type = "float", marshaller_type = "float", suppress_emission = true)]]
Marshaller<configured_float> : ConfiguredPrimitiveMarshaller<configured_float, float> {};
template <> struct ReturnMarshaller<configured_float> : Marshaller<configured_float> {};

template <>
struct [[sharpcpp::managed_type(public_type = "double", marshaller_type = "double", suppress_emission = true)]]
Marshaller<configured_double> : ConfiguredPrimitiveMarshaller<configured_double, double> {};
template <> struct ReturnMarshaller<configured_double> : Marshaller<configured_double> {};

template <>
struct [[sharpcpp::managed_type(public_type = "nint", marshaller_type = "nint", suppress_emission = true)]]
Marshaller<configured_nint> : ConfiguredPrimitiveMarshaller<configured_nint, std::intptr_t> {};
template <> struct ReturnMarshaller<configured_nint> : Marshaller<configured_nint> {};

template <>
struct [[sharpcpp::managed_type(public_type = "nuint", marshaller_type = "nuint", suppress_emission = true)]]
Marshaller<configured_nuint> : ConfiguredPrimitiveMarshaller<configured_nuint, std::uintptr_t> {};
template <> struct ReturnMarshaller<configured_nuint> : Marshaller<configured_nuint> {};
}
