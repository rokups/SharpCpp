#pragma once

#include <SharpCpp/Marshaller.hpp>

#include "ReservedNames.hpp"

namespace SharpCpp
{
/// Binds opaque_token as managed `object`, the parameter type of the static NativeObject helpers.
template <>
struct [[sharpcpp::managed_type(
    public_type = "object",
    managed_marshaller = "global::SharpCpp.Tests.E2E.Records.OpaqueTokenMarshaller",
    suppress_emission = true
)]] Marshaller<reserved_names::opaque_token> : IndirectValueMarshallerBase<reserved_names::opaque_token>
{
};
}
