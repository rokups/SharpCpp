#pragma once

#include <SharpCpp/Marshaller.hpp>

#include "Projection.hpp"

namespace SharpCpp
{
template <typename T>
struct [[sharpcpp::managed_type(
    public_type = "{0}",
    projection = true,
    return_ownership = "owning"
)]] Marshaller<coverage_matrix::projected_owner<T>>
{
    using cpp_type = coverage_matrix::projected_owner<T>;
    using abi_type = T*;

    abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return value.detach();
    }

    cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return cpp_type(value);
    }
};

template <typename T>
struct ReturnMarshaller<coverage_matrix::projected_owner<T>>
    : Marshaller<coverage_matrix::projected_owner<T>>
{
};
}
