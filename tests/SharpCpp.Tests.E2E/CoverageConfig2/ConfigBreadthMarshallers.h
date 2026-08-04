#pragma once

#include <SharpCpp/Marshaller.hpp>

#include "ConfigBreadth.hpp"

namespace SharpCpp
{
template <typename TValue, typename THidden>
struct config2_exposed_root
{
    using cpp_type = config_breadth2::config2_surface<TValue, THidden>;

    [[sharpcpp::expose_member]]
    static int ReadConfigured(const cpp_type& self)
    {
        return self.read();
    }
};

template <typename TValue, typename THidden>
struct config2_exposed_left : virtual config2_exposed_root<TValue, THidden>
{
    using cpp_type = config_breadth2::config2_surface<TValue, THidden>;

    [[sharpcpp::expose_member(shape = "method", exposed = true)]]
    static void BumpConfigured(cpp_type& self, int delta)
    {
        self.bump(delta);
    }
};

template <typename TValue, typename THidden>
struct config2_exposed_right : virtual config2_exposed_root<TValue, THidden>
{
    using cpp_type = config_breadth2::config2_surface<TValue, THidden>;

    [[sharpcpp::expose_member]]
    static TValue EchoConfigured(const cpp_type& self, TValue value)
    {
        return self.echo(value);
    }
};

template <typename TValue, typename THidden>
struct config2_exposed_diamond
    : config2_exposed_left<TValue, THidden>
    , config2_exposed_right<TValue, THidden>
{
};

template <typename TValue, typename TCallback, typename THidden>
struct config2_repair_exposed
{
    using cpp_type = config_breadth2::config2_repair<TValue, TCallback, THidden>;

    [[sharpcpp::expose_member(
        shape = "method",
        exposed = true,
        scanner_probe = ("quoted \\\" )", (1 + (2)))
    )]]
    static int InvokeRepairConfigured(
        const cpp_type& self,
        TValue other)
    {
        return self.invoke(other);
    }

    static TValue EchoRepairConfigured(const cpp_type& self)
        [[sharpcpp::expose_member]]
    {
        return self.echo_value();
    }
};

template <>
struct [[sharpcpp::managed_type(
    public_type = "Config2Value",
    managed_namespace = "SharpCpp.Tests.E2E.Generated",
    managed_base_type = "global::SharpCpp.Support.NativeObject",
    managed_static_interfaces = "global::SharpCpp.Tests.E2E.CoverageConfig2.IConfig2NativeStatic<{managed_type}>; global::SharpCpp.Tests.E2E.CoverageConfig2.IConfig2NativeMarker<{managed_type}>",
    managed_static_interfaces_abstract = true,
    managed_wrapper_copyable = true,
    owned_handle = true,
    return_ownership = "owning"
)]] Marshaller<config_breadth2::config2_value>
    : OwnedHandleMarshallerBase<config_breadth2::config2_value>
{
};

template <>
struct Marshaller<config_breadth2::config2_value&>
    : OwnedHandleReferenceMarshallerBase<config_breadth2::config2_value>
{
};

template <>
struct Marshaller<const config_breadth2::config2_value&>
    : OwnedHandleConstReferenceMarshallerBase<config_breadth2::config2_value>
{
};

template <>
struct Marshaller<config_breadth2::config2_value*>
    : OwnedHandlePointerMarshallerBase<config_breadth2::config2_value>
{
};

template <>
struct Marshaller<const config_breadth2::config2_value*>
    : OwnedHandleConstPointerMarshallerBase<config_breadth2::config2_value>
{
};

template <>
struct ReturnMarshaller<config_breadth2::config2_value>
    : OwnedHandleReturnMarshallerBase<config_breadth2::config2_value>
{
};

template <typename TValue, typename THidden>
struct [[sharpcpp::managed_type(
    public_type = "Config2Surface<{0}>",
    managed_namespace = "SharpCpp.Tests.E2E.Generated",
    managed_base_type = "global::SharpCpp.Support.NativeObject",
    managed_base_type_constraints = " where {0} : class",
    managed_wrapper_copyable = true,
    owned_handle = true,
    return_ownership = "owning"
)]] Marshaller<config_breadth2::config2_surface<TValue, THidden>>
    : OwnedHandleMarshallerBase<config_breadth2::config2_surface<TValue, THidden>>
    , config2_exposed_diamond<TValue, THidden>
{
};

template <typename TValue, typename THidden>
struct Marshaller<config_breadth2::config2_surface<TValue, THidden>&>
    : OwnedHandleReferenceMarshallerBase<config_breadth2::config2_surface<TValue, THidden>>
{
};

template <typename TValue, typename THidden>
struct Marshaller<const config_breadth2::config2_surface<TValue, THidden>&>
    : OwnedHandleConstReferenceMarshallerBase<config_breadth2::config2_surface<TValue, THidden>>
{
};

template <typename TValue, typename THidden>
struct Marshaller<config_breadth2::config2_surface<TValue, THidden>*>
    : OwnedHandlePointerMarshallerBase<config_breadth2::config2_surface<TValue, THidden>>
{
};

template <typename TValue, typename THidden>
struct Marshaller<const config_breadth2::config2_surface<TValue, THidden>*>
    : OwnedHandleConstPointerMarshallerBase<config_breadth2::config2_surface<TValue, THidden>>
{
};

template <typename TValue, typename THidden>
struct ReturnMarshaller<config_breadth2::config2_surface<TValue, THidden>>
    : OwnedHandleReturnMarshallerBase<config_breadth2::config2_surface<TValue, THidden>>
{
};

template <typename TValue, typename THidden>
struct [[sharpcpp::managed_type(
    public_type = "Config2Outer<{0}>",
    managed_namespace = "SharpCpp.Tests.E2E.Generated",
    managed_base_type = "global::SharpCpp.Support.NativeObject",
    managed_base_type_constraints = " where {0} : class",
    managed_wrapper_copyable = true,
    owned_handle = true,
    return_ownership = "owning"
)]] Marshaller<config_breadth2::config2_outer<TValue, THidden>>
    : OwnedHandleMarshallerBase<config_breadth2::config2_outer<TValue, THidden>>
{
};

template <typename TValue, typename THidden>
struct Marshaller<config_breadth2::config2_outer<TValue, THidden>&>
    : OwnedHandleReferenceMarshallerBase<config_breadth2::config2_outer<TValue, THidden>>
{
};

template <typename TValue, typename THidden>
struct Marshaller<const config_breadth2::config2_outer<TValue, THidden>&>
    : OwnedHandleConstReferenceMarshallerBase<config_breadth2::config2_outer<TValue, THidden>>
{
};

template <typename TValue, typename THidden>
struct Marshaller<config_breadth2::config2_outer<TValue, THidden>*>
    : OwnedHandlePointerMarshallerBase<config_breadth2::config2_outer<TValue, THidden>>
{
};

template <typename TValue, typename THidden>
struct Marshaller<const config_breadth2::config2_outer<TValue, THidden>*>
    : OwnedHandleConstPointerMarshallerBase<config_breadth2::config2_outer<TValue, THidden>>
{
};

template <typename TValue, typename THidden>
struct ReturnMarshaller<config_breadth2::config2_outer<TValue, THidden>>
    : OwnedHandleReturnMarshallerBase<config_breadth2::config2_outer<TValue, THidden>>
{
};

template <>
struct [[sharpcpp::managed_type(return_ownership = "owning")]]
ReturnMarshaller<config_breadth2::config2_nested_surface_alias>
    : OwnedHandleReturnMarshallerBase<config_breadth2::config2_nested_surface_alias>
{
};

template <typename TValue, typename TCallback, typename THidden>
struct [[sharpcpp::managed_type(
    public_type = "Config2Repair<{0}>",
    managed_namespace = "SharpCpp.Tests.E2E.Generated",
    managed_base_type = "global::SharpCpp.Support.NativeObject",
    managed_base_type_constraints = " where {0} : class",
    managed_wrapper_copyable = true,
    owned_handle = true,
    return_ownership = "owning"
)]] Marshaller<config_breadth2::config2_repair<TValue, TCallback, THidden>>
    : OwnedHandleMarshallerBase<config_breadth2::config2_repair<TValue, TCallback, THidden>>
    , config2_repair_exposed<TValue, TCallback, THidden>
{
};

template <>
struct [[sharpcpp::managed_type(
    public_type = "Config2Repair<{0}>",
    managed_namespace = "SharpCpp.Tests.E2E.Generated",
    managed_base_type = "global::SharpCpp.Support.NativeObject",
    managed_base_type_constraints = " where {0} : class",
    managed_wrapper_copyable = true,
    owned_handle = true,
    return_ownership = "owning",
    scanner_probe = ("quoted \\\" )", (1 + (2)))
)]] Marshaller<config_breadth2::config2_repair_alias>
    : OwnedHandleMarshallerBase<config_breadth2::config2_repair_alias>
    , config2_repair_exposed<
        config_breadth2::config2_value_alias*,
        config_breadth2::config2_repair_callback,
        unsigned int>
{
};

template <>
struct Marshaller<config_breadth2::config2_repair_alias&>
    : OwnedHandleReferenceMarshallerBase<config_breadth2::config2_repair_alias>
{
};

template <>
struct Marshaller<const config_breadth2::config2_repair_alias&>
    : OwnedHandleConstReferenceMarshallerBase<config_breadth2::config2_repair_alias>
{
};

template <>
struct Marshaller<config_breadth2::config2_repair_alias*>
    : OwnedHandlePointerMarshallerBase<config_breadth2::config2_repair_alias>
{
};

template <>
struct Marshaller<const config_breadth2::config2_repair_alias*>
    : OwnedHandleConstPointerMarshallerBase<config_breadth2::config2_repair_alias>
{
};

template <>
struct [[sharpcpp::managed_type(return_ownership = "owning")]]
ReturnMarshaller<config_breadth2::config2_repair<
    config_breadth2::config2_value_alias*,
    std::function<int(
        config_breadth2::config2_value_alias*,
        config_breadth2::config2_value_alias*)>,
    unsigned int>>
    : OwnedHandleReturnMarshallerBase<config_breadth2::config2_repair_alias>
{
};
}
