#pragma once

#include <vector>

namespace generic_fields
{
struct point
{
    int x{};
    int y{};
};

/// Quaternion-like value type projected to a managed struct (see GenericFieldsMarshallers.h).
struct spin
{
    spin() = default;
    spin(float w_, float x_, float y_, float z_) : w(w_), x(x_), y(y_), z(z_) {}

    float dot(const spin& other) const
    {
        return w * other.w + x * other.x + y * other.y + z * other.z;
    }

    float w{1.0f};
    float x{};
    float y{};
    float z{};
};

/// Public value fields of a class template are bound per instantiation; static, array and
/// member shapes that need more than plain member access stay unbound.
template <typename T>
struct
[[sharpcpp::instantiate("int")]]
[[sharpcpp::instantiate("float")]]
[[sharpcpp::instantiate("generic_fields::point")]]
cell
{
    cell() = default;

    T value{};
    int count{};
    const T frozen{};
    static int shared;
    T pair[2]{};

    /// Shares its managed name with the field below, which is renamed.
    int total() const
    {
        return count * 10;
    }

    int Total{};
};

template <typename T>
int cell<T>::shared = 0;

/// A field typed as the template argument keeps the value mapping of that argument; a
/// container of the argument keeps a dependent spelling and is reported instead.
template <typename T>
struct
[[sharpcpp::instantiate("int")]]
slot
{
    slot() = default;

    T value{};
    std::vector<T> history;

    int history_size() const
    {
        return static_cast<int>(history.size());
    }
};

/// The explicit specialization below changes the type of derivative, so primary-template
/// field accessors must not be rendered for tracked<spin>.
template <typename T>
struct
[[sharpcpp::instantiate("int")]]
tracked
{
    tracked() = default;

    T value{};
    T derivative{};

    int kind() const
    {
        return 0;
    }
};

template <>
struct tracked<spin>
{
    tracked() = default;

    spin value{};
    point derivative{};

    int kind() const
    {
        return 1;
    }
};

/// Only discovered through a by-value return of probe<spin>, like rbfx ValueWithDerivative<Quaternion>.
template <typename T>
struct probe
{
    probe() = default;

    T value{};
};

/// Discovers slot<spin>, tracked<spin> and probe<spin> from surface uses instead of explicit instantiate requests.
struct slot_user
{
    static tracked<spin> make_tracked()
    {
        return {};
    }

    static probe<spin> make_probe(float w)
    {
        probe<spin> result;
        result.value.w = w;
        return result;
    }

    static int history_size_of(const slot<spin>& s)
    {
        return s.history_size();
    }

    static slot<spin> make(float w)
    {
        slot<spin> result;
        result.value.w = w;
        return result;
    }
};
} // namespace generic_fields
