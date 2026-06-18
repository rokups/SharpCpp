set(_rbfx_default_sdk_root "${CMAKE_SOURCE_DIR}/out/rbfx-sdk")
if(DEFINED ENV{RBFX_SDK_ROOT} AND EXISTS "$ENV{RBFX_SDK_ROOT}")
  set(_rbfx_default_sdk_root "$ENV{RBFX_SDK_ROOT}")
endif()

set(RBFX_SDK_ROOT "${_rbfx_default_sdk_root}" CACHE PATH "Path to the installed rbfx SDK root.")
set(RBFX_SDK_PARSE_LANGUAGE_STANDARD "gnu++20" CACHE STRING "C++ standard used by SharpCpp.Rbfx.Generator when parsing the installed rbfx SDK.")

if(RBFX_SDK_ROOT STREQUAL "" OR NOT EXISTS "${RBFX_SDK_ROOT}/share/Urho3D/CMake/Urho3DConfig.cmake")
  message(FATAL_ERROR "Set RBFX_SDK_ROOT to the installed rbfx SDK root before enabling rbfx bindings.")
endif()

if(NOT EXISTS "${RBFX_SDK_ROOT}/include/Urho3D")
  message(FATAL_ERROR "rbfx SDK include directory was not found at '${RBFX_SDK_ROOT}/include/Urho3D'.")
endif()

if(DEFINED CMAKE_MAP_IMPORTED_CONFIG_RELEASE)
  if(NOT "RelWithDebInfo" IN_LIST CMAKE_MAP_IMPORTED_CONFIG_RELEASE)
    list(APPEND CMAKE_MAP_IMPORTED_CONFIG_RELEASE RelWithDebInfo)
  endif()
else()
  set(CMAKE_MAP_IMPORTED_CONFIG_RELEASE RelWithDebInfo)
endif()

if(DEFINED CMAKE_MAP_IMPORTED_CONFIG_MINSIZEREL)
  if(NOT "RelWithDebInfo" IN_LIST CMAKE_MAP_IMPORTED_CONFIG_MINSIZEREL)
    list(APPEND CMAKE_MAP_IMPORTED_CONFIG_MINSIZEREL RelWithDebInfo)
  endif()
else()
  set(CMAKE_MAP_IMPORTED_CONFIG_MINSIZEREL RelWithDebInfo)
endif()

list(PREPEND CMAKE_PREFIX_PATH "${RBFX_SDK_ROOT}")
set(BUILD_SHARED_LIBS ON)
set(CMAKE_DISABLE_FIND_PACKAGE_TBB TRUE)
set(CMAKE_DISABLE_FIND_PACKAGE_Vulkan TRUE)
find_package(Urho3D 0.0.0 CONFIG REQUIRED)

set(RBFX_URHO3D_TARGET Urho3D)

macro(rbfx_collect_target_property_recursive output target property)
  get_target_property(_values ${target} ${property})
  if(_values)
    list(APPEND ${output} ${_values})
  endif()
  get_target_property(_link_values ${target} INTERFACE_LINK_LIBRARIES)
  if(_link_values)
    foreach(_lib ${_link_values})
      if(TARGET ${_lib} AND NOT "${_lib}" IN_LIST _rbfx_checked_libraries)
        list(APPEND _rbfx_checked_libraries "${_lib}")
        rbfx_collect_target_property_recursive(${output} ${_lib} ${property})
      endif()
    endforeach()
  endif()
endmacro()

set(_rbfx_checked_libraries)
rbfx_collect_target_property_recursive(RBFX_URHO3D_INCLUDE_DIRECTORIES ${RBFX_URHO3D_TARGET} INTERFACE_INCLUDE_DIRECTORIES)
set(_rbfx_checked_libraries)
rbfx_collect_target_property_recursive(RBFX_URHO3D_COMPILE_DEFINITIONS ${RBFX_URHO3D_TARGET} INTERFACE_COMPILE_DEFINITIONS)
set(_rbfx_checked_libraries)
rbfx_collect_target_property_recursive(RBFX_URHO3D_COMPILE_OPTIONS ${RBFX_URHO3D_TARGET} INTERFACE_COMPILE_OPTIONS)

list(REMOVE_DUPLICATES RBFX_URHO3D_INCLUDE_DIRECTORIES)
list(REMOVE_DUPLICATES RBFX_URHO3D_COMPILE_DEFINITIONS)
list(REMOVE_DUPLICATES RBFX_URHO3D_COMPILE_OPTIONS)

set(RBFX_GENERATOR_PROJECT "${CMAKE_CURRENT_SOURCE_DIR}/SharpCpp.Rbfx.Generator/SharpCpp.Rbfx.Generator.csproj")
set(RBFX_BINDINGS_PROJECT "${CMAKE_CURRENT_SOURCE_DIR}/SharpCpp.Rbfx.Bindings/SharpCpp.Rbfx.Bindings.csproj")
set(RBFX_E2E_TESTS_PROJECT "${CMAKE_CURRENT_SOURCE_DIR}/SharpCpp.Rbfx.Tests.E2E/SharpCpp.Rbfx.Tests.E2E.csproj")
set(RBFX_GENERATED_ROOT "${CMAKE_BINARY_DIR}/generated/rbfx")
set(RBFX_FILE_STEM "SharpCpp.Rbfx")
set(RBFX_GENERATED_STAMP "${RBFX_GENERATED_ROOT}/generate.stamp")
set(RBFX_METADATA_FILE "${RBFX_GENERATED_ROOT}/${RBFX_FILE_STEM}.Bindings.json")
set(RBFX_SDK_FLAGS_FILE "${CMAKE_BINARY_DIR}/rbfx-sdk-flags/$<CONFIG>/urho3d-sdk-flags.txt")
set(RBFX_GENERATOR_LOG_FILE "${RBFX_GENERATED_ROOT}/generator.log")
set(RBFX_RUNTIME_DEPENDENCIES_SCRIPT "${CMAKE_CURRENT_BINARY_DIR}/CopyRbfxRuntimeDependencies.cmake")
set(RBFX_TEST_RUNSETTINGS_FILE "${CMAKE_BINARY_DIR}/bin/$<CONFIG>/SharpCpp.Rbfx.Tests.runsettings")

if(CMAKE_SYSTEM_NAME STREQUAL "Linux")
  string(CONCAT _rbfx_test_runsettings_content
    "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
    "<RunSettings>\n"
    "  <RunConfiguration>\n"
    "    <EnvironmentVariables>\n"
    "      <LD_LIBRARY_PATH>${CMAKE_BINARY_DIR}/bin/$<CONFIG>/</LD_LIBRARY_PATH>\n"
    "      <LD_PRELOAD>${CMAKE_BINARY_DIR}/bin/$<CONFIG>/libUrho3D.so</LD_PRELOAD>\n"
    "    </EnvironmentVariables>\n"
    "  </RunConfiguration>\n"
    "</RunSettings>\n")
else()
  string(CONCAT _rbfx_test_runsettings_content
    "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
    "<RunSettings>\n"
    "</RunSettings>\n")
endif()

file(GENERATE
  OUTPUT "${RBFX_TEST_RUNSETTINGS_FILE}"
  CONTENT "${_rbfx_test_runsettings_content}")

configure_file(
  "${CMAKE_CURRENT_SOURCE_DIR}/CopyRbfxRuntimeDependencies.cmake.in"
  "${RBFX_RUNTIME_DEPENDENCIES_SCRIPT}"
  @ONLY)

string(CONCAT _rbfx_sdk_flags_content
  "compiler_path=${CMAKE_CXX_COMPILER}\n"
  "language_standard=${RBFX_SDK_PARSE_LANGUAGE_STANDARD}\n")

if(RBFX_URHO3D_INCLUDE_DIRECTORIES)
  list(JOIN RBFX_URHO3D_INCLUDE_DIRECTORIES "\ninclude_directory=" _rbfx_sdk_include_directories)
  string(APPEND _rbfx_sdk_flags_content "include_directory=${_rbfx_sdk_include_directories}\n")
endif()

if(RBFX_URHO3D_COMPILE_DEFINITIONS)
  list(JOIN RBFX_URHO3D_COMPILE_DEFINITIONS "\ndefine=" _rbfx_sdk_compile_definitions)
  string(APPEND _rbfx_sdk_flags_content "define=${_rbfx_sdk_compile_definitions}\n")
endif()

if(RBFX_URHO3D_COMPILE_OPTIONS)
  list(JOIN RBFX_URHO3D_COMPILE_OPTIONS "\nclang_argument=" _rbfx_sdk_compile_options)
  string(APPEND _rbfx_sdk_flags_content "clang_argument=${_rbfx_sdk_compile_options}\n")
endif()

file(GENERATE OUTPUT "${RBFX_SDK_FLAGS_FILE}" CONTENT "${_rbfx_sdk_flags_content}")

file(GLOB_RECURSE RBFX_SDK_INPUT_HEADERS CONFIGURE_DEPENDS
  "${RBFX_SDK_ROOT}/include/Urho3D/*.h"
  "${RBFX_SDK_ROOT}/include/Urho3D/*.hpp")

set_property(GLOBAL PROPERTY SHARPCPP_RBFX_GENERATED_DIR "${RBFX_GENERATED_ROOT}")
set_property(GLOBAL PROPERTY SHARPCPP_RBFX_GENERATED_STAMP "${RBFX_GENERATED_STAMP}")
set_property(GLOBAL PROPERTY SHARPCPP_RBFX_TESTS_E2E_GENERATED_DIR "${RBFX_GENERATED_ROOT}")
set_property(GLOBAL PROPERTY SHARPCPP_RBFX_RUNSETTINGS_FILE "${RBFX_TEST_RUNSETTINGS_FILE}")

sharpcpp_add_dotnet_build_target(
  sharpcpp-rbfx-generator
  "${RBFX_GENERATOR_PROJECT}"
  DEPENDS sharpcpp-model sharpcpp-metadata sharpcpp-parsing sharpcpp-pipeline sharpcpp-rendering sharpcpp-support)

get_target_property(RBFX_GENERATOR_BUILD_STAMP sharpcpp-rbfx-generator SHARPCPP_DOTNET_BUILD_STAMP)

get_filename_component(RBFX_GENERATOR_SRC_DIR "${RBFX_GENERATOR_PROJECT}" DIRECTORY)
file(GLOB_RECURSE RBFX_GENERATOR_INPUTS CONFIGURE_DEPENDS
  "${CMAKE_SOURCE_DIR}/src/SharpCpp.Model/*.cs"
  "${CMAKE_SOURCE_DIR}/src/SharpCpp.Metadata/*.cs"
  "${CMAKE_SOURCE_DIR}/src/SharpCpp.Parsing/*.cs"
  "${CMAKE_SOURCE_DIR}/src/SharpCpp.Pipeline/*.cs"
  "${CMAKE_SOURCE_DIR}/src/SharpCpp.Rendering/*.cs"
  "${RBFX_GENERATOR_SRC_DIR}/*.cs"
  "${CMAKE_SOURCE_DIR}/include/SharpCpp/*.hpp"
  "${CMAKE_SOURCE_DIR}/include/SharpCpp/*.h")

add_custom_command(
  OUTPUT "${RBFX_GENERATED_STAMP}"
  COMMAND "${CMAKE_COMMAND}" 
          "-DGENERATOR_DLL=${SHARPCPP_BIN_DIR}/SharpCpp.Rbfx.Generator.dll"
          "-DGENERATED_STAMP=${RBFX_GENERATED_STAMP}"
          "-DGENERATED_DIR=${RBFX_GENERATED_ROOT}"
          -P "${CMAKE_CURRENT_SOURCE_DIR}/CheckGeneratedStaleness.cmake"
  COMMAND "${CMAKE_COMMAND}" -E make_directory "${RBFX_GENERATED_ROOT}"
  COMMAND "${DOTNET_EXECUTABLE}" "${SHARPCPP_BIN_DIR}/SharpCpp.Rbfx.Generator.dll"
          --sdk-root "${RBFX_SDK_ROOT}"
          --flags-file "${RBFX_SDK_FLAGS_FILE}"
          --generated-root "${RBFX_GENERATED_ROOT}"
          --project-root "${CMAKE_SOURCE_DIR}"
          --sdk-library "$<TARGET_FILE:${RBFX_URHO3D_TARGET}>"
          --log-file "${RBFX_GENERATOR_LOG_FILE}"
  COMMAND "${CMAKE_COMMAND}" -E touch "${RBFX_GENERATED_STAMP}"
  DEPENDS "${RBFX_GENERATOR_BUILD_STAMP}" "${RBFX_SDK_FLAGS_FILE}" ${RBFX_SDK_INPUT_HEADERS} ${RBFX_GENERATOR_INPUTS}
  WORKING_DIRECTORY "${CMAKE_SOURCE_DIR}"
  VERBATIM
  USES_TERMINAL)

add_custom_target(
  sharpcpp-generate-rbfx
  DEPENDS
    "${RBFX_GENERATED_STAMP}")

file(MAKE_DIRECTORY "${RBFX_GENERATED_ROOT}")
file(GLOB_RECURSE RBFX_GENERATED_CPP_FILES CONFIGURE_DEPENDS "${RBFX_GENERATED_ROOT}/*.g.cpp")
if(NOT RBFX_GENERATED_CPP_FILES)
  file(WRITE "${RBFX_GENERATED_ROOT}/.stub.cpp" "// stub — replaced by generator\n")
  set(RBFX_GENERATED_CPP_FILES "${RBFX_GENERATED_ROOT}/.stub.cpp")
endif()
set_source_files_properties(${RBFX_GENERATED_CPP_FILES} PROPERTIES OBJECT_DEPENDS "${RBFX_GENERATED_STAMP}")
add_library(SharpCppRbfx_Native SHARED ${RBFX_GENERATED_CPP_FILES} "${CMAKE_CURRENT_SOURCE_DIR}/SharpCpp.Rbfx.Bindings/Interop/RefCountedRuntime.cpp")
add_dependencies(SharpCppRbfx_Native sharpcpp-generate-rbfx)
target_compile_features(SharpCppRbfx_Native PRIVATE cxx_std_20)
set_target_properties(SharpCppRbfx_Native PROPERTIES CXX_SCAN_FOR_MODULES OFF UNITY_BUILD ON UNITY_BUILD_BATCH_SIZE 100 OUTPUT_NAME "SharpCpp.Rbfx.Native")
target_include_directories(SharpCppRbfx_Native PRIVATE "${RBFX_GENERATED_ROOT}")
target_include_directories(SharpCppRbfx_Native PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}/include")
target_link_libraries(SharpCppRbfx_Native PRIVATE sharpcpp-native-support ${RBFX_URHO3D_TARGET})
target_compile_options(SharpCppRbfx_Native PRIVATE $<$<COMPILE_LANG_AND_ID:CXX,GNU,Clang,AppleClang>:-Wno-attributes>)
sharpcpp_set_binary_output(SharpCppRbfx_Native)
set_target_properties(
  SharpCppRbfx_Native
  PROPERTIES
    BUILD_RPATH "$ORIGIN"
    INSTALL_RPATH "$ORIGIN")

set(RBFX_RUNTIME_COPY_COMMAND
  "${CMAKE_COMMAND}"
  -Dtarget_file=$<SHELL_PATH:$<TARGET_FILE:SharpCppRbfx_Native>>
  -Doutput_dir=$<SHELL_PATH:$<TARGET_FILE_DIR:SharpCppRbfx_Native>>
  -Durho_dir=$<SHELL_PATH:$<TARGET_FILE_DIR:${RBFX_URHO3D_TARGET}>>
  -Durho_library_file=$<SHELL_PATH:$<TARGET_FILE:${RBFX_URHO3D_TARGET}>>
  -Dconfig=$<CONFIG>
  -P "$<SHELL_PATH:${RBFX_RUNTIME_DEPENDENCIES_SCRIPT}>")

add_custom_command(
  TARGET SharpCppRbfx_Native
  POST_BUILD
  COMMAND ${RBFX_RUNTIME_COPY_COMMAND}
  VERBATIM)

add_custom_target(
  sharpcpp-rbfx-runtime
  COMMAND ${RBFX_RUNTIME_COPY_COMMAND}
  DEPENDS SharpCppRbfx_Native
  VERBATIM)

file(GLOB_RECURSE RBFX_GENERATED_CS_FILES CONFIGURE_DEPENDS "${RBFX_GENERATED_ROOT}/*.g.cs")

sharpcpp_add_dotnet_build_target(
  sharpcpp-rbfx-managed
  "${RBFX_BINDINGS_PROJECT}"
  DEPENDS sharpcpp-generate-rbfx SharpCppRbfx_Native sharpcpp-support ${RBFX_GENERATED_CS_FILES}
  PROPERTIES "-p:SharpCppGeneratedDir=${RBFX_GENERATED_ROOT}")

sharpcpp_add_dotnet_build_target(
  sharpcpp-rbfx-tests-e2e-managed
  "${RBFX_E2E_TESTS_PROJECT}"
  DEPENDS sharpcpp-rbfx-managed
  PROPERTIES "-p:SharpCppGeneratedDir=${RBFX_GENERATED_ROOT}" "-p:SharpCppUserCodeGeneratedDir=${RBFX_USERCODE_GENERATED_ROOT}")

add_dependencies(sharpcpp-rbfx-managed sharpcpp-rbfx-runtime)
add_dependencies(sharpcpp-rbfx-tests-e2e-managed sharpcpp-rbfx-runtime)

add_custom_target(sharpcpp-rbfx-native DEPENDS sharpcpp-generate-rbfx SharpCppRbfx_Native sharpcpp-rbfx-runtime)

add_subdirectory(Rbfx.UserCode)
add_subdirectory(Rbfx.Application)

add_dependencies(sharpcpp-rbfx-tests-e2e-managed sharpcpp-rbfx-usercode-managed)

if(BUILD_TESTING)
  add_test(
    NAME sharpcpp.rbfx.e2e
    COMMAND "${DOTNET_EXECUTABLE}" test "${RBFX_E2E_TESTS_PROJECT}"
            --configuration $<CONFIG>
            --no-build
            --no-restore
            --settings "$<SHELL_PATH:${RBFX_TEST_RUNSETTINGS_FILE}>"
            --logger "console;verbosity=minimal")
endif()
