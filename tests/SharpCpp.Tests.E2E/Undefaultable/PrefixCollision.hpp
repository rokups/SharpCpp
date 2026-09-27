#pragma once

#include <cstddef>

// Mirrors Diligent's ShaderCreateInfo: overloads share a `(const text_char*, size_t)` prefix and
// continue with a defaulted `const text_char*`. `text_char` is a typedef of `char`, so the
// defaulted parameter is undefaultable through the `const char*` marshaller configuration and
// every overload splits into a prefix overload plus a full overload. Prefix overloads that
// collide across the overload set must be emitted once.
namespace e2e::undefaultable_prefix
{
using text_char = char;

inline int measure_text(const text_char* text, std::size_t length, const text_char* entry)
{
    auto value = static_cast<int>(length) * 100;
    if (text != nullptr)
        value += text[0] == 'a' ? 10 : 20;
    if (entry != nullptr)
        value += entry[0] == 'm' ? 1 : 2;
    return value;
}

// Two constructors and two methods sharing the prefix: calling with only the prefix is ambiguous
// in C++, so the prefix overload is dropped and only the longer overloads remain.
struct prefix_shared_info
{
    prefix_shared_info(const text_char* source, std::size_t length, const text_char* entry = "main", int macros = 7, int language = 3)
        : origin_(1), value_(measure_text(source, length, entry) + (macros * 10000) + (language * 1000))
    {
    }

    prefix_shared_info(const text_char* source, std::size_t length, const text_char* entry = "main", double language = 4.0)
        : origin_(2), value_(measure_text(source, length, entry) + static_cast<int>(language * 1000))
    {
    }

    int describe(const text_char* source, std::size_t length, const text_char* entry = "main", int macros = 7) const
    {
        return 100000 + measure_text(source, length, entry) + macros * 1000;
    }

    int describe(const text_char* source, std::size_t length, const text_char* entry = "main", double language = 4.0) const
    {
        return 200000 + measure_text(source, length, entry) + static_cast<int>(language * 1000);
    }

    int origin() const { return origin_; }
    int value() const { return value_; }

private:
    int origin_{};
    int value_{};
};

// One overload's full parameter list is the shared prefix: the prefix call binds to it through
// the exact member pointer. (A constructor with that shape cannot be called natively, as
// constructors have no address to disambiguate the C++ call.)
struct prefix_exact_info
{
    int describe(const text_char* source, std::size_t length) const
    {
        return 300000 + measure_text(source, length, nullptr);
    }

    int describe(const text_char* source, std::size_t length, const text_char* entry = "main", int language = 3) const
    {
        return 400000 + measure_text(source, length, entry) + (language * 1000);
    }
};

inline int prefix_shared_describe(const text_char* source, std::size_t length, const text_char* entry = "main", int macros = 7)
{
    return 500000 + measure_text(source, length, entry) + macros * 1000;
}

inline int prefix_shared_describe(const text_char* source, std::size_t length, const text_char* entry = "main", double language = 4.0)
{
    return 600000 + measure_text(source, length, entry) + static_cast<int>(language * 1000);
}

inline int prefix_exact_describe(const text_char* source, std::size_t length)
{
    return 700000 + measure_text(source, length, nullptr);
}

inline int prefix_exact_describe(const text_char* source, std::size_t length, const text_char* entry = "main", int language = 3)
{
    return 800000 + measure_text(source, length, entry) + (language * 1000);
}
} // namespace e2e::undefaultable_prefix
