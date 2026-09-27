#pragma once

#include <functional>

struct [[sharpcpp::director]] inheritance_access_root
{
    virtual ~inheritance_access_root() = default;

protected:
    virtual int inherited_access(int value) const
    {
        return value + 100;
    }
};

struct inheritance_access_mid : inheritance_access_root
{
    int inherited_access(int value) const override
    {
        return value + 200;
    }
};

struct base_counter : inheritance_access_mid
{
    explicit base_counter(int value)
        : value_(value)
    {
    }

    virtual int get_kind() const
    {
        return 1;
    }

    int get_value() const
    {
        return value_;
    }

    int inherited_access(int value) const override
    {
        return value_ + value;
    }

    static int inherited_static(int value)
    {
        return value + 2;
    }

protected:
    int value_{};
};

struct inheritance_projection_mixin
{
    explicit inheritance_projection_mixin(int offset)
        : offset_(offset)
    {
    }

    int projected_const(int value) const
    {
        return value + offset_;
    }

    /// Adds the native offset and an optional increment.
    int projected_default(int value, int increment = 3) const
    {
        return value + increment + offset_;
    }

    int projected_mutate(int amount)
    {
        offset_ += amount;
        return offset_;
    }

    void projected_ref([[sharpcpp::ref]] int* value) const
    {
        *value += offset_;
    }

    int projected_out(int seed, [[sharpcpp::out]] int* value) const
    {
        *value = seed + offset_;
        return *value;
    }

    base_counter* projected_handle(base_counter* value) const
    {
        return value;
    }

    base_counter projected_owned_value(int value) const
    {
        return base_counter(value + offset_);
    }

    int projected_callback(
        [[sharpcpp::delegate("SharpCpp.Tests.E2E.Records.InheritanceProjectionCallback")]] std::function<int(int)> callback,
        int value) const
    {
        return callback(value) + offset_;
    }

private:
    int offset_{};
};

struct derived_counter : base_counter, inheritance_projection_mixin
{
    derived_counter(int value, int bonus)
        : base_counter(value)
        , inheritance_projection_mixin(bonus)
        , bonus_(bonus)
    {
    }

    int get_bonus() const
    {
        return bonus_;
    }

    int get_kind() const override
    {
        return 2;
    }

    int add_all() const
    {
        return value_ + bonus_;
    }

    base_counter* echo_base(base_counter* value)
    {
        return value;
    }

    static base_counter* identity_base(base_counter* value)
    {
        return value;
    }

private:
    int bonus_{};
};

struct inheritance_holder
{
    base_counter* base_view{};
};

inline inheritance_holder* create_inheritance_holder(base_counter* base_view)
{
    static inheritance_holder holder {};
    holder.base_view = base_view;
    return &holder;
}

inline int read_base_counter(base_counter* value)
{
    return value->get_value();
}

inline int read_base_counter_kind(base_counter* value)
{
    return value->get_kind();
}

inline base_counter* get_shared_derived_as_base()
{
    static derived_counter shared(30, 4);
    return &shared;
}

// A variadic class template cannot be a C# generic; records deriving from it keep the
// template's own concrete base.
struct variadic_root
{
    virtual ~variadic_root() = default;
    int root_value() const { return 11; }
};

template <typename Instance, typename... Values>
struct variadic_node : variadic_root
{
    int arity() const { return static_cast<int>(sizeof...(Values)); }
};

struct variadic_leaf : variadic_node<int, float, double>
{
    int leaf_value() const { return 5; }
};

inline int read_variadic_root(variadic_root* value)
{
    return value->root_value();
}

// A class template deriving from its template parameter cannot be a C# generic;
// records instantiating it derive from the concrete argument instead.
struct tracked_base
{
    virtual ~tracked_base() = default;
    int tracked_value() const { return 21; }
};

struct tracked_tag
{
};

template <class TBase, class TTag>
struct tracked_component : TBase
{
    int tag_size() const { return static_cast<int>(sizeof(TTag)); }
};

struct tracked_leaf : tracked_component<tracked_base, tracked_tag>
{
    int leaf_value() const { return 3; }
};
