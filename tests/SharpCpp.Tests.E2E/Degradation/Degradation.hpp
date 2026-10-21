#pragma once

#include <functional>
#include <string_view>
#include <vector>

#include "../Callback/EaFunction.h"

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

struct pipeline_configured_unsupported
{
    int value{};
};

template <typename T>
struct pipeline_configured_box
{
    T value{};
};

struct pipeline_nonflattenable_value
{
    explicit pipeline_nonflattenable_value(int value)
        : value_(value)
    {
    }

    int get_value() const { return value_; }

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
        , record_value(value + 2)
        , configured{value + 3}
    {
    }

    int read() const
    {
        return reference + nested.value + record_value.get_value() + configured.value;
    }

    int source{};
    int& reference;
    std::function<void(int)> callback;
    ea::function<void(int)> ea_callback;
    pipeline_plain_value nested;
    pipeline_nonflattenable_value record_value;
    pipeline_configured_unsupported configured;
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
using pipeline_configured_unsupported_alias = pipeline_configured_unsupported;
using pipeline_missing_handle_box_alias = pipeline_configured_box<pipeline_ignored_enum*>;

inline void reject_configured_unsupported(pipeline_configured_unsupported value)
{
    (void)value;
}

inline void reject_missing_external_handle(pipeline_configured_box<pipeline_ignored_enum*> value)
{
    (void)value;
}

inline void reject_nested_ignored_record(
    pipeline_configured_box<pipeline_ignored_record*> value)
{
    (void)value;
}

inline void reject_ref_struct_argument(pipeline_configured_box<std::string_view> value)
{
    (void)value;
}

inline void reject_unsupported_generic_container(pipeline_configured_box<int> value)
{
    (void)value;
}

inline void reject_configured_callback_return(
    std::function<pipeline_configured_unsupported()> callback)
{
    (void)callback;
}

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

inline void reject_ea_configured_callback_return(
    ea::function<pipeline_configured_unsupported()> callback)
{
    (void)callback;
}

inline void reject_ea_nested_callback_return(
    ea::function<ea::function<int(int)>()> callback)
{
    (void)callback;
}

inline void reject_ea_nested_callback_parameter(
    ea::function<void(ea::function<int(int)>)> callback)
{
    (void)callback;
}

inline void reject_mixed_nested_callback_return(
    std::function<ea::function<int(int)>()> callback)
{
    (void)callback;
}

inline void reject_mixed_nested_callback_parameter(
    ea::function<void(std::function<int(int)>)> callback)
{
    (void)callback;
}

template <typename T>
[[sharpcpp::instantiate("pipeline_breadth::pipeline_bad_managed")]]
T invalid_managed_name_instantiation(T value)
{
    return value;
}

template <typename T>
[[sharpcpp::instantiate(
    "pipeline_breadth::pipeline_configured_box<pipeline_breadth::pipeline_bad_managed>")]]
T invalid_nested_managed_name_instantiation(T value)
{
    return value;
}

struct pipeline_open_primary
{
    explicit pipeline_open_primary(int value)
        : value_(value)
    {
    }

protected:
    int value_{};
};

template <typename T>
struct pipeline_open_secondary
{
    T open_return() const { return {}; }
    void open_parameter(T value) { (void)value; }
};

struct pipeline_open_derived final
    : pipeline_open_primary
    , pipeline_open_secondary<int>
{
    explicit pipeline_open_derived(int value)
        : pipeline_open_primary(value)
    {
    }

    int get_round_trip() const { return value_ + 4; }
};

inline pipeline_open_derived* create_pipeline_open_derived(int value)
{
    return new pipeline_open_derived(value);
}

struct pipeline_director_payload
{
    int value{};
};

struct [[sharpcpp::director]] pipeline_unsupported_director
{
    virtual ~pipeline_unsupported_director() = default;
    virtual void update([[sharpcpp::ref]] pipeline_director_payload* value)
    {
        if (value != nullptr)
            ++value->value;
    }
};
}
