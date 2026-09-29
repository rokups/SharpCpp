#pragma once

#include <SharpCpp/Marshaller.hpp>

#include "ConstantTemplateArguments.hpp"

namespace SharpCpp
{
template <class T, unsigned N>
struct [[sharpcpp::managed_type(
    public_type = "ConstantTemplateArguments.Slots<{0}>",
    managed_base_type = "global::SharpCpp.Support.VectorBase<{0}>",
    managed_namespace = "ConstantTemplateArguments"
)]] Marshaller<constant_template_arguments::slots<T, N>>
    : IndexedSequenceCollectionMarshallerBase<constant_template_arguments::slots<T, N>, T> {};

template <class T, unsigned N>
struct Marshaller<constant_template_arguments::slots<T, N>&>
    : OwnedHandleReferenceMarshallerBase<constant_template_arguments::slots<T, N>> {};

template <class T, unsigned N>
struct Marshaller<const constant_template_arguments::slots<T, N>&>
    : OwnedHandleConstReferenceMarshallerBase<constant_template_arguments::slots<T, N>> {};

template <class T, unsigned N>
struct Marshaller<constant_template_arguments::slots<T, N>*>
    : OwnedHandlePointerMarshallerBase<constant_template_arguments::slots<T, N>> {};
}
