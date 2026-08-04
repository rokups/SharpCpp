#pragma once

#include <SharpCpp/Marshaller.hpp>

#include "RuntimePolicy.hpp"

namespace SharpCpp
{
template <>
struct [[sharpcpp::managed_type(
    public_type = "RuntimePolicyBase",
    managed_base_type = "global::SharpCpp.Tests.E2E.CoverageMatrix.CoveragePolicyNativeObject",
    managed_instance_cache_factory = "global::SharpCpp.Tests.E2E.CoverageMatrix.CoveragePolicyCache.Shared",
    managed_instance_cache_type = "global::SharpCpp.Support.INativeInstanceCache",
    managed_static_interfaces = "global::SharpCpp.Tests.E2E.CoverageMatrix.ICoverageStaticTypeInfo<{managed_type}>",
    managed_static_interfaces_abstract = true,
    release_strategy = "base",
    owned_handle = true
)]] Marshaller<runtime_policy_base*>
{
    using cpp_type = runtime_policy_base*;
    using abi_type = runtime_policy_base*;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept { return value; }
    constexpr cpp_type ConvertToCpp(abi_type value) noexcept { return value; }
};

template <>
struct [[sharpcpp::managed_type(public_type = "RuntimeConfiguredValue")]]
Marshaller<runtime_rename_value*>
{
    using cpp_type = runtime_rename_value*;
    using abi_type = runtime_rename_value*;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept { return value; }
    constexpr cpp_type ConvertToCpp(abi_type value) noexcept { return value; }
};

template <>
struct [[sharpcpp::managed_type(
    public_type = "int",
    marshaller_type = "int",
    suppress_emission = true
)]] Marshaller<runtime_scalar>
{
    using cpp_type = runtime_scalar;
    using abi_type = int;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept { return value.value; }
    constexpr cpp_type ConvertToCpp(abi_type value) noexcept { return {value}; }
};

template <>
struct ReturnMarshaller<runtime_scalar> : Marshaller<runtime_scalar>
{
};

template <typename T>
struct [[sharpcpp::managed_type(
    public_type = "{0}",
    projection = true,
    return_ownership = "owning"
)]] Marshaller<runtime_cache_owner<T>>
{
    using cpp_type = runtime_cache_owner<T>;
    using abi_type = T*;

    abi_type ConvertToAbi(cpp_type& value) noexcept { return value.detach(); }
    cpp_type ConvertToCpp(abi_type value) noexcept { return cpp_type(value); }
};

template <typename T>
struct ReturnMarshaller<runtime_cache_owner<T>> : Marshaller<runtime_cache_owner<T>>
{
};
}
