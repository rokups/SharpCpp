#pragma once

#include "CrossHeaderA.hpp"

namespace cross_header
{
struct cross_header_payload
{
    explicit cross_header_payload(int value)
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

inline cross_header_payload* make_cross_header_payload(int value)
{
    return new cross_header_payload(value);
}

inline int read_cross_header_payload(const cross_header_payload* value)
{
    return value == nullptr ? -1 : value->get_value();
}
}
