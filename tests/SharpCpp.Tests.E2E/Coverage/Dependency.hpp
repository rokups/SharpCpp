#pragma once

namespace sharpcpp_coverage
{
enum class dependency_state : unsigned int
{
    idle = 0,
    ready = 1
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

private:
    int value_{};
    dependency_state state_{dependency_state::idle};
};

using dependency_alias = dependency_value;

inline dependency_value* create_dependency_value(int value, dependency_state state)
{
    return new dependency_value(value, state);
}
}
