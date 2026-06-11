#pragma once

#include <cstddef>
#include <string>
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

template <>
struct [[sharpcpp::managed_type(public_type = "global::System.ReadOnlySpan<byte>", undefaultable = true)]] Marshaller<const char*>
{
    using cpp_type = const char*;
    using abi_type = const char*;

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
struct [[sharpcpp::managed_type(public_type = "string")]] Marshaller<const char**>
{
    using cpp_type = const char**;
    using abi_type = const char**;

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
struct [[sharpcpp::managed_type(public_type = "string")]] Marshaller<char**>
{
    using cpp_type = char**;
    using abi_type = char**;

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
struct [[sharpcpp::managed_type(
    managed_base_type = "global::SharpCpp.Support.StringBase",
    public_type = "Std.String",
    managed_namespace = "Std"
)]]
Marshaller<std::basic_string<char> >
{
    using cpp_type = std::string;
    using abi_type = std::string*;

    constexpr abi_type ConvertToAbi(cpp_type& value) const noexcept
    {
        return &value;
    }

    constexpr cpp_type& ConvertToCpp(abi_type value) const noexcept
    {
        return *value;
    }

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false)]]
    static std::size_t GetCountCore(const std::string& self)
    {
        return self.size();
    }

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false)]]
    static void AddCore(std::string& self, unsigned char value)
    {
        self.push_back(static_cast<char>(value));
    }

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false)]]
    static void ClearCore(std::string& self)
    {
        self.clear();
    }
};

template <>
struct Marshaller<const std::string&>
{
    using cpp_type = const std::string&;
    using abi_type = std::string*;

    constexpr abi_type ConvertToAbi(cpp_type value) const noexcept
    {
        return const_cast<std::string*>(&value);
    }

    constexpr cpp_type ConvertToCpp(abi_type value) const noexcept
    {
        return *value;
    }
};

template <>
struct Marshaller<const std::string*>
{
    using cpp_type = const std::string*;
    using abi_type = std::string*;

    constexpr abi_type ConvertToAbi(cpp_type value) const noexcept
    {
        return const_cast<std::string*>(value);
    }

    constexpr cpp_type ConvertToCpp(abi_type value) const noexcept
    {
        return value;
    }
};

template <>
struct Marshaller<std::string*>
{
    using cpp_type = std::string*;
    using abi_type = std::string*;

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
using ParameterMarshaller = Marshaller<T>;

template <class T>
struct ReturnMarshaller : Marshaller<T> {};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::System.ReadOnlySpan<byte>",
    managed_marshaller = "global::SharpCpp.Support.Utf8SpanReturnMarshaller"
)]] ReturnMarshaller<const char*> : Marshaller<const char*> {};

template <class T>
using ret_t = typename ReturnMarshaller<T>::abi_type;
}