#pragma once

namespace validation_unresolved
{
struct payload;
}

struct validation_base
{
    int get_value() const
    {
        return 7;
    }
};

template <typename T>
struct validation_no_instantiations_record
{
    T echo(T value) const
    {
        return value;
    }
};

template <typename T>
struct
[[sharpcpp::instantiate("int")]]
validation_inheriting_record : validation_base
{
    T echo(T value) const
    {
        return value;
    }
};

template <typename T>
struct
[[sharpcpp::instantiate("int")]]
validation_field_record
{
    T value{};

    T get_value() const
    {
        return value;
    }
};

template <typename T>
struct
[[sharpcpp::instantiate("int")]]
validation_operator_record
{
    T operator+(T value) const
    {
        return value + T{1};
    }
};

template <typename T>
struct validation_discovered_record
{
    T echo(T value) const
    {
        return value;
    }
};

inline validation_discovered_record<validation_unresolved::payload*>*
make_validation_unresolved_record()
{
    return new validation_discovered_record<validation_unresolved::payload*>();
}

template <typename T>
T validation_no_instantiations_function(T value)
{
    return value;
}

template <typename T>
T validation_long_double_instantiation(T value)
{
    return value;
}

template long double validation_long_double_instantiation<long double>(long double value);

template <typename T>
[[sharpcpp::instantiate("validation_unresolved::payload*")]]
T validation_unresolved_instantiation(T value)
{
    return value;
}

inline int validation_breadth_round_trip(int value)
{
    return value + 3;
}
