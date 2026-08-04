#pragma once

#include <SharpCpp/Marshaller.hpp>

#include "Surface.hpp"

namespace SharpCpp
{
template <typename T, std::size_t N>
struct [[sharpcpp::managed_type(
    public_type = "E2EStd.Array<{0}>",
    managed_base_type = "global::SharpCpp.Support.ArrayBase<{0}>",
    managed_namespace = "E2EStd",
    managed_wrapper_provider_kind = "array",
    managed_wrapper_copyable = true,
    fixed_array = true
)]]
Marshaller<e2e_std::array<T, N>> : FixedArrayValueMarshallerBase<e2e_std::array<T, N>, N> {};

template <typename T, std::size_t N>
struct Marshaller<e2e_std::array<T, N>&> : FixedArrayReferenceMarshallerBase<e2e_std::array<T, N>, N> {};

template <typename T, std::size_t N>
struct Marshaller<const e2e_std::array<T, N>&> : FixedArrayConstReferenceMarshallerBase<e2e_std::array<T, N>, N> {};

template <typename T, std::size_t N>
struct Marshaller<e2e_std::array<T, N>*> : FixedArrayPointerMarshallerBase<e2e_std::array<T, N>, N> {};

template <typename T, std::size_t N>
struct Marshaller<const e2e_std::array<T, N>*> : FixedArrayConstPointerMarshallerBase<e2e_std::array<T, N>, N> {};

template <typename T, std::size_t N>
struct ReturnMarshaller<e2e_std::array<T, N>> : FixedArrayReturnMarshallerBase<e2e_std::array<T, N>, N> {};

template <typename T>
struct [[sharpcpp::managed_type(
    public_type = "E2EStd.SharedArray<{0}>",
    managed_base_type = "global::SharpCpp.Support.SharedArrayBase<{0}>",
    managed_namespace = "E2EStd",
    managed_wrapper_provider_kind = "dynamic_array",
    dynamic_array = true,
    return_ownership = "owning"
)]]
Marshaller<e2e_std::shared_array<T>> : OwnedHandleMarshallerBase<e2e_std::shared_array<T>> {};

template <typename T>
struct Marshaller<e2e_std::shared_array<T>&> : OwnedHandleReferenceMarshallerBase<e2e_std::shared_array<T>> {};

template <typename T>
struct Marshaller<const e2e_std::shared_array<T>&> : OwnedHandleConstReferenceMarshallerBase<e2e_std::shared_array<T>> {};

template <typename T>
struct Marshaller<e2e_std::shared_array<T>*> : OwnedHandlePointerMarshallerBase<e2e_std::shared_array<T>> {};

template <typename T>
struct Marshaller<const e2e_std::shared_array<T>*> : OwnedHandleConstPointerMarshallerBase<e2e_std::shared_array<T>> {};

template <typename T>
struct ReturnMarshaller<e2e_std::shared_array<T>> : OwnedHandleReturnMarshallerBase<e2e_std::shared_array<T>> {};

template <typename T>
struct [[sharpcpp::managed_type(
    public_type = "global::E2EStd.ProviderArray<{0}>",
    managed_base_type = "global::SharpCpp.Support.SharedArrayBase<{0}>",
    managed_wrapper_assembly = "SharpCpp.Tests.E2E",
    managed_wrapper_provider_kind = "dynamic_array",
    managed_wrapper_provider_namespace = "global::Std.Infrastructure",
    dynamic_array = true,
    return_ownership = "owning"
)]]
Marshaller<e2e_std::provider_array<T>> : OwnedHandleMarshallerBase<e2e_std::provider_array<T>> {};

template <typename T>
struct Marshaller<e2e_std::provider_array<T>&> : OwnedHandleReferenceMarshallerBase<e2e_std::provider_array<T>> {};

template <typename T>
struct Marshaller<const e2e_std::provider_array<T>&> : OwnedHandleConstReferenceMarshallerBase<e2e_std::provider_array<T>> {};

template <typename T>
struct Marshaller<e2e_std::provider_array<T>*> : OwnedHandlePointerMarshallerBase<e2e_std::provider_array<T>> {};

template <typename T>
struct Marshaller<const e2e_std::provider_array<T>*> : OwnedHandleConstPointerMarshallerBase<e2e_std::provider_array<T>> {};

template <typename T>
struct ReturnMarshaller<e2e_std::provider_array<T>> : OwnedHandleReturnMarshallerBase<e2e_std::provider_array<T>> {};
}
