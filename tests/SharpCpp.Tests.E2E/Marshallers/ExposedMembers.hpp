#pragma once

namespace exposed_members
{
struct listener
{
    int id{};
};

/// A configured record keeps the constructors the language declares.
struct constructed
{
    explicit constructed(int value)
        : value(value)
    {
    }

    int value{};
};

inline listener* create_listener(int id)
{
    static listener value;
    value.id = id;
    return &value;
}
}
