#pragma once

struct mi_left
{
    virtual int left_value() const
    {
        return 10;
    }
};

struct mi_right
{
    virtual int right_value() const
    {
        return 20;
    }

    int right_only() const
    {
        return right_value() + 1;
    }
};

struct [[sharpcpp::director]] mi_counter : mi_left, mi_right
{
    mi_counter() = default;

    int left_value() const override
    {
        return 100;
    }

    int right_value() const override
    {
        return 200;
    }

    int sum_virtuals() const
    {
        return left_value() + right_value();
    }
};

struct mi_pure_right
{
    virtual int pure_right_value() const = 0;
};

struct mi_concrete_private_override : mi_left, mi_pure_right
{
private:
    int pure_right_value() const override
    {
        return 30;
    }
};

// Secondary-base parameters take the IMiRight view; a derived wrapper passes its mi_right
// subobject pointer through the native static_cast accessor.
inline int consume_right(mi_right& right)
{
    return right.right_value();
}

inline int consume_right_ptr(mi_right* right)
{
    return right == nullptr ? -1 : right->right_only();
}

inline mi_counter& shared_mi_counter()
{
    static mi_counter counter;
    return counter;
}

// A polymorphic secondary-base return resolves to the most derived wrapper (MiCounter).
inline mi_right* pick_right()
{
    return &shared_mi_counter();
}

inline mi_right* pick_counter_right(mi_counter& counter)
{
    return &counter;
}

// Non-polymorphic secondary base: parameters still take the IMiPlainRight view, returns keep
// the MiPlainRight class (the complete object cannot be recovered without RTTI).
struct mi_plain_left
{
    int left = 1;

    int plain_left_value() const
    {
        return left;
    }
};

struct mi_plain_right
{
    int right = 2;

    int plain_right_value() const
    {
        return right;
    }
};

struct mi_plain_pair : mi_plain_left, mi_plain_right
{
    mi_plain_pair() = default;
};

inline int consume_plain_right(const mi_plain_right& right)
{
    return right.plain_right_value();
}

inline mi_plain_right* pick_plain_right()
{
    static mi_plain_pair pair;
    pair.right = 5;
    return &pair;
}

// A top-level const pointer is still a view parameter.
inline int consume_right_const_ptr(mi_right* const right)
{
    return right->right_only();
}

// A primary-base pointer to a director-constructed object also resolves to the live wrapper.
inline mi_left* pick_counter_left(mi_counter& counter)
{
    return &counter;
}

// A secondary base shared by two derived records is one view both wrappers implement.
struct mi_nested_inner
{
    virtual int inner_value() const
    {
        return 50;
    }
};

struct mi_nested_pair : mi_left, mi_nested_inner
{
    mi_nested_pair() = default;
};

struct mi_nested_other : mi_left, mi_nested_inner
{
    mi_nested_other() = default;
};

inline int consume_nested_inner(mi_nested_inner& inner)
{
    return inner.inner_value();
}

// A record with a virtual base is ignored (and logged), so its virtual secondary base gets no
// view: the parameter keeps the class type.
struct mi_virtual_right
{
    virtual int virtual_right_value() const
    {
        return 40;
    }
};

struct mi_virtual_pair : mi_left, virtual mi_virtual_right
{
    mi_virtual_pair() = default;
};

inline int consume_virtual_right(mi_virtual_right* right)
{
    return right == nullptr ? -1 : right->virtual_right_value();
}
