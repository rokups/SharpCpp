#pragma once

#include <functional>

// A director gives a native function object to a managed override. The override gets a delegate
// that calls the native function object. The delegate is valid only during the override call.
// A call after the override returns throws ObjectDisposedException in managed code.
using dnc_position_callback = std::function<void(int, float&)>;

enum class dnc_mode
{
    first,
    second
};

class [[sharpcpp::director]] dnc_visitor
{
public:
    virtual ~dnc_visitor() = default;

    // A ref parameter: the override writes the managed value back to the native reference.
    virtual void move(const dnc_position_callback& callback)
    {
        float position = 1.0f;
        callback(1, position);
    }

    // A callback with a return value.
    virtual int combine(const std::function<int(int, int)>& callback)
    {
        return callback(1, 2);
    }

    // A function object by value.
    virtual double scale(std::function<double(double)> callback)
    {
        return callback(0.5);
    }

    // An enum parameter and an enum return value.
    virtual dnc_mode choose(const std::function<dnc_mode(dnc_mode)>& callback)
    {
        return callback(dnc_mode::first);
    }
};

// The native callback adds twice the index to the position and records the result.
inline float dnc_call_move(dnc_visitor& visitor, int scale)
{
    float last = -1.0f;
    visitor.move([&last, scale](int index, float& position)
    {
        position += static_cast<float>(index * scale);
        last = position;
    });
    return last;
}

inline int dnc_call_combine(dnc_visitor& visitor, int factor)
{
    return visitor.combine([factor](int left, int right) { return (left + right) * factor; });
}

inline int dnc_call_combine_empty(dnc_visitor& visitor)
{
    return visitor.combine(std::function<int(int, int)>{});
}

inline double dnc_call_scale(dnc_visitor& visitor)
{
    return visitor.scale([](double value) { return value * 4.0; });
}

inline dnc_mode dnc_call_choose(dnc_visitor& visitor)
{
    return visitor.choose([](dnc_mode mode) { return mode == dnc_mode::first ? dnc_mode::second : dnc_mode::first; });
}
