set(RBFX_APPLICATION_PROJECT "${CMAKE_CURRENT_SOURCE_DIR}/Rbfx.Application.csproj")

sharpcpp_add_dotnet_build_target(
  sharpcpp-rbfx-application
  "${RBFX_APPLICATION_PROJECT}"
  DEPENDS sharpcpp-rbfx-usercode-managed sharpcpp-rbfx-managed)
