#pragma once

template <class T>
struct Marshaller;

template <>
struct [[sharpcpp::managed_type(public_type = "global::Test.Managed", undefaultable = true)]] Marshaller<int> {};
