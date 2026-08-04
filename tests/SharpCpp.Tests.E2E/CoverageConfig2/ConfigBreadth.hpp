#pragma once

#include <functional>

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

template <typename TValue, typename THidden = unsigned short>
struct config2_surface
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
}
