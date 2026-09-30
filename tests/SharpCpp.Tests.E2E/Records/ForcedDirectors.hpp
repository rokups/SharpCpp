#pragma once

namespace forced_directors
{
/// Abstract with a user constructor; the generator driver forces it director-capable.
class forced_abstract
{
public:
    explicit forced_abstract(int seed)
        : seed_(seed)
    {
    }

    virtual ~forced_abstract() = default;

    virtual int compute() const = 0;

    int seed() const
    {
        return seed_;
    }

protected:
    int seed_{};
};

/// The same shape without forcing: its constructor needs a director, so it is unbound.
class unforced_abstract
{
public:
    explicit unforced_abstract(int seed)
        : seed_(seed)
    {
    }

    virtual ~unforced_abstract() = default;

    virtual int compute() const = 0;

    int seed() const
    {
        return seed_;
    }

protected:
    int seed_{};
};

/// Bound concrete derived of a director-less abstract.
class unforced_concrete : public unforced_abstract
{
public:
    unforced_concrete()
        : unforced_abstract(3)
    {
    }

    int compute() const override
    {
        return 70;
    }
};

/// Director-less abstract derived that adds a pure virtual and inherits compute() unimplemented.
class unforced_abstract_child : public unforced_abstract
{
public:
    explicit unforced_abstract_child(int seed)
        : unforced_abstract(seed)
    {
    }

    virtual int twice() const = 0;
};

/// Director-less abstract derived that implements the inherited pure virtual and adds its own.
class unforced_partial_child : public unforced_abstract
{
public:
    explicit unforced_partial_child(int seed)
        : unforced_abstract(seed)
    {
    }

    int compute() const override
    {
        return seed_ + 1;
    }

    virtual int extra() const = 0;
};

inline unforced_partial_child* hidden_unforced_partial_child()
{
    struct hidden final : unforced_partial_child
    {
        hidden()
            : unforced_partial_child(8)
        {
        }

        int extra() const override
        {
            return 100 + seed_;
        }
    };

    static hidden instance;
    return &instance;
}

/// Returns an instance of an implementation the generator never sees.
inline unforced_abstract* hidden_unforced_abstract()
{
    struct hidden final : unforced_abstract
    {
        hidden()
            : unforced_abstract(5)
        {
        }

        int compute() const override
        {
            return 40 + seed_;
        }
    };

    static hidden instance;
    return &instance;
}

inline unforced_abstract& hidden_unforced_abstract_ref()
{
    return *hidden_unforced_abstract();
}

inline unforced_abstract* null_unforced_abstract()
{
    return nullptr;
}

inline unforced_abstract* shared_unforced_concrete()
{
    static unforced_concrete instance;
    return &instance;
}

inline unforced_abstract_child* hidden_unforced_abstract_child()
{
    struct hidden final : unforced_abstract_child
    {
        hidden()
            : unforced_abstract_child(6)
        {
        }

        int compute() const override
        {
            return 10 + seed_;
        }

        int twice() const override
        {
            return 2 * seed_;
        }
    };

    static hidden instance;
    return &instance;
}

/// Forced, no user constructor: the implicit default constructor is materialized.
class forced_implicit_abstract
{
public:
    virtual ~forced_implicit_abstract() = default;

    virtual int compute() const = 0;
};

class copyable_base
{
};

/// Forced, derived: implicit copy and move constructors are not bound.
class forced_derived_abstract : public copyable_base
{
public:
    virtual ~forced_derived_abstract() = default;

    virtual int compute() const = 0;
};

inline int compute_forced_abstract(const forced_abstract* value)
{
    return value->compute() + value->seed();
}

inline int compute_forced_implicit_abstract(const forced_implicit_abstract* value)
{
    return value->compute();
}

inline int compute_forced_derived_abstract(const forced_derived_abstract* value)
{
    return value->compute();
}
}
