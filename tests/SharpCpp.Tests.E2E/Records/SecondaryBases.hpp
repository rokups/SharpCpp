#pragma once

namespace secondary_bases
{
struct primary
{
    virtual ~primary() = default;

    int primary_value() const
    {
        return 1;
    }
};

struct runnable
{
    virtual ~runnable() = default;
    virtual int run() = 0;
};

/// Overrides the secondary base's pure virtual privately: the class is still concrete.
class hidden_runner : public primary, public runnable
{
private:
    int run() override
    {
        return 5;
    }
};

/// A secondary base that itself has a secondary base: ILabelled : ITickable must not redeclare tick().
struct tickable
{
    virtual ~tickable() = default;
    virtual int tick() = 0;

    virtual tickable* self()
    {
        return this;
    }

    int stored_rate = 3;

    int get_rate() const
    {
        return stored_rate;
    }

    void set_rate(int value)
    {
        stored_rate = value;
    }
};

struct label_base
{
    virtual ~label_base() = default;
};

struct labelled : public label_base, public tickable
{
    int labelled_stored_rate = 4;

    labelled* self() override
    {
        return this;
    }

    int get_rate() const
    {
        return labelled_stored_rate;
    }

    void set_rate(int value)
    {
        labelled_stored_rate = value;
    }

    int label() const
    {
        return 7;
    }
};

class clock : public primary, public labelled
{
public:
    int tick() override
    {
        return 9;
    }
};

/// Non-view classes (never a secondary base themselves) that reach tickable's accessors through a secondary base.
/// (a) redeclares both accessors, hiding the base ones.
class gauge : public primary, public tickable
{
public:
    int gauge_rate = 20;

    int tick() override
    {
        return 21;
    }

    int get_rate() const
    {
        return gauge_rate;
    }

    void set_rate(int value)
    {
        gauge_rate = value;
    }
};

/// (b) redeclares only the getter.
class meter : public primary, public tickable
{
public:
    int tick() override
    {
        return 22;
    }

    int get_rate() const
    {
        return 100 + stored_rate;
    }
};

/// (d) redeclares only the setter.
class splitter : public primary, public tickable
{
public:
    int split_rate = 30;

    int tick() override
    {
        return 23;
    }

    void set_rate(int value)
    {
        split_rate = value;
    }
};

/// (c) the accessors come through the primary chain while tickable is a secondary base.
class rated_base : public primary
{
public:
    int rated_value = 40;

    int get_rate() const
    {
        return rated_value;
    }

    void set_rate(int value)
    {
        rated_value = value;
    }
};

class rated_ticker : public rated_base, public tickable
{
public:
    int tick() override
    {
        return 24;
    }
};

/// Covariant overrides of secondary-base virtuals: the managed interface keeps the base return type.
struct pulse
{
    virtual ~pulse() = default;
    virtual pulse* source() = 0;
};

struct beacon
{
    virtual ~beacon() = default;
    virtual beacon* origin()
    {
        return this;
    }
};

class lighthouse : public primary, public pulse, public beacon
{
public:
    lighthouse* source() override
    {
        return this;
    }

    lighthouse* origin() override
    {
        return this;
    }
};

struct [[sharpcpp::director]] signal_tower : public primary, public beacon
{
    signal_tower* origin() override
    {
        return this;
    }
};

/// A covariant override on the secondary base's own primary chain.
struct origin_base
{
    virtual ~origin_base() = default;
    virtual origin_base* root()
    {
        return this;
    }
};

struct origin_mid : public origin_base
{
    origin_mid* root() override
    {
        return this;
    }
};

class origin_user : public primary, public origin_mid
{
};

/// Non-virtual hiding inside a secondary base's own chain: the most-derived declaration (doc, default, body) is the observable one.
struct scale_base
{
    virtual ~scale_base() = default;

    /// Scales by the base factor.
    int scale(int factor = 2) const
    {
        return 10 * factor;
    }
};

struct scale_hiding : public scale_base
{
    /// Scales by the hiding factor.
    int scale(int factor = 3) const
    {
        return 100 * factor;
    }
};

class scale_holder : public primary, public scale_hiding
{
};

inline int tick_via_tickable(tickable* value)
{
    return value->tick();
}

inline int tick_via_labelled(labelled* value)
{
    return value->tick();
}

/// Non-virtual hiding of a secondary-base method with another return type: the interface keeps the
/// base return type, so the classes implement it explicitly through the base declaration.
struct rate_source
{
    int rate() const
    {
        return 5;
    }
};

class rate_meter : public primary, public rate_source
{
public:
    double rate() const
    {
        return 2.5;
    }
};

struct rate_scaler : public label_base, public rate_source
{
    double rate() const
    {
        return 2.5;
    }
};

class rate_panel : public primary, public rate_scaler
{
};

inline int rate_via_source(rate_source* source)
{
    return source->rate();
}

/// A covariant override on a secondary base's primary chain, where the overridden base is itself a
/// rendered interface (trail_direct makes trail_base one).
struct trail_base
{
    virtual ~trail_base() = default;
    virtual trail_base* head()
    {
        return this;
    }

    int base_tag() const
    {
        return 1;
    }
};

struct trail_mid : public trail_base
{
    trail_mid* head() override
    {
        return this;
    }

    int mid_tag() const
    {
        return 2;
    }
};

class trail_user : public primary, public trail_mid
{
};

class trail_direct : public primary, public trail_base
{
};

inline trail_base* trail_head_via_base(trail_base* value)
{
    return value->head();
}

struct ridge_root
{
    virtual ~ridge_root() = default;

    int level() const
    {
        return 1;
    }

    int root_only() const
    {
        return 4;
    }
};

struct ridge_left : public virtual ridge_root
{
    int left_tag() const
    {
        return 3;
    }
};

/// Hides ridge_root::level, which wins over the declaration reached through ridge_left (dominance).
struct ridge_right : public virtual ridge_root
{
    int level() const
    {
        return 2;
    }
};

struct ridge_join : public ridge_left, public ridge_right
{
};

class ridge_user : public primary, public ridge_join
{
};

inline int ridge_level_via_join(ridge_join* value)
{
    return value->level();
}

struct span_base
{
    virtual ~span_base() = default;

    int span() const
    {
        return 1;
    }

    int span(int n) const
    {
        return n;
    }
};

/// Hides both span_base::span overloads by declaring one with another parameter list.
struct span_mid : public span_base
{
    int span(int a, int b) const
    {
        return a + b;
    }
};

class span_user : public primary, public span_mid
{
};

}
