#pragma once

#include <SharpCpp/Marshaller.hpp>

#include "ConfiguredFixedArray.hpp"

namespace SharpCpp
{

struct configured_vector3_abi
{
    float x;
    float y;
    float z;
};

inline configured_vector3_abi ToConfiguredVector3Abi(const configured_vector3& value) noexcept
{
    return {value.x, value.y, value.z};
}

inline configured_vector3 ToConfiguredVector3Cpp(const configured_vector3_abi& value) noexcept
{
    return {value.x, value.y, value.z};
}

// A projected value type with a namespaced public_type (mirrors JPH::Float3 ->
// global::RedBolt.Math.Float3), used directly as a fixed-size C array field element
// (mirrors JPH::Triangle::mV). suppress_emission = true and a POD abi_type keep this
// on the same shape as the real Jolt binding that exposed the fixed-array codegen bug.
template <>
struct [[sharpcpp::managed_type(
    public_type = "global::SharpCpp.Tests.E2E.Marshallers.ConfiguredVector3",
    suppress_emission = true
)]] Marshaller<configured_vector3>
{
    using cpp_type = configured_vector3;
    using abi_type = configured_vector3_abi;

    abi_type ConvertToAbi(cpp_type value) noexcept { return ToConfiguredVector3Abi(value); }
    cpp_type ConvertToCpp(abi_type value) noexcept { return ToConfiguredVector3Cpp(value); }
};

template <>
struct Marshaller<const configured_vector3&> : ProjectedValueConstReferenceMarshallerBase<configured_vector3> {};

template <>
struct Marshaller<configured_vector3&> : ProjectedValueReferenceMarshallerBase<configured_vector3> {};

}
