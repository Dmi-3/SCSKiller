# Experimental shadPS4 cache warmup (NVIDIA)

This unofficial fork can ask a matching shadPS4 build to replay an existing game pipeline cache without
executing the guest game. It prepares pipelines already recorded during play. It does not extract every shader
from a PS4 dump or prepare effects from unvisited scenes.

The emulator must be named `shadPS4.exe`, support `--warmup-cache`, and use the same source revision and renderer
settings as the game. NVIDIA's driver cache uses the executable name; other GPU vendors are not supported by
this experimental warmup mode. Driver updates or renderer changes may require a new recording.

```text
scskiller shadps4-warm --emulator <warmup-build/shadPS4.exe> --game <game/eboot.bin> --user-data <shadPS4-data> --serial CUSA03281
```

SCSKiller checks the emulator's help output before invoking the new mode. Stock shadPS4 builds are refused.
It copies `config.json`, the title's custom configuration and recorded cache into a temporary portable `user`
folder. Existing saves, settings and game cache are not modified. The driver receives the replayed pipelines.
The temporary copy is removed afterward; no shader binaries are uploaded or included in this repository.

The emulator prints `SCSKILLER_WARM {"loaded":N,"total":N}` and exits successfully only for a complete, nonempty
replay on NVIDIA. Missing, incompatible or partially stale caches fail the command. Shader stutter in a new scene,
readback overhead and emulator crashes can still occur.

## Reference and validation

Khronos describes [warming recorded resource caches](https://docs.vulkan.org/samples/latest/samples/performance/pipeline_cache/README.html#resource-cache-warmup): replay needs the full pipeline state, not shader modules alone.
The [Vulkan pipeline cache guide](https://docs.vulkan.org/guide/latest/pipeline_cache.html) explains reuse between runs.
The experimental driver-cache sharing assumption was checked locally on an RTX 4080: the same executable basename
reused a Vulkan probe cache from another directory; a different basename did not. This is a measured result on that
GPU/driver, not a cross-vendor Vulkan guarantee.

The native patch in `tools/shadps4-warm.patch` targets shadPS4 revision
`c7e065d1b415be16c23e260a21f1dd8bbfc4cb57` (0.19.0). Apply it to that revision with `git apply --check` followed
by `git apply`, then build using shadPS4's Windows build instructions. Keep the resulting executable named
`shadPS4.exe`. The warmup path skips profile migration and play-time updates, and never executes the guest.

Local integration validation on CUSA03281 replayed 380/380 recorded pipelines and exited successfully through
SCSKiller. Original configuration and save-file hashes were unchanged. This validates replay and isolation;
it does not measure the reduction in gameplay stutter or prove the game's crashes are fixed.