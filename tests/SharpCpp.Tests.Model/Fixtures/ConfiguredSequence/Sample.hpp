#pragma once

#include <cstddef>
#include <functional>
#include <string_view>
#include <vector>

namespace sample
{
using view = std::string_view;

struct id
{
    unsigned value{};
};

struct target {};
struct value {};

template <class T>
class ref
{
public:
    T* get() const;
};

template <class T>
class result
{
public:
    const T& get() const;
};

using target_result = result<ref<target>>;
using value_result = result<value>;

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

struct buffer : id_array
{
};

struct owner
{
    array<id> values;
    target_array references;
    target_result target_value;
    value_result plain_value;
};

template <class T, unsigned N>
class static_buffer
{
public:
    unsigned size() const { return N; }
    T values[N];
};

struct bounded_owner
{
    static_buffer<id, 4> values;
};

template <class T>
struct callback_box {};

void consume_callback(const std::function<callback_box<static_buffer<int, 4>*>*(
    callback_box<static_buffer<int, 4>*>*)>& callback);

void consume(const view& value);
}
