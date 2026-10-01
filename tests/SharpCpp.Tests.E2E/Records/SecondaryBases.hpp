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
