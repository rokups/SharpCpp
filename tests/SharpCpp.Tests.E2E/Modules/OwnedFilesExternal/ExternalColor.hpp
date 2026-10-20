#pragma once

// This header is outside the owned root. Only an owned alias makes its enums part of the managed surface.
namespace external_vendor
{
enum EXTERNAL_COLOR : unsigned char
{
    EXTERNAL_COLOR_RED = 1,
    EXTERNAL_COLOR_GREEN = 2,
    EXTERNAL_COLOR_BLUE = 4
};

typedef unsigned short ExternalUint16;

// The macro hides the fixed underlying type from the source text of the declaration.
#define EXTERNAL_TYPED_ENUM(Name, Type) enum Name : Type

EXTERNAL_TYPED_ENUM(EXTERNAL_SHADE, ExternalUint16)
{
    EXTERNAL_SHADE_LIGHT = 1,
    EXTERNAL_SHADE_DARK = 2
};
}
