#pragma once

#include <string>

#define E2E_UNDEFAULTABLE_OFFSET 40

inline int describe_level(int value, int threshold = 5, const char* label = "default")
{
    (void)label;
    return value >= threshold ? 1 : 0;
}

inline int check_undefaultable_only(const char* label = "none")
{
    (void)label;
    return 42;
}

inline int applied_config_value{};

inline void apply_config(const char* config = "default", int factor = 3)
{
    applied_config_value = (config != nullptr ? config[0] : 0) + factor;
}

inline int get_applied_config_value()
{
    return applied_config_value;
}

inline int mixed_defaults(int a = 1, const char* b = "b", int c = 3)
{
    (void)b;
    return (a * 100) + c;
}

inline int undefaultable_cascade_value(
    int seed,
    const char* tag = "native",
    int offset = E2E_UNDEFAULTABLE_OFFSET,
    int tail = 5)
{
    return (seed * 1000) + (tag != nullptr ? tag[0] : 0) + (offset * 10) + tail;
}

inline void undefaultable_cascade_void(
    int seed,
    const char* tag = "native",
    int offset = E2E_UNDEFAULTABLE_OFFSET,
    int tail = 5)
{
    applied_config_value = undefaultable_cascade_value(seed, tag, offset, tail);
}

struct undefaultable_result
{
    int get_value() const { return value; }
    int value{};
};

inline undefaultable_result* select_undefaultable_result(
    int seed,
    const char* tag = "native",
    int offset = E2E_UNDEFAULTABLE_OFFSET,
    int tail = 5)
{
    static undefaultable_result result;
    result.value = undefaultable_cascade_value(seed, tag, offset, tail);
    return &result;
}

struct undefaultable_handler
{
    explicit undefaultable_handler(int id, const char* name = "unnamed")
        : id_(id)
        , name_(name ? name : "unnamed")
    {
    }

    undefaultable_handler(
        int id,
        int bias,
        const char* name = "unnamed",
        int offset = E2E_UNDEFAULTABLE_OFFSET,
        int tail = 5)
        : id_(id + bias + offset + tail)
        , name_(name ? name : "unnamed")
    {
    }

    int get_id() const
    {
        return id_;
    }

    const char* get_name() const
    {
        return name_.c_str();
    }

    int process(int amount, const char* tag = "default", int scale = 2) const
    {
        return id_ + amount + scale;
    }

    static int lookup(const char* key = "default", int fallback = -1)
    {
        (void)key;
        return fallback;
    }

    static void clear_cache(const char* reason = "unknown")
    {
        cache_value_ = reason != nullptr ? reason[0] : 0;
    }

    static int get_cache_value()
    {
        return cache_value_;
    }

    int cascade(
        int amount,
        const char* tag = "member",
        int offset = E2E_UNDEFAULTABLE_OFFSET,
        int tail = 5) const
    {
        return (id_ * 10000) + (amount * 1000) +
            (tag != nullptr ? tag[0] : 0) + (offset * 10) + tail;
    }

    static int static_cascade(
        int seed,
        const char* tag = "static",
        int offset = E2E_UNDEFAULTABLE_OFFSET,
        int tail = 5)
    {
        return undefaultable_cascade_value(seed, tag, offset, tail);
    }

    static void static_cascade_void(
        int seed,
        const char* tag = "static",
        int offset = E2E_UNDEFAULTABLE_OFFSET,
        int tail = 5)
    {
        cache_value_ = undefaultable_cascade_value(seed, tag, offset, tail);
    }

private:
    inline static int cache_value_{};
    int id_{};
    std::string name_;
};
