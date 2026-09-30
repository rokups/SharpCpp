#pragma once

#include <cstring>

// Pointer-to-pointer parameters are ambiguous: `char**` is an array of strings or an output
// string, `T**` an array of pointers or an output pointer. The defaults are chosen from an
// adjacent value count; [[sharpcpp::in]]/[[sharpcpp::out]] or a consumer-set
// PointerPointerShape metadata value select the other shape.

struct pointer_pointer_item
{
    explicit pointer_pointer_item(int value)
        : value_(value)
    {
    }

    int get_value() const
    {
        return value_;
    }

private:
    int value_{};
};

inline int measure_strings(const char* const* names, unsigned int count)
{
    int result = 0;
    for (unsigned int i = 0; i < count; ++i)
        result += static_cast<int>(std::strlen(names[i]));
    return result;
}

// argv style: an adjacent value count makes a mutable char** an input string array.
inline int measure_argv(int argc, char** argv)
{
    return measure_strings(argv, static_cast<unsigned int>(argc));
}

inline int measure_mutable_names(char** names, unsigned int name_count)
{
    return measure_strings(names, name_count);
}

inline int measure_const_names(const char** names, unsigned int name_count)
{
    return measure_strings(names, name_count);
}

inline int measure_char_const_names(char* const* names, unsigned int count)
{
    return measure_strings(names, count);
}

// Without a count the pointer receives one borrowed string, which is never freed.
inline void get_borrowed_label(char** value)
{
    static char label[] = "borrowed label";
    *value = label;
}

inline void get_borrowed_const_label(const char** value)
{
    *value = "borrowed const label";
}

inline void get_null_label(const char** value)
{
    *value = nullptr;
}

// A pointer count does not make the string pointer an input array.
inline void get_label_with_length(char** value, int* length)
{
    static char label[] = "counted label";
    *value = label;
    *length = static_cast<int>(sizeof(label) - 1);
}

// [[sharpcpp::in]] forces an input array even without a recognizable count.
inline int measure_forced_names(int total, [[sharpcpp::in]] char** names)
{
    return measure_strings(names, static_cast<unsigned int>(total));
}

// [[sharpcpp::out]] forces one output string although a count is adjacent.
inline void get_forced_label(int argc, [[sharpcpp::out]] char** value)
{
    static char label[] = "forced label";
    *value = argc >= 0 ? label : nullptr;
}

inline int sum_items(pointer_pointer_item** items, unsigned int item_count)
{
    int result = 0;
    for (unsigned int i = 0; i < item_count; ++i)
        result += items[i]->get_value();
    return result;
}

inline int sum_const_items(pointer_pointer_item* const* items, unsigned int item_count)
{
    return sum_items(const_cast<pointer_pointer_item**>(items), item_count);
}

// Without a count, or with a pointer count, a record pointer-to-pointer stays an output.
inline void pick_item(pointer_pointer_item** item)
{
    static pointer_pointer_item value{41};
    *item = &value;
}

inline void pick_item_with_pointer_count(pointer_pointer_item** items, unsigned int* item_count)
{
    static pointer_pointer_item value{43};
    *items = &value;
    *item_count = 1;
}

// [[sharpcpp::out]] forces an output although a value count is adjacent.
inline void pick_forced_item([[sharpcpp::out]] pointer_pointer_item** item, unsigned int slot_count)
{
    static pointer_pointer_item value{47};
    *item = slot_count == 0 ? nullptr : &value;
}

// [[sharpcpp::in]] forces an input array without a recognizable count.
inline int sum_forced_items(unsigned int total, [[sharpcpp::in]] pointer_pointer_item** items)
{
    return sum_items(items, total);
}

// Consumer-side selection: the E2E generator sets PointerPointerShape on these parameters.
inline int measure_configured_names(int total, char** names)
{
    return measure_strings(names, static_cast<unsigned int>(total));
}

inline void get_configured_label(int argc, char** value)
{
    static char label[] = "configured label";
    *value = argc >= 0 ? label : nullptr;
}

inline int sum_configured_items(unsigned int total, pointer_pointer_item** items)
{
    return sum_items(items, total);
}

inline void pick_configured_item(pointer_pointer_item** item, unsigned int slot_count)
{
    static pointer_pointer_item value{53};
    *item = slot_count == 0 ? nullptr : &value;
}

// A shape that cannot be honoured degrades the function with a diagnostic: a parameter that is
// not a pointer-to-pointer, an unknown shape value, and an output through a const pointer.
inline int measure_bad_shape(int total, int* names)
{
    return total + *names;
}

inline int measure_bad_value(int total, char** names)
{
    return measure_strings(names, static_cast<unsigned int>(total));
}

inline void pick_const_item(pointer_pointer_item* const* item)
{
    (void)item;
}

inline void get_const_label(char* const* value)
{
    (void)value;
}

// A size is not a recognized count for the output-to-array flip (only argc, Num* and *Count*
// are), so these stay outputs.
inline void pick_sized_item(pointer_pointer_item** item, unsigned long size)
{
    static pointer_pointer_item value{59};
    *item = size == 0 ? nullptr : &value;
}

inline void get_sized_label(char** value, unsigned long size)
{
    static char label[] = "sized label";
    *value = size == 0 ? nullptr : label;
}

// An explicit attribute that cannot be honoured degrades the function with a diagnostic.
inline void get_attributed_const_label([[sharpcpp::out]] char* const* value)
{
    (void)value;
}

// The E2E generator retypes the parameter to a raw pointer before requesting an array.
inline void pick_retyped_item(pointer_pointer_item** item)
{
    (void)item;
}

// The count must relate to the pointer-to-pointer for the output-to-array flip. A specific
// count that names another parameter, or a generic count claimed by the pointer before it,
// leaves the parameter an output.
inline int parse_words(const unsigned int* words, unsigned long word_count, pointer_pointer_item** parsed)
{
    static pointer_pointer_item value{61};
    *parsed = &value;
    return static_cast<int>(words[0] + word_count);
}

inline int parse_counted_data(const int* data, unsigned int count, pointer_pointer_item** result)
{
    static pointer_pointer_item value{67};
    *result = &value;
    return data[0] + static_cast<int>(count);
}

inline void allocate_vertices(unsigned int NumVertices, pointer_pointer_item** ppAllocation)
{
    static pointer_pointer_item value{71};
    *ppAllocation = NumVertices == 0 ? nullptr : &value;
}

inline void allocate_swapchain_data(int DeviceType, unsigned int ImageCount, pointer_pointer_item** ppSwapchainImageData)
{
    static pointer_pointer_item value{73};
    *ppSwapchainImageData = DeviceType + ImageCount == 0 ? nullptr : &value;
}

inline void pick_item_after_argc(int argc, pointer_pointer_item** item)
{
    static pointer_pointer_item value{79};
    *item = argc < 0 ? nullptr : &value;
}

// A specific count with the same stem, and a generic count after the array, pair with it.
inline int enqueue_task(pointer_pointer_item* task, pointer_pointer_item** ppPrerequisites, unsigned int NumPrerequisites)
{
    int result = task->get_value();
    for (unsigned int i = 0; i < NumPrerequisites; ++i)
        result += ppPrerequisites[i]->get_value();
    return result;
}

inline int test_drawables(pointer_pointer_item** drawables, unsigned int count)
{
    return sum_items(drawables, count);
}

// argc pairs with any char** right after it, not only with one named argv.
inline int measure_args(int argc, char** args)
{
    return measure_strings(args, static_cast<unsigned int>(argc));
}

// A record pointer before a generic count owns that count, so the pointer-to-pointer after it
// stays an output.
inline void pick_after_handle(pointer_pointer_item* owner, unsigned int count, pointer_pointer_item** item)
{
    static pointer_pointer_item value{83};
    *item = owner == nullptr || count == 0 ? nullptr : &value;
}

// count{X}, a generic count with nothing pointer-like before it, and a single `p` prefix pair
// with the array; a Count in the middle of the name has no stem and does not.
inline int sum_count_prefixed(unsigned int countItems, pointer_pointer_item** items)
{
    return sum_items(items, countItems);
}

inline void pick_limited(pointer_pointer_item** item, unsigned int itemCountLimit)
{
    static pointer_pointer_item value{89};
    *item = itemCountLimit == 0 ? nullptr : &value;
}

inline int sum_leading_count(unsigned int count, pointer_pointer_item** items)
{
    return sum_items(items, count);
}

inline int sum_hungarian_items(pointer_pointer_item** pItems, unsigned int NumItems)
{
    return sum_items(pItems, NumItems);
}
