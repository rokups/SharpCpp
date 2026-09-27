#pragma once

#include <SharpCpp/Marshaller.hpp>

#include "ExposedMembers.hpp"

namespace SharpCpp
{
template <>
struct Marshaller<exposed_members::listener*>
{
    using cpp_type = exposed_members::listener*;
    using abi_type = exposed_members::listener*;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept { return value; }
    constexpr cpp_type ConvertToCpp(abi_type value) noexcept { return value; }

    /// An exposed member returning `void*` is a raw pointer.
    [[sharpcpp::expose_member]]
    static void* GetHandle(exposed_members::listener& self) { return &self.id; }
};

template <>
struct [[sharpcpp::managed_type(
    public_type = "Constructed",
    managed_base_type = "global::SharpCpp.Tests.E2E.Marshallers.ExposedNativeObject",
    release_strategy = "base"
)]] Marshaller<exposed_members::constructed*>
{
    using cpp_type = exposed_members::constructed*;
    using abi_type = exposed_members::constructed*;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept { return value; }
    constexpr cpp_type ConvertToCpp(abi_type value) noexcept { return value; }

    [[sharpcpp::expose_member]]
    static int Twice(exposed_members::constructed& self) { return self.value * 2; }
};
}
