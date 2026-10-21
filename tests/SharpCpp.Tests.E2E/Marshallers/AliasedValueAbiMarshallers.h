#pragma once

#include <SharpCpp/Marshaller.hpp>

#include "AliasedValueAbi.hpp"

namespace SharpCpp
{

struct aliased_int_pair_abi
{
    int x;
    int y;
};

// The marshaller names the alias. The ABI type differs from the C++ type, so a field setter
// converts through the marshaller and does not copy the C++ type.
template <>
struct [[sharpcpp::managed_type(
    public_type = "global::SharpCpp.Tests.E2E.Marshallers.AliasedIntPair",
    suppress_emission = true
)]] Marshaller<aliased_value_abi::int_pair>
{
    using cpp_type = aliased_value_abi::int_pair;
    using abi_type = aliased_int_pair_abi;

    abi_type ConvertToAbi(const cpp_type& value) noexcept { return {value.x_, value.y_}; }
    cpp_type ConvertToCpp(abi_type value) noexcept { return cpp_type(value.x, value.y); }
};

template <>
struct Marshaller<const aliased_value_abi::int_pair&>
    : ProjectedValueConstReferenceMarshallerBase<aliased_value_abi::int_pair> {};

template <>
struct Marshaller<aliased_value_abi::int_pair&>
    : ProjectedValueReferenceMarshallerBase<aliased_value_abi::int_pair> {};

}
