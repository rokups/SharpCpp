#pragma once

#include <cstdint>

#define E2E_POINTER_ARRAY_OFFSET 9

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

inline int sum_keyword_pointer_values(const int* out, unsigned int count)
{
    return sum_pointer_values(out, count);
}

inline pointer_array_payload* get_pointer_payload_array()
{
    static pointer_array_payload values[] = {pointer_array_payload(17), pointer_array_payload(19)};
    return values;
}

inline void increment_pointer_values(int* values, unsigned int count)
{
    for (unsigned int i = 0; i < count; ++i)
        ++values[i];
}

inline void enumerate_pointer_values(
    unsigned int valueCapacityInput,
    unsigned int* valueCountOutput,
    int* values)
{
    constexpr int available[] = {7, 11, 13};
    *valueCountOutput = 3;
    const unsigned int count = valueCapacityInput < 3 ? valueCapacityInput : 3;
    for (unsigned int i = 0; i < count; ++i)
        values[i] = available[i];
}

enum class pointer_array_mode : unsigned int
{
    first = 3,
    second = 5,
};

inline void enumerate_pointer_modes(
    unsigned int modeCapacityInput,
    unsigned int* modeCountOutput,
    pointer_array_mode* modes)
{
    constexpr pointer_array_mode available[] = {pointer_array_mode::first, pointer_array_mode::second};
    *modeCountOutput = 2;
    const unsigned int count = modeCapacityInput < 2 ? modeCapacityInput : 2;
    for (unsigned int i = 0; i < count; ++i)
        modes[i] = available[i];
}

/// An OpenXR-style output struct filled through the two-call idiom: the caller passes the
/// first element of a NativeArray as the buffer.
struct pointer_array_record
{
    int id;
    float weight;
};

inline void enumerate_pointer_records(
    unsigned int recordCapacityInput,
    unsigned int* recordCountOutput,
    pointer_array_record* records)
{
    *recordCountOutput = 2;
    const unsigned int count = recordCapacityInput < 2 ? recordCapacityInput : 2;
    for (unsigned int i = 0; i < count; ++i)
        records[i] = pointer_array_record{static_cast<int>(i) + 4, 0.5f * static_cast<float>(i + 1)};
}

/// Method forms of the capacity/count record buffer idiom.
struct pointer_record_source
{
    explicit pointer_record_source(int first_id)
        : first_id_(first_id)
    {
    }

    void enumerate(
        unsigned int recordCapacityInput,
        unsigned int* recordCountOutput,
        pointer_array_record* records)
    {
        *recordCountOutput = 2;
        const unsigned int count = recordCapacityInput < 2 ? recordCapacityInput : 2;
        for (unsigned int i = 0; i < count; ++i)
            records[i] = pointer_array_record{first_id_ + static_cast<int>(i), 1.5f};
    }

    static void enumerate_static(
        unsigned int recordCapacityInput,
        unsigned int* recordCountOutput,
        pointer_array_record* records)
    {
        *recordCountOutput = 2;
        const unsigned int count = recordCapacityInput < 2 ? recordCapacityInput : 2;
        for (unsigned int i = 0; i < count; ++i)
            records[i] = pointer_array_record{100 + static_cast<int>(i), 2.5f};
    }

private:
    int first_id_{};
};

inline int sum_pointer_values_with_default(
    const int* values,
    unsigned int count,
    int offset = E2E_POINTER_ARRAY_OFFSET)
{
    return sum_pointer_values(values, count) + offset;
}

inline pointer_array_payload* first_pointer_payload(
    [[sharpcpp::in]] pointer_array_payload** values,
    unsigned int count)
{
    return count == 0 ? nullptr : values[0];
}

inline void choose_pointer_payload(
    [[sharpcpp::in]] pointer_array_payload** values,
    unsigned int count,
    [[sharpcpp::out]] pointer_array_payload*& selected)
{
    selected = count == 0 ? nullptr : values[count - 1];
}

inline pointer_array_payload* pointer_payload_or_fallback(
    [[sharpcpp::in]] pointer_array_payload** values,
    unsigned int count,
    pointer_array_payload* fallback)
{
    return count == 0 ? fallback : values[0];
}

inline int anchored_pointer_payload_sum(
    [[sharpcpp::in]] pointer_array_payload** values,
    unsigned int count,
    const pointer_array_payload* anchor)
{
    int result = anchor == nullptr ? 0 : anchor->get_value();
    for (unsigned int i = 0; i < count; ++i)
        result += values[i]->get_value();
    return result;
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
