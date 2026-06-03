#pragma once

enum class field_mode : int
{
    idle = 1,
    active = 2,
    suspended = 3
};

struct field_payload
{
    int value{};
};

struct field_box
{
    int count{};
    bool enabled{};
    field_mode mode{field_mode::idle};
    field_payload* payload{};
};

inline field_payload* create_field_payload(int value)
{
    return new field_payload { value };
}

inline field_box* create_field_box(int count, bool enabled, field_mode mode, field_payload* payload)
{
    static field_box box {};
    box.count = count;
    box.enabled = enabled;
    box.mode = mode;
    box.payload = payload;
    return &box;
}
