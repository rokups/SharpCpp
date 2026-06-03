#pragma once

struct field_box
{
    int count{};
    bool enabled{};
};

inline field_box* create_field_box(int count, bool enabled)
{
    static field_box box {};
    box.count = count;
    box.enabled = enabled;
    return &box;
}
