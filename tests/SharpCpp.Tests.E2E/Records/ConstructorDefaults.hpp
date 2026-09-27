#pragma once

#include <cstring>

#define CONSTRUCTOR_DEFAULT_BREADTH_OFFSET 7

struct constructor_default_first
{
    explicit constructor_default_first(const char* label = "native")
        : value_(label == nullptr ? -1 : static_cast<int>(std::strlen(label)))
    {
    }

    int get_value() const { return value_; }

private:
    int value_{};
};

struct constructor_default_macro_before
{
    constructor_default_macro_before(
        int offset = CONSTRUCTOR_DEFAULT_BREADTH_OFFSET,
        const char* label = "abc")
        : value_(offset + (label == nullptr ? 0 : static_cast<int>(std::strlen(label))))
    {
    }

    int get_value() const { return value_; }

private:
    int value_{};
};

struct constructor_default_ref_before
{
    constructor_default_ref_before(
        [[sharpcpp::ref]] int* value,
        const char* label = "xy")
    {
        if (value != nullptr)
            ++*value;
        value_ = (value == nullptr ? 0 : *value) +
            (label == nullptr ? 0 : static_cast<int>(std::strlen(label)));
    }

    int get_value() const { return value_; }

private:
    int value_{};
};

