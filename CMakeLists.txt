cmake_minimum_required(VERSION 3.28)

project(SharpCppV2 LANGUAGES CXX)

include(CTest)

find_program(DOTNET_EXECUTABLE dotnet REQUIRED)

function(sharpcpp2_add_dotnet_build_target target_name project_path)
  add_custom_target(
    ${target_name}
    COMMAND "${DOTNET_EXECUTABLE}" build "${project_path}" --configuration $<CONFIG> --nologo --verbosity minimal
    WORKING_DIRECTORY "${CMAKE_SOURCE_DIR}"
    VERBATIM
    USES_TERMINAL)
endfunction()

set(SHARPCPP2_BIN_DIR "${CMAKE_BINARY_DIR}/bin/$<CONFIG>")

add_library(sharpcpp2-native-support INTERFACE)
target_compile_features(sharpcpp2-native-support INTERFACE cxx_std_20)
target_include_directories(sharpcpp2-native-support INTERFACE "${CMAKE_SOURCE_DIR}/include")

sharpcpp2_add_dotnet_build_target(sharpcpp2-model "${CMAKE_SOURCE_DIR}/src/SharpCpp.Model/SharpCpp.Model.csproj")
sharpcpp2_add_dotnet_build_target(sharpcpp2-metadata "${CMAKE_SOURCE_DIR}/src/SharpCpp.Metadata/SharpCpp.Metadata.csproj")
sharpcpp2_add_dotnet_build_target(sharpcpp2-parsing "${CMAKE_SOURCE_DIR}/src/SharpCpp.Parsing/SharpCpp.Parsing.csproj")
sharpcpp2_add_dotnet_build_target(sharpcpp2-pipeline "${CMAKE_SOURCE_DIR}/src/SharpCpp.Pipeline/SharpCpp.Pipeline.csproj")
sharpcpp2_add_dotnet_build_target(sharpcpp2-rendering "${CMAKE_SOURCE_DIR}/src/SharpCpp.Rendering/SharpCpp.Rendering.csproj")
sharpcpp2_add_dotnet_build_target(sharpcpp2-support "${CMAKE_SOURCE_DIR}/src/SharpCpp.Support/SharpCpp.Support.csproj")
sharpcpp2_add_dotnet_build_target(sharpcpp2-tool "${CMAKE_SOURCE_DIR}/src/SharpCpp.Tool/SharpCpp.Tool.csproj")
sharpcpp2_add_dotnet_build_target(sharpcpp2-tests-model "${CMAKE_SOURCE_DIR}/tests/SharpCpp.Tests.Model/SharpCpp.Tests.Model.csproj")
sharpcpp2_add_dotnet_build_target(sharpcpp2-tests-metadata "${CMAKE_SOURCE_DIR}/tests/SharpCpp.Tests.Metadata/SharpCpp.Tests.Metadata.csproj")

add_custom_target(
  sharpcpp2-managed
  DEPENDS
    sharpcpp2-model
    sharpcpp2-metadata
    sharpcpp2-parsing
    sharpcpp2-pipeline
    sharpcpp2-rendering
    sharpcpp2-support
    sharpcpp2-tool)

add_custom_target(
  sharpcpp2-tests
  DEPENDS
    sharpcpp2-managed
    sharpcpp2-tests-model
    sharpcpp2-tests-metadata)
add_custom_target(sharpcpp2-all ALL DEPENDS sharpcpp2-managed)

if(BUILD_TESTING)
  add_test(
    NAME sharpcpp2.tests.model
    COMMAND "${DOTNET_EXECUTABLE}" test "${CMAKE_SOURCE_DIR}/tests/SharpCpp.Tests.Model/SharpCpp.Tests.Model.csproj"
            --configuration $<CONFIG>
            --no-build
            --no-restore
            --logger "console;verbosity=minimal")

  add_test(
    NAME sharpcpp2.tests.metadata
    COMMAND "${DOTNET_EXECUTABLE}" test "${CMAKE_SOURCE_DIR}/tests/SharpCpp.Tests.Metadata/SharpCpp.Tests.Metadata.csproj"
            --configuration $<CONFIG>
            --no-build
            --no-restore
            --logger "console;verbosity=minimal")
endif()
