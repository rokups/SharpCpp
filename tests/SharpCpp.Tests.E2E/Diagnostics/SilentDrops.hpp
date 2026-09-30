#pragma once

// Fixtures for declarations the pipeline or renderer leaves out without an IGNORED line; each
// must still show up as a WARNING in generator.log (see DiagnosticLogTests).

// The field `count` shares its managed name with the method `Count`, which cannot be bound (rvalue
// reference parameter) and is therefore ignored. The collision resolver skips ignored members, so
// the field keeps its name and the renderer leaves it out.
struct silent_drop_field_clash
{
    int count = 3;

    void Count(int&& value)
    {
        count = value;
    }
};

// Same clash on a generic record.
template <typename T>
struct
[[sharpcpp::instantiate("int")]]
silent_drop_generic_field_clash
{
    T count{};

    void Count(T&& value)
    {
        count = value;
    }
};

// A base of a dropped generic base that is virtual cannot be hoisted onto the derived record.
struct silent_drop_virtual_root
{
    int root_value() const
    {
        return 1;
    }
};

template <typename T>
struct silent_drop_generic_base : virtual silent_drop_virtual_root
{
    T base_value() const
    {
        return T{};
    }
};

struct silent_drop_hoisting_derived : silent_drop_generic_base<int>
{
    int derived_value() const
    {
        return 2;
    }
};
