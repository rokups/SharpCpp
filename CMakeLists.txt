cmake_minimum_required(VERSION 3.28)

project(SharpCpp LANGUAGES CXX)

include(CTest)

find_program(DOTNET_EXECUTABLE dotnet REQUIRED)

function(sharpcpp_initialize_consumer)
  set(options)
  set(oneValueArgs PACKAGE_ROOT CONSUMER_ROOT OUTPUT_ROOT INTERMEDIATE_ROOT DOTNET_STATE_DIR PROPS_FILE COMPAT_PROPS_FILE)
  set(multiValueArgs)
  cmake_parse_arguments(ARG "${options}" "${oneValueArgs}" "${multiValueArgs}" ${ARGN})

  get_property(already_initialized GLOBAL PROPERTY SHARPCPP_CONSUMER_INITIALIZED)
  if(already_initialized)
    return()
  endif()

  if("${ARG_PACKAGE_ROOT}" STREQUAL "")
    get_filename_component(package_root "${CMAKE_CURRENT_FUNCTION_LIST_DIR}/.." ABSOLUTE)
  else()
    get_filename_component(package_root "${ARG_PACKAGE_ROOT}" ABSOLUTE BASE_DIR "${CMAKE_SOURCE_DIR}")
  endif()

  if("${ARG_CONSUMER_ROOT}" STREQUAL "")
    set(consumer_root "${CMAKE_SOURCE_DIR}")
  else()
    get_filename_component(consumer_root "${ARG_CONSUMER_ROOT}" ABSOLUTE BASE_DIR "${CMAKE_SOURCE_DIR}")
  endif()

  if("${ARG_OUTPUT_ROOT}" STREQUAL "")
    set(output_root "${CMAKE_BINARY_DIR}/bin")
  else()
    get_filename_component(output_root "${ARG_OUTPUT_ROOT}" ABSOLUTE BASE_DIR "${CMAKE_BINARY_DIR}")
  endif()

  if("${ARG_INTERMEDIATE_ROOT}" STREQUAL "")
    set(intermediate_root "${CMAKE_BINARY_DIR}/obj/managed")
  else()
    get_filename_component(intermediate_root "${ARG_INTERMEDIATE_ROOT}" ABSOLUTE BASE_DIR "${CMAKE_BINARY_DIR}")
  endif()

  if("${ARG_DOTNET_STATE_DIR}" STREQUAL "")
    set(dotnet_state_dir "${CMAKE_BINARY_DIR}/cmake/dotnet")
  else()
    get_filename_component(dotnet_state_dir "${ARG_DOTNET_STATE_DIR}" ABSOLUTE BASE_DIR "${CMAKE_BINARY_DIR}")
  endif()

  if("${ARG_PROPS_FILE}" STREQUAL "")
    set(build_props_file "${dotnet_state_dir}/SharpCpp.CMake.props")
  else()
    get_filename_component(build_props_file "${ARG_PROPS_FILE}" ABSOLUTE BASE_DIR "${CMAKE_BINARY_DIR}")
  endif()

  if("${ARG_COMPAT_PROPS_FILE}" STREQUAL "")
    set(compat_props_file "${consumer_root}/.CMakeLast.props")
  else()
    get_filename_component(compat_props_file "${ARG_COMPAT_PROPS_FILE}" ABSOLUTE BASE_DIR "${consumer_root}")
  endif()

  set_property(GLOBAL PROPERTY SHARPCPP_PACKAGE_ROOT "${package_root}")
  set_property(GLOBAL PROPERTY SHARPCPP_CONSUMER_ROOT "${consumer_root}")
  set_property(GLOBAL PROPERTY SHARPCPP_OUTPUT_ROOT "${output_root}")
  set_property(GLOBAL PROPERTY SHARPCPP_INTERMEDIATE_ROOT "${intermediate_root}")
  set_property(GLOBAL PROPERTY SHARPCPP_DOTNET_STATE_DIR "${dotnet_state_dir}")
  set_property(GLOBAL PROPERTY SHARPCPP_BUILD_PROPS_FILE "${build_props_file}")
  set_property(GLOBAL PROPERTY SHARPCPP_COMPAT_PROPS_FILE "${compat_props_file}")
  set_property(GLOBAL PROPERTY SHARPCPP_CONSUMER_INITIALIZED TRUE)

  get_property(finalize_deferred GLOBAL PROPERTY SHARPCPP_CONSUMER_FINALIZE_DEFERRED)
  if(NOT finalize_deferred)
    cmake_language(DEFER CALL sharpcpp_finalize_consumer)
    set_property(GLOBAL PROPERTY SHARPCPP_CONSUMER_FINALIZE_DEFERRED TRUE)
  endif()
endfunction()

function(sharpcpp_ensure_consumer_initialized)
  get_property(consumer_initialized GLOBAL PROPERTY SHARPCPP_CONSUMER_INITIALIZED)
  if(NOT consumer_initialized)
    sharpcpp_initialize_consumer()
  endif()
endfunction()

function(sharpcpp_write_last_props)
  sharpcpp_ensure_consumer_initialized()

  set(configurations)
  if(CMAKE_CONFIGURATION_TYPES)
    set(configurations ${CMAKE_CONFIGURATION_TYPES})
  elseif(CMAKE_BUILD_TYPE)
    set(configurations ${CMAKE_BUILD_TYPE})
  else()
    set(configurations Debug)
  endif()
  list(REMOVE_DUPLICATES configurations)
  list(GET configurations 0 default_configuration)

  get_property(output_root GLOBAL PROPERTY SHARPCPP_OUTPUT_ROOT)
  get_property(intermediate_root GLOBAL PROPERTY SHARPCPP_INTERMEDIATE_ROOT)
  get_property(build_props_file GLOBAL PROPERTY SHARPCPP_BUILD_PROPS_FILE)
  get_property(compat_props_file GLOBAL PROPERTY SHARPCPP_COMPAT_PROPS_FILE)

  string(REPLACE "\\" "/" output_root "${output_root}")
  string(REPLACE "\\" "/" intermediate_root "${intermediate_root}")

  set(content "<Project>\n")
  string(APPEND content
    "  <PropertyGroup Condition=\"'$(Configuration)' == ''\">\n"
    "    <SharpCppOutputDir>${output_root}/${default_configuration}</SharpCppOutputDir>\n"
    "    <SharpCppIntermediateRoot>${intermediate_root}/${default_configuration}</SharpCppIntermediateRoot>\n"
    "  </PropertyGroup>\n")

  foreach(configuration IN LISTS configurations)
    string(APPEND content
      "  <PropertyGroup Condition=\"'$(Configuration)' == '${configuration}'\">\n"
      "    <SharpCppOutputDir>${output_root}/${configuration}</SharpCppOutputDir>\n"
      "    <SharpCppIntermediateRoot>${intermediate_root}/${configuration}</SharpCppIntermediateRoot>\n"
      "  </PropertyGroup>\n")
  endforeach()

  string(APPEND content "</Project>\n")

  get_filename_component(build_props_dir "${build_props_file}" DIRECTORY)
  file(MAKE_DIRECTORY "${build_props_dir}")
  file(WRITE "${build_props_file}" "${content}")

  if(NOT compat_props_file STREQUAL "")
    get_filename_component(compat_props_dir "${compat_props_file}" DIRECTORY)
    file(MAKE_DIRECTORY "${compat_props_dir}")
    file(WRITE "${compat_props_file}" "${content}")
  endif()
endfunction()

function(sharpcpp_restore_dotnet_project)
  sharpcpp_ensure_consumer_initialized()

  if("${ARGN}" STREQUAL "")
    return()
  endif()

  get_property(build_props_file GLOBAL PROPERTY SHARPCPP_BUILD_PROPS_FILE)
  get_property(package_root GLOBAL PROPERTY SHARPCPP_PACKAGE_ROOT)
  get_property(intermediate_root GLOBAL PROPERTY SHARPCPP_INTERMEDIATE_ROOT)

  set(configurations)
  if(CMAKE_CONFIGURATION_TYPES)
    set(configurations ${CMAKE_CONFIGURATION_TYPES})
  elseif(CMAKE_BUILD_TYPE)
    set(configurations ${CMAKE_BUILD_TYPE})
  else()
    set(configurations Debug)
  endif()
  list(REMOVE_DUPLICATES configurations)

  foreach(project_path IN LISTS ARGN)
    get_filename_component(project_dir "${project_path}" DIRECTORY)
    foreach(configuration IN LISTS configurations)
      execute_process(
        COMMAND "${CMAKE_COMMAND}" -E env "MSBuildEnableWorkloadResolver=false"
                "${DOTNET_EXECUTABLE}" restore "${project_path}" --nologo
                -p:Configuration=${configuration}
                -p:RestoreBuildInParallel=false
                -p:SharpCppIntermediateRoot=${intermediate_root}/${configuration}
                "-p:SharpCppPackageRoot=${package_root}"
                "-p:SharpCppCMakePropsPath=${build_props_file}"
        WORKING_DIRECTORY "${project_dir}"
        COMMAND_ERROR_IS_FATAL ANY)
    endforeach()
  endforeach()
endfunction()

function(sharpcpp_finalize_consumer)
  sharpcpp_ensure_consumer_initialized()
  sharpcpp_write_last_props()
  sharpcpp_restore_dotnet_project(
    "${CMAKE_SOURCE_DIR}/src/SharpCpp.Model/SharpCpp.Model.csproj"
    "${CMAKE_SOURCE_DIR}/src/SharpCpp.Metadata/SharpCpp.Metadata.csproj"
    "${CMAKE_SOURCE_DIR}/src/SharpCpp.Parsing/SharpCpp.Parsing.csproj"
    "${CMAKE_SOURCE_DIR}/src/SharpCpp.Pipeline/SharpCpp.Pipeline.csproj"
    "${CMAKE_SOURCE_DIR}/src/SharpCpp.Rendering/SharpCpp.Rendering.csproj"
    "${CMAKE_SOURCE_DIR}/src/SharpCpp.Support/SharpCpp.Support.csproj"
    "${CMAKE_SOURCE_DIR}/src/SharpCpp.Tool/SharpCpp.Tool.csproj"
    "${CMAKE_SOURCE_DIR}/tests/SharpCpp.Tests.Model/SharpCpp.Tests.Model.csproj"
    "${CMAKE_SOURCE_DIR}/tests/SharpCpp.Tests.Metadata/SharpCpp.Tests.Metadata.csproj"
    "${CMAKE_SOURCE_DIR}/tests/SharpCpp.Tests.Rendering/SharpCpp.Tests.Rendering.csproj"
    "${CMAKE_SOURCE_DIR}/tests/SharpCpp.Tests.E2E/SharpCpp.Tests.E2E.csproj")
endfunction()

function(sharpcpp_add_dotnet_build_target target_name project_path)
  sharpcpp_ensure_consumer_initialized()

  set(options)
  set(oneValueArgs)
  set(multiValueArgs DEPENDS PROPERTIES)
  cmake_parse_arguments(ARG "${options}" "${oneValueArgs}" "${multiValueArgs}" ${ARGN})

  get_property(consumer_root GLOBAL PROPERTY SHARPCPP_CONSUMER_ROOT)
  get_property(package_root GLOBAL PROPERTY SHARPCPP_PACKAGE_ROOT)
  get_property(build_props_file GLOBAL PROPERTY SHARPCPP_BUILD_PROPS_FILE)
  get_property(output_root GLOBAL PROPERTY SHARPCPP_OUTPUT_ROOT)
  get_property(dotnet_state_dir GLOBAL PROPERTY SHARPCPP_DOTNET_STATE_DIR)

  get_filename_component(absolute_project_path "${project_path}" ABSOLUTE BASE_DIR "${consumer_root}")
  get_filename_component(project_dir "${absolute_project_path}" DIRECTORY)
  file(GLOB_RECURSE project_inputs CONFIGURE_DEPENDS
    "${project_dir}/*.cs"
    "${project_dir}/*.csproj"
    "${project_dir}/*.props"
    "${project_dir}/*.targets")
  list(FILTER project_inputs EXCLUDE REGEX "/(bin|obj)/")

  set(shared_inputs
    "${build_props_file}"
    "${package_root}/cmake/dotnet/SharpCpp.Build.props"
    "${package_root}/cmake/dotnet/SharpCpp.Build.targets"
    "${consumer_root}/Directory.Build.props"
    "${consumer_root}/Directory.Build.targets"
    "${consumer_root}/Directory.Packages.props")

  set(existing_shared_inputs)
  foreach(shared_input IN LISTS shared_inputs)
    if(shared_input STREQUAL "${build_props_file}" OR EXISTS "${shared_input}")
      list(APPEND existing_shared_inputs "${shared_input}")
    endif()
  endforeach()

  set(stamp_file "${dotnet_state_dir}/$<CONFIG>/${target_name}.stamp")

  add_custom_command(
    OUTPUT "${stamp_file}"
    COMMAND "${CMAKE_COMMAND}" -E make_directory "$<SHELL_PATH:${output_root}/$<CONFIG>>"
    COMMAND "${CMAKE_COMMAND}" -E make_directory "$<SHELL_PATH:${dotnet_state_dir}/$<CONFIG>>"
    COMMAND "${CMAKE_COMMAND}" -E env "MSBuildEnableWorkloadResolver=false"
            "${DOTNET_EXECUTABLE}" msbuild "${absolute_project_path}"
            -maxcpucount:1
            -restore:false
            -nologo
            -verbosity:quiet
            -consoleloggerparameters:ErrorsOnly
            -p:Configuration=$<CONFIG>
            "-p:SharpCppPackageRoot=${package_root}"
            "-p:SharpCppCMakePropsPath=${build_props_file}"
            ${ARG_PROPERTIES}
    COMMAND "${CMAKE_COMMAND}" -E touch "$<SHELL_PATH:${stamp_file}>"
    DEPENDS ${ARG_DEPENDS} ${project_inputs} ${existing_shared_inputs}
    WORKING_DIRECTORY "${consumer_root}"
    VERBATIM
    USES_TERMINAL)

  add_custom_target("${target_name}" DEPENDS "${stamp_file}")
  set_target_properties("${target_name}" PROPERTIES SHARPCPP_DOTNET_BUILD_STAMP "${stamp_file}")
endfunction()

function(sharpcpp_set_binary_output target_name)
  set_target_properties(
    "${target_name}"
    PROPERTIES
      RUNTIME_OUTPUT_DIRECTORY "${SHARPCPP_BIN_DIR}"
      LIBRARY_OUTPUT_DIRECTORY "${SHARPCPP_BIN_DIR}"
      ARCHIVE_OUTPUT_DIRECTORY "${SHARPCPP_BIN_DIR}")
endfunction()

set(SHARPCPP_BIN_DIR "${CMAKE_BINARY_DIR}/bin/$<CONFIG>")

sharpcpp_initialize_consumer()

add_library(sharpcpp-native-support INTERFACE)
target_compile_features(sharpcpp-native-support INTERFACE cxx_std_20)
target_include_directories(sharpcpp-native-support INTERFACE "${CMAKE_SOURCE_DIR}/include")

sharpcpp_add_dotnet_build_target(sharpcpp-model "${CMAKE_SOURCE_DIR}/src/SharpCpp.Model/SharpCpp.Model.csproj")
sharpcpp_add_dotnet_build_target(sharpcpp-metadata "${CMAKE_SOURCE_DIR}/src/SharpCpp.Metadata/SharpCpp.Metadata.csproj" DEPENDS sharpcpp-model)
sharpcpp_add_dotnet_build_target(sharpcpp-parsing "${CMAKE_SOURCE_DIR}/src/SharpCpp.Parsing/SharpCpp.Parsing.csproj" DEPENDS sharpcpp-model sharpcpp-metadata)
sharpcpp_add_dotnet_build_target(sharpcpp-pipeline "${CMAKE_SOURCE_DIR}/src/SharpCpp.Pipeline/SharpCpp.Pipeline.csproj" DEPENDS sharpcpp-model sharpcpp-metadata sharpcpp-parsing)
sharpcpp_add_dotnet_build_target(sharpcpp-rendering "${CMAKE_SOURCE_DIR}/src/SharpCpp.Rendering/SharpCpp.Rendering.csproj" DEPENDS sharpcpp-model)
sharpcpp_add_dotnet_build_target(sharpcpp-support "${CMAKE_SOURCE_DIR}/src/SharpCpp.Support/SharpCpp.Support.csproj")
sharpcpp_add_dotnet_build_target(sharpcpp-tool "${CMAKE_SOURCE_DIR}/src/SharpCpp.Tool/SharpCpp.Tool.csproj" DEPENDS sharpcpp-model sharpcpp-metadata sharpcpp-parsing sharpcpp-pipeline sharpcpp-rendering sharpcpp-support)
sharpcpp_add_dotnet_build_target(sharpcpp-tests-model "${CMAKE_SOURCE_DIR}/tests/SharpCpp.Tests.Model/SharpCpp.Tests.Model.csproj" DEPENDS sharpcpp-model)
sharpcpp_add_dotnet_build_target(sharpcpp-tests-metadata "${CMAKE_SOURCE_DIR}/tests/SharpCpp.Tests.Metadata/SharpCpp.Tests.Metadata.csproj" DEPENDS sharpcpp-model sharpcpp-metadata)
sharpcpp_add_dotnet_build_target(sharpcpp-tests-rendering "${CMAKE_SOURCE_DIR}/tests/SharpCpp.Tests.Rendering/SharpCpp.Tests.Rendering.csproj" DEPENDS sharpcpp-model sharpcpp-rendering)

add_custom_target(sharpcpp-generator DEPENDS sharpcpp-tool)
get_target_property(SHARPCPP_TOOL_BUILD_STAMP sharpcpp-tool SHARPCPP_DOTNET_BUILD_STAMP)

set(SHARPCPP_E2E_GENERATED_ROOT "${CMAKE_BINARY_DIR}/generated/sharpcpp-e2e")
set(SHARPCPP_E2E_FILE_STEM "SharpCppTestsE2E")
set(SHARPCPP_E2E_HEADER_FILE "${CMAKE_SOURCE_DIR}/tests/SharpCpp.Tests.E2E/SampleRuntime/SampleRuntime.hpp")
set(SHARPCPP_E2E_GENERATED_CS "${SHARPCPP_E2E_GENERATED_ROOT}/${SHARPCPP_E2E_FILE_STEM}.Bindings.g.cs")
set(SHARPCPP_E2E_GENERATED_ALIAS_CS "${SHARPCPP_E2E_GENERATED_ROOT}/${SHARPCPP_E2E_FILE_STEM}.Bindings.Aliases.g.cs")
set(SHARPCPP_E2E_GENERATED_CPP "${SHARPCPP_E2E_GENERATED_ROOT}/${SHARPCPP_E2E_FILE_STEM}.Bindings.g.cpp")
set(SHARPCPP_E2E_METADATA_FILE "${SHARPCPP_E2E_GENERATED_ROOT}/${SHARPCPP_E2E_FILE_STEM}.Bindings.json")

add_custom_command(
  OUTPUT
    "${SHARPCPP_E2E_GENERATED_CS}"
    "${SHARPCPP_E2E_GENERATED_ALIAS_CS}"
    "${SHARPCPP_E2E_GENERATED_CPP}"
    "${SHARPCPP_E2E_METADATA_FILE}"
  COMMAND "${CMAKE_COMMAND}" -E make_directory "$<SHELL_PATH:${SHARPCPP_E2E_GENERATED_ROOT}>"
  COMMAND "${DOTNET_EXECUTABLE}" "$<SHELL_PATH:${SHARPCPP_BIN_DIR}/SharpCpp.Tool.dll>"
          generate-sample
          --generated-root "$<SHELL_PATH:${SHARPCPP_E2E_GENERATED_ROOT}>"
          --project-root "$<SHELL_PATH:${CMAKE_SOURCE_DIR}>"
          --file-stem "${SHARPCPP_E2E_FILE_STEM}"
  DEPENDS "${SHARPCPP_TOOL_BUILD_STAMP}" "${SHARPCPP_E2E_HEADER_FILE}"
  WORKING_DIRECTORY "${CMAKE_SOURCE_DIR}"
  VERBATIM
  USES_TERMINAL)

add_custom_target(
  sharpcpp-generate-e2e
  DEPENDS
    "${SHARPCPP_E2E_GENERATED_CS}"
    "${SHARPCPP_E2E_GENERATED_ALIAS_CS}"
    "${SHARPCPP_E2E_GENERATED_CPP}"
    "${SHARPCPP_E2E_METADATA_FILE}")

add_library(
  SharpCppTestsE2E_Native
  SHARED
  "${SHARPCPP_E2E_GENERATED_CPP}")
add_dependencies(SharpCppTestsE2E_Native sharpcpp-generate-e2e)
target_compile_features(SharpCppTestsE2E_Native PRIVATE cxx_std_20)
target_include_directories(
  SharpCppTestsE2E_Native
  PRIVATE
    "${SHARPCPP_E2E_GENERATED_ROOT}"
    "${CMAKE_SOURCE_DIR}/tests/SharpCpp.Tests.E2E")
sharpcpp_set_binary_output(SharpCppTestsE2E_Native)

sharpcpp_add_dotnet_build_target(
  sharpcpp-e2e-managed
  "${CMAKE_SOURCE_DIR}/tests/SharpCpp.Tests.E2E/SharpCpp.Tests.E2E.csproj"
  DEPENDS sharpcpp-generate-e2e SharpCppTestsE2E_Native
  PROPERTIES "-p:SharpCppGeneratedDir=${SHARPCPP_E2E_GENERATED_ROOT}")

add_custom_target(sharpcpp-native DEPENDS SharpCppTestsE2E_Native)

add_custom_target(
  sharpcpp-managed
  DEPENDS
    sharpcpp-model
    sharpcpp-metadata
    sharpcpp-parsing
    sharpcpp-pipeline
    sharpcpp-rendering
    sharpcpp-support
    sharpcpp-generator
    sharpcpp-e2e-managed)

add_custom_target(
  sharpcpp-tests
  DEPENDS
    sharpcpp-managed
    sharpcpp-tests-model
    sharpcpp-tests-metadata
    sharpcpp-tests-rendering)
add_custom_target(sharpcpp-all ALL DEPENDS sharpcpp-managed sharpcpp-native)

if(BUILD_TESTING)
  add_test(
    NAME sharpcpp.tests.model
    COMMAND "${DOTNET_EXECUTABLE}" test "${CMAKE_SOURCE_DIR}/tests/SharpCpp.Tests.Model/SharpCpp.Tests.Model.csproj"
            --configuration $<CONFIG>
            --no-build
            --no-restore
            --logger "console;verbosity=minimal")

  add_test(
    NAME sharpcpp.tests.metadata
    COMMAND "${DOTNET_EXECUTABLE}" test "${CMAKE_SOURCE_DIR}/tests/SharpCpp.Tests.Metadata/SharpCpp.Tests.Metadata.csproj"
            --configuration $<CONFIG>
            --no-build
            --no-restore
            --logger "console;verbosity=minimal")

  add_test(
    NAME sharpcpp.tests.rendering
    COMMAND "${DOTNET_EXECUTABLE}" test "${CMAKE_SOURCE_DIR}/tests/SharpCpp.Tests.Rendering/SharpCpp.Tests.Rendering.csproj"
            --configuration $<CONFIG>
            --no-build
            --no-restore
            --logger "console;verbosity=minimal")

  add_test(
    NAME sharpcpp.e2e
    COMMAND "${CMAKE_COMMAND}" -E env
            "LD_LIBRARY_PATH=$<SHELL_PATH:${SHARPCPP_BIN_DIR}>:$ENV{LD_LIBRARY_PATH}"
            "${DOTNET_EXECUTABLE}" test "${CMAKE_SOURCE_DIR}/tests/SharpCpp.Tests.E2E/SharpCpp.Tests.E2E.csproj"
            --configuration $<CONFIG>
            --no-build
            --no-restore
            --logger "console;verbosity=minimal")
endif()
