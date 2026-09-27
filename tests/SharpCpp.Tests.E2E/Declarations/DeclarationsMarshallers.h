#pragma once

#include <SharpCpp/Marshaller.hpp>

#include "Declarations.hpp"

namespace SharpCpp
{
template <>
struct [[sharpcpp::managed_type(
    public_type = "BreadthPayload",
    marshaller_type = "nint",
    suppress_emission = true
)]] Marshaller<parser_breadth::breadth_payload_projection>
{
    using cpp_type = parser_breadth::breadth_payload_projection;
    using abi_type = parser_breadth::breadth_payload*;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept { return value.value; }
    constexpr cpp_type ConvertToCpp(abi_type value) noexcept { return {value}; }
};

template <>
struct ReturnMarshaller<parser_breadth::breadth_payload_projection>
    : Marshaller<parser_breadth::breadth_payload_projection>
{
};

template <>
struct [[sharpcpp::managed_type(
    public_type = "BreadthPayload?",
    marshaller_type = "nint",
    suppress_emission = true
)]] Marshaller<parser_breadth::breadth_nullable_payload_projection>
{
    using cpp_type = parser_breadth::breadth_nullable_payload_projection;
    using abi_type = parser_breadth::breadth_payload*;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept { return value.value; }
    constexpr cpp_type ConvertToCpp(abi_type value) noexcept { return {value}; }
};

template <>
struct ReturnMarshaller<parser_breadth::breadth_nullable_payload_projection>
    : Marshaller<parser_breadth::breadth_nullable_payload_projection>
{
};

template <>
struct [[sharpcpp::managed_type(
    public_type = "BreadthMode",
    marshaller_type = "global::Parser_breadth.BreadthMode",
    suppress_emission = true
)]] Marshaller<parser_breadth::breadth_mode_projection>
{
    using cpp_type = parser_breadth::breadth_mode_projection;
    using abi_type = parser_breadth::breadth_mode;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept { return value.value; }
    constexpr cpp_type ConvertToCpp(abi_type value) noexcept { return {value}; }
};

template <>
struct ReturnMarshaller<parser_breadth::breadth_mode_projection>
    : Marshaller<parser_breadth::breadth_mode_projection>
{
};
}
