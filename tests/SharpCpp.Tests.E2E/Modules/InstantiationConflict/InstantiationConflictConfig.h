#pragma once

#include <SharpCpp/Marshaller.hpp>

#include "InstantiationConflict.hpp"

// The name does not end in Marshallers.h, so the main E2E compilation does not load this file.

namespace SharpCpp
{
template <typename TValue, typename TTag>
struct hidden_box_exposed
{
    using cpp_type = hidden_argument_conflict::hidden_box<TValue, TTag>;

    [[sharpcpp::expose_member(shape = "method", exposed = true)]]
    static int ReadHiddenTag(const cpp_type& self)
    {
        return self.tag();
    }
};

template <typename TValue, typename TTag>
struct [[sharpcpp::managed_type(
    public_type = "HiddenBox<{0}>",
    managed_namespace = "SharpCpp.Tests.E2E.Generated",
    managed_base_type = "global::SharpCpp.Support.NativeObject",
    managed_base_type_constraints = " where {0} : class",
    owned_handle = true,
    return_ownership = "owning"
)]] Marshaller<hidden_argument_conflict::hidden_box<TValue, TTag>>
    : OwnedHandleMarshallerBase<hidden_argument_conflict::hidden_box<TValue, TTag>>
    , hidden_box_exposed<TValue, TTag>
{
};
}
