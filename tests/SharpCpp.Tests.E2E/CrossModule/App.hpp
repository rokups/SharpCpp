#pragma once

#include "Library.hpp"

namespace cross_app
{
struct app_widget : cross_library::library_widget
{
    int app_value() const { return 9; }
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

inline void app_report(cross_library::library_widget* widget, cross_library::progress_callback callback, int value)
{
    widget->set_progress(std::move(callback));
    widget->report(value);
}
} // namespace cross_app
