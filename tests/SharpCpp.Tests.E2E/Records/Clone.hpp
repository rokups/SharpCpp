#pragma once

struct clone_counter
{
    explicit clone_counter(int value)
        : value_(value)
    {
    }

    clone_counter(const clone_counter&) = default;

    int get_value() const
    {
        return value_;
    }

    void set_value(int value)
    {
        value_ = value;
    }

private:
    int value_{};
};

struct clone_collision_counter
{
    explicit clone_collision_counter(int value)
        : value_(value)
    {
    }

    clone_collision_counter(const clone_collision_counter&) = default;

    int clone() const { return value_ + 1; }
    int get_value() const { return value_; }

private:
    int value_{};
};
