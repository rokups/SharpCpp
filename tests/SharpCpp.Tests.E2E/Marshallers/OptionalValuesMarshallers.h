#pragma once

#include <SharpCpp/Marshaller.hpp>
#include <SharpCpp/detail/MarshallerSupport.hpp>

#include "OptionalValues.hpp"

namespace SharpCpp
{
template <>
struct [[sharpcpp::managed_type(
    public_type = "global::SharpCpp.Tests.E2E.Marshallers.OptionalPoint",
    suppress_emission = true
)]] Marshaller<optional_values::point>
{
    using cpp_type = optional_values::point;
    using abi_type = optional_values::point;

    abi_type ConvertToAbi(cpp_type value) noexcept { return value; }
    cpp_type ConvertToCpp(abi_type value) noexcept { return value; }
};

/// The ABI struct of optional_values::vec. The order of the members is y first.
struct vec_abi
{
    float y;
    float x;
};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::SharpCpp.Tests.E2E.Marshallers.OptionalVec",
    marshaller_type = "global::SharpCpp.Tests.E2E.Marshallers.OptionalVec",
    suppress_emission = true
)]] Marshaller<optional_values::vec>
{
    using cpp_type = optional_values::vec;
    using abi_type = vec_abi;

    abi_type ConvertToAbi(const cpp_type& value) noexcept { return {value.y(), value.x()}; }
    cpp_type ConvertToCpp(abi_type value) noexcept { return {value.x, value.y}; }
};

/// The ABI struct of optional_values::fixed_pair. The class has const members, so the ABI struct is
/// a separate plain struct.
struct fixed_pair_abi
{
    int first;
    int second;
};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::SharpCpp.Tests.E2E.Marshallers.OptionalFixedPair",
    marshaller_type = "global::SharpCpp.Tests.E2E.Marshallers.OptionalFixedPair",
    suppress_emission = true
)]] Marshaller<optional_values::fixed_pair>
{
    using cpp_type = optional_values::fixed_pair;
    using abi_type = fixed_pair_abi;

    abi_type ConvertToAbi(const cpp_type& value) noexcept { return {value.first, value.second}; }
    cpp_type ConvertToCpp(abi_type value) noexcept { return {value.first, value.second}; }
};

/// The loose_optional template has the same handle projection as std::optional.
template <typename T>
    requires is_optional_handle_abi_v<T>
struct [[sharpcpp::managed_type(
    public_type = "{0}?",
    projection = true
)]]
Marshaller<optional_values::loose_optional<T>> : OptionalHandleMarshallerBase<optional_values::loose_optional<T>, T> {};

template <typename T>
    requires is_optional_handle_abi_v<T>
struct [[sharpcpp::managed_type(return_ownership = "owning")]]
ReturnMarshaller<optional_values::loose_optional<T>>
    : OptionalHandleReturnMarshallerBase<optional_values::loose_optional<T>, T> {};

static_assert(is_optional_value_abi_v<int>);
static_assert(is_optional_value_abi_v<optional_values::point>);
static_assert(!is_optional_handle_abi_v<optional_values::point>);
static_assert(is_optional_handle_abi_v<optional_values::item>);
static_assert(!is_optional_value_abi_v<optional_values::item>);
static_assert(detail::is_emplace_only_optional_v<std::optional<optional_values::pinned>>);
static_assert(!detail::is_emplace_only_optional_v<std::optional<int>>);
static_assert(!detail::is_emplace_only_optional_v<int>);
static_assert(is_optional_value_abi_v<optional_values::mode>);
static_assert(is_optional_value_abi_v<optional_values::vec>);
static_assert(sizeof(Marshaller<std::optional<optional_values::mode>>::abi_type) == optional_values::optional_mode_abi_size());
static_assert(optional_values::optional_mode_abi_size() == 2);
static_assert(is_optional_value_abi_v<optional_values::fixed_pair>);
static_assert(std::is_trivially_copyable_v<optional_values::fixed_pair>);
static_assert(!std::is_copy_assignable_v<std::optional<optional_values::fixed_pair>>);
static_assert(detail::is_emplace_only_optional_v<optional_values::loose_optional<optional_values::pinned>>);

static_assert(sizeof(Marshaller<std::optional<int>>::abi_type) == optional_values::optional_int_abi_size());
static_assert(sizeof(Marshaller<std::optional<double>>::abi_type) == optional_values::optional_double_abi_size());
static_assert(sizeof(Marshaller<std::optional<optional_values::point>>::abi_type) == optional_values::optional_point_abi_size());
}
