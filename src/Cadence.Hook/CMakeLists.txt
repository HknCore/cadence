cmake_minimum_required(VERSION 3.21)
project(CadenceHook LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

include(FetchContent)
FetchContent_Declare(
  minhook
  GIT_REPOSITORY https://github.com/TsudaKageyu/minhook.git
  GIT_TAG        8af6b4acae5a9388fd742b56fa79ece89d96f823
)
FetchContent_MakeAvailable(minhook)

add_library(CadenceHook SHARED
  src/dllmain.cpp
  src/hooks.cpp
  src/limiter.cpp
)
target_include_directories(CadenceHook PRIVATE include)
target_link_libraries(CadenceHook PRIVATE minhook winmm)
target_compile_definitions(CadenceHook PRIVATE WIN32_LEAN_AND_MEAN NOMINMAX UNICODE _UNICODE)

if(MSVC)
  target_compile_options(CadenceHook PRIVATE /W4 /permissive- /utf-8)
  # Statische CRT: die DLL laeuft im fremden Spielprozess ohne eigene Runtime-Installation.
  set_property(TARGET CadenceHook PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
  set_property(TARGET minhook PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
else()
  target_link_options(CadenceHook PRIVATE -static -static-libgcc -static-libstdc++)
endif()
