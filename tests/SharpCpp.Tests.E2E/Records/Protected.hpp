#pragma once

struct protected_payload
{
    protected_payload() = default;

    explicit protected_payload(int value)
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

struct [[sharpcpp::director]] protected_counter
{
protected:
    explicit protected_counter(int seed)
        : payloads_{protected_payload(seed), protected_payload(seed + 1)}
        , seed_(seed)
    {
    }

    int increment(int amount)
    {
        seed_ += amount;
        return seed_;
    }

    virtual int scale(int value)
    {
        return (seed_ * factor_) + value;
    }

    int factor_{2};
    protected_payload payloads_[2]{};

public:
    int get_seed() const
    {
        return seed_;
    }

    int call_scale(int value)
    {
        return scale(value);
    }

    int sum_payloads() const
    {
        return payloads_[0].get_value() + payloads_[1].get_value();
    }

private:
    int seed_{};
};

struct plain_protected_counter
{
protected:
    int hidden_value() const
    {
        return 7;
    }

    int factor_{3};

public:
    plain_protected_counter() = default;

    int visible_value() const
    {
        return hidden_value() * factor_;
    }
};

// A templated member record is flattened into its owner; its protected state is not
// accessible from the owner (it is not a base), so it must not be flattened.
template <typename T>
class guarded_value
{
public:
    T get() const
    {
        return value_;
    }

protected:
    T value_{5};
};

struct guarded_value_holder
{
    guarded_value<int> guarded;
    int visible{9};
};
