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
Marshaller<e2e_std::string> : OwnedStringMarshallerBase<e2e_std::string> {};

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
