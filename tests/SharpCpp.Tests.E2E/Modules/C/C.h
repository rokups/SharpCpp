#pragma once

#include <stdint.h>

typedef struct coverage_c_handle coverage_c_handle;
typedef void* coverage_c_context;

typedef struct coverage_c_pair
{
    int left;
    int right;
} coverage_c_pair;

typedef union coverage_c_event
{
    uint32_t type;
    coverage_c_pair point;
} coverage_c_event;

typedef struct coverage_c_arguments
{
    struct coverage_c_ray* ray;
} coverage_c_arguments;

typedef struct coverage_c_ray
{
    int value;
} coverage_c_ray;

static int coverage_c_handle_storage;
static int coverage_c_context_storage;
static coverage_c_event coverage_c_event_storage;
static coverage_c_ray coverage_c_ray_storage;
static coverage_c_arguments coverage_c_arguments_storage;

static inline coverage_c_handle* coverage_c_get_handle(void)
{
    return (coverage_c_handle*)&coverage_c_handle_storage;
}

static inline int coverage_c_use_handle(coverage_c_handle* value)
{
    return value == coverage_c_get_handle() ? 41 : -1;
}

static inline coverage_c_context coverage_c_get_context(void)
{
    return &coverage_c_context_storage;
}

static inline int coverage_c_use_context(coverage_c_context value)
{
    return value == coverage_c_get_context() ? 42 : -1;
}

static inline const coverage_c_event* coverage_c_make_event(int left, int right)
{
    coverage_c_event_storage.point.left = left;
    coverage_c_event_storage.point.right = right;
    return &coverage_c_event_storage;
}

static inline int coverage_c_sum_event(const coverage_c_event* value)
{
    return value == 0 ? -1 : value->point.left + value->point.right;
}

static inline coverage_c_arguments* coverage_c_make_arguments(int value)
{
    coverage_c_ray_storage.value = value;
    coverage_c_arguments_storage.ray = &coverage_c_ray_storage;
    return &coverage_c_arguments_storage;
}

static inline int coverage_c_read_arguments(const coverage_c_arguments* value)
{
    return value == 0 || value->ray == 0 ? -1 : value->ray->value;
}

/* Anonymous records nested in named storage flatten into native access paths. */
typedef struct c_window_info
{
    int version;
    union
    {
        struct
        {
            void* display;
            unsigned long window;
        } x11;
    } info;
} c_window_info;

static c_window_info c_window_info_storage;

static inline c_window_info* c_get_window_info(void)
{
    return &c_window_info_storage;
}

static inline unsigned long c_read_window(const c_window_info* value)
{
    return value->info.x11.window + (value->info.x11.display == 0 ? 0u : 1000u);
}

/* Typedefs of anonymous structs flatten; typedefs of anonymous unions stay values. */
typedef struct
{
    int enabled;
} c_features;

typedef struct
{
    c_features capabilities;
} c_settings;

typedef union
{
    int integer;
    float scalar;
} c_value;

typedef struct
{
    c_value data;
} c_value_settings;

static c_settings c_settings_storage;
static c_value_settings c_value_settings_storage;

static inline c_settings* c_get_settings(void)
{
    return &c_settings_storage;
}

static inline int c_read_settings(const c_settings* value)
{
    return value->capabilities.enabled;
}

static inline c_value_settings* c_get_value_settings(void)
{
    c_value_settings_storage.data.integer = 29;
    return &c_value_settings_storage;
}

/* Anonymous enums are skipped; named ones are bound. */
enum
{
    c_first_anonymous,
    c_second_anonymous
};

enum c_named_value
{
    c_third_value = 3
};

static inline enum c_named_value c_get_named_value(void)
{
    return c_third_value;
}

/* Pointer returns and parameters in C. */
static const unsigned int c_words_storage[3] = {5, 6, 7};

static inline const char* c_get_message(void)
{
    return "message";
}

static inline const unsigned int* c_get_words(void)
{
    return c_words_storage;
}

static inline int c_compile(const char** source)
{
    int length = 0;
    while ((*source)[length] != '\0')
        ++length;
    return length;
}

/* Adjacent pointer/count parameters are input arrays; a writable pointer is writable. */
typedef unsigned long c_size_t;

static inline unsigned int c_optimize(const unsigned int* words, c_size_t word_count)
{
    unsigned int result = 0;
    for (c_size_t i = 0; i < word_count; ++i)
        result += words[i];
    return result;
}

static inline int c_register_flags(const char** flags, c_size_t flag_count)
{
    int result = 0;
    for (c_size_t i = 0; i < flag_count; ++i)
        result += flags[i][0];
    return result;
}

static inline void c_read_values(unsigned int* values, c_size_t value_count)
{
    for (c_size_t i = 0; i < value_count; ++i)
        values[i] = (unsigned int)(i * 2);
}

/* A pointer typedef to an opaque struct is a handle type (Vulkan style). */
typedef struct c_native_handle_T* c_native_handle;

static int c_native_handle_storage;

static inline c_native_handle c_create_native_handle(void)
{
    return (c_native_handle)&c_native_handle_storage;
}

static inline int c_use_native_handle(c_native_handle handle)
{
    return handle == c_create_native_handle() ? 8 : -1;
}
