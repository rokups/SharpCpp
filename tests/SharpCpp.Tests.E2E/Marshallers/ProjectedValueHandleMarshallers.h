#pragma once

#include <SharpCpp/Marshaller.hpp>

#include "ProjectedValueHandle.hpp"

namespace SharpCpp
{

struct pv_vec_abi
{
    float x;
    float y;
    float z;
};

// A pure value projection: suppress_emission with a namespaced public_type and no wrapper
// infrastructure, round-tripping as a blittable managed struct (mirrors Float3/Mat44).
template <>
struct [[sharpcpp::managed_type(
    public_type = "global::SharpCpp.Tests.E2E.Marshallers.ProjectedValueVec",
    suppress_emission = true
)]] Marshaller<pv_ns::pv_vec>
{
    using cpp_type = pv_ns::pv_vec;
    using abi_type = pv_vec_abi;

    abi_type ConvertToAbi(cpp_type value) noexcept { return {value.x, value.y, value.z}; }
    cpp_type ConvertToCpp(abi_type value) noexcept { return {value.x, value.y, value.z}; }
};

template <>
struct Marshaller<const pv_ns::pv_vec&> : ProjectedValueConstReferenceMarshallerBase<pv_ns::pv_vec> {};

template <>
struct Marshaller<pv_ns::pv_vec&> : ProjectedValueReferenceMarshallerBase<pv_ns::pv_vec> {};

}
