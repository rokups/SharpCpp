#pragma once

namespace injected_template
{
class slot_allocator
{
public:
    int capacity = 8;
};

/// Mirrors Jolt's LockFreeHashMap: a member alias of the injected class name, a nested
/// class befriending the template, and a constructor taking a reference.
template <class Key, class Value>
class slot_map
{
public:
    using MapType = slot_map<Key, Value>;

    explicit slot_map(slot_allocator& allocator)
        : allocator_(allocator)
    {
    }

    int capacity() const
    {
        return allocator_.capacity;
    }

    class entry
    {
    public:
        const Key& key() const { return key_; }

    private:
        template <class K, class V> friend class slot_map;

        Key key_{};
        Value value_{};
    };

private:
    slot_allocator& allocator_;
};

inline slot_map<int, float>* make_slot_map(slot_allocator& allocator)
{
    return new slot_map<int, float>(allocator);
}
}
