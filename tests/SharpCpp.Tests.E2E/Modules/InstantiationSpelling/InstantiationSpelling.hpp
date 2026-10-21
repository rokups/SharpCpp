#pragma once

// The generator must accept this header. Each record has one specialization with several
// spellings: a defaulted argument, an explicit default, an alias and a defaulted nested argument.
// It is not part of the E2E native library.

namespace hidden_argument_spelling
{
struct spelled_item
{
    virtual ~spelled_item() = default;

    int value = 0;
};

struct default_tag
{
    static constexpr int value = 1;
};

using default_tag_alias = default_tag;

template <typename T, typename U = int>
struct tag_list
{
    static constexpr int value = 2;
};

using int_tag_list = tag_list<int>;

/// The managed name "SpelledBox<{0}>" uses only TValue.
template <typename TValue, typename TTag = default_tag>
struct spelled_box
{
    int tag() const { return TTag::value; }

    TValue value{};
};

using default_spelled_box = spelled_box<spelled_item*>;

inline spelled_box<spelled_item*>* make_box_with_defaulted_tag() { return new spelled_box<spelled_item*>(); }

inline spelled_box<spelled_item*, default_tag>* make_box_with_explicit_tag()
{
    return new spelled_box<spelled_item*, default_tag>();
}

inline spelled_box<spelled_item*, default_tag_alias>* make_box_with_tag_alias()
{
    return new spelled_box<spelled_item*, default_tag_alias>();
}

inline default_spelled_box* make_box_through_alias() { return new default_spelled_box(); }

/// The managed name "NestedBox<{0}>" uses only TValue. TTag has a defaulted nested argument.
template <typename TValue, typename TTag>
struct nested_box
{
    int tag() const { return TTag::value; }

    TValue value{};
};

inline nested_box<spelled_item*, tag_list<int>>* make_nested_box_with_defaulted_argument()
{
    return new nested_box<spelled_item*, tag_list<int>>();
}

inline nested_box<spelled_item*, tag_list<int, int>>* make_nested_box_with_explicit_argument()
{
    return new nested_box<spelled_item*, tag_list<int, int>>();
}

inline nested_box<spelled_item*, int_tag_list>* make_nested_box_through_alias()
{
    return new nested_box<spelled_item*, int_tag_list>();
}
}
