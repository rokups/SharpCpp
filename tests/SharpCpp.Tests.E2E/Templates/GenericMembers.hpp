#pragma once

namespace generic_members
{
class element
{
public:
    template <typename T>
    T get_attribute(const T& default_value) const
    {
        return default_value;
    }
};

template int element::get_attribute<int>(const int&) const;
template float element::get_attribute<float>(const float&) const;

namespace noise
{
struct vector
{
    using Type = int;
};
}

/// The template parameter shares its name with an unrelated member alias.
template <typename Type>
class [[sharpcpp::instantiate("int")]] result
{
public:
    const Type& get() const
    {
        return value_;
    }

    void set(const Type& value)
    {
        value_ = value;
    }

private:
    Type value_{};
};

class guard
{
public:
    template <typename T>
    explicit guard(const T& value)
        : value_(static_cast<int>(value))
    {
    }

    int value() const
    {
        return value_;
    }

    static guard* create(int value)
    {
        static guard instance(value);
        instance.value_ = value;
        return &instance;
    }

private:
    int value_{};
};

class settings
{
public:
    int apply()
    {
        return ++applied_;
    }

private:
    int applied_{};
};

template <typename T>
class sink
{
public:
    virtual ~sink() = default;
    virtual void set(T value) = 0;
};

class int_sink : public sink<int>
{
public:
    void set(int value) override
    {
        value_ = value;
    }

    int get() const
    {
        return value_;
    }

private:
    int value_{};
};

class explicit_settings
{
public:
    explicit explicit_settings(int value)
        : value_(value)
    {
    }

    int value() const
    {
        return value_;
    }

private:
    int value_{};
};

template <typename T>
class inherited_base
{
public:
    explicit inherited_base(T value)
        : value_(value)
    {
    }

    T value() const
    {
        return value_;
    }

private:
    T value_{};
};

struct inherited_derived : inherited_base<int>
{
    using inherited_base<int>::inherited_base;
};

struct hit_result
{
    int id{};
};

template <typename TResult>
class hit_collector
{
public:
    virtual ~hit_collector() = default;
    virtual void add_hit(const TResult& result) = 0;
};

template <typename TCollector>
class all_hits : public TCollector
{
public:
    using result_type = hit_result;

    void add_hit(const result_type& result) override
    {
        hit = result;
        had_hit_ = true;
    }

    bool had_hit() const
    {
        return had_hit_;
    }

    result_type hit;

private:
    bool had_hit_{};
};

using hit_result_collector = hit_collector<hit_result>;
using hit_results [[sharpcpp::materialize]] = all_hits<hit_result_collector>;
static_assert(sizeof(hit_results) > 0);

inline int add_hit_result(hit_result_collector* collector, int id)
{
    collector->add_hit(hit_result{id});
    return id;
}
}

namespace generic_members
{
inline result<int>* create_int_result(int value)
{
    static result<int> instance;
    instance.set(value);
    return &instance;
}
}

namespace generic_members
{
struct pair_traits
{
};

template <typename TResult, typename TTraits>
class pair_collector
{
public:
    virtual ~pair_collector() = default;

    int collected() const
    {
        return count_;
    }

protected:
    int count_{};
};

/// Derives from its type parameter; the alias closes it over a two-argument template.
template <typename TCollector>
class counting_hits : public TCollector
{
public:
    void hit()
    {
        ++this->count_;
    }
};

using pair_result_collector = pair_collector<hit_result, pair_traits>;
using pair_hits = counting_hits<pair_result_collector>;

inline pair_hits* create_pair_hits()
{
    static pair_hits instance;
    return &instance;
}
}

namespace generic_members
{
inline int count_pair_hits(const pair_hits* value)
{
    return value->collected();
}
}

namespace generic_members
{
template <typename T>
class adder
{
public:
    virtual ~adder() = default;
    virtual void add(const T& value) = 0;
};

/// The concrete override of a generic `const T&` virtual overrides it in managed code too.
class int_adder : public adder<int>
{
public:
    void add(const int& value) override
    {
        total_ += value;
    }

    int total() const
    {
        return total_;
    }

private:
    int total_{};
};

inline int add_through_base(adder<int>* target, int value)
{
    target->add(value);
    return value;
}
}
