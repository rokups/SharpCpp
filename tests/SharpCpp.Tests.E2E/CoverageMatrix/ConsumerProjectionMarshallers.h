#pragma once

#include <SharpCpp/Marshaller.hpp>

#include "ConsumerProjection.hpp"

namespace SharpCpp
{
template <>
struct [[sharpcpp::managed_type(
    public_type = "int",
    marshaller_type = "int",
    undefaultable = true,
    suppress_emission = true
)]] Marshaller<consumer_projection_scalar>
{
    using cpp_type = consumer_projection_scalar;
    using abi_type = int;

    abi_type ConvertToAbi(const cpp_type& value) const noexcept
    {
        return value.value;
    }

    cpp_type ConvertToCpp(abi_type value) const noexcept
    {
        return cpp_type(value);
    }
};

template <>
struct ReturnMarshaller<consumer_projection_scalar> : Marshaller<consumer_projection_scalar> {};
}
