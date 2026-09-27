#pragma once

namespace nested_enums
{
/// A record that only scopes its enum: the enum takes the record's name.
struct IndexFormat
{
    enum Enum
    {
        UInt16,
        UInt32
    };
};

inline IndexFormat::Enum get_index_format()
{
    return IndexFormat::UInt32;
}

inline IndexFormat::Enum echo_index_format(IndexFormat::Enum value)
{
    return value;
}

namespace first
{
enum Type
{
    FirstValue = 3
};

inline Type get_first_type()
{
    return FirstValue;
}
}

namespace second
{
class Log
{
public:
    enum Type
    {
        SecondValue = 5
    };

    static Type get_type()
    {
        return SecondValue;
    }
};
}

class Variant
{
public:
    enum Type : unsigned long
    {
        None,
        Integer
    };

    explicit Variant(Type type)
        : type_(type)
    {
    }

    Type GetType() const
    {
        return type_;
    }

private:
    Type type_{};
};

class Channel
{
public:
    enum Type
    {
        Error = 7
    };

    static Type get_type()
    {
        return Error;
    }
};

enum class small_i8 : signed char
{
    value = -3
};

enum class small_u8 : unsigned char
{
    value = 250
};

enum class small_i16 : short
{
    value = -300
};

enum class small_u16 : unsigned short
{
    value = 65000
};

inline int sum_small_enums(small_i8 a, small_u8 b, small_i16 c, small_u16 d)
{
    return static_cast<int>(a) + static_cast<int>(b) + static_cast<int>(c) + static_cast<int>(d);
}
}
