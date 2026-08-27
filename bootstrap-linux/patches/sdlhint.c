#define _GNU_SOURCE
#include <dlfcn.h>
#include <string.h>
#include <stdbool.h>
#include <stdio.h>

// Interpose SDL_SetHint to force SDL_VIDEO_X11_EXTERNAL_WINDOW_INPUT=1
// on Linux. The engine sets it to 0 before wrapping the play widget
// with SDL_CreateWindowFrom, which skips SetupWindowInput and leaves
// SDL with no X input. Hint 1 lets SDL select XI2 input alongside Qt.
// Keep off render path — only this config call.
//
// Build: gcc -shared -fPIC -O2 -o libsdlhint.so sdlhint.c -ldl
// Use: LD_PRELOAD=libsdlhint.so ./run-editor.sh

typedef bool (*SDL_SetHint_fn)(const char *name, const char *value);
typedef bool (*SDL_SetHintWithPriority_fn)(const char *name, const char *value, int priority);

bool SDL_SetHint(const char *name, const char *value)
{
    static SDL_SetHint_fn real = NULL;
    if (!real) real = (SDL_SetHint_fn)dlsym(RTLD_NEXT, "SDL_SetHint");

    if (name && strcmp(name, "SDL_VIDEO_X11_EXTERNAL_WINDOW_INPUT") == 0)
    {
        fprintf(stderr, "[sdlhint] SDL_SetHint(%s, %s) -> forcing 1\n", name, value ? value : "(null)");
        if (real) return real(name, "1");
        return true;
    }

    if (real) return real(name, value);
    return false;
}

bool SDL_SetHintWithPriority(const char *name, const char *value, int priority)
{
    static SDL_SetHintWithPriority_fn real = NULL;
    if (!real) real = (SDL_SetHintWithPriority_fn)dlsym(RTLD_NEXT, "SDL_SetHintWithPriority");

    if (name && strcmp(name, "SDL_VIDEO_X11_EXTERNAL_WINDOW_INPUT") == 0)
    {
        fprintf(stderr, "[sdlhint] SDL_SetHintWithPriority(%s, %s, %d) -> forcing 1\n", name, value ? value : "(null)", priority);
        if (real) return real(name, "1", priority);
        return true;
    }

    if (real) return real(name, value, priority);
    return false;
}
