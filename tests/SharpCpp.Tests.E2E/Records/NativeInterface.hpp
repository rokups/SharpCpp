#pragma once

struct [[sharpcpp::native_interface]] native_interface_target
{
    virtual int read() = 0;
};

struct [[sharpcpp::native_interface]] native_interface_counter
{
    virtual int get_value() = 0;
    virtual int sum_targets([[sharpcpp::in]] native_interface_target** targets, unsigned int count) = 0;
};

struct [[sharpcpp::ignore]] native_interface_target_impl final : native_interface_target
{
    explicit native_interface_target_impl(int value)
        : value_(value)
    {
    }

    int read() override
    {
        return value_;
    }

    int value_{};
};

struct [[sharpcpp::ignore]] native_interface_counter_impl final : native_interface_counter
{
    explicit native_interface_counter_impl(int value)
        : value_(value)
    {
    }

    int get_value() override
    {
        return value_;
    }

    int sum_targets(native_interface_target** targets, unsigned int count) override
    {
        int result = 0;
        for (unsigned int i = 0; i < count; ++i)
        {
            if (targets[i] != nullptr)
            {
                result += targets[i]->read();
            }
        }
        return result;
    }

    int value_{};
};

inline native_interface_counter* get_native_interface_counter()
{
    static native_interface_counter_impl counter{42};
    return &counter;
}

inline void create_native_interface_counter([[sharpcpp::out]] native_interface_counter** counter)
{
    static native_interface_counter_impl instance{73};
    *counter = &instance;
}

inline void replace_native_interface_counter([[sharpcpp::ref]] native_interface_counter** counter)
{
    static native_interface_counter_impl instance{91};
    *counter = &instance;
}

template <typename T>
[[sharpcpp::instantiate("native_interface_counter*")]]
native_interface_counter* generic_native_interface_identity(T value)
{
    return value;
}

inline native_interface_target* get_native_interface_target_a()
{
    static native_interface_target_impl target{10};
    return &target;
}

inline native_interface_target* get_native_interface_target_b()
{
    static native_interface_target_impl target{15};
    return &target;
}

// A native interface declared through a macro is still a definition.
#define NATIVE_INTERFACE_BEGIN(NAME) struct [[sharpcpp::native_interface]] NAME {
#define NATIVE_INTERFACE_END };

NATIVE_INTERFACE_BEGIN(native_interface_macro_counter)
    virtual int get_value() = 0;
NATIVE_INTERFACE_END

struct [[sharpcpp::ignore]] native_interface_macro_counter_impl final : native_interface_macro_counter
{
    int get_value() override
    {
        return 64;
    }
};

inline native_interface_macro_counter* get_native_interface_macro_counter()
{
    static native_interface_macro_counter_impl counter;
    return &counter;
}
