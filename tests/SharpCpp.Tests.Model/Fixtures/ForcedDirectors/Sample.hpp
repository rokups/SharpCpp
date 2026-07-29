#pragma once

namespace Test
{

class ForcedAbstract
{
public:
    explicit ForcedAbstract(int seed)
        : seed_(seed)
    {
    }

    virtual ~ForcedAbstract() = default;

    virtual int Compute() const = 0;

protected:
    int seed_{};
};

class ForcedImplicitAbstract
{
public:
    virtual ~ForcedImplicitAbstract() = default;

    virtual int Compute() const = 0;
};

class ImplicitDefaultAbstract
{
public:
    virtual ~ImplicitDefaultAbstract() = default;

    virtual int Compute() const = 0;
};

class CopyableBase
{
};

class ImplicitDefaultDerivedAbstract : public CopyableBase
{
public:
    virtual ~ImplicitDefaultDerivedAbstract() = default;

    virtual int Compute() const = 0;
};

}
