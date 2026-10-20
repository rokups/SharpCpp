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
struct [[sharpcpp::managed_type(public_type = "long", marshaller_type = "long", suppress_emission = true)]]
Marshaller<marshaller_config::twin<T, T>>
{
    using cpp_type = marshaller_config::twin<T, T>;
    using abi_type = long long;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept { return static_cast<long long>(value.first) * 1000 + value.second; }
    constexpr cpp_type ConvertToCpp(abi_type value) noexcept { return {static_cast<T>(value / 1000), static_cast<T>(value % 1000)}; }
};

template <class T>
struct [[sharpcpp::managed_type(public_type = "long", marshaller_type = "long", suppress_emission = true)]]
Marshaller<marshaller_config::twin<const T, T>>
{
    using cpp_type = marshaller_config::twin<const T, T>;
    using abi_type = long long;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept { return static_cast<long long>(value.first) * 1000 + value.second; }
    constexpr cpp_type ConvertToCpp(abi_type value) noexcept { return {static_cast<T>(value / 1000), static_cast<T>(value % 1000)}; }
};

template <class T, int N>
struct [[sharpcpp::managed_type(public_type = "long", marshaller_type = "long", suppress_emission = true)]]
Marshaller<marshaller_config::triple<T, N, N>>
{
    using cpp_type = marshaller_config::triple<T, N, N>;
    using abi_type = long long;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept { return static_cast<long long>(value.value) * 1000 + N; }
    constexpr cpp_type ConvertToCpp(abi_type value) noexcept { return {static_cast<T>(value / 1000)}; }
};

template <class T, int N>
struct [[sharpcpp::managed_type(public_type = "int", marshaller_type = "int", suppress_emission = true)]]
Marshaller<marshaller_config::box<T, N>>
{
    using cpp_type = marshaller_config::box<T, N>;
    using abi_type = int;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept { return static_cast<int>(value.value) + N; }
    constexpr cpp_type ConvertToCpp(abi_type value) noexcept { return {static_cast<T>(value - N)}; }
};

template <>
struct [[sharpcpp::managed_type(
    managed_base_type = "global::SharpCpp.Support.StringBase",
    public_type = "MarshallerConfig.Rope",
    managed_namespace = "MarshallerConfig"
)]] Marshaller<marshaller_config::rope*>
{
    using cpp_type = marshaller_config::rope*;
    using abi_type = marshaller_config::rope*;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept { return value; }
    constexpr cpp_type ConvertToCpp(abi_type value) noexcept { return value; }

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false)]]
    static unsigned long long GetCountCore(const marshaller_config::rope& self) { return self.size(); }

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false)]]
    static unsigned char GetAtCore(const marshaller_config::rope& self, unsigned long long index) { return self.at(index); }

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false)]]
    static void AddCore(marshaller_config::rope& self, unsigned char value) { self.push_back(static_cast<char>(value)); }

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false)]]
    static void ClearCore(marshaller_config::rope& self) { self.clear(); }
};

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
