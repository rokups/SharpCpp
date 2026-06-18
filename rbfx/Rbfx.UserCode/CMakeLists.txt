set(RBFX_USERCODE_GENERATOR_PROJECT "${CMAKE_CURRENT_SOURCE_DIR}/Rbfx.UserCode.Generator/Rbfx.UserCode.Generator.csproj")
set(RBFX_USERCODE_BINDINGS_PROJECT "${CMAKE_CURRENT_SOURCE_DIR}/Rbfx.UserCode.Bindings/Rbfx.UserCode.Bindings.csproj")
set(RBFX_USERCODE_NATIVE_SOURCE "${CMAKE_CURRENT_SOURCE_DIR}/src/RotatingComponent.cpp")
set(RBFX_USERCODE_GENERATED_ROOT "${CMAKE_BINARY_DIR}/generated/rbfx-usercode")
set(RBFX_USERCODE_GENERATED_STAMP "${RBFX_USERCODE_GENERATED_ROOT}/generate.stamp")
set(RBFX_USERCODE_GENERATOR_LOG_FILE "${RBFX_USERCODE_GENERATED_ROOT}/generator.log")
set(RBFX_USERCODE_FILE_STEM "Rbfx.UserCode")

set_property(GLOBAL PROPERTY SHARPCPP_RBFX_USERCODE_GENERATED_DIR "${RBFX_USERCODE_GENERATED_ROOT}")
set_property(GLOBAL PROPERTY SHARPCPP_RBFX_USERCODE_GENERATED_STAMP "${RBFX_USERCODE_GENERATED_STAMP}")

file(GLOB_RECURSE RBFX_USERCODE_INPUT_HEADERS CONFIGURE_DEPENDS
  "${CMAKE_CURRENT_SOURCE_DIR}/include/*.h"
  "${CMAKE_CURRENT_SOURCE_DIR}/include/*.hpp")

sharpcpp_add_dotnet_build_target(
  sharpcpp-rbfx-usercode-generator
  "${RBFX_USERCODE_GENERATOR_PROJECT}"
  DEPENDS sharpcpp-model sharpcpp-metadata sharpcpp-parsing sharpcpp-pipeline sharpcpp-rendering sharpcpp-support)

get_target_property(RBFX_USERCODE_GENERATOR_BUILD_STAMP sharpcpp-rbfx-usercode-generator SHARPCPP_DOTNET_BUILD_STAMP)

add_custom_command(
  OUTPUT "${RBFX_USERCODE_GENERATED_STAMP}"
  COMMAND "${CMAKE_COMMAND}" -E make_directory "$<SHELL_PATH:${RBFX_USERCODE_GENERATED_ROOT}>"
  COMMAND "${DOTNET_EXECUTABLE}" "$<SHELL_PATH:${SHARPCPP_BIN_DIR}/Rbfx.UserCode.Generator.dll>"
          --sdk-root "$<SHELL_PATH:${RBFX_SDK_ROOT}>"
          --flags-file "$<SHELL_PATH:${RBFX_SDK_FLAGS_FILE}>"
          --user-code-root "$<SHELL_PATH:${CMAKE_CURRENT_SOURCE_DIR}>"
          --generated-root "$<SHELL_PATH:${RBFX_USERCODE_GENERATED_ROOT}>"
          --import-binding-metadata "$<SHELL_PATH:${RBFX_METADATA_FILE}>"
          --log-file "$<SHELL_PATH:${RBFX_USERCODE_GENERATOR_LOG_FILE}>"
  COMMAND "${CMAKE_COMMAND}" -E touch "${RBFX_USERCODE_GENERATED_STAMP}"
  DEPENDS
    "${RBFX_USERCODE_GENERATOR_BUILD_STAMP}"
    "${RBFX_SDK_FLAGS_FILE}"
    "${RBFX_GENERATED_STAMP}"
    "${RBFX_USERCODE_NATIVE_SOURCE}"
    ${RBFX_USERCODE_INPUT_HEADERS}
    ${RBFX_SDK_INPUT_HEADERS}
  WORKING_DIRECTORY "${CMAKE_SOURCE_DIR}"
  VERBATIM
  USES_TERMINAL)

add_custom_target(
  sharpcpp-generate-rbfx-usercode
  DEPENDS "${RBFX_USERCODE_GENERATED_STAMP}")

file(MAKE_DIRECTORY "${RBFX_USERCODE_GENERATED_ROOT}")
file(GLOB RBFX_USERCODE_GENERATED_CPP_FILES CONFIGURE_DEPENDS "${RBFX_USERCODE_GENERATED_ROOT}/*.g.cpp")
if(NOT RBFX_USERCODE_GENERATED_CPP_FILES)
  file(WRITE "${RBFX_USERCODE_GENERATED_ROOT}/.stub.cpp" "// stub - replaced by generator\n")
  set(RBFX_USERCODE_GENERATED_CPP_FILES "${RBFX_USERCODE_GENERATED_ROOT}/.stub.cpp")
endif()
set_source_files_properties(${RBFX_USERCODE_GENERATED_CPP_FILES} PROPERTIES OBJECT_DEPENDS "${RBFX_USERCODE_GENERATED_STAMP}")

add_library(SharpCppRbfxUserCode_Native SHARED ${RBFX_USERCODE_GENERATED_CPP_FILES} "${RBFX_USERCODE_NATIVE_SOURCE}")
add_dependencies(SharpCppRbfxUserCode_Native sharpcpp-generate-rbfx-usercode sharpcpp-rbfx-runtime)
target_compile_features(SharpCppRbfxUserCode_Native PRIVATE cxx_std_20)
set_target_properties(SharpCppRbfxUserCode_Native PROPERTIES CXX_SCAN_FOR_MODULES OFF UNITY_BUILD ON UNITY_BUILD_BATCH_SIZE 100 OUTPUT_NAME "SharpCpp.Rbfx.UserCode.Native")
target_include_directories(
  SharpCppRbfxUserCode_Native
  PRIVATE
    "${RBFX_USERCODE_GENERATED_ROOT}"
    "${CMAKE_CURRENT_SOURCE_DIR}/include")
target_link_libraries(SharpCppRbfxUserCode_Native PRIVATE sharpcpp-native-support ${RBFX_URHO3D_TARGET})
target_compile_options(SharpCppRbfxUserCode_Native PRIVATE $<$<COMPILE_LANG_AND_ID:CXX,GNU,Clang,AppleClang>:-Wno-attributes>)
sharpcpp_set_binary_output(SharpCppRbfxUserCode_Native)
set_target_properties(SharpCppRbfxUserCode_Native PROPERTIES BUILD_RPATH "$ORIGIN" INSTALL_RPATH "$ORIGIN")

sharpcpp_add_dotnet_build_target(
  sharpcpp-rbfx-usercode-managed
  "${RBFX_USERCODE_BINDINGS_PROJECT}"
  DEPENDS sharpcpp-generate-rbfx-usercode SharpCppRbfxUserCode_Native sharpcpp-support sharpcpp-rbfx-managed
  PROPERTIES "-p:SharpCppUserCodeGeneratedDir=${RBFX_USERCODE_GENERATED_ROOT}")

add_custom_target(
  sharpcpp-rbfx-usercode
  DEPENDS sharpcpp-rbfx-usercode-managed)
