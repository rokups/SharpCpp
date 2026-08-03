#pragma once

extern "C"
{
struct linkage_record
{
    explicit linkage_record(int value)
        : value_(value)
    {
    }

    int get_value() const
    {
        return value_;
    }

private:
    int value_{};
};

enum class linkage_mode
{
    add = 1,
    multiply = 2
};

inline int linkage_apply(linkage_mode mode, int left, int right)
{
    return mode == linkage_mode::add ? left + right : left * right;
}

namespace linkage_nested
{
struct nested_record
{
    explicit nested_record(int value)
        : value_(value)
    {
    }

    int doubled() const
    {
        return value_ * 2;
    }

private:
    int value_{};
};

inline int nested_add(int value)
{
    return value + 3;
}
}

extern "C++"
{
template <typename T>
struct [[sharpcpp::instantiate("int")]] linkage_box
{
    explicit linkage_box(T value)
        : value_(value)
    {
    }

    T get_value() const
    {
        return value_;
    }

private:
    T value_{};
};

template <typename T>
[[sharpcpp::instantiate("int")]]
T linkage_identity(T value)
{
    return value;
}

template struct linkage_box<long>;
}
}
