#pragma once

#include <SharpCpp/ManagedCallback.hpp>

#include <string>
#include <utility>

#include "EaFunction.h"

// The ea::function (eastl::function) forms of the std::function callback fixtures. SharpCpp
// must support both function object types in the same way.

struct ea_callback_box
{
    int value_{};

    int get_value()
    {
        return value_;
    }

    void set_value(int value)
    {
        value_ = value;
    }
};

inline void ea_invoke_void_callback(ea::function<void(int)> callback, int value)
{
    callback(value);
}

inline int ea_invoke_int_callback(ea::function<int(int, int)> callback, int lhs, int rhs)
{
    return callback(lhs, rhs);
}

inline int ea_invoke_copied_callback(ea::function<int(int)> callback, int value)
{
    auto copied = callback;
    return copied(value);
}

inline ea::function<int(int)>& ea_retained_callback()
{
    static ea::function<int(int)> callback;
    return callback;
}

inline void ea_retain_callback(ea::function<int(int)> callback)
{
    ea_retained_callback() = std::move(callback);
}

inline int ea_invoke_retained_callback(int value)
{
    return ea_retained_callback()(value);
}

inline void ea_clear_retained_callback()
{
    ea_retained_callback() = nullptr;
}

inline int ea_invoke_object_pointer_callback(ea::function<void(ea_callback_box*)> callback, int value)
{
    static ea_callback_box box{};
    box.set_value(value);
    callback(&box);
    return box.get_value();
}

inline int ea_invoke_primitive_reference_callback(ea::function<void(int&)> callback, int value)
{
    int local = value;
    callback(local);
    return local;
}

inline int ea_invoke_string_reference_callback(const ea::function<int(const std::string&)>& callback)
{
    static const std::string value = "SharpCpp";
    return callback(value);
}

inline int ea_invoke_generated_delegate_callback([[sharpcpp::delegate]] ea::function<int(int)> callback, int value)
{
    return callback(value);
}

inline bool ea_invoke_rvalue_callback(ea::function<bool(int)>&& callback, int value)
{
    return callback(value);
}

// An alias of a callback with a return value.
using ea_transform = ea::function<int(int)>;

inline int ea_apply_transform(const ea_transform& transform, int value)
{
    return transform(value);
}

inline int ea_apply_transform_twice(ea_transform transform, int value)
{
    return transform(transform(value));
}

// A ManagedCallback converts to an ea::function.
inline int ea_invoke_through_managed_callback(SharpCpp::ManagedCallback<int(int)> callback, int value)
{
    ea::function<int(int)> function = callback;
    return function(value);
}

// The alias has a reference parameter. The record is not a director, so the virtual method is a
// plain member function.
using ea_position_callback = ea::function<void(int, float&)>;

struct ea_agent
{
    ea_agent() = default;
    virtual ~ea_agent() = default;

    virtual void set_position_callback(const ea_position_callback& callback)
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
    ea_position_callback callback_;
};

// A director gives a native ea::function to a managed override, as for std::function.
class [[sharpcpp::director]] ea_visitor
{
public:
    virtual ~ea_visitor() = default;

    virtual void move(const ea_position_callback& callback)
    {
        float position = 1.0f;
        callback(1, position);
    }

    virtual int combine(const ea::function<int(int, int)>& callback)
    {
        return callback(1, 2);
    }

    virtual int sink(ea_transform callback)
    {
        return callback(1);
    }

    virtual int sink_rvalue(ea::function<int(int)>&& callback)
    {
        return callback(2);
    }

    // A bool callback parameter cannot cross to a managed override, so this virtual is not bound.
    virtual int visit(const ea::function<int(bool)>& callback)
    {
        return callback(true);
    }
};

inline float ea_call_move(ea_visitor& visitor, int scale)
{
    float last = -1.0f;
    visitor.move([&last, scale](int index, float& position)
    {
        position += static_cast<float>(index * scale);
        last = position;
    });
    return last;
}

inline int ea_call_combine(ea_visitor& visitor, int factor)
{
    return visitor.combine([factor](int left, int right) { return (left + right) * factor; });
}

inline int ea_call_combine_empty(ea_visitor& visitor)
{
    return visitor.combine(ea::function<int(int, int)>{});
}

inline int ea_call_sink(ea_visitor& visitor, int factor)
{
    ea_transform source = [factor](int value) { return value * factor; };
    return visitor.sink(std::move(source));
}

inline int ea_call_sink_rvalue(ea_visitor& visitor, int factor)
{
    ea::function<int(int)> source = [factor](int value) { return value * factor; };
    return visitor.sink_rvalue(std::move(source));
}

inline int ea_call_visit(ea_visitor& visitor)
{
    return visitor.visit([](bool value) { return value ? 20 : 0; });
}

// The forms that std::function does not support either: a callback return value, a callback
// field and a mutable callback reference.
inline ea::function<void(int)> ea_get_callback()
{
    return {};
}

struct ea_callback_owner
{
    ea::function<void(int)> callback;
    int value{};
};

inline void ea_replace_callback(ea::function<void(int)>& callback)
{
    callback = nullptr;
}
