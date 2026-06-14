#pragma once

namespace Urho3D
{
template <class T>
class BaseVector3
{
public:
    T x_;
    T y_;
    T z_;
};

using Vector3 = BaseVector3<float>;

class ActionBuilder
{
public:
    void JumpBy(const Vector3& offset);
};
}
