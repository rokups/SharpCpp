#pragma once

#include "Sample.hpp"
#include <SharpCpp/Marshaller.hpp>

namespace SharpCpp
{
template <>
struct [[sharpcpp::managed_type(
    public_type = "global::System.ReadOnlySpan<byte>",
    managed_marshaller = "global::SharpCpp.Support.Utf8SpanAbiMarshaller",
    undefaultable = true
)]]
Marshaller<ea::string_view> : StringViewMarshallerBase<ea::string_view> {};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::System.ReadOnlySpan<byte>",
    managed_marshaller = "global::SharpCpp.Support.Utf8SpanAbiReturnMarshaller"
)]] ReturnMarshaller<ea::string_view> : Marshaller<ea::string_view> {};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Test.Vector3",
    managed_marshaller = "global::Test.Vector3Marshaller",
    suppress_emission = true
)]] Marshaller<sample::value3>
{
    using cpp_type = sample::value3;
    using abi_type = sample::value3;
    abi_type ConvertToAbi(cpp_type value) noexcept { return value; }
    cpp_type ConvertToCpp(abi_type value) noexcept { return value; }
};
}
