#pragma once

#include <functional>
#include <vector>

namespace pipeline_breadth
{
inline int pipeline_round_trip(int value)
{
    return value + 1;
}

struct [[sharpcpp::ignore("pipeline degradation coverage")]] pipeline_hidden_base
{
    explicit pipeline_hidden_base(int value)
        : hidden_(value)
    {
    }

private:
    int hidden_{};
};

struct pipeline_usable_base
{
    explicit pipeline_usable_base(int value)
        : value_(value)
    {
    }

    int get_base_value() const { return value_; }

protected:
    int value_{};
};

struct pipeline_rebased final : pipeline_hidden_base, pipeline_usable_base
{
    explicit pipeline_rebased(int value)
        : pipeline_hidden_base(value + 100)
        , pipeline_usable_base(value)
    {
    }

    int get_total() const { return get_base_value() + 2; }
};

inline pipeline_rebased* create_pipeline_rebased(int value)
{
    return new pipeline_rebased(value);
}

struct pipeline_orphan_base
{
    explicit pipeline_orphan_base(int value)
        : value_(value)
    {
    }

    int get_value() const { return value_; }

private:
    int value_{};
};

struct pipeline_accessor_probe
{
    explicit pipeline_accessor_probe(int value)
        : value_(value)
    {
    }

    int get_native_value() const { return value_; }
    void set_native_value(int value) { value_ = value; }

private:
    int value_{};
};

struct pipeline_plain_value
{
    int value{};
};

struct pipeline_field_host
{
    explicit pipeline_field_host(int value)
        : source(value)
        , reference(source)
        , nested{value + 1}
    {
    }

    int read() const { return reference + nested.value; }

    int source{};
    int& reference;
    std::function<void(int)> callback;
    pipeline_plain_value nested;
};

inline pipeline_field_host* create_pipeline_field_host(int value)
{
    return new pipeline_field_host(value);
}

struct pipeline_bad_managed
{
    int value{};
};

inline int read_pipeline_bad_managed(pipeline_bad_managed value)
{
    return value.value;
}

struct pipeline_incomplete;

struct [[sharpcpp::ignore("pipeline alias coverage")]] pipeline_ignored_record
{
    int value{};
};

enum class [[sharpcpp::ignore("pipeline alias coverage")]] pipeline_ignored_enum
{
    value
};

using pipeline_incomplete_alias = pipeline_incomplete*;
using pipeline_ignored_record_alias = pipeline_ignored_record*;
using pipeline_ignored_enum_alias = pipeline_ignored_enum;
using pipeline_unsupported_primitive_alias = long double;
using pipeline_nested_unsupported_alias = std::vector<long double>;

inline void reject_nested_callback_return(
    std::function<std::function<int(int)>()> callback)
{
    (void)callback;
}

inline void reject_nested_callback_parameter(
    std::function<void(std::function<int(int)>)> callback)
{
    (void)callback;
}

template <typename T>
[[sharpcpp::instantiate("pipeline_breadth::pipeline_bad_managed")]]
T invalid_managed_name_instantiation(T value)
{
    return value;
}

struct [[sharpcpp::director]] pipeline_unsupported_director
{
    virtual ~pipeline_unsupported_director() = default;
    virtual void update([[sharpcpp::ref]] int* value) = 0;
};
}
