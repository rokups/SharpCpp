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

    base_counter* echo_base(base_counter* value)
    {
        return value;
    }

    static base_counter* identity_base(base_counter* value)
    {
        return value;
    }

private:
    int bonus_{};
};

inline int read_base_counter(base_counter* value)
{
    return value->get_value();
}
