#pragma once

template <class T>
struct Marshaller;

template <class T>
struct ReturnMarshaller;

namespace std
{
template <class TSignature>
class function;
}

template <>
struct [[sharpcpp::managed_type(public_type = "global::Test.Managed", undefaultable = true)]] Marshaller<int> {};

template <>
struct [[sharpcpp::managed_type(return_ownership = "owning")]] ReturnMarshaller<float> {};

template <class TSignature>
struct [[sharpcpp::managed_type(callback = true, managed_marshaller = "global::Test.CallbackMarshaller")]] Marshaller<std::function<TSignature>> {};
