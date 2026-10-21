#pragma once

#include <cstddef>
#include <memory>
#include <type_traits>
#include <utility>

// A minimal stand-in for eastl::function (EASTL). The core E2E tests have no EASTL, so they use
// this type to check that SharpCpp supports a function object type other than std::function in
// the same way. A fixture opts in when it includes this header. EaFunctionMarshallers.h holds the
// marshaller specializations, which mirror the std::function specializations in
// SharpCpp/Marshaller.hpp (and the eastl::function specializations of the rbfx addon).
namespace eastl
{
template <class TSignature>
class function;

template <class TResult, class... TArgs>
class function<TResult(TArgs...)>
{
public:
    function() noexcept = default;

    function(std::nullptr_t) noexcept
    {
    }

    template <class TFunctor,
        class = std::enable_if_t<!std::is_same_v<std::decay_t<TFunctor>, function> &&
            std::is_invocable_r_v<TResult, std::decay_t<TFunctor>&, TArgs...>>>
    function(TFunctor&& functor)
        : callable_(std::make_unique<Model<std::decay_t<TFunctor>>>(std::forward<TFunctor>(functor)))
    {
    }

    function(const function& other)
        : callable_(other.callable_ ? other.callable_->Clone() : nullptr)
    {
    }

    function(function&& other) noexcept = default;

    function& operator=(const function& other)
    {
        function(other).swap(*this);
        return *this;
    }

    function& operator=(function&& other) noexcept = default;

    function& operator=(std::nullptr_t) noexcept
    {
        callable_.reset();
        return *this;
    }

    ~function() = default;

    explicit operator bool() const noexcept
    {
        return callable_ != nullptr;
    }

    TResult operator()(TArgs... arguments) const
    {
        return callable_->Invoke(std::forward<TArgs>(arguments)...);
    }

    void swap(function& other) noexcept
    {
        callable_.swap(other.callable_);
    }

private:
    struct Concept
    {
        virtual ~Concept() = default;
        virtual TResult Invoke(TArgs... arguments) = 0;
        virtual std::unique_ptr<Concept> Clone() const = 0;
    };

    template <class TFunctor>
    struct Model final : Concept
    {
        template <class T>
        explicit Model(T&& value)
            : functor(std::forward<T>(value))
        {
        }

        TResult Invoke(TArgs... arguments) override
        {
            return functor(std::forward<TArgs>(arguments)...);
        }

        std::unique_ptr<Concept> Clone() const override
        {
            return std::make_unique<Model>(functor);
        }

        TFunctor functor;
    };

    std::unique_ptr<Concept> callable_;
};
}

namespace ea = eastl;
