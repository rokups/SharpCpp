#pragma once

#include <utility>

namespace coverage_matrix
{
struct projected_payload
{
    explicit projected_payload(int value)
        : value_(value)
    {
    }

    int get_value() const
    {
        return value_;
    }

private:
    int value_{};
};

template <typename T>
struct projected_owner
{
    projected_owner() = default;

    explicit projected_owner(T* value)
        : value_(value)
    {
    }

    projected_owner(projected_owner&& other) noexcept
        : value_(other.detach())
    {
    }

    projected_owner& operator=(projected_owner&& other) noexcept
    {
        value_ = other.detach();
        return *this;
    }

    projected_owner(const projected_owner&) = default;
    projected_owner& operator=(const projected_owner&) = default;

    T* get() const
    {
        return value_;
    }

    T* detach()
    {
        return std::exchange(value_, nullptr);
    }

private:
    T* value_{};
};

inline projected_owner<projected_payload> make_projected_payload(int value)
{
    return projected_owner<projected_payload>(new projected_payload(value));
}

inline int consume_projected_payload(projected_owner<projected_payload> value)
{
    return value.get() != nullptr ? value.get()->get_value() : -1;
}

struct [[sharpcpp::director]] projected_owner_director
{
    projected_owner_director() = default;
    virtual ~projected_owner_director() = default;

    virtual projected_owner<projected_payload> create_payload(int value)
    {
        return make_projected_payload(value);
    }

    int invoke_and_consume(int value)
    {
        auto owner = create_payload(value);
        auto* payload = owner.detach();
        if (payload == nullptr)
            return -1;

        const int result = payload->get_value();
        delete payload;
        return result;
    }
};
}
