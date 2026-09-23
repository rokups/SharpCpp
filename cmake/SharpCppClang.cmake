set(SHARPCPP_CLANG_RESOURCE_DIRECTORY
    "${CMAKE_BINARY_DIR}/sharpcpp-clang-resource"
    CACHE PATH
    "Clang resource directory used by SharpCpp parser consumers"
    FORCE)

function(sharpcpp_prepare_clang_resource_directory)
    set(resource_directory "${SHARPCPP_CLANG_RESOURCE_DIRECTORY}")
    set(resource_include_directory "${resource_directory}/include")
    if(EXISTS "${resource_include_directory}/stddef.h")
        return()
    endif()

    find_package(Git REQUIRED)
    set(headers_checkout_directory "${CMAKE_BINARY_DIR}/_deps/sharpcppclangheaders-src")
    set(headers_source_directory "${headers_checkout_directory}/clang/lib/Headers")
    if(NOT EXISTS "${headers_source_directory}/stddef.h")
        file(REMOVE_RECURSE "${headers_checkout_directory}")
        file(MAKE_DIRECTORY "${CMAKE_BINARY_DIR}/_deps")
        execute_process(
            COMMAND "${GIT_EXECUTABLE}" clone
                --depth 1
                --branch llvmorg-20.1.2
                --filter=blob:none
                --sparse
                https://github.com/llvm/llvm-project.git
                "${headers_checkout_directory}"
            RESULT_VARIABLE clone_result
            OUTPUT_VARIABLE clone_output
            ERROR_VARIABLE clone_error)
        if(NOT clone_result EQUAL 0)
            message(FATAL_ERROR
                "Could not obtain the pinned Clang builtin headers: ${clone_error}${clone_output}")
        endif()
    endif()

    execute_process(
        COMMAND "${GIT_EXECUTABLE}" -C "${headers_checkout_directory}"
                sparse-checkout init --cone
        COMMAND_ERROR_IS_FATAL ANY)
    execute_process(
        COMMAND "${GIT_EXECUTABLE}" -C "${headers_checkout_directory}"
                sparse-checkout set --cone clang/lib/Headers
        COMMAND_ERROR_IS_FATAL ANY)

    if(NOT EXISTS "${headers_source_directory}/stddef.h")
        message(FATAL_ERROR
            "The pinned LLVM checkout does not contain Clang builtin headers: ${headers_source_directory}")
    endif()

    file(MAKE_DIRECTORY "${resource_directory}")
    file(REMOVE_RECURSE "${resource_include_directory}")
    file(CREATE_LINK
        "${headers_source_directory}"
        "${resource_include_directory}"
        SYMBOLIC
        RESULT link_result)
    if(NOT link_result STREQUAL "0")
        file(COPY "${headers_source_directory}/" DESTINATION "${resource_include_directory}")
    endif()

    if(NOT EXISTS "${resource_include_directory}/stddef.h")
        message(FATAL_ERROR
            "Could not prepare the Clang builtin headers at ${resource_include_directory}")
    endif()
endfunction()

sharpcpp_prepare_clang_resource_directory()
