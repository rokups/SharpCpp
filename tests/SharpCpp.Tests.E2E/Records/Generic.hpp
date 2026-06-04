#pragma once

template <typename T>
struct [[sharpcpp::rename("BoxOf<{0}>")]] value_box
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

    template <typename U>
    U project(U scale) const
    {
        return static_cast<U>(value_) * scale;
    }

private:
    T value_{};
};

inline value_box<int>* create_int_box(int value)
{
    return value_box<int>::create(value);
}

inline value_box<float>* create_float_box(float value)
{
    return value_box<float>::create(value);
}
