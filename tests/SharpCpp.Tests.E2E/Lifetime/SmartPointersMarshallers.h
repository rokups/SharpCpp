#pragma once

#include <SharpCpp/Marshaller.hpp>

#include "SmartPointers.hpp"

namespace SharpCpp
{
template <>
struct [[sharpcpp::managed_type(
    public_type = "RefCounted",
    managed_base_type = "global::SharpCpp.Tests.E2E.Lifetime.RefCountedNativeObject",
    release_strategy = "base"
)]] Marshaller<smart_pointers::ref_counted*>
{
    using cpp_type = smart_pointers::ref_counted*;
    using abi_type = smart_pointers::ref_counted*;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept { return value; }
    constexpr cpp_type ConvertToCpp(abi_type value) noexcept { return value; }
};

template <class TCpp, class TRefCounted>
struct [[sharpcpp::managed_type(
    public_type = "{0}",
    projection = true,
    return_ownership = "owning"
)]] Marshaller<smart_pointers::shared_ptr<TCpp, TRefCounted>>
{
    using cpp_type = smart_pointers::shared_ptr<TCpp, TRefCounted>;
    using abi_type = TCpp*;

    abi_type ConvertToAbi(cpp_type value) noexcept
    {
        value.Get()->AddRef();
        return value.Get();
    }

    cpp_type ConvertToCpp(abi_type value) noexcept { return cpp_type(value); }
};

template <class TCpp, class TRefCounted>
struct ReturnMarshaller<smart_pointers::shared_ptr<TCpp, TRefCounted>> : Marshaller<smart_pointers::shared_ptr<TCpp, TRefCounted>> {};

template <class TCpp, class TRefCounted>
struct Marshaller<const smart_pointers::shared_ptr<TCpp, TRefCounted>&>
{
    using cpp_type = smart_pointers::shared_ptr<TCpp, TRefCounted>;
    using abi_type = TCpp*;

    cpp_type ConvertToCpp(abi_type value) noexcept { return cpp_type(value); }
};

template <class TCpp, class TRefCounted>
struct [[sharpcpp::managed_type(
    public_type = "{0}",
    projection = true,
    return_ownership = "owning"
)]] Marshaller<smart_pointers::weak_ptr<TCpp, TRefCounted>>
{
    using cpp_type = smart_pointers::weak_ptr<TCpp, TRefCounted>;
    using abi_type = TCpp*;

    abi_type ConvertToAbi(cpp_type value) noexcept
    {
        value.Get()->AddRef();
        return value.Get();
    }
};

template <class TCpp, class TRefCounted>
struct ReturnMarshaller<smart_pointers::weak_ptr<TCpp, TRefCounted>> : Marshaller<smart_pointers::weak_ptr<TCpp, TRefCounted>> {};

template <class T>
struct [[sharpcpp::managed_type(
    public_type = "{0}",
    projection = true,
    return_ownership = "owning"
)]] Marshaller<smart_pointers::unique_ptr<T, smart_pointers::releaser<T>>>
{
    using cpp_type = smart_pointers::unique_ptr<T, smart_pointers::releaser<T>>;
    using abi_type = T*;

    abi_type ConvertToAbi(cpp_type& value) noexcept
    {
        T* detached = value.Detach();
        detached->AddRef();
        return detached;
    }
};

template <class T>
struct ReturnMarshaller<smart_pointers::unique_ptr<T, smart_pointers::releaser<T>>> : Marshaller<smart_pointers::unique_ptr<T, smart_pointers::releaser<T>>> {};
}
