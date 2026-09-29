#pragma once

#include <atomic>
#include <cstddef>
#include <new>
#include <vector>

namespace non_copyable_elements
{
/// Fixed-capacity sequence whose copy constructor copies its elements (as JPH::StaticArray).
template <class T, unsigned N>
class slots
{
public:
    using size_type = std::size_t;

    slots() = default;

    slots(const slots& other)
    {
        for (size_type index = 0; index < other.size_; ++index)
        {
            ::new (&storage_[index]) T(other[index]);
        }

        size_ = other.size_;
    }

    ~slots()
    {
        clear();
    }

    size_type size() const
    {
        return size_;
    }

    void clear()
    {
        for (size_type index = 0; index < size_; ++index)
        {
            (*this)[index].~T();
        }

        size_ = 0;
    }

    void push_back(const T& value)
    {
        ::new (&storage_[size_++]) T(value);
    }

    T& back()
    {
        return (*this)[size_ - 1];
    }

    T* begin()
    {
        return &(*this)[0];
    }

    void erase(T* position)
    {
        for (T* current = position; current + 1 < begin() + size_; ++current)
        {
            *current = *(current + 1);
        }

        back().~T();
        --size_;
    }

    T& operator[](size_type index)
    {
        return *std::launder(reinterpret_cast<T*>(&storage_[index]));
    }

    const T& operator[](size_type index) const
    {
        return *std::launder(reinterpret_cast<const T*>(&storage_[index]));
    }

private:
    struct alignas(T) element_storage
    {
        unsigned char bytes[sizeof(T)];
    };

    size_type size_{};
    element_storage storage_[N];
};

/// The atomic member implicitly deletes the copy constructor.
struct atomic_counter
{
    std::atomic<int> count{};
};

/// Container fields of non-copyable elements are not bound; the rest of the record is.
struct counter_owner
{
    std::vector<atomic_counter> counters;
    slots<atomic_counter, 4> fixed_counters;
    slots<int, 4> numbers;
    int total{};

    int doubled_total() const
    {
        return total * 2;
    }
};

/// Embedding the owner by value makes this record non-copyable as well.
struct counter_holder
{
    counter_owner owner;
    int id{};
};

/// Containers of the owner copy its elements, so this declaration is not bound.
inline int count_counter_owners(const std::vector<counter_owner>& owners)
{
    return static_cast<int>(owners.size());
}
}
