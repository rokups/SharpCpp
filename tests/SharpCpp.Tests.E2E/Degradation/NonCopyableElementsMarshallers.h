#pragma once

#include <SharpCpp/Marshaller.hpp>

#include "NonCopyableElements.hpp"

namespace SharpCpp
{
template <class T, unsigned N>
struct [[sharpcpp::managed_type(
    public_type = "NonCopyableElements.Slots<{0}>",
    managed_base_type = "global::SharpCpp.Support.VectorBase<{0}>",
    managed_namespace = "NonCopyableElements"
)]] Marshaller<non_copyable_elements::slots<T, N>>
    : IndexedSequenceCollectionMarshallerBase<non_copyable_elements::slots<T, N>, T> {};

template <class T, unsigned N>
struct Marshaller<non_copyable_elements::slots<T, N>&>
    : OwnedHandleReferenceMarshallerBase<non_copyable_elements::slots<T, N>> {};

template <class T, unsigned N>
struct Marshaller<const non_copyable_elements::slots<T, N>&>
    : OwnedHandleConstReferenceMarshallerBase<non_copyable_elements::slots<T, N>> {};

template <class T, unsigned N>
struct Marshaller<non_copyable_elements::slots<T, N>*>
    : OwnedHandlePointerMarshallerBase<non_copyable_elements::slots<T, N>> {};
}
