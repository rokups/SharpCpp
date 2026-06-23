#pragma once

#include <SharpCpp/Marshaller.hpp>

#include <RmlUi/Core/Variant.h>

namespace SharpCpp
{

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Rml.Variant",
    managed_namespace = "Rml"
)]] Marshaller<Rml::Variant>
{
    using cpp_type = Rml::Variant;
    using abi_type = Rml::Variant;

    abi_type ConvertToAbi(cpp_type value) const noexcept
    {
        return value;
    }

    cpp_type ConvertToCpp(abi_type value) const noexcept
    {
        return value;
    }

    [[sharpcpp::expose_member]]
    static void SetString(cpp_type& self, const eastl::basic_string<char>& value)
    {
        self = Rml::String(value);
    }

    [[sharpcpp::expose_member]]
    static eastl::basic_string<char> GetString(const cpp_type& self)
    {
        return self.Get<Rml::String>();
    }
};

} // namespace SharpCpp
