#pragma once

#if defined(_WIN32)
#define SHARPCPP_EXPORT extern "C" __declspec(dllexport)
#define SHARPCPP_EXPORT_NAMED(name) SHARPCPP_EXPORT
#elif defined(__EMSCRIPTEN__)
#define SHARPCPP_EXPORT extern "C" __attribute__((visibility("default")))
#define SHARPCPP_EXPORT_NAMED(name) __attribute__((visibility("default"), export_name(#name)))
#else
#define SHARPCPP_EXPORT extern "C" __attribute__((visibility("default")))
#define SHARPCPP_EXPORT_NAMED(name) SHARPCPP_EXPORT
#endif
