#pragma once

#include <SharpCpp/Marshaller.hpp>
#include <SharpCpp/detail/RbfxMathMarshallers.hpp>

namespace SharpCpp
{

// ═════════════════════════════════════════════════════════════════════
// Urho3D::Vector3
// ═════════════════════════════════════════════════════════════════════
//
// Parameter: identity marshalling (C# Vector3 is layout-compatible with
// Urho3D::Vector3: both are [StructLayout(Sequential)] with three floats).
//
// Return: uses Vector3Abi (separate ABI struct) with a C# custom
// marshaller for the Vector3Abi → Vector3 conversion.
// ═════════════════════════════════════════════════════════════════════

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.Vector3",
    marshaller_type = "global::Urho3D.Math.Vector3",
    suppress_emission = true
)]]
Marshaller<Urho3D::Vector3>
{
    using cpp_type = Urho3D::Vector3;
    using abi_type = Urho3D::Vector3;

    constexpr abi_type ConvertToAbi(const cpp_type& value) const noexcept
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
    public_type = "global::Urho3D.Math.Vector3",
    marshaller_type = "global::Urho3D.Interop.Vector3Abi",
    managed_marshaller = "global::Urho3D.Interop.Vector3ReturnMarshaller"
)]]
ReturnMarshaller<Urho3D::Vector3>
{
    using cpp_type = Urho3D::Vector3;
    using abi_type = Vector3Abi;

    abi_type ConvertToAbi(const cpp_type& value) const
    {
        return ToAbi(value);
    }

    cpp_type ConvertToCpp(const abi_type& value) const
    {
        return ToCpp(value);
    }
};

} // namespace SharpCpp
