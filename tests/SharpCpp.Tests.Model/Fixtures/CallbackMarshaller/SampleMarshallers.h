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
