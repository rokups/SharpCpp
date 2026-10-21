#pragma once

#include <SharpCpp/Marshaller.hpp>

#include "EaFunction.h"

// The eastl::function marshallers. They mirror the std::function marshallers in
// SharpCpp/Marshaller.hpp.
namespace SharpCpp
{
template <class TSignature>
struct [[sharpcpp::managed_type(callback = true)]] Marshaller<eastl::function<TSignature>>
    : ManagedCallbackMarshallerBase<eastl::function<TSignature>, TSignature>
{
};

template <class TSignature>
struct Marshaller<const eastl::function<TSignature>>
    : ManagedCallbackMarshallerBase<eastl::function<TSignature>, TSignature>
{
};

template <class TSignature>
struct Marshaller<eastl::function<TSignature>&&>
    : ManagedCallbackMarshallerBase<eastl::function<TSignature>, TSignature>
{
};

template <class TSignature>
struct Marshaller<const eastl::function<TSignature>&>
    : ManagedCallbackConstReferenceMarshallerBase<eastl::function<TSignature>, TSignature>
{
};
}
