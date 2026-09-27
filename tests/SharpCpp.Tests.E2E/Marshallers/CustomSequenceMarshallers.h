#pragma once

#include <SharpCpp/Marshaller.hpp>

#include "CustomSequence.hpp"

namespace SharpCpp
{
template <>
struct [[sharpcpp::managed_type(
    public_type = "uint",
    suppress_emission = true
)]] Marshaller<custom_sequence::id>
{
    using cpp_type = custom_sequence::id;
    using abi_type = unsigned;

    abi_type ConvertToAbi(cpp_type value) noexcept { return value.value; }
    cpp_type ConvertToCpp(abi_type value) noexcept { return {value}; }
};

template <class T>
struct [[sharpcpp::managed_type(
    public_type = "CustomSequences.Array<{0}>",
    managed_base_type = "global::SharpCpp.Support.VectorBase<{0}>",
    managed_namespace = "CustomSequences"
)]] Marshaller<custom_sequence::array<T>>
    : SequenceCollectionMarshallerBase<custom_sequence::array<T>, T> {};

template <class T>
struct [[sharpcpp::managed_type(
    public_type = "{0}",
    projection = true
)]] Marshaller<custom_sequence::ref<T>>
{
    using cpp_type = custom_sequence::ref<T>;
    using abi_type = T*;

    abi_type ConvertToAbi(const cpp_type& value) noexcept { return value.get(); }
    cpp_type ConvertToCpp(abi_type value) noexcept { return cpp_type(value); }
};

template <class T>
struct [[sharpcpp::managed_type(return_ownership = "owning")]]
ReturnMarshaller<const custom_sequence::ref<T>&> : Marshaller<custom_sequence::ref<T>> {};
}
