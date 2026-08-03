#pragma once

#include <array>
#include <functional>
#include <vector>

namespace sharpcpp_coverage
{
enum class dependency_state : unsigned int
{
    idle = 0,
    ready = 1
};

enum class [[sharpcpp::flags]] dependency_permissions : unsigned int
{
    none = 0u,
    read = 1u,
    write = 2u,
    all = 3u
};

struct dependency_value
{
    dependency_value(int value, dependency_state state)
        : value_(value), state_(state)
    {
    }

    dependency_value(const dependency_value&) = default;

    int get_value() const { return value_; }
    void set_value(int value) { value_ = value; }
    dependency_state get_state() const { return state_; }
    void set_state(dependency_state state) { state_ = state; }

    int exposed_value{};

private:
    int value_{};
    dependency_state state_{dependency_state::idle};
};

using dependency_alias = dependency_value;
using dependency_handle_alias = dependency_value*;
using dependency_state_alias = dependency_state;
using dependency_unsigned_alias = unsigned long long;
using dependency_fixed_alias = std::array<int, 3>;
using dependency_dynamic_alias = std::vector<int>;
using dependency_action = std::function<void()>;
using dependency_transform = std::function<int(int)>;

struct dependency_base
{
    explicit dependency_base(int value)
        : value_(value)
    {
    }

    virtual ~dependency_base() = default;

    virtual int get_base_value() const
    {
        return value_;
    }

protected:
    int value_{};
};

struct dependency_derived final : dependency_base
{
    explicit dependency_derived(int value)
        : dependency_base(value)
    {
    }

    int get_base_value() const final
    {
        return value_ + 1;
    }

    int get_derived_value() const
    {
        return value_ * 2;
    }
};

template <typename T>
struct
[[sharpcpp::instantiate("int")]]
dependency_box
{
    explicit dependency_box(T value)
        : value_(value)
    {
    }

    T get_value() const
    {
        return value_;
    }

    void set_value(T value)
    {
        value_ = value;
    }

private:
    T value_{};
};

using dependency_int_box_alias = dependency_box<int>;

struct dependency_primitives
{
    dependency_primitives() = default;

    bool echo_bool(bool value) const { return value; }
    signed char echo_int8(signed char value) const { return value; }
    unsigned char echo_uint8(unsigned char value) const { return value; }
    short echo_int16(short value) const { return value; }
    unsigned short echo_uint16(unsigned short value) const { return value; }
    int echo_int32(int value) const { return value; }
    unsigned int echo_uint32(unsigned int value) const { return value; }
    long long echo_int64(long long value) const { return value; }
    unsigned long long echo_uint64(unsigned long long value) const { return value; }
    float echo_single(float value) const { return value; }
    double echo_double(double value) const { return value; }
    long echo_native_int(long value) const { return value; }
    unsigned long echo_native_uint(unsigned long value) const { return value; }
    void* echo_pointer(void* value) const { return value; }
    int echo_reference(const int& value) const { return value; }

    void invoke_action(dependency_action callback) const
    {
        callback();
    }

    int invoke_transform(dependency_transform callback, int value) const
    {
        return callback(value);
    }

    void update([[sharpcpp::ref]] int* value) const
    {
        ++*value;
    }

    void write([[sharpcpp::out]] int* value) const
    {
        *value = 17;
    }

    static int static_value(int value)
    {
        return value + 1;
    }

protected:
    int protected_value(int value) const
    {
        return value + 2;
    }
};

inline dependency_value* create_dependency_value(int value, dependency_state state)
{
    return new dependency_value(value, state);
}

inline dependency_base* create_dependency_base(int value)
{
    return new dependency_base(value);
}

inline dependency_derived* create_dependency_derived(int value)
{
    return new dependency_derived(value);
}

inline dependency_box<int>* create_dependency_box(int value)
{
    return new dependency_box<int>(value);
}

}
