#pragma once

/// A tightly packed POD vector projected to a namespaced managed type (mirrors
/// JPH::Float3 -> global::RedBolt.Math.Float3 in RedBolt's JoltMarshallers.hpp), used as
/// a fixed-size C array field element (mirrors JPH::Triangle::mV).
struct configured_vector3
{
    float x{};
    float y{};
    float z{};
};

struct configured_triangle
{
    configured_vector3 vertices[3]{};
};

inline configured_triangle* make_configured_triangle(
    float x0, float y0, float z0,
    float x1, float y1, float z1,
    float x2, float y2, float z2)
{
    auto* result = new configured_triangle{};
    result->vertices[0] = configured_vector3{x0, y0, z0};
    result->vertices[1] = configured_vector3{x1, y1, z1};
    result->vertices[2] = configured_vector3{x2, y2, z2};
    return result;
}

inline float sum_configured_triangle(const configured_triangle& value)
{
    float total = 0.0f;
    for (const auto& vertex : value.vertices)
    {
        total += vertex.x + vertex.y + vertex.z;
    }
    return total;
}
