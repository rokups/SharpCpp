#pragma once

#include <cstdlib>
#include <cstring>
#include <string>

// Caller-owned strings. [[sharpcpp::freed_by(release)]] on an output `char**` parameter or on a
// `char*`/`const char*` return makes the wrapper copy the text into a managed string and then
// call the named free function. The live counter proves the free ran (and was skipped for null).

inline int& owned_text_counter()
{
    static int live = 0;
    return live;
}

inline int owned_text_live_allocations()
{
    return owned_text_counter();
}

inline char* allocate_owned_text(const std::string& text)
{
    auto* result = static_cast<char*>(std::malloc(text.size() + 1));
    std::memcpy(result, text.c_str(), text.size() + 1);
    ++owned_text_counter();
    return result;
}

inline void release_owned_text(char* text)
{
    if (text == nullptr)
        return;
    std::free(text);
    --owned_text_counter();
}

inline void release_owned_memory(void* memory)
{
    if (memory == nullptr)
        return;
    std::free(memory);
    --owned_text_counter();
}

[[sharpcpp::freed_by(release_owned_text)]] inline char* make_owned_text(const char* prefix)
{
    return allocate_owned_text(std::string(prefix) + "-owned");
}

[[sharpcpp::freed_by(release_owned_memory)]] inline const char* make_owned_const_text()
{
    return allocate_owned_text("owned const text");
}

[[sharpcpp::freed_by(release_owned_text)]] inline char* make_null_owned_text()
{
    return nullptr;
}

inline void get_owned_label([[sharpcpp::freed_by(release_owned_text)]] char** value)
{
    *value = allocate_owned_text("owned label");
}

inline void get_owned_null_label([[sharpcpp::freed_by(release_owned_text)]] char** value)
{
    *value = nullptr;
}

// Both forms in one call: the label is freed in the post-call, the return after it.
[[sharpcpp::freed_by(release_owned_text)]] inline char* make_owned_text_with_label([[sharpcpp::freed_by(release_owned_text)]] char** label)
{
    *label = allocate_owned_text("paired label");
    return allocate_owned_text("paired text");
}

// Native writes *value only on success; the wrapper must not free or copy an unwritten pointer.
inline int get_owned_label_unwritten([[sharpcpp::freed_by(release_owned_text)]] char** value)
{
    (void)value;
    return -1;
}

// The attribute scan must not bleed into neighbours: an untagged char* function right after a
// tagged prototype (`;` boundary) and one right after a body (`}` boundary).
[[sharpcpp::freed_by(release_owned_text)]] inline char* make_owned_bleed_tagged();
inline char* make_bleed_untagged_after_prototype()
{
    return nullptr;
}

[[sharpcpp::freed_by(release_owned_text)]] inline char* make_owned_bleed_tagged()
{
    return allocate_owned_text("bleed");
}
inline char* make_bleed_untagged_after_body()
{
    return nullptr;
}

// Untagged here: the E2E generator tags these through consumer metadata.
inline char* make_consumer_owned_text()
{
    return allocate_owned_text("consumer text");
}

inline void get_consumer_owned_label(char** value)
{
    *value = allocate_owned_text("consumer label");
}

// Degraded cases: each is dropped with a logged reason.
[[sharpcpp::freed_by(no_such_release)]] inline char* make_unfreed_text()
{
    return nullptr;
}

inline void release_two_args(char* text, int count)
{
    (void)text;
    (void)count;
}

[[sharpcpp::freed_by(release_two_args)]] inline char* make_two_arg_freed_text()
{
    return nullptr;
}

[[sharpcpp::freed_by(release_owned_text)]] inline int make_non_pointer_owned()
{
    return 0;
}

inline int measure_in_freed([[sharpcpp::in]] [[sharpcpp::freed_by(release_owned_text)]] char** names)
{
    return names == nullptr ? 0 : 1;
}

inline int sum_counted_freed([[sharpcpp::counted_by(count)]] [[sharpcpp::freed_by(release_owned_text)]] char** names, unsigned int count)
{
    return names == nullptr ? 0 : static_cast<int>(count);
}

[[sharpcpp::freed_by()]] inline char* make_empty_freed_by()
{
    return nullptr;
}

inline void release_overloaded(char* text)
{
    (void)text;
}

inline void release_overloaded(void* memory)
{
    (void)memory;
}

[[sharpcpp::freed_by(release_overloaded)]] inline char* make_overloaded_freed()
{
    return nullptr;
}

[[sharpcpp::freed_by(release_owned_text)]] inline char* make_defaulted_owned_text(int length = 3)
{
    return allocate_owned_text(std::string(static_cast<std::size_t>(length), 'x'));
}

struct owned_text_holder
{
    [[sharpcpp::freed_by(release_owned_text)]] static char* make_static()
    {
        return allocate_owned_text("static");
    }
};
