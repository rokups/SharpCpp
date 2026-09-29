#pragma once

/// A record nested two records deep inside its enclosing scopes:
/// nested_outer::nested_middle::nested_inner. The depth-1 case (a record nested one record
/// deep) is covered by Pointers/PointerShapes.hpp's owner::value.
struct nested_outer
{
    int outer_id() const { return 100; }

    struct nested_middle
    {
        int middle_id() const { return 200; }

        struct nested_inner
        {
            int value{};

            int doubled() const { return value * 2; }

            /// A record nested three records deep:
            /// nested_outer::nested_middle::nested_inner::nested_deepest.
            struct nested_deepest
            {
                int value{};
            };
        };
    };
};

inline nested_outer* create_nested_outer()
{
    return new nested_outer{};
}

inline nested_outer::nested_middle* create_nested_middle()
{
    return new nested_outer::nested_middle{};
}

inline nested_outer::nested_middle::nested_inner* create_nested_inner(int value)
{
    return new nested_outer::nested_middle::nested_inner{value};
}

inline int read_nested_inner(const nested_outer::nested_middle::nested_inner& value)
{
    return value.doubled();
}

inline int read_nested_inner_copy(const nested_outer::nested_middle::nested_inner value)
{
    return value.doubled();
}

inline nested_outer::nested_middle::nested_inner make_nested_inner(int value)
{
    return nested_outer::nested_middle::nested_inner{value};
}

inline nested_outer::nested_middle::nested_inner::nested_deepest* create_nested_deepest(int value)
{
    return new nested_outer::nested_middle::nested_inner::nested_deepest{value};
}

inline int read_nested_deepest(const nested_outer::nested_middle::nested_inner::nested_deepest& value)
{
    return value.value;
}
