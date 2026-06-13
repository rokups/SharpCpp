set(RBFX_APPLICATION_PROJECT "${CMAKE_CURRENT_SOURCE_DIR}/Rbfx.Application.csproj")

get_property(RBFX_APPLICATION_USERCODE_GENERATED_DIR GLOBAL PROPERTY SHARPCPP_RBFX_USERCODE_GENERATED_DIR)

sharpcpp_add_dotnet_build_target(
  sharpcpp-rbfx-application
  "${RBFX_APPLICATION_PROJECT}"
  DEPENDS sharpcpp-rbfx-usercode-managed sharpcpp-rbfx-managed
  PROPERTIES "-p:SharpCppUserCodeGeneratedDir=${RBFX_APPLICATION_USERCODE_GENERATED_DIR}")
