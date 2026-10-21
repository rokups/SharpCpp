#pragma once

// Library and App are generated as two modules; App imports Library's metadata the way
// an application binds against an engine. Neither is post-processed by the driver.

#include <functional>
#include <string>
#include <unordered_map>
#include <utility>
#include <vector>

#include "../../Callback/EaFunction.h"

namespace cross_library
{
using progress_callback = std::function<void(int)>;
/// The dependent module sees the ref parameter through the imported alias.
using adjust_callback = std::function<void(int&)>;

/// The ea::function (eastl::function) forms of the callback aliases.
using ea_progress_callback = ea::function<void(int)>;
// The extra parameter keeps the target shape different from adjust_callback. The generator
// names one shared delegate for each target shape.
using ea_adjust_callback = ea::function<void(int&, int)>;
using ea_transform_callback = ea::function<int(int)>;

inline int library_ea_adjust(ea_adjust_callback callback, int value)
{
    callback(value, 1);
    return value;
}

/// A second function with the alias. It uses the same delegate type as library_ea_adjust.
inline int library_ea_adjust_twice(const ea_adjust_callback& callback, int value)
{
    callback(value, 1);
    callback(value, 1);
    return value;
}

inline int library_ea_transform(const ea_transform_callback& callback, int value)
{
    return callback(value);
}

inline int library_adjust(adjust_callback callback, int value)
{
    callback(value);
    return value;
}

/// A second function with the alias. It uses the same delegate type as library_adjust.
inline int library_adjust_twice(const adjust_callback& callback, int value)
{
    callback(value);
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

    void set_ea_progress(ea_progress_callback callback) { ea_progress_ = std::move(callback); }

    void report_ea(int value)
    {
        if (ea_progress_)
            ea_progress_(value);
    }

    std::string describe() const { return "widget"; }

private:
    progress_callback progress_;
    ea_progress_callback ea_progress_;
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

/// The ea::function form of the nested handler alias.
struct library_ea_handler
{
    using function = ea::function<void(score_map&)>;

    int id() const { return 2; }
};

struct library_ea_events
{
    template <class T>
    [[sharpcpp::instantiate("cross_library::library_ea_handler::function")]]
    int subscribe(T handler)
    {
        score_map scores{{1, 2.5f}};
        handler(scores);
        return static_cast<int>(scores.size());
    }
};

} // namespace cross_library
