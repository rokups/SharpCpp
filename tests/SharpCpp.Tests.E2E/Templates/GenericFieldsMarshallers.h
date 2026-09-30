#pragma once

#include <SharpCpp/Marshaller.hpp>

#include "GenericFields.hpp"

namespace SharpCpp
{
// Mirrors Marshaller<Urho3D::Quaternion>: the public and marshaller types are the same
// blittable managed struct and the ABI type is the native record itself.
template <>
struct [[sharpcpp::managed_type(
    public_type = "global::SharpCpp.Tests.E2E.Templates.SpinValue",
    marshaller_type = "global::SharpCpp.Tests.E2E.Templates.SpinValue",
    suppress_emission = true
)]]
Marshaller<generic_fields::spin>
{
    using cpp_type = generic_fields::spin;
    using abi_type = generic_fields::spin;
    abi_type ConvertToAbi(const cpp_type& v) noexcept { return v; }
    cpp_type ConvertToCpp(abi_type v) noexcept { return v; }
};
}
