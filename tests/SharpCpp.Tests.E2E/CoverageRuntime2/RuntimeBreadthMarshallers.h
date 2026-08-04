#pragma once

#include <SharpCpp/Marshaller.hpp>

#include "RuntimeBreadth.hpp"

namespace SharpCpp
{
template <>
struct [[sharpcpp::managed_type(
    public_type = "RuntimeBreadthCacheValue",
    managed_base_type = "global::SharpCpp.Tests.E2E.CoverageRuntime2.RuntimeBreadthManagedObject",
    release_strategy = "base",
    owned_handle = true
)]] Marshaller<runtime_breadth_cache_value>
    : OwnedHandleMarshallerBase<runtime_breadth_cache_value>
{
};

template <>
struct Marshaller<runtime_breadth_cache_value*>
    : OwnedHandlePointerMarshallerBase<runtime_breadth_cache_value>
{
};

template <>
struct Marshaller<const runtime_breadth_cache_value*>
    : OwnedHandleConstPointerMarshallerBase<runtime_breadth_cache_value>
{
};

template <>
struct [[sharpcpp::managed_type(
    public_type = "RuntimeBreadthLifecycle",
    managed_base_type = "global::SharpCpp.Tests.E2E.CoverageRuntime2.RuntimeBreadthManagedObject",
    release_strategy = "base",
    owned_handle = true
)]] Marshaller<runtime_breadth_lifecycle>
    : OwnedHandleMarshallerBase<runtime_breadth_lifecycle>
{
};

template <>
struct Marshaller<runtime_breadth_lifecycle*>
    : OwnedHandlePointerMarshallerBase<runtime_breadth_lifecycle>
{
};

template <>
struct Marshaller<const runtime_breadth_lifecycle*>
    : OwnedHandleConstPointerMarshallerBase<runtime_breadth_lifecycle>
{
};

template <typename T>
struct [[sharpcpp::managed_type(
    public_type = "{0}",
    projection = true,
    return_ownership = "owning"
)]] Marshaller<runtime_breadth_owner<T>>
{
    using cpp_type = runtime_breadth_owner<T>;
    using abi_type = T*;

    abi_type ConvertToAbi(cpp_type& value) noexcept
    {
        return value.detach();
    }

    cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return cpp_type(value);
    }
};

template <typename T>
struct ReturnMarshaller<runtime_breadth_owner<T>>
    : Marshaller<runtime_breadth_owner<T>>
{
};
}
