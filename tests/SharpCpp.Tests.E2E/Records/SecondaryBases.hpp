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

}
