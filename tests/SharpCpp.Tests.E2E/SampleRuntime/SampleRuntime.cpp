#include "SampleRuntime.hpp"

struct SampleContext
{
    int Value;
};

int add_numbers(int lhs, int rhs)
{
    return lhs + rhs;
}

SampleContext* create_context()
{
    static SampleContext context { 42 };
    return &context;
}
