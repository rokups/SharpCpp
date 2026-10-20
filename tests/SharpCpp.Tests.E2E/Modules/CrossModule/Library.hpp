#pragma once

// Library and App are generated as two modules; App imports Library's metadata the way
// an application binds against an engine. Neither is post-processed by the driver.

#include <functional>
#include <string>
#include <unordered_map>
#include <utility>
#include <vector>

namespace cross_library
{
using progress_callback = std::function<void(int)>;
/// The dependent module sees the ref parameter through the imported alias.
using adjust_callback = std::function<void(int&)>;

inline int library_adjust(adjust_callback callback, int value)
{
    callback(value);
    return value;
}

struct library_settings
{
    std::string name;
    std::vector<int> values;
    std::unordered_map<int, int> counters;

    int total() const
    {
        int result = 0;
        for (int value : values)
            result += value;
        return result;
    }
};

// Spelled with the defaulted template arguments omitted.
using score_map = std::unordered_map<int, float>;

// A vector of an element type with no marshaller cannot be projected; the alias is pruned.
using enabled_passes = std::vector<std::pair<std::string, bool>>;

inline float library_score(const score_map& scores, int key)
{
    auto found = scores.find(key);
    return found == scores.end() ? -1.0f : found->second;
}

/// A handle record that only appears as the mapped type of a standard container returned by a
/// member of library_widget. A module that derives from library_widget and never names the marker
/// still emits the container provider, which marshals the handle through a marshaller of that module.
struct library_marker
{
    int id() const { return 3; }
};

struct library_widget
{
    virtual ~library_widget() = default;

    const std::unordered_map<int, library_marker*>& markers() const { return markers_; }

    void set_progress(progress_callback callback) { progress_ = std::move(callback); }

    void report(int value)
    {
        if (progress_)
            progress_(value);
    }

    std::string describe() const { return "widget"; }

private:
    progress_callback progress_;
    std::unordered_map<int, library_marker*> markers_;
};
/// A hasher with a defaulted parameter (eastl::hash<T, Enable = void>): a map spelled with
/// it written out and one taking it as a default are one specialization.
template <class T, class Enable = void>
struct library_hash : std::hash<T>
{
};

template <class K, class V, class H = library_hash<K>>
using library_map = std::unordered_map<K, V, H>;

inline double library_sum(const library_map<int, double>& values)
{
    double result = 0;
    for (const auto& [key, value] : values)
        result += value;
    return result;
}

inline double library_sum_written(const std::unordered_map<int, double, library_hash<int>>& values)
{
    return library_sum(values);
}

/// A member template instantiated with a callback named by a nested alias (rbfx's
/// EventHandler::HandlerFunction) keeps the alias of the callback's parameter.
struct library_handler
{
    using function = std::function<void(score_map&)>;

    int id() const { return 1; }
};

struct library_events
{
    template <class T>
    [[sharpcpp::instantiate("cross_library::library_handler::function")]]
    int subscribe(T handler)
    {
        score_map scores{{1, 2.5f}};
        handler(scores);
        return static_cast<int>(scores.size());
    }
};

} // namespace cross_library
