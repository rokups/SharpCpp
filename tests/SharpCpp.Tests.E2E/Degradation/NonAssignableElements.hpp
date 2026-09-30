#pragma once

#include <atomic>
#include <cstddef>
#include <memory>
#include <vector>

namespace non_assignable_elements
{
/// User-declared copy constructor (so copy-constructible), but the atomic member implicitly
/// deletes the copy assignment (as JPH::PhysicsUpdateContext::Step).
struct step
{
    step() = default;

    step(const step& other)
        : value(other.value.load())
    {
    }

    std::atomic<int> value{};
};

/// Element that is copy-constructible and copy-assignable.
struct plain_item
{
    int value{};
};

/// Arena the allocator is constructed from (as JPH::TempAllocator).
struct arena
{
    int id{};
};

/// Allocator without a default constructor (as JPH::STLTempAllocator).
template <class T>
class arena_allocator
{
public:
    using value_type = T;

    explicit arena_allocator(arena& source)
        : arena_(&source)
    {
    }

    template <class U>
    arena_allocator(const arena_allocator<U>& other) noexcept
        : arena_(other.source())
    {
    }

    T* allocate(std::size_t count)
    {
        return std::allocator<T>().allocate(count);
    }

    void deallocate(T* pointer, std::size_t count)
    {
        std::allocator<T>().deallocate(pointer, count);
    }

    arena* source() const noexcept
    {
        return arena_;
    }

    template <class U>
    bool operator==(const arena_allocator<U>& other) const noexcept
    {
        return arena_ == other.source();
    }

    template <class U>
    bool operator!=(const arena_allocator<U>& other) const noexcept
    {
        return arena_ != other.source();
    }

private:
    arena* arena_;
};

using step_list = std::vector<step, arena_allocator<step>>;
using arena_numbers = std::vector<int, arena_allocator<int>>;

/// Neither container can be bound: the elements are not assignable, and the allocator
/// cannot be default-constructed. The rest of the record is bound.
class step_context
{
public:
    step_list steps;
    arena_numbers arena_values;
    std::vector<step> plain_steps;
    std::vector<plain_item> items;
    std::vector<int> numbers;
    int total{};

    step_context()
        : steps(arena_allocator<step>(shared_arena())),
          arena_values(arena_allocator<int>(shared_arena()))
    {
    }

    int doubled_total() const
    {
        return total * 2;
    }

    const std::vector<step>& get_plain_steps() const
    {
        return plain_steps;
    }

    void set_plain_steps(const std::vector<step>& value)
    {
        plain_steps.clear();
        for (const auto& element : value)
        {
            plain_steps.emplace_back(element);
        }
    }

    step_list make_steps() const
    {
        return step_list(arena_allocator<step>(shared_arena()));
    }

    void take_steps(const step_list& value)
    {
        total = static_cast<int>(value.size());
    }

    arena_numbers make_arena_numbers() const
    {
        return arena_numbers(arena_allocator<int>(shared_arena()));
    }

    void take_arena_numbers(const arena_numbers& value)
    {
        total = static_cast<int>(value.size());
    }

    int count_items() const
    {
        return static_cast<int>(items.size());
    }

private:
    static arena& shared_arena()
    {
        static arena value;
        return value;
    }
};

inline int count_steps(const std::vector<step>& values)
{
    return static_cast<int>(values.size());
}

inline int count_items(const std::vector<plain_item>& values)
{
    return static_cast<int>(values.size());
}
}
