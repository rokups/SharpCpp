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
