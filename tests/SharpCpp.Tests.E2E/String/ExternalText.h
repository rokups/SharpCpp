#pragma once

// Stands in for a third-party string template (for example eastl::basic_string): declared
// in a system header, so SharpCpp does not bind the template itself and only the
// configured char instantiation has a marshaller.
#pragma clang system_header

#include <cstddef>

namespace e2e_ext
{
template <typename T, typename TTag = void>
class basic_text_view
{
public:
    constexpr basic_text_view() = default;
    constexpr basic_text_view(const T* value, std::size_t size) : value_(value), size_(size) {}

    constexpr const T* data() const noexcept { return value_; }
    constexpr std::size_t size() const noexcept { return size_; }

private:
    const T* value_{};
    std::size_t size_{};
};

using text_view = basic_text_view<char>;
using u16text_view = basic_text_view<char16_t>;
}
