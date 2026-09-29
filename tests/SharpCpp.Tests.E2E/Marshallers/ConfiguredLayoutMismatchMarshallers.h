#pragma once

#include <SharpCpp/Marshaller.hpp>

#include "ConfiguredLayoutMismatch.hpp"

namespace SharpCpp
{

struct configured_wide_vector3_abi
{
    float x;
    float y;
    float z;
};

inline configured_wide_vector3_abi ToConfiguredWideVector3Abi(const configured_wide_vector3& value) noexcept
{
    return {value.v[0], value.v[1], value.v[2]};
}

inline configured_wide_vector3 ToConfiguredWideVector3Cpp(const configured_wide_vector3_abi& value) noexcept
{
    return make_configured_wide_vector3(value.x, value.y, value.z);
}

// A projected value type whose native layout (16-byte, SIMD-aligned) genuinely differs from its
// abi_type layout (12-byte, tightly packed) -- mirrors JPH::Vec3 / JoltVector3Abi, the case the
// generic Marshaller<T*>/Marshaller<const T*>/Marshaller<T&>/Marshaller<const T&> fallbacks in
// Marshaller.hpp now static_assert against. suppress_emission = true keeps this off the
// generated managed surface, same as the real Jolt projection.
template <>
struct [[sharpcpp::managed_type(
    public_type = "global::SharpCpp.Tests.E2E.Marshallers.ConfiguredWideVector3",
    suppress_emission = true
)]] Marshaller<configured_wide_vector3>
{
    using cpp_type = configured_wide_vector3;
    using abi_type = configured_wide_vector3_abi;

    abi_type ConvertToAbi(cpp_type value) noexcept { return ToConfiguredWideVector3Abi(value); }
    cpp_type ConvertToCpp(abi_type value) noexcept { return ToConfiguredWideVector3Cpp(value); }
};

template <>
struct Marshaller<const configured_wide_vector3&>
    : ProjectedValueConstReferenceMarshallerBase<configured_wide_vector3> {};

template <>
struct Marshaller<configured_wide_vector3&>
    : ProjectedValueReferenceMarshallerBase<configured_wide_vector3> {};

// by_reference = true (mirrors Marshaller<const char**>'s "string" projection) tells the
// generator this pointer crosses by address rather than by value, so the parameter renders as
// `in ConfiguredWideVector3` and P/Invoke marshals it through the pointer ABI -- matching
// arg_t<const configured_wide_vector3*>, which is what ProjectedValueConstPointerMarshallerBase's
// abi_type actually is. Without this attribute the parser has no signal that a plain (non-array,
// non-handle) pointer parameter should stay pointer-shaped and instead treats it like a by-value
// parameter, which would silently mismatch the native ABI.
template <>
struct [[sharpcpp::managed_type(
    public_type = "global::SharpCpp.Tests.E2E.Marshallers.ConfiguredWideVector3",
    by_reference = true
)]] Marshaller<const configured_wide_vector3*>
    : ProjectedValueConstPointerMarshallerBase<configured_wide_vector3> {};

}
