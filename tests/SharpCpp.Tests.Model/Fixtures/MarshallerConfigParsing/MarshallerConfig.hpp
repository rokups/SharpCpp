#pragma once

template <class T>
struct Marshaller;

template <class T>
struct ReturnMarshaller;

template <>
struct [[sharpcpp::managed_type(public_type = "global::Test.Managed", undefaultable = true)]] Marshaller<int> {};

template <>
struct [[sharpcpp::managed_type(return_ownership = "owning")]] ReturnMarshaller<float> {};
