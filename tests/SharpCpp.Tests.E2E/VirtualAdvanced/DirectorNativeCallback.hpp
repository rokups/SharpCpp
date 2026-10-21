#pragma once

#include <functional>

#include "../Callback/EaFunction.h"

// A director gives a native function object to a managed override. The override gets a delegate
// that calls the native function object. For a const reference parameter, the delegate is valid
// only during the override call, and no native memory is allocated. A call after the override
// returns throws ObjectDisposedException in managed code. For a by-value or rvalue-reference
// parameter (a sink), the function object moves into a native holder that the delegate owns.
using dnc_position_callback = std::function<void(int, float&)>;

// A function object that counts its instances. A moved-from instance is not live.
struct [[sharpcpp::ignore("native test helper")]] dnc_counted_function
{
    static inline int copies = 0;
    static inline int live = 0;
    static inline int destroyed = 0;

    explicit dnc_counted_function(int factor)
        : factor_(factor)
    {
        ++live;
    }

    dnc_counted_function(const dnc_counted_function& other)
        : factor_(other.factor_), alive_(other.alive_)
    {
        ++copies;
        if (alive_)
        {
            ++live;
        }
    }

    dnc_counted_function(dnc_counted_function&& other) noexcept
        : factor_(other.factor_), alive_(other.alive_)
    {
        other.alive_ = false;
    }

    dnc_counted_function& operator=(const dnc_counted_function&) = delete;
    dnc_counted_function& operator=(dnc_counted_function&&) = delete;

    ~dnc_counted_function()
    {
        if (alive_)
        {
            --live;
            ++destroyed;
        }
    }

    int operator()(int value) const
    {
        return value * factor_;
    }

private:
    int factor_{};
    bool alive_{true};
};

inline void dnc_reset_counts()
{
    dnc_counted_function::copies = 0;
    dnc_counted_function::live = 0;
    dnc_counted_function::destroyed = 0;
}

inline int dnc_copy_count()
{
    return dnc_counted_function::copies;
}

inline int dnc_live_count()
{
    return dnc_counted_function::live;
}

inline int dnc_destroyed_count()
{
    return dnc_counted_function::destroyed;
}

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

    // A sink parameter by value: the override can keep the delegate.
    virtual int sink(std::function<int(int)> callback)
    {
        return callback(1);
    }

    // A sink parameter by rvalue reference: the override can keep the delegate.
    virtual int sink_rvalue(std::function<int(int)>&& callback)
    {
        return callback(2);
    }

    // A const reference: the override can copy the function object with Retain().
    virtual int borrow(const std::function<int(int)>& callback)
    {
        return callback(3);
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

// The source function object moves into the parameter, and the director moves it into the holder.
// The source and the parameter are destroyed before this function returns.
inline int dnc_call_sink(dnc_visitor& visitor, int factor)
{
    std::function<int(int)> source = dnc_counted_function(factor);
    return visitor.sink(std::move(source));
}

// The director moves the source function object into the holder through the rvalue reference.
inline int dnc_call_sink_rvalue(dnc_visitor& visitor, int factor)
{
    std::function<int(int)> source = dnc_counted_function(factor);
    return visitor.sink_rvalue(std::move(source));
}

inline int dnc_call_borrow(dnc_visitor& visitor, int factor)
{
    const std::function<int(int)> source = dnc_counted_function(factor);
    return visitor.borrow(source);
}

inline int dnc_call_sink_empty(dnc_visitor& visitor)
{
    return visitor.sink(std::function<int(int)>{});
}

// A managed delegate that a director gives back to native code.
inline int dnc_invoke(const std::function<int(int)>& callback, int value)
{
    return callback(value);
}

// The ea::function (eastl::function) form of the director fixture above. The function object
// lifetime rules are the same as for std::function. The counters of dnc_counted_function are
// shared.
using ea_dnc_position_callback = ea::function<void(int, float&)>;

class [[sharpcpp::director]] ea_dnc_visitor
{
public:
    virtual ~ea_dnc_visitor() = default;

    virtual void move(const ea_dnc_position_callback& callback)
    {
        float position = 1.0f;
        callback(1, position);
    }

    virtual int combine(const ea::function<int(int, int)>& callback)
    {
        return callback(1, 2);
    }

    virtual double scale(ea::function<double(double)> callback)
    {
        return callback(0.5);
    }

    virtual int sink(ea::function<int(int)> callback)
    {
        return callback(1);
    }

    virtual int sink_rvalue(ea::function<int(int)>&& callback)
    {
        return callback(2);
    }

    virtual int borrow(const ea::function<int(int)>& callback)
    {
        return callback(3);
    }

    virtual dnc_mode choose(const ea::function<dnc_mode(dnc_mode)>& callback)
    {
        return callback(dnc_mode::first);
    }

    // A noexcept signature of the function object (the stand-in of EaFunction.h).
    virtual int borrow_noexcept(const ea::function<int(int) noexcept>& callback)
    {
        return callback(4);
    }

    virtual int sink_noexcept(ea::function<int(int) noexcept> callback)
    {
        return callback(5);
    }
};

inline float ea_dnc_call_move(ea_dnc_visitor& visitor, int scale)
{
    float last = -1.0f;
    visitor.move([&last, scale](int index, float& position)
    {
        position += static_cast<float>(index * scale);
        last = position;
    });
    return last;
}

inline int ea_dnc_call_combine(ea_dnc_visitor& visitor, int factor)
{
    return visitor.combine([factor](int left, int right) { return (left + right) * factor; });
}

inline int ea_dnc_call_combine_empty(ea_dnc_visitor& visitor)
{
    return visitor.combine(ea::function<int(int, int)>{});
}

inline double ea_dnc_call_scale(ea_dnc_visitor& visitor)
{
    return visitor.scale([](double value) { return value * 4.0; });
}

inline dnc_mode ea_dnc_call_choose(ea_dnc_visitor& visitor)
{
    return visitor.choose([](dnc_mode mode) { return mode == dnc_mode::first ? dnc_mode::second : dnc_mode::first; });
}

inline int ea_dnc_call_sink(ea_dnc_visitor& visitor, int factor)
{
    ea::function<int(int)> source = dnc_counted_function(factor);
    return visitor.sink(std::move(source));
}

inline int ea_dnc_call_sink_rvalue(ea_dnc_visitor& visitor, int factor)
{
    ea::function<int(int)> source = dnc_counted_function(factor);
    return visitor.sink_rvalue(std::move(source));
}

inline int ea_dnc_call_borrow(ea_dnc_visitor& visitor, int factor)
{
    const ea::function<int(int)> source = dnc_counted_function(factor);
    return visitor.borrow(source);
}

inline int ea_dnc_call_sink_empty(ea_dnc_visitor& visitor)
{
    return visitor.sink(ea::function<int(int)>{});
}

inline int ea_dnc_call_borrow_noexcept(ea_dnc_visitor& visitor, int factor)
{
    return visitor.borrow_noexcept([factor](int value) noexcept { return value * factor; });
}

inline int ea_dnc_call_sink_noexcept(ea_dnc_visitor& visitor, int factor)
{
    return visitor.sink_noexcept([factor](int value) noexcept { return value * factor; });
}

// A managed delegate that a director gives back to native code.
inline int ea_dnc_invoke(const ea::function<int(int)>& callback, int value)
{
    return callback(value);
}
