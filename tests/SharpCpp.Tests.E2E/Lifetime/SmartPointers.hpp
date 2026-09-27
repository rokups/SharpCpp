#pragma once

#include <utility>

namespace smart_pointers
{
/// An intrusive reference-counted base in the style of Urho3D::RefCounted.
class ref_counted
{
public:
    virtual ~ref_counted()
    {
        ++destroyed_;
    }

    int AddRef()
    {
        return ++refs_;
    }

    int ReleaseRef()
    {
        const int refs = --refs_;
        if (refs <= 0)
            delete this;
        return refs;
    }

    int Refs() const
    {
        return refs_;
    }

    static int Destroyed()
    {
        return destroyed_;
    }

private:
    int refs_{};
    inline static int destroyed_{};
};

template <class T, class TRefCounted = T>
class shared_ptr
{
public:
    shared_ptr() = default;

    explicit shared_ptr(T* value)
        : value_(value)
    {
        if (value_ != nullptr)
            value_->AddRef();
    }

    shared_ptr(const shared_ptr& other)
        : shared_ptr(other.value_)
    {
    }

    shared_ptr& operator=(const shared_ptr& other)
    {
        shared_ptr copy(other);
        std::swap(value_, copy.value_);
        return *this;
    }

    ~shared_ptr()
    {
        if (value_ != nullptr)
            value_->ReleaseRef();
    }

    T* Get() const
    {
        return value_;
    }

private:
    T* value_{};
};

template <class T, class TRefCounted = T>
class weak_ptr
{
public:
    explicit weak_ptr(T* value)
        : value_(value)
    {
    }

    T* Get() const
    {
        return value_;
    }

private:
    T* value_{};
};

template <class T>
struct releaser
{
    void operator()(T* value) const
    {
        delete value;
    }
};

template <class T, class TReleaser>
class unique_ptr
{
public:
    explicit unique_ptr(T* value)
        : value_(value)
    {
    }

    unique_ptr(unique_ptr&& other) noexcept
        : value_(other.Detach())
    {
    }

    ~unique_ptr()
    {
        if (value_ != nullptr)
            TReleaser{}(value_);
    }

    T* Detach()
    {
        T* value = value_;
        value_ = nullptr;
        return value;
    }

private:
    T* value_{};
};

class payload : public ref_counted
{
public:
    explicit payload(int value)
        : value_(value)
    {
    }

    int value() const
    {
        return value_;
    }

private:
    int value_{};
};

inline payload* shared_payload_storage()
{
    static payload* value = []
    {
        auto* created = new payload(11);
        created->AddRef();
        return created;
    }();
    return value;
}

/// Returned smart pointers hand the managed wrapper a reference of its own.
inline shared_ptr<payload> get_payload_shared()
{
    return shared_ptr<payload>(shared_payload_storage());
}

inline weak_ptr<payload> get_payload_weak()
{
    return weak_ptr<payload>(shared_payload_storage());
}

using payload_unique = unique_ptr<payload, releaser<payload>>;

inline payload_unique get_payload_unique(int value)
{
    return payload_unique(new payload(value));
}

inline int use_payload_shared(const shared_ptr<payload>& value)
{
    return value.Get()->value() * 10 + value.Get()->Refs();
}

inline int payload_refs()
{
    return shared_payload_storage()->Refs();
}

inline void release_ref_counted(void* value)
{
    static_cast<ref_counted*>(value)->ReleaseRef();
}
}
