#pragma once

#include <cstddef>
#include <functional>

#include "../Callback/EaFunction.h"
#include <memory>
#include <string>
#include <unordered_map>
#include <vector>

namespace container_aliases
{
/// A consumer vector with a defaulted allocator parameter the managed type hides.
template <class T, class TAllocator = void>
class vector
{
public:
    std::size_t size() const { return values_.size(); }
    void clear() { values_.clear(); }
    T& operator[](std::size_t index) { return values_[index]; }
    const T& operator[](std::size_t index) const { return values_[index]; }
    void push_back(const T& value) { values_.push_back(value); }
    auto begin() { return values_.begin(); }
    void insert(typename std::vector<T>::iterator position, const T& value) { values_.insert(position, value); }
    void erase(typename std::vector<T>::iterator position) { values_.erase(position); }

private:
    std::vector<T> values_;
};

template <class T>
struct base_vector3
{
    base_vector3() = default;

    base_vector3(T x, T y, T z)
        : x_(x), y_(y), z_(z)
    {
    }

    T x_{};
    T y_{};
    T z_{};
};

/// The value alias has a managed struct; an unrelated type elsewhere shares its short name.
using vector3 = base_vector3<float>;

namespace other
{
class vector3
{
};
}

class action_builder
{
public:
    float jump_by(const vector3& offset)
    {
        return offset.x_ + offset.y_ * 10 + offset.z_ * 100;
    }

    float move_by(const vector3& offset = vector3(1, 2, 3))
    {
        return jump_by(offset);
    }
};

class hull_builder
{
public:
    const vector<vector3>& get_vertices() const
    {
        return stored_;
    }

    void set_vertices(const vector<vector3>& vertices)
    {
        stored_ = vertices;
    }

    vector<vector3> stored_;
};

class object
{
public:
    explicit object(int id = 0)
        : id_(id)
    {
    }

    int get_id() const
    {
        return id_;
    }

private:
    int id_{};
};

class event_hub
{
public:
    int unsubscribe_except(const vector<object*>& exceptions)
    {
        int result = 0;
        for (std::size_t i = 0; i < exceptions.size(); ++i)
            result += exceptions[i]->get_id();
        return result;
    }
};

using int_vector = vector<int>;

class int_vector_consumer
{
public:
    int accept(const int_vector& items)
    {
        return static_cast<int>(items.size());
    }
};

struct string_hash
{
    unsigned value{};

    bool operator==(const string_hash& other) const = default;
};

struct string_hash_hasher
{
    std::size_t operator()(const string_hash& value) const
    {
        return value.value;
    }
};

template <class TKey>
struct map_hasher : std::hash<TKey>
{
};

template <>
struct map_hasher<string_hash> : string_hash_hasher
{
};

/// A hash map with dummy policy parameters, reached through an alias template.
template <class TKey, class TValue, class THash = int, class TPredicate = int, class TAllocator = int, bool CacheHashCode = true>
class hash_map : public std::unordered_map<TKey, TValue, map_hasher<TKey>>
{
};

template <class TKey, class TValue, class THash = int, class TPredicate = int, class TAllocator = int, bool CacheHashCode = true>
using unordered_map = hash_map<TKey, TValue, THash, TPredicate, TAllocator, CacheHashCode>;

class variant
{
public:
    variant() = default;

    explicit variant(int value)
        : value_(value)
    {
    }

    int get_value() const
    {
        return value_;
    }

private:
    int value_{};
};

using variant_map = unordered_map<string_hash, variant>;
using string_variant_map = unordered_map<std::string, variant>;

class data_hub
{
public:
    data_hub()
    {
        data_.emplace(string_hash{7}, variant(70));
        strings_.emplace("seven", variant(77));
    }

    const variant_map& get_event_data_map() const
    {
        return data_;
    }

    const string_variant_map& get_string_event_data_map() const
    {
        return strings_;
    }

    /// Callback arguments keep the map alias.
    int send(object* receiver, const std::function<int(object*, string_hash, variant_map&)>& handler)
    {
        return handler(receiver, string_hash{7}, data_);
    }

    int ea_send(object* receiver, const ea::function<int(object*, string_hash, variant_map&)>& handler)
    {
        return handler(receiver, string_hash{7}, data_);
    }

private:
    variant_map data_;
    string_variant_map strings_;
};

/// Alias templates over the allocator keep the configured string wrapper.
template <typename T>
using allocator = std::allocator<T>;

using string = std::basic_string<char, std::char_traits<char>, allocator<char>>;

inline namespace abi
{
class inline_value
{
public:
    int value() const
    {
        return 3;
    }
};
}

class loader
{
public:
    const string& get_error() const
    {
        return error_;
    }

    bool load(string* error)
    {
        *error = "alias";
        return true;
    }

    bool load_std(std::string* error)
    {
        *error = "std";
        return true;
    }

private:
    string error_{"none"};
};

namespace alpha
{
class context
{
public:
    int touch() const
    {
        return 1;
    }
};
}

namespace beta
{
class context
{
public:
    int touch() const
    {
        return 2;
    }
};
}

enum intersection
{
    OUTSIDE,
    INTERSECTS,
    INSIDE
};

inline intersection classify(int value)
{
    return value < 0 ? OUTSIDE : value == 0 ? INTERSECTS : INSIDE;
}
}
