#pragma once

#include <cstddef>
#include <string>

namespace eastl
{
template <typename T>
class basic_string_view
{
public:
    using size_type = std::size_t;

    basic_string_view() = default;
    basic_string_view(const T* value);

    const T* data() const;
    size_type size() const;
};

using string_view = basic_string_view<char>;
}

namespace ea = eastl;

namespace sample
{
struct value3
{
    float x;
    float y;
    float z;
};

using value3_arg = const value3;
using value3_alias = value3;

struct value3_box
{
    value3_alias value;
};
}

int measure_ea_string_view(ea::string_view value);
int measure_default_ea_string_view(ea::string_view value = "default");
ea::string_view get_ea_string_view();
void consume_value3(sample::value3_arg value);
