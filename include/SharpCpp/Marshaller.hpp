#pragma once

#include <type_traits>

namespace SharpCpp
{
template <class T>
struct Marshaller
{
    using cpp_type = T;
    using abi_type = T;

    constexpr abi_type ConvertToAbi(cpp_type value) const noexcept
    {
        return value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) const noexcept
    {
        return value;
    }
};

template <>
struct Marshaller<void>
{
    using cpp_type = void;
    using abi_type = void;
};

template <>
struct [[sharpcpp::managed_type(public_type = "global::System.IntPtr")]] Marshaller<void*>
{
    using cpp_type = void*;
    using abi_type = void*;

    constexpr abi_type ConvertToAbi(cpp_type value) const noexcept
    {
        return value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) const noexcept
    {
        return value;
    }
};

template <>
struct [[sharpcpp::managed_type(public_type = "global::System.IntPtr")]] Marshaller<const void*>
{
    using cpp_type = const void*;
    using abi_type = const void*;

    constexpr abi_type ConvertToAbi(cpp_type value) const noexcept
    {
        return value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) const noexcept
    {
        return value;
    }
};

template <class T>
struct Marshaller<T&>
{
    using cpp_type = T&;
    using abi_type = typename Marshaller<T>::abi_type*;

    constexpr abi_type ConvertToAbi(cpp_type value) const noexcept
    {
        return &value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) const noexcept
    {
        return *value;
    }
};

template <class T>
struct Marshaller<const T&>
{
    using cpp_type = const T&;
    using abi_type = const typename Marshaller<T>::abi_type*;

    constexpr abi_type ConvertToAbi(cpp_type value) const noexcept
    {
        return &value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) const noexcept
    {
        return *value;
    }
};

template <class T>
struct Marshaller<T*>
{
    using cpp_type = T*;
    using abi_type = typename Marshaller<T>::abi_type*;

    constexpr abi_type ConvertToAbi(cpp_type value) const noexcept
    {
        return value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) const noexcept
    {
        return value;
    }

    constexpr cpp_type ConvertToCpp(void* value) const noexcept
    {
        return static_cast<cpp_type>(value);
    }
};

template <class T>
struct Marshaller<const T*>
{
    using cpp_type = const T*;
    using abi_type = const typename Marshaller<T>::abi_type*;

    constexpr abi_type ConvertToAbi(cpp_type value) const noexcept
    {
        return value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) const noexcept
    {
        return value;
    }
};

template <class T>
using arg_t = typename Marshaller<T>::abi_type;

template <class T>
using ret_t = typename Marshaller<T>::abi_type;

template <class T>
using ParameterMarshaller = Marshaller<T>;

template <class T>
using ReturnMarshaller = Marshaller<T>;
}