#pragma once

#include <memory>
#include <string>

namespace sample
{
inline namespace abi
{
class InlineValue {};
}

template <typename T>
using allocator = std::allocator<T>;

using string = std::basic_string<char, std::char_traits<char>, allocator<char>>;

class Result
{
public:
    const string& GetError() const;
};

class Loader
{
public:
    bool Load(string* error);
    bool LoadStd(std::string* error);
};
}
