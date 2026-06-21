#include "SharpCpp/Export.hpp"
#include "SharpCpp/Marshaller.hpp"

#include "ImGui/imgui.h"
#include "RmlUi/Core.h"
#include "Urho3D/Core/CoreEvents.h"
#include "Urho3D/Engine/Application.h"
#include "Urho3D/Engine/EngineDefs.h"
#include "Urho3D/Graphics/Camera.h"
#include "Urho3D/Graphics/Renderer.h"
#include "Urho3D/Graphics/StaticModel.h"
#include "Urho3D/Graphics/Viewport.h"
#include "Urho3D/Graphics/Zone.h"
#include "Urho3D/Resource/ResourceCache.h"
#include "Urho3D/RmlUI/RmlUI.h"
#include "Urho3D/Scene/Node.h"
#include "Urho3D/Scene/Scene.h"

using namespace SharpCpp;

namespace
{
using ApplicationGetTypeCallback = ret_t<Urho3D::StringHash> (*)(void* self);
using ApplicationIsInstanceOfCallback = ret_t<bool> (*)(void* self, arg_t<Urho3D::StringHash> type);
using ApplicationVoidCallback = ret_t<void> (*)(void* self);
using UpdateCallback = void (*)(void* context, float timeStep);

template <class T> T* AddRefAndReturn(T* value)
{
    if (value != nullptr)
    {
        value->AddRef();
    }

    return value;
}

class SharpCppRbfxApplication final : public Urho3D::Application
{
public:
    explicit SharpCppRbfxApplication(Urho3D::Context* context)
        : Urho3D::Application(context)
    {
    }

    void ConnectDirector(void* getType, void* isInstanceOf, void* setup, void* start, void* stop)
    {
        getType_ = reinterpret_cast<ApplicationGetTypeCallback>(getType);
        isInstanceOf_ = reinterpret_cast<ApplicationIsInstanceOfCallback>(isInstanceOf);
        setup_ = reinterpret_cast<ApplicationVoidCallback>(setup);
        start_ = reinterpret_cast<ApplicationVoidCallback>(start);
        stop_ = reinterpret_cast<ApplicationVoidCallback>(stop);
    }

    void SetEngineParameter(const char* name, bool value)
    {
        engineParameters_[name] = value;
    }

    void SetEngineParameter(const char* name, int value)
    {
        engineParameters_[name] = value;
    }

    void SetEngineParameter(const char* name, const char* value)
    {
        engineParameters_[name] = ea::string{value != nullptr ? value : ""};
    }

    Urho3D::StringHash GetType() const override
    {
        if (getType_ != nullptr)
        {
            ReturnMarshaller<Urho3D::StringHash> returnMarshaller;
            return returnMarshaller.ConvertToCpp(getType_(const_cast<void*>(static_cast<const void*>(this))));
        }

        return Urho3D::Application::GetType();
    }

    bool IsInstanceOf(Urho3D::StringHash type) const override
    {
        if (isInstanceOf_ != nullptr)
        {
            ParameterMarshaller<Urho3D::StringHash> argumentMarshaller;
            ReturnMarshaller<bool> returnMarshaller;
            return returnMarshaller.ConvertToCpp(isInstanceOf_(const_cast<void*>(static_cast<const void*>(this)), argumentMarshaller.ConvertToAbi(type)));
        }

        return Urho3D::Application::IsInstanceOf(type);
    }

    void Setup() override
    {
        if (setup_ != nullptr)
        {
            setup_(this);
            return;
        }

        Urho3D::Application::Setup();
    }

    void Start() override
    {
        if (start_ != nullptr)
        {
            start_(this);
            return;
        }

        Urho3D::Application::Start();
    }

    void Stop() override
    {
        if (stop_ != nullptr)
        {
            stop_(this);
            return;
        }

        Urho3D::Application::Stop();
    }

private:
    ApplicationGetTypeCallback getType_{};
    ApplicationIsInstanceOfCallback isInstanceOf_{};
    ApplicationVoidCallback setup_{};
    ApplicationVoidCallback start_{};
    ApplicationVoidCallback stop_{};
};
}

extern "C"
{
SHARPCPP_EXPORT ret_t<ea::string*> sharpcpp_rbfx_String_Create()
{
    return new ea::string();
}

SHARPCPP_EXPORT ret_t<Urho3D::Context*> sharpcpp_rbfx_Context_Create()
{
    return AddRefAndReturn(new Urho3D::Context());
}

SHARPCPP_EXPORT ret_t<Urho3D::Object*> sharpcpp_rbfx_Context_GetSubsystem(arg_t<Urho3D::Context*> self, arg_t<Urho3D::StringHash> type)
{
    ParameterMarshaller<Urho3D::StringHash> typeMarshaller;
    return self != nullptr ? self->GetSubsystem(typeMarshaller.ConvertToCpp(type)) : nullptr;
}

SHARPCPP_EXPORT ret_t<Urho3D::Object*> sharpcpp_rbfx_Object_GetSubsystem(arg_t<Urho3D::Object*> self, arg_t<Urho3D::StringHash> type)
{
    ParameterMarshaller<Urho3D::StringHash> typeMarshaller;
    return self != nullptr ? self->GetSubsystem(typeMarshaller.ConvertToCpp(type)) : nullptr;
}

SHARPCPP_EXPORT ret_t<Urho3D::Context*> sharpcpp_rbfx_Object_GetContext(arg_t<Urho3D::Object*> self)
{
    return self != nullptr ? self->GetContext() : nullptr;
}

SHARPCPP_EXPORT ret_t<Urho3D::Application*> sharpcpp_rbfx_Application_Create(arg_t<Urho3D::Context*> context)
{
    return AddRefAndReturn(new SharpCppRbfxApplication(context));
}

SHARPCPP_EXPORT ret_t<void> sharpcpp_rbfx_Application_ConnectDirector(
    arg_t<Urho3D::Application*> self,
    void* getType,
    void* isInstanceOf,
    void* setup,
    void* start,
    void* stop)
{
    if (auto* application = dynamic_cast<SharpCppRbfxApplication*>(self))
    {
        application->ConnectDirector(getType, isInstanceOf, setup, start, stop);
    }
}

SHARPCPP_EXPORT ret_t<void> sharpcpp_rbfx_Application_SetEngineParameterBool(arg_t<Urho3D::Application*> self, const char* name, arg_t<bool> value)
{
    if (auto* application = dynamic_cast<SharpCppRbfxApplication*>(self))
    {
        application->SetEngineParameter(name, value);
    }
}

SHARPCPP_EXPORT ret_t<void> sharpcpp_rbfx_Application_SetEngineParameterInt(arg_t<Urho3D::Application*> self, const char* name, arg_t<int> value)
{
    if (auto* application = dynamic_cast<SharpCppRbfxApplication*>(self))
    {
        application->SetEngineParameter(name, value);
    }
}

SHARPCPP_EXPORT ret_t<void> sharpcpp_rbfx_Application_SetEngineParameterString(arg_t<Urho3D::Application*> self, const char* name, const char* value)
{
    if (auto* application = dynamic_cast<SharpCppRbfxApplication*>(self))
    {
        application->SetEngineParameter(name, value);
    }
}

SHARPCPP_EXPORT ret_t<void> sharpcpp_rbfx_Application_SubscribeToUpdate(arg_t<Urho3D::Application*> self, UpdateCallback callback, void* context)
{
    if (self == nullptr || callback == nullptr)
    {
        return;
    }

    self->SubscribeToEvent(Urho3D::E_UPDATE, [callback, context](Urho3D::StringHash, Urho3D::VariantMap& eventData) {
        callback(context, eventData[Urho3D::Update::P_TIMESTEP].GetFloat());
    });
}

SHARPCPP_EXPORT ret_t<Urho3D::Scene*> sharpcpp_rbfx_Scene_Create(arg_t<Urho3D::Context*> context)
{
    return AddRefAndReturn(new Urho3D::Scene(context));
}

SHARPCPP_EXPORT ret_t<Urho3D::Node*> sharpcpp_rbfx_Node_CreateChild(arg_t<Urho3D::Node*> self, const char* name)
{
    const ea::string childName{name != nullptr ? name : ""};
    return self != nullptr ? self->CreateChild(childName) : nullptr;
}

SHARPCPP_EXPORT ret_t<Urho3D::Component*> sharpcpp_rbfx_Node_CreateComponent(arg_t<Urho3D::Node*> self, arg_t<Urho3D::StringHash> type, arg_t<unsigned> id)
{
    ParameterMarshaller<Urho3D::StringHash> typeMarshaller;
    return self != nullptr ? self->CreateComponent(typeMarshaller.ConvertToCpp(type), id) : nullptr;
}

SHARPCPP_EXPORT ret_t<Urho3D::Viewport*> sharpcpp_rbfx_Viewport_Create(arg_t<Urho3D::Context*> context)
{
    return AddRefAndReturn(new Urho3D::Viewport(context));
}

SHARPCPP_EXPORT ret_t<Urho3D::Scene*> sharpcpp_rbfx_Viewport_GetScene(arg_t<Urho3D::Viewport*> self)
{
    return self != nullptr ? self->GetScene() : nullptr;
}

SHARPCPP_EXPORT ret_t<void> sharpcpp_rbfx_Viewport_SetScene(arg_t<Urho3D::Viewport*> self, arg_t<Urho3D::Scene*> scene)
{
    if (self != nullptr)
    {
        self->SetScene(scene);
    }
}

SHARPCPP_EXPORT ret_t<Urho3D::Camera*> sharpcpp_rbfx_Viewport_GetCamera(arg_t<Urho3D::Viewport*> self)
{
    return self != nullptr ? self->GetCamera() : nullptr;
}

SHARPCPP_EXPORT ret_t<void> sharpcpp_rbfx_Viewport_SetCamera(arg_t<Urho3D::Viewport*> self, arg_t<Urho3D::Camera*> camera)
{
    if (self != nullptr)
    {
        self->SetCamera(camera);
    }
}

SHARPCPP_EXPORT ret_t<Urho3D::Resource*> sharpcpp_rbfx_ResourceCache_GetResource(
    arg_t<Urho3D::ResourceCache*> self,
    arg_t<Urho3D::StringHash> type,
    const char* name,
    arg_t<bool> sendEventOnFailure)
{
    const ea::string resourceName{name != nullptr ? name : ""};
    ParameterMarshaller<Urho3D::StringHash> typeMarshaller;
    return self != nullptr ? self->GetResource(typeMarshaller.ConvertToCpp(type), resourceName, sendEventOnFailure) : nullptr;
}

SHARPCPP_EXPORT ret_t<void> sharpcpp_rbfx_Renderer_SetViewport(arg_t<Urho3D::Renderer*> self, arg_t<unsigned> index, arg_t<Urho3D::Viewport*> viewport)
{
    if (self != nullptr)
    {
        self->SetViewport(index, viewport);
    }
}

SHARPCPP_EXPORT ret_t<void> sharpcpp_rbfx_Renderer_SetDefaultZoneFogColor(arg_t<Urho3D::Renderer*> self, const Urho3D::Color* color)
{
    if (self != nullptr && self->GetDefaultZone() != nullptr && color != nullptr)
    {
        self->GetDefaultZone()->SetFogColor(*color);
    }
}

SHARPCPP_EXPORT ret_t<void> sharpcpp_rbfx_StaticModel_SetModel(arg_t<Urho3D::StaticModel*> self, arg_t<Urho3D::Model*> model)
{
    if (self != nullptr)
    {
        self->SetModel(model);
    }
}

SHARPCPP_EXPORT ret_t<bool> sharpcpp_rbfx_StaticModel_SetMaterial(arg_t<Urho3D::StaticModel*> self, arg_t<unsigned> index, arg_t<Urho3D::Material*> material)
{
    return self != nullptr ? self->SetMaterial(index, material) : false;
}

SHARPCPP_EXPORT ret_t<bool> sharpcpp_rbfx_RmlUI_LoadFont(arg_t<Urho3D::RmlUI*> self, arg_t<ea::string*> resourceName, arg_t<bool> fallback)
{
    return self != nullptr && resourceName != nullptr ? self->LoadFont(*resourceName, fallback) : false;
}

SHARPCPP_EXPORT ret_t<Rml::Context*> sharpcpp_rbfx_RmlUI_GetRmlContext(arg_t<Urho3D::RmlUI*> self)
{
    return self != nullptr ? self->GetRmlContext() : nullptr;
}

SHARPCPP_EXPORT ret_t<Rml::DataModelConstructor*> sharpcpp_rbfx_Rml_Context_CreateDataModel(arg_t<Rml::Context*> self, arg_t<Rml::String*> name)
{
    if (self == nullptr || name == nullptr)
    {
        return nullptr;
    }

    return new Rml::DataModelConstructor(self->CreateDataModel(*name));
}

SHARPCPP_EXPORT ret_t<Rml::ElementDocument*> sharpcpp_rbfx_Rml_Context_LoadDocumentFromMemory(
    arg_t<Rml::Context*> self,
    arg_t<Rml::String*> documentRml,
    arg_t<Rml::String*> sourceUrl)
{
    return self != nullptr && documentRml != nullptr && sourceUrl != nullptr
        ? self->LoadDocumentFromMemory(*documentRml, *sourceUrl)
        : nullptr;
}

SHARPCPP_EXPORT ret_t<void> sharpcpp_rbfx_Rml_ElementDocument_Show(arg_t<Rml::ElementDocument*> self)
{
    if (self != nullptr)
    {
        self->Show();
    }
}

SHARPCPP_EXPORT ret_t<void> sharpcpp_rbfx_ImGui_SetNextWindowPos(const Urho3D::Vector2* pos, arg_t<int> cond)
{
    if (pos != nullptr)
    {
        ImGui::SetNextWindowPos(ImVec2{pos->x_, pos->y_}, static_cast<ImGuiCond>(cond));
    }
}

SHARPCPP_EXPORT ret_t<void> sharpcpp_rbfx_ImGui_PushTextWrapPos(arg_t<float> wrapLocalPosX)
{
    ImGui::PushTextWrapPos(wrapLocalPosX);
}

SHARPCPP_EXPORT ret_t<void> sharpcpp_rbfx_ImGui_PopTextWrapPos()
{
    ImGui::PopTextWrapPos();
}

SHARPCPP_EXPORT ret_t<bool> sharpcpp_rbfx_ImGui_Button(const char* label)
{
    return ImGui::Button(label != nullptr ? label : "");
}

SHARPCPP_EXPORT ret_t<bool> sharpcpp_rbfx_ImGui_BeginItemTooltip()
{
    return ImGui::BeginItemTooltip();
}

SHARPCPP_EXPORT ret_t<void> sharpcpp_rbfx_ImGui_EndTooltip()
{
    ImGui::EndTooltip();
}
}
