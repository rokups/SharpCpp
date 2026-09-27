#pragma once

struct [[sharpcpp::ignore]] skipped_record
{
    int value;
    int get_value() const { return value; }
};

struct [[sharpcpp::ignore("not yet supported")]] skipped_record_with_reason
{
    int count;
};

struct partially_ignored_record
{
    partially_ignored_record() {}

    [[sharpcpp::ignore("internal use only")]]
    int internal_method() { return 42; }

    [[sharpcpp::ignore]]
    int skipped_field;

    int visible_field;

    [[sharpcpp::ignore("debug constructor")]]
    partially_ignored_record(int debug_value) {}

    [[sharpcpp::ignore]]
    static int skipped_static() { return 0; }

    [[sharpcpp::ignore]]
    partially_ignored_record operator+(const partially_ignored_record& other) const { return {}; }
};

[[sharpcpp::ignore("manual stub")]]
inline int skipped_free_function(int x) { return x * 2; }

enum class [[sharpcpp::ignore]] skipped_enum
{
    value_a = 0,
    value_b = 1
};

#define SHARPCPP_IGNORED_PAIR(NAME) \
    struct [[sharpcpp::ignore]] NAME##_macro_record { int value; }; \
    enum class [[sharpcpp::ignore]] NAME##_macro_enum { first = 0, second = 1 };

SHARPCPP_IGNORED_PAIR(macro_generated)

// A comment-only annotation directly above a declaration is honoured by the prescan.
// [[sharpcpp::ignore("prescan only")]]
inline int prescan_ignored_function(int value)
{
    return value;
}

struct deleted_box
{
    deleted_box() {}
    deleted_box(int) = delete;

    deleted_box& operator=(const deleted_box&) = delete;
    int deleted_method() = delete;

    int value() const
    {
        return 3;
    }
};

inline int ignore_surface_round_trip(int value)
{
    return value + 1;
}
