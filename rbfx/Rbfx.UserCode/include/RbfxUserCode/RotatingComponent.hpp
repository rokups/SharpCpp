#pragma once

#include <Urho3D/Core/Context.h>
#include <Urho3D/Scene/LogicComponent.h>

namespace RbfxUserCode
{

class RotatingComponent : public Urho3D::LogicComponent
{
public:
    void SetRotationSpeed(float degreesPerSecond);
    float GetRotationSpeed() const;

protected:
    explicit RotatingComponent(Urho3D::Context* context);

private:
    float rotationSpeed_{45.0f};
};

} // namespace RbfxUserCode
