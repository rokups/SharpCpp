#pragma once

#include <functional>

namespace phase2_breadth
{
struct phase2_value;
using phase2_alias = phase2_value;
using phase2_callback = std::function<int(phase2_alias*)>;

phase2_alias* phase2_forward_identity(phase2_alias* value);
int phase2_invoke_alias_callback(const phase2_callback& callback, phase2_alias* value);

struct phase2_value
{
    phase2_value() = default;

    explicit phase2_value(int value)
        : value_(value)
    {
    }

    virtual ~phase2_value() = default;
    virtual int get_value() const { return value_; }
    void set_value(int value) { value_ = value; }

    static int get_type_static() { return 61; }

protected:
    int value_{};
};

struct phase2_derived final : phase2_value
{
    explicit phase2_derived(int value)
        : phase2_value(value)
    {
    }

    int get_value() const final { return value_ + 1; }
    static int get_type_static() { return 71; }
};

struct phase2_value_holder
{
    static inline phase2_value shared{47};

    phase2_value_holder() = default;

    phase2_value value{17};
    phase2_value replacement{29};
    const phase2_value fixed{37};

    int read_value() const { return value.get_value(); }
    static int read_shared() { return shared.get_value(); }
};

inline phase2_alias* phase2_forward_identity(phase2_alias* value)
{
    return value;
}

inline int phase2_invoke_alias_callback(const phase2_callback& callback, phase2_alias* value)
{
    return callback(value) + 1;
}

template <typename T>
T phase2_generic_identity(T value)
{
    return value;
}

template phase2_alias* phase2_generic_identity<phase2_alias*>(phase2_alias* value);

template <typename TValue, typename THidden = int>
struct phase2_box
{
    explicit phase2_box(TValue value)
        : value_(value)
    {
    }

    TValue get_value() const { return value_; }

private:
    TValue value_{};
};

using phase2_alias_box = phase2_box<phase2_alias*, int>;

inline phase2_alias_box* create_phase2_alias_box(phase2_alias* value)
{
    static phase2_alias_box box(value);
    box = phase2_alias_box(value);
    return &box;
}

inline int read_phase2_alias_box(const phase2_alias_box* box)
{
    return box->get_value()->get_value();
}
}
