#pragma once

#include <algorithm>
#include <functional>
#include <span>
#include <string_view>
#include <vector>

namespace signal_like
{
struct point
{
    int x{};
    int y{};
};

/// A plain native receiver. The Urho3D receiver is a RefCounted object.
struct receiver
{
    int id{};
};

struct default_sender
{
    int id{};
};

struct custom_sender
{
    int id{};
};

/// The subscription list of a signal. A higher priority runs first.
template <class Priority, class Sender, class... A>
class signal_base
{
public:
    void unsubscribe(receiver* target)
    {
        subscriptions_.erase(
            std::remove_if(subscriptions_.begin(), subscriptions_.end(),
                [target](const subscription& item) { return item.target == target; }),
            subscriptions_.end());
    }

    /// Returns false and calls no slot when an invocation is already in progress.
    bool invoke(Sender* sender, A... args)
    {
        if (invoking_)
            return false;

        invoking_ = true;
        const auto copy = subscriptions_;
        for (const auto& item : copy)
            item.slot(sender, args...);
        invoking_ = false;
        return true;
    }

    bool has_subscriptions() const { return !subscriptions_.empty(); }

    int count() const { return static_cast<int>(subscriptions_.size()); }

protected:
    struct subscription
    {
        receiver* target{};
        Priority priority{};
        std::function<void(Sender*, A...)> slot;
    };

    void add(receiver* target, Priority priority, std::function<void(Sender*, A...)> slot)
    {
        auto position = std::find_if(subscriptions_.begin(), subscriptions_.end(),
            [priority](const subscription& item) { return item.priority < priority; });
        subscriptions_.insert(position, subscription{target, priority, std::move(slot)});
    }

private:
    std::vector<subscription> subscriptions_;
    bool invoking_{};
};

/// The primary template is only forward-declared, as Urho3D::Signal is.
template <class Signature, class Sender = default_sender>
struct sig;

/// The partial specialization holds the members. The pack keeps the slot arguments.
template <class Sender, class... A>
struct sig<void(A...), Sender> : signal_base<int, Sender, A...>
{
    void subscribe(receiver* target, std::function<void(A...)> slot)
    {
        this->add(target, 0, [slot](Sender*, A... args) { slot(args...); });
    }

    void subscribe_with_sender(receiver* target, std::function<void(Sender*, A...)> slot)
    {
        this->add(target, 0, std::move(slot));
    }
};

/// The PrioritySignal form: a subscription has a priority.
template <class Signature, class Priority = int, class Sender = default_sender>
struct prio_sig;

template <class Sender, class Priority, class... A>
struct prio_sig<void(A...), Priority, Sender> : signal_base<Priority, Sender, A...>
{
    void subscribe(receiver* target, Priority priority, std::function<void(A...)> slot)
    {
        this->add(target, priority, [slot](Sender*, A... args) { slot(args...); });
    }
};

struct button
{
    sig<void()> clicked;
    sig<void(int)> pressed;
    sig<void(int)> released;
    sig<void(const point&)> moved;
    sig<void(point)> dropped;
    sig<void(int&), custom_sender> adjusted;
    sig<void(bool&)> handled;
    /// The shape of Urho3D::NetworkConnection::OnDataReceived: a byte view and a ref flag.
    sig<void(std::span<const unsigned char>, bool&)> received;
    /// A string view and a span view in one slot, with a sender.
    sig<void(std::string_view, std::span<const int>), custom_sender> labelled;
    sig<void(int), custom_sender> sent;
    prio_sig<void(int)> ordered;
    prio_sig<void(int), float> weighted;
};

struct slider
{
    sig<void(int)> changed;
};

/// Adds a native slot, so that a test can count the subscriptions of a field.
inline void add_native_slot(button& target)
{
    target.pressed.subscribe(nullptr, [](int) {});
}

/// Invokes the bool& signal from native code and returns the value that the slots wrote.
inline bool invoke_handled(button& target)
{
    bool value = false;
    target.handled.invoke(nullptr, value);
    return value;
}

/// Invokes the int& signal from native code and returns the value that the slots wrote.
inline int invoke_adjusted(button& target, custom_sender* sender, int initial)
{
    int value = initial;
    target.adjusted.invoke(sender, value);
    return value;
}

/// Invokes the byte-view signal from native code. The view is valid only during the slots.
inline bool invoke_received(button& target, int count)
{
    unsigned char bytes[4]{10, 20, 30, 40};
    bool handled = false;
    target.received.invoke(nullptr, std::span<const unsigned char>(bytes, static_cast<std::size_t>(count < 4 ? count : 4)), handled);
    return handled;
}

/// Invokes the string-view signal from native code.
inline void invoke_labelled(button& target, custom_sender* sender)
{
    const int values[2]{3, 4};
    target.labelled.invoke(sender, std::string_view("label"), std::span<const int>(values));
}
}
