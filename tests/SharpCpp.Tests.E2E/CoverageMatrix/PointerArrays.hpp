#pragma once

#include <cstdint>

struct pointer_array_payload
{
    explicit pointer_array_payload(int value)
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

inline int sum_pointer_values(const int* values, unsigned int count)
{
    int result = 0;
    for (unsigned int i = 0; i < count; ++i)
        result += values[i];
    return result;
}

inline void increment_pointer_values(int* values, unsigned int count)
{
    for (unsigned int i = 0; i < count; ++i)
        ++values[i];
}

inline pointer_array_payload* first_pointer_payload(
    [[sharpcpp::in]] pointer_array_payload** values,
    unsigned int count)
{
    return count == 0 ? nullptr : values[0];
}

inline int sum_pointer_addresses([[sharpcpp::in]] void* const* values, unsigned int count)
{
    int result = 0;
    for (unsigned int i = 0; i < count; ++i)
        result += static_cast<int>(reinterpret_cast<std::uintptr_t>(values[i]));
    return result;
}

inline int weighted_pointer_payload_sum(
    unsigned int count,
    [[sharpcpp::in]] pointer_array_payload* const* values,
    const int* weights)
{
    int result = 0;
    for (unsigned int i = 0; i < count; ++i)
        result += values[i]->get_value() * weights[i];
    return result;
}

struct pointer_array_host
{
    explicit pointer_array_host(int bias)
        : bias_(bias)
    {
    }

    int sum(const int* values, unsigned int count) const
    {
        return bias_ + sum_pointer_values(values, count);
    }

    void increment(int* values, unsigned int count) const
    {
        increment_pointer_values(values, count);
    }

    pointer_array_payload* first(unsigned int count, pointer_array_payload* values[]) const
    {
        return count == 0 ? nullptr : values[0];
    }

    static int weighted_sum(
        unsigned int count,
        [[sharpcpp::in]] pointer_array_payload* const* values,
        const int* weights)
    {
        return weighted_pointer_payload_sum(count, values, weights);
    }

private:
    int bias_{};
};
