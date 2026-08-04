#pragma once

/// Represents a documented record with a brief description.
struct documented_record
{
    /// Creates a documented record with the given value.
    documented_record(int value)
        : stored_value(value)
    {
    }

    /// Gets the stored value.
    int get_value() const { return stored_value; }

    /// Sets the stored value.
    void set_value(int value) { stored_value = value; }

    /// Resets the record to its default state.
    static int reset(int value = 41) { return value + 1; }

    /// The current value stored in the record.
    int stored_value;

    /// Increments the stored value by the given amount.
    /// @return The new value after increment.
    int increment(int amount) const { return stored_value + amount; }
};

/**
 * A free function with a block-style documentation comment.
 * @param x The value to double.
 * @return The doubled value.
 */
inline int documented_free_function(int x) { return x * 2; }

/// Enumeration of status values for document processing.
enum class documented_status
{
    /// Operation completed successfully.
    success = 0,

    /// Operation is still pending.
    pending = 1,

    /// Operation encountered a failure.
    failure = 2
};

inline documented_status roundtrip_documented_status(documented_status value)
{
    return value;
}

/// Escapes XML-sensitive characters: <tag>value & "other"
inline int function_with_xml_sensitive_comment() { return 42; }
