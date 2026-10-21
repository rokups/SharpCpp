#pragma once

#include <functional>

// The alias has a reference parameter. The only use of the alias is a virtual method.
// The record is not a director, so the virtual method is a plain member function.
using vrc_position_callback = std::function<void(int, float&)>;

struct vrc_agent
{
    vrc_agent() = default;
    virtual ~vrc_agent() = default;

    virtual void set_position_callback(const vrc_position_callback& callback)
    {
        callback_ = callback;
    }

    float apply(int agent, float position) const
    {
        if (callback_)
        {
            callback_(agent, position);
        }
        return position;
    }

private:
    vrc_position_callback callback_;
};
