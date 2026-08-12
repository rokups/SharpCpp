#pragma once

using field_uint64 = unsigned long long;

enum class field_mode : int
{
    idle = 1,
    active = 2,
    suspended = 3
};

struct field_payload
{
    int value{};

    inline static int shared_value = 5;
    inline static field_payload* shared_payload{};
};

struct field_box
{
    field_box(int count, bool enabled, field_mode mode, field_payload* payload, int fixed_value)
        : count(count)
        , enabled(enabled)
        , mode(mode)
        , payload(payload)
        , fixed_value(fixed_value)
    {
    }

    int count{};
    bool enabled{};
    field_mode mode{field_mode::idle};
    field_payload* payload{};
    field_payload embedded{7};
    field_payload payload_samples[2]{};
    field_payload* payload_handles[2]{};
    field_payload* single_payload[1]{};
    field_mode modes[2]{field_mode::idle, field_mode::active};
    bool switches[2]{false, true};
    int pair[2]{11, 13};
    int matrix[2][3]{{1, 2, 3}, {4, 5, 6}};
    int samples[3]{};
    field_uint64 wide_samples[2]{};
    const int fixed_value{};
};

inline field_payload* create_field_payload(int value)
{
    return new field_payload { value };
}

inline field_box* create_field_box(int count, bool enabled, field_mode mode, field_payload* payload, int fixed_value)
{
    return new field_box(count, enabled, mode, payload, fixed_value);
}

struct pointer_count_field_box
{
    field_payload** Items{};
    unsigned int NumItems{};
    field_payload* Values{};
    unsigned int ValueCount{};
    const field_payload* ConstValues{};
    unsigned int ConstValueCount{};
};

inline pointer_count_field_box* create_pointer_count_field_box(field_payload* first, field_payload* second)
{
    static field_payload* items[2];
    static field_payload values[2]{{59}, {61}};
    static const field_payload const_values[2]{{67}, {71}};
    items[0] = first;
    items[1] = second;
    return new pointer_count_field_box{items, 2, values, 2, const_values, 2};
}

class intrusive_field_payload
{
public:
    void AddRef() const
    {
        ++ref_count_;
    }

    void Release() const
    {
        if (--ref_count_ == 0)
            delete this;
    }

    int GetRefCount() const
    {
        return ref_count_;
    }

    static int GetDestroyedCount()
    {
        return destroyed_count_;
    }

private:
    ~intrusive_field_payload()
    {
        ++destroyed_count_;
    }

    mutable int ref_count_{};
    inline static int destroyed_count_{};
};

inline intrusive_field_payload* create_intrusive_field_payload()
{
    return new intrusive_field_payload;
}

struct projected_field_value
{
    int value{};
};

struct offset_projected_field_value
{
    int value{};
};

using offset_projected_field_alias = offset_projected_field_value;

struct projected_field_box
{
    offset_projected_field_alias value{};
};

inline projected_field_box* create_projected_field_box()
{
    return new projected_field_box;
}

inline int read_projected_field_value(const projected_field_value& value)
{
    return value.value;
}

inline int read_projected_field_value_copy(const projected_field_value value)
{
    return value.value;
}

inline const projected_field_value& get_projected_field_value_reference()
{
    static projected_field_value value{42};
    return value;
}

inline void increment_projected_field_value(projected_field_value& value)
{
    ++value.value;
}
