#pragma once

namespace e2e::records
{
struct operator_value
{
    int value{};
};
}

struct operator_box
{
    explicit operator_box(int value)
        : value_(value)
    {
    }

    int get_value() const
    {
        return value_;
    }

    operator int() const
    {
        return value_;
    }

    explicit operator bool() const
    {
        return value_ != 0;
    }

    operator e2e::records::operator_value() const
    {
        return { value_ };
    }

    int operator+(int value) const
    {
        return value_ + value;
    }

    int operator-() const
    {
        return -value_;
    }

    bool operator==(const operator_box& rhs) const
    {
        return value_ == rhs.value_;
    }

    bool operator!=(const operator_box& rhs) const
    {
        return value_ != rhs.value_;
    }

    bool operator<(const operator_box& rhs) const
    {
        return value_ < rhs.value_;
    }

    bool operator>(const operator_box& rhs) const
    {
        return value_ > rhs.value_;
    }

private:
    int value_{};
};
