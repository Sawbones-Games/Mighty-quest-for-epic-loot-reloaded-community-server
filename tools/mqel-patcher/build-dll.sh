#!/bin/bash
# Build the dinput8.dll cert-bypass proxy (32-bit). Requires zig.
# The exports forward to dinput8_orig.dll via src/dinput8.def (a renamed companion the patcher creates
# from the user's own System32\dinput8.dll). We ship only our own code here.
ZIG="${ZIG:-C:/Users/Thrax/AppData/Local/Temp/claude/d--Mighty-quest-for-epic-loot-decomp/102a6e54-aa99-463a-9804-53b0e86cf584/scratchpad/zig/zig-x86_64-windows-0.16.0/zig.exe}"
cd "$(dirname "$0")"
"$ZIG" cc -target x86-windows-gnu -shared -O2 -o dinput8.dll src/mqel_patch.c src/dinput8.def
echo "built: $(ls -la dinput8.dll)"
