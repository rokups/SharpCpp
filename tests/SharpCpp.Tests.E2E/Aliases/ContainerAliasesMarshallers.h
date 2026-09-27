#pragma once

#include <SharpCpp/Marshaller.hpp>

#include "ContainerAliases.hpp"

namespace SharpCpp
{
template <typename T, typename TAllocator>
struct [[sharpcpp::managed_type(
    public_type = "ContainerAliases.Vector<{0}>",
    managed_base_type = "global::SharpCpp.Support.VectorBase<{0}>",
    managed_namespace = "ContainerAliases"
)]] Marshaller<container_aliases::vector<T, TAllocator>>
    : SequenceCollectionMarshallerBase<container_aliases::vector<T, TAllocator>, T> {};

template <typename T, typename TAllocator>
struct Marshaller<const container_aliases::vector<T, TAllocator>&>
    : OwnedHandleConstReferenceMarshallerBase<container_aliases::vector<T, TAllocator>> {};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::SharpCpp.Tests.E2E.Aliases.Vector3f",
    marshaller_type = "global::SharpCpp.Tests.E2E.Aliases.Vector3f",
    suppress_emission = true
)]] Marshaller<container_aliases::vector3>
{
    using cpp_type = container_aliases::vector3;
    using abi_type = container_aliases::vector3;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept { return value; }
    constexpr cpp_type ConvertToCpp(abi_type value) noexcept { return value; }
};

template <>
struct ReturnMarshaller<container_aliases::vector3> : Marshaller<container_aliases::vector3> {};

template <>
struct Marshaller<const container_aliases::vector3&>
{
    using cpp_type = const container_aliases::vector3&;
    using abi_type = const container_aliases::vector3*;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept { return &value; }
    constexpr cpp_type ConvertToCpp(abi_type value) noexcept { return *value; }
};

template <>
struct [[sharpcpp::managed_type(
    public_type = "uint",
    marshaller_type = "uint",
    suppress_emission = true
)]] Marshaller<container_aliases::string_hash>
{
    using cpp_type = container_aliases::string_hash;
    using abi_type = unsigned;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept { return value.value; }
    constexpr cpp_type ConvertToCpp(abi_type value) noexcept { return {value}; }
};

template <>
struct ReturnMarshaller<container_aliases::string_hash> : Marshaller<container_aliases::string_hash> {};

template <typename TKey, typename TValue, typename THash, typename TPredicate, typename TAllocator, bool CacheHashCode>
struct [[sharpcpp::managed_type(
    public_type = "ContainerAliases.UnorderedMap<{0}, {1}>",
    managed_base_type = "global::SharpCpp.Support.UnorderedMapBase<{0}, {1}>",
    managed_base_type_constraints = "where {0} : notnull",
    managed_namespace = "ContainerAliases"
)]] Marshaller<container_aliases::unordered_map<TKey, TValue, THash, TPredicate, TAllocator, CacheHashCode>>
    : MapCollectionMarshallerBase<container_aliases::unordered_map<TKey, TValue, THash, TPredicate, TAllocator, CacheHashCode>, TKey, TValue> {};

template <typename TKey, typename TValue, typename THash, typename TPredicate, typename TAllocator, bool CacheHashCode>
struct Marshaller<const container_aliases::unordered_map<TKey, TValue, THash, TPredicate, TAllocator, CacheHashCode>&>
    : OwnedHandleConstReferenceMarshallerBase<container_aliases::unordered_map<TKey, TValue, THash, TPredicate, TAllocator, CacheHashCode>> {};

template <typename TKey, typename TValue, typename THash, typename TPredicate, typename TAllocator, bool CacheHashCode>
struct Marshaller<container_aliases::unordered_map<TKey, TValue, THash, TPredicate, TAllocator, CacheHashCode>&>
    : OwnedHandleReferenceMarshallerBase<container_aliases::unordered_map<TKey, TValue, THash, TPredicate, TAllocator, CacheHashCode>> {};
}
