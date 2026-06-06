#pragma once

// Override const char* mapping for rbfx: return types and ref parameters
// don't work with ReadOnlySpan<byte> in [LibraryImport].
template <>
struct [[sharpcpp::managed_type(public_type = "global::System.IntPtr")]] Marshaller<const char*>
{
    using cpp_type = const char*;
    using abi_type = const char*;

    constexpr abi_type ConvertToAbi(cpp_type value) const noexcept { return value; }
    constexpr cpp_type ConvertToCpp(abi_type value) const noexcept { return value; }
};
