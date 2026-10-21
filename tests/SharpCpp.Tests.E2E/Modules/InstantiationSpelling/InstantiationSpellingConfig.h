#pragma once

#include <SharpCpp/Marshaller.hpp>

#include "InstantiationSpelling.hpp"

// The name does not end in Marshallers.h, so the main E2E compilation does not load this file.

namespace SharpCpp
{
template <typename TValue, typename TTag>
struct [[sharpcpp::managed_type(
    public_type = "SpelledBox<{0}>",
    managed_namespace = "SharpCpp.Tests.E2E.Generated",
    managed_base_type = "global::SharpCpp.Support.NativeObject",
    managed_base_type_constraints = " where {0} : class",
    owned_handle = true,
    return_ownership = "owning"
)]] Marshaller<hidden_argument_spelling::spelled_box<TValue, TTag>>
    : OwnedHandleMarshallerBase<hidden_argument_spelling::spelled_box<TValue, TTag>>
{
};

template <typename TValue, typename TTag>
struct [[sharpcpp::managed_type(
    public_type = "NestedBox<{0}>",
    managed_namespace = "SharpCpp.Tests.E2E.Generated",
    managed_base_type = "global::SharpCpp.Support.NativeObject",
    managed_base_type_constraints = " where {0} : class",
    owned_handle = true,
    return_ownership = "owning"
)]] Marshaller<hidden_argument_spelling::nested_box<TValue, TTag>>
    : OwnedHandleMarshallerBase<hidden_argument_spelling::nested_box<TValue, TTag>>
{
};
}
