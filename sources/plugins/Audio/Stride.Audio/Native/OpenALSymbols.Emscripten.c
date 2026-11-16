// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

// WebAssembly has no dynamic libraries: OpenAL is linked into the application (Emscripten's, over Web Audio), and
// OpenAL.cpp finds its functions here, by name, as it does in a library on the other platforms.

#ifdef __EMSCRIPTEN__

#include "../../../deps/NativePath/NativePath.h"

#define HAVE_STDINT_H
#include "../../../../../deps/OpenAL/AL/al.h"
#include "../../../../../deps/OpenAL/AL/alc.h"

typedef struct { const char* name; void* address; } Symbol;

#define SYMBOL(function) { #function, (void*)function }

static const Symbol symbols[] =
{
	SYMBOL(alcOpenDevice), SYMBOL(alcCloseDevice), SYMBOL(alcCreateContext), SYMBOL(alcDestroyContext),
	SYMBOL(alcMakeContextCurrent), SYMBOL(alcGetCurrentContext), SYMBOL(alcProcessContext), SYMBOL(alcGetError),
	SYMBOL(alcSuspendContext),
	SYMBOL(alSourcePlay), SYMBOL(alSourcePause), SYMBOL(alSourceStop), SYMBOL(alSourcef), SYMBOL(alDeleteSources),
	SYMBOL(alDeleteBuffers), SYMBOL(alGenSources), SYMBOL(alGenBuffers), SYMBOL(alSource3i), SYMBOL(alSourcei),
	SYMBOL(alBufferData), SYMBOL(alSourceQueueBuffers), SYMBOL(alSourceUnqueueBuffers), SYMBOL(alGetSourcei),
	SYMBOL(alGetSourcef), SYMBOL(alSourcefv), SYMBOL(alListenerfv), SYMBOL(alListenerf), SYMBOL(alGetError),
};

void* LoadDynamicLibrary(const char* libraryPath)
{
	// Any name is the one library there is
	return (void*)symbols;
}

void FreeDynamicLibrary(void* handle)
{
}

void* GetSymbolAddress(void* handle, const char* symbolName)
{
	for (size_t i = 0; i < sizeof(symbols) / sizeof(symbols[0]); i++)
	{
		if (strcmp(symbols[i].name, symbolName) == 0)
			return symbols[i].address;
	}
	return NULL;
}

// There is no NativePath library for WebAssembly: its allocator is the one of the C library.
void* npMalloc(size_t size)
{
	return __builtin_malloc(size);
}

void npFree(void* pointer)
{
	__builtin_free(pointer);
}

#endif
