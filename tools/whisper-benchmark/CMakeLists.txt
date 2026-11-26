cmake_minimum_required(VERSION 3.20)
project(WhisperBenchmarkReference LANGUAGES CXX)

set(WHISPER_SOURCE "" CACHE PATH "Pinned whisper.cpp source directory")
set(WHISPER_LIBRARY "" CACHE FILEPATH "Release whisper import library")
set(WHISPER_BIN "" CACHE PATH "Release directory containing whisper and ggml DLLs")
add_executable(whisper-benchmark-reference reference.cpp)
target_compile_features(whisper-benchmark-reference PRIVATE cxx_std_17)
target_include_directories(whisper-benchmark-reference PRIVATE
    "${WHISPER_SOURCE}/include"
    "${WHISPER_SOURCE}/ggml/include")
target_link_libraries(whisper-benchmark-reference PRIVATE "${WHISPER_LIBRARY}")
set_property(TARGET whisper-benchmark-reference PROPERTY RUNTIME_OUTPUT_DIRECTORY_RELEASE "${WHISPER_BIN}")
