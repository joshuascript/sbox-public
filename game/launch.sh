#!/usr/bin/env bash

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# 1a. Recompile the case-insensitive path resolver from source.
#     Source: $SCRIPT_DIR/bin/linuxsteamrt64/casefold.c
gcc -O2 -fPIC -shared \
    -o "$SCRIPT_DIR/bin/linuxsteamrt64/libcasefold.so" \
    "$SCRIPT_DIR/bin/linuxsteamrt64/casefold.c" \
    -ldl -lpthread

# 1b. Inject case-insensitive path resolver — rewrites mixed-case engine paths
#     to whatever the disk actually has so Source 2 file opens succeed on
#     case-sensitive filesystems. See bin/linuxsteamrt64/casefold.c.
# 1c. Inject Steam overlay — hooks Vulkan/GL swapchain for the in-game UI.
export LD_PRELOAD="$SCRIPT_DIR/bin/linuxsteamrt64/libcasefold.so:/home/joshua/.steam/debian-installation/ubuntu12_64/gameoverlayrenderer.so"

# 2. Make the system NVIDIA libs (incl. libnvidia-ml.so.1, NVML) reachable via
#    dlopen. Steam's Sniper runtime would mount these via pressure-vessel
#    automatically; running outside the container we have to point at them.
export LD_LIBRARY_PATH=/usr/lib/x86_64-linux-gnu

# 3. Tell the overlay what game it's attached to. s&box AppID is 590830
export SteamAppId=590830
export SteamGameId=590830

# 4. Force Xwayland for the window so the overlay can actually render.
#    Steam overlay's hooks target X11 GLX/Vulkan-on-X11 paths; native Wayland
#    surfaces aren't supported.
export SDL_VIDEODRIVER=x11

# 5. Raise inotify instance limit — the engine creates one watcher per mounted
#    sub-filesystem per Watch() call and hits the kernel default of 128 fast.
echo "WARNING: sudo is required to temporarily raise the inotify instance limit."
echo "This is a known limitation while this GitHub fork is still being optimized."
echo "Source: $SCRIPT_DIR/bin/linuxsteamrt64/casefold.c"
echo "Source: $SCRIPT_DIR/launch.sh"
sudo sysctl -w fs.inotify.max_user_instances=1024

# 6. Launch sbox
exec ./sbox "$@"
