#pragma once

#include <SharpCpp/Marshaller.hpp>

#include "Field.hpp"

namespace SharpCpp
{

template <>
struct [[sharpcpp::managed_type(
    public_type = "int",
    suppress_emission = true
)]] Marshaller<projected_field_value>
{
    using cpp_type = projected_field_value;
    using abi_type = int;

    abi_type ConvertToAbi(cpp_type value) noexcept { return value.value; }
    cpp_type ConvertToCpp(abi_type value) noexcept { return {value}; }
};

template <>
struct Marshaller<const projected_field_value&>
    : ProjectedValueConstReferenceMarshallerBase<projected_field_value> {};

template <>
struct Marshaller<projected_field_value&>
    : ProjectedValueReferenceMarshallerBase<projected_field_value> {};

} // namespace SharpCpp
