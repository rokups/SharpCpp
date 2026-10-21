#pragma once

// The generator must reject this header. It is not part of the E2E native library.

namespace hidden_argument_conflict
{
struct hidden_item
{
    hidden_item() = default;

    explicit hidden_item(int value)
        : value_(value)
    {
    }

    virtual ~hidden_item() = default;

    int get_value() const { return value_; }

private:
    int value_{};
};

struct tag_one
{
    static constexpr int value = 1;
};

struct tag_two
{
    static constexpr int value = 2;
};

/// The managed name "HiddenBox<{0}>" uses only TValue. Two specializations that differ
/// only in TTag thus have one managed class.
template <typename TValue, typename TTag>
struct hidden_box
{
    hidden_box() = default;

    explicit hidden_box(TValue value)
        : value_(value)
    {
    }

    int tag() const { return TTag::value; }

    TValue item() const { return value_; }

private:
    TValue value_{};
};

inline hidden_box<hidden_item*, tag_one>* make_hidden_box_one(hidden_item* item)
{
    return new hidden_box<hidden_item*, tag_one>(item);
}

inline hidden_box<hidden_item*, tag_two>* make_hidden_box_two(hidden_item* item)
{
    return new hidden_box<hidden_item*, tag_two>(item);
}
}
