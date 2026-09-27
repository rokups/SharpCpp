#pragma once

#include <SharpCpp/Marshaller.hpp>

#include "ConfiguredMember.hpp"

namespace SharpCpp
{
template <>
struct [[sharpcpp::managed_type(
    public_type = "global::SharpCpp.Tests.E2E.Generated.ConfiguredMemberValue",
    managed_namespace = "SharpCpp.Tests.E2E.Generated",
    managed_base_type = "global::SharpCpp.Support.NativeObject"
)]] Marshaller<configured_member_value> : OwnedHandleMarshallerBase<configured_member_value>
{
    using cpp_type = configured_member_value;

    [[sharpcpp::expose_member]]
    static int Probe(cpp_type& self, int value) { return self.value + value; }

    [[sharpcpp::expose_member]]
    static double Probe(cpp_type& self, double value) { return self.value + value; }

    [[sharpcpp::expose_member]]
    static configured_member_payload* EchoPayload(cpp_type&, configured_member_payload* value) { return value; }
};

template <>
struct Marshaller<configured_member_value&> : OwnedHandleReferenceMarshallerBase<configured_member_value> {};

template <>
struct Marshaller<const configured_member_value&> : OwnedHandleConstReferenceMarshallerBase<configured_member_value> {};

template <>
struct Marshaller<configured_member_value*> : OwnedHandlePointerMarshallerBase<configured_member_value> {};

template <>
struct Marshaller<const configured_member_value*> : OwnedHandleConstPointerMarshallerBase<configured_member_value> {};

template <>
struct ReturnMarshaller<configured_member_value> : OwnedHandleReturnMarshallerBase<configured_member_value> {};
}
