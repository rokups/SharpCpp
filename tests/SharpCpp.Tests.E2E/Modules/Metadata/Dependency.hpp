#pragma once

#include <array>
#include <functional>
#include <vector>

namespace sharpcpp_coverage
{
enum class dependency_state : unsigned int
{
    idle = 0,
    ready = 1
};

enum class [[sharpcpp::flags]] dependency_permissions : unsigned int
{
    none = 0u,
    read = 1u,
    write = 2u,
    all = 3u
};

enum class dependency_small_state : unsigned short
{
    idle = 0,
    ready = 1,
    high = 0xe001
};

enum class dependency_i8_state : signed char
{
    idle = 0,
    ready = 1,
    negative = -8
};

enum class dependency_u8_state : unsigned char
{
    idle = 0,
    ready = 1
};

enum class dependency_i16_state : short
{
    idle = 0,
    ready = 1
};

enum class dependency_i32_state : int
{
    idle = 0,
    ready = 1
};

enum class dependency_i64_state : long long
{
    idle = 0,
    ready = 1
};

enum class dependency_u64_state : unsigned long long
{
    idle = 0,
    ready = 1
};

enum class dependency_void_projection_state : int { idle = 0, ready = 1 };
enum class dependency_bool_projection_state : int { idle = 0, ready = 1 };
enum class dependency_u32_projection_state : int { idle = 0, ready = 1 };
enum class dependency_float_projection_state : int { idle = 0, ready = 1 };
enum class dependency_double_projection_state : int { idle = 0, ready = 1 };
enum class dependency_long_projection_state : int { idle = 0, ready = 1 };
enum class dependency_ulong_projection_state : int { idle = 0, ready = 1 };
enum class dependency_pointer_projection_state : int { idle = 0, ready = 1 };

inline unsigned long long consume_dependency_enum_matrix(
    dependency_i8_state i8,
    dependency_u8_state u8,
    dependency_i16_state i16,
    dependency_i32_state i32,
    dependency_i64_state i64,
    dependency_u64_state u64)
{
    return static_cast<unsigned long long>(i8) +
        static_cast<unsigned long long>(u8) +
        static_cast<unsigned long long>(i16) +
        static_cast<unsigned long long>(i32) +
        static_cast<unsigned long long>(i64) +
        static_cast<unsigned long long>(u64);
}

inline int consume_dependency_projection_enum_matrix(
    dependency_void_projection_state void_state,
    dependency_bool_projection_state bool_state,
    dependency_u32_projection_state u32_state,
    dependency_float_projection_state float_state,
    dependency_double_projection_state double_state,
    dependency_long_projection_state long_state,
    dependency_ulong_projection_state ulong_state,
    dependency_pointer_projection_state pointer_state)
{
    return static_cast<int>(void_state) + static_cast<int>(bool_state) +
        static_cast<int>(u32_state) + static_cast<int>(float_state) +
        static_cast<int>(double_state) + static_cast<int>(long_state) +
        static_cast<int>(ulong_state) + static_cast<int>(pointer_state);
}

struct dependency_value
{
    dependency_value(int value, dependency_state state)
        : value_(value), state_(state)
    {
    }

    dependency_value(const dependency_value&) = default;

    int get_value() const { return value_; }
    void set_value(int value) { value_ = value; }
    dependency_state get_state() const { return state_; }
    void set_state(dependency_state state) { state_ = state; }

    int exposed_value{};

private:
    int value_{};
    dependency_state state_{dependency_state::idle};
};

using dependency_alias = dependency_value;
using dependency_handle_alias = dependency_value*;
using dependency_state_alias = dependency_state;
using dependency_small_state_alias = dependency_small_state;
using dependency_signed_alias = long long;
using dependency_rebound_handle_alias = dependency_value*;
using dependency_unsigned_alias = unsigned long long;
using dependency_fixed_alias = std::array<int, 4>;
using dependency_dynamic_alias = std::vector<int>;
using dependency_action = std::function<void()>;
using dependency_transform = std::function<int(int)>;

struct dependency_base
{
    explicit dependency_base(int value)
        : value_(value)
    {
    }

    virtual ~dependency_base() = default;

    virtual int get_base_value() const
    {
        return value_;
    }

protected:
    int value_{};
};

struct dependency_derived final : dependency_base
{
    explicit dependency_derived(int value)
        : dependency_base(value)
    {
    }

    int get_base_value() const final
    {
        return value_ + 1;
    }

    int get_derived_value() const
    {
        return value_ * 2;
    }
};

template <typename T>
struct
[[sharpcpp::instantiate("int")]]
[[sharpcpp::instantiate("dependency_value*")]]
dependency_box
{
    explicit dependency_box(T value)
        : value_(value)
    {
    }

    T get_value() const
    {
        return value_;
    }

    void set_value(T value)
    {
        value_ = value;
    }

private:
    T value_{};
};

using dependency_int_box_alias = dependency_box<int>;
using dependency_value_box_alias = dependency_box<dependency_value*>;

template <typename T>
T dependency_import_identity(T value)
{
    return value;
}

template dependency_value_box_alias* dependency_import_identity(
    dependency_value_box_alias* value);

inline dependency_signed_alias increment_dependency_signed_alias(dependency_signed_alias value)
{
    return value + 2;
}

inline dependency_small_state_alias round_trip_dependency_small_state_alias(
    dependency_small_state_alias value)
{
    return value;
}

inline int read_dependency_rebound_handle_alias(
    dependency_rebound_handle_alias value)
{
    return value == nullptr ? -1 : value->get_value();
}

inline int read_dependency_value_box_alias(const dependency_value_box_alias* value)
{
    const auto* item = value == nullptr ? nullptr : value->get_value();
    return item == nullptr ? -1 : item->get_value();
}

struct dependency_primitives
{
    dependency_primitives() = default;

    bool echo_bool(bool value) const { return value; }
    signed char echo_int8(signed char value) const { return value; }
    unsigned char echo_uint8(unsigned char value) const { return value; }
    short echo_int16(short value) const { return value; }
    unsigned short echo_uint16(unsigned short value) const { return value; }
    int echo_int32(int value) const { return value; }
    unsigned int echo_uint32(unsigned int value) const { return value; }
    long long echo_int64(long long value) const { return value; }
    unsigned long long echo_uint64(unsigned long long value) const { return value; }
    float echo_single(float value) const { return value; }
    double echo_double(double value) const { return value; }
    long echo_native_int(long value) const { return value; }
    unsigned long echo_native_uint(unsigned long value) const { return value; }
    void* echo_pointer(void* value) const { return value; }
    int echo_reference(const int& value) const { return value; }

    void invoke_action(dependency_action callback) const
    {
        callback();
    }

    int invoke_transform(dependency_transform callback, int value) const
    {
        return callback(value);
    }

    void update([[sharpcpp::ref]] int* value) const
    {
        ++*value;
    }

    void write([[sharpcpp::out]] int* value) const
    {
        *value = 17;
    }

    static int static_value(int value)
    {
        return value + 1;
    }

protected:
    int protected_value(int value) const
    {
        return value + 2;
    }
};

struct dependency_holder
{
    explicit dependency_holder(dependency_value* item)
        : item_(item)
    {
    }

    dependency_value* get_item() const { return item_; }
    void set_item(dependency_value* item) { item_ = item; }
    int read_item() const { return item_ == nullptr ? -1 : item_->get_value(); }

private:
    dependency_value* item_{};
};

inline dependency_value* create_dependency_value(int value, dependency_state state)
{
    return new dependency_value(value, state);
}

inline dependency_base* create_dependency_base(int value)
{
    return new dependency_base(value);
}

inline dependency_derived* create_dependency_derived(int value)
{
    return new dependency_derived(value);
}

inline dependency_box<int>* create_dependency_box(int value)
{
    return new dependency_box<int>(value);
}

inline dependency_value_box_alias* create_dependency_value_box(dependency_value* value)
{
    return new dependency_value_box_alias(value);
}

inline dependency_holder* create_dependency_holder(dependency_value* item)
{
    return new dependency_holder(item);
}


/// Accessor pairs become properties; mismatched or shadowed pairs stay methods.
struct dependency_accessors
{
    int get_count() const { return count_; }
    void set_count(int value) { count_ = value; }

    bool is_hovered() const { return hovered_; }
    void hover() { hovered_ = true; }

    int get_ratio() const { return ratio_; }
    void set_ratio(float value) { ratio_ = static_cast<int>(value); }

    int get_size() const { return 3; }
    int Size() const { return 4; }

    int get_limit() const { return Limit + 1; }
    int Limit{2};

private:
    int count_{};
    int ratio_{};
    bool hovered_{};
};

struct dependency_level_source
{
    virtual ~dependency_level_source() = default;
    virtual int get_level() const { return level_; }
    virtual void set_level(int value) { level_ = value; }

protected:
    int level_{};
};

/// Methods projected from a secondary base are not promoted to properties.
struct dependency_leveled : dependency_value, dependency_level_source
{
    dependency_leveled()
        : dependency_value(0, dependency_state::idle)
    {
    }
};
}

