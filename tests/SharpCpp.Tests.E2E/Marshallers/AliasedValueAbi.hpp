#pragma once

/// A configured value type reached through an alias of a class template specialization. The
/// type has a base class and a user constructor, and it is 8 bytes (mirrors Urho3D::IntVector2,
/// an alias of BaseIntegerVector2<int>). MSVC returns such a class through a hidden pointer, so
/// its marshaller crosses the boundary as a different trivial ABI struct.
namespace aliased_value_abi
{
struct pair_base
{
};

template <typename T>
class basic_pair : public pair_base
{
public:
    basic_pair() noexcept = default;

    basic_pair(T x, T y) noexcept
        : x_(x)
        , y_(y)
    {
    }

    T x_{};
    T y_{};
};

using int_pair = basic_pair<int>;

static_assert(sizeof(int_pair) == 8);

/// The fields use the alias and the canonical spelling.
struct int_pair_holder
{
    int id{};
    int_pair value{};
    basic_pair<int> canonical{};
};

inline int_pair make_int_pair(int x, int y) noexcept
{
    return int_pair(x, y);
}

inline int sum_int_pair(int_pair value) noexcept
{
    return value.x_ * 10 + value.y_;
}

inline int sum_int_pair_const_reference(const int_pair& value) noexcept
{
    return value.x_ * 10 + value.y_;
}

inline void double_int_pair(int_pair& value) noexcept
{
    value.x_ *= 2;
    value.y_ *= 2;
}

inline int read_int_pair_holder(const int_pair_holder& holder) noexcept
{
    return holder.id * 10000 + (holder.value.x_ * 10 + holder.value.y_) * 100 + holder.canonical.x_ * 10 + holder.canonical.y_;
}
}
