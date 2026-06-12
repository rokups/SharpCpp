#pragma once

#include <SharpCpp/Marshaller.hpp>
#include "RbfxMathMarshallers.hpp"

#include <EASTL/list.h>
#include <EASTL/map.h>
#include <EASTL/string.h>
#include <EASTL/unordered_map.h>
#include <EASTL/vector.h>

namespace SharpCpp
{

template <>
struct [[sharpcpp::managed_type(
    managed_base_type = "global::SharpCpp.Support.StringBase",
    public_type = "Ea.String",
    managed_namespace = "Ea"
)]]
Marshaller<eastl::basic_string<char>> : OwnedStringMarshallerBase<eastl::basic_string<char>> {};

template <>
struct Marshaller<eastl::basic_string<char>&> : OwnedStringReferenceMarshallerBase<eastl::basic_string<char>> {};

template <>
struct Marshaller<const eastl::basic_string<char>&> : OwnedStringConstReferenceMarshallerBase<eastl::basic_string<char>> {};

template <>
struct Marshaller<const eastl::basic_string<char>*> : OwnedStringConstPointerMarshallerBase<eastl::basic_string<char>> {};

template <>
struct Marshaller<eastl::basic_string<char>*> : OwnedStringPointerMarshallerBase<eastl::basic_string<char>> {};

template <>
struct ReturnMarshaller<eastl::basic_string<char>> : OwnedStringReturnMarshallerBase<eastl::basic_string<char>> {};

template <typename T, typename TAllocator>
struct [[sharpcpp::managed_type(
    public_type = "Ea.Vector<{0}>",
    managed_base_type = "global::SharpCpp.Support.VectorBase<{0}>",
    managed_namespace = "Ea"
)]]
Marshaller<eastl::vector<T, TAllocator>> : SequenceCollectionMarshallerBase<eastl::vector<T, TAllocator>, T> {};

template <typename T, typename TAllocator>
struct Marshaller<eastl::vector<T, TAllocator>&> : OwnedHandleReferenceMarshallerBase<eastl::vector<T, TAllocator>> {};

template <typename T, typename TAllocator>
struct Marshaller<const eastl::vector<T, TAllocator>&> : OwnedHandleConstReferenceMarshallerBase<eastl::vector<T, TAllocator>> {};

template <typename T, typename TAllocator>
struct Marshaller<eastl::vector<T, TAllocator>*> : OwnedHandlePointerMarshallerBase<eastl::vector<T, TAllocator>> {};

template <typename T, typename TAllocator>
struct Marshaller<const eastl::vector<T, TAllocator>*> : OwnedHandleConstPointerMarshallerBase<eastl::vector<T, TAllocator>> {};

template <typename T, typename TAllocator>
struct ReturnMarshaller<eastl::vector<T, TAllocator>> : OwnedHandleReturnMarshallerBase<eastl::vector<T, TAllocator>> {};

template <typename T, typename TAllocator>
struct [[sharpcpp::managed_type(
    public_type = "Ea.List<{0}>",
    managed_base_type = "global::SharpCpp.Support.ListBase<{0}>",
    managed_namespace = "Ea"
)]]
Marshaller<eastl::list<T, TAllocator>> : ListCollectionMarshallerBase<eastl::list<T, TAllocator>, T> {};

template <typename T, typename TAllocator>
struct Marshaller<eastl::list<T, TAllocator>&> : OwnedHandleReferenceMarshallerBase<eastl::list<T, TAllocator>> {};

template <typename T, typename TAllocator>
struct Marshaller<const eastl::list<T, TAllocator>&> : OwnedHandleConstReferenceMarshallerBase<eastl::list<T, TAllocator>> {};

template <typename T, typename TAllocator>
struct Marshaller<eastl::list<T, TAllocator>*> : OwnedHandlePointerMarshallerBase<eastl::list<T, TAllocator>> {};

template <typename T, typename TAllocator>
struct Marshaller<const eastl::list<T, TAllocator>*> : OwnedHandleConstPointerMarshallerBase<eastl::list<T, TAllocator>> {};

template <typename T, typename TAllocator>
struct ReturnMarshaller<eastl::list<T, TAllocator>> : OwnedHandleReturnMarshallerBase<eastl::list<T, TAllocator>> {};

template <typename TKey, typename TValue, typename TCompare, typename TAllocator>
struct [[sharpcpp::managed_type(
    public_type = "Ea.Map<{0}, {1}>",
    managed_base_type = "global::SharpCpp.Support.MapBase<{0}, {1}>",
    managed_base_type_constraints = "where {0} : notnull",
    managed_namespace = "Ea"
)]]
Marshaller<eastl::map<TKey, TValue, TCompare, TAllocator>> : MapCollectionMarshallerBase<eastl::map<TKey, TValue, TCompare, TAllocator>, TKey, TValue> {};

template <typename TKey, typename TValue, typename TCompare, typename TAllocator>
struct Marshaller<eastl::map<TKey, TValue, TCompare, TAllocator>&> : OwnedHandleReferenceMarshallerBase<eastl::map<TKey, TValue, TCompare, TAllocator>> {};

template <typename TKey, typename TValue, typename TCompare, typename TAllocator>
struct Marshaller<const eastl::map<TKey, TValue, TCompare, TAllocator>&> : OwnedHandleConstReferenceMarshallerBase<eastl::map<TKey, TValue, TCompare, TAllocator>> {};

template <typename TKey, typename TValue, typename TCompare, typename TAllocator>
struct Marshaller<eastl::map<TKey, TValue, TCompare, TAllocator>*> : OwnedHandlePointerMarshallerBase<eastl::map<TKey, TValue, TCompare, TAllocator>> {};

template <typename TKey, typename TValue, typename TCompare, typename TAllocator>
struct Marshaller<const eastl::map<TKey, TValue, TCompare, TAllocator>*> : OwnedHandleConstPointerMarshallerBase<eastl::map<TKey, TValue, TCompare, TAllocator>> {};

template <typename TKey, typename TValue, typename TCompare, typename TAllocator>
struct ReturnMarshaller<eastl::map<TKey, TValue, TCompare, TAllocator>> : OwnedHandleReturnMarshallerBase<eastl::map<TKey, TValue, TCompare, TAllocator>> {};

template <typename TKey, typename TValue, typename THash, typename TPredicate, typename TAllocator, bool CacheHashCode>
struct [[sharpcpp::managed_type(
    public_type = "Ea.UnorderedMap<{0}, {1}>",
    managed_base_type = "global::SharpCpp.Support.UnorderedMapBase<{0}, {1}>",
    managed_base_type_constraints = "where {0} : notnull",
    managed_namespace = "Ea"
)]]
Marshaller<eastl::unordered_map<TKey, TValue, THash, TPredicate, TAllocator, CacheHashCode>> : MapCollectionMarshallerBase<eastl::unordered_map<TKey, TValue, THash, TPredicate, TAllocator, CacheHashCode>, TKey, TValue> {};

template <typename TKey, typename TValue, typename THash, typename TPredicate, typename TAllocator, bool CacheHashCode>
struct Marshaller<eastl::unordered_map<TKey, TValue, THash, TPredicate, TAllocator, CacheHashCode>&> : OwnedHandleReferenceMarshallerBase<eastl::unordered_map<TKey, TValue, THash, TPredicate, TAllocator, CacheHashCode>> {};

template <typename TKey, typename TValue, typename THash, typename TPredicate, typename TAllocator, bool CacheHashCode>
struct Marshaller<const eastl::unordered_map<TKey, TValue, THash, TPredicate, TAllocator, CacheHashCode>&> : OwnedHandleConstReferenceMarshallerBase<eastl::unordered_map<TKey, TValue, THash, TPredicate, TAllocator, CacheHashCode>> {};

template <typename TKey, typename TValue, typename THash, typename TPredicate, typename TAllocator, bool CacheHashCode>
struct Marshaller<eastl::unordered_map<TKey, TValue, THash, TPredicate, TAllocator, CacheHashCode>*> : OwnedHandlePointerMarshallerBase<eastl::unordered_map<TKey, TValue, THash, TPredicate, TAllocator, CacheHashCode>> {};

template <typename TKey, typename TValue, typename THash, typename TPredicate, typename TAllocator, bool CacheHashCode>
struct Marshaller<const eastl::unordered_map<TKey, TValue, THash, TPredicate, TAllocator, CacheHashCode>*> : OwnedHandleConstPointerMarshallerBase<eastl::unordered_map<TKey, TValue, THash, TPredicate, TAllocator, CacheHashCode>> {};

template <typename TKey, typename TValue, typename THash, typename TPredicate, typename TAllocator, bool CacheHashCode>
struct ReturnMarshaller<eastl::unordered_map<TKey, TValue, THash, TPredicate, TAllocator, CacheHashCode>> : OwnedHandleReturnMarshallerBase<eastl::unordered_map<TKey, TValue, THash, TPredicate, TAllocator, CacheHashCode>> {};

// ═════════════════════════════════════════════════════════════════════
// Parameter ABI: pointer-to-original-type via Marshaller<const T&> specialization.
//   The generic Marshaller<const T&> dereferences through Marshaller<T>::abi_type,
//   which would double-indirect if Marshaller<T>::abi_type is also a pointer.
//   Keeping Marshaller<T>::abi_type = T (value) avoids this.
// Return ABI: ABI struct via ReturnMarshaller<T> specialization.
// ═════════════════════════════════════════════════════════════════════

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.Vector2",
    marshaller_type = "global::Urho3D.Math.Vector2",
    suppress_emission = true
)]]
Marshaller<Urho3D::Vector2>
{
    using cpp_type = Urho3D::Vector2;
    using abi_type = Urho3D::Vector2;
    abi_type ConvertToAbi(const cpp_type& v) const noexcept { return v; }
    cpp_type ConvertToCpp(abi_type v) const noexcept { return v; }
};

template <>
struct Marshaller<const Urho3D::Vector2&>
{
    using cpp_type = const Urho3D::Vector2&;
    using abi_type = const Urho3D::Vector2*;
    abi_type ConvertToAbi(cpp_type v) const noexcept { return &v; }
    cpp_type ConvertToCpp(abi_type v) const noexcept { return *v; }
};

template <>
struct ReturnMarshaller<Urho3D::Vector2>
{
    using cpp_type = Urho3D::Vector2;
    using abi_type = Vector2Abi;
    abi_type ConvertToAbi(const cpp_type& v) const { return ToAbi(v); }
    cpp_type ConvertToCpp(const abi_type& v) const { return ToCpp(v); }
};

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
    abi_type ConvertToAbi(const cpp_type& v) const noexcept { return v; }
    cpp_type ConvertToCpp(abi_type v) const noexcept { return v; }
};

template <>
struct Marshaller<const Urho3D::Vector3&>
{
    using cpp_type = const Urho3D::Vector3&;
    using abi_type = const Urho3D::Vector3*;
    abi_type ConvertToAbi(cpp_type v) const noexcept { return &v; }
    cpp_type ConvertToCpp(abi_type v) const noexcept { return *v; }
};

template <>
struct ReturnMarshaller<Urho3D::Vector3>
{
    using cpp_type = Urho3D::Vector3;
    using abi_type = Vector3Abi;
    abi_type ConvertToAbi(const cpp_type& v) const { return ToAbi(v); }
    cpp_type ConvertToCpp(const abi_type& v) const { return ToCpp(v); }
};

// ═════════════════════════════════════════════════════════════════════

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.Vector4",
    marshaller_type = "global::Urho3D.Math.Vector4",
    suppress_emission = true
)]]
Marshaller<Urho3D::Vector4>
{
    using cpp_type = Urho3D::Vector4;
    using abi_type = Urho3D::Vector4;
    abi_type ConvertToAbi(const cpp_type& v) const noexcept { return v; }
    cpp_type ConvertToCpp(abi_type v) const noexcept { return v; }
};

template <>
struct Marshaller<const Urho3D::Vector4&>
{
    using cpp_type = const Urho3D::Vector4&;
    using abi_type = const Urho3D::Vector4*;
    abi_type ConvertToAbi(cpp_type v) const noexcept { return &v; }
    cpp_type ConvertToCpp(abi_type v) const noexcept { return *v; }
};

template <>
struct ReturnMarshaller<Urho3D::Vector4>
{
    using cpp_type = Urho3D::Vector4;
    using abi_type = Vector4Abi;
    abi_type ConvertToAbi(const cpp_type& v) const { return ToAbi(v); }
    cpp_type ConvertToCpp(const abi_type& v) const { return ToCpp(v); }
};

// ═════════════════════════════════════════════════════════════════════

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.IntVector2",
    marshaller_type = "global::Urho3D.Math.IntVector2",
    suppress_emission = true
)]]
Marshaller<Urho3D::IntVector2>
{
    using cpp_type = Urho3D::IntVector2;
    using abi_type = Urho3D::IntVector2;
    abi_type ConvertToAbi(const cpp_type& v) const noexcept { return v; }
    cpp_type ConvertToCpp(abi_type v) const noexcept { return v; }
};

template <>
struct Marshaller<const Urho3D::IntVector2&>
{
    using cpp_type = const Urho3D::IntVector2&;
    using abi_type = const Urho3D::IntVector2*;
    abi_type ConvertToAbi(cpp_type v) const noexcept { return &v; }
    cpp_type ConvertToCpp(abi_type v) const noexcept { return *v; }
};

// ═════════════════════════════════════════════════════════════════════

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.IntVector3",
    marshaller_type = "global::Urho3D.Math.IntVector3",
    suppress_emission = true
)]]
Marshaller<Urho3D::IntVector3>
{
    using cpp_type = Urho3D::IntVector3;
    using abi_type = Urho3D::IntVector3;
    abi_type ConvertToAbi(const cpp_type& v) const noexcept { return v; }
    cpp_type ConvertToCpp(abi_type v) const noexcept { return v; }
};

template <>
struct Marshaller<const Urho3D::IntVector3&>
{
    using cpp_type = const Urho3D::IntVector3&;
    using abi_type = const Urho3D::IntVector3*;
    abi_type ConvertToAbi(cpp_type v) const noexcept { return &v; }
    cpp_type ConvertToCpp(abi_type v) const noexcept { return *v; }
};

// ═════════════════════════════════════════════════════════════════════

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.DoubleVector2",
    marshaller_type = "global::Urho3D.Math.DoubleVector2",
    suppress_emission = true
)]]
Marshaller<Urho3D::DoubleVector2>
{
    using cpp_type = Urho3D::DoubleVector2;
    using abi_type = Urho3D::DoubleVector2;
    abi_type ConvertToAbi(const cpp_type& v) const noexcept { return v; }
    cpp_type ConvertToCpp(abi_type v) const noexcept { return v; }
};

template <>
struct Marshaller<const Urho3D::DoubleVector2&>
{
    using cpp_type = const Urho3D::DoubleVector2&;
    using abi_type = const Urho3D::DoubleVector2*;
    abi_type ConvertToAbi(cpp_type v) const noexcept { return &v; }
    cpp_type ConvertToCpp(abi_type v) const noexcept { return *v; }
};

// ═════════════════════════════════════════════════════════════════════

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.DoubleVector3",
    marshaller_type = "global::Urho3D.Math.DoubleVector3",
    suppress_emission = true
)]]
Marshaller<Urho3D::DoubleVector3>
{
    using cpp_type = Urho3D::DoubleVector3;
    using abi_type = Urho3D::DoubleVector3;
    abi_type ConvertToAbi(const cpp_type& v) const noexcept { return v; }
    cpp_type ConvertToCpp(abi_type v) const noexcept { return v; }
};

template <>
struct Marshaller<const Urho3D::DoubleVector3&>
{
    using cpp_type = const Urho3D::DoubleVector3&;
    using abi_type = const Urho3D::DoubleVector3*;
    abi_type ConvertToAbi(cpp_type v) const noexcept { return &v; }
    cpp_type ConvertToCpp(abi_type v) const noexcept { return *v; }
};

// ═════════════════════════════════════════════════════════════════════

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.Quaternion",
    marshaller_type = "global::Urho3D.Math.Quaternion",
    suppress_emission = true
)]]
Marshaller<Urho3D::Quaternion>
{
    using cpp_type = Urho3D::Quaternion;
    using abi_type = Urho3D::Quaternion;
    abi_type ConvertToAbi(const cpp_type& v) const noexcept { return v; }
    cpp_type ConvertToCpp(abi_type v) const noexcept { return v; }
};

template <>
struct Marshaller<const Urho3D::Quaternion&>
{
    using cpp_type = const Urho3D::Quaternion&;
    using abi_type = const Urho3D::Quaternion*;
    abi_type ConvertToAbi(cpp_type v) const noexcept { return &v; }
    cpp_type ConvertToCpp(abi_type v) const noexcept { return *v; }
};

template <>
struct ReturnMarshaller<Urho3D::Quaternion>
{
    using cpp_type = Urho3D::Quaternion;
    using abi_type = QuaternionAbi;
    abi_type ConvertToAbi(const cpp_type& v) const { return ToAbi(v); }
    cpp_type ConvertToCpp(const abi_type& v) const { return ToCpp(v); }
};

// ═════════════════════════════════════════════════════════════════════

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.Color",
    marshaller_type = "global::Urho3D.Math.Color",
    suppress_emission = true
)]]
Marshaller<Urho3D::Color>
{
    using cpp_type = Urho3D::Color;
    using abi_type = Urho3D::Color;
    abi_type ConvertToAbi(const cpp_type& v) const noexcept { return v; }
    cpp_type ConvertToCpp(abi_type v) const noexcept { return v; }
};

// ═════════════════════════════════════════════════════════════════════

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.StringHash",
    marshaller_type = "global::Urho3D.Math.StringHash",
    suppress_emission = true
)]]
Marshaller<Urho3D::StringHash>
{
    using cpp_type = Urho3D::StringHash;
    using abi_type = unsigned int;
    abi_type ConvertToAbi(const cpp_type& v) const noexcept { return v.Value(); }
    cpp_type ConvertToCpp(abi_type v) const noexcept { return static_cast<cpp_type>(v); }
};

template <>
struct Marshaller<const Urho3D::StringHash&>
{
    using cpp_type = const Urho3D::StringHash&;
    using abi_type = const unsigned int*;
    abi_type ConvertToAbi(cpp_type v) const noexcept { return reinterpret_cast<abi_type>(&v); }
    cpp_type ConvertToCpp(abi_type v) const noexcept { return *reinterpret_cast<const Urho3D::StringHash*>(v); }
};

// Generic Marshaller<T&> produces abi_type = Marshaller<T>::abi_type* and
// ConvertToCpp(abi_type) → return *value.  Since StringHash's abi_type is
// unsigned int, that would try to return an unsigned int as
// Urho3D::StringHash&, which fails.  StringHash wraps unsigned int so
// pointer reinterpretation is safe — no mutable state needed.
template <>
struct Marshaller<Urho3D::StringHash&>
{
    using cpp_type = Urho3D::StringHash&;
    using abi_type = unsigned int*;
    abi_type ConvertToAbi(cpp_type v) const noexcept { return reinterpret_cast<abi_type>(&v); }
    cpp_type ConvertToCpp(abi_type v) const noexcept { return *reinterpret_cast<Urho3D::StringHash*>(v); }
};

// ═════════════════════════════════════════════════════════════════════

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.BoundingBox",
    marshaller_type = "global::Urho3D.Math.BoundingBox",
    suppress_emission = true
)]]
Marshaller<Urho3D::BoundingBox>
{
    using cpp_type = Urho3D::BoundingBox;
    using abi_type = Urho3D::BoundingBox;
    abi_type ConvertToAbi(const cpp_type& v) const noexcept { return v; }
    cpp_type ConvertToCpp(abi_type v) const noexcept { return v; }
};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.Matrix2",
    marshaller_type = "global::Urho3D.Math.Matrix2",
    suppress_emission = true
)]]
Marshaller<Urho3D::Matrix2>
{
    using cpp_type = Urho3D::Matrix2;
    using abi_type = Urho3D::Matrix2;
    abi_type ConvertToAbi(const cpp_type& v) const noexcept { return v; }
    cpp_type ConvertToCpp(abi_type v) const noexcept { return v; }
};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.Matrix3",
    marshaller_type = "global::Urho3D.Math.Matrix3",
    suppress_emission = true
)]]
Marshaller<Urho3D::Matrix3>
{
    using cpp_type = Urho3D::Matrix3;
    using abi_type = Urho3D::Matrix3;
    abi_type ConvertToAbi(const cpp_type& v) const noexcept { return v; }
    cpp_type ConvertToCpp(abi_type v) const noexcept { return v; }
};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.Matrix3x4",
    marshaller_type = "global::Urho3D.Math.Matrix3x4",
    suppress_emission = true
)]]
Marshaller<Urho3D::Matrix3x4>
{
    using cpp_type = Urho3D::Matrix3x4;
    using abi_type = Urho3D::Matrix3x4;
    abi_type ConvertToAbi(const cpp_type& v) const noexcept { return v; }
    cpp_type ConvertToCpp(abi_type v) const noexcept { return v; }
};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.Matrix4",
    marshaller_type = "global::Urho3D.Math.Matrix4",
    suppress_emission = true
)]]
Marshaller<Urho3D::Matrix4>
{
    using cpp_type = Urho3D::Matrix4;
    using abi_type = Urho3D::Matrix4;
    abi_type ConvertToAbi(const cpp_type& v) const noexcept { return v; }
    cpp_type ConvertToCpp(abi_type v) const noexcept { return v; }
};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.Plane",
    marshaller_type = "global::Urho3D.Math.Plane",
    suppress_emission = true
)]]
Marshaller<Urho3D::Plane>
{
    using cpp_type = Urho3D::Plane;
    using abi_type = Urho3D::Plane;
    abi_type ConvertToAbi(const cpp_type& v) const noexcept { return v; }
    cpp_type ConvertToCpp(abi_type v) const noexcept { return v; }
};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.Ray",
    marshaller_type = "global::Urho3D.Math.Ray",
    suppress_emission = true
)]]
Marshaller<Urho3D::Ray>
{
    using cpp_type = Urho3D::Ray;
    using abi_type = Urho3D::Ray;
    abi_type ConvertToAbi(const cpp_type& v) const noexcept { return v; }
    cpp_type ConvertToCpp(abi_type v) const noexcept { return v; }
};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.Sphere",
    marshaller_type = "global::Urho3D.Math.Sphere",
    suppress_emission = true
)]]
Marshaller<Urho3D::Sphere>
{
    using cpp_type = Urho3D::Sphere;
    using abi_type = Urho3D::Sphere;
    abi_type ConvertToAbi(const cpp_type& v) const noexcept { return v; }
    cpp_type ConvertToCpp(abi_type v) const noexcept { return v; }
};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.Rect",
    marshaller_type = "global::Urho3D.Math.Rect",
    suppress_emission = true
)]]
Marshaller<Urho3D::Rect>
{
    using cpp_type = Urho3D::Rect;
    using abi_type = Urho3D::Rect;
    abi_type ConvertToAbi(const cpp_type& v) const noexcept { return v; }
    cpp_type ConvertToCpp(abi_type v) const noexcept { return v; }
};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.IntRect",
    marshaller_type = "global::Urho3D.Math.IntRect",
    suppress_emission = true
)]]
Marshaller<Urho3D::IntRect>
{
    using cpp_type = Urho3D::IntRect;
    using abi_type = Urho3D::IntRect;
    abi_type ConvertToAbi(const cpp_type& v) const noexcept { return v; }
    cpp_type ConvertToCpp(abi_type v) const noexcept { return v; }
};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.Transform",
    marshaller_type = "global::Urho3D.Math.Transform",
    suppress_emission = true
)]]
Marshaller<Urho3D::Transform>
{
    using cpp_type = Urho3D::Transform;
    using abi_type = Urho3D::Transform;
    abi_type ConvertToAbi(const cpp_type& v) const noexcept { return v; }
    cpp_type ConvertToCpp(abi_type v) const noexcept { return v; }
};

} // namespace SharpCpp
