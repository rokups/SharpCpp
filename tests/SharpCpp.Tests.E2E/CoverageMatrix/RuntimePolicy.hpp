#pragma once

struct runtime_policy_base
{
    explicit runtime_policy_base(int value)
        : value_(value)
    {
    }

    virtual ~runtime_policy_base() = default;

    virtual int get_value() const
    {
        return value_;
    }

protected:
    int value_{};
};

struct runtime_policy_derived final : runtime_policy_base
{
    explicit runtime_policy_derived(int value)
        : runtime_policy_base(value)
    {
    }

    int get_value() const final
    {
        return value_ + 1;
    }

    static int get_type_static()
    {
        return 73;
    }
};

inline runtime_policy_base* get_runtime_policy_base(int value)
{
    static runtime_policy_base instance(value);
    return &instance;
}

inline runtime_policy_derived* get_runtime_policy_derived(int value)
{
    static runtime_policy_derived instance(value);
    return &instance;
}

struct [[sharpcpp::rename("RuntimeRenamedValue")]] runtime_rename_value
{
    explicit runtime_rename_value(int value)
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

struct runtime_scalar
{
    int value{};
};

inline runtime_scalar make_runtime_scalar(int value)
{
    return {value};
}

inline int consume_runtime_scalar(runtime_scalar value)
{
    return value.value + 1;
}
