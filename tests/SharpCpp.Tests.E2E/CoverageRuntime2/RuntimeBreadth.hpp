#pragma once

#include <array>
#include <list>
#include <map>
#include <numeric>
#include <string>
#include <unordered_map>
#include <utility>
#include <vector>

template <typename T>
struct runtime_breadth_owner
{
    explicit runtime_breadth_owner(T* value = nullptr)
        : value_(value)
    {
    }

    runtime_breadth_owner(runtime_breadth_owner&& other) noexcept
        : value_(other.detach())
    {
    }

    runtime_breadth_owner& operator=(runtime_breadth_owner&& other) noexcept
    {
        value_ = other.detach();
        return *this;
    }

    runtime_breadth_owner(const runtime_breadth_owner&) = delete;
    runtime_breadth_owner& operator=(const runtime_breadth_owner&) = delete;

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

struct runtime_breadth_cache_value
{
    explicit runtime_breadth_cache_value(int value)
        : value_(value)
    {
    }

    int get_value() const
    {
        return value_;
    }

    void set_value(int value)
    {
        value_ = value;
    }

private:
    int value_{};
};

inline runtime_breadth_cache_value& runtime_breadth_cache_storage()
{
    static runtime_breadth_cache_value value(0);
    return value;
}

inline runtime_breadth_cache_value* runtime_breadth_get_cache_borrowed(int value)
{
    auto& result = runtime_breadth_cache_storage();
    result.set_value(value);
    return &result;
}

inline runtime_breadth_owner<runtime_breadth_cache_value> runtime_breadth_take_cache_owned(int value)
{
    auto& result = runtime_breadth_cache_storage();
    result.set_value(value);
    return runtime_breadth_owner<runtime_breadth_cache_value>(&result);
}

inline int runtime_breadth_read_cache_value(runtime_breadth_cache_value* value)
{
    return value == nullptr ? -1 : value->get_value();
}

struct runtime_breadth_poly_base
{
    virtual ~runtime_breadth_poly_base() = default;

    virtual int get_kind() const
    {
        return 1;
    }

    virtual int get_value() const = 0;
};

struct runtime_breadth_poly_derived final : runtime_breadth_poly_base
{
    runtime_breadth_poly_derived() = default;
    runtime_breadth_poly_derived(const runtime_breadth_poly_derived&) = delete;
    runtime_breadth_poly_derived& operator=(const runtime_breadth_poly_derived&) = delete;

    int get_kind() const final
    {
        return 2;
    }

    int get_value() const final
    {
        return value_;
    }

    int get_bonus() const
    {
        return value_ + 5;
    }

    void set_value(int value)
    {
        value_ = value;
    }

private:
    int value_{};
};

inline runtime_breadth_poly_base* runtime_breadth_get_polymorphic(int value)
{
    static runtime_breadth_poly_derived result;
    result.set_value(value);
    return &result;
}

inline int runtime_breadth_lifecycle_destroyed_count = 0;

struct [[sharpcpp::director]] runtime_breadth_lifecycle
{
    explicit runtime_breadth_lifecycle(int value)
        : value_(value)
    {
    }

    virtual ~runtime_breadth_lifecycle()
    {
        ++runtime_breadth_lifecycle_destroyed_count;
    }

    virtual void on_native_destroying()
    {
    }

    int get_value() const
    {
        return value_;
    }

private:
    int value_{};
};

inline int runtime_breadth_destroy_lifecycle(runtime_breadth_lifecycle* value)
{
    if (value == nullptr)
        return -1;

    const auto result = value->get_value();
    value->on_native_destroying();
    delete value;
    return result;
}

inline int runtime_breadth_get_lifecycle_destroyed_count()
{
    return runtime_breadth_lifecycle_destroyed_count;
}

inline int runtime_breadth_payload_destroyed_count = 0;

struct runtime_breadth_payload
{
    explicit runtime_breadth_payload(int value)
        : value_(value)
    {
    }

    ~runtime_breadth_payload()
    {
        ++runtime_breadth_payload_destroyed_count;
    }

    int get_value() const
    {
        return value_;
    }

private:
    int value_{};
};

inline runtime_breadth_owner<runtime_breadth_payload> runtime_breadth_make_payload(int value)
{
    return runtime_breadth_owner<runtime_breadth_payload>(new runtime_breadth_payload(value));
}

struct [[sharpcpp::director]] runtime_breadth_transfer_director
{
    virtual ~runtime_breadth_transfer_director() = default;

    virtual runtime_breadth_owner<runtime_breadth_payload> create_payload(int value)
    {
        return runtime_breadth_make_payload(value);
    }

    int invoke_and_destroy(int value)
    {
        auto owner = create_payload(value);
        auto* payload = owner.detach();
        if (payload == nullptr)
            return -1;

        const auto result = payload->get_value();
        delete payload;
        return result;
    }
};

inline int runtime_breadth_get_payload_destroyed_count()
{
    return runtime_breadth_payload_destroyed_count;
}

inline std::array<unsigned, 3> runtime_breadth_make_array()
{
    return { 3u, 5u, 7u };
}

inline unsigned runtime_breadth_sum_array(const std::array<unsigned, 3>& values)
{
    return std::accumulate(values.begin(), values.end(), 0u);
}

inline std::vector<unsigned> runtime_breadth_make_vector()
{
    return { 2u, 4u, 6u };
}

inline unsigned runtime_breadth_sum_vector(const std::vector<unsigned>& values)
{
    return std::accumulate(values.begin(), values.end(), 0u);
}

inline std::list<unsigned> runtime_breadth_make_list()
{
    return { 11u, 13u };
}

inline unsigned runtime_breadth_sum_list(const std::list<unsigned>& values)
{
    return std::accumulate(values.begin(), values.end(), 0u);
}

inline std::map<unsigned, unsigned> runtime_breadth_make_map()
{
    return { { 2u, 3u }, { 5u, 7u } };
}

inline unsigned runtime_breadth_sum_map(const std::map<unsigned, unsigned>& values)
{
    auto result = 0u;
    for (const auto& [key, value] : values)
        result += key + value;
    return result;
}

inline std::unordered_map<unsigned, unsigned> runtime_breadth_make_unordered_map()
{
    return { { 3u, 4u }, { 6u, 8u } };
}

inline unsigned runtime_breadth_sum_unordered_map(const std::unordered_map<unsigned, unsigned>& values)
{
    auto result = 0u;
    for (const auto& [key, value] : values)
        result += key + value;
    return result;
}

inline std::string runtime_breadth_make_string()
{
    return "runtime";
}

inline unsigned runtime_breadth_sum_string(const std::string& value)
{
    return std::accumulate(value.begin(), value.end(), 0u,
        [](unsigned result, unsigned char byte) { return result + byte; });
}
