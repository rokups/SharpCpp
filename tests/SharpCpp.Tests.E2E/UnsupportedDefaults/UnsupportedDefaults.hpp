#pragma once

#include <functional>
#include <vector>

#define E2E_UNSUPPORTED_DEFAULT_SINGLE 42.0f
#define E2E_UNSUPPORTED_DEFAULT_A 10.0f
#define E2E_UNSUPPORTED_DEFAULT_B 20.0f
#define E2E_UNSUPPORTED_DEFAULT_OFFSET 4
#define E2E_UNSUPPORTED_DEFAULT_DELTA 6
#define E2E_UNSUPPORTED_DEFAULT_RHS 7
#define E2E_UNSUPPORTED_DEFAULT_ALTERNATING_OFFSET 8
#define E2E_UNSUPPORTED_DEFAULT_ALTERNATING_DELTA 6
enum class unsupported_default_mode : unsigned short
{
    idle = 0,
    high = 0xe001
};

/// Returns 0 when x is less than the threshold (defaults to 42.0f).
inline int check_threshold_single_default(float x, float threshold = E2E_UNSUPPORTED_DEFAULT_SINGLE)
{
    return x < threshold ? 0 : 1;
}

/// Returns 0 when a < b (defaults a=10.0f, b=20.0f).
inline int check_two_defaults_cascade(float x, float a = E2E_UNSUPPORTED_DEFAULT_A, float b = E2E_UNSUPPORTED_DEFAULT_B)
{
    (void)x;
    return a < b ? 0 : 1;
}

/// Plain function with no unexpressible defaults (regression guard).
inline int check_plain_no_defaults(int value)
{
    return value * 2;
}

inline int check_mixed_defaults(int seed, int factor = 2, int offset = E2E_UNSUPPORTED_DEFAULT_OFFSET, int tail = 5)
{
    return (seed * 1000) + (factor * 100) + (offset * 10) + tail;
}

inline int check_alternating_defaults(
    int seed,
    int factor = 2,
    int offset = E2E_UNSUPPORTED_DEFAULT_ALTERNATING_OFFSET,
    int middle = 7,
    int delta = E2E_UNSUPPORTED_DEFAULT_ALTERNATING_DELTA,
    int tail = 5)
{
    return (seed * 100000) + (factor * 10000) + (offset * 1000) + (middle * 100) + (delta * 10) + tail;
}

inline int unsupported_default_state;

inline void set_unsupported_default_state(int value = E2E_UNSUPPORTED_DEFAULT_OFFSET)
{
    unsupported_default_state = value;
}

inline int get_unsupported_default_state()
{
    return unsupported_default_state;
}

inline int unsupported_default_with_output(
    int seed,
    int offset = E2E_UNSUPPORTED_DEFAULT_OFFSET,
    const void* filter = nullptr,
    [[sharpcpp::out]] int* output = nullptr)
{
    if (output != nullptr)
        *output = seed + offset;
    return seed + offset + (filter == nullptr ? 1 : 2);
}

inline void set_unsupported_default_with_output(
    int seed,
    int offset = E2E_UNSUPPORTED_DEFAULT_OFFSET,
    [[sharpcpp::out]] int* output = nullptr)
{
    unsupported_default_state = seed + offset;
    if (output != nullptr)
        *output = unsupported_default_state + 1;
}

struct unsupported_default_result
{
    explicit unsupported_default_result(int value)
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

inline unsupported_default_result* make_unsupported_default_result(
    int value = E2E_UNSUPPORTED_DEFAULT_OFFSET)
{
    return new unsupported_default_result(value);
}

struct unsupported_default_constructor_payload
{
    explicit unsupported_default_constructor_payload(int value)
        : value(value)
    {
    }

    int value{};
};

struct unsupported_default_constructor_collision
{
    explicit unsupported_default_constructor_collision(unsupported_default_constructor_payload* payload)
        : value_(payload == nullptr ? -1 : payload->value)
    {
    }

    unsupported_default_constructor_collision(
        const unsupported_default_constructor_payload* payload,
        int offset = E2E_UNSUPPORTED_DEFAULT_OFFSET,
        [[sharpcpp::out]] int* observed = nullptr)
        : value_((payload == nullptr ? -1 : payload->value) + offset)
    {
        if (observed != nullptr)
            *observed = value_ + 1;
    }

    int get_value() const
    {
        return value_;
    }

private:
    int value_{};
};

struct unsupported_default_counter
{
    explicit unsupported_default_counter(int seed, int factor = 2, int offset = E2E_UNSUPPORTED_DEFAULT_OFFSET, int tail = 5)
        : value_((seed * 1000) + (factor * 100) + (offset * 10) + tail)
    {
    }

    int get_value() const
    {
        return value_;
    }

    int combine(int amount, int bias = 3, int delta = E2E_UNSUPPORTED_DEFAULT_DELTA, int scale = 2) const
    {
        return value_ + (amount * 1000) + (bias * 100) + (delta * 10) + scale;
    }

    static int add(int lhs, int rhs = E2E_UNSUPPORTED_DEFAULT_RHS, int tail = 5)
    {
        return (lhs * 100) + (rhs * 10) + tail;
    }

private:
    int value_{};
};

struct unsupported_default_enum_box
{
    explicit unsupported_default_enum_box(
        unsupported_default_mode mode = (unsupported_default_mode::high))
        : mode_(mode)
    {
    }

    unsupported_default_mode get_mode() const
    {
        return mode_;
    }

private:
    unsupported_default_mode mode_{};
};

struct unsupported_default_item
{
    int value{};
};

using unsupported_default_callback = std::function<int(int)>;

/// Pointer-to-record default null followed by a callback default C# cannot express.
inline int unsupported_default_record_pointer_then_callback(
    int seed,
    const unsupported_default_item* item = nullptr,
    const unsupported_default_callback& callback = {})
{
    const auto base = seed + (item == nullptr ? -1 : item->value);
    return callback ? callback(base) : base;
}

inline int unsupported_default_vector_pointer_then_callback(
    int seed,
    const std::vector<int>* values = nullptr,
    const unsupported_default_callback& callback = {})
{
    const auto base = seed + (values == nullptr ? -1 : static_cast<int>(values->size()));
    return callback ? callback(base) : base;
}

/// Two consecutive null-default record pointers followed by an unexpressible callback default.
inline int unsupported_default_two_record_pointers_then_callback(
    int seed,
    const unsupported_default_item* a = nullptr,
    const unsupported_default_item* b = nullptr,
    const unsupported_default_callback& callback = {})
{
    const auto base = seed + (a == nullptr ? 0 : a->value * 10) + (b == nullptr ? 0 : b->value * 100);
    return callback ? callback(base) : base;
}

/// A null-default callback followed by a null-default pointer and an unexpressible default.
inline int unsupported_default_callback_then_pointer(
    int seed,
    std::function<int(int)> first = nullptr,
    const unsupported_default_item* item = nullptr,
    int tail = E2E_UNSUPPORTED_DEFAULT_OFFSET)
{
    const auto base = seed + (item == nullptr ? 0 : item->value * 10) + tail;
    return first ? first(base) : base;
}
