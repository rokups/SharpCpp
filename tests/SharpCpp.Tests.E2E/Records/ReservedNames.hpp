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

/// The virtual close() would become NativeClose, which native_close() already uses: it is ignored.
struct taken_window
{
    bool native_close_called = false;
    bool virtual_close_called = false;

    virtual ~taken_window() = default;

    virtual void close()
    {
        virtual_close_called = true;
    }

    void native_close()
    {
        native_close_called = true;
    }
};

/// A non-virtual overload set shares one replacement: NativeClose() and NativeClose(int), no NativeClose2.
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
}
