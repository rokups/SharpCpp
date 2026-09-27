set(SHARPCPP_E2E_GENERATED_ROOT "${SHARPCPP_GENERATED_ROOT}/sharpcpp-e2e")
set(SHARPCPP_E2E_GENERATED_STAMP "${SHARPCPP_E2E_GENERATED_ROOT}/generate.stamp")
set(SHARPCPP_E2E_FILE_STEM "SharpCppTestsE2E")
set(SHARPCPP_E2E_GENERATOR_LOG_FILE "${SHARPCPP_E2E_GENERATED_ROOT}/generator.log")
set(SHARPCPP_E2E_UNITY_PREFIX "${SHARPCPP_E2E_GENERATED_ROOT}/unity/SharpCppTestsE2E.")
set(SHARPCPP_E2E_UNITY_SHARD_COUNT 8)
set(SHARPCPP_E2E_UNITY_SOURCES "")
math(EXPR SHARPCPP_E2E_LAST_UNITY_SHARD "${SHARPCPP_E2E_UNITY_SHARD_COUNT} - 1")
foreach(shard RANGE ${SHARPCPP_E2E_LAST_UNITY_SHARD})
  list(APPEND SHARPCPP_E2E_UNITY_SOURCES "${SHARPCPP_E2E_UNITY_PREFIX}${shard}.cpp")
endforeach()
set(SHARPCPP_E2E_GENERATOR_DLL
  "${SHARPCPP_MANAGED_OUTPUT_ROOT}/$<CONFIG>/SharpCpp.Tests/SharpCpp.Tests.E2E.Generator/SharpCpp.Tests.E2E.Generator.dll")

file(GLOB_RECURSE SHARPCPP_E2E_INPUT_HEADERS CONFIGURE_DEPENDS
  "${CMAKE_CURRENT_SOURCE_DIR}/*/*.hpp"
  "${CMAKE_CURRENT_SOURCE_DIR}/*/*.h")
file(GLOB_RECURSE SHARPCPP_E2E_GENERATOR_INPUTS CONFIGURE_DEPENDS
  "${SharpCpp_SOURCE_DIR}/src/SharpCpp.Model/*.cs"
  "${SharpCpp_SOURCE_DIR}/src/SharpCpp.Metadata/*.cs"
  "${SharpCpp_SOURCE_DIR}/src/SharpCpp.Parsing/*.cs"
  "${SharpCpp_SOURCE_DIR}/src/SharpCpp.Pipeline/*.cs"
  "${SharpCpp_SOURCE_DIR}/src/SharpCpp.Rendering/*.cs"
  "${SharpCpp_SOURCE_DIR}/src/SharpCpp.Toolchain/*.cs"
  "${SharpCpp_SOURCE_DIR}/tests/SharpCpp.Tests.E2E.Generator/*.cs"
  "${SharpCpp_SOURCE_DIR}/include/SharpCpp/*.hpp"
  "${SharpCpp_SOURCE_DIR}/include/SharpCpp/*.h")

# Renames keep file timestamps, so the generation also depends on the input set itself.
set(SHARPCPP_E2E_INPUT_MANIFEST "${CMAKE_CURRENT_BINARY_DIR}/sharpcpp-e2e-inputs.txt")
string(REPLACE ";" "\n" SHARPCPP_E2E_INPUT_MANIFEST_CONTENT
  "${SHARPCPP_E2E_INPUT_HEADERS};${SHARPCPP_E2E_GENERATOR_INPUTS}")
file(CONFIGURE OUTPUT "${SHARPCPP_E2E_INPUT_MANIFEST}" CONTENT "${SHARPCPP_E2E_INPUT_MANIFEST_CONTENT}\n")

add_custom_command(
  OUTPUT "${SHARPCPP_E2E_GENERATED_STAMP}"
  COMMAND "${CMAKE_COMMAND}"
          "-DGENERATOR_DLL=${SHARPCPP_E2E_GENERATOR_DLL}"
          "-DGENERATED_STAMP=${SHARPCPP_E2E_GENERATED_STAMP}"
          "-DGENERATED_DIR=${SHARPCPP_E2E_GENERATED_ROOT}"
          -P "${SharpCpp_SOURCE_DIR}/cmake/SharpCppGeneratedStaleness.cmake"
  COMMAND "${CMAKE_COMMAND}" -E make_directory "${SHARPCPP_E2E_GENERATED_ROOT}"
  COMMAND "${CMAKE_COMMAND}" -E env
          "SHARPCPP_CLANG_RESOURCE_DIRECTORY=${SHARPCPP_CLANG_RESOURCE_DIRECTORY}"
          "${DOTNET_EXECUTABLE}" "${SHARPCPP_E2E_GENERATOR_DLL}"
          --generated-root "${SHARPCPP_E2E_GENERATED_ROOT}"
          --project-root "${SharpCpp_SOURCE_DIR}"
          --file-stem "${SHARPCPP_E2E_FILE_STEM}"
          --compiler "${CMAKE_CXX_COMPILER}"
          --log-file "${SHARPCPP_E2E_GENERATOR_LOG_FILE}"
  COMMAND "${CMAKE_COMMAND}"
          "-DGENERATED_DIR=${SHARPCPP_E2E_GENERATED_ROOT}"
          "-DOUTPUT_PREFIX=${SHARPCPP_E2E_UNITY_PREFIX}"
          "-DSHARD_COUNT=${SHARPCPP_E2E_UNITY_SHARD_COUNT}"
          -P "${SharpCpp_SOURCE_DIR}/cmake/SharpCppWriteUnitySources.cmake"
  COMMAND "${CMAKE_COMMAND}" -E touch "${SHARPCPP_E2E_GENERATED_STAMP}"
  BYPRODUCTS ${SHARPCPP_E2E_UNITY_SOURCES}
  DEPENDS "${SHARPCPP_E2E_GENERATOR_DLL}" "${SHARPCPP_E2E_INPUT_MANIFEST}" ${SHARPCPP_E2E_INPUT_HEADERS} ${SHARPCPP_E2E_GENERATOR_INPUTS}
  WORKING_DIRECTORY "${SharpCpp_SOURCE_DIR}"
  VERBATIM
  USES_TERMINAL)

add_custom_target(sharpcpp-generate-e2e DEPENDS "${SHARPCPP_E2E_GENERATED_STAMP}")

# The generator's output set varies with the fixtures; the fixed unity shards include it, so
# neither configure nor the build graph depends on individual generated file names.
add_library(SharpCppTestsE2E_Native SHARED ${SHARPCPP_E2E_UNITY_SOURCES})
add_dependencies(SharpCppTestsE2E_Native sharpcpp-generate-e2e)
target_compile_features(SharpCppTestsE2E_Native PRIVATE cxx_std_20)
set_target_properties(
  SharpCppTestsE2E_Native
  PROPERTIES
    CXX_SCAN_FOR_MODULES OFF)
target_include_directories(
  SharpCppTestsE2E_Native
  PRIVATE
    "${SHARPCPP_E2E_GENERATED_ROOT}"
    "${CMAKE_CURRENT_SOURCE_DIR}")
target_link_libraries(SharpCppTestsE2E_Native PRIVATE SharpCpp::NativeSupport)
target_compile_options(
  SharpCppTestsE2E_Native
  PRIVATE $<$<COMPILE_LANG_AND_ID:CXX,GNU,Clang,AppleClang>:-Wno-attributes>)
sharpcpp_set_native_output(SharpCppTestsE2E_Native)
