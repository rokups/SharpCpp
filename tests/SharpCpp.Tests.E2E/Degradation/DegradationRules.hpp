#pragma once

#include <functional>
#include <string_view>

namespace degradation_rules
{
/// A static constant and an instance field that project to the same managed name.
struct bounce_settings
{
    static constexpr unsigned int MaxBounces = 8;
    unsigned int maxBounces_ = 3;
};

inline unsigned int read_bounce_settings(const bounce_settings* value)
{
    return value->maxBounces_ + bounce_settings::MaxBounces;
}

enum class attribute_mode
{
    none = 0,
    editable = 1
};

template <typename T>
struct [[sharpcpp::instantiate("degradation_rules::attribute_mode")]] flag_set
{
    int bits{};
};

using attribute_mode_flags = flag_set<attribute_mode>;

/// A generic argument that is an enum resolves by the enum's identity through the alias.
struct attribute_info
{
    explicit attribute_info(attribute_mode_flags mode)
        : mode_(mode)
    {
    }

    int bits() const
    {
        return mode_.bits;
    }

    attribute_mode_flags mode_;
};

struct context
{
    int value() const
    {
        return 11;
    }
};

struct [[sharpcpp::ignore("degradation coverage")]] hidden_context
{
    int value{};
};

/// Protected handle fields are bound for directors unless their record is not.
class [[sharpcpp::director]] context_object
{
public:
    virtual ~context_object() = default;

    virtual int read() const
    {
        return context_ == nullptr ? -1 : context_->value();
    }

protected:
    context* context_{};
    hidden_context* hidden_{};
};

inline context* get_shared_context()
{
    static context value;
    return &value;
}

inline int read_context_object(const context_object* value)
{
    return value->read();
}

/// Reference fields are not bound; the record still is.
struct reference_owner
{
    explicit reference_owner(int& value)
        : value(value)
    {
    }

    int& value;
    int plain{};
};

inline reference_owner* create_reference_owner()
{
    static int storage = 4;
    static reference_owner owner(storage);
    owner.plain = 6;
    return &owner;
}

/// A record that is only forward-declared, and the function that needs it.
struct forward_only;
inline forward_only* get_forward_only()
{
    return nullptr;
}

/// A director virtual whose callback cannot be generated is not bound; the record is.
class [[sharpcpp::director]] drawable
{
public:
    virtual ~drawable() = default;

    virtual void update_origin(const int& origin)
    {
        origin_ = origin;
    }

    virtual int origin() const
    {
        return origin_;
    }

protected:
    int origin_{};
};

/// A director whose pure virtual callback cannot be generated cannot be subclassed.
class [[sharpcpp::director]] unsupported_abstract
{
public:
    virtual ~unsupported_abstract() = default;
    virtual int compute(int& value) = 0;
};

class [[sharpcpp::director]] unavailable_abstract
{
public:
    virtual ~unavailable_abstract() = default;
    virtual int&& compute() = 0;
};

struct utility
{
    static int run()
    {
        return 12;
    }
};

struct callback_result
{
    int value{};
};

/// Callback returns of record values are supported.
inline int invoke_result_callback(const std::function<callback_result()>& callback)
{
    return callback().value;
}

/// Callback return types, callback fields and by-reference callbacks are not.
inline std::function<void(int)> get_callback()
{
    return {};
}

struct callback_owner
{
    std::function<void(int)> callback;
    int value{};
};

inline void replace_callback(std::function<void(int)>& callback)
{
    callback = {};
}

/// Callback arguments that need a managed conversion cannot be generated.
inline int invoke_view_callback(const std::function<int(std::string_view)>& callback)
{
    return callback("text");
}

struct value_argument
{
    int value{};
};

inline int invoke_value_argument_callback(const std::function<int(value_argument)>& callback)
{
    return callback(value_argument{3});
}

/// One delegate name requested with two different signatures.
inline void first_shared_delegate([[sharpcpp::delegate("SharedDelegate")]] std::function<void(int)> callback)
{
    callback(1);
}

inline void second_shared_delegate([[sharpcpp::delegate("SharedDelegate")]] std::function<void(float)> callback)
{
    callback(1.0f);
}

/// A renamed assignment operator is a method.
struct assignable
{
    int value{};

    [[sharpcpp::rename("Assign")]]
    assignable& operator=(const assignable& other)
    {
        value = other.value + 1;
        return *this;
    }
};

/// The global and the namespaced declaration are one managed declaration.
inline int equivalent_declaration()
{
    return 1;
}

namespace nested
{
inline int equivalent_declaration()
{
    return 2;
}
}
}

namespace degradation_rules
{
/// Const and non-const overloads that project to one managed signature keep the non-const one.
struct const_overloads
{
    int& data()
    {
        return value;
    }

    const int& data() const
    {
        return value;
    }

    /// The non-const overload is unsupported, so the const one is kept.
    int&& take()
    {
        return static_cast<int&&>(value);
    }

    int take() const
    {
        return value + 1;
    }

    /// Different parameters keep both.
    int item(int index)
    {
        return value + index;
    }

    int item() const
    {
        return value;
    }

    /// A member template and a const overload with the same parameters are distinct.
    template <typename T>
    [[sharpcpp::instantiate("int")]]
    T lookup(int key)
    {
        return static_cast<T>(key * 2);
    }

    int lookup(int key) const
    {
        return key;
    }

    int value{5};
};

/// Iterator endpoints and assignment operators are not bound; equality is.
struct iterable
{
    const int* begin() const
    {
        return values;
    }

    const int* end() const
    {
        return values + 2;
    }

    int count() const
    {
        return 2;
    }

    iterable& operator=(const iterable&) = default;

    bool operator==(const iterable& other) const
    {
        return values[0] == other.values[0];
    }

    int values[2]{1, 2};
};
}
