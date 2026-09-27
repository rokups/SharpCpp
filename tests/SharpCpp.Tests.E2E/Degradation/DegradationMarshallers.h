#pragma once

#include <SharpCpp/Marshaller.hpp>

#include "Degradation.hpp"

namespace SharpCpp
{
template <>
struct [[sharpcpp::managed_type(
    managed_base_type = "MissingPipelineManagedBase",
    owned_handle = true
)]] Marshaller<pipeline_breadth::pipeline_orphan_base>
    : OwnedHandleMarshallerBase<pipeline_breadth::pipeline_orphan_base>
{
};

template <>
struct [[sharpcpp::managed_type(
    public_type = "bad::Managed",
    marshaller_type = "int",
    suppress_emission = true
)]] Marshaller<pipeline_breadth::pipeline_bad_managed>
{
    using cpp_type = pipeline_breadth::pipeline_bad_managed;
    using abi_type = int;

    abi_type ConvertToAbi(cpp_type value) noexcept { return value.value; }
    cpp_type ConvertToCpp(abi_type value) noexcept { return {value}; }
};

template <>
struct ReturnMarshaller<pipeline_breadth::pipeline_bad_managed>
    : Marshaller<pipeline_breadth::pipeline_bad_managed>
{
};

template <>
struct [[sharpcpp::managed_type(
    public_type = "pipeline_breadth::pipeline_configured_unsupported",
    suppress_emission = true
)]] Marshaller<pipeline_breadth::pipeline_configured_unsupported>
{
    using cpp_type = pipeline_breadth::pipeline_configured_unsupported;
    using abi_type = int;

    abi_type ConvertToAbi(cpp_type value) noexcept { return value.value; }
    cpp_type ConvertToCpp(abi_type value) noexcept { return {value}; }
};

template <>
struct ReturnMarshaller<pipeline_breadth::pipeline_configured_unsupported>
    : Marshaller<pipeline_breadth::pipeline_configured_unsupported>
{
};

template <typename T>
struct [[sharpcpp::managed_type(
    public_type = "PipelineConfiguredBox<{0}>",
    suppress_emission = true
)]] Marshaller<pipeline_breadth::pipeline_configured_box<T>>
{
    using cpp_type = pipeline_breadth::pipeline_configured_box<T>;
    using abi_type = cpp_type*;

    abi_type ConvertToAbi(cpp_type& value) noexcept { return &value; }
    cpp_type& ConvertToCpp(abi_type value) noexcept { return *value; }
};

template <typename T>
struct ReturnMarshaller<pipeline_breadth::pipeline_configured_box<T>>
    : Marshaller<pipeline_breadth::pipeline_configured_box<T>>
{
};

struct pipeline_accessor_members
{
    using cpp_type = pipeline_breadth::pipeline_accessor_probe;

    [[sharpcpp::expose_member]]
    static int GetPipelineMismatch(const cpp_type& self)
    {
        return self.get_native_value();
    }

    [[sharpcpp::expose_member]]
    static void SetPipelineMismatch(cpp_type& self, float value)
    {
        self.set_native_value(static_cast<int>(value));
    }

    [[sharpcpp::expose_member]]
    static int GetCollision(const cpp_type& self)
    {
        return self.get_native_value();
    }

    [[sharpcpp::expose_member]]
    static void SetCollision(cpp_type& self, int value)
    {
        self.set_native_value(value);
    }

    [[sharpcpp::expose_member]]
    static int Collision(const cpp_type& self)
    {
        return self.get_native_value();
    }
};

template <>
struct [[sharpcpp::managed_type(owned_handle = true)]]
Marshaller<pipeline_breadth::pipeline_accessor_probe>
    : OwnedHandleMarshallerBase<pipeline_breadth::pipeline_accessor_probe>
    , pipeline_accessor_members
{
};
}
