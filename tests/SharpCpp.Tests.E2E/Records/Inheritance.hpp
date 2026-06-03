#pragma once

struct base_counter
{
    explicit base_counter(int value)
        : value_(value)
    {
    }

    int get_value() const
    {
        return value_;
    }

protected:
    int value_{};
};

struct derived_counter : base_counter
{
    derived_counter(int value, int bonus)
        : base_counter(value)
        , bonus_(bonus)
    {
    }

    int get_bonus() const
    {
        return bonus_;
    }

    int add_all() const
    {
        return value_ + bonus_;
    }

private:
    int bonus_{};
};
