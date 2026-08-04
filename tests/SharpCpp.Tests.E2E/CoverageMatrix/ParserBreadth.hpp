#pragma once

#include <cstdint>
#include <functional>
#include <locale>
#include <system_error>
#include <utility>

namespace parser_breadth
{
enum class breadth_mode : unsigned short
{
    idle = 0,
    active = 1
};

enum
{
    anonymous_breadth_value = 3
};

struct breadth_payload
{
    explicit breadth_payload(int value = 0)
        : value_(value)
    {
    }

    int get_value() const
    {
        return value_;
    }

private:
    int value_{};
};

struct breadth_payload_projection
{
    breadth_payload* value{};
};

struct breadth_nullable_payload_projection
{
    breadth_payload* value{};
};

struct breadth_mode_projection
{
    breadth_mode value{breadth_mode::idle};
};

inline breadth_payload_projection breadth_project_payload(breadth_payload_projection value)
{
    return value;
}

inline breadth_nullable_payload_projection breadth_project_nullable_payload(
    breadth_nullable_payload_projection value)
{
    return value;
}

inline breadth_mode_projection breadth_project_mode(breadth_mode_projection value)
{
    return value;
}

using breadth_payload_alias = breadth_payload;
using breadth_payload_handle = breadth_payload*;
using breadth_mode_alias = breadth_mode;
using breadth_raw_callback = void (*)(int);
using breadth_int_reference = int&;

inline breadth_payload_alias& breadth_alias_reference(breadth_payload_alias& value)
{
    return value;
}

inline int invoke_breadth_raw_callback(breadth_raw_callback callback)
{
    if (callback != nullptr)
        callback(7);
    return callback == nullptr ? 0 : 1;
}

struct breadth_late_record;
int read_breadth_late_record(const breadth_late_record* value);

struct breadth_late_record
{
    explicit breadth_late_record(int value)
        : value(value)
    {
    }

    int value{};
};

inline int read_breadth_late_record(const breadth_late_record* value)
{
    return value == nullptr ? -1 : value->value;
}

struct breadth_out_of_line_owner
{
    struct nested;
};

struct breadth_out_of_line_owner::nested
{
    explicit nested(int value)
        : value_(value)
    {
    }

    int read() const
    {
        return value_;
    }

private:
    int value_{};
};

inline breadth_out_of_line_owner::nested* make_breadth_out_of_line_nested(int value)
{
    return new breadth_out_of_line_owner::nested(value);
}

template <typename T>
struct breadth_forward_box;

template <typename T>
struct
[[sharpcpp::instantiate("int")]]
[[sharpcpp::instantiate("breadth_payload*")]]
breadth_forward_box
{
    explicit breadth_forward_box(T value)
        : value_(value)
    {
    }

    T get_value() const
    {
        return value_;
    }

    T echo(T value) const
    {
        return value;
    }

    breadth_payload* echo_payload(breadth_payload* value) const
    {
        return value;
    }

    int read_payload(const breadth_payload* value) const
    {
        return value == nullptr ? -1 : value->get_value();
    }

private:
    T value_{};
};

template <typename T>
T breadth_roundtrip_box(T value)
{
    return value;
}

template breadth_forward_box<int>* breadth_roundtrip_box(
    breadth_forward_box<int>* value);
template breadth_forward_box<breadth_payload*>* breadth_roundtrip_box(
    breadth_forward_box<breadth_payload*>* value);

using breadth_int_box = breadth_forward_box<int>;
template <typename T>
using breadth_box_alias_template = breadth_forward_box<T>;
using breadth_payload_box_alias = breadth_box_alias_template<breadth_payload*>;

inline breadth_forward_box<breadth_payload*>* make_breadth_payload_box(breadth_payload* value)
{
    return new breadth_forward_box<breadth_payload*>(value);
}

inline breadth_payload* read_breadth_payload_box(const breadth_forward_box<breadth_payload*>* value)
{
    return value == nullptr ? nullptr : value->get_value();
}

inline breadth_payload_box_alias* make_breadth_alias_payload_box(breadth_payload* value)
{
    return new breadth_payload_box_alias(value);
}

inline breadth_payload* read_breadth_alias_payload_box(const breadth_payload_box_alias* value)
{
    return value == nullptr ? nullptr : value->get_value();
}

template <typename T>
[[sharpcpp::instantiate("bool")]]
[[sharpcpp::instantiate("signed char")]]
[[sharpcpp::instantiate("unsigned char")]]
[[sharpcpp::instantiate("int8_t")]]
[[sharpcpp::instantiate("uint8_t")]]
[[sharpcpp::instantiate("short")]]
[[sharpcpp::instantiate("unsigned short")]]
[[sharpcpp::instantiate("int16_t")]]
[[sharpcpp::instantiate("uint16_t")]]
[[sharpcpp::instantiate("int")]]
[[sharpcpp::instantiate("unsigned int")]]
[[sharpcpp::instantiate("int32_t")]]
[[sharpcpp::instantiate("uint32_t")]]
[[sharpcpp::instantiate("long long")]]
[[sharpcpp::instantiate("unsigned long long")]]
[[sharpcpp::instantiate("int64_t")]]
[[sharpcpp::instantiate("uint64_t")]]
[[sharpcpp::instantiate("float")]]
[[sharpcpp::instantiate("double")]]
[[sharpcpp::instantiate("long")]]
[[sharpcpp::instantiate("unsigned long")]]
[[sharpcpp::instantiate("char")]]
[[sharpcpp::instantiate("breadth_payload*")]]
T breadth_template_identity(T value)
{
    return value;
}

template breadth_payload* breadth_template_identity<breadth_payload*>(breadth_payload* value);
template breadth_mode breadth_template_identity<breadth_mode>(breadth_mode value);
template long double breadth_template_identity<long double>(long double value);

template <typename... TValues>
[[sharpcpp::ignore("template pack parser breadth")]]
int breadth_pack_sum(TValues... values)
{
    return (0 + ... + static_cast<int>(values));
}

template int breadth_pack_sum<int, short, long>(int, short, long);

template <int N>
[[sharpcpp::ignore("non-type template parser breadth")]]
int breadth_non_type_template(int value)
{
    return value + N;
}

template int breadth_non_type_template<3>(int value);

template <typename T>
[[sharpcpp::instantiate("int")]]
T breadth_unnamed_identity(T)
{
    return T{53};
}

template <typename T>
[[sharpcpp::instantiate("int")]]
void breadth_increment_template(T& value)
{
    ++value;
}

inline int invoke_breadth_callback_reference(const std::function<int(int)>& callback, int value)
{
    return callback(value) + 1;
}

struct breadth_pointer_fields
{
    int* int_pointer{};
    double* double_pointer{};
    unsigned long long* uint64_pointer{};
    breadth_raw_callback callback{};
    int** nested_pointer{};
    char* text{};
    unsigned int bits : 3 {};
    inline static int static_value = 17;
    inline static int static_values[2] = {3, 5};
};

struct breadth_flattened_leaf
{
    /// Value copied through a flattened native field accessor.
    int visible{};
    breadth_mode_projection projected{};

private:
    int hidden{};
};

struct breadth_flattened_owner
{
    struct
    {
        breadth_flattened_leaf leaf{};
        char* text{};
        std::locale* external{};
    } group;
};

inline int read_breadth_flattened_owner(const breadth_flattened_owner* value)
{
    if (value == nullptr)
        return -1;
    return value->group.leaf.visible +
        (value->group.text == nullptr ? 1 : 0) +
        (value->group.external == nullptr ? 2 : 0);
}

struct [[sharpcpp::ignore("reference field parser breadth")]] breadth_reference_field
{
    explicit breadth_reference_field(int& value)
        : value(value)
    {
    }

    int& value;
};

struct breadth_nested_enum_owner
{
    enum class [[sharpcpp::rename("BreadthNestedMode")]] Enum : unsigned char
    {
        first = 1,
        second = 2
    };

    explicit breadth_nested_enum_owner(Enum value)
        : value_(value)
    {
    }

    Enum get_value() const { return value_; }

private:
    Enum value_{};
};

struct breadth_object_names
{
    explicit breadth_object_names(int value)
        : value_(value)
    {
    }

    int get_type() const { return value_ + 1; }
    int get_hash_code() const { return value_ + 2; }
    bool equals(const breadth_object_names& other) const { return value_ == other.value_; }

private:
    int value_{};
};

struct breadth_named_base
{
    int ping(int value) const { return value + 1; }
};

struct breadth_named_derived : breadth_named_base
{
    breadth_named_derived() = default;

    int ping(int value) const { return value + 2; }
};

struct breadth_empty_anonymous_owner
{
    explicit breadth_empty_anonymous_owner(int value)
        : value(value)
    {
    }

    int read() const { return value; }

    struct
    {
    } empty;
    int value{};
};

struct breadth_unnamed_members
{
    explicit breadth_unnamed_members(int)
        : value_(61)
    {
    }

    int read() const { return value_; }

    static int combine(int, int value)
    {
        return value + 1;
    }

private:
    int value_{};
};

struct breadth_abstract
{
    explicit breadth_abstract(int value)
        : value_(value)
    {
    }

    virtual ~breadth_abstract() = default;
    virtual int calculate() const = 0;

protected:
    int value_{};
};

struct breadth_private_destructor
{
    explicit breadth_private_destructor(int value)
        : value_(value)
    {
    }

    int get_value() const { return value_; }

private:
    ~breadth_private_destructor() = default;
    int value_{};
};

struct [[sharpcpp::ignore("generic constructor parser breadth")]] breadth_generic_constructor
{
    template <typename T>
    explicit breadth_generic_constructor(T value)
        : value_(static_cast<int>(value))
    {
    }

    int value_{};
};

struct [[sharpcpp::ignore("unnamed generic parameter parser breadth")]] breadth_unnamed_generic_method
{
    template <typename T>
    [[sharpcpp::instantiate("int")]]
    T project(T) const;
};

[[sharpcpp::ignore("rvalue parser breadth")]]
inline breadth_payload consume_breadth_rvalue(breadth_payload&& value)
{
    return std::move(value);
}

inline int breadth_variadic(int first, ...)
{
    return first;
}

[[sharpcpp::ignore("external record parser breadth")]]
inline int consume_external_locale_value(std::locale value)
{
    return value.name().empty() ? 0 : 1;
}

[[sharpcpp::ignore("external record pointer parser breadth")]]
inline int consume_external_locale_pointer(std::locale** value)
{
    return value == nullptr ? 0 : 1;
}

[[sharpcpp::ignore("external enum parser breadth")]]
inline int consume_external_error(std::errc value)
{
    return static_cast<int>(value);
}

inline int evaluated_breadth_defaults(
    int seed,
    int integer = 1 + 2,
    unsigned int unsigned_value = 2u + 3u,
    float single_value = 1.25f + 0.25f,
    double double_value = 2.0 * 2.0,
    bool enabled = 1 < 2,
    breadth_mode mode = breadth_mode :: active)
{
    return seed + integer + static_cast<int>(unsigned_value) +
        static_cast<int>(single_value * 10.0f) + static_cast<int>(double_value) +
        (enabled ? 1 : 0) + (mode == breadth_mode::active ? 10 : 0);
}

enum breadth_plain_mode
{
    breadth_plain_idle = 0,
    breadth_plain_active = 1
};

inline int breadth_literal_defaults(
    float single_value = 1.25f,
    double double_value = 2.5,
    breadth_plain_mode mode = breadth_plain_active)
{
    return static_cast<int>(single_value * 100.0f) +
        static_cast<int>(double_value * 100.0) +
        (mode == breadth_plain_active ? 1 : 0);
}

namespace defaults
{
inline constexpr int qualified_value = 7;
}

inline constexpr int breadth_named_default = 9;

inline int breadth_qualified_default(int value = defaults::qualified_value)
{
    return value + 1;
}

inline int breadth_named_identifier_default(int value = breadth_named_default)
{
    return value + 1;
}

inline void breadth_unnamed_parameter(int)
{
}

inline int parser_breadth_round_trip(int value)
{
    return value + anonymous_breadth_value;
}
}

namespace parser_alias_target
{
struct alias_payload
{
    explicit alias_payload(int value)
        : value_(value)
    {
    }

    int get_value() const { return value_; }

private:
    int value_{};
};
}

namespace parser_alias = parser_alias_target;
using parser_alias_payload = parser_alias::alias_payload;

inline parser_alias_payload* make_parser_alias_payload(int value)
{
    return new parser_alias_payload(value);
}

inline int read_parser_alias_payload(const parser_alias_payload* value)
{
    return value == nullptr ? -1 : value->get_value();
}
