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
