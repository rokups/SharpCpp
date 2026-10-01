#pragma once

namespace pv_ns
{

/// A tightly packed vector projected to a namespaced managed struct (mirrors JPH::Float3 ->
/// global::RedBolt.Math.Float3 in RedBolt's JoltMarshallers.hpp). It has constructors and lives
/// in a C++ namespace, like the real Jolt types.
struct pv_vec
{
    pv_vec() = default;
    pv_vec(float ix, float iy, float iz) : x(ix), y(iy), z(iz) {}

    float x{};
    float y{};
    float z{};

    float length_sq() const { return x * x + y * y + z * z; }
    pv_vec scaled(float f) const { return pv_vec(x * f, y * f, z * f); }
    static pv_vec unit_x() { return pv_vec(1, 0, 0); }
};

using pv_vec_alias = pv_vec;

/// Holds the projected value both as a plain field and as a fixed-size C array field (mirrors
/// JPH::Triangle::mV), with by-value members.
struct pv_holder
{
    pv_vec single{};
    pv_vec line[3]{};
    pv_vec_alias aliased{};

    pv_vec get_single() const { return single; }
    void set_single(pv_vec v) { single = v; }

    float sum_line() const
    {
        float total = 0.0f;
        for (const auto& item : line)
        {
            total += item.x + item.y + item.z;
        }
        return total;
    }
};

/// A fixed-size array of the projected value as the only member (the JPH::Triangle::mV shape).
struct pv_tri
{
    pv_vec v[3];
};

/// A class template instantiated with the projected value (mirrors JPH::Array<Float3>).
template <typename T>
struct pv_box
{
    T value{};
    T items[2]{};

    T get() const { return value; }
    void set(T v) { value = v; }
    T get_at(int i) const { return items[i]; }
    void set_at(int i, T v) { items[i] = v; }
};

using pv_vec_box = pv_box<pv_vec>;

inline pv_vec pv_add(pv_vec a, pv_vec b)
{
    return pv_vec(a.x + b.x, a.y + b.y, a.z + b.z);
}

inline pv_holder* make_pv_holder()
{
    return new pv_holder();
}

inline pv_tri* make_pv_tri()
{
    return new pv_tri();
}

inline pv_vec_box* make_pv_box()
{
    return new pv_vec_box();
}

}

namespace pv_ns2
{

/// A namespaced plain value record without member functions, projected to a managed struct. Used
/// from a record in the same namespace, so the field spellings are unqualified and the generated
/// wrapper must qualify them.
struct pv_plain
{
    float x;
    float y;
};

using pv_plain_alias = pv_plain;

/// Declared next to pv_plain, so the field element types are spelled unqualified in the source.
struct pv_plain_holder
{
    pv_plain origin{};
    pv_plain points[2];
    pv_plain_alias aliased[2];
};

inline pv_plain_holder* make_pv_plain_holder()
{
    return new pv_plain_holder();
}

}
