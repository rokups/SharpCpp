#pragma once

// Library and App are generated as two modules; App imports Library's metadata the way
// an application binds against an engine. Neither is post-processed by the driver.

#include <functional>
#include <string>
#include <unordered_map>
#include <vector>

namespace cross_library
{
using progress_callback = std::function<void(int)>;

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

struct library_widget
{
    virtual ~library_widget() = default;

    void set_progress(progress_callback callback) { progress_ = std::move(callback); }

    void report(int value)
    {
        if (progress_)
            progress_(value);
    }

    std::string describe() const { return "widget"; }

private:
    progress_callback progress_;
};
} // namespace cross_library
