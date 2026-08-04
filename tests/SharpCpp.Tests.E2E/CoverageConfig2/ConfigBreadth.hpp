#pragma once

#include <functional>
#include <map>

namespace config_breadth2
{
struct config2_value
{
    config2_value() = default;

    explicit config2_value(int value)
        : value_(value)
    {
    }

    virtual ~config2_value() = default;

    virtual int get_value() const { return value_; }
    void set_value(int value) { value_ = value; }
    static int get_type_static() { return 67; }

protected:
    int value_{};
};

namespace qualified
{
using value_alias = config2_value;
}

namespace view = qualified;

using config2_value_alias = qualified::value_alias;
using config2_value_pointer_alias = view::value_alias*;

namespace generic_std
{
template <
    typename TKey,
    typename TValue,
    typename TCompare = std::less<TKey>,
    typename TAllocator = std::allocator<std::pair<const TKey, TValue>>>
using map = std::map<TKey, TValue, TCompare, TAllocator>;
}

namespace generic_std_view = generic_std;

using config2_value_map_alias =
    generic_std_view::map<int, config2_value_pointer_alias>;

struct config2_derived final : config2_value
{
    explicit config2_derived(int value)
        : config2_value(value)
    {
    }

    int get_value() const final { return value_ + 2; }
    static int get_type_static() { return 73; }
};

inline const qualified::value_alias& config2_alias_const_reference(
    const qualified::value_alias& value)
{
    return value;
}

inline config2_value_pointer_alias config2_alias_pointer(config2_value_pointer_alias value)
{
    return value;
}

inline config2_value_map_alias make_config2_value_map(config2_value_pointer_alias value)
{
    return { { 4, value } };
}

inline int read_config2_value_map(const config2_value_map_alias& values)
{
    return values.at(4)->get_value();
}

template <typename TValue, typename THidden = unsigned short>
struct
[[sharpcpp::instantiate("config_breadth2::config2_derived*")]]
config2_surface
{
    config2_surface() = default;

    explicit config2_surface(TValue value, int bias = 0)
        : value_(value)
        , bias_(bias)
    {
    }

    int read() const
    {
        return value_ == nullptr ? bias_ : value_->get_value() + bias_;
    }

    TValue echo(TValue value) const { return value; }
    void bump(int delta) { bias_ += delta; }
    static int get_type_static() { return 79; }

private:
    TValue value_{};
    int bias_{};
};

using config2_value_surface_alias = config2_surface<qualified::value_alias*, unsigned short>;

inline config2_value_surface_alias* make_config2_surface(
    config2_value_alias* value,
    int bias)
{
    return new config2_value_surface_alias(value, bias);
}

inline config2_surface<config2_derived*>* make_config2_derived_surface(
    config2_derived* value,
    int bias)
{
    return new config2_surface<config2_derived*>(value, bias);
}

template <typename TValue, typename THidden = unsigned long long>
struct config2_outer
{
    config2_outer() = default;

    explicit config2_outer(TValue value, int bias = 0)
        : value_(value)
        , bias_(bias)
    {
    }

    int read() const
    {
        return value_ == nullptr ? bias_ : value_->read() + bias_;
    }

    TValue get_value() const { return value_; }

private:
    TValue value_{};
    int bias_{};
};

using config2_nested_surface_alias =
    config2_outer<const config2_value_surface_alias*, unsigned long long>;

inline config2_nested_surface_alias make_config2_nested(
    const config2_value_surface_alias* value,
    int bias)
{
    return config2_nested_surface_alias(value, bias);
}

inline int read_config2_nested(const config2_nested_surface_alias& value)
{
    return value.read();
}

using config2_nested_callback =
    std::function<int(config2_value_surface_alias*, config2_value_alias*)>;

inline int invoke_config2_nested_callback(
    const config2_nested_callback& callback,
    config2_value_surface_alias* surface,
    config2_value_alias* value)
{
    return callback(surface, value) + 3;
}

using config2_repair_callback =
    std::function<int(config2_value_alias*, config2_value_alias*)>;

template <
    typename TValue,
    typename TCallback = config2_repair_callback,
    typename THidden = unsigned int>
struct config2_repair;

template <typename TValue, typename TCallback, typename THidden>
struct config2_repair;

using config2_repair_alias =
    config2_repair<config2_value_alias*, config2_repair_callback, unsigned int>;

namespace repair_view
{
using qualified_alias = config2_repair_alias;
}

template <typename TValue, typename TCallback, typename THidden>
struct
[[sharpcpp::instantiate("config_breadth2::config2_derived*")]]
config2_repair
{
    config2_repair() = default;

    [[sharpcpp::ignore("constructed through generated factory")]]
    config2_repair(TValue value, TCallback callback, int bias)
        : value_(value)
        , callback_(callback)
        , bias_(bias)
    {
    }

    int invoke(config2_value_alias* other) const
    {
        return callback_ ? callback_(value_, other) + bias_ : bias_;
    }

    TValue echo_value() const { return value_; }

private:
    TValue value_{};
    TCallback callback_{};
    int bias_{};
};

inline config2_repair_alias* make_config2_repair_alias(
    config2_value_alias* value,
    const config2_repair_callback& callback,
    int bias)
{
    return new config2_repair_alias(value, callback, bias);
}

inline const repair_view::qualified_alias& config2_repair_const_reference(
    const repair_view::qualified_alias& value)
{
    return value;
}

inline int invoke_config2_repair_alias(
    const repair_view::qualified_alias& value,
    config2_value_alias* other)
{
    return value.invoke(other) + 1;
}

template <typename TCallback>
int config2_invoke_specialized_callback(
    TCallback callback,
    config2_value_alias* value)
{
    config2_value_surface_alias surface(value, 9);
    return callback(&surface, value) + 7;
}

template int config2_invoke_specialized_callback<config2_nested_callback>(
    config2_nested_callback callback,
    config2_value_alias* value);

struct config2_render_base
{
protected:
    int inherited_values_[2]{ 2, 3 };
    int shadowed_value_{ 11 };
};

struct [[sharpcpp::director]] config2_render_record : config2_render_base
{
    config2_render_record() = default;

    [[sharpcpp::rename("DuplicateValue")]]
    int read_duplicate_value() const { return duplicate_value_; }

    [[sharpcpp::rename("ShadowedValue")]]
    int read_shadowed_value() const { return shadowed_value_; }

    int sum_inherited_values() const
    {
        return inherited_values_[0] + inherited_values_[1];
    }

    int duplicate_value_{ 13 };
};
}
