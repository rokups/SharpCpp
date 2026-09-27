#pragma once

#include <array>
#include <list>
#include <map>
#include <memory>
#include <numeric>
#include <string>
#include <unordered_map>
#include <vector>

inline std::string e2e_make_std_text(const char* value)
{
    return std::string(value);
}

inline int e2e_checksum_std_text(const std::string& value)
{
    return std::accumulate(value.begin(), value.end(), 0, [](int result, unsigned char byte) {
        return result + byte;
    });
}

// Copy constructor is implicitly deleted by the unique_ptr member; containers of it
// cannot be bound by value and must degrade without breaking the module.
struct e2e_std_move_only
{
    std::unique_ptr<int> data;
    int size{};
};

inline int e2e_count_std_move_only(const std::vector<e2e_std_move_only>& values)
{
    return static_cast<int>(values.size());
}

struct e2e_std_probe
{
    int id{};
};

// Elements are pointers to const; the element marshaller binds the non-const pointer.
inline std::array<const e2e_std_probe*, 2> e2e_make_std_probes(e2e_std_probe* first, e2e_std_probe* second)
{
    return { first, second };
}

// Assignment is implicitly deleted by the const member, so fields of this type are
// read-only; the owner stays default-constructible.
class e2e_std_hashed_name
{
public:
    e2e_std_hashed_name() = default;
    explicit e2e_std_hashed_name(int length)
        : length_(length)
    {
    }

    int get_length() const { return length_; }
    int get_hash() const { return hash_; }

private:
    int length_{};
    const int hash_{7};
};

struct e2e_std_named_setting
{
    e2e_std_hashed_name name;
    int priority{};
};

inline std::vector<int> e2e_make_std_numbers()
{
    return { 2, 4, 6 };
}

inline int e2e_sum_std_numbers(const std::vector<int>& values)
{
    return std::accumulate(values.begin(), values.end(), 0);
}

inline std::array<int, 4> e2e_make_std_array_numbers()
{
    return { 1, 3, 5, 7 };
}

inline int e2e_sum_std_array_numbers(const std::array<int, 4>& values)
{
    return std::accumulate(values.begin(), values.end(), 0);
}

inline std::array<short, 3> e2e_make_std_array_shorts()
{
    return {2, 4, 6};
}

inline int e2e_sum_std_array_shorts(const std::array<short, 3>& values)
{
    return std::accumulate(values.begin(), values.end(), 0);
}

inline std::list<std::string> e2e_make_std_words()
{
    return { "red", "green", "blue" };
}

inline int e2e_count_std_words(const std::list<std::string>& values)
{
    return static_cast<int>(values.size());
}

inline std::string e2e_join_std_words(const std::list<std::string>& values)
{
    std::string result;
    for (const auto& value : values)
    {
        if (!result.empty())
        {
            result += ',';
        }

        result += value;
    }

    return result;
}

inline std::map<std::string, int> e2e_make_std_scores()
{
    return { { "one", 1 }, { "two", 2 } };
}

inline int e2e_total_std_scores(const std::map<std::string, int>& values)
{
    auto total = 0;
    for (const auto& [key, value] : values)
    {
        total += value;
    }

    return total;
}

using e2e_std_score_alias = std::map<int, int>;

inline e2e_std_score_alias e2e_make_aliased_std_scores()
{
    return { { 3, 5 }, { 7, 11 } };
}

inline int e2e_total_aliased_std_scores(const e2e_std_score_alias& values)
{
    auto total = 0;
    for (const auto& [key, value] : values)
        total += key + value;
    return total;
}

inline std::unordered_map<std::string, int> e2e_make_std_lookup()
{
    return { { "left", 7 }, { "right", 9 } };
}

inline int e2e_total_std_lookup(const std::unordered_map<std::string, int>& values)
{
    auto total = 0;
    for (const auto& [key, value] : values)
    {
        total += value;
    }

    return total;
}

namespace qualified_stdlib
{
enum class lookup_key : int
{
    left = 1,
    right = 2
};

using lookup = std::unordered_map<lookup_key, int>;

inline lookup make_lookup()
{
    return {{lookup_key::left, 5}, {lookup_key::right, 7}};
}

inline int total_lookup(const lookup& values)
{
    auto total = 0;
    for (const auto& [key, value] : values)
        total += static_cast<int>(key) + value;
    return total;
}
}

struct std_library_owner
{
    std_library_owner()
        : numbers({ 1, 2, 3 })
        , scores({ { "alpha", 1 }, { "beta", 2 } })
        , words({ "red", "green" })
        , lookup({ { "left", 7 }, { "right", 9 } })
    {
    }

    const std::vector<int>& get_numbers_ref() const
    {
        return numbers;
    }

    const std::map<std::string, int>& get_scores_ref() const
    {
        return scores;
    }

    const std::list<std::string>& get_words_ref() const
    {
        return words;
    }

    const std::unordered_map<std::string, int>& get_lookup_ref() const
    {
        return lookup;
    }

    std::vector<int> numbers;
    std::map<std::string, int> scores;
    std::list<std::string> words;
    std::unordered_map<std::string, int> lookup;
};
