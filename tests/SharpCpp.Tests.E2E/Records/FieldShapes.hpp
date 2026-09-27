#pragma once

namespace field_shapes
{
typedef char Char;

struct shader_create_info
{
    const Char* Source = nullptr;
    const Char* EntryPoint = nullptr;
};

inline int shader_source_length(const shader_create_info* info)
{
    int length = 0;
    if (info != nullptr && info->Source != nullptr)
        while (info->Source[length] != '\0')
            ++length;
    return length;
}

inline shader_create_info* create_shader_create_info()
{
    static shader_create_info value{"void main() {}", "main"};
    return &value;
}

typedef int (*writer)(const unsigned char* data, unsigned long size);

struct picture
{
    writer write = nullptr;
};

inline int sum_picture_bytes(const unsigned char* data, unsigned long size)
{
    int result = 0;
    for (unsigned long i = 0; i < size; ++i)
        result += data[i];
    return result;
}

inline writer get_sum_picture_writer()
{
    return &sum_picture_bytes;
}

inline int write_picture(const picture* value)
{
    const unsigned char data[3] = {10, 20, 12};
    return value == nullptr || value->write == nullptr ? -1 : value->write(data, 3);
}

struct mesh
{
    unsigned int* indices = nullptr;
    const float* weights = nullptr;
    unsigned int count = 0;
};

inline float weigh_mesh(const mesh* value)
{
    float result = 0;
    for (unsigned int i = 0; i < value->count; ++i)
        result += static_cast<float>(value->indices[i]) * value->weights[i];
    return result;
}

using table_callback = void (*)(int value);

struct string_table
{
    const char** names = nullptr;
    table_callback callbacks[4]{};
    unsigned int count = 0;
};

inline int string_table_first_length(const string_table* value)
{
    int length = 0;
    if (value->count > 0)
        while (value->names[0][length] != '\0')
            ++length;
    return length;
}

struct bit_flags
{
    unsigned int visible : 1;
    unsigned int layer : 3;
};

inline int read_bit_flags(const bit_flags* value)
{
    return static_cast<int>(value->visible) * 10 + static_cast<int>(value->layer);
}

struct globals
{
    inline static int value = 3;
};

inline int read_globals_value()
{
    return globals::value;
}
}

namespace field_shapes
{
inline const char** get_string_table_names(string_table* value)
{
    return value->names;
}
}
