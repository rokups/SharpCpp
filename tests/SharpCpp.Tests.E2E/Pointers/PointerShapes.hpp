#pragma once

namespace pointer_shapes
{
struct payload
{
    int value{};
};

using payload_pointer = payload*;

inline payload get_payload()
{
    return payload{1};
}

inline payload* get_payload_pointer()
{
    static payload value{2};
    return &value;
}

inline payload** get_payload_pointer_pointer()
{
    static payload* value = get_payload_pointer();
    return &value;
}

inline payload_pointer get_payload_alias_pointer()
{
    return get_payload_pointer();
}

inline int read_payload_pointer_pointer(payload** value)
{
    *value = get_payload_pointer();
    return (*value)->value;
}

struct buffer
{
    int id{};
};

using buffer_alias = buffer;

/// Records and primitive values passed as parallel input arrays; a lone pointer is not an array.
struct device_context
{
    virtual ~device_context() = default;

    virtual unsigned long long set_buffers(
        unsigned int start_slot,
        unsigned int num_buffers,
        buffer_alias* const* buffers,
        const unsigned long long* offsets) = 0;

    virtual unsigned long long set_value(const unsigned long long* value) = 0;
};

struct device_context_impl final : device_context
{
    unsigned long long set_buffers(
        unsigned int start_slot,
        unsigned int num_buffers,
        buffer_alias* const* buffers,
        const unsigned long long* offsets) override
    {
        unsigned long long result = start_slot;
        for (unsigned int i = 0; i < num_buffers; ++i)
            result += static_cast<unsigned long long>(buffers[i]->id) * offsets[i];
        return result;
    }

    unsigned long long set_value(const unsigned long long* value) override
    {
        return *value;
    }
};

inline device_context* create_device_context()
{
    static device_context_impl value;
    return &value;
}

/// A record declared inside another record's scope.
struct owner
{
    struct value
    {
        int data{};
    };

    value* item{};
};

inline owner* create_owner(int data)
{
    static owner::value item{};
    static owner instance{};
    item.data = data;
    instance.item = &item;
    return &instance;
}

struct value
{
    int data{};
};

/// Director callbacks receive primitive pointers raw and records as wrappers.
struct [[sharpcpp::director]] renderer
{
    virtual ~renderer() = default;
    virtual int render(int* indices, const value& translation, int count) = 0;
};

inline int render_with(renderer* target)
{
    int indices[3] = {1, 2, 3};
    value translation{10};
    return target->render(indices, translation, 3);
}
}
