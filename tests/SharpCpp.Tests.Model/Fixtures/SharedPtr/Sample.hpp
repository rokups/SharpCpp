#pragma once

namespace Urho3D
{
class RefCounted
{
public:
    virtual ~RefCounted() = default;
    int AddRef();
    int ReleaseRef();
};

template <class T, class TRefCounted = T>
class SharedPtr
{
public:
    T* Get() const;
};

template <class T, class TRefCounted = T>
class WeakPtr
{
public:
    T* Get() const;
};

template <class T>
struct Releaser
{
};

template <class T, class TReleaser>
class UniquePtr
{
public:
    T* Get() const;
};
}

class sample_payload : public Urho3D::RefCounted
{
public:
    int get_value() const;
};

Urho3D::SharedPtr<sample_payload> get_payload_shared();
Urho3D::WeakPtr<sample_payload> get_payload_weak();
using sample_payload_ptr = Urho3D::UniquePtr<sample_payload, Urho3D::Releaser<sample_payload>>;
sample_payload_ptr get_payload_unique();
void use_payload_shared(const Urho3D::SharedPtr<sample_payload>& value);
