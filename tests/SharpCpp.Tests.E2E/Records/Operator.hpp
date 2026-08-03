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

struct operator_truth
{
    explicit operator_truth(bool value)
        : value_(value)
    {
    }

    bool operator!() const
    {
        return !value_;
    }

private:
    bool value_{};
};

struct pointer_operator_box
{
    explicit pointer_operator_box(int value)
        : value_(value)
    {
    }

    int get_value() const
    {
        return value_;
    }

    pointer_operator_box* operator+(int value) const
    {
        static pointer_operator_box result(0);
        result.value_ = value_ + value;
        return &result;
    }

    pointer_operator_box* operator-() const
    {
        static pointer_operator_box result(0);
        result.value_ = -value_;
        return &result;
    }

private:
    int value_{};
};
