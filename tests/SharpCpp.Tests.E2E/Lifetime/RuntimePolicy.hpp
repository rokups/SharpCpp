#pragma once

#include <utility>

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

inline int runtime_cache_destroyed_count = 0;

struct runtime_cache_value
{
    explicit runtime_cache_value(int value)
        : value_(value)
    {
    }

    ~runtime_cache_value()
    {
        ++runtime_cache_destroyed_count;
    }

    int get_value() const
    {
        return value_;
    }

private:
    int value_{};
};

inline runtime_cache_value*& runtime_cache_slot()
{
    static runtime_cache_value* value{};
    return value;
}

inline runtime_cache_value* create_runtime_cache_borrowed(int value)
{
    auto*& slot = runtime_cache_slot();
    delete slot;
    slot = new runtime_cache_value(value);
    return slot;
}

template <typename T>
struct runtime_cache_owner
{
    explicit runtime_cache_owner(T* value)
        : value_(value)
    {
    }

    runtime_cache_owner(runtime_cache_owner&& other) noexcept
        : value_(other.detach())
    {
    }

    runtime_cache_owner(const runtime_cache_owner&) = delete;

    T* detach()
    {
        return std::exchange(value_, nullptr);
    }

private:
    T* value_{};
};

inline runtime_cache_owner<runtime_cache_value> take_runtime_cache_owned()
{
    return runtime_cache_owner<runtime_cache_value>(std::exchange(runtime_cache_slot(), nullptr));
}

inline int get_runtime_cache_destroyed_count()
{
    return runtime_cache_destroyed_count;
}
