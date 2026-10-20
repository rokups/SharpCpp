#pragma once

#include <string>
#include <unordered_map>
#include <vector>

namespace marshaller_config
{
/// Configured as a managed int by a marshaller whose attribute is spread over tokens.
struct level
{
    constexpr level(int initial = 0)
        : value(initial)
    {
    }

    int value;
};

inline int describe(int base, level extra = 5)
{
    return base * 10 + extra.value;
}

/// Configured as a managed uint; used as a closed generic argument of a map value.
struct tag
{
    unsigned value{};
};

/// A pair whose marshaller only matches when both arguments are the same type.
template <class TFirst, class TSecond>
struct twin
{
    TFirst first;
    TSecond second;
};

inline long long pack_twin(twin<int, int> value)
{
    return static_cast<long long>(value.first) * 1000 + value.second;
}

/// Different arguments do not match the configured pattern: an ordinary instantiation.
inline long long pack_mixed(twin<int, short> value)
{
    return value.first + value.second;
}

/// A `const T` pattern parameter binds T to the type without the const, so the second argument
/// must be the same type without the const.
inline long long pack_const_twin(twin<const int, int> value)
{
    return static_cast<long long>(value.first) * 1000 + value.second;
}

/// The second argument differs from the unqualified first argument: no match.
inline long long pack_const_mixed(twin<const int, short> value)
{
    return value.first + value.second;
}

/// A value with two non-type arguments; its marshaller only matches when they are equal.
template <class T, int Rows, int Columns>
struct triple
{
    T value;
};

inline long long pack_square(triple<int, 3, 3> value)
{
    return static_cast<long long>(value.value) * 1000 + 3;
}

/// Different non-type arguments do not match the configured pattern: an ordinary instantiation.
inline long long pack_wide(triple<int, 3, 4> value)
{
    return value.value * 100 + 34;
}

/// A value with one non-type argument; its marshaller binds any of them.
template <class T, int Size>
struct box
{
    T value;
};

inline int unbox(box<int, 5> value)
{
    return value.value + 5;
}

/// A string without contiguous storage: the managed view copies it byte by byte.
class rope
{
public:
    unsigned long long size() const { return text_.size(); }
    unsigned char at(unsigned long long index) const { return static_cast<unsigned char>(text_[index]); }
    void push_back(char value) { text_.push_back(value); }
    void clear() { text_.clear(); }

private:
    std::string text_;
};

inline rope* get_rope()
{
    static rope value;
    if (value.size() == 0)
        for (char c : std::string("knot"))
            value.push_back(c);
    return &value;
}

/// Its marshaller inherits exposed members from a helper base and re-declares one.
struct tally
{
    int count{};
};

inline tally* get_tally()
{
    static tally instance{4};
    return &instance;
}

namespace urho
{
class variant
{
public:
    explicit variant(int value = 0)
        : value_(value)
    {
    }

    int get() const
    {
        return value_;
    }

private:
    int value_{};
};
}

namespace rml
{
class variant
{
public:
    explicit variant(int value = 0)
        : value_(value)
    {
    }

    int get() const
    {
        return value_ * 100;
    }

private:
    int value_{};
};
}

/// Map values name same-named records in sibling namespaces, and a closed vector.
class variant_store
{
public:
    variant_store()
    {
        urho_.emplace("a", urho::variant(1));
        rml_.emplace("a", rml::variant(2));
        tags_[3] = {tag{7}, tag{8}};
    }

    const std::unordered_map<std::string, urho::variant>& get_urho() const
    {
        return urho_;
    }

    const std::unordered_map<std::string, rml::variant>& get_rml() const
    {
        return rml_;
    }

    const std::unordered_map<unsigned, std::vector<tag>>& get_tags() const
    {
        return tags_;
    }

private:
    std::unordered_map<std::string, urho::variant> urho_;
    std::unordered_map<std::string, rml::variant> rml_;
    std::unordered_map<unsigned, std::vector<tag>> tags_;
};
}
