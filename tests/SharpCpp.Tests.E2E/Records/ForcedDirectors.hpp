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

protected:
    int seed_{};
};

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
