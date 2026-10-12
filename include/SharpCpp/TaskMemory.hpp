#pragma once

#include <cstddef>
#include <cstdlib>

namespace SharpCpp
{
#if defined(_WIN32)
// The managed runtime and a native module can link different CRT heaps on Windows, so memory that
// crosses the border is allocated from the COM task allocator: the process-wide heap that
// Marshal.AllocCoTaskMem and Marshal.FreeCoTaskMem use. It is declared here, not included, to keep
// the Windows headers out of the generated code.
#if defined(_WIN64)
using TaskMemorySize = unsigned long long;
#else
using TaskMemorySize = unsigned long;
#endif

extern "C" __declspec(dllimport) void* __stdcall CoTaskMemAlloc(TaskMemorySize size);
extern "C" __declspec(dllimport) void __stdcall CoTaskMemFree(void* memory);
#if defined(_MSC_VER)
#pragma comment(lib, "ole32.lib")
#endif
#endif

/// Allocates memory that the managed side releases with Marshal.FreeCoTaskMem, or the other way around.
inline void* AllocateTaskMemory(std::size_t size) noexcept
{
#if defined(_WIN32)
    return CoTaskMemAlloc(static_cast<TaskMemorySize>(size));
#else
    return std::malloc(size);
#endif
}

/// Releases memory from AllocateTaskMemory or from Marshal.AllocCoTaskMem.
inline void FreeTaskMemory(void* memory) noexcept
{
#if defined(_WIN32)
    CoTaskMemFree(memory);
#else
    std::free(memory);
#endif
}
}
