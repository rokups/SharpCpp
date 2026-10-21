#pragma once

#include <functional>

#include "../../Callback/EaFunction.h"
#include <locale>
#include <string_view>
#include <system_error>

struct global_excluded_record
{
    int value{};
};

enum class global_excluded_enum
{
    value
};

inline int global_excluded_function(int value)
{
    return value;
}

namespace coverage_outside
{
struct outside_record
{
    int value{};
};

enum class outside_enum
{
    value
};

inline int outside_function(int value)
{
    return value;
}
}

namespace coverage_allowed
{
inline int degradation_round_trip(int value)
{
    return value + 1;
}

struct mapped_counter
{
    explicit mapped_counter(int value)
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

struct degradation_field_collision
{
    explicit degradation_field_collision(int value)
        : value(value)
    {
    }

    int Value() const { return value + 1; }
    int ValueValue() const { return value + 2; }

    int value{};
};

inline degradation_field_collision* create_degradation_field_collision(int value)
{
    return new degradation_field_collision(value);
}

struct degradation_operator_surface
{
    explicit degradation_operator_surface(int value)
        : value_(value)
    {
    }

    int get_value() const { return value_; }
    int operator+() const { return value_ + 3; }
    int operator-(int value) const { return value_ - value; }

private:
    int value_{};
};

int duplicate_exact(int value);
inline int duplicate_exact(int value)
{
    return value + 2;
}

[[sharpcpp::rename("ManagedCollision")]]
inline int managed_collision_first(int value)
{
    return value + 3;
}

[[sharpcpp::rename("ManagedCollision")]]
inline int managed_collision_second(int value)
{
    return value + 4;
}

struct const_collision
{
    explicit const_collision(int value)
        : value_(value)
    {
    }

    int read() { return value_; }
    int read() const { return value_; }

private:
    int value_{};
};

struct incomplete_record;

inline int consume_incomplete(incomplete_record* value)
{
    return value != nullptr ? 1 : 0;
}

struct [[sharpcpp::ignore("coverage degradation")]] ignored_record
{
    int value{};
};

using degradation_ignored_handle_alias = ignored_record*;

enum class [[sharpcpp::ignore("coverage degradation")]] ignored_enum
{
    value
};

using degradation_ignored_enum_alias = ignored_enum;
using degradation_unsupported_primitive_alias = long double;
using degradation_external_handle_alias = std::locale*;

inline int consume_ignored_record(ignored_record* value)
{
    return value != nullptr ? value->value : 0;
}

inline int consume_ignored_enum(ignored_enum value)
{
    return value == ignored_enum::value ? 1 : 0;
}

inline int consume_external_enum(std::errc value)
{
    return static_cast<int>(value);
}

inline int consume_external_handle(std::locale* value)
{
    return value != nullptr ? 1 : 0;
}

inline void callback_with_unsupported_return(std::function<long double()> callback)
{
    (void)callback;
}

inline void callback_with_ignored_return(std::function<ignored_record*()> callback)
{
    (void)callback;
}

inline void callback_with_unsupported_parameter(std::function<void(long double)> callback)
{
    (void)callback;
}

inline void callback_with_ignored_parameter(std::function<void(ignored_record*)> callback)
{
    (void)callback;
}

inline void callback_marked_ref([[sharpcpp::ref]] std::function<void(int)> callback)
{
    callback(1);
}

inline void ea_callback_with_unsupported_return(ea::function<long double()> callback)
{
    (void)callback;
}

inline void ea_callback_with_ignored_return(ea::function<ignored_record*()> callback)
{
    (void)callback;
}

inline void ea_callback_with_unsupported_parameter(ea::function<void(long double)> callback)
{
    (void)callback;
}

inline void ea_callback_with_ignored_parameter(ea::function<void(ignored_record*)> callback)
{
    (void)callback;
}

inline void ea_callback_marked_ref([[sharpcpp::ref]] ea::function<void(int)> callback)
{
    callback(1);
}

struct record_value
{
    int value{};
};

struct unsupported_fields
{
    explicit unsupported_fields(int& source)
        : reference(source)
    {
    }

    int read() const
    {
        return reference + nested.value;
    }

    int& reference;
    std::function<void(int)> callback;
    ea::function<void(int)> ea_callback;
    record_value nested;
};

struct increment_operator
{
    explicit increment_operator(int value)
        : value_(value)
    {
    }

    increment_operator& operator++()
    {
        ++value_;
        return *this;
    }

private:
    int value_{};
};

struct heterogeneous_equality
{
    bool operator==(int value) const
    {
        return value == 0;
    }
};

struct [[sharpcpp::director]] non_pure_unsupported_director
{
    virtual ~non_pure_unsupported_director() = default;

    virtual void mutate([[sharpcpp::ref]] int* value)
    {
        if (value != nullptr)
            ++*value;
    }
};

struct [[sharpcpp::director]] pure_unsupported_director
{
    virtual ~pure_unsupported_director() = default;
    virtual void write([[sharpcpp::ref]] int* value) = 0;
};

struct ignored_base
{
    [[sharpcpp::ignore]]
    int hidden() const { return 0; }
};

struct usable_base
{
    int base_value() const { return 7; }
};

struct rebased_derived : ignored_base, usable_base
{
    int derived_value() const { return 9; }
};

struct primary_base
{
    int primary_value() const { return 1; }
};

struct operator_secondary_base
{
    bool operator==(const operator_secondary_base&) const { return true; }
};

struct unsupported_secondary_derived : primary_base, operator_secondary_base
{
    int derived_value() const { return 2; }
};

struct virtual_base
{
    int value() const { return 3; }
};

struct virtual_derived : virtual virtual_base
{
    int derived_value() const { return 4; }
};

template <typename T>
struct
[[sharpcpp::instantiate("int")]]
[[sharpcpp::instantiate("coverage_outside::outside_record*")]]
filtered_box
{
    filtered_box() = default;

    T echo(T value) const
    {
        return value;
    }
};

inline void consume_incomplete_box(const filtered_box<incomplete_record*>& value)
{
    (void)value;
}

inline void consume_global_box(const filtered_box<::global_excluded_record*>& value)
{
    (void)value;
}

inline void consume_outside_box(const filtered_box<coverage_outside::outside_record*>& value)
{
    (void)value;
}

template <typename T>
[[sharpcpp::instantiate("int")]]
T filtered_identity(T value)
{
    return value;
}

template struct filtered_box<std::string_view>;
template std::string_view filtered_identity<std::string_view>(std::string_view value);
}

namespace coverage_allowed::detail
{
struct hidden_record
{
    int value{};
};

enum class hidden_enum
{
    value
};

inline int hidden_function(int value)
{
    return value;
}
}

namespace coverage_allowed::Detail
{
struct hidden_record
{
    int value{};
};
}

namespace coverage_allowed
{
inline void consume_detail_box(const filtered_box<detail::hidden_record*>& value)
{
    (void)value;
}
}

namespace coverage_allowed
{
/// Same-named nested classes of two records, projected into one flat namespace (rbfx's
/// particle graph nodes each declare an `Instance`).
struct first_node
{
    int id() const { return 10; }

    struct instance
    {
        int read() const { return 1; }
    };
};

struct second_node
{
    int id() const { return 20; }

    struct instance
    {
        int read() const { return 2; }
    };
};

/// An enclosing record without API of its own scopes its nested record as a namespace.
struct third_node
{
    struct instance
    {
        int read() const { return 3; }
    };
};
}
