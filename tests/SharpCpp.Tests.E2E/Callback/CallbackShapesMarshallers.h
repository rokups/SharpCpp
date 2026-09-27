#pragma once

#include <SharpCpp/Marshaller.hpp>

#include "CallbackShapes.hpp"

namespace SharpCpp
{
template <>
struct [[sharpcpp::managed_type(
    public_type = "global::SharpCpp.Tests.E2E.Callback.CallbackPoint2",
    marshaller_type = "global::SharpCpp.Tests.E2E.Callback.CallbackPoint2",
    suppress_emission = true
)]] Marshaller<callback_shapes::point2>
{
    using cpp_type = callback_shapes::point2;
    using abi_type = callback_shapes::point2;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept { return value; }
    constexpr cpp_type ConvertToCpp(abi_type value) noexcept { return value; }
};

template <>
struct Marshaller<const callback_shapes::point2&>
{
    using cpp_type = const callback_shapes::point2&;
    using abi_type = const callback_shapes::point2*;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept { return &value; }
    constexpr cpp_type ConvertToCpp(abi_type value) noexcept { return *value; }
};
}
