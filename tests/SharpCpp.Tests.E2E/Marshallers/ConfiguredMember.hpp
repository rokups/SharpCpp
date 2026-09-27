#pragma once

struct configured_member_payload
{
    explicit configured_member_payload(int value)
        : value_(value)
    {
    }

    int get_value() const { return value_; }

private:
    int value_{};
};

struct configured_member_value
{
    configured_member_value() = default;

    explicit configured_member_value(int value)
        : value(value)
    {
    }

    int value{40};
};

inline configured_member_value make_configured_member_value(int value)
{
    return configured_member_value(value);
}

inline int read_configured_member_value(const configured_member_value& value)
{
    return value.value;
}

inline configured_member_payload* make_configured_member_payload(int value)
{
    return new configured_member_payload(value);
}

