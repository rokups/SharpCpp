#pragma once

#include <SharpCpp/Marshaller.hpp>

#include "Sample.hpp"

namespace SharpCpp
{
template <>
struct [[sharpcpp::managed_type(
    public_type = "uint",
    suppress_emission = true
)]] Marshaller<sample::id>
{
    using cpp_type = sample::id;
    using abi_type = unsigned;

    abi_type ConvertToAbi(cpp_type value) noexcept { return value.value; }
    cpp_type ConvertToCpp(abi_type value) noexcept { return {value}; }
};

template <class T>
struct [[sharpcpp::managed_type(
    public_type = "Test.Array<{0}>",
    managed_base_type = "global::SharpCpp.Support.VectorBase<{0}>",
    managed_namespace = "Test"
)]] Marshaller<sample::array<T>>
    : SequenceCollectionMarshallerBase<sample::array<T>, T> {};

template <class T>
struct [[sharpcpp::managed_type(
    public_type = "{0}",
    projection = true
)]] Marshaller<sample::ref<T>>
{
    using cpp_type = sample::ref<T>;
    using abi_type = T*;
};

template <class T, unsigned N>
struct [[sharpcpp::managed_type(
    public_type = "StaticBuffer<{0}>",
    managed_base_type = "global::SharpCpp.Support.ArrayBase<{0}>",
    fixed_array = true
)]] Marshaller<sample::static_buffer<T, N>> {};

template <class T>
struct [[sharpcpp::managed_type(return_ownership = "owning")]]
ReturnMarshaller<const sample::ref<T>&> : Marshaller<sample::ref<T>> {};
}
