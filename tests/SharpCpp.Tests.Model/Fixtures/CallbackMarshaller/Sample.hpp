#pragma once

#include "SampleMarshallers.h"

namespace Test
{
void ForEach(std::function<void(int, float)> callback);
int Transform(std::function<int(int)> callback);
void SetCallback(std::function<void()> callback);
void SetDefaultCallback(std::function<unsigned()> callback = nullptr);
}
