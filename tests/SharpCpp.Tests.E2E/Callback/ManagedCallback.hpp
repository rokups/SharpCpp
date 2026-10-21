#pragma once

#include <SharpCpp/ManagedCallback.hpp>

#include <cstddef>
#include <functional>
#include <new>
#include <span>
#include <string_view>
#include <type_traits>
#include <utility>

namespace managed_callback
{
/// A function object with only a fixed small buffer and no heap fallback. It has the same
/// size limit as the Urho3D::Signal handler type ea::fixed_function<4 * sizeof(void*), ...>.
template <std::size_t TSize, class TSignature>
class fixed_function;

template <std::size_t TSize, class TResult, class... TArgs>
class fixed_function<TSize, TResult(TArgs...)>
{
public:
    template <class TFunctor>
    explicit fixed_function(TFunctor functor)
    {
        static_assert(sizeof(TFunctor) <= TSize, "The functor does not fit the fixed buffer.");
        static_assert(alignof(TFunctor) <= alignof(std::max_align_t), "The functor alignment is too large.");
        ::new (static_cast<void*>(storage_)) TFunctor(std::move(functor));
        invoke_ = [](void* storage, TArgs... arguments) -> TResult
        {
            return (*static_cast<TFunctor*>(storage))(std::forward<TArgs>(arguments)...);
        };
        destroy_ = [](void* storage)
        {
            static_cast<TFunctor*>(storage)->~TFunctor();
        };
    }

    fixed_function(const fixed_function&) = delete;
    fixed_function& operator=(const fixed_function&) = delete;

    ~fixed_function()
    {
        destroy_(storage_);
    }

    TResult operator()(TArgs... arguments)
    {
        return invoke_(storage_, std::forward<TArgs>(arguments)...);
    }

private:
    alignas(std::max_align_t) unsigned char storage_[TSize];
    TResult (*invoke_)(void*, TArgs...){};
    void (*destroy_)(void*){};
};

using slot_function = fixed_function<4 * sizeof(void*), int(int)>;

static_assert(sizeof(SharpCpp::ManagedCallback<int(int)>) <= 4 * sizeof(void*),
    "ManagedCallback must fit a 4 * sizeof(void*) small buffer.");
static_assert(std::is_copy_constructible_v<SharpCpp::ManagedCallback<int(int)>>,
    "ManagedCallback must be copyable.");

/// Stores a copy of the callback in a 32-byte fixed buffer and calls the copy.
inline int invoke_through_fixed_function(SharpCpp::ManagedCallback<int(int)> callback, int value)
{
    slot_function slot(callback);
    return slot(value);
}

inline slot_function*& stored_slot()
{
    static slot_function* slot = nullptr;
    return slot;
}

/// Keeps the callback in a native slot until disconnect_slot.
inline void connect_slot(SharpCpp::ManagedCallback<int(int)> callback)
{
    delete stored_slot();
    stored_slot() = new slot_function(std::move(callback));
}

inline int emit_slot(int value)
{
    return stored_slot() != nullptr ? (*stored_slot())(value) : -1;
}

/// Destroys the native slot and with it the last copy of the callback.
inline void disconnect_slot()
{
    delete stored_slot();
    stored_slot() = nullptr;
}

static_assert(std::is_nothrow_invocable_v<const SharpCpp::ManagedCallback<int(int) noexcept>&, int>,
    "The noexcept form of ManagedCallback must have a noexcept call operator.");

/// A noexcept signature has its own ManagedCallback specialization.
inline int invoke_noexcept_callback(SharpCpp::ManagedCallback<int(int) noexcept> callback, int value) noexcept
{
    slot_function slot(callback);
    return slot(value);
}

/// A non-const primitive reference is a managed ref parameter.
inline bool invoke_bool_ref_callback(SharpCpp::ManagedCallback<void(bool&)> callback, bool initial)
{
    bool value = initial;
    callback(value);
    return value;
}

/// The std::function form of a primitive ref parameter, with a result.
inline int invoke_int_ref_callback(std::function<int(int&)> callback, int initial)
{
    int value = initial;
    const int result = callback(value);
    return value * 100 + result;
}

/// A span of const blittable elements arrives in the managed delegate as ReadOnlySpan<T>.
/// The span is a view of this native array. It is valid only during the call.
inline int invoke_byte_span_callback(SharpCpp::ManagedCallback<int(std::span<const unsigned char>)> callback, int count)
{
    unsigned char bytes[8]{};
    for (int index = 0; index < count && index < 8; ++index)
        bytes[index] = static_cast<unsigned char>(index + 1);
    return callback(std::span<const unsigned char>(bytes, static_cast<std::size_t>(count < 8 ? count : 8)));
}

/// A span view together with a ref parameter, the shape of the Urho3D network signals.
inline bool invoke_float_span_ref_callback(std::function<void(std::span<const float>, bool&)> callback)
{
    const float values[3]{1.5f, 2.5f, 3.5f};
    bool handled = false;
    callback(std::span<const float>(values), handled);
    return handled;
}

/// An empty span arrives as an empty ReadOnlySpan<T>.
inline int invoke_empty_span_callback(SharpCpp::ManagedCallback<int(std::span<const int>)> callback)
{
    return callback(std::span<const int>());
}

/// A UTF-8 string view arrives in the managed delegate as ReadOnlySpan<byte>.
inline std::size_t invoke_string_view_callback(SharpCpp::ManagedCallback<std::size_t(int, std::string_view)> callback)
{
    const char text[] = "hello, view";
    return callback(7, std::string_view(text, 5));
}
}
