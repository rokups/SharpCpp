#pragma once

// Fixtures for declarations the pipeline or renderer leaves out without an IGNORED line; each
// must still show up as a WARNING in generator.log (see DiagnosticLogTests).

// The field `count` shares its managed name with the method `Count`, which cannot be bound (rvalue
// reference parameter) and is therefore ignored. Ignored members emit nothing under their managed
// name, so they do not clash: the field is still rendered as `Count` (and the method is logged as
// IGNORED).
struct silent_drop_field_clash
{
    int count = 3;

    void Count(int&& value)
    {
        count = value;
    }
};

// The method `Sum` is renamed to `Total` by the generator after the collision resolver has run, so
// the field `total` (managed name `Total`) clashes with a live method: the renderer leaves the field
// out and logs a WARNING.
struct silent_drop_field_renamed_clash
{
    int total = 4;

    int Sum() const
    {
        return total;
    }
};

// Same ignored-method clash on a generic record.
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
