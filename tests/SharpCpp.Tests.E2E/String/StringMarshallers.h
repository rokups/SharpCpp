#pragma once

#include <SharpCpp/Marshaller.hpp>

#include "String.hpp"

namespace SharpCpp
{
template <>
struct [[sharpcpp::managed_type(
    managed_base_type = "global::SharpCpp.Support.StringBase",
    public_type = "E2EStd.String",
    managed_namespace = "E2EStd",
    managed_wrapper_provider_kind = "string",
    managed_wrapper_copyable = true
)]]
Marshaller<e2e_std::string> : OwnedStringMarshallerBase<e2e_std::string>
{
    using cpp_type = e2e_std::string;

    [[sharpcpp::expose_member]] static int Echo(cpp_type&, int value) { return value; }
    [[sharpcpp::expose_member]] static double Echo(cpp_type&, double value) { return value; }
    [[sharpcpp::expose_member]] static bool EchoBool(cpp_type&, bool value) { return value; }
    [[sharpcpp::expose_member]] static char EchoChar(cpp_type&, char value) { return value; }
    [[sharpcpp::expose_member]] static unsigned char EchoByte(cpp_type&, unsigned char value) { return value; }
    [[sharpcpp::expose_member]] static short EchoShort(cpp_type&, short value) { return value; }
    [[sharpcpp::expose_member]] static unsigned short EchoUShort(cpp_type&, unsigned short value) { return value; }
    [[sharpcpp::expose_member]] static unsigned int EchoUInt(cpp_type&, unsigned int value) { return value; }
    [[sharpcpp::expose_member]] static long EchoLong(cpp_type&, long value) { return value; }
    [[sharpcpp::expose_member]] static unsigned long EchoULong(cpp_type&, unsigned long value) { return value; }
    [[sharpcpp::expose_member]] static long long EchoLongLong(cpp_type&, long long value) { return value; }
    [[sharpcpp::expose_member]] static unsigned long long EchoULongLong(cpp_type&, unsigned long long value) { return value; }
    [[sharpcpp::expose_member]] static float EchoFloat(cpp_type&, float value) { return value; }
    [[sharpcpp::expose_member]] static std::size_t EchoSize(cpp_type&, std::size_t value) { return value; }
    [[sharpcpp::expose_member]] static char* EchoPointer(cpp_type&, char* value) { return value; }
    [[sharpcpp::expose_member]] static void Touch(cpp_type& self, unsigned char value) { self.push_back(static_cast<char>(value)); }
};

template <>
struct Marshaller<e2e_std::string&> : OwnedStringReferenceMarshallerBase<e2e_std::string> {};

template <>
struct Marshaller<const e2e_std::string&> : OwnedStringConstReferenceMarshallerBase<e2e_std::string> {};

template <>
struct Marshaller<const e2e_std::string*> : OwnedStringConstPointerMarshallerBase<e2e_std::string> {};

template <>
struct Marshaller<e2e_std::string*> : OwnedStringPointerMarshallerBase<e2e_std::string> {};

template <>
struct ReturnMarshaller<e2e_std::string> : OwnedStringReturnMarshallerBase<e2e_std::string> {};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::System.ReadOnlySpan<byte>",
    managed_marshaller = "global::SharpCpp.Support.Utf8SpanAbiMarshaller",
    undefaultable = true
)]]
Marshaller<e2e_std::string_view> : StringViewMarshallerBase<e2e_std::string_view> {};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::System.ReadOnlySpan<byte>",
    managed_marshaller = "global::SharpCpp.Support.Utf8SpanAbiReturnMarshaller"
)]] ReturnMarshaller<e2e_std::string_view> : Marshaller<e2e_std::string_view> {};
}
