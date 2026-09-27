#pragma once

namespace flattened_fields
{
struct stencil_desc
{
    int StencilFailOp;
};

struct depth_stencil_desc
{
    stencil_desc BackFace;
};

struct graphics_pipeline_desc
{
    depth_stencil_desc DepthStencilDesc;
};

struct pipeline_state_desc
{
    graphics_pipeline_desc GraphicsPipeline;
};

inline int read_pipeline_stencil(const pipeline_state_desc* value)
{
    return value->GraphicsPipeline.DepthStencilDesc.BackFace.StencilFailOp;
}

/// A record with methods stays a wrapper when nested in plain data.
struct curve
{
    void clear()
    {
        points = 0;
    }

    void add_point(float, float)
    {
        ++points;
    }

    int point_count() const
    {
        return points;
    }

    int points;
};

struct engine
{
    curve torque;
    float maximum;
};

struct settings
{
    engine engine_settings;
    curve friction;
};

inline settings* create_settings()
{
    static settings value{};
    value.engine_settings.torque.points = 0;
    value.engine_settings.maximum = 0;
    value.friction.points = 0;
    return &value;
}

inline int read_settings_torque(const settings* value)
{
    return value->engine_settings.torque.points;
}

template <class T>
struct buffer
{
    int size;
    T* data;

    void clear()
    {
        size = 0;
    }
};

struct draw_list
{
    buffer<int> vertices;
};

inline int read_draw_list_size(const draw_list* value)
{
    return value->vertices.size;
}

struct features
{
    int enabled;

    static bool equal(const features& left, const features& right)
    {
        return left.enabled == right.enabled;
    }

    template <class T>
    static void enumerate(T&&)
    {
    }

    bool operator==(const features& other) const
    {
        return enabled == other.enabled;
    }
};

struct requested_settings
{
    features requested;
};

inline int read_requested_enabled(const requested_settings* value)
{
    return value->requested.enabled;
}

struct nested_state
{
    int value;
    inline static int shared_value = 1;
};

struct state_owner
{
    nested_state state;
};

inline int read_state_owner(const state_owner* value)
{
    return value->state.value + nested_state::shared_value;
}

struct aliased_storage_vector
{
    using storage = struct
    {
        float data[4];
    };

    union
    {
        storage value;
        float components[4];
    };
};

inline float sum_aliased_storage_vector(const aliased_storage_vector* value)
{
    return value->components[0] + value->components[1] + value->components[2] + value->components[3];
}

struct typedef_features
{
    int enabled;
};

typedef struct typedef_features typedef_features;

struct typedef_settings
{
    typedef_features capabilities;
};

inline int read_typedef_settings(const typedef_settings* value)
{
    return value->capabilities.enabled;
}

class [[sharpcpp::director]] protected_container
{
public:
    virtual ~protected_container() = default;

    virtual int get_value() const
    {
        return data.value;
    }

protected:
    struct nested_value
    {
        int value;
    } data{};
};

inline int read_protected_container(const protected_container* value)
{
    return value->get_value();
}

struct protected_constructor
{
    int value;

protected:
    protected_constructor() = default;
};

struct protected_constructor_factory : protected_constructor
{
    static protected_constructor* create(int value)
    {
        static protected_constructor_factory instance;
        instance.value = value;
        return &instance;
    }
};
}
