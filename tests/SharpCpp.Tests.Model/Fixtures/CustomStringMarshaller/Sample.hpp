#pragma once

class ea_compat_string
{
public:
    ea_compat_string() = default;

    unsigned long long size() const
    {
        return 0;
    }

    void push_back(char value)
    {
    }

    void clear()
    {
    }
};

int measure_ea_string(const ea_compat_string& value);
