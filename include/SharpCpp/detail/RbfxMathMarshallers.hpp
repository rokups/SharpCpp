#pragma once

#include <Urho3D/Math/Vector3.h>
#include <Urho3D/Math/Quaternion.h>

namespace SharpCpp
{

// ─── Vector3 ABI ────────────────────────────────────────────────────

struct Vector3Abi
{
    float X;
    float Y;
    float Z;
};

inline Urho3D::Vector3 ToCpp(const Vector3Abi& value)
{
    return {value.X, value.Y, value.Z};
}

inline Vector3Abi ToAbi(const Urho3D::Vector3& value)
{
    return {value.x_, value.y_, value.z_};
}

// ─── Quaternion ABI ──────────────────────────────────────────────────

struct QuaternionAbi
{
    float W;
    float X;
    float Y;
    float Z;
};

inline Urho3D::Quaternion ToCpp(const QuaternionAbi& value)
{
    return {value.W, value.X, value.Y, value.Z};
}

inline QuaternionAbi ToAbi(const Urho3D::Quaternion& value)
{
    return {value.w_, value.x_, value.y_, value.z_};
}

} // namespace SharpCpp
