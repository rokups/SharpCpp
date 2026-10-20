#pragma once

#include <SharpCpp/ManagedCallback.hpp>
#include <SharpCpp/Marshaller.hpp>

#include "SignalLike.hpp"

namespace SharpCpp
{
/// Each instantiation of the template becomes one non-generic wrapper class.
/// The pack `Args` expands into the parameters of each instantiation.
template <class Sender, class... Args>
struct [[sharpcpp::managed_type(
    public_type = "Sig",
    flatten_instantiations = true
)]] Marshaller<signal_like::sig<void(Args...), Sender>*>
{
    using cpp_type = signal_like::sig<void(Args...), Sender>*;
    using abi_type = signal_like::sig<void(Args...), Sender>*;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept { return value; }
    constexpr cpp_type ConvertToCpp(abi_type value) noexcept { return value; }

    [[sharpcpp::expose_member]]
    static void Subscribe(signal_like::sig<void(Args...), Sender>& self, signal_like::receiver* receiver, SharpCpp::ManagedCallback<void(Args...)> handler)
    {
        self.subscribe(receiver, std::move(handler));
    }

    [[sharpcpp::expose_member]]
    static void SubscribeWithSender(signal_like::sig<void(Args...), Sender>& self, signal_like::receiver* receiver, SharpCpp::ManagedCallback<void(Sender*, Args...)> handler)
    {
        self.subscribe_with_sender(receiver, std::move(handler));
    }

    [[sharpcpp::expose_member]]
    static void Unsubscribe(signal_like::sig<void(Args...), Sender>& self, signal_like::receiver* receiver) { self.unsubscribe(receiver); }

    [[sharpcpp::expose_member(throw_on_false = "The signal \"sig\" is already being invoked.")]]
    static bool Invoke(signal_like::sig<void(Args...), Sender>& self, Sender* sender, Args... args) { return self.invoke(sender, args...); }

    [[sharpcpp::expose_member]]
    static bool HasSubscriptions(signal_like::sig<void(Args...), Sender>& self) { return self.has_subscriptions(); }

    [[sharpcpp::expose_member]]
    static int Count(signal_like::sig<void(Args...), Sender>& self) { return self.count(); }

    // The generator refuses throw_on_false here: the result is not a bool.
    [[sharpcpp::expose_member(throw_on_false = "No subscriptions.")]]
    static int CountOrFail(signal_like::sig<void(Args...), Sender>& self) { return self.count(); }
};

template <class Sender, class Priority, class... Args>
struct [[sharpcpp::managed_type(
    public_type = "PrioSig",
    flatten_instantiations = true
)]] Marshaller<signal_like::prio_sig<void(Args...), Priority, Sender>*>
{
    using cpp_type = signal_like::prio_sig<void(Args...), Priority, Sender>*;
    using abi_type = signal_like::prio_sig<void(Args...), Priority, Sender>*;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept { return value; }
    constexpr cpp_type ConvertToCpp(abi_type value) noexcept { return value; }

    [[sharpcpp::expose_member]]
    static void Subscribe(signal_like::prio_sig<void(Args...), Priority, Sender>& self, signal_like::receiver* receiver, Priority priority, SharpCpp::ManagedCallback<void(Args...)> handler)
    {
        self.subscribe(receiver, priority, std::move(handler));
    }

    [[sharpcpp::expose_member(throw_on_false = "The signal is already being invoked.")]]
    static bool Invoke(signal_like::prio_sig<void(Args...), Priority, Sender>& self, Sender* sender, Args... args) { return self.invoke(sender, args...); }

    [[sharpcpp::expose_member]]
    static bool HasSubscriptions(signal_like::prio_sig<void(Args...), Priority, Sender>& self) { return self.has_subscriptions(); }
};
}
