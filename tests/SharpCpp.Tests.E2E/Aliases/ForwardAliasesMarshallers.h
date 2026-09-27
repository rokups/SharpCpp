#pragma once

#include <SharpCpp/Marshaller.hpp>

#include "ForwardAliases.hpp"

namespace SharpCpp
{
struct phase2_value_exposed_members
{
    using cpp_type = phase2_breadth::phase2_value;

    [[sharpcpp::expose_member]]
    static int ReadConfigured(const cpp_type& self);

    [[sharpcpp::expose_member]]
    static void WriteConfigured(cpp_type& self, int value);
};

inline int phase2_value_exposed_members::ReadConfigured(const cpp_type& self)
{
    return self.get_value();
}

inline void phase2_value_exposed_members::WriteConfigured(cpp_type& self, int value)
{
    self.set_value(value);
}

template <>
struct [[sharpcpp::managed_type(
    public_type = "Phase2Value",
    managed_namespace = "SharpCpp.Tests.E2E.Generated",
    managed_base_type = "global::SharpCpp.Tests.E2E.Aliases.Phase2ManagedBase",
    managed_static_interfaces = "global::SharpCpp.Tests.E2E.Aliases.IPhase2NativeStatic<{managed_type}>; global::SharpCpp.Tests.E2E.Aliases.IPhase2NativeMarker<{managed_type}>",
    managed_static_interfaces_abstract = true,
    managed_wrapper_copyable = true,
    owned_handle = true
)]] Marshaller<phase2_breadth::phase2_value>
    : OwnedHandleMarshallerBase<phase2_breadth::phase2_value>
    , phase2_value_exposed_members
{
};

template <>
struct Marshaller<phase2_breadth::phase2_value&>
    : OwnedHandleReferenceMarshallerBase<phase2_breadth::phase2_value>
{
};

template <>
struct Marshaller<const phase2_breadth::phase2_value&>
    : OwnedHandleConstReferenceMarshallerBase<phase2_breadth::phase2_value>
{
};

template <>
struct Marshaller<phase2_breadth::phase2_value*>
    : OwnedHandlePointerMarshallerBase<phase2_breadth::phase2_value>
{
};

template <>
struct Marshaller<const phase2_breadth::phase2_value*>
    : OwnedHandleConstPointerMarshallerBase<phase2_breadth::phase2_value>
{
};

template <>
struct ReturnMarshaller<phase2_breadth::phase2_value>
    : OwnedHandleReturnMarshallerBase<phase2_breadth::phase2_value>
{
};

template <typename TValue, typename THidden>
struct [[sharpcpp::managed_type(
    public_type = "Phase2Box<{0}>",
    managed_namespace = "SharpCpp.Tests.E2E.Generated",
    managed_base_type = "global::SharpCpp.Support.NativeObject",
    managed_base_type_constraints = " where {0} : class",
    managed_wrapper_copyable = true,
    owned_handle = true
)]] Marshaller<phase2_breadth::phase2_box<TValue, THidden>>
    : OwnedHandleMarshallerBase<phase2_breadth::phase2_box<TValue, THidden>>
{
};

template <typename TValue, typename THidden>
struct Marshaller<phase2_breadth::phase2_box<TValue, THidden>&>
    : OwnedHandleReferenceMarshallerBase<phase2_breadth::phase2_box<TValue, THidden>>
{
};

template <typename TValue, typename THidden>
struct Marshaller<const phase2_breadth::phase2_box<TValue, THidden>&>
    : OwnedHandleConstReferenceMarshallerBase<phase2_breadth::phase2_box<TValue, THidden>>
{
};

template <typename TValue, typename THidden>
struct Marshaller<phase2_breadth::phase2_box<TValue, THidden>*>
    : OwnedHandlePointerMarshallerBase<phase2_breadth::phase2_box<TValue, THidden>>
{
};

template <typename TValue, typename THidden>
struct Marshaller<const phase2_breadth::phase2_box<TValue, THidden>*>
    : OwnedHandleConstPointerMarshallerBase<phase2_breadth::phase2_box<TValue, THidden>>
{
};

template <typename TValue, typename THidden>
struct ReturnMarshaller<phase2_breadth::phase2_box<TValue, THidden>>
    : OwnedHandleReturnMarshallerBase<phase2_breadth::phase2_box<TValue, THidden>>
{
};
}
