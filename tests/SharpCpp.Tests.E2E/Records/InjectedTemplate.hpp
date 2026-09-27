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

    /// A default that depends on a template argument cannot be expressed in C#.
    int scaled(int factor = sizeof(Value) * 2) const
    {
        return allocator_.capacity * factor;
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

struct point
{
    point(float x, float y)
        : x(x), y(y)
    {
    }

    float x;
    float y;
};

/// A nested record whose constructor default names a namespace-level type unqualified.
struct settings
{
    struct vertex
    {
        explicit vertex(const point& position, point velocity = point(0, 0), float mass = 1.0f)
            : sum(position.x + position.y + velocity.x + velocity.y + mass)
        {
        }

        float sum;
    };
};
}
