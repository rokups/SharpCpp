#pragma once

#define E2E_UNSUPPORTED_DEFAULT_SINGLE 42.0f
#define E2E_UNSUPPORTED_DEFAULT_A 10.0f
#define E2E_UNSUPPORTED_DEFAULT_B 20.0f

/// Returns 0 when x is less than the threshold (defaults to 42.0f).
inline int check_threshold_single_default(float x, float threshold = E2E_UNSUPPORTED_DEFAULT_SINGLE)
{
    return x < threshold ? 0 : 1;
}

/// Returns 0 when a < b (defaults a=10.0f, b=20.0f).
inline int check_two_defaults_cascade(float x, float a = E2E_UNSUPPORTED_DEFAULT_A, float b = E2E_UNSUPPORTED_DEFAULT_B)
{
    (void)x;
    return a < b ? 0 : 1;
}

/// Plain function with no unexpressible defaults (regression guard).
inline int check_plain_no_defaults(int value)
{
    return value * 2;
}
