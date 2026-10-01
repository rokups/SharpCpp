#pragma once

namespace reserved_names
{
/// C++ names that collide with the SafeHandle/NativeObject surface of every wrapper.
struct document
{
    bool closed = false;
    int close_code = 0;

    void close()
    {
        closed = true;
    }

    int dispose()
    {
        return 42;
    }

    bool is_closed() const
    {
        return closed;
    }

    bool get_is_closed() const
    {
        return closed;
    }

    /// Bound as an instance GetHandle with two parameters: a plain overload of the static
    /// NativeObject.GetHandle(object), so it keeps its name.
    int get_handle(int a, int b) const
    {
        return a + b;
    }

    /// An instance GetObject(unsigned) is an overload of the static NativeObject.GetObject(object),
    /// not a hide, so it keeps its name.
    int get_object(unsigned index) const
    {
        return static_cast<int>(index) + 1;
    }

    /// An explicit rename to a reserved name is the author's choice and is left alone
    /// (the different signature keeps it from hiding SafeHandle.Close()).
    [[sharpcpp::rename("Close")]]
    void close_with(int code)
    {
        closed = true;
        close_code = code;
    }
};

struct base_window
{
    bool base_closed = false;
    bool derived_closed = false;

    virtual ~base_window() = default;

    virtual void close()
    {
        base_closed = true;
    }
};

struct derived_window : base_window
{
    void close() override
    {
        derived_closed = true;
    }
};

/// A second level of the override chain: it still takes the name of the root, base_window.
struct deep_window : derived_window
{
    bool deep_closed = false;

    void close() override
    {
        deep_closed = true;
    }
};

/// The virtual close() would become CloseTakenWindow, which close_taken_window() already uses: it is ignored.
struct taken_window
{
    bool taken_close_called = false;
    bool virtual_close_called = false;

    virtual ~taken_window() = default;

    virtual void close()
    {
        virtual_close_called = true;
    }

    void close_taken_window()
    {
        taken_close_called = true;
    }
};

/// A non-virtual close() would become CloseCounterWindow, which close_counter_window() already uses: it takes
/// the counter instead and is bound as CloseCounterWindow2.
struct counter_window
{
    bool closed = false;
    bool counter_close_called = false;

    void close()
    {
        closed = true;
    }

    void close_counter_window()
    {
        counter_close_called = true;
    }
};

/// A non-virtual overload set shares one replacement: CloseOverloadedWindow() and CloseOverloadedWindow(int), no counter.
struct overloaded_window
{
    int last_code = 0;
    bool closed = false;

    void close()
    {
        closed = true;
    }

    void close(int code)
    {
        last_code = code;
    }
};

/// A virtual close() and a non-virtual overload share the replacement; the virtual one is not ignored.
struct mixed_window
{
    bool virtual_close_called = false;
    int last_code = 0;

    virtual ~mixed_window() = default;

    virtual void close()
    {
        virtual_close_called = true;
    }

    void close(int code)
    {
        last_code = code;
    }
};

/// A parameter type that the marshaller configuration binds as managed `object`.
struct opaque_token
{
    int value = 0;

    /// A reserved SafeHandle name on a projected record: the rename names the owning record, not the
    /// managed type (`object`) it is projected onto.
    bool is_invalid() const
    {
        return value == 0;
    }
};

/// Functions whose managed signature is exactly a reserved static NativeObject helper, GetHandle(object),
/// GetObject(object) and TransferHandleOwnership(object): they are renamed because they would hide the helper.
struct opaque_window
{
    int get_handle(opaque_token token) const
    {
        return token.value * 2;
    }

    static int get_object(opaque_token token)
    {
        return token.value + 100;
    }

    static int transfer_handle_ownership(opaque_token token)
    {
        return token.value - 1;
    }
};

/// A second root for close(int): split_window overrides virtuals declared in two different records, so the
/// reserved-name overload set cannot be renamed for both chains and is ignored.
struct other_base
{
    virtual ~other_base() = default;

    virtual void close(int code)
    {
        other_code = code;
    }

    int other_code = 0;
};

struct split_window : base_window, other_base
{
    void close() override
    {
        derived_closed = true;
    }

    void close(int code) override
    {
        other_code = code + 1;
    }
};
}
