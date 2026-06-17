#pragma once

namespace eastl
{
template <typename TKey, typename TValue, typename THash, typename TPredicate, typename TAllocator, bool CacheHashCode>
class hash_map {};

template <typename TKey, typename TValue, typename THash, typename TPredicate, typename TAllocator, bool CacheHashCode>
using unordered_map = hash_map<TKey, TValue, THash, TPredicate, TAllocator, CacheHashCode>;
}

namespace ea = eastl;

using VariantMap = ea::unordered_map<int, int, int, int, int, true>;

class EventHub
{
public:
    const VariantMap& GetEventDataMap() const;
};
