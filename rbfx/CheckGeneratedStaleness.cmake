if(NOT EXISTS "${GENERATOR_DLL}")
  message(FATAL_ERROR "Generator DLL not found: ${GENERATOR_DLL}")
endif()

if(EXISTS "${GENERATED_STAMP}")
  file(TIMESTAMP "${GENERATOR_DLL}" GENERATOR_TIME "%s")
  file(TIMESTAMP "${GENERATED_STAMP}" GENERATED_TIME "%s")

  if(GENERATOR_TIME GREATER GENERATED_TIME)
    file(REMOVE_RECURSE "${GENERATED_DIR}")
    message(STATUS "Generator was rebuilt; clearing stale generated bindings in ${GENERATED_DIR}")
  endif()
endif()
