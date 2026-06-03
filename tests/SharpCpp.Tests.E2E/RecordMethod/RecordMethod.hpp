#pragma once

struct counter_box
{
    int value_{};

    int get_value()
    {
        return value_;
    }

    void set_value(int value)
    {
        value_ = value;
    }

    int add(int delta)
    {
        value_ += delta;
        return value_;
    }

    bool is_value(int expected)
    {
        return value_ == expected;
    }
};

inline counter_box* create_counter_box(int value)
{
    static counter_box box {};
    box.set_value(value);
    return &box;
}
