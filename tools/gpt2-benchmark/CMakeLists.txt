cmake_minimum_required(VERSION 3.20)
project(Gpt2VulkanReference LANGUAGES CXX)

set(LLAMA_INCLUDE "" CACHE PATH "Pinned llama.cpp and ggml headers")
set(LLAMA_BIN "" CACHE PATH "Directory with the pinned Windows Vulkan binaries")
find_program(LIB_EXE lib REQUIRED)

function(import_dll name)
    set(definition "${CMAKE_CURRENT_BINARY_DIR}/${name}.def")
    set(library "${CMAKE_CURRENT_BINARY_DIR}/${name}.lib")
    file(WRITE "${definition}" "LIBRARY ${name}.dll\nEXPORTS\n")
    foreach(symbol IN LISTS ARGN)
        file(APPEND "${definition}" "  ${symbol}\n")
    endforeach()
    execute_process(COMMAND "${LIB_EXE}" /NOLOGO /MACHINE:X64 "/DEF:${definition}" "/OUT:${library}"
        COMMAND_ERROR_IS_FATAL ANY)
    add_library(${name} SHARED IMPORTED)
    set_target_properties(${name} PROPERTIES IMPORTED_LOCATION "${LLAMA_BIN}/${name}.dll" IMPORTED_IMPLIB "${library}")
endfunction()

import_dll(ggml ggml_backend_load_all ggml_backend_dev_by_name)
import_dll(ggml-base ggml_backend_dev_description)
import_dll(llama llama_backend_init llama_model_default_params llama_model_load_from_file llama_model_free
    llama_context_default_params llama_init_from_model llama_free llama_get_memory llama_memory_clear
    llama_batch_get_one llama_decode llama_get_logits_ith)
add_executable(gpt2-benchmark-reference reference.cpp)
target_compile_features(gpt2-benchmark-reference PRIVATE cxx_std_17)
target_include_directories(gpt2-benchmark-reference PRIVATE "${LLAMA_INCLUDE}")
target_link_libraries(gpt2-benchmark-reference PRIVATE llama ggml ggml-base)
set_target_properties(gpt2-benchmark-reference PROPERTIES RUNTIME_OUTPUT_DIRECTORY "${LLAMA_BIN}")
