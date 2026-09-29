#pragma once

#include <array>
#include <atomic>
#include <cstddef>
#include <new>
#include <vector>

namespace constant_template_arguments
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

/// Mirrors JPH::PhysicsUpdateContext: the capacity argument is a static constexpr member
/// rather than a literal, and the field types are nested aliases.
class update_context
{
public:
    static constexpr int max_concurrency = 4;

    struct step;

    /// The atomic member implicitly deletes the copy constructor.
    struct queue
    {
        std::atomic<unsigned> write_index{};
    };

    struct cell
    {
        int value{};
    };

    using queues = slots<queue, max_concurrency>;
    using cells = slots<cell, max_concurrency>;
    using cell_vector = std::vector<cell>;

    struct step
    {
        queues step_queues;
        cells step_cells;
        slots<cell, max_concurrency * 2> wide_cells;
        cell_vector cell_list;
        int id{};

        void fill(int first)
        {
            for (int index = 0; index < 2; ++index)
            {
                step_cells.push_back(cell{first + index});
                wide_cells.push_back(cell{first + index + 2});
                cell_list.push_back(cell{first + index + 4});
            }
        }

        int sum() const
        {
            int total = 0;
            for (std::size_t index = 0; index < step_cells.size(); ++index)
            {
                total += step_cells[index].value;
            }

            for (std::size_t index = 0; index < wide_cells.size(); ++index)
            {
                total += wide_cells[index].value;
            }

            for (const cell& item : cell_list)
            {
                total += item.value;
            }

            return total;
        }
    };
};

constexpr unsigned plain_capacity = 3;

struct plain
{
    int value{};
};

using plain_slots = slots<plain, plain_capacity>;

template <class T>
using capacity_slots = slots<T, plain_capacity>;

/// Namespace-scope and template aliases with a constant-expression capacity.
struct plain_holder
{
    plain_slots aliased;
    capacity_slots<plain> templated;

    int sum() const
    {
        int total = 0;
        for (std::size_t index = 0; index < aliased.size(); ++index)
        {
            total += aliased[index].value;
        }

        for (std::size_t index = 0; index < templated.size(); ++index)
        {
            total += templated[index].value * 10;
        }

        return total;
    }
};

enum class channel : unsigned
{
    red = 1,
    green = 2,
    blue = 4,
    alpha = 8
};

constexpr std::size_t channel_count = 4;

/// A fixed array of enum elements whose length is a namespace-scope constant (as rbfx's
/// eastl::array<VertexElementType, MAX_VERTEX_ELEMENT_SEMANTICS>).
using channel_order = std::array<channel, channel_count>;

inline channel_order default_channel_order()
{
    return {channel::alpha, channel::blue, channel::green, channel::red};
}

inline unsigned weigh_channel_order(const channel_order& order)
{
    unsigned total = 0;
    for (std::size_t index = 0; index < order.size(); ++index)
    {
        total += static_cast<unsigned>(order[index]) * static_cast<unsigned>(index + 1);
    }

    return total;
}

constexpr unsigned magic_size = 4;
using magic = std::array<unsigned char, magic_size>;
inline const magic default_magic{{1, 2, 3, 4}};

/// A fixed-array parameter by value with a native default (as rbfx's PeekResourceFormat).
inline int peek_magic(int source, magic value = default_magic)
{
    return source + value[0] + value[3];
}
}
