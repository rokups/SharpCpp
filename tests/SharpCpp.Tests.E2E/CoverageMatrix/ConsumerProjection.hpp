#pragma once

#include <array>
#include <cstddef>

struct consumer_projection_scalar
{
    explicit consumer_projection_scalar(int value = 0)
        : value(value)
    {
    }

    int value{};
};

struct consumer_projection_value
{
    explicit consumer_projection_value(int value = 0)
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

inline int consumer_projection_round_trip(int value)
{
    return value + 1;
}

inline int consumer_projection_function_pointer(int value)
{
    return value + 2;
}

inline int consumer_projection_adapt_int(int value)
{
    return value + 3;
}

inline void consumer_projection_adapt_void(int& value)
{
    value += 4;
}

inline consumer_projection_value consumer_projection_adapt_value(int value)
{
    return consumer_projection_value(value + 5);
}

using consumer_projection_callback = int (*)(int);

struct consumer_projection_function_holder
{
    explicit consumer_projection_function_holder(consumer_projection_callback callback = nullptr)
        : callback(callback)
    {
    }

    int apply(int value) const
    {
        return callback == nullptr ? value : callback(value);
    }

    consumer_projection_callback callback{};
};

struct consumer_projection_query_target;

struct consumer_projection_query_source
{
    explicit consumer_projection_query_source(int marker = 0)
        : marker_(marker)
    {
    }

    virtual ~consumer_projection_query_source() = default;

    int get_marker() const
    {
        return marker_;
    }

    [[sharpcpp::ignore("consumer query hook")]]
    void QueryInterface(int id, consumer_projection_query_source** result);

private:
    int marker_{};
};

struct consumer_projection_query_target final : consumer_projection_query_source
{
    explicit consumer_projection_query_target(int value)
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

inline void consumer_projection_query_source::QueryInterface(
    int id,
    consumer_projection_query_source** result)
{
    *result = id == 73
        ? static_cast<consumer_projection_query_source*>(new consumer_projection_query_target(41))
        : nullptr;
}

inline consumer_projection_query_source* create_consumer_projection_query_source()
{
    return new consumer_projection_query_source(7);
}

struct [[sharpcpp::director]] consumer_projection_director
{
protected:
    explicit consumer_projection_director(int first, int second)
        : values{first, second}
    {
    }

public:
    virtual ~consumer_projection_director() = default;

    int sum_values() const
    {
        return values[0] + values[1];
    }

    int invoke_transform(int value)
    {
        return transform(value);
    }

    int invoke_measure(int value)
    {
        return measure(value);
    }

    virtual int measure(
        int value,
        consumer_projection_scalar increment = consumer_projection_scalar{3})
    {
        return value + increment.value;
    }

protected:
    virtual int transform(int value) = 0;

    int values[2]{};
};

struct consumer_projection_host
{
    explicit consumer_projection_host(int value)
        : value_(value)
        , value_array{consumer_projection_value(value), consumer_projection_value(value + 1)}
        , primitive_array{value, value + 1}
    {
    }

    int consume_payload(consumer_projection_value* value) const
    {
        return value == nullptr ? -1 : value->get_value() + value_;
    }

    void touch_payload(consumer_projection_value* value) const
    {
        (void)value;
    }

    int sum_array(const std::array<int, 2>& values) const
    {
        return value_ + values[0] + values[1];
    }

    consumer_projection_scalar projected_value(int value) const
    {
        return consumer_projection_scalar(value + value_);
    }

    int sum_primitive_array() const
    {
        return primitive_array[0] + primitive_array[1];
    }

    static int static_consume_payload(consumer_projection_value* value)
    {
        return value == nullptr ? -1 : value->get_value();
    }

    static void static_touch_payload(consumer_projection_value* value)
    {
        (void)value;
    }

    static int static_sum_array(const std::array<int, 2>& values)
    {
        return values[0] + values[1];
    }

    static consumer_projection_scalar static_projected_value(int value)
    {
        return consumer_projection_scalar(value + 7);
    }

    template <typename T>
    [[sharpcpp::instantiate("consumer_projection_value*")]]
    T echo_generic(T value) const
    {
        return value;
    }

    template <typename T>
    [[sharpcpp::instantiate("consumer_projection_value*")]]
    void consume_generic(T value) const
    {
        (void)value;
    }

    template <typename T>
    [[sharpcpp::instantiate("int")]]
    consumer_projection_value* select_generic(T, consumer_projection_value* value) const
    {
        return value;
    }

    template <typename T>
    [[sharpcpp::instantiate("consumer_projection_value*")]]
    int measure_generic(T value) const
    {
        return value == nullptr ? -1 : value->get_value();
    }

    std::array<consumer_projection_value, 2> value_array;
    int primitive_array[2]{};

private:
    int value_{};
};

template <typename T>
struct [[sharpcpp::instantiate("int")]] consumer_projection_clone_box
{
    explicit consumer_projection_clone_box(T value)
        : value_(value)
    {
    }

    consumer_projection_clone_box(const consumer_projection_clone_box&) = default;

    consumer_projection_clone_box* clone() const
    {
        return new consumer_projection_clone_box(*this);
    }

    T get_value() const
    {
        return value_;
    }

private:
    T value_{};
};

inline int sum_consumer_projection_payloads(
    [[sharpcpp::in]] consumer_projection_value** values,
    unsigned int count,
    consumer_projection_scalar bias = consumer_projection_scalar{2})
{
    int result = bias.value;
    for (unsigned int index = 0; index < count; ++index)
    {
        if (values[index] != nullptr)
            result += values[index]->get_value();
    }
    return result;
}
