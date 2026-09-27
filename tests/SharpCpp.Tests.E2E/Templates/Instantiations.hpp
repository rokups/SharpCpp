#pragma once

#include <functional>
#include <map>
#include <string_view>
#include <vector>

namespace instantiations
{
/// The map instantiation is only named inside the callback signature.
inline int invoke_with_map(const std::function<int(const std::map<int, int>&)>& callback)
{
    std::map<int, int> values{{1, 10}, {2, 20}};
    return callback(values);
}

/// A generic record whose member uses an open container type: that member is not bound
/// and does not make the container an instantiation.
template <typename T>
struct [[sharpcpp::instantiate("int")]] open_member_box
{
    int count(const std::vector<T>& values) const
    {
        return static_cast<int>(values.size());
    }

    T identity(T value) const
    {
        return value;
    }
};

struct default_tag
{
};

/// A template with a defaulted argument is instantiated from uses that omit it.
template <typename T, typename Tag = default_tag>
struct tagged_box
{
    T value{};

    T get() const
    {
        return value;
    }
};

inline tagged_box<int>* create_tagged_box(int value)
{
    static tagged_box<int> box;
    box.value = value;
    return &box;
}

/// The explicit instantiation survives; the one inferred from a degraded use does not.
template <typename T>
struct [[sharpcpp::instantiate("unsigned int")]] explicit_box
{
    T value{};
};

inline int degraded_explicit_box_use(explicit_box<int>* box, int&& moved)
{
    return box->value + moved;
}

/// Instantiation arguments must cover every template parameter.
template <typename To, typename From>
[[sharpcpp::instantiate("unsigned int")]]
To bit_cast_value(From value)
{
    return static_cast<To>(value);
}

/// Containers of ref-struct projections cannot be instantiated.
inline int count_views(const std::vector<std::string_view>& values)
{
    return static_cast<int>(values.size());
}
}

namespace instantiations
{
inline explicit_box<unsigned int>* create_explicit_box(unsigned int value)
{
    static explicit_box<unsigned int> box;
    box.value = value;
    return &box;
}

inline open_member_box<int>* create_open_member_box()
{
    static open_member_box<int> box;
    return &box;
}
}

#include <locale>

namespace instantiations
{
struct [[sharpcpp::ignore("instantiation argument coverage")]] ignored_item
{
    int value{};
};

/// Containers of an unresolvable external type or of an ignored record are not bound.
inline int count_locales(const std::vector<std::locale*>& values)
{
    return static_cast<int>(values.size());
}

inline int count_ignored_items(const std::vector<ignored_item*>& values)
{
    return static_cast<int>(values.size());
}
}
