#pragma once

#include <functional>

#include "../Callback/EaFunction.h"
#include <cstddef>
#include <memory>
#include <type_traits>

struct surface_payload
{
    explicit surface_payload(int value)
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

namespace e2e_std
{
template <typename T, std::size_t N>
struct
[[sharpcpp::instantiate("int", "3")]]
[[sharpcpp::instantiate("surface_payload*", "2")]]
array
{
    T& operator[](std::size_t index) { return values[index]; }
    const T& operator[](std::size_t index) const { return values[index]; }

    T values[N]{};
};

template <typename T>
class shared_array
{
public:
    shared_array() = default;

    explicit shared_array(T* values)
        : values_(values)
    {
    }

    T& operator[](std::size_t index) { return values_[index]; }
    const T& operator[](std::size_t index) const { return values_[index]; }

private:
    std::shared_ptr<T[]> values_;
};

template <typename T>
class provider_array
{
public:
    provider_array() = default;

    explicit provider_array(T* values)
        : values_(values)
    {
    }

    T& operator[](std::size_t index) { return values_[index]; }
    const T& operator[](std::size_t index) const { return values_[index]; }

private:
    std::shared_ptr<T[]> values_;
};
}

inline e2e_std::array<int, 3> make_surface_fixed_array(int seed)
{
    e2e_std::array<int, 3> result;
    result[0] = seed;
    result[1] = seed + 1;
    result[2] = seed + 2;
    return result;
}

inline int sum_surface_fixed_array(const e2e_std::array<int, 3>& value)
{
    return value[0] + value[1] + value[2];
}

inline int sum_surface_fixed_handles(const e2e_std::array<surface_payload*, 2>& value)
{
    return (value[0] != nullptr ? value[0]->get_value() : 0) +
        (value[1] != nullptr ? value[1]->get_value() : 0);
}

inline int sum_c_array_ref(const int (&values)[3])
{
    return values[0] + values[1] + values[2];
}

inline void fill_c_array_ref(unsigned (&out)[3], unsigned base)
{
    out[0] = base;
    out[1] = base + 1;
    out[2] = base + 2;
}

inline int sum_c_array_ref_handles(surface_payload* const (&values)[2])
{
    return (values[0] != nullptr ? values[0]->get_value() : 0) +
        (values[1] != nullptr ? values[1]->get_value() : 0);
}

inline e2e_std::shared_array<int> make_surface_shared_array(int seed)
{
    e2e_std::shared_array<int> result(new int[3]{});
    result[0] = seed;
    result[1] = seed + 1;
    result[2] = seed + 2;
    return result;
}

inline int sum_surface_shared_array(const e2e_std::shared_array<int>& value)
{
    return value[0] + value[1] + value[2];
}

inline int sum_surface_shared_handles(const e2e_std::shared_array<surface_payload*>& value)
{
    return (value[0] != nullptr ? value[0]->get_value() : 0) +
        (value[1] != nullptr ? value[1]->get_value() : 0);
}

inline e2e_std::provider_array<int> make_surface_provider_array(int seed)
{
    e2e_std::provider_array<int> result(new int[3]{});
    result[0] = seed;
    result[1] = seed + 1;
    result[2] = seed + 2;
    return result;
}

inline int sum_surface_provider_array(const e2e_std::provider_array<int>& value)
{
    return value[0] + value[1] + value[2];
}

inline int sum_surface_provider_handles(const e2e_std::provider_array<surface_payload*>& value)
{
    return (value[0] != nullptr ? value[0]->get_value() : 0) +
        (value[1] != nullptr ? value[1]->get_value() : 0);
}

struct surface_array_discovery_host
{
    explicit surface_array_discovery_host(int seed)
        : shared_values(new double[2]{static_cast<double>(seed + 4), static_cast<double>(seed + 5)})
        , provider_values(new float[2]{static_cast<float>(seed + 6), static_cast<float>(seed + 7)})
    {
        fixed_values[0] = seed;
        fixed_values[1] = seed + 1;
        fixed_values[2] = seed + 2;
    }

    int sum() const
    {
        return fixed_values[0] + fixed_values[1] + fixed_values[2] +
            static_cast<int>(shared_values[0] + shared_values[1]) +
            static_cast<int>(provider_values[0] + provider_values[1]);
    }

    e2e_std::array<int, 3> fixed_values;
    e2e_std::shared_array<double> shared_values;
    e2e_std::provider_array<float> provider_values;
};

template <typename T>
struct surface_array_callback_box
{
    explicit surface_array_callback_box(int seed)
    {
        if constexpr (std::is_same_v<T, e2e_std::array<int, 3>*>)
        {
            value_[0] = seed;
            value_[1] = seed + 1;
            value_[2] = seed + 2;
        }
        else if constexpr (std::is_same_v<T, e2e_std::shared_array<double>*>)
        {
            value_ = e2e_std::shared_array<double>(
                new double[2]{static_cast<double>(seed), static_cast<double>(seed + 1)});
        }
        else
        {
            static_assert(std::is_same_v<T, e2e_std::provider_array<float>*>);
            value_ = e2e_std::provider_array<float>(
                new float[2]{static_cast<float>(seed), static_cast<float>(seed + 1)});
        }
    }

    void add_to_first(int value)
    {
        value_[0] += value;
    }

    int sum() const
    {
        if constexpr (std::is_same_v<T, e2e_std::array<int, 3>*>)
            return value_[0] + value_[1] + value_[2];
        else
            return static_cast<int>(value_[0] + value_[1]);
    }

private:
    std::remove_pointer_t<T> value_{};
};

namespace surface_discovery
{
using fixed_view = const e2e_std::array<int, 3>*;
using shared_view = const e2e_std::shared_array<int>*;
using provider_view = const e2e_std::provider_array<int>*;
}

inline int invoke_surface_fixed_discovery_callback(
    const std::function<surface_array_callback_box<e2e_std::array<int, 3>*>*(
        surface_array_callback_box<e2e_std::array<int, 3>*>*,
        surface_array_callback_box<e2e_std::shared_array<double>*>*)>& callback,
    int seed)
{
    surface_array_callback_box<e2e_std::array<int, 3>*> fixed(seed);
    surface_array_callback_box<e2e_std::shared_array<double>*> shared(seed + 10);
    auto* returned = callback(&fixed, &shared);
    return returned != nullptr ? returned->sum() + shared.sum() : -1;
}

inline int invoke_surface_dynamic_discovery_callback(
    const std::function<surface_array_callback_box<e2e_std::shared_array<double>*>*(
        surface_array_callback_box<e2e_std::shared_array<double>*>*,
        surface_array_callback_box<e2e_std::provider_array<float>*>*)>& callback,
    int seed)
{
    surface_array_callback_box<e2e_std::shared_array<double>*> shared(seed);
    surface_array_callback_box<e2e_std::provider_array<float>*> provider(seed + 10);
    auto* returned = callback(&shared, &provider);
    return returned != nullptr ? returned->sum() + provider.sum() : -1;
}

inline int ea_invoke_surface_fixed_discovery_callback(
    const ea::function<surface_array_callback_box<e2e_std::array<int, 3>*>*(
        surface_array_callback_box<e2e_std::array<int, 3>*>*,
        surface_array_callback_box<e2e_std::shared_array<double>*>*)>& callback,
    int seed)
{
    surface_array_callback_box<e2e_std::array<int, 3>*> fixed(seed);
    surface_array_callback_box<e2e_std::shared_array<double>*> shared(seed + 10);
    auto* returned = callback(&fixed, &shared);
    return returned != nullptr ? returned->sum() + shared.sum() : -1;
}

inline int ea_invoke_surface_dynamic_discovery_callback(
    const ea::function<surface_array_callback_box<e2e_std::shared_array<double>*>*(
        surface_array_callback_box<e2e_std::shared_array<double>*>*,
        surface_array_callback_box<e2e_std::provider_array<float>*>*)>& callback,
    int seed)
{
    surface_array_callback_box<e2e_std::shared_array<double>*> shared(seed);
    surface_array_callback_box<e2e_std::provider_array<float>*> provider(seed + 10);
    auto* returned = callback(&shared, &provider);
    return returned != nullptr ? returned->sum() + provider.sum() : -1;
}

struct flattened_leaf
{
    int x{};
    int y{};
};

struct flattened_middle
{
    flattened_leaf back;
};

struct flattened_owner
{
    explicit flattened_owner(int seed)
        : state{{seed, seed + 1}}
        , anonymous_data{seed + 2, {seed + 3}}
        , choice{seed + 4}
        , sentinel(seed + 5)
    {
    }

    int total() const
    {
        return state.back.x + state.back.y + anonymous_data.value +
            anonymous_data.inner.deep + choice.number + sentinel;
    }

    flattened_middle state;
    struct
    {
        int value;
        struct
        {
            int deep;
        } inner;
    } anonymous_data;
    union
    {
        int number;
        unsigned bits;
    } choice;
    int sentinel{};
};

inline flattened_owner* create_flattened_owner(int seed)
{
    return new flattened_owner(seed);
}

struct surface_generic_host
{
    explicit surface_generic_host(int bias)
        : bias_(bias)
    {
    }

    template <typename T>
    [[sharpcpp::instantiate("int")]]
    [[sharpcpp::instantiate("float")]]
    T project(T value) const
    {
        return value + static_cast<T>(bias_);
    }

private:
    int bias_{};
};

template <typename T>
[[sharpcpp::instantiate("int")]]
[[sharpcpp::instantiate("surface_payload*")]]
T surface_identity(T value)
{
    return value;
}

template <typename T>
[[sharpcpp::instantiate("int")]]
[[sharpcpp::instantiate("surface_payload*")]]
void surface_consume(T value)
{
    (void)value;
}

template <typename T>
[[sharpcpp::instantiate("int")]]
[[sharpcpp::instantiate("surface_payload*")]]
int surface_measure(T value)
{
    if constexpr (std::is_pointer_v<T>)
        return value != nullptr ? value->get_value() : 0;
    else
        return static_cast<int>(value);
}

inline surface_payload* discover_surface_identity_payload(surface_payload* value)
{
    return surface_identity<surface_payload*>(value);
}

inline void discover_surface_consume_payload(surface_payload* value)
{
    surface_consume<surface_payload*>(value);
}

inline int discover_surface_measure_payload(surface_payload* value)
{
    return surface_measure<surface_payload*>(value);
}

extern "C"
{
struct coverage_linkage_box
{
    explicit coverage_linkage_box(int value)
        : value(value)
    {
    }

    int value{};
};

inline int coverage_linkage_add(int left, int right)
{
    return left + right;
}

inline coverage_linkage_box* create_coverage_linkage_box(int value)
{
    return new coverage_linkage_box(value);
}

inline int read_coverage_linkage_box(coverage_linkage_box* value)
{
    return value != nullptr ? value->value : 0;
}
}

struct collision_surface
{
    int value{};
    int amount{};
    inline static const int limit = 7;

    int Value() const { return value; }
    int Amount() const { return amount; }
    static int Limit() { return limit; }
};

inline collision_surface* create_collision_surface(int value, int amount)
{
    return new collision_surface{value, amount};
}

struct empty_surface_base
{
};

struct usable_surface_derived : empty_surface_base
{
    explicit usable_surface_derived(int value)
        : value_(value)
    {
    }

    int get_value() const { return value_; }

private:
    int value_{};
};

inline usable_surface_derived* create_usable_surface_derived(int value)
{
    return new usable_surface_derived(value);
}

struct unsupported_operator_surface
{
    int operator*(const unsupported_operator_surface&) const { return 0; }
    bool operator<=(const unsupported_operator_surface&) const { return false; }
    bool operator!=(const unsupported_operator_surface&) const { return false; }
};

struct [[sharpcpp::director]] unsupported_director_surface
{
    virtual ~unsupported_director_surface() = default;
    virtual void mutate(int& value) = 0;
};

template <typename T>
[[sharpcpp::instantiate("int")]]
int unsupported_hidden_template(int value)
{
    return value;
}

inline std::function<int(int)> unsupported_callback_return()
{
    return [](int value) { return value; };
}

inline void unsupported_callback_reference(std::function<int(int)>& callback)
{
    (void)callback;
}

inline ea::function<int(int)> ea_unsupported_callback_return()
{
    return [](int value) { return value; };
}

inline void ea_unsupported_callback_reference(ea::function<int(int)>& callback)
{
    (void)callback;
}

inline void unsupported_delegate_non_callback([[sharpcpp::delegate]] int value)
{
    (void)value;
}

inline void unsupported_delegate_name(
    [[sharpcpp::delegate("not valid")]] std::function<void(int)> callback)
{
    callback(1);
}

inline void unsupported_external_delegate_name(
    [[sharpcpp::delegate("global::")]] std::function<void(int)> callback)
{
    callback(1);
}

inline void unsupported_duplicate_delegate_a(
    [[sharpcpp::delegate("SharedSurfaceCallback")]] std::function<void(int)> callback)
{
    callback(1);
}

inline void unsupported_duplicate_delegate_b(
    [[sharpcpp::delegate("SharedSurfaceCallback")]] std::function<void(float)> callback)
{
    callback(1.0f);
}

inline void ea_unsupported_delegate_name(
    [[sharpcpp::delegate("not valid")]] ea::function<void(int)> callback)
{
    callback(1);
}

inline void ea_unsupported_external_delegate_name(
    [[sharpcpp::delegate("global::")]] ea::function<void(int)> callback)
{
    callback(1);
}

inline void ea_unsupported_duplicate_delegate_a(
    [[sharpcpp::delegate("EaSharedSurfaceCallback")]] ea::function<void(int)> callback)
{
    callback(1);
}

inline void ea_unsupported_duplicate_delegate_b(
    [[sharpcpp::delegate("EaSharedSurfaceCallback")]] ea::function<void(float)> callback)
{
    callback(1.0f);
}

inline long double unsupported_long_double(long double value)
{
    return value;
}

inline int coverage_surface_sentinel(int value)
{
    return value + 1;
}
