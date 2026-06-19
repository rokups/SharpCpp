#pragma once

namespace std
{
template <class TSignature>
class function
{
public:
    function(decltype(nullptr) = nullptr);
};
}

template <class T>
struct Marshaller;

template <class TSignature>
struct [[sharpcpp::managed_type(callback = true, managed_marshaller = "global::Test.CallbackMarshaller")]] Marshaller<std::function<TSignature>> {};

namespace Test
{
struct Vector2;
}

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Test.Vector2",
    marshaller_type = "global::Test.Vector2")]] Marshaller<Test::Vector2>
{
    using cpp_type = Test::Vector2;
    using abi_type = Test::Vector2;
};
