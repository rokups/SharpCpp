#pragma once

#include <SharpCpp/Marshaller.hpp>
#include <SharpCpp/detail/MarshallerSupport.hpp>

#include "SpanValues.hpp"

namespace SharpCpp
{
template <>
struct [[sharpcpp::managed_type(
    public_type = "global::SharpCpp.Tests.E2E.Marshallers.SpanPoint",
    marshaller_type = "global::SharpCpp.Tests.E2E.Marshallers.SpanPoint",
    suppress_emission = true
)]] Marshaller<span_values::point>
{
    using cpp_type = span_values::point;
    using abi_type = span_values::point;

    abi_type ConvertToAbi(cpp_type value) noexcept { return value; }
    cpp_type ConvertToCpp(abi_type value) noexcept { return value; }
};

/// The ABI struct of span_values::swapped. The order of the members is b first.
struct swapped_abi
{
    float b;
    float a;
};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::SharpCpp.Tests.E2E.Marshallers.SpanSwapped",
    marshaller_type = "global::SharpCpp.Tests.E2E.Marshallers.SpanSwapped",
    suppress_emission = true
)]] Marshaller<span_values::swapped>
{
    using cpp_type = span_values::swapped;
    using abi_type = swapped_abi;

    abi_type ConvertToAbi(cpp_type value) noexcept { return {value.b, value.a}; }
    cpp_type ConvertToCpp(abi_type value) noexcept { return {value.a, value.b}; }
};
}
