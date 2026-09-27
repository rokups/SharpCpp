#pragma once

namespace attribute_syntax
{
/// Attribute tokens may be spaced, split across lines and stacked.
struct [[ sharpcpp :: rename ( "SpacedRename" ) ]] spaced_rename
{
    int value{1};
};

struct [ [ sharpcpp :: rename ( "BracketSpacedRename" ) ] ] bracket_spaced_rename
{
    int value{2};
};

enum class
[[sharpcpp::rename(
    "MultilineFlags"
)]][[sharpcpp::flags]]
multiline_flags : unsigned int
{
    none = 0u,
    first = 1u,
    second = 2u
};

[[sharpcpp::ignore("says \"no\"\nacross lines")]]
inline int escaped_ignore_reason()
{
    return 0;
}

inline multiline_flags combine_multiline_flags(multiline_flags left, multiline_flags right)
{
    return static_cast<multiline_flags>(static_cast<unsigned int>(left) | static_cast<unsigned int>(right));
}
}
