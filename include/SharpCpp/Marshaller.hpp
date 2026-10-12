#pragma once

#include <array>
#include <cstddef>
#include <concepts>
#include <cstdlib>
#include <cstdint>
#include <exception>
#include <functional>
#include <iterator>
#include <list>
#include <map>
#include <memory>
#include <new>
#include <stdexcept>
#include <string>
#include <string_view>
#include <type_traits>
#include <unordered_map>
#include <utility>
#include <vector>

namespace SharpCpp
{
struct ManagedCallbackHandleAbi
{
    void* state{};
    void* callback{};
};

struct NativeArrayAbi
{
    void* handle{};
    std::size_t length{};
};

template <class T, std::size_t N>
struct NativeArray {};

namespace detail
{
template <class T>
using remove_cvref_t = std::remove_cv_t<std::remove_reference_t<T>>;

template <class T, class = void>
struct HasSync : std::false_type {};

template <class T>
struct HasSync<T, std::void_t<decltype(std::declval<T&>().Sync())>> : std::true_type {};

template <class T>
inline constexpr bool HasSyncValue = HasSync<T>::value;

template <class TMarshaller>
struct SyncOnDestroyMarshaller : TMarshaller
{
    using TMarshaller::TMarshaller;

    ~SyncOnDestroyMarshaller() noexcept
    {
        if constexpr (HasSyncValue<TMarshaller>)
        {
            this->Sync();
        }
    }
};

template <class T>
inline constexpr bool DefaultPassesByValue =
    !std::is_reference_v<T> &&
    (std::is_scalar_v<remove_cvref_t<T>> || std::is_enum_v<remove_cvref_t<T>>);

template <class T>
concept IntrusiveReferenceTarget = requires(T* value)
{
    { value->AddRef() } -> std::same_as<void>;
    { value->Release() } -> std::same_as<void>;
};

template <class T>
using DefaultPointerAbi = std::conditional_t<
    std::is_const_v<std::remove_reference_t<T>>,
    std::add_pointer_t<std::add_const_t<remove_cvref_t<T>>>,
    std::add_pointer_t<remove_cvref_t<T>>>;

template <class T, bool IsEnum = std::is_enum_v<remove_cvref_t<T>>>
struct DefaultValueAbi
{
    using type = T;
};

template <class T>
struct DefaultValueAbi<T, true>
{
    using enum_type = remove_cvref_t<T>;
    using underlying_type = std::underlying_type_t<enum_type>;
    using type = std::conditional_t<
        sizeof(underlying_type) < sizeof(std::uint32_t),
        std::conditional_t<std::is_signed_v<underlying_type>, std::int32_t, std::uint32_t>,
        underlying_type>;
};

template <class T>
using DefaultValueAbiType = typename DefaultValueAbi<T>::type;

template <class T>
using DefaultAbi = std::conditional_t<DefaultPassesByValue<T>, T, DefaultPointerAbi<T>>;

template <class T, bool PassesByValue = DefaultPassesByValue<T>>
struct DefaultMarshallerBase;

template <class T>
struct DefaultMarshallerBase<T, true>
{
    using is_default_marshaller = std::true_type;
    using cpp_type = T;
    using abi_type = DefaultValueAbiType<T>;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return static_cast<abi_type>(value);
    }

    constexpr cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return static_cast<cpp_type>(value);
    }
};

template <class T>
struct DefaultMarshallerBase<T, false>
{
    using is_default_marshaller = std::true_type;
    using cpp_type = T;
    using abi_type = DefaultPointerAbi<T>;
    using value_type = remove_cvref_t<T>;

    constexpr abi_type ConvertToAbi(value_type& value) noexcept
    {
        return std::addressof(value);
    }

    constexpr abi_type ConvertToAbi(const value_type& value) noexcept
    {
        return const_cast<value_type*>(std::addressof(value));
    }

    cpp_type ConvertToCpp(abi_type value) noexcept
    {
        if (value == nullptr)
        {
            // SharpCpp complex value ABI cannot be null.
            std::terminate();
        }

        return *value;
    }
};

// Detects the `is_default_marshaller` tag that DefaultMarshallerBase adds to a Marshaller
// specialization. Unlike IsDefaultMarshaller<T> further below (which looks up Marshaller<T>
// from a cpp type T), this inspects a marshaller type M directly, which is what the generic
// pointer/reference fallbacks need: they already have the value marshaller type in hand.
template <class M, class = void>
struct is_default_marshaller : std::false_type {};

template <class M>
struct is_default_marshaller<M, std::void_t<typename M::is_default_marshaller>> : std::true_type {};

template <class M>
inline constexpr bool is_default_marshaller_v = is_default_marshaller<M>::value;

// sizeof/alignof an incomplete type is ill-formed wherever it appears, not just when the branch
// using it is "taken" -- so a plain sizeof(T) == sizeof(U) inside a static_assert's || chain
// still hard-errors for opaque forward-declared handle types (e.g. `struct handle_t;` used only
// as `handle_t*`). Route the comparison through a class template partial specialization instead:
// substitution failure there is in the immediate context and is SFINAE-friendly, so an
// incomplete A or B just makes this evaluate to false rather than failing to compile.
template <class T, class = void>
struct is_complete_type : std::false_type {};

template <class T>
struct is_complete_type<T, std::void_t<decltype(sizeof(T))>> : std::true_type {};

template <class T>
inline constexpr bool is_complete_type_v = is_complete_type<T>::value;

template <class A, class B, class = void>
struct is_layout_compatible_pair : std::false_type {};

// A type is always layout-compatible with itself: reinterpret_cast<A*> from A* is a no-op, not an
// actual reinterpretation, so this holds even when A is incomplete or not trivially copyable (e.g.
// an "identity" Marshaller<T> whose abi_type is T itself, but T has a user-provided special member
// function -- common for SIMD-aligned math types with a defaulted-looking but user-written copy
// constructor).
template <class A>
struct is_layout_compatible_pair<A, A, void> : std::true_type {};

template <class A, class B>
struct is_layout_compatible_pair<
    A,
    B,
    std::enable_if_t<!std::is_same_v<A, B> && is_complete_type_v<A> && is_complete_type_v<B>>>
    : std::bool_constant<
          sizeof(A) == sizeof(B) && alignof(A) == alignof(B) &&
          std::is_trivially_copyable_v<A> && std::is_trivially_copyable_v<B>>
{
};

template <class A, class B>
inline constexpr bool is_layout_compatible_pair_v = is_layout_compatible_pair<A, B>::value;

template <class T, bool PassesByValue = DefaultPassesByValue<T>>
struct DefaultReturnMarshallerBase;

template <class T>
struct DefaultReturnMarshallerBase<T, true> : DefaultMarshallerBase<T, true>
{
};

template <class T>
struct DefaultReturnMarshallerBase<T, false>
{
    using cpp_type = T;
    using value_type = remove_cvref_t<T>;
    using abi_type = DefaultPointerAbi<T>;

    abi_type ConvertToAbi(cpp_type value) noexcept
    {
        if constexpr (std::is_lvalue_reference_v<T>)
        {
            return std::addressof(value);
        }
        else
        {
            return new value_type(std::move(value));
        }
    }

    cpp_type ConvertToCpp(abi_type value) noexcept
    {
        if (value == nullptr)
        {
            // SharpCpp complex return ABI cannot be null.
            std::terminate();
        }

        return *value;
    }
};

template <typename TFunction, typename TSignature>
TFunction CreateManagedCallbackWrapper(ManagedCallbackHandleAbi handle);
}

template <class TString>
struct OwnedStringMarshallerBase
{
    using cpp_type = TString;
    using abi_type = TString*;

    constexpr abi_type ConvertToAbi(cpp_type& value) noexcept
    {
        return &value;
    }

    constexpr cpp_type& ConvertToCpp(abi_type value) noexcept
    {
        return *value;
    }

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false)]]
    static unsigned long long GetCountCore(const cpp_type& self)
    {
        return self.size();
    }

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false)]]
    static const char* GetDataCore(const cpp_type& self)
    {
        return self.data();
    }

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false)]]
    static void AddCore(cpp_type& self, unsigned char value)
    {
        self.push_back(static_cast<char>(value));
    }

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false)]]
    static void ClearCore(cpp_type& self)
    {
        self.clear();
    }
};

template <class TString>
struct OwnedStringConstReferenceMarshallerBase
{
    using cpp_type = const TString&;
    using abi_type = TString*;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return const_cast<TString*>(&value);
    }

    constexpr cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return *value;
    }
};

template <class TString>
struct OwnedStringReferenceMarshallerBase
{
    using cpp_type = TString&;
    using abi_type = TString*;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return &value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return *value;
    }
};

template <class TString>
struct OwnedStringConstPointerMarshallerBase
{
    using cpp_type = const TString*;
    using abi_type = TString*;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return const_cast<TString*>(value);
    }

    constexpr cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return value;
    }
};

template <class TString>
struct OwnedStringPointerMarshallerBase
{
    using cpp_type = TString*;
    using abi_type = TString*;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return value;
    }
};

template <class TString>
struct OwnedStringReturnMarshallerBase
{
    using cpp_type = TString;
    using abi_type = TString*;

    abi_type ConvertToAbi(const cpp_type& value) noexcept
    {
        return new TString(value);
    }

    abi_type ConvertToAbi(cpp_type&& value) noexcept
    {
        return new TString(std::move(value));
    }

    cpp_type& ConvertToCpp(abi_type value) noexcept
    {
        return *value;
    }
};

template <class TValue>
struct OwnedHandleMarshallerBase
{
    using cpp_type = TValue;
    using abi_type = TValue*;

    template <class TSource>
    abi_type ConvertToAbi(TSource&& value) noexcept
    {
        return new TValue(std::forward<TSource>(value));
    }

    template <class TCpp = cpp_type>
    TCpp ConvertToCpp(abi_type value) noexcept
    {
        if constexpr (std::is_copy_constructible_v<TCpp>)
        {
            return *value;
        }

        return std::move(*value);
    }
};

template <class TValue>
struct OwnedHandleReferenceMarshallerBase
{
    using cpp_type = TValue&;
    using abi_type = TValue*;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return &value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return *value;
    }
};

template <class TValue>
struct OwnedHandleConstReferenceMarshallerBase
{
    using cpp_type = const TValue&;
    using abi_type = TValue*;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return const_cast<TValue*>(&value);
    }

    constexpr cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return *value;
    }
};

template <class TValue>
struct OwnedHandlePointerMarshallerBase
{
    using cpp_type = TValue*;
    using abi_type = TValue*;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return value;
    }
};

template <class TValue>
struct OwnedHandleConstPointerMarshallerBase
{
    using cpp_type = const TValue*;
    using abi_type = TValue*;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return const_cast<TValue*>(value);
    }

    constexpr cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return value;
    }
};

template <class TValue>
struct OwnedHandleReturnMarshallerBase : OwnedHandleMarshallerBase<TValue>
{
};

template <class TFunction, class TSignature>
struct ManagedCallbackMarshallerBase
{
    using cpp_type = TFunction;
    using abi_type = ManagedCallbackHandleAbi*;

    template <class T = cpp_type>
    abi_type ConvertToAbi(const T&) noexcept
    {
        static_assert(!std::is_same_v<T, T>,
            "SharpCpp does not support native-to-managed callback function-object returns in this slice.");
        return {};
    }

    cpp_type ConvertToCpp(abi_type value) noexcept
    {
        if (value == nullptr)
            return {};

        const auto handle = *value;
        std::free(value);
        return detail::CreateManagedCallbackWrapper<cpp_type, TSignature>(handle);
    }
};

template <class TFunction, class TSignature>
struct ManagedCallbackConstReferenceMarshallerBase
{
    using cpp_type = const TFunction&;
    using abi_type = ManagedCallbackHandleAbi*;

    template <class T = cpp_type>
    abi_type ConvertToAbi(T) noexcept
    {
        static_assert(!std::is_same_v<T, T>,
            "SharpCpp does not support native-to-managed callback function-object returns in this slice.");
        return {};
    }

    cpp_type ConvertToCpp(abi_type value) noexcept
    {
        if (value == nullptr)
        {
            storage_ = {};
            return storage_;
        }

        const auto handle = *value;
        std::free(value);
        storage_ = detail::CreateManagedCallbackWrapper<TFunction, TSignature>(handle);
        return storage_;
    }

private:
    mutable TFunction storage_{};
};

template <class TStringView>
struct StringViewMarshallerBase
{
    using cpp_type = TStringView;
    struct abi_type
    {
        const char* data{};
        std::size_t size{};
    };

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return { value.data(), value.size() };
    }

    constexpr cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return value.data == nullptr ? cpp_type{} : cpp_type(value.data, value.size);
    }
};

template <class TStringView>
struct StringViewConstReferenceMarshallerBase
{
    using cpp_type = const TStringView&;
    using abi_type = typename StringViewMarshallerBase<TStringView>::abi_type;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return { value.data(), value.size() };
    }

    cpp_type ConvertToCpp(abi_type value) noexcept
    {
        storage_ = value.data == nullptr ? TStringView{} : TStringView(value.data, value.size);
        return storage_;
    }

private:
    mutable TStringView storage_{};
};

template <class TCollection, class T>
struct SequenceCollectionMarshallerBase : OwnedHandleMarshallerBase<TCollection>
{
    using cpp_type = TCollection;
    using abi_type = typename OwnedHandleMarshallerBase<TCollection>::abi_type;

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false, disambiguate = false)]]
    static unsigned long long GetCountCore(const cpp_type& self)
    {
        return self.size();
    }

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false, disambiguate = false)]]
    static void ClearCore(cpp_type& self)
    {
        self.clear();
    }

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false)]]
    static T GetAtCore(const cpp_type& self, unsigned long long index)
    {
        return self[static_cast<std::size_t>(index)];
    }

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false)]]
    static void SetAtCore(cpp_type& self, unsigned long long index, const T& value)
    {
        self[static_cast<std::size_t>(index)] = value;
    }

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false)]]
    static void AddCore(cpp_type& self, const T& value)
    {
        self.push_back(value);
    }

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false)]]
    static void InsertAtCore(cpp_type& self, unsigned long long index, const T& value)
    {
        self.insert(self.begin() + static_cast<std::size_t>(index), value);
    }

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false)]]
    static void RemoveAtCore(cpp_type& self, unsigned long long index)
    {
        self.erase(self.begin() + static_cast<std::size_t>(index));
    }
};

template <class TList, class T>
struct ListCollectionMarshallerBase : OwnedHandleMarshallerBase<TList>
{
    using cpp_type = TList;
    using abi_type = typename OwnedHandleMarshallerBase<TList>::abi_type;

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false, disambiguate = false)]]
    static unsigned long long GetCountCore(const cpp_type& self)
    {
        return self.size();
    }

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false, disambiguate = false)]]
    static void ClearCore(cpp_type& self)
    {
        self.clear();
    }

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false)]]
    static T GetAtCore(const cpp_type& self, unsigned long long index)
    {
        return *std::next(self.begin(), static_cast<typename cpp_type::difference_type>(index));
    }

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false)]]
    static void SetAtCore(cpp_type& self, unsigned long long index, const T& value)
    {
        *std::next(self.begin(), static_cast<typename cpp_type::difference_type>(index)) = value;
    }

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false)]]
    static void AddCore(cpp_type& self, const T& value)
    {
        self.push_back(value);
    }

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false)]]
    static void InsertAtCore(cpp_type& self, unsigned long long index, const T& value)
    {
        self.insert(std::next(self.begin(), static_cast<typename cpp_type::difference_type>(index)), value);
    }

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false)]]
    static void RemoveAtCore(cpp_type& self, unsigned long long index)
    {
        self.erase(std::next(self.begin(), static_cast<typename cpp_type::difference_type>(index)));
    }
};

/// Collection adapter for containers that have indexed access and a maximum
/// capacity but no insert operation (for example JPH::StaticArray).
template <class TSequence, class T>
struct IndexedSequenceCollectionMarshallerBase : OwnedHandleMarshallerBase<TSequence>
{
    using cpp_type = TSequence;
    using abi_type = typename OwnedHandleMarshallerBase<TSequence>::abi_type;

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false, disambiguate = false)]]
    static unsigned long long GetCountCore(const cpp_type& self)
    {
        return self.size();
    }

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false, disambiguate = false)]]
    static void ClearCore(cpp_type& self)
    {
        self.clear();
    }

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false)]]
    static T GetAtCore(const cpp_type& self, unsigned long long index)
    {
        return self[static_cast<typename cpp_type::size_type>(index)];
    }

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false)]]
    static void SetAtCore(cpp_type& self, unsigned long long index, const T& value)
    {
        self[static_cast<typename cpp_type::size_type>(index)] = value;
    }

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false)]]
    static void AddCore(cpp_type& self, const T& value)
    {
        self.push_back(value);
    }

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false)]]
    static void InsertAtCore(cpp_type& self, unsigned long long index, const T& value)
    {
        const auto size = self.size();
        const auto position = static_cast<typename cpp_type::size_type>(index);
        if (position >= size)
        {
            self.push_back(value);
            return;
        }

        self.push_back(self.back());
        for (auto current = size; current > position; --current)
            self[current] = self[current - 1];
        self[position] = value;
    }

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false)]]
    static void RemoveAtCore(cpp_type& self, unsigned long long index)
    {
        self.erase(self.begin() + static_cast<typename cpp_type::size_type>(index));
    }
};

template <class TMap, class TKey, class TValue>
struct MapCollectionMarshallerBase : OwnedHandleMarshallerBase<TMap>
{
    using cpp_type = TMap;
    using abi_type = typename OwnedHandleMarshallerBase<TMap>::abi_type;

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false, disambiguate = false)]]
    static unsigned long long GetCountCore(const cpp_type& self)
    {
        return self.size();
    }

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false, disambiguate = false)]]
    static void ClearCore(cpp_type& self)
    {
        self.clear();
    }

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false)]]
    static bool ContainsKeyCore(const cpp_type& self, const TKey& key)
    {
        return self.find(key) != self.end();
    }

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false)]]
    static TValue GetValueCore(const cpp_type& self, const TKey& key)
    {
        return self.find(key)->second;
    }

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false)]]
    static void SetValueCore(cpp_type& self, const TKey& key, const TValue& value)
    {
        self[key] = value;
    }

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false)]]
    static bool AddCore(cpp_type& self, const TKey& key, const TValue& value)
    {
        return self.emplace(key, value).second;
    }

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false)]]
    static bool RemoveCore(cpp_type& self, const TKey& key)
    {
        return self.erase(key) != 0;
    }

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false)]]
    static TKey GetKeyAtCore(const cpp_type& self, unsigned long long index)
    {
        return std::next(self.begin(), static_cast<typename cpp_type::difference_type>(index))->first;
    }

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false)]]
    static TValue GetValueAtCore(const cpp_type& self, unsigned long long index)
    {
        return std::next(self.begin(), static_cast<typename cpp_type::difference_type>(index))->second;
    }
};

template <class TArray, std::size_t N>
struct FixedArrayReferenceMarshallerBase
{
    using cpp_type = TArray&;
    using abi_type = NativeArrayAbi;

    cpp_type ConvertToCpp(abi_type value) noexcept
    {
        Validate(value);
        return *static_cast<TArray*>(value.handle);
    }

    abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return { &value, N };
    }

    static void Validate(abi_type value) noexcept
    {
        if (value.handle == nullptr || value.length != N)
        {
            // SharpCpp fixed array length mismatch.
            std::terminate();
        }
    }
};

template <class TArray, std::size_t N>
struct FixedArrayConstReferenceMarshallerBase
{
    using cpp_type = const TArray&;
    using abi_type = NativeArrayAbi;

    cpp_type ConvertToCpp(abi_type value) noexcept
    {
        Validate(value);
        return *static_cast<const TArray*>(value.handle);
    }

    abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return { const_cast<TArray*>(&value), N };
    }

    static void Validate(abi_type value) noexcept
    {
        if (value.handle == nullptr || value.length != N)
        {
            // SharpCpp fixed array length mismatch.
            std::terminate();
        }
    }
};

template <class TArray, std::size_t N>
struct FixedArrayValueMarshallerBase
{
    using cpp_type = TArray;
    using abi_type = NativeArrayAbi;

    cpp_type ConvertToCpp(abi_type value) noexcept
    {
        FixedArrayConstReferenceMarshallerBase<TArray, N>::Validate(value);
        return *static_cast<const TArray*>(value.handle);
    }

    abi_type ConvertToAbi(cpp_type& value) noexcept
    {
        return { &value, N };
    }

    abi_type ConvertToAbi(const cpp_type& value) noexcept
    {
        return { const_cast<TArray*>(&value), N };
    }
};

template <class TArray, std::size_t N>
struct FixedArrayPointerMarshallerBase
{
    using cpp_type = TArray*;
    using abi_type = NativeArrayAbi;

    cpp_type ConvertToCpp(abi_type value) noexcept
    {
        if (value.handle == nullptr)
        {
            return nullptr;
        }

        if (value.length != N)
        {
            // SharpCpp fixed array length mismatch.
            std::terminate();
        }

        return static_cast<TArray*>(value.handle);
    }

    abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return { value, value == nullptr ? 0 : N };
    }
};

template <class TArray, std::size_t N>
struct FixedArrayConstPointerMarshallerBase
{
    using cpp_type = const TArray*;
    using abi_type = NativeArrayAbi;

    cpp_type ConvertToCpp(abi_type value) noexcept
    {
        if (value.handle == nullptr)
        {
            return nullptr;
        }

        if (value.length != N)
        {
            // SharpCpp fixed array length mismatch.
            std::terminate();
        }

        return static_cast<const TArray*>(value.handle);
    }

    abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return { const_cast<TArray*>(value), value == nullptr ? 0 : N };
    }
};

template <class TArray, std::size_t N>
struct FixedArrayReturnMarshallerBase
{
    using cpp_type = TArray;
    using abi_type = NativeArrayAbi;

    abi_type ConvertToAbi(const cpp_type& value) noexcept
    {
        return { new TArray(value), N };
    }

    abi_type ConvertToAbi(cpp_type&& value) noexcept
    {
        return { new TArray(std::move(value)), N };
    }

    cpp_type ConvertToCpp(abi_type value) noexcept
    {
        FixedArrayConstReferenceMarshallerBase<TArray, N>::Validate(value);
        return *static_cast<const TArray*>(value.handle);
    }
};

template <class TElement, std::size_t N>
struct NativeFixedArrayMarshallerBase
{
    using cpp_type = TElement[N];
    using abi_type = NativeArrayAbi;

    cpp_type& ConvertToCpp(abi_type value) noexcept
    {
        Validate(value);
        return *reinterpret_cast<TElement (*)[N]>(value.handle);
    }

    abi_type ConvertToAbi(cpp_type& value) noexcept
    {
        return { value, N };
    }

    abi_type ConvertToAbi(const cpp_type& value) noexcept
    {
        return { const_cast<TElement*>(value), N };
    }

    static void Validate(abi_type value) noexcept
    {
        if (value.handle == nullptr || value.length != N)
        {
            // SharpCpp fixed array length mismatch.
            std::terminate();
        }
    }
};

template <class T>
struct Marshaller : detail::DefaultMarshallerBase<T> {};

template <class T>
struct IndirectValueMarshallerBase
{
    static_assert(std::is_trivially_copyable_v<T> && std::is_trivially_destructible_v<T>);

    using cpp_type = T;
    using abi_type = T*;

    abi_type ConvertToAbi(const cpp_type& value) noexcept
    {
        auto* storage = static_cast<T*>(std::malloc(sizeof(T)));
        if (storage == nullptr)
            std::terminate();

        return new (storage) T(value);
    }

    cpp_type ConvertToCpp(abi_type value) noexcept
    {
        if (value == nullptr)
            std::terminate();

        return *value;
    }
};

template <class T>
struct IndirectConstReferenceValueMarshallerBase
{
    static_assert(std::is_trivially_copyable_v<T> && std::is_trivially_destructible_v<T>);

    using cpp_type = const T&;
    using abi_type = T*;

    abi_type ConvertToAbi(cpp_type value) noexcept
    {
        auto* storage = static_cast<T*>(std::malloc(sizeof(T)));
        if (storage == nullptr)
            std::terminate();

        return new (storage) T(value);
    }

    cpp_type ConvertToCpp(abi_type value) noexcept
    {
        if (value == nullptr)
            std::terminate();

        return *value;
    }
};

template <class TResult, class... TArgs>
struct Marshaller<TResult(TArgs...)>
{
    using cpp_type = TResult (*)(TArgs...);
    using abi_type = cpp_type;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return value;
    }
};

template <class TResult, class... TArgs>
struct Marshaller<TResult (*)(TArgs...)>
{
    using cpp_type = TResult (*)(TArgs...);
    using abi_type = cpp_type;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return value;
    }
};

template <>
struct Marshaller<void>
{
    using cpp_type = void;
    using abi_type = void;
};

template <class TValue>
struct ProjectedValueConstReferenceMarshallerBase
{
    using cpp_type = const TValue&;
    using value_marshaller_type = Marshaller<TValue>;
    using value_abi_type = typename value_marshaller_type::abi_type;
    using abi_type = const value_abi_type*;

    abi_type ConvertToAbi(cpp_type value) noexcept
    {
        abi_storage_ = value_marshaller_.ConvertToAbi(value);
        return &abi_storage_;
    }

    cpp_type ConvertToCpp(abi_type value) noexcept
    {
        storage_ = value_marshaller_.ConvertToCpp(*value);
        return storage_;
    }

private:
    value_marshaller_type value_marshaller_;
    value_abi_type abi_storage_{};
    TValue storage_{};
};

template <class TValue>
struct ProjectedValueReferenceMarshallerBase
{
    using cpp_type = TValue&;
    using value_marshaller_type = Marshaller<TValue>;
    using value_abi_type = typename value_marshaller_type::abi_type;
    using abi_type = value_abi_type*;

    abi_type ConvertToAbi(cpp_type value) noexcept
    {
        abi_storage_ = value_marshaller_.ConvertToAbi(value);
        return &abi_storage_;
    }

    cpp_type ConvertToCpp(abi_type value) noexcept
    {
        abi_argument_ = value;
        storage_ = value_marshaller_.ConvertToCpp(*value);
        return storage_;
    }

    void Sync() noexcept
    {
        if (abi_argument_ != nullptr)
            *abi_argument_ = value_marshaller_.ConvertToAbi(storage_);
    }

private:
    value_marshaller_type value_marshaller_;
    value_abi_type abi_storage_{};
    abi_type abi_argument_{};
    TValue storage_{};
};

// Single-element, nullable pointer counterpart to ProjectedValueConstReferenceMarshallerBase,
// for a `const T*` parameter/field where Marshaller<T> is a projected value type whose abi_type
// layout does not match T's native layout (so the generic Marshaller<const T*> fallback cannot
// reinterpret_cast between them). This is not an array marshaller: it copies at most one element.
template <class TValue>
struct ProjectedValueConstPointerMarshallerBase
{
    using cpp_type = const TValue*;
    using value_marshaller_type = Marshaller<TValue>;
    using value_abi_type = typename value_marshaller_type::abi_type;
    using abi_type = const value_abi_type*;

    abi_type ConvertToAbi(cpp_type value) noexcept
    {
        if (value == nullptr)
            return nullptr;

        abi_storage_ = value_marshaller_.ConvertToAbi(*value);
        return &abi_storage_;
    }

    cpp_type ConvertToCpp(abi_type value) noexcept
    {
        if (value == nullptr)
            return nullptr;

        storage_ = value_marshaller_.ConvertToCpp(*value);
        return &storage_;
    }

private:
    value_marshaller_type value_marshaller_;
    value_abi_type abi_storage_{};
    TValue storage_{};
};

// Single-element, nullable pointer counterpart to ProjectedValueReferenceMarshallerBase, for a
// mutable `T*` parameter/field where Marshaller<T> is a projected value type whose abi_type
// layout does not match T's native layout. Like the reference base, an in/out pointer needs
// Sync() to write the (possibly modified) managed value back through the borrowed abi pointer
// after the call; a null pointer stays null through both conversions and Sync is then a no-op.
template <class TValue>
struct ProjectedValuePointerMarshallerBase
{
    using cpp_type = TValue*;
    using value_marshaller_type = Marshaller<TValue>;
    using value_abi_type = typename value_marshaller_type::abi_type;
    using abi_type = value_abi_type*;

    abi_type ConvertToAbi(cpp_type value) noexcept
    {
        if (value == nullptr)
            return nullptr;

        abi_storage_ = value_marshaller_.ConvertToAbi(*value);
        return &abi_storage_;
    }

    cpp_type ConvertToCpp(abi_type value) noexcept
    {
        abi_argument_ = value;
        if (value == nullptr)
        {
            storage_ = TValue{};
            return nullptr;
        }

        storage_ = value_marshaller_.ConvertToCpp(*value);
        return &storage_;
    }

    void Sync() noexcept
    {
        if (abi_argument_ != nullptr)
            *abi_argument_ = value_marshaller_.ConvertToAbi(storage_);
    }

private:
    value_marshaller_type value_marshaller_;
    value_abi_type abi_storage_{};
    abi_type abi_argument_{};
    TValue storage_{};
};

// Array counterpart to ProjectedValuePointerMarshallerBase: a mutable `T*` parameter that
// addresses a caller-owned array of projected values, bound to Span<TManaged> through
// SharpCpp.Support.SpanAbiMarshaller<T>. The element count crosses the ABI in-band next to
// the data pointer, so the native callee sees no length; the managed caller is responsible for
// passing a span at least as long as the callee writes (see MetadataKeys.MinimumSpanLength).
// Because T's native layout can differ from the ABI element layout (e.g. a 16-byte SIMD vector
// against a 12-byte managed struct), the elements are copied into native storage for the call
// and written back after it by Sync(). A null or empty span reaches the callee as nullptr.
template <class TValue>
struct ProjectedValueSpanMarshallerBase
{
    using cpp_type = TValue*;
    using value_marshaller_type = Marshaller<TValue>;
    using value_abi_type = typename value_marshaller_type::abi_type;

    static_assert(
        std::is_trivially_copyable_v<value_abi_type>,
        "ProjectedValueSpanMarshallerBase<T> passes Marshaller<T>::abi_type elements through a "
        "managed Span, so the abi_type must be trivially copyable.");

    struct abi_type
    {
        value_abi_type* data{};
        std::size_t length{};
    };

    cpp_type ConvertToCpp(abi_type value)
    {
        abi_argument_ = value;
        storage_.clear();
        if (value.data == nullptr || value.length == 0)
            return nullptr;

        storage_.reserve(value.length);
        for (std::size_t index = 0; index < value.length; ++index)
            storage_.push_back(value_marshaller_.ConvertToCpp(value.data[index]));
        return storage_.data();
    }

    void Sync() noexcept
    {
        for (std::size_t index = 0; index < storage_.size(); ++index)
            abi_argument_.data[index] = value_marshaller_.ConvertToAbi(storage_[index]);
    }

private:
    value_marshaller_type value_marshaller_;
    abi_type abi_argument_{};
    std::vector<TValue> storage_;
};

// Read-only counterpart to ProjectedValueSpanMarshallerBase: a `const T*` parameter that
// addresses an array of projected values, bound to ReadOnlySpan<TManaged> through
// SharpCpp.Support.ReadOnlySpanAbiMarshaller<T>. Nothing is written back.
template <class TValue>
struct ProjectedValueConstSpanMarshallerBase
{
    using cpp_type = const TValue*;
    using value_marshaller_type = Marshaller<TValue>;
    using value_abi_type = typename value_marshaller_type::abi_type;

    static_assert(
        std::is_trivially_copyable_v<value_abi_type>,
        "ProjectedValueConstSpanMarshallerBase<T> passes Marshaller<T>::abi_type elements "
        "through a managed ReadOnlySpan, so the abi_type must be trivially copyable.");

    struct abi_type
    {
        const value_abi_type* data{};
        std::size_t length{};
    };

    cpp_type ConvertToCpp(abi_type value)
    {
        storage_.clear();
        if (value.data == nullptr || value.length == 0)
            return nullptr;

        storage_.reserve(value.length);
        for (std::size_t index = 0; index < value.length; ++index)
            storage_.push_back(value_marshaller_.ConvertToCpp(value.data[index]));
        return storage_.data();
    }

private:
    value_marshaller_type value_marshaller_;
    std::vector<TValue> storage_;
};

template <class TSignature>
struct [[sharpcpp::managed_type(callback = true)]] Marshaller<std::function<TSignature>>
    : ManagedCallbackMarshallerBase<std::function<TSignature>, TSignature>
{
};

template <class TSignature>
struct Marshaller<const std::function<TSignature>>
    : ManagedCallbackMarshallerBase<std::function<TSignature>, TSignature>
{
};

template <class TSignature>
struct Marshaller<std::function<TSignature>&&>
    : ManagedCallbackMarshallerBase<std::function<TSignature>, TSignature>
{
};

template <class TSignature>
struct Marshaller<const std::function<TSignature>&>
    : ManagedCallbackConstReferenceMarshallerBase<std::function<TSignature>, TSignature>
{
};

template <>
struct [[sharpcpp::managed_type(public_type = "global::System.IntPtr")]] Marshaller<void*>
{
    using cpp_type = void*;
    using abi_type = void*;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return value;
    }
};

template <>
struct [[sharpcpp::managed_type(public_type = "global::System.IntPtr")]] Marshaller<const void*>
{
    using cpp_type = const void*;
    using abi_type = const void*;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return value;
    }
};

template <>
struct [[sharpcpp::managed_type(public_type = "global::System.IntPtr")]] Marshaller<signed char*>
{
    using cpp_type = signed char*;
    using abi_type = signed char*;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return value;
    }
};

template <>
struct [[sharpcpp::managed_type(public_type = "global::System.IntPtr")]] Marshaller<const signed char*>
{
    using cpp_type = const signed char*;
    using abi_type = const signed char*;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return value;
    }
};

template <>
struct [[sharpcpp::managed_type(public_type = "global::System.IntPtr")]] Marshaller<volatile signed char*>
{
    using cpp_type = volatile signed char*;
    using abi_type = volatile signed char*;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return value;
    }
};

template <>
struct [[sharpcpp::managed_type(public_type = "global::System.IntPtr")]] Marshaller<const volatile signed char*>
{
    using cpp_type = const volatile signed char*;
    using abi_type = const volatile signed char*;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return value;
    }
};

template <>
struct [[sharpcpp::managed_type(public_type = "global::System.IntPtr")]] Marshaller<unsigned char*>
{
    using cpp_type = unsigned char*;
    using abi_type = unsigned char*;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return value;
    }
};

template <>
struct [[sharpcpp::managed_type(public_type = "global::System.IntPtr")]] Marshaller<const unsigned char*>
{
    using cpp_type = const unsigned char*;
    using abi_type = const unsigned char*;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return value;
    }
};

template <>
struct [[sharpcpp::managed_type(public_type = "global::System.IntPtr")]] Marshaller<volatile unsigned char*>
{
    using cpp_type = volatile unsigned char*;
    using abi_type = volatile unsigned char*;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return value;
    }
};

template <>
struct [[sharpcpp::managed_type(public_type = "global::System.IntPtr")]] Marshaller<const volatile unsigned char*>
{
    using cpp_type = const volatile unsigned char*;
    using abi_type = const volatile unsigned char*;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return value;
    }
};

template <>
struct Marshaller<const signed char*&>
{
    using cpp_type = const signed char*&;
    using abi_type = const signed char**;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept
    {
        storage_ = value;
        return &storage_;
    }

    cpp_type ConvertToCpp(abi_type value) noexcept
    {
        abi_argument_ = value;
        storage_ = *value;
        return storage_;
    }

    void Sync() noexcept
    {
        if (abi_argument_)
            *abi_argument_ = storage_;
    }

private:
    mutable const signed char* storage_{};
    mutable abi_type abi_argument_{};
};

template <>
struct Marshaller<const unsigned char*&>
{
    using cpp_type = const unsigned char*&;
    using abi_type = const unsigned char**;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept
    {
        storage_ = value;
        return &storage_;
    }

    cpp_type ConvertToCpp(abi_type value) noexcept
    {
        abi_argument_ = value;
        storage_ = *value;
        return storage_;
    }

    void Sync() noexcept
    {
        if (abi_argument_)
            *abi_argument_ = storage_;
    }

private:
    mutable const unsigned char* storage_{};
    mutable abi_type abi_argument_{};
};

template <class T>
struct Marshaller<T&&>
{
    using cpp_type = T&&;
    using value_marshaller_type = Marshaller<T>;
    using abi_type = typename value_marshaller_type::abi_type;

    abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return value_marshaller_.ConvertToAbi(value);
    }

    cpp_type ConvertToCpp(abi_type value) noexcept
    {
        storage_ = value_marshaller_.ConvertToCpp(value);
        return std::move(storage_);
    }

private:
    value_marshaller_type value_marshaller_;
    T storage_{};
};

template <class T>
struct Marshaller<T&>
{
    using cpp_type = T&;
    using value_abi_type = typename Marshaller<T>::abi_type;
    using abi_type = std::conditional_t<
        std::is_pointer_v<T>,
        value_abi_type*,
        std::conditional_t<std::is_pointer_v<value_abi_type>, value_abi_type, value_abi_type*>>;

    static_assert(
        detail::is_default_marshaller_v<Marshaller<T>> ||
        std::is_pointer_v<value_abi_type> ||
        detail::is_layout_compatible_pair_v<T, value_abi_type>,
        "Marshaller<T&> reinterpret_casts &value from T* to Marshaller<T>::abi_type*, which is "
        "only safe when the two layouts match. Marshaller<T> is a custom marshaller whose "
        "abi_type does not provably match T's layout here. Specialize Marshaller<T&> for T using "
        "ProjectedValueReferenceMarshallerBase, or ignore this member in the generator.");

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return reinterpret_cast<abi_type>(std::addressof(value));
    }

    constexpr cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return *reinterpret_cast<T*>(value);
    }
};

template <class T>
struct Marshaller<const T&>
{
    using cpp_type = const T&;
    using value_abi_type = typename Marshaller<T>::abi_type;
    using abi_type = std::conditional_t<
        std::is_pointer_v<T>,
        std::add_pointer_t<std::add_const_t<value_abi_type>>,
        std::conditional_t<
            std::is_pointer_v<value_abi_type>,
            std::add_pointer_t<std::add_const_t<std::remove_pointer_t<value_abi_type>>>,
            std::add_pointer_t<std::add_const_t<value_abi_type>>>>;

    static_assert(
        detail::is_default_marshaller_v<Marshaller<T>> ||
        std::is_pointer_v<value_abi_type> ||
        detail::is_layout_compatible_pair_v<T, value_abi_type>,
        "Marshaller<const T&> reinterpret_casts &value from const T* to "
        "const Marshaller<T>::abi_type*, which is only safe when the two layouts match. "
        "Marshaller<T> is a custom marshaller whose abi_type does not provably match T's layout "
        "here. Specialize Marshaller<const T&> for T using "
        "ProjectedValueConstReferenceMarshallerBase, or ignore this member in the generator.");

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return reinterpret_cast<abi_type>(const_cast<std::remove_reference_t<cpp_type>*>(std::addressof(value)));
    }

    constexpr cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return *reinterpret_cast<const T*>(value);
    }
};

template <class T>
    requires (!std::is_fundamental_v<T> && !std::is_enum_v<T> && !std::is_pointer_v<T>)
struct Marshaller<const T>
{
    using cpp_type = const T;
    using value_marshaller_type = Marshaller<T>;
    using reference_marshaller_type = Marshaller<const T&>;
    using marshaller_type = std::conditional_t<
        std::is_pointer_v<typename value_marshaller_type::abi_type>,
        reference_marshaller_type,
        value_marshaller_type>;
    using abi_type = typename marshaller_type::abi_type;

    abi_type ConvertToAbi(const cpp_type& value) noexcept
    {
        return marshaller_.ConvertToAbi(value);
    }

    cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return marshaller_.ConvertToCpp(value);
    }

private:
    marshaller_type marshaller_;
};

template <class T>
struct Marshaller<T*>
{
    using cpp_type = T*;
    using value_abi_type = typename Marshaller<T>::abi_type;
    using abi_type = std::conditional_t<
        std::is_pointer_v<T>,
        value_abi_type*,
        std::conditional_t<std::is_pointer_v<value_abi_type>, value_abi_type, value_abi_type*>>;

    static_assert(
        detail::is_default_marshaller_v<Marshaller<T>> ||
        std::is_pointer_v<value_abi_type> ||
        detail::is_layout_compatible_pair_v<T, value_abi_type>,
        "Marshaller<T*> reinterpret_casts a T* to Marshaller<T>::abi_type*, which is only safe "
        "when the two layouts match (and is wrong for an array of more than one element even "
        "when they do). Marshaller<T> is a custom marshaller whose abi_type does not provably "
        "match T's layout here. Specialize Marshaller<T*> for T using "
        "ProjectedValuePointerMarshallerBase (single element), ProjectedValueSpanMarshallerBase "
        "(an array, bound to Span<T>) or ignore this member in the generator.");

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return reinterpret_cast<abi_type>(value);
    }

    constexpr cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return reinterpret_cast<cpp_type>(value);
    }

};

template <class T>
struct Marshaller<const T*>
{
    using cpp_type = const T*;
    using value_abi_type = typename Marshaller<T>::abi_type;
    using abi_type = std::conditional_t<
        std::is_pointer_v<value_abi_type>,
        std::add_pointer_t<std::add_const_t<std::remove_pointer_t<value_abi_type>>>,
        std::add_pointer_t<std::add_const_t<value_abi_type>>>;

    static_assert(
        detail::is_default_marshaller_v<Marshaller<T>> ||
        std::is_pointer_v<value_abi_type> ||
        detail::is_layout_compatible_pair_v<T, value_abi_type>,
        "Marshaller<const T*> reinterpret_casts a const T* to const Marshaller<T>::abi_type*, "
        "which is only safe when the two layouts match (and is wrong for an array of more than "
        "one element even when they do). Marshaller<T> is a custom marshaller whose abi_type "
        "does not provably match T's layout here. Specialize Marshaller<const T*> for T using "
        "ProjectedValueConstPointerMarshallerBase (single element), "
        "ProjectedValueConstSpanMarshallerBase (an array, bound to ReadOnlySpan<T>) or ignore "
        "this member in the generator.");

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return reinterpret_cast<abi_type>(value);
    }

    constexpr cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return reinterpret_cast<cpp_type>(value);
    }
};

template <class T>
    requires (!std::is_fundamental_v<T> && !std::is_void_v<T>)
struct Marshaller<const T*&>
{
    using cpp_type = const T*&;
    using pointer_marshaller_type = Marshaller<const T*>;
    using abi_pointer_type = typename pointer_marshaller_type::abi_type;
    using abi_type = abi_pointer_type*;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept
    {
        abi_storage_ = pointer_marshaller_type{}.ConvertToAbi(value);
        return &abi_storage_;
    }

    cpp_type ConvertToCpp(abi_type value) noexcept
    {
        abi_argument_ = value;
        storage_ = pointer_marshaller_type{}.ConvertToCpp(*value);
        return storage_;
    }

    void Sync() noexcept
    {
        if (abi_argument_)
            *abi_argument_ = pointer_marshaller_type{}.ConvertToAbi(storage_);
    }

private:
    mutable const T* storage_{};
    mutable abi_pointer_type abi_storage_{};
    mutable abi_type abi_argument_{};
};

template <>
struct [[sharpcpp::managed_type(public_type = "global::System.ReadOnlySpan<byte>", undefaultable = true)]] Marshaller<const char*>
{
    using cpp_type = const char*;
    using abi_type = const char*;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return value;
    }
};

template <>
struct Marshaller<const char*&>
{
    using cpp_type = const char*&;
    using abi_type = const char*;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return value;
    }

    cpp_type ConvertToCpp(abi_type value) noexcept
    {
        storage_ = value;
        return storage_;
    }

private:
    mutable const char* storage_{};
};

template <>
struct [[sharpcpp::managed_type(public_type = "string", by_reference = true)]] Marshaller<const char**>
{
    using cpp_type = const char**;
    using abi_type = const char**;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return value;
    }
};

template <>
struct [[sharpcpp::managed_type(public_type = "global::System.IntPtr")]] Marshaller<const char* const*>
{
    using cpp_type = const char* const*;
    using abi_type = const char* const*;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return value;
    }
};

template <>
struct [[sharpcpp::managed_type(public_type = "string", by_reference = true)]] Marshaller<char**>
{
    using cpp_type = char**;
    using abi_type = char**;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return value;
    }
};

template <>
struct [[sharpcpp::managed_type(
    managed_base_type = "global::SharpCpp.Support.StringBase",
    public_type = "Std.String",
    managed_namespace = "Std",
    managed_wrapper_assembly = "SharpCpp.Std",
    managed_wrapper_provider_kind = "string",
    managed_wrapper_provider_namespace = "Std.Infrastructure",
    managed_wrapper_copyable = true
)]]
Marshaller<std::basic_string<char> > : OwnedStringMarshallerBase<std::basic_string<char>> {};

template <>
struct Marshaller<std::string&> : OwnedStringReferenceMarshallerBase<std::string> {};

template <>
struct Marshaller<const std::string&> : OwnedStringConstReferenceMarshallerBase<std::string> {};

template <>
struct Marshaller<const std::string*> : OwnedStringConstPointerMarshallerBase<std::string> {};

template <>
struct Marshaller<std::string*> : OwnedStringPointerMarshallerBase<std::string> {};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::System.ReadOnlySpan<byte>",
    managed_marshaller = "global::SharpCpp.Support.Utf8SpanAbiMarshaller",
    undefaultable = true
)]]
Marshaller<std::string_view> : StringViewMarshallerBase<std::string_view> {};

template <>
struct Marshaller<const std::string_view&> : StringViewConstReferenceMarshallerBase<std::string_view> {};

template <typename T, typename TAllocator>
struct [[sharpcpp::managed_type(
    public_type = "Std.Vector<{0}>",
    managed_base_type = "global::SharpCpp.Support.VectorBase<{0}>",
    managed_namespace = "Std",
    managed_wrapper_assembly = "SharpCpp.Std",
    managed_wrapper_provider_kind = "vector",
    managed_wrapper_copyable = true
)]]
Marshaller<std::vector<T, TAllocator>> : SequenceCollectionMarshallerBase<std::vector<T, TAllocator>, T> {};

template <typename T, typename TAllocator>
struct Marshaller<std::vector<T, TAllocator>&> : OwnedHandleReferenceMarshallerBase<std::vector<T, TAllocator>> {};

template <typename T, typename TAllocator>
struct Marshaller<const std::vector<T, TAllocator>&> : OwnedHandleConstReferenceMarshallerBase<std::vector<T, TAllocator>> {};

template <typename T, typename TAllocator>
struct Marshaller<std::vector<T, TAllocator>*> : OwnedHandlePointerMarshallerBase<std::vector<T, TAllocator>> {};

template <typename T, typename TAllocator>
struct Marshaller<const std::vector<T, TAllocator>*> : OwnedHandleConstPointerMarshallerBase<std::vector<T, TAllocator>> {};

template <typename T, std::size_t N>
struct [[sharpcpp::managed_type(
    public_type = "Std.Array<{0}>",
    managed_base_type = "global::SharpCpp.Support.ArrayBase<{0}>",
    managed_namespace = "Std",
    managed_wrapper_assembly = "SharpCpp.Std",
    managed_wrapper_provider_kind = "array",
    managed_wrapper_copyable = true,
    fixed_array = true
)]]
Marshaller<std::array<T, N>> : FixedArrayValueMarshallerBase<std::array<T, N>, N> {};

template <typename T, std::size_t N>
struct [[sharpcpp::managed_type(
    public_type = "SharpCpp.NativeArray<{0}>",
    managed_base_type = "global::SharpCpp.Support.ArrayBase<{0}>",
    managed_namespace = "SharpCpp",
    fixed_array = true,
    native_array = true
)]]
Marshaller<NativeArray<T, N>> {};

template <typename T, std::size_t N>
struct Marshaller<std::array<T, N>&> : FixedArrayReferenceMarshallerBase<std::array<T, N>, N> {};

template <typename T, std::size_t N>
struct Marshaller<const std::array<T, N>&> : FixedArrayConstReferenceMarshallerBase<std::array<T, N>, N> {};

template <typename T, std::size_t N>
struct Marshaller<std::array<T, N>*> : FixedArrayPointerMarshallerBase<std::array<T, N>, N> {};

template <typename T, std::size_t N>
struct Marshaller<const std::array<T, N>*> : FixedArrayConstPointerMarshallerBase<std::array<T, N>, N> {};

template <typename T, std::size_t N>
struct Marshaller<T[N]> : NativeFixedArrayMarshallerBase<T, N> {};

template <typename T, std::size_t N>
struct Marshaller<T (&)[N]> : FixedArrayReferenceMarshallerBase<T[N], N> {};

template <typename T, std::size_t N>
struct Marshaller<const T (&)[N]> : FixedArrayConstReferenceMarshallerBase<T[N], N> {};

template <typename T, typename TAllocator>
struct [[sharpcpp::managed_type(
    public_type = "Std.List<{0}>",
    managed_base_type = "global::SharpCpp.Support.ListBase<{0}>",
    managed_namespace = "Std",
    managed_wrapper_assembly = "SharpCpp.Std",
    managed_wrapper_provider_kind = "list",
    managed_wrapper_copyable = true
)]]
Marshaller<std::list<T, TAllocator>> : ListCollectionMarshallerBase<std::list<T, TAllocator>, T> {};

template <typename T, typename TAllocator>
struct Marshaller<std::list<T, TAllocator>&> : OwnedHandleReferenceMarshallerBase<std::list<T, TAllocator>> {};

template <typename T, typename TAllocator>
struct Marshaller<const std::list<T, TAllocator>&> : OwnedHandleConstReferenceMarshallerBase<std::list<T, TAllocator>> {};

template <typename T, typename TAllocator>
struct Marshaller<std::list<T, TAllocator>*> : OwnedHandlePointerMarshallerBase<std::list<T, TAllocator>> {};

template <typename T, typename TAllocator>
struct Marshaller<const std::list<T, TAllocator>*> : OwnedHandleConstPointerMarshallerBase<std::list<T, TAllocator>> {};

template <typename TKey, typename TValue, typename TCompare, typename TAllocator>
struct [[sharpcpp::managed_type(
    public_type = "Std.Map<{0}, {1}>",
    managed_base_type = "global::SharpCpp.Support.MapBase<{0}, {1}>",
    managed_base_type_constraints = "where {0} : notnull",
    managed_namespace = "Std",
    managed_wrapper_assembly = "SharpCpp.Std",
    managed_wrapper_provider_kind = "map",
    managed_wrapper_copyable = true
)]]
Marshaller<std::map<TKey, TValue, TCompare, TAllocator>> : MapCollectionMarshallerBase<std::map<TKey, TValue, TCompare, TAllocator>, TKey, TValue> {};

template <typename TKey, typename TValue, typename TCompare, typename TAllocator>
struct Marshaller<std::map<TKey, TValue, TCompare, TAllocator>&> : OwnedHandleReferenceMarshallerBase<std::map<TKey, TValue, TCompare, TAllocator>> {};

template <typename TKey, typename TValue, typename TCompare, typename TAllocator>
struct Marshaller<const std::map<TKey, TValue, TCompare, TAllocator>&> : OwnedHandleConstReferenceMarshallerBase<std::map<TKey, TValue, TCompare, TAllocator>> {};

template <typename TKey, typename TValue, typename TCompare, typename TAllocator>
struct Marshaller<std::map<TKey, TValue, TCompare, TAllocator>*> : OwnedHandlePointerMarshallerBase<std::map<TKey, TValue, TCompare, TAllocator>> {};

template <typename TKey, typename TValue, typename TCompare, typename TAllocator>
struct Marshaller<const std::map<TKey, TValue, TCompare, TAllocator>*> : OwnedHandleConstPointerMarshallerBase<std::map<TKey, TValue, TCompare, TAllocator>> {};

template <typename TKey, typename TValue, typename THash, typename TKeyEqual, typename TAllocator>
struct [[sharpcpp::managed_type(
    public_type = "Std.UnorderedMap<{0}, {1}>",
    managed_base_type = "global::SharpCpp.Support.UnorderedMapBase<{0}, {1}>",
    managed_base_type_constraints = "where {0} : notnull",
    managed_namespace = "Std",
    managed_wrapper_assembly = "SharpCpp.Std",
    managed_wrapper_provider_kind = "unordered_map",
    managed_wrapper_copyable = true
)]]
Marshaller<std::unordered_map<TKey, TValue, THash, TKeyEqual, TAllocator>> : MapCollectionMarshallerBase<std::unordered_map<TKey, TValue, THash, TKeyEqual, TAllocator>, TKey, TValue> {};

template <typename TKey, typename TValue, typename THash, typename TKeyEqual, typename TAllocator>
struct Marshaller<std::unordered_map<TKey, TValue, THash, TKeyEqual, TAllocator>&> : OwnedHandleReferenceMarshallerBase<std::unordered_map<TKey, TValue, THash, TKeyEqual, TAllocator>> {};

template <typename TKey, typename TValue, typename THash, typename TKeyEqual, typename TAllocator>
struct Marshaller<const std::unordered_map<TKey, TValue, THash, TKeyEqual, TAllocator>&> : OwnedHandleConstReferenceMarshallerBase<std::unordered_map<TKey, TValue, THash, TKeyEqual, TAllocator>> {};

template <typename TKey, typename TValue, typename THash, typename TKeyEqual, typename TAllocator>
struct Marshaller<std::unordered_map<TKey, TValue, THash, TKeyEqual, TAllocator>*> : OwnedHandlePointerMarshallerBase<std::unordered_map<TKey, TValue, THash, TKeyEqual, TAllocator>> {};

template <typename TKey, typename TValue, typename THash, typename TKeyEqual, typename TAllocator>
struct Marshaller<const std::unordered_map<TKey, TValue, THash, TKeyEqual, TAllocator>*> : OwnedHandleConstPointerMarshallerBase<std::unordered_map<TKey, TValue, THash, TKeyEqual, TAllocator>> {};

namespace detail
{
// Forces instantiation of a class type that is not a reference. MSVC reports C2526 for a C linkage function that
// returns a class that is not yet instantiated.
template <typename T, typename = void>
struct CompleteType { using type = T; };

template <typename T>
struct CompleteType<T, std::void_t<decltype(sizeof(std::enable_if_t<!std::is_reference_v<T>, T>))>> { using type = T; };

// Names a function type outside of any extern "C" scope. MSVC gives C linkage to a function type named by a typedef
// or alias (also one that comes from a class member) inside an extern "C" function body and then rejects class
// return types (C2526). Generated code names this holder class instead, and MarshallerInvoker unwraps it in C++ scope.
template <typename R, typename... A>
struct FunctionTypeHolder { using type = R(A...); };

template <typename T>
struct UnwrapFunctionType { using type = T; };

template <typename R, typename... A>
struct UnwrapFunctionType<FunctionTypeHolder<R, A...>> { using type = R(A...); };

template <typename T>
using UnwrapFunctionType_t = typename UnwrapFunctionType<T>::type;

// Selects the overload of a function name that matches FT and returns its address. Generated code calls this
// instead of a static_cast to a function pointer type, so the function type is never named in the extern "C" body.
template <typename FT>
constexpr std::add_pointer_t<UnwrapFunctionType_t<FT>> AsFunctionPointer(std::add_pointer_t<UnwrapFunctionType_t<FT>> pointer) noexcept
{
    return pointer;
}
}

template <class T>
using arg_t = typename detail::CompleteType<typename Marshaller<T>::abi_type>::type;

template <class T>
using field_t = T*;

template <class T>
using ParameterMarshaller = detail::SyncOnDestroyMarshaller<Marshaller<T>>;

namespace detail
{
template <class T, class = void>
struct IsDefaultMarshaller : std::false_type {};

template <class T>
struct IsDefaultMarshaller<T, std::void_t<typename Marshaller<T>::is_default_marshaller>> : std::true_type {};

template <class T>
using ConfiguredReferenceReturnMarshallerBase = std::conditional_t<
    std::is_pointer_v<typename Marshaller<remove_cvref_t<T>>::abi_type>,
    Marshaller<T>,
    Marshaller<remove_cvref_t<T>>>;

// A returned reference to a value the default marshaller passes by value (scalars, enums)
// crosses as a copy, like a configured value does; records keep the borrowed pointer.
template <class T>
using DefaultReferenceReturnMarshallerBase = std::conditional_t<
    std::is_lvalue_reference_v<T> && DefaultPassesByValue<remove_cvref_t<T>>,
    DefaultReturnMarshallerBase<remove_cvref_t<T>>,
    DefaultReturnMarshallerBase<T>>;

template <class T>
using ConfiguredReturnMarshallerBase = std::conditional_t<
    IsDefaultMarshaller<remove_cvref_t<T>>::value,
    DefaultReferenceReturnMarshallerBase<T>,
    std::conditional_t<
        std::is_lvalue_reference_v<T>,
        ConfiguredReferenceReturnMarshallerBase<T>,
        Marshaller<std::remove_cv_t<T>>>>;
}

template <class T>
struct ReturnMarshaller : detail::ConfiguredReturnMarshallerBase<T> {};

template <detail::IntrusiveReferenceTarget T>
struct ReturnMarshaller<T*>
{
    using cpp_type = T*;
    using abi_type = T*;

    abi_type ConvertToAbi(cpp_type value) noexcept
    {
        if (value != nullptr)
            value->AddRef();
        return value;
    }

    cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return value;
    }
};

template <>
struct ReturnMarshaller<void> : Marshaller<void> {};

template <>
struct [[sharpcpp::managed_type(return_ownership = "owning")]]
ReturnMarshaller<std::basic_string<char> > : OwnedStringReturnMarshallerBase<std::basic_string<char>> {};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::System.ReadOnlySpan<byte>",
    managed_marshaller = "global::SharpCpp.Support.Utf8SpanAbiReturnMarshaller"
)]] ReturnMarshaller<std::string_view> : Marshaller<std::string_view> {};

template <typename T, typename TAllocator>
struct [[sharpcpp::managed_type(return_ownership = "owning")]]
ReturnMarshaller<std::vector<T, TAllocator>> : OwnedHandleReturnMarshallerBase<std::vector<T, TAllocator>> {};

template <typename T, std::size_t N>
struct [[sharpcpp::managed_type(return_ownership = "owning")]]
ReturnMarshaller<std::array<T, N>> : FixedArrayReturnMarshallerBase<std::array<T, N>, N> {};

template <typename T, std::size_t N>
struct ReturnMarshaller<T[N]> : NativeFixedArrayMarshallerBase<T, N> {};

template <typename T, typename TAllocator>
struct [[sharpcpp::managed_type(return_ownership = "owning")]]
ReturnMarshaller<std::list<T, TAllocator>> : OwnedHandleReturnMarshallerBase<std::list<T, TAllocator>> {};

template <typename TKey, typename TValue, typename TCompare, typename TAllocator>
struct [[sharpcpp::managed_type(return_ownership = "owning")]]
ReturnMarshaller<std::map<TKey, TValue, TCompare, TAllocator>> : OwnedHandleReturnMarshallerBase<std::map<TKey, TValue, TCompare, TAllocator>> {};

template <typename TKey, typename TValue, typename THash, typename TKeyEqual, typename TAllocator>
struct [[sharpcpp::managed_type(return_ownership = "owning")]]
ReturnMarshaller<std::unordered_map<TKey, TValue, THash, TKeyEqual, TAllocator>> : OwnedHandleReturnMarshallerBase<std::unordered_map<TKey, TValue, THash, TKeyEqual, TAllocator>> {};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::System.ReadOnlySpan<byte>",
    managed_marshaller = "global::SharpCpp.Support.Utf8SpanReturnMarshaller"
)]] ReturnMarshaller<const char*> : Marshaller<const char*> {};

namespace detail
{
struct DefaultMarshallerComplexSmoke
{
    int Value{};
};

enum class DefaultMarshallerSignedEnumSmoke : std::int16_t
{
    Value = -1
};

enum class DefaultMarshallerUnsignedEnumSmoke : std::uint16_t
{
    Value = 1
};

static_assert(std::is_same_v<arg_t<DefaultMarshallerSignedEnumSmoke>, std::int32_t>);
static_assert(std::is_same_v<arg_t<DefaultMarshallerUnsignedEnumSmoke>, std::uint32_t>);
static_assert(std::is_same_v<field_t<DefaultMarshallerUnsignedEnumSmoke>, DefaultMarshallerUnsignedEnumSmoke*>);
}

namespace detail
{
template <typename T>
auto TryClone(T* instance, int) -> decltype(new T(*instance), (T*)nullptr)
{
    return new T(*instance);
}

template <typename T>
T* TryClone(T*, long)
{
    return nullptr;
}
}

template <class T>
using ret_t = typename detail::CompleteType<typename ReturnMarshaller<T>::abi_type>::type;

static_assert(std::is_same_v<arg_t<int>, int>);
static_assert(std::is_same_v<arg_t<int*>, int*>);
static_assert(std::is_same_v<arg_t<int**>, int**>);
static_assert(std::is_same_v<arg_t<detail::DefaultMarshallerComplexSmoke>, detail::DefaultMarshallerComplexSmoke*>);
static_assert(std::is_same_v<arg_t<detail::DefaultMarshallerComplexSmoke**>, detail::DefaultMarshallerComplexSmoke**>);
static_assert(std::is_same_v<arg_t<const detail::DefaultMarshallerComplexSmoke**>, const detail::DefaultMarshallerComplexSmoke**>);
static_assert(std::is_same_v<arg_t<const detail::DefaultMarshallerComplexSmoke&>, const detail::DefaultMarshallerComplexSmoke*>);
static_assert(std::is_same_v<arg_t<void* const&>, void* const*>);
static_assert(std::is_same_v<ret_t<int>, int>);
static_assert(std::is_same_v<ret_t<detail::DefaultMarshallerComplexSmoke>, detail::DefaultMarshallerComplexSmoke*>);
static_assert(std::is_same_v<ret_t<detail::DefaultMarshallerSignedEnumSmoke>, std::int32_t>);
static_assert(std::is_same_v<ret_t<detail::DefaultMarshallerUnsignedEnumSmoke>, std::uint32_t>);
}
