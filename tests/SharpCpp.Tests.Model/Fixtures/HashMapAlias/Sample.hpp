#pragma once

namespace eastl
{
template <typename T>
class basic_string {};

using string = basic_string<char>;

template <typename TKey, typename TValue, typename THash, typename TPredicate, typename TAllocator, bool CacheHashCode>
class hash_map {};

template <typename TKey, typename TValue, typename THash, typename TPredicate, typename TAllocator, bool CacheHashCode>
using unordered_map = hash_map<TKey, TValue, THash, TPredicate, TAllocator, CacheHashCode>;
}

namespace ea = eastl;

namespace Urho3D
{
struct StringHash {};
class Variant
{
public:
    Variant();
};

using VariantMap = ea::unordered_map<StringHash, Variant, int, int, int, true>;
using StringVariantMap = ea::unordered_map<ea::string, Variant, int, int, int, true>;

class EventHub
{
public:
    const VariantMap& GetEventDataMap() const;
    const StringVariantMap& GetStringEventDataMap() const;
};
}
