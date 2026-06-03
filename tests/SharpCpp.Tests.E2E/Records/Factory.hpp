#pragma once

struct factory_box
{
    explicit factory_box(int value)
        : value_(value)
    {
    }

    int get_value()
    {
        return value_;
    }

    static factory_box* create(int value)
    {
        static factory_box box(value);
        box.value_ = value;
        return &box;
    }

    static int add(int lhs, int rhs)
    {
        return lhs + rhs;
    }

private:
    int value_{};
};
