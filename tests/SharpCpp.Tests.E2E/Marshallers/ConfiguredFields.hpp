#pragma once

#include <string>

#include "ConfiguredPrimitives.hpp"

namespace configured_fields
{
/// A marshaller-backed record keeps its configured value type as a field.
struct body_settings
{
    configured_indirect_pair position;
};

inline int sum_body_position(const body_settings* value)
{
    return value->position.first + value->position.second;
}

struct material_data
{
    configured_indirect_pair color;
};

/// A marshaller-backed record inside flattened plain data keeps its configured type.
struct material
{
    material_data data;
};

inline int sum_material_color(const material* value)
{
    return value->data.color.first + value->data.color.second;
}

struct pointer_buffer
{
    int size;
    configured_indirect_pair* data;
    std::string* name;
};

/// Flattened pointer fields do not take the value marshaller of their pointee.
struct pointer_owner
{
    pointer_buffer values;
};

inline pointer_owner* create_pointer_owner()
{
    static configured_indirect_pair pair{4, 5};
    static std::string name = "owner";
    static pointer_owner value{{1, &pair, &name}};
    return &value;
}

inline int read_pointer_owner_pair(const pointer_owner* value)
{
    return value->values.data->first + value->values.data->second;
}
}
