#pragma once

#include <SharpCpp/Marshaller.hpp>
#include <atomic>
#include <exception>
#include <functional>
#include <memory>
#include <cstddef>
#include <stdexcept>
#include <tuple>
#include <type_traits>
#include <utility>

namespace SharpCpp
{
namespace detail
{
// Container element types keep pointee const (std::array<const T*, N>) while the
// element marshaller binds T*; drop that const so the element can be marshalled.
template <typename TMarshalled, typename TElement>
constexpr decltype(auto) AsMarshalledElement(TElement&& element) noexcept
{
    if constexpr (std::is_pointer_v<TMarshalled> && std::is_pointer_v<std::remove_cvref_t<TElement>>)
    {
        return const_cast<TMarshalled>(element);
    }
    else
    {
        return std::forward<TElement>(element);
    }
}

template <typename T, typename = void>
struct has_clear : std::false_type {};

template <typename T>
struct has_clear<T, std::void_t<decltype(std::declval<T&>().clear())>> : std::true_type {};

template <typename T>
inline constexpr bool has_clear_v = has_clear<T>::value;

// True for an optional-like type (has_value, reset, emplace) whose value type is not assignable.
// The assignment operator of eastl::optional is not constrained, so the check uses the value type.
template <typename T, typename = void>
struct is_emplace_only_optional : std::false_type {};

template <typename T>
struct is_emplace_only_optional<
    T,
    std::void_t<
        typename T::value_type,
        decltype(std::declval<T&>().has_value()),
        decltype(std::declval<T&>().reset()),
        decltype(std::declval<T&>().emplace(std::declval<typename T::value_type>()))>>
    : std::bool_constant<
          !std::is_assignable_v<typename T::value_type&, typename T::value_type&&> ||
          !std::is_assignable_v<typename T::value_type&, const typename T::value_type&>>
{
};

template <typename T>
inline constexpr bool is_emplace_only_optional_v = is_emplace_only_optional<T>::value;

template <typename T>
using remove_cvref_t = std::remove_cv_t<std::remove_reference_t<T>>;

template <typename T>
void AssignBorrowedStorage(T& target, T* value)
{
    if (value == nullptr)
    {
        if constexpr (has_clear_v<T>)
            target.clear();
        else
            SharpCpp::FatalError("SharpCpp borrowed field setter cannot clear this field type.");
    }
    else if constexpr (std::is_assignable_v<T&, T&&>)
        target = std::move(*value);
    else if constexpr (std::is_assignable_v<T&, const T&>)
        target = *value;
    else
        SharpCpp::FatalError("SharpCpp borrowed field setter cannot assign this field type.");
}

template <typename TTuple>
struct MarshallerTupleForTuple;

template <typename... TCppArgs>
struct MarshallerTupleForTuple<std::tuple<TCppArgs...>>
{
    using type = std::tuple<ParameterMarshaller<TCppArgs>...>;
};

template <typename TTuple>
using marshaller_tuple_t = typename MarshallerTupleForTuple<TTuple>::type;

template <typename TCpp>
using callback_parameter_abi_t = typename ParameterMarshaller<TCpp>::abi_type;

template <typename TCpp>
using callback_return_abi_t = typename ReturnMarshaller<TCpp>::abi_type;

using managed_callback_release_pointer_t = void (*)(void*);

inline std::atomic<managed_callback_release_pointer_t> g_managed_callback_release{};

inline void SetManagedCallbackRelease(managed_callback_release_pointer_t release) noexcept
{
    g_managed_callback_release.store(release, std::memory_order_release);
}

inline void ReleaseManagedCallbackState(void* state)
{
    if (state == nullptr)
        return;

    const auto release = g_managed_callback_release.load(std::memory_order_acquire);
    if (release == nullptr)
    {
        std::terminate();
    }

    release(state);
}

template <typename TSignature>
struct ManagedCallbackInvokePointer;

template <typename TResult, typename... TArgs>
struct ManagedCallbackInvokePointer<TResult(TArgs...)>
{
    using type = callback_return_abi_t<TResult> (*)(void*, callback_parameter_abi_t<TArgs>...);
};

template <typename TResult, typename... TArgs>
struct ManagedCallbackInvokePointer<TResult(TArgs...) noexcept>
{
    using type = callback_return_abi_t<TResult> (*)(void*, callback_parameter_abi_t<TArgs>...);
};

template <typename TSignature>
using managed_callback_invoke_pointer_t = typename ManagedCallbackInvokePointer<TSignature>::type;

template <typename TCppArgumentTuple, typename TCallable, typename... TCppArgs, std::size_t... TIndices>
decltype(auto) InvokeCallbackWithExplicitSignatureImpl(
    TCallable&& callable,
    std::index_sequence<TIndices...>,
    TCppArgs&&... cppArguments)
{
    marshaller_tuple_t<TCppArgumentTuple> marshallers;
    auto invoke = [&]() -> decltype(auto)
    {
        return std::invoke(
            std::forward<TCallable>(callable),
            std::get<TIndices>(marshallers).ConvertToAbi(cppArguments)...);
    };

    return invoke();
}

template <typename TCppArgumentTuple, typename TCallable, typename... TCppArgs>
decltype(auto) InvokeCallbackWithExplicitSignature(TCallable&& callable, TCppArgs&&... cppArguments)
{
    return InvokeCallbackWithExplicitSignatureImpl<TCppArgumentTuple>(
        std::forward<TCallable>(callable),
        std::make_index_sequence<std::tuple_size_v<TCppArgumentTuple>>{},
        std::forward<TCppArgs>(cppArguments)...);
}

template <typename TSignature>
struct ManagedCallbackSignature
{
    using type = TSignature;
};

template <typename TResult, typename... TArgs>
struct ManagedCallbackSignature<TResult(TArgs...) noexcept>
{
    using type = TResult(TArgs...);
};

template <typename T>
inline constexpr bool is_managed_callback_v = false;

template <typename TSignature>
inline constexpr bool is_managed_callback_v<SharpCpp::ManagedCallback<TSignature>> = true;

template <typename TFunction, typename TSignature>
TFunction CreateManagedCallbackWrapper(SharpCpp::ManagedCallbackHandleAbi handle)
{
    // The noexcept form is a separate class, so it takes the handle directly.
    if constexpr (is_managed_callback_v<TFunction>)
    {
        return TFunction(handle);
    }
    else
    {
        // A noexcept signature keeps its noexcept: a function object with such a signature
        // accepts only a callable that does not throw.
        using signature = remove_cvref_t<TSignature>;
        SharpCpp::ManagedCallback<signature> callback(handle);
        if (!callback)
        {
            return TFunction{};
        }

        return TFunction(std::move(callback));
    }
}
}

// A copyable function object that calls a managed delegate. It holds only the trampoline
// pointer and a shared reference to the GCHandle state, so it fits a 32-byte small buffer
// (for example ea::fixed_function<4 * sizeof(void*), ...>). The last copy releases the
// GCHandle when it is destroyed.
template <typename TResult, typename... TArgs>
class ManagedCallback<TResult(TArgs...)>
{
public:
    using invoke_pointer = detail::managed_callback_invoke_pointer_t<TResult(TArgs...)>;

    ManagedCallback() noexcept = default;

    // Takes ownership of the handle state. A handle without a trampoline is empty, and its
    // state is released immediately.
    explicit ManagedCallback(ManagedCallbackHandleAbi handle)
    {
        if (handle.callback == nullptr)
        {
            detail::ReleaseManagedCallbackState(handle.state);
            return;
        }

        lifetime_ = std::shared_ptr<void>(handle.state, [](void* state)
        {
            detail::ReleaseManagedCallbackState(state);
        });
        callback_ = reinterpret_cast<invoke_pointer>(handle.callback);
    }

    explicit operator bool() const noexcept
    {
        return callback_ != nullptr;
    }

    TResult operator()(TArgs... cppArguments) const
    {
        if (callback_ == nullptr)
        {
            FatalError("SharpCpp: an empty ManagedCallback was invoked.");
        }

        auto invoke = [callback = callback_, state = lifetime_.get()](detail::callback_parameter_abi_t<TArgs>... abiArguments)
            -> detail::callback_return_abi_t<TResult>
        {
            return callback(state, abiArguments...);
        };

        if constexpr (std::is_void_v<TResult>)
        {
            detail::InvokeCallbackWithExplicitSignature<std::tuple<TArgs...>>(
                invoke,
                std::forward<TArgs>(cppArguments)...);
        }
        else
        {
            auto result = detail::InvokeCallbackWithExplicitSignature<std::tuple<TArgs...>>(
                invoke,
                std::forward<TArgs>(cppArguments)...);
            return ReturnMarshaller<TResult>{}.ConvertToCpp(result);
        }
    }

private:
    invoke_pointer callback_{};
    std::shared_ptr<void> lifetime_;
};

template <typename TResult, typename... TArgs>
class ManagedCallback<TResult(TArgs...) noexcept> : public ManagedCallback<TResult(TArgs...)>
{
public:
    using ManagedCallback<TResult(TArgs...)>::ManagedCallback;

    // A managed exception ends the process (Environment.FailFast), so no exception leaves the call.
    TResult operator()(TArgs... cppArguments) const noexcept
    {
        return ManagedCallback<TResult(TArgs...)>::operator()(std::forward<TArgs>(cppArguments)...);
    }
};

static_assert(sizeof(ManagedCallback<void()>) == 3 * sizeof(void*),
    "ManagedCallback must stay three pointers wide to fit a 4 * sizeof(void*) small buffer.");

namespace detail
{
// Converts a by-value result that the invoker keeps in a local. The result goes in as an
// rvalue when the marshaller accepts one, the same as a direct call on the prvalue.
// Owning marshallers that take only cpp_type& get the lvalue and steal from it.
template <typename TMarshaller, typename TValue>
constexpr decltype(auto) ConvertByValueResultToAbi(TMarshaller& marshaller, TValue& result)
{
    if constexpr (requires { std::declval<TMarshaller&>().ConvertToAbi(std::declval<TValue&&>()); })
    {
        return marshaller.ConvertToAbi(std::move(result));
    }
    else
    {
        return marshaller.ConvertToAbi(result);
    }
}

template <typename TResult, typename TCppArgumentTuple, typename TCallable, typename TMarshallerTuple, typename... TAbiArgs, std::size_t... TIndices>
constexpr decltype(auto) InvokeWithMarshallersImpl(
    TCallable&& callable,
    TMarshallerTuple& marshallers,
    std::index_sequence<TIndices...>,
    TAbiArgs&&... abiArguments)
{
    auto invoke_fn = [&]() -> TResult
    {
        return std::invoke(
            std::forward<TCallable>(callable),
            std::get<TIndices>(marshallers).ConvertToCpp(std::forward<TAbiArgs>(abiArguments))...);
    };

    if constexpr (std::is_void_v<TResult>)
    {
        invoke_fn();
        return;
    }
    else
    {
        ReturnMarshaller<TResult> ret_marshaller;
        decltype(auto) result = invoke_fn();
        if constexpr (std::is_reference_v<TResult>)
        {
            return ret_marshaller.ConvertToAbi(result);
        }
        else
        {
            return ConvertByValueResultToAbi(ret_marshaller, result);
        }
    }
}

// Gives the invoker the native function object of a borrowed handle. The state is the address of
// the function object of the caller. The function object lives only during the director call.
template <typename TFunction>
struct NativeCallbackBorrow
{
    static const TFunction& Get(void* state) noexcept
    {
        return *static_cast<const TFunction*>(state);
    }
};

// Calls a native function object that a director gives to a managed override. The managed code
// gives ABI arguments, and the parameter marshallers convert them to C++ arguments. A mutable
// reference argument points to managed memory, so the function object writes it back in place.
// TAccess gets the function object from the handle state.
template <typename TAccess, typename TSignature>
struct NativeCallbackInvoker;

template <typename TAccess, typename TResult, typename... TArgs>
struct NativeCallbackInvoker<TAccess, TResult(TArgs...)>
{
    // A C++ exception must not cross into managed code, so an exception ends the process.
    static callback_return_abi_t<TResult> Invoke(void* state, callback_parameter_abi_t<TArgs>... abiArguments) noexcept
    {
        const auto& function = TAccess::Get(state);
        using argument_tuple = std::tuple<TArgs...>;
        marshaller_tuple_t<argument_tuple> marshallers;
        return InvokeWithMarshallersImpl<TResult, argument_tuple>(
            function,
            marshallers,
            std::index_sequence_for<TArgs...>{},
            abiArguments...);
    }
};

// A heap holder that owns a native function object for managed code. Each owned handle holds one
// reference. The managed delegate releases its reference when it is disposed or finalized. The
// last release deletes the holder in the module that allocated it.
template <typename TFunction, typename TSignature>
struct NativeCallbackHolder
{
    template <typename T>
    explicit NativeCallbackHolder(T&& value)
        : function(std::forward<T>(value))
    {
    }

    static const TFunction& Get(void* state) noexcept
    {
        return static_cast<const NativeCallbackHolder*>(state)->function;
    }

    static void Release(void* state) noexcept
    {
        auto* holder = static_cast<NativeCallbackHolder*>(state);
        if (holder->references.fetch_sub(1, std::memory_order_acq_rel) == 1)
        {
            delete holder;
        }
    }

    // Gives a second owned handle to the same holder.
    static void Retain(void* state, SharpCpp::ManagedCallbackHandleAbi* result) noexcept
    {
        auto* holder = static_cast<NativeCallbackHolder*>(state);
        holder->references.fetch_add(1, std::memory_order_relaxed);
        *result = holder->Handle();
    }

    SharpCpp::ManagedCallbackHandleAbi Handle() noexcept
    {
        return {
            this,
            reinterpret_cast<void*>(&NativeCallbackInvoker<NativeCallbackHolder, TSignature>::Invoke),
            reinterpret_cast<void*>(&NativeCallbackHolder::Release),
            reinterpret_cast<void*>(&NativeCallbackHolder::Retain)};
    }

    std::atomic<std::size_t> references{1};
    TFunction function;
};

template <typename TFunction>
bool IsEmptyNativeCallback(const TFunction& function) noexcept
{
    if constexpr (std::is_constructible_v<bool, const TFunction&>)
    {
        return !static_cast<bool>(function);
    }
    else
    {
        return false;
    }
}

// Copies a borrowed function object into a new holder. Managed code calls it during the director
// call (the Retain extension method), so the borrowed function object still lives. A failed
// allocation ends the process, because a C++ exception must not cross into managed code.
template <typename TFunction, typename TSignature>
void RetainBorrowedNativeCallback(void* state, SharpCpp::ManagedCallbackHandleAbi* result) noexcept
{
    auto* holder = new NativeCallbackHolder<TFunction, TSignature>(NativeCallbackBorrow<TFunction>::Get(state));
    *result = holder->Handle();
}

// A borrowed handle: no allocation. The handle has no release function, because the caller owns
// the function object. Managed code can copy the function object with the retain function.
template <typename TFunction, typename TSignature>
SharpCpp::ManagedCallbackHandleAbi CreateNativeCallbackHandle(const TFunction& function) noexcept
{
    // An empty function object crosses as an empty handle, and the managed override gets null.
    if (IsEmptyNativeCallback(function))
    {
        return {};
    }

    using signature = typename ManagedCallbackSignature<remove_cvref_t<TSignature>>::type;
    return {
        const_cast<void*>(static_cast<const void*>(std::addressof(function))),
        reinterpret_cast<void*>(&NativeCallbackInvoker<NativeCallbackBorrow<TFunction>, signature>::Invoke),
        nullptr,
        reinterpret_cast<void*>(&RetainBorrowedNativeCallback<TFunction, signature>)};
}

// An owned handle: the function object moves into a new holder, and the handle owns the holder.
// This is the only conversion that allocates. A failed allocation ends the process.
template <typename TFunction, typename TSignature>
SharpCpp::ManagedCallbackHandleAbi CreateOwnedNativeCallbackHandle(TFunction&& function) noexcept
{
    if (IsEmptyNativeCallback(function))
    {
        return {};
    }

    using signature = typename ManagedCallbackSignature<remove_cvref_t<TSignature>>::type;
    auto* holder = new NativeCallbackHolder<TFunction, signature>(std::move(function));
    return holder->Handle();
}

template <typename TResult, typename TCppArgumentTuple, typename TCallable, typename TMarshallerTuple, typename... TAbiArgs, std::size_t... TIndices>
auto InvokeWithMarshallersOwnedImpl(
    TCallable&& callable,
    TMarshallerTuple& marshallers,
    std::index_sequence<TIndices...>,
    TAbiArgs&&... abiArguments) -> std::remove_cv_t<std::remove_reference_t<TResult>>*
{
    using owned_type = std::remove_cv_t<std::remove_reference_t<TResult>>;
    auto invoke_fn = [&]() -> decltype(auto)
    {
        return std::invoke(
            std::forward<TCallable>(callable),
            std::get<TIndices>(marshallers).ConvertToCpp(std::forward<TAbiArgs>(abiArguments))...);
    };

    if constexpr (std::is_lvalue_reference_v<TResult>)
    {
        auto* result = std::addressof(invoke_fn());
        return new owned_type(*result);
    }
    else
    {
        return new owned_type(invoke_fn());
    }
}

template <typename TResult, typename TCppArgumentTuple, typename TCallable, typename TMarshallerTuple, typename... TAbiArgs, std::size_t... TIndices>
auto InvokeWithMarshallersValueImpl(
    TCallable&& callable,
    TMarshallerTuple& marshallers,
    std::index_sequence<TIndices...>,
    TAbiArgs&&... abiArguments) -> std::remove_cv_t<std::remove_reference_t<TResult>>
{
    return std::invoke(
        std::forward<TCallable>(callable),
        std::get<TIndices>(marshallers).ConvertToCpp(std::forward<TAbiArgs>(abiArguments))...);
}

template <typename TResult, typename TCppArgumentTuple, typename TMethod, typename TObject, typename TMarshallerTuple, typename... TAbiArgs, std::size_t... TIndices>
constexpr decltype(auto) InvokeMemberWithMarshallersImpl(
    TMethod method,
    TObject&& instance,
    TMarshallerTuple& marshallers,
    std::index_sequence<TIndices...>,
    TAbiArgs&&... abiArguments)
{
    // Decay the method to a value type — the INVOKE specification
    // requires a pointer-to-member value, not a reference-to-member.
    // Also use direct member-access syntax (obj.*m)(args) instead of
    // std::invoke to avoid std::invoke_result deduction issues with
    // reference-returning member functions in template contexts.
    std::decay_t<TMethod> m = method;

    // Inlined invoke+sync — avoids lambda std::invoke_result issues.
    if constexpr (std::is_void_v<TResult>)
    {
        (std::forward<TObject>(instance).*m)(
            std::get<TIndices>(marshallers).ConvertToCpp(std::forward<TAbiArgs>(abiArguments))...);
        return;
    }
    else
    {
        ReturnMarshaller<TResult> ret_marshaller;
        if constexpr (std::is_lvalue_reference_v<TResult>)
        {
            auto* result = std::addressof(
                (std::forward<TObject>(instance).*m)(
                    std::get<TIndices>(marshallers).ConvertToCpp(std::forward<TAbiArgs>(abiArguments))...));
            return ret_marshaller.ConvertToAbi(static_cast<TResult>(*result));
        }
        else
        {
            auto result =
                (std::forward<TObject>(instance).*m)(
                    std::get<TIndices>(marshallers).ConvertToCpp(std::forward<TAbiArgs>(abiArguments))...);
            return ConvertByValueResultToAbi(ret_marshaller, result);
        }
    }
}

template <typename TResult, typename TCppArgumentTuple, typename TMethod, typename TObject, typename TMarshallerTuple, typename... TAbiArgs, std::size_t... TIndices>
auto InvokeMemberWithMarshallersValueImpl(
    TMethod method,
    TObject&& instance,
    TMarshallerTuple& marshallers,
    std::index_sequence<TIndices...>,
    TAbiArgs&&... abiArguments) -> std::remove_cv_t<std::remove_reference_t<TResult>>
{
    std::decay_t<TMethod> m = method;
    return (std::forward<TObject>(instance).*m)(
        std::get<TIndices>(marshallers).ConvertToCpp(std::forward<TAbiArgs>(abiArguments))...);
}

template <typename TResult, typename TCppArgumentTuple, typename TMethod, typename TObject, typename TMarshallerTuple, typename... TAbiArgs, std::size_t... TIndices>
auto InvokeMemberWithMarshallersOwnedImpl(
    TMethod method,
    TObject&& instance,
    TMarshallerTuple& marshallers,
    std::index_sequence<TIndices...>,
    TAbiArgs&&... abiArguments) -> std::remove_cv_t<std::remove_reference_t<TResult>>*
{
    using owned_type = std::remove_cv_t<std::remove_reference_t<TResult>>;
    std::decay_t<TMethod> m = method;

    if constexpr (std::is_lvalue_reference_v<TResult>)
    {
        auto* result = std::addressof(
            (std::forward<TObject>(instance).*m)(
                std::get<TIndices>(marshallers).ConvertToCpp(std::forward<TAbiArgs>(abiArguments))...));
        return new owned_type(*result);
    }
    else
    {
        return new owned_type(
            (std::forward<TObject>(instance).*m)(
                std::get<TIndices>(marshallers).ConvertToCpp(std::forward<TAbiArgs>(abiArguments))...));
    }
}

template <typename FT>
struct FunctionTraits;

template <typename TResult, typename... TArgs>
struct FunctionTraits<TResult(TArgs...)>
{
    using return_type = TResult;
    using argument_tuple = std::tuple<TArgs...>;
};

template <typename TResult, typename... TArgs>
struct FunctionTraits<TResult(TArgs...) noexcept>
{
    using return_type = TResult;
    using argument_tuple = std::tuple<TArgs...>;
};

template <typename FT>
struct MemberFunctionTraits;

template <typename TResult, typename TSelf, typename... TArgs>
struct MemberFunctionTraits<TResult (TSelf::*)(TArgs...)>
{
    using return_type = TResult;
    using class_type = TSelf;
    using argument_tuple = std::tuple<TArgs...>;
};

template <typename TResult, typename TSelf, typename... TArgs>
struct MemberFunctionTraits<TResult (TSelf::*)(TArgs...) const>
{
    using return_type = TResult;
    using class_type = TSelf;
    using argument_tuple = std::tuple<TArgs...>;
};

template <typename TResult, typename TSelf, typename... TArgs>
struct MemberFunctionTraits<TResult (TSelf::*)(TArgs...) noexcept>
{
    using return_type = TResult;
    using class_type = TSelf;
    using argument_tuple = std::tuple<TArgs...>;
};

template <typename TResult, typename TSelf, typename... TArgs>
struct MemberFunctionTraits<TResult (TSelf::*)(TArgs...) const noexcept>
{
    using return_type = TResult;
    using class_type = TSelf;
    using argument_tuple = std::tuple<TArgs...>;
};

template <typename T, typename... TArgs, std::size_t... TIndices, typename... TAbiArgs>
auto ConstructImpl(std::index_sequence<TIndices...>, TAbiArgs&&... abiArguments) -> ret_t<T*>
{
    marshaller_tuple_t<std::tuple<TArgs...>> marshallers;
    ReturnMarshaller<T*> ret_marshaller;
    return ret_marshaller.ConvertToAbi(
        new T(std::get<TIndices>(marshallers).ConvertToCpp(std::forward<TAbiArgs>(abiArguments))...));
}

// Function pointer parameter of invoke_as. The converting constructor gives an overloaded function
// name its target type. A callable object does not convert, so the compiler does not instantiate the
// conversion operator of a generic lambda (GCC 13 fails in that instantiation).
template <typename FT>
struct FunctionPointerArgument
{
    FunctionPointerArgument(std::add_pointer_t<UnwrapFunctionType_t<FT>> value) noexcept // NOLINT(google-explicit-constructor)
        : value(value)
    {
    }

    std::add_pointer_t<UnwrapFunctionType_t<FT>> value;
};

struct [[maybe_unused]] MarshallerInvokerImpl;
}

struct MarshallerInvoker
{
    template <typename FT, typename TCppArgumentTuple = typename detail::FunctionTraits<detail::UnwrapFunctionType_t<FT>>::argument_tuple, typename... TAbiArgs>
    static decltype(auto) invoke_as(detail::FunctionPointerArgument<FT> callable, TAbiArgs&&... abiArguments)
    {
        using traits = detail::FunctionTraits<detail::UnwrapFunctionType_t<FT>>;
        using result_type = typename traits::return_type;
        using signature_arg_tuple = typename traits::argument_tuple;
        using arg_tuple = TCppArgumentTuple;

        static_assert(
            std::tuple_size_v<arg_tuple> == std::tuple_size_v<signature_arg_tuple>,
            "SharpCpp marshaller argument tuple arity mismatch.");

        detail::marshaller_tuple_t<arg_tuple> marshallers;
        return detail::InvokeWithMarshallersImpl<result_type, arg_tuple>(
            callable.value,
            marshallers,
            std::make_index_sequence<std::tuple_size_v<arg_tuple>>{},
            std::forward<TAbiArgs>(abiArguments)...);
    }

    template <typename FT, typename TCppArgumentTuple = typename detail::FunctionTraits<detail::UnwrapFunctionType_t<FT>>::argument_tuple, typename... TAbiArgs>
    static auto invoke_owned_as(detail::FunctionPointerArgument<FT> callable, TAbiArgs&&... abiArguments)
    {
        using traits = detail::FunctionTraits<detail::UnwrapFunctionType_t<FT>>;
        using result_type = typename traits::return_type;
        using signature_arg_tuple = typename traits::argument_tuple;
        using arg_tuple = TCppArgumentTuple;

        static_assert(
            std::tuple_size_v<arg_tuple> == std::tuple_size_v<signature_arg_tuple>,
            "SharpCpp marshaller argument tuple arity mismatch.");

        detail::marshaller_tuple_t<arg_tuple> marshallers;
        return detail::InvokeWithMarshallersOwnedImpl<result_type, arg_tuple>(
            callable.value,
            marshallers,
            std::make_index_sequence<std::tuple_size_v<arg_tuple>>{},
            std::forward<TAbiArgs>(abiArguments)...);
    }

    template <typename FT, typename TCppArgumentTuple = typename detail::FunctionTraits<detail::UnwrapFunctionType_t<FT>>::argument_tuple, typename... TAbiArgs>
    static auto invoke_value_as(detail::FunctionPointerArgument<FT> callable, TAbiArgs&&... abiArguments)
    {
        using traits = detail::FunctionTraits<detail::UnwrapFunctionType_t<FT>>;
        using result_type = typename traits::return_type;
        using signature_arg_tuple = typename traits::argument_tuple;
        using arg_tuple = TCppArgumentTuple;

        static_assert(
            std::tuple_size_v<arg_tuple> == std::tuple_size_v<signature_arg_tuple>,
            "SharpCpp marshaller argument tuple arity mismatch.");

        detail::marshaller_tuple_t<arg_tuple> marshallers;
        return detail::InvokeWithMarshallersValueImpl<result_type, arg_tuple>(
            callable.value,
            marshallers,
            std::make_index_sequence<std::tuple_size_v<arg_tuple>>{},
            std::forward<TAbiArgs>(abiArguments)...);
    }

    template <typename FT, typename TCppArgumentTuple = typename detail::FunctionTraits<detail::UnwrapFunctionType_t<FT>>::argument_tuple, typename TCallable, typename... TAbiArgs>
        requires(!std::is_same_v<std::remove_cvref_t<TCallable>, std::add_pointer_t<detail::UnwrapFunctionType_t<FT>>>)
    static auto invoke_value_as(TCallable&& callable, TAbiArgs&&... abiArguments)
    {
        using traits = detail::FunctionTraits<detail::UnwrapFunctionType_t<FT>>;
        using result_type = typename traits::return_type;
        using signature_arg_tuple = typename traits::argument_tuple;
        using arg_tuple = TCppArgumentTuple;

        static_assert(
            std::tuple_size_v<arg_tuple> == std::tuple_size_v<signature_arg_tuple>,
            "SharpCpp marshaller argument tuple arity mismatch.");

        detail::marshaller_tuple_t<arg_tuple> marshallers;
        return detail::InvokeWithMarshallersValueImpl<result_type, arg_tuple>(
            std::forward<TCallable>(callable),
            marshallers,
            std::make_index_sequence<std::tuple_size_v<arg_tuple>>{},
            std::forward<TAbiArgs>(abiArguments)...);
    }

    template <typename FT, typename TCppArgumentTuple = typename detail::FunctionTraits<detail::UnwrapFunctionType_t<FT>>::argument_tuple, typename TCallable, typename... TAbiArgs>
        requires(!std::is_same_v<std::remove_cvref_t<TCallable>, std::add_pointer_t<detail::UnwrapFunctionType_t<FT>>>)
    static auto invoke_owned_as(TCallable&& callable, TAbiArgs&&... abiArguments)
    {
        if constexpr (std::is_member_function_pointer_v<std::remove_reference_t<TCallable>>)
        {
            using traits = detail::MemberFunctionTraits<std::remove_reference_t<TCallable>>;
            using result_type = typename traits::return_type;
            using class_type = typename traits::class_type;
            using signature_arg_tuple = typename traits::argument_tuple;
            using arg_tuple = TCppArgumentTuple;

            static_assert(
                std::tuple_size_v<arg_tuple> == std::tuple_size_v<signature_arg_tuple>,
                "SharpCpp marshaller argument tuple arity mismatch.");

            detail::marshaller_tuple_t<arg_tuple> marshallers;
            return detail::InvokeMemberWithMarshallersOwnedImpl<result_type, arg_tuple>(
                std::forward<TCallable>(callable),
                *static_cast<class_type*>(nullptr),
                marshallers,
                std::make_index_sequence<std::tuple_size_v<arg_tuple>>{},
                std::forward<TAbiArgs>(abiArguments)...);
        }
        else
        {
            using traits = detail::FunctionTraits<detail::UnwrapFunctionType_t<FT>>;
            using result_type = typename traits::return_type;
            using signature_arg_tuple = typename traits::argument_tuple;
            using arg_tuple = TCppArgumentTuple;

            static_assert(
                std::tuple_size_v<arg_tuple> == std::tuple_size_v<signature_arg_tuple>,
                "SharpCpp marshaller argument tuple arity mismatch.");

            detail::marshaller_tuple_t<arg_tuple> marshallers;
            return detail::InvokeWithMarshallersOwnedImpl<result_type, arg_tuple>(
                std::forward<TCallable>(callable),
                marshallers,
                std::make_index_sequence<std::tuple_size_v<arg_tuple>>{},
                std::forward<TAbiArgs>(abiArguments)...);
        }
    }

    template <typename FT, typename TCppArgumentTuple = typename detail::FunctionTraits<detail::UnwrapFunctionType_t<FT>>::argument_tuple, typename TCallable, typename... TAbiArgs>
        requires(!std::is_same_v<std::remove_cvref_t<TCallable>, std::add_pointer_t<detail::UnwrapFunctionType_t<FT>>>)
    static decltype(auto) invoke_as(TCallable&& callable, TAbiArgs&&... abiArguments)
    {
        // Detect member function pointers and route to the member-path.
        // When libclang misreports a method as static, the generated code
        // passes a member function pointer to invoke_as. Without this branch,
        // InvokeWithMarshallersImpl would call std::invoke(member_ptr) with
        // no instance, producing a compile error.
        if constexpr (std::is_member_function_pointer_v<std::remove_reference_t<TCallable>>)
        {
            using traits = detail::MemberFunctionTraits<std::remove_reference_t<TCallable>>;
            using result_type = typename traits::return_type;
            using class_type = typename traits::class_type;
            using signature_arg_tuple = typename traits::argument_tuple;
            using arg_tuple = TCppArgumentTuple;

            static_assert(
                std::tuple_size_v<arg_tuple> == std::tuple_size_v<signature_arg_tuple>,
                "SharpCpp marshaller argument tuple arity mismatch.");

            detail::marshaller_tuple_t<arg_tuple> marshallers;
            // Use a nullptr instance — the member function doesn't access
            // this (it's actually a true static in spirit or has no data
            // dependencies). If the method is protected, the compiler will
            // reject this at the call site below anyway.
            return detail::InvokeMemberWithMarshallersImpl<result_type, arg_tuple>(
                std::forward<TCallable>(callable),
                *static_cast<class_type*>(nullptr),
                marshallers,
                std::make_index_sequence<std::tuple_size_v<arg_tuple>>{},
                std::forward<TAbiArgs>(abiArguments)...);
        }
        else
        {
            using traits = detail::FunctionTraits<detail::UnwrapFunctionType_t<FT>>;
            using result_type = typename traits::return_type;
            using signature_arg_tuple = typename traits::argument_tuple;
            using arg_tuple = TCppArgumentTuple;

            static_assert(
                std::tuple_size_v<arg_tuple> == std::tuple_size_v<signature_arg_tuple>,
                "SharpCpp marshaller argument tuple arity mismatch.");

            detail::marshaller_tuple_t<arg_tuple> marshallers;
            return detail::InvokeWithMarshallersImpl<result_type, arg_tuple>(
                std::forward<TCallable>(callable),
                marshallers,
                std::make_index_sequence<std::tuple_size_v<arg_tuple>>{},
                std::forward<TAbiArgs>(abiArguments)...);
        }
    }

    template <typename FT, typename TCppArgumentTuple = typename detail::MemberFunctionTraits<FT>::argument_tuple, typename TSelfAbi, typename... TAbiArgs>
    static decltype(auto) invoke(FT method, TSelfAbi self, TAbiArgs&&... abiArguments)
    {
        using traits = detail::MemberFunctionTraits<FT>;
        using result_type = typename traits::return_type;
        using class_type = typename traits::class_type;
        using signature_arg_tuple = typename traits::argument_tuple;
        using arg_tuple = TCppArgumentTuple;

        static_assert(
            std::tuple_size_v<arg_tuple> == std::tuple_size_v<signature_arg_tuple>,
            "SharpCpp marshaller argument tuple arity mismatch.");

        Marshaller<class_type*> self_marshaller;
        auto* instance = static_cast<class_type*>(self_marshaller.ConvertToCpp(self));
        detail::marshaller_tuple_t<arg_tuple> marshallers;

        return detail::InvokeMemberWithMarshallersImpl<result_type, arg_tuple>(
            method,
            *instance,
            marshallers,
            std::make_index_sequence<std::tuple_size_v<arg_tuple>>{},
            std::forward<TAbiArgs>(abiArguments)...);
    }

    template <typename FT, typename TCppArgumentTuple = typename detail::MemberFunctionTraits<FT>::argument_tuple, typename TSelfAbi, typename... TAbiArgs>
    static auto invoke_owned(FT method, TSelfAbi self, TAbiArgs&&... abiArguments)
    {
        using traits = detail::MemberFunctionTraits<FT>;
        using result_type = typename traits::return_type;
        using class_type = typename traits::class_type;
        using signature_arg_tuple = typename traits::argument_tuple;
        using arg_tuple = TCppArgumentTuple;

        static_assert(
            std::tuple_size_v<arg_tuple> == std::tuple_size_v<signature_arg_tuple>,
            "SharpCpp marshaller argument tuple arity mismatch.");

        Marshaller<class_type*> self_marshaller;
        auto* instance = static_cast<class_type*>(self_marshaller.ConvertToCpp(self));
        detail::marshaller_tuple_t<arg_tuple> marshallers;

        return detail::InvokeMemberWithMarshallersOwnedImpl<result_type, arg_tuple>(
            method,
            *instance,
            marshallers,
            std::make_index_sequence<std::tuple_size_v<arg_tuple>>{},
            std::forward<TAbiArgs>(abiArguments)...);
    }

    template <typename FT, typename TCppArgumentTuple = typename detail::MemberFunctionTraits<FT>::argument_tuple, typename TSelfAbi, typename... TAbiArgs>
    static auto invoke_value(FT method, TSelfAbi self, TAbiArgs&&... abiArguments)
    {
        using traits = detail::MemberFunctionTraits<FT>;
        using result_type = typename traits::return_type;
        using class_type = typename traits::class_type;
        using signature_arg_tuple = typename traits::argument_tuple;
        using arg_tuple = TCppArgumentTuple;

        static_assert(
            std::tuple_size_v<arg_tuple> == std::tuple_size_v<signature_arg_tuple>,
            "SharpCpp marshaller argument tuple arity mismatch.");

        Marshaller<class_type*> self_marshaller;
        auto* instance = static_cast<class_type*>(self_marshaller.ConvertToCpp(self));
        detail::marshaller_tuple_t<arg_tuple> marshallers;

        return detail::InvokeMemberWithMarshallersValueImpl<result_type, arg_tuple>(
            method,
            *instance,
            marshallers,
            std::make_index_sequence<std::tuple_size_v<arg_tuple>>{},
            std::forward<TAbiArgs>(abiArguments)...);
    }

    template <typename FT, typename TCppArgumentTuple = typename detail::MemberFunctionTraits<FT>::argument_tuple, typename TSelf, typename... TAbiArgs>
    static decltype(auto) invoke_instance(FT method, TSelf* instance, TAbiArgs&&... abiArguments)
    {
        using traits = detail::MemberFunctionTraits<FT>;
        using result_type = typename traits::return_type;
        using signature_arg_tuple = typename traits::argument_tuple;
        using arg_tuple = TCppArgumentTuple;

        static_assert(
            std::tuple_size_v<arg_tuple> == std::tuple_size_v<signature_arg_tuple>,
            "SharpCpp marshaller argument tuple arity mismatch.");

        detail::marshaller_tuple_t<arg_tuple> marshallers;

        return detail::InvokeMemberWithMarshallersImpl<result_type, arg_tuple>(
            method,
            *instance,
            marshallers,
            std::make_index_sequence<std::tuple_size_v<arg_tuple>>{},
            std::forward<TAbiArgs>(abiArguments)...);
    }

    template <typename FT, typename TCppArgumentTuple = typename detail::MemberFunctionTraits<FT>::argument_tuple, typename TSelf, typename... TAbiArgs>
    static auto invoke_owned_instance(FT method, TSelf* instance, TAbiArgs&&... abiArguments)
    {
        using traits = detail::MemberFunctionTraits<FT>;
        using result_type = typename traits::return_type;
        using signature_arg_tuple = typename traits::argument_tuple;
        using arg_tuple = TCppArgumentTuple;

        static_assert(
            std::tuple_size_v<arg_tuple> == std::tuple_size_v<signature_arg_tuple>,
            "SharpCpp marshaller argument tuple arity mismatch.");

        detail::marshaller_tuple_t<arg_tuple> marshallers;

        return detail::InvokeMemberWithMarshallersOwnedImpl<result_type, arg_tuple>(
            method,
            *instance,
            marshallers,
            std::make_index_sequence<std::tuple_size_v<arg_tuple>>{},
            std::forward<TAbiArgs>(abiArguments)...);
    }

    template <typename FT, typename TMethod, typename TSelfAbi, typename... TAbiArgs,
        std::enable_if_t<
            std::is_member_function_pointer_v<TMethod> &&
            !std::is_same_v<TMethod, FT>,
            int> = 0>
    static decltype(auto) invoke(TMethod method, TSelfAbi self, TAbiArgs&&... abiArguments)
    {
        using traits = detail::MemberFunctionTraits<TMethod>;
        using result_type = typename traits::return_type;
        using class_type = typename traits::class_type;
        using arg_tuple = typename traits::argument_tuple;

        Marshaller<class_type*> self_marshaller;
        auto* instance = static_cast<class_type*>(self_marshaller.ConvertToCpp(self));
        detail::marshaller_tuple_t<arg_tuple> marshallers;

        return detail::InvokeMemberWithMarshallersImpl<result_type, arg_tuple>(
            method,
            *instance,
            marshallers,
            std::make_index_sequence<std::tuple_size_v<arg_tuple>>{},
            std::forward<TAbiArgs>(abiArguments)...);
    }

    template <typename T, typename... TArgs, typename... TAbiArgs>
    static auto construct(TAbiArgs&&... abiArguments) -> ret_t<T*>
    {
        return detail::ConstructImpl<T, TArgs...>(
            std::make_index_sequence<sizeof...(TArgs)>{},
            std::forward<TAbiArgs>(abiArguments)...);
    }

    template <typename T>
    static void destroy(arg_t<T*> self)
    {
        Marshaller<T*> self_marshaller;
        delete self_marshaller.ConvertToCpp(self);
    }

    template <typename TResult, typename TClass>
    static auto get_field(TResult TClass::*field, arg_t<TClass*> self) -> ret_t<TResult>
    {
        Marshaller<TClass*> self_marshaller;
        ReturnMarshaller<TResult> marshaller;
        return marshaller.ConvertToAbi(std::invoke(field, self_marshaller.ConvertToCpp(self)));
    }

    template <typename TResult, typename TClass>
    static auto get_field(TResult TClass::*field, arg_t<TClass*> self, std::true_type) -> field_t<TResult>
    {
        Marshaller<TClass*> self_marshaller;
        return std::addressof(std::invoke(field, self_marshaller.ConvertToCpp(self)));
    }

    template <typename TValue, typename TClass>
    static void set_field(TValue TClass::*field, arg_t<TClass*> self, arg_t<TValue> value)
    {
        Marshaller<TClass*> self_marshaller;
        ParameterMarshaller<TValue> marshaller;
        if constexpr (std::is_array_v<TValue>)
        {
            auto& target = std::invoke(field, self_marshaller.ConvertToCpp(self));
            auto& source = marshaller.ConvertToCpp(value);
            for (std::size_t index = 0; index < std::extent_v<TValue>; ++index)
            {
                target[index] = source[index];
            }
        }
        else if constexpr (detail::is_emplace_only_optional_v<TValue>)
        {
            // An optional of a class with a const member has no assignment operator, so the
            // setter destroys the old value and constructs the new one in place.
            auto& target = std::invoke(field, self_marshaller.ConvertToCpp(self));
            auto source = marshaller.ConvertToCpp(value);
            target.reset();
            if (source.has_value())
                target.emplace(std::move(*source));
        }
        else
        {
            std::invoke(field, self_marshaller.ConvertToCpp(self)) = marshaller.ConvertToCpp(value);
        }
    }

    template <typename TValue, typename TClass>
    static void set_field(TValue TClass::*field, arg_t<TClass*> self, arg_t<TValue>* value)
    {
        Marshaller<TClass*> self_marshaller;
        ParameterMarshaller<TValue> marshaller;
        std::invoke(field, self_marshaller.ConvertToCpp(self)) = marshaller.ConvertToCpp(*value);
    }

    template <typename TValue, typename TClass>
    static void set_field(TValue TClass::*field, arg_t<TClass*> self, SharpCpp::field_t<TValue> value, std::true_type)
    {
        Marshaller<TClass*> self_marshaller;
        auto& target = std::invoke(field, self_marshaller.ConvertToCpp(self));
        if (value == nullptr)
        {
            if constexpr (detail::has_clear_v<TValue>)
            {
                target.clear();
                return;
            }
            else
            {
                SharpCpp::FatalError("SharpCpp borrowed field setter cannot clear this field type.");
            }
        }

        target = std::move(*value);
    }
};
}
