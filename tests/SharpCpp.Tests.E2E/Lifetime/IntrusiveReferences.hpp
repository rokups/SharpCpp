#pragma once

namespace intrusive_references
{
/// AddRef/Release are inherited: records deriving from the base are intrusive targets too.
class ref_target_base
{
public:
    void AddRef() const
    {
        ++refs_;
    }

    void Release() const
    {
        if (--refs_ == 0)
            delete this;
    }

    int refs() const
    {
        return refs_;
    }

    static int destroyed()
    {
        return destroyed_;
    }

protected:
    virtual ~ref_target_base()
    {
        ++destroyed_;
    }

private:
    mutable int refs_{};
    inline static int destroyed_{};
};

class ref_target final : public ref_target_base
{
public:
    int value() const
    {
        return 7;
    }
};

inline ref_target* create_ref_target()
{
    return new ref_target;
}

/// Reading an intrusive pointer field takes a reference of its own.
struct ref_holder
{
    ref_target* target{};
};
}
