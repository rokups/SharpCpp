#pragma once

#include "Library.hpp"

namespace cross_app
{
struct app_widget : cross_library::library_widget
{
    int app_value() const { return 9; }

    // A derived wrapper takes the static interfaces of its bases only when it has GetTypeStatic.
    static int GetTypeStatic() { return 11; }
};

inline std::string app_describe(const cross_library::library_settings& settings)
{
    return settings.name + "!";
}

inline int app_count(const std::vector<int>& values)
{
    return static_cast<int>(values.size());
}

inline int app_lookup(const std::unordered_map<int, int>& counters, int key)
{
    auto found = counters.find(key);
    return found == counters.end() ? -1 : found->second;
}

// The same C++ type as cross_library::score_map, spelled with an explicit hasher.
inline int app_score_count(const std::unordered_map<int, float, std::hash<int>>& scores)
{
    return static_cast<int>(scores.size());
}

/// An imported callback alias with a ref parameter writes back to the native value.
inline int app_adjust(cross_library::adjust_callback callback, int value)
{
    callback(value);
    return value;
}

/// An imported ea::function alias with a ref parameter writes back to the native value.
inline int app_ea_adjust(cross_library::ea_adjust_callback callback, int value)
{
    callback(value, 1);
    return value;
}

inline int app_ea_transform(const cross_library::ea_transform_callback& callback, int value)
{
    return callback(value) + 1;
}

inline void app_ea_report(cross_library::library_widget* widget, cross_library::ea_progress_callback callback, int value)
{
    widget->set_ea_progress(std::move(callback));
    widget->report_ea(value);
}

inline void app_report(cross_library::library_widget* widget, cross_library::progress_callback callback, int value)
{
    widget->set_progress(std::move(callback));
    widget->report(value);
}

// Imported records returned by pointer and by reference. The library module keeps its handle
// marshallers internal, so the app module emits its own.
inline cross_library::library_widget* app_same_widget(cross_library::library_widget* widget)
{
    return widget;
}

inline const cross_library::library_settings& app_same_settings(const cross_library::library_settings& settings)
{
    return settings;
}
} // namespace cross_app
