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

inline int tick_via_tickable(tickable* value)
{
    return value->tick();
}

inline int tick_via_labelled(labelled* value)
{
    return value->tick();
}

}
