#pragma once

struct constructor_box
{
    explicit constructor_box(int value)
        : value_(value)
    {
    }

    int get_value()
    {
        return value_;
    }

private:
    int value_{};
};
