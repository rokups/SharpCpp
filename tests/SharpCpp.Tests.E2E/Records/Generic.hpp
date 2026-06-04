#pragma once

#include <SharpCpp/Marshaller.hpp>

template <typename T>
struct value_box
{
    explicit value_box(T value)
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

    static value_box<T>* create(T value)
    {
        return new value_box<T>(value);
    }

private:
    T value_{};
};

namespace SharpCpp
{
template <typename T>
struct [[sharpcpp::managed_type(public_type = "BoxOf<{0}>")]] Marshaller<value_box<T>*>
{
    using cpp_type = value_box<T>*;
    using abi_type = value_box<T>*;

    constexpr abi_type ConvertToAbi(cpp_type value) const noexcept
    {
        return value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) const noexcept
    {
        return value;
    }
};
}

inline value_box<int>* create_int_box(int value)
{
    return value_box<int>::create(value);
}

inline value_box<float>* create_float_box(float value)
{
    return value_box<float>::create(value);
}
