#pragma once

#include <cstddef>
#include <iterator>
#include <list>
#include <map>
#include <string>
#include <type_traits>
#include <unordered_map>
#include <utility>
#include <vector>

namespace SharpCpp
{
template <class TString>
struct OwnedStringMarshallerBase
{
    using cpp_type = TString;
    using abi_type = TString*;

    constexpr abi_type ConvertToAbi(cpp_type& value) const noexcept
    {
        return &value;
    }

    constexpr cpp_type& ConvertToCpp(abi_type value) const noexcept
    {
        return *value;
    }

    [[sharpcpp::expose_member(shape = "protected_override", exposed = false)]]
    static std::size_t GetCountCore(const cpp_type& self)
    {
        return self.size();
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

    constexpr abi_type ConvertToAbi(cpp_type value) const noexcept
    {
        return const_cast<TString*>(&value);
    }

    constexpr cpp_type ConvertToCpp(abi_type value) const noexcept
    {
        return *value;
    }
};

template <class TString>
struct OwnedStringReferenceMarshallerBase
{
    using cpp_type = TString&;
    using abi_type = TString*;

    constexpr abi_type ConvertToAbi(cpp_type value) const noexcept
    {
        return &value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) const noexcept
    {
        return *value;
    }
};

template <class TString>
struct OwnedStringConstPointerMarshallerBase
{
    using cpp_type = const TString*;
    using abi_type = TString*;

    constexpr abi_type ConvertToAbi(cpp_type value) const noexcept
    {
        return const_cast<TString*>(value);
    }

    constexpr cpp_type ConvertToCpp(abi_type value) const noexcept
    {
        return value;
    }
};

template <class TString>
struct OwnedStringPointerMarshallerBase
{
    using cpp_type = TString*;
    using abi_type = TString*;

    constexpr abi_type ConvertToAbi(cpp_type value) const noexcept
    {
        return value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) const noexcept
    {
        return value;
    }
};

template <class TString>
struct OwnedStringReturnMarshallerBase
{
    using cpp_type = TString;
    using abi_type = TString*;

    abi_type ConvertToAbi(const cpp_type& value) const
    {
        return new TString(value);
    }

    abi_type ConvertToAbi(cpp_type&& value) const
    {
        return new TString(std::move(value));
    }

    cpp_type& ConvertToCpp(abi_type value) const noexcept
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
    abi_type ConvertToAbi(TSource&& value) const
    {
        return new TValue(std::forward<TSource>(value));
    }

    template <class TCpp = cpp_type>
    TCpp ConvertToCpp(abi_type value) const
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

    constexpr abi_type ConvertToAbi(cpp_type value) const noexcept
    {
        return &value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) const noexcept
    {
        return *value;
    }
};

template <class TValue>
struct OwnedHandleConstReferenceMarshallerBase
{
    using cpp_type = const TValue&;
    using abi_type = TValue*;

    constexpr abi_type ConvertToAbi(cpp_type value) const noexcept
    {
        return const_cast<TValue*>(&value);
    }

    constexpr cpp_type ConvertToCpp(abi_type value) const noexcept
    {
        return *value;
    }
};

template <class TValue>
struct OwnedHandlePointerMarshallerBase
{
    using cpp_type = TValue*;
    using abi_type = TValue*;

    constexpr abi_type ConvertToAbi(cpp_type value) const noexcept
    {
        return value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) const noexcept
    {
        return value;
    }
};

template <class TValue>
struct OwnedHandleConstPointerMarshallerBase
{
    using cpp_type = const TValue*;
    using abi_type = TValue*;

    constexpr abi_type ConvertToAbi(cpp_type value) const noexcept
    {
        return const_cast<TValue*>(value);
    }

    constexpr cpp_type ConvertToCpp(abi_type value) const noexcept
    {
        return value;
    }
};

template <class TValue>
struct OwnedHandleReturnMarshallerBase : OwnedHandleMarshallerBase<TValue>
{
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

template <class T>
struct Marshaller
{
    using cpp_type = T;
    using abi_type = T;

    constexpr abi_type ConvertToAbi(cpp_type value) const noexcept
    {
        return value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) const noexcept
    {
        return value;
    }
};

template <class TResult, class... TArgs>
struct Marshaller<TResult(TArgs...)>
{
    using cpp_type = TResult (*)(TArgs...);
    using abi_type = cpp_type;

    constexpr abi_type ConvertToAbi(cpp_type value) const noexcept
    {
        return value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) const noexcept
    {
        return value;
    }
};

template <class TResult, class... TArgs>
struct Marshaller<TResult (*)(TArgs...)>
{
    using cpp_type = TResult (*)(TArgs...);
    using abi_type = cpp_type;

    constexpr abi_type ConvertToAbi(cpp_type value) const noexcept
    {
        return value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) const noexcept
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

template <>
struct [[sharpcpp::managed_type(public_type = "global::System.IntPtr")]] Marshaller<void*>
{
    using cpp_type = void*;
    using abi_type = void*;

    constexpr abi_type ConvertToAbi(cpp_type value) const noexcept
    {
        return value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) const noexcept
    {
        return value;
    }
};

template <>
struct [[sharpcpp::managed_type(public_type = "global::System.IntPtr")]] Marshaller<const void*>
{
    using cpp_type = const void*;
    using abi_type = const void*;

    constexpr abi_type ConvertToAbi(cpp_type value) const noexcept
    {
        return value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) const noexcept
    {
        return value;
    }
};

template <class T>
struct Marshaller<T&>
{
    using cpp_type = T&;
    using abi_type = typename Marshaller<T>::abi_type*;

    constexpr abi_type ConvertToAbi(cpp_type value) const noexcept
    {
        return reinterpret_cast<abi_type>(std::addressof(value));
    }

    constexpr cpp_type ConvertToCpp(abi_type value) const noexcept
    {
        return *reinterpret_cast<std::remove_reference_t<cpp_type>*>(value);
    }
};

template <class T>
struct Marshaller<const T&>
{
    using cpp_type = const T&;
    using abi_type = const typename Marshaller<T>::abi_type*;

    constexpr abi_type ConvertToAbi(cpp_type value) const noexcept
    {
        return reinterpret_cast<abi_type>(const_cast<std::remove_reference_t<cpp_type>*>(std::addressof(value)));
    }

    constexpr cpp_type ConvertToCpp(abi_type value) const noexcept
    {
        return *reinterpret_cast<std::remove_const_t<std::remove_reference_t<cpp_type>>*>(const_cast<typename Marshaller<T>::abi_type*>(value));
    }
};

template <class T>
struct Marshaller<T*>
{
    using cpp_type = T*;
    using abi_type = typename Marshaller<T>::abi_type*;

    constexpr abi_type ConvertToAbi(cpp_type value) const noexcept
    {
        return reinterpret_cast<abi_type>(value);
    }

    constexpr cpp_type ConvertToCpp(abi_type value) const noexcept
    {
        return reinterpret_cast<cpp_type>(value);
    }

    constexpr cpp_type ConvertToCpp(void* value) const noexcept
    {
        return static_cast<cpp_type>(value);
    }
};

template <class T>
struct Marshaller<const T*>
{
    using cpp_type = const T*;
    using abi_type = const typename Marshaller<T>::abi_type*;

    constexpr abi_type ConvertToAbi(cpp_type value) const noexcept
    {
        return reinterpret_cast<abi_type>(value);
    }

    constexpr cpp_type ConvertToCpp(abi_type value) const noexcept
    {
        return reinterpret_cast<cpp_type>(const_cast<typename Marshaller<T>::abi_type*>(value));
    }
};

template <>
struct [[sharpcpp::managed_type(public_type = "global::System.ReadOnlySpan<byte>", undefaultable = true)]] Marshaller<const char*>
{
    using cpp_type = const char*;
    using abi_type = const char*;

    constexpr abi_type ConvertToAbi(cpp_type value) const noexcept
    {
        return value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) const noexcept
    {
        return value;
    }
};

template <>
struct [[sharpcpp::managed_type(public_type = "string")]] Marshaller<const char**>
{
    using cpp_type = const char**;
    using abi_type = const char**;

    constexpr abi_type ConvertToAbi(cpp_type value) const noexcept
    {
        return value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) const noexcept
    {
        return value;
    }
};

template <>
struct [[sharpcpp::managed_type(public_type = "string")]] Marshaller<char**>
{
    using cpp_type = char**;
    using abi_type = char**;

    constexpr abi_type ConvertToAbi(cpp_type value) const noexcept
    {
        return value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) const noexcept
    {
        return value;
    }
};

template <>
struct [[sharpcpp::managed_type(
    managed_base_type = "global::SharpCpp.Support.StringBase",
    public_type = "Std.String",
    managed_namespace = "Std"
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

template <typename T, typename TAllocator>
struct [[sharpcpp::managed_type(
    public_type = "Std.Vector<{0}>",
    managed_base_type = "global::SharpCpp.Support.VectorBase<{0}>",
    managed_namespace = "Std"
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

template <typename T, typename TAllocator>
struct [[sharpcpp::managed_type(
    public_type = "Std.List<{0}>",
    managed_base_type = "global::SharpCpp.Support.ListBase<{0}>",
    managed_namespace = "Std"
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
    managed_namespace = "Std"
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
    managed_namespace = "Std"
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

template <class T>
using arg_t = typename Marshaller<T>::abi_type;

template <class T>
using field_t = T*;

template <class T>
using ParameterMarshaller = Marshaller<T>;

template <class T>
struct ReturnMarshaller : Marshaller<T> {};

template <>
struct ReturnMarshaller<std::basic_string<char> > : OwnedStringReturnMarshallerBase<std::basic_string<char>> {};

template <typename T, typename TAllocator>
struct ReturnMarshaller<std::vector<T, TAllocator>> : OwnedHandleReturnMarshallerBase<std::vector<T, TAllocator>> {};

template <typename T, typename TAllocator>
struct ReturnMarshaller<std::list<T, TAllocator>> : OwnedHandleReturnMarshallerBase<std::list<T, TAllocator>> {};

template <typename TKey, typename TValue, typename TCompare, typename TAllocator>
struct ReturnMarshaller<std::map<TKey, TValue, TCompare, TAllocator>> : OwnedHandleReturnMarshallerBase<std::map<TKey, TValue, TCompare, TAllocator>> {};

template <typename TKey, typename TValue, typename THash, typename TKeyEqual, typename TAllocator>
struct ReturnMarshaller<std::unordered_map<TKey, TValue, THash, TKeyEqual, TAllocator>> : OwnedHandleReturnMarshallerBase<std::unordered_map<TKey, TValue, THash, TKeyEqual, TAllocator>> {};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::System.ReadOnlySpan<byte>",
    managed_marshaller = "global::SharpCpp.Support.Utf8SpanReturnMarshaller"
)]] ReturnMarshaller<const char*> : Marshaller<const char*> {};

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
using ret_t = typename ReturnMarshaller<T>::abi_type;
}
