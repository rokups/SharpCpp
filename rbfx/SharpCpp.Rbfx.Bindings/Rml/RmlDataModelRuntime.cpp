#include "SharpCpp/Export.hpp"
#include "SharpCpp/Marshaller.hpp"

#include <RmlUi/Core.h>

using namespace SharpCpp;

namespace
{
using BindFuncCallback = void (*)(void*, Rml::Variant*);
using BindEventCallback = void (*)(void*, Rml::DataModelHandle*, Rml::Event*, const Rml::VariantList*);

Rml::String MakeRmlString(const char* value, std::size_t length)
{
    return value != nullptr ? Rml::String(value, length) : Rml::String();
}
}

extern "C"
{
SHARPCPP_EXPORT ret_t<bool> sharpcpp_rml_DataModelConstructor_BindFunc(
    arg_t<Rml::DataModelConstructor*> self,
    arg_t<Rml::String*> name,
    BindFuncCallback getFunc,
    BindFuncCallback setFunc,
    void* context)
{
    if (self == nullptr || name == nullptr || getFunc == nullptr)
    {
        return false;
    }

    Rml::DataGetFunc get = [getFunc, context](Rml::Variant& value) {
        getFunc(context, &value);
    };
    Rml::DataSetFunc set;
    if (setFunc != nullptr)
    {
        set = [setFunc, context](const Rml::Variant& value) {
            setFunc(context, const_cast<Rml::Variant*>(&value));
        };
    }

    return self->BindFunc(*name, std::move(get), std::move(set));
}

SHARPCPP_EXPORT ret_t<bool> sharpcpp_rml_DataModelConstructor_BindEventCallback(
    arg_t<Rml::DataModelConstructor*> self,
    arg_t<Rml::String*> name,
    BindEventCallback eventFunc,
    void* context)
{
    if (self == nullptr || name == nullptr || eventFunc == nullptr)
    {
        return false;
    }

    Rml::DataEventFunc callback = [eventFunc, context](Rml::DataModelHandle handle, Rml::Event& event, const Rml::VariantList& arguments) {
        auto* heapHandle = new Rml::DataModelHandle(handle);
        eventFunc(context, heapHandle, &event, &arguments);
    };

    return self->BindEventCallback(*name, std::move(callback));
}

SHARPCPP_EXPORT ret_t<Rml::DataModelHandle*> sharpcpp_rml_DataModelConstructor_GetModelHandle(arg_t<Rml::DataModelConstructor*> self)
{
    if (self == nullptr)
    {
        return nullptr;
    }

    return new Rml::DataModelHandle(self->GetModelHandle());
}

SHARPCPP_EXPORT ret_t<bool> sharpcpp_rml_DataModelHandle_IsVariableDirty(
    arg_t<Rml::DataModelHandle*> self,
    const char* variableName,
    std::size_t variableNameLength)
{
    if (self == nullptr)
    {
        return false;
    }

    const auto name = MakeRmlString(variableName, variableNameLength);
    return self->IsVariableDirty(name);
}

SHARPCPP_EXPORT ret_t<void> sharpcpp_rml_DataModelHandle_DirtyVariable(
    arg_t<Rml::DataModelHandle*> self,
    const char* variableName,
    std::size_t variableNameLength)
{
    if (self == nullptr)
    {
        return;
    }

    const auto name = MakeRmlString(variableName, variableNameLength);
    self->DirtyVariable(name);
}

SHARPCPP_EXPORT ret_t<void> sharpcpp_rml_Variant_SetString(arg_t<Rml::Variant*> self, const char* value, std::size_t valueLength)
{
    if (self == nullptr)
    {
        return;
    }

    *self = MakeRmlString(value, valueLength);
}

SHARPCPP_EXPORT ret_t<Rml::String*> sharpcpp_rml_Variant_GetString(arg_t<Rml::Variant*> self)
{
    if (self == nullptr)
    {
        return nullptr;
    }

    return new Rml::String(self->Get<Rml::String>());
}

SHARPCPP_EXPORT ret_t<std::size_t> sharpcpp_rml_VariantList_Count(arg_t<const Rml::VariantList*> self)
{
    return self != nullptr ? self->size() : 0;
}

SHARPCPP_EXPORT ret_t<const Rml::Variant*> sharpcpp_rml_VariantList_Get(arg_t<const Rml::VariantList*> self, arg_t<std::size_t> index)
{
    if (self == nullptr || index >= self->size())
    {
        return nullptr;
    }

    return &(*self)[index];
}
}
