#pragma once

#include <functional>
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

enum class [[sharpcpp::ignore("coverage degradation")]] ignored_enum
{
    value
};

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

    virtual void mutate(int& value)
    {
        ++value;
    }
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
