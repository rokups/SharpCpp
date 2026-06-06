#include <RbfxUserCode/RotatingComponent.hpp>

namespace RbfxUserCode
{

RotatingComponent::RotatingComponent(Urho3D::Context* context)
    : LogicComponent(context)
{
}

void RotatingComponent::SetRotationSpeed(float degreesPerSecond)
{
    rotationSpeed_ = degreesPerSecond;
}

float RotatingComponent::GetRotationSpeed() const
{
    return rotationSpeed_;
}

} // namespace RbfxUserCode
