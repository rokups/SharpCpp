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
