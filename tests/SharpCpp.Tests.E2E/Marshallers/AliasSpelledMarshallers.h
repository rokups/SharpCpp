#pragma once

#include <SharpCpp/Marshaller.hpp>

#include "AliasSpelled.hpp"

namespace SharpCpp
{
template <>
struct [[sharpcpp::managed_type(
    public_type = "global::System.ReadOnlySpan<byte>",
    managed_marshaller = "global::SharpCpp.Support.Utf8SpanAbiMarshaller",
    undefaultable = true
)]]
Marshaller<alias_spelled_ea::string_view> : StringViewMarshallerBase<alias_spelled_ea::string_view> {};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::System.ReadOnlySpan<byte>",
    managed_marshaller = "global::SharpCpp.Support.Utf8SpanAbiReturnMarshaller"
)]] ReturnMarshaller<alias_spelled_ea::string_view> : Marshaller<alias_spelled_ea::string_view> {};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::SharpCpp.Tests.E2E.Marshallers.AliasValue3",
    managed_marshaller = "global::SharpCpp.Tests.E2E.Marshallers.AliasValue3Marshaller",
    suppress_emission = true
)]] Marshaller<alias_spelled::value3>
{
    using cpp_type = alias_spelled::value3;
    using abi_type = alias_spelled::value3;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept { return value; }
    constexpr cpp_type ConvertToCpp(abi_type value) noexcept { return value; }
};

template <>
struct ReturnMarshaller<alias_spelled::value3> : Marshaller<alias_spelled::value3> {};

template <>
struct [[sharpcpp::managed_type(public_type = "float", marshaller_type = "float", suppress_emission = true)]]
Marshaller<alias_spelled::weight>
{
    using cpp_type = alias_spelled::weight;
    using abi_type = float;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept { return value.kilograms; }
    constexpr cpp_type ConvertToCpp(abi_type value) noexcept { return {value}; }
};
}
