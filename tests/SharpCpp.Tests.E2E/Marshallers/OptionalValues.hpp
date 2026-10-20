#pragma once

#include <cstddef>
#include <optional>
#include <type_traits>
#include <utility>

namespace optional_values
{
/// A blittable value that the marshaller configures as a managed struct.
struct point
{
    float x{};
    float y{};
    float z{};
};

/// A copyable class that crosses the ABI as a handle.
class item
{
public:
    item() = default;

    explicit item(int value)
        : value_(value)
    {
    }

    int get_value() const
    {
        return value_;
    }

    void set_value(int value)
    {
        value_ = value;
    }

private:
    int value_{};
};

inline std::optional<int> pick(bool has_value)
{
    if (has_value)
        return 7;
    return std::nullopt;
}

inline int sum(std::optional<int> first, const std::optional<float>& second)
{
    return first.value_or(100) + static_cast<int>(second.value_or(1000.0f));
}

inline std::optional<point> make(bool has_value)
{
    if (has_value)
        return point{1.0f, 2.0f, 3.0f};
    return std::nullopt;
}

inline float component_sum(std::optional<point> value)
{
    return value.has_value() ? value->x + value->y + value->z : -1.0f;
}

inline std::optional<item> find(bool has_value)
{
    if (has_value)
        return item{42};
    return std::nullopt;
}

inline int take(std::optional<item> value)
{
    return value.has_value() ? value->get_value() : -1;
}

inline int take_reference(const std::optional<item>& value)
{
    return value.has_value() ? value->get_value() : -1;
}

/// A record with an optional field.
struct settings
{
    std::optional<unsigned> limit;
};

/// A narrow enum. The optional crosses in the exact underlying type of the enum.
enum class mode : unsigned char
{
    off = 1,
    on = 2,
};

inline std::optional<mode> pick_mode(bool has_value)
{
    if (has_value)
        return mode::on;
    return std::nullopt;
}

inline int mode_value(std::optional<mode> value)
{
    return value.has_value() ? static_cast<int>(*value) : -1;
}

/// A class that the marshaller converts to a plain ABI struct with another member order. The
/// managed type is the struct of the ABI, and it is also the marshaller type.
class vec
{
public:
    vec() = default;

    vec(float x, float y)
        : x_(x)
        , y_(y)
    {
    }

    float x() const
    {
        return x_;
    }

    float y() const
    {
        return y_;
    }

private:
    float x_{};
    float y_{};
};

inline std::optional<vec> make_vec(bool has_value)
{
    if (has_value)
        return vec{3.0f, 4.0f};
    return std::nullopt;
}

inline float vec_difference(std::optional<vec> value)
{
    return value.has_value() ? value->x() - value->y() : -100.0f;
}

/// A const reference parameter with a nullopt default. The managed parameter is already nullable.
inline float vec_difference_or_default(const std::optional<vec>& value = std::nullopt, int scale = 1)
{
    return value.has_value() ? (value->x() - value->y()) * static_cast<float>(scale) : -50.0f;
}

/// A record with fixed arrays of an optional element and of its underlying element. The two
/// arrays have the same native shape and must get different native entry point names.
struct optional_arrays
{
    std::optional<unsigned> optional_items[2]{};
    unsigned plain_items[2]{};
};

/// A class with a const member. It has no assignment operator, so an optional of it cannot be
/// assigned.
class pinned
{
public:
    explicit pinned(int id)
        : id_(id)
    {
    }

    int get_id() const
    {
        return id_;
    }

private:
    const int id_;
};

/// A record with an optional field of a type that is not assignable. The getter is generated. The
/// generator emits no setter, because std::optional of such a type has a deleted assignment, so
/// the record cannot be assigned. The record must still build.
struct pinned_holder
{
    std::optional<pinned> data;
    int marker{};
};

/// A const reference parameter of the handle path for a type that is not assignable. The
/// marshaller builds its storage in place.
inline int take_pinned(const std::optional<pinned>& value)
{
    return value.has_value() ? value->get_id() : -1;
}

/// A trivially copyable struct with const members. It is a configured value type: it crosses in
/// fixed_pair_abi form. An optional of it has no usable assignment operator.
struct fixed_pair
{
    const int first;
    const int second;
};

inline int fixed_pair_sum(const std::optional<fixed_pair>& value)
{
    return value.has_value() ? value->first + value->second : -1;
}

inline std::optional<fixed_pair> make_fixed_pair(bool has_value)
{
    if (has_value)
        return fixed_pair{3, 4};
    return std::nullopt;
}

/// An optional-like class with an assignment operator that is not constrained, as the optional
/// of EASTL has. The generator sees an assignable class, so it emits a setter for a field of this
/// type. The setter must not assign: the value type is not assignable.
template <class T>
class loose_optional
{
public:
    using value_type = T;

    loose_optional() = default;

    loose_optional(const T& value)
        : storage_(value)
    {
    }

    loose_optional(T&& value)
        : storage_(std::move(value))
    {
    }

    loose_optional(const loose_optional&) = default;

    loose_optional& operator=(const loose_optional& other)
    {
        storage_ = other.storage_;
        return *this;
    }

    bool has_value() const
    {
        return storage_.has_value();
    }

    void reset()
    {
        storage_.reset();
    }

    T& emplace(T&& value)
    {
        return storage_.emplace(std::move(value));
    }

    T& operator*()
    {
        return *storage_;
    }

    const T& operator*() const
    {
        return *storage_;
    }

private:
    std::optional<T> storage_;
};

/// A record with a field of loose_optional over a type that is not assignable. The generated
/// setter goes through reset() and emplace().
struct loose_holder
{
    loose_optional<pinned> data;
    int marker{};
};

/// A record with an optional handle field of an assignable class.
struct item_holder
{
    std::optional<item> held;
};

/// A class that cannot be copied. The handle path copies the value, so an optional of it is not
/// bound. The generator must ignore the function instead of failing the build.
class move_only
{
public:
    move_only() = default;
    move_only(move_only&&) = default;
    move_only& operator=(move_only&&) = default;
    move_only(const move_only&) = delete;
    move_only& operator=(const move_only&) = delete;

    int value() const
    {
        return 5;
    }
};

inline std::optional<move_only> make_move_only()
{
    return move_only{};
}

// The native size of the optional ABI struct for a value T. OptionalValuesMarshallers.h checks
// that these sizes are the sizes of the Marshaller<std::optional<T>>::abi_type structs.
constexpr std::size_t optional_int_abi_size()
{
    struct abi
    {
        int value;
        bool has_value;
    };
    return sizeof(abi);
}

constexpr std::size_t optional_double_abi_size()
{
    struct abi
    {
        double value;
        bool has_value;
    };
    return sizeof(abi);
}

constexpr std::size_t optional_mode_abi_size()
{
    struct abi
    {
        std::underlying_type_t<mode> value;
        bool has_value;
    };
    return sizeof(abi);
}

constexpr std::size_t optional_point_abi_size()
{
    struct abi
    {
        point value;
        bool has_value;
    };
    return sizeof(abi);
}
}
