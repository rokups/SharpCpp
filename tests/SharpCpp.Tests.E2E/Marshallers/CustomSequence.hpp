#pragma once

#include <cstddef>
#include <string_view>
#include <vector>

namespace custom_sequence
{
using view = std::string_view;

struct id
{
    unsigned value{};
};

struct target
{
    int value{};
};

template <class T>
class ref
{
public:
    ref() = default;

    explicit ref(T* value)
        : value_(value)
    {
    }

    T* get() const
    {
        return value_;
    }

private:
    T* value_{};
};

/// A consumer-owned sequence configured as a managed list.
template <class T>
class array
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
    bool operator==(const array&) const = default;

    int implementation_detail{};

private:
    std::vector<T> values_;
};

using id_array = array<id>;
template <class T>
using ref_array = array<ref<T>>;
using target_array = ref_array<target>;

/// A record deriving from a configured sequence takes the sequence's managed type as base.
struct buffer : id_array
{
};

struct owner
{
    array<id> values;
    target_array references;
};

inline owner* create_owner()
{
    static target first{5};
    static target second{6};
    static owner value;
    value.values.clear();
    value.values.push_back(id{3});
    value.values.push_back(id{4});
    value.references.clear();
    value.references.push_back(ref<target>(&first));
    value.references.push_back(ref<target>(&second));
    return &value;
}

inline unsigned sum_ids(const array<id>& values)
{
    unsigned result = 0;
    for (std::size_t i = 0; i < values.size(); ++i)
        result += values[i].value;
    return result;
}

inline unsigned sum_buffer(const buffer& value)
{
    return sum_ids(value);
}

inline int consume_view(const view& value)
{
    return static_cast<int>(value.size());
}

/// Ownership configured on the returned reference type applies per instantiation.
template <class T>
class result
{
public:
    const T& get() const
    {
        return value_;
    }

    T value_{};
};

using target_result = result<ref<target>>;

inline target_result* create_target_result(int value)
{
    static target_result instance;
    instance.value_ = ref<target>(new target{value});
    return &instance;
}
}
