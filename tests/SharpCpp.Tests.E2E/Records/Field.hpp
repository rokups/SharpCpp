#pragma once

enum class field_mode : int
{
    idle = 1,
    active = 2,
    suspended = 3
};

struct field_box
{
    int count{};
    bool enabled{};
    field_mode mode{field_mode::idle};
};

inline field_box* create_field_box(int count, bool enabled, field_mode mode)
{
    static field_box box {};
    box.count = count;
    box.enabled = enabled;
    box.mode = mode;
    return &box;
}
