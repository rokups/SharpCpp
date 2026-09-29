#pragma once

/// A projected value type whose native layout genuinely differs from its abi_type layout
/// (mirrors JPH::Vec3, which is 16-byte SIMD natively but projects to the 12-byte
/// JoltVector3Abi). alignas(16) float v[4] gives this a 16-byte size and 16-byte alignment;
/// only v[0..2] carry values, v[3] is unused padding (mirroring Vec3's W lane). This is the
/// shape that makes Marshaller.hpp's generic Marshaller<T*>/Marshaller<const T*>/Marshaller<T&>/
/// Marshaller<const T&> fallbacks static_assert: reinterpret_cast-ing this type's storage as its
/// (differently laid out) abi_type would silently misread memory, so this fixture supplies
/// pointer/reference marshallers explicitly instead of relying on those fallbacks.
struct alignas(16) configured_wide_vector3
{
    float v[4]{};
};

inline configured_wide_vector3 make_configured_wide_vector3(float x, float y, float z) noexcept
{
    configured_wide_vector3 result{};
    result.v[0] = x;
    result.v[1] = y;
    result.v[2] = z;
    return result;
}

struct configured_wide_triangle
{
    configured_wide_vector3 arr[3]{};
};

inline configured_wide_triangle* make_configured_wide_triangle(
    float x0, float y0, float z0,
    float x1, float y1, float z1,
    float x2, float y2, float z2)
{
    auto* result = new configured_wide_triangle{};
    result->arr[0] = make_configured_wide_vector3(x0, y0, z0);
    result->arr[1] = make_configured_wide_vector3(x1, y1, z1);
    result->arr[2] = make_configured_wide_vector3(x2, y2, z2);
    return result;
}

inline float sum_configured_wide_triangle(const configured_wide_triangle& value) noexcept
{
    float total = 0.0f;
    for (const auto& vertex : value.arr)
        total += vertex.v[0] + vertex.v[1] + vertex.v[2];
    return total;
}

// Single-element, nullable const-pointer parameter. Exercises
// ProjectedValueConstPointerMarshallerBase rather than an array/collection marshaller.
inline float sum_configured_wide_vector3_ptr(const configured_wide_vector3* value) noexcept
{
    if (value == nullptr)
        return -1.0f;
    return value->v[0] + value->v[1] + value->v[2];
}

// Single-element const-reference parameter. Exercises ProjectedValueConstReferenceMarshallerBase.
inline float sum_configured_wide_vector3_ref(const configured_wide_vector3& value) noexcept
{
    return value.v[0] + value.v[1] + value.v[2];
}

// Mutable array output parameter. Marshaller<configured_wide_vector3*> binds it as
// Span<ConfiguredWideVector3> (ProjectedValueSpanMarshallerBase): native writes 16-byte elements
// into a std::vector the marshaller copies back to the 12-byte managed elements after the call.
// The generator sets MinimumSpanLength = 3 on `out`, so a shorter span throws before this runs.
inline void fill_configured_wide_vector3s(configured_wide_vector3* out) noexcept
{
    for (int i = 0; i < 3; ++i)
        out[i] = make_configured_wide_vector3(
            static_cast<float>(i * 3 + 1),
            static_cast<float>(i * 3 + 2),
            static_cast<float>(i * 3 + 3));
}

// Same span projection without a minimum-length hint: an empty span reaches native as nullptr.
inline bool configured_wide_vector3s_is_null(configured_wide_vector3* values) noexcept
{
    return values == nullptr;
}

/// A second layout-mismatched value type (16 bytes native, 8 bytes ABI) whose const pointer binds
/// as ReadOnlySpan<T>. configured_wide_vector3's const pointer already binds as a single `in`
/// element (by_reference = true above), and a C++ type has exactly one Marshaller<const T*>.
struct alignas(16) configured_padded_pair
{
    float first{};
    float second{};
};

inline float sum_configured_padded_pairs(const configured_padded_pair* values, int count) noexcept
{
    if (values == nullptr)
        return -1.0f;
    float total = 0.0f;
    for (int i = 0; i < count; ++i)
        total += values[i].first + values[i].second;
    return total;
}
