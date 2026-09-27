#pragma once

namespace dropped_base
{
/// rbfx's archive shape: a root with virtuals, a helper template base that is never
/// instantiated on its own, a generic intermediate base, and a concrete final override.
class archive
{
public:
    virtual ~archive() = default;
    virtual int read() { return 1; }
};

template <class Block, bool IsInput, bool IsHumanReadable>
class archive_t : public archive
{
public:
    bool is_input() const { return IsInput; }
};

template <class Block, bool IsInput>
class json_base : public archive_t<Block, IsInput, true>
{
public:
    int name() const { return 5; }
};

class json_block
{
public:
    int depth() const { return 0; }
};

class json_input final : public json_base<json_block, true>
{
public:
    int read() final { return 7; }
};
}
