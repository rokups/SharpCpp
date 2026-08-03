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

static int coverage_c_handle_storage;
static int coverage_c_context_storage;
static coverage_c_event coverage_c_event_storage;

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
