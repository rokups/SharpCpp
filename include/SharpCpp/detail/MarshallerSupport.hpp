#pragma once

#include <SharpCpp/Marshaller.hpp>
#include <functional>
#include <tuple>
#include <type_traits>
#include <utility>

namespace SharpCpp
{
namespace detail
{
template <typename T, typename = void>
struct has_sync : std::false_type {};

template <typename T>
struct has_sync<T, std::void_t<decltype(std::declval<T&>().Sync())>> : std::true_type {};

template <typename T>
inline constexpr bool has_sync_v = has_sync<T>::value;

template <typename T>
using remove_cvref_t = std::remove_cv_t<std::remove_reference_t<T>>;

template <typename TMarshaller>
constexpr void SyncMarshaller(TMarshaller& marshaller)
{
    if constexpr (has_sync_v<TMarshaller>)
    {
        marshaller.Sync();
    }
}

template <typename TMarshallerTuple, std::size_t... TIndices>
constexpr void SyncMarshallersImpl(TMarshallerTuple& marshallers, std::index_sequence<TIndices...>)
{
    (SyncMarshaller(std::get<TIndices>(marshallers)), ...);
}

template <typename TMarshallerTuple>
constexpr void SyncMarshallers(TMarshallerTuple& marshallers)
{
    using tuple_type = remove_cvref_t<TMarshallerTuple>;
    SyncMarshallersImpl(marshallers, std::make_index_sequence<std::tuple_size_v<tuple_type>>{});
}

template <typename TInvoker, typename TMarshallerTuple>
constexpr decltype(auto) InvokeAndSyncMarshallers(TInvoker&& invoker, TMarshallerTuple& marshallers)
{
    if constexpr (std::is_void_v<std::invoke_result_t<TInvoker>>)
    {
        std::invoke(std::forward<TInvoker>(invoker));
        SyncMarshallers(marshallers);
        return;
    }
    else
    {
        using result_type = std::invoke_result_t<TInvoker>;

        if constexpr (std::is_lvalue_reference_v<result_type>)
        {
            auto* result = std::addressof(std::invoke(std::forward<TInvoker>(invoker)));
            SyncMarshallers(marshallers);
            return static_cast<result_type>(*result);
        }
        else
        {
            auto result = std::invoke(std::forward<TInvoker>(invoker));
            SyncMarshallers(marshallers);
            return result;
        }
    }
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

template <typename TResult, typename TCppArgumentTuple, typename TCallable, typename TMarshallerTuple, typename... TAbiArgs, std::size_t... TIndices>
constexpr decltype(auto) InvokeWithMarshallersImpl(
    TCallable&& callable,
    TMarshallerTuple& marshallers,
    std::index_sequence<TIndices...>,
    TAbiArgs&&... abiArguments)
{
    auto invoke_fn = [&]() -> decltype(auto)
    {
        return std::invoke(
            std::forward<TCallable>(callable),
            std::get<TIndices>(marshallers).ConvertToCpp(std::forward<TAbiArgs>(abiArguments))...);
    };

    if constexpr (std::is_void_v<TResult>)
    {
        InvokeAndSyncMarshallers(invoke_fn, marshallers);
        return;
    }
    else
    {
        ReturnMarshaller<TResult> ret_marshaller;
        decltype(auto) result = InvokeAndSyncMarshallers(invoke_fn, marshallers);
        return ret_marshaller.ConvertToAbi(result);
    }
}

template <typename TResult, typename TCppArgumentTuple, typename TMethod, typename TObject, typename TMarshallerTuple, typename... TAbiArgs, std::size_t... TIndices>
constexpr decltype(auto) InvokeMemberWithMarshallersImpl(
    TMethod method,
    TObject&& instance,
    TMarshallerTuple& marshallers,
    std::index_sequence<TIndices...>,
    TAbiArgs&&... abiArguments)
{
    auto invoke_fn = [&]() -> decltype(auto)
    {
        return std::invoke(
            method,
            std::forward<TObject>(instance),
            std::get<TIndices>(marshallers).ConvertToCpp(std::forward<TAbiArgs>(abiArguments))...);
    };

    if constexpr (std::is_void_v<TResult>)
    {
        InvokeAndSyncMarshallers(invoke_fn, marshallers);
        return;
    }
    else
    {
        ReturnMarshaller<TResult> ret_marshaller;
        decltype(auto) result = InvokeAndSyncMarshallers(invoke_fn, marshallers);
        return ret_marshaller.ConvertToAbi(result);
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

struct [[maybe_unused]] MarshallerInvokerImpl;
}

struct MarshallerInvoker
{
    template <typename FT, typename... TAbiArgs>
    static decltype(auto) invoke_as(typename std::add_pointer_t<FT> callable, TAbiArgs&&... abiArguments)
    {
        using traits = detail::FunctionTraits<FT>;
        using result_type = typename traits::return_type;
        using arg_tuple = typename traits::argument_tuple;

        detail::marshaller_tuple_t<arg_tuple> marshallers;
        return detail::InvokeWithMarshallersImpl<result_type, arg_tuple>(
            callable,
            marshallers,
            std::make_index_sequence<std::tuple_size_v<arg_tuple>>{},
            std::forward<TAbiArgs>(abiArguments)...);
    }

    template <typename FT, typename TCallable, typename... TAbiArgs>
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
            using arg_tuple = typename traits::argument_tuple;

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
            using traits = detail::FunctionTraits<FT>;
            using result_type = typename traits::return_type;
            using arg_tuple = typename traits::argument_tuple;

            detail::marshaller_tuple_t<arg_tuple> marshallers;
            return detail::InvokeWithMarshallersImpl<result_type, arg_tuple>(
                std::forward<TCallable>(callable),
                marshallers,
                std::make_index_sequence<std::tuple_size_v<arg_tuple>>{},
                std::forward<TAbiArgs>(abiArguments)...);
        }
    }

    template <typename FT, typename TSelfAbi, typename... TAbiArgs>
    static decltype(auto) invoke(FT method, TSelfAbi self, TAbiArgs&&... abiArguments)
    {
        using traits = detail::MemberFunctionTraits<FT>;
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

    template <typename FT, typename TSelf, typename... TAbiArgs>
    static decltype(auto) invoke_instance(FT method, TSelf* instance, TAbiArgs&&... abiArguments)
    {
        using traits = detail::MemberFunctionTraits<FT>;
        using result_type = typename traits::return_type;
        using arg_tuple = typename traits::argument_tuple;

        detail::marshaller_tuple_t<arg_tuple> marshallers;

        return detail::InvokeMemberWithMarshallersImpl<result_type, arg_tuple>(
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

    template <typename TValue, typename TClass>
    static void set_field(TValue TClass::*field, arg_t<TClass*> self, arg_t<TValue> value)
    {
        Marshaller<TClass*> self_marshaller;
        ParameterMarshaller<TValue> marshaller;
        std::invoke(field, self_marshaller.ConvertToCpp(self)) = marshaller.ConvertToCpp(value);
    }
};
}
