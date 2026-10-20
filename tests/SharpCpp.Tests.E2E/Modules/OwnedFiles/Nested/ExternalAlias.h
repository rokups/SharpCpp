#pragma once

#include "../../OwnedFilesExternal/ExternalColor.hpp"

namespace owned_root
{
// The alias wraps the external enum under the alias name.
using OwnedColor = external_vendor::EXTERNAL_COLOR;

inline OwnedColor owned_next_color(OwnedColor value)
{
    return static_cast<OwnedColor>(static_cast<unsigned char>(value) * 2);
}

using OwnedShade = external_vendor::EXTERNAL_SHADE;

inline OwnedShade owned_darker_shade(OwnedShade value)
{
    return static_cast<OwnedShade>(static_cast<unsigned short>(value) + 1);
}
}
