#pragma once

#include <SharpCpp/Marshaller.hpp>

#include "MarshallerConfig.hpp"

namespace SharpCpp
{
// Attribute tokens separated by spaces and line breaks.
template <>
struct [[ sharpcpp :: managed_type (
    public_type   =   "int" ,
    marshaller_type = "int",
    undefaultable = true ,
    suppress_emission = true
) ]] Marshaller<marshaller_config::level>
{
    using cpp_type = marshaller_config::level;
    using abi_type = int;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept { return value.value; }
    cpp_type ConvertToCpp(abi_type value) noexcept { return marshaller_config::level(value); }
};

template <>
struct [[sharpcpp::managed_type(public_type = "uint", marshaller_type = "uint", suppress_emission = true)]]
Marshaller<marshaller_config::tag>
{
    using cpp_type = marshaller_config::tag;
    using abi_type = unsigned;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept { return value.value; }
    constexpr cpp_type ConvertToCpp(abi_type value) noexcept { return {value}; }
};

template <>
struct ReturnMarshaller<marshaller_config::tag> : Marshaller<marshaller_config::tag> {};

template <class T>
struct tally_helpers
{
    [[sharpcpp::expose_member]]
    static int Count(T& self) { return self.count; }

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false)]]
    static int Weight(T& self) { return self.count; }
};

template <>
struct Marshaller<marshaller_config::tally*> : tally_helpers<marshaller_config::tally>
{
    using cpp_type = marshaller_config::tally*;
    using abi_type = marshaller_config::tally*;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept { return value; }
    constexpr cpp_type ConvertToCpp(abi_type value) noexcept { return value; }

    /// The leaf declaration replaces the helper's protected override with a public method.
    [[sharpcpp::expose_member(shape = "method")]]
    static int Weight(marshaller_config::tally& self) { return self.count * 3; }
};
}
