#pragma once

namespace SharpCpp
{
template <class T>
struct Marshaller;

template <class T>
using ParameterMarshaller = Marshaller<T>;

template <class T>
using ReturnMarshaller = Marshaller<T>;
}
