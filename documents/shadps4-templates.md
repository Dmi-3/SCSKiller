# Experimental shadPS4 compute templates

`shadps4-template` extracts compute shader containers from zlib PSARC archives,
reads their GNM workgroup dimensions and register settings, translates GCN to
SPIR-V, and creates Vulkan compute pipelines without running guest code.
It requires the matching patched shadPS4 build and currently supports NVIDIA.

Example (paths must point to your own dump and installed profile):

```text
scskiller shadps4-template --emulator PATH/shadPS4.exe --game PATH/eboot.bin --user-data PATH/user --serial CUSA03281 --out report.json --include-recorded
```

`--limit N` bounds the experiment. By default, shaders already present in the
recorded SPIR-V cache are excluded. `--include-recorded` also compiles those
shaders so the generated SPIR-V can be compared byte for byte with actual game
variants. Each shader runs in its own child process with a 60-second timeout;
failures are reported individually. The batch has a 20-minute cancellation limit.

Resource descriptors and indirect user data are placeholders. Compilation
success alone does **not** establish gameplay reuse or a driver cache hit.
`MatchesRecorded: true` means identical SPIR-V was found; false means recorded
variants exist but none match; null means no recorded reference was available.
Graphics shaders, render-target formats, vertex layouts, and unseen runtime
resource combinations are outside this prototype. This is not full-game pipeline
precompilation and does not replace recorded-cache warmup.

Configuration and reference SPIR-V are copied into an isolated temporary profile.
Extracted game shader bytes stay temporary and are deleted afterwards. Assumed
templates are never registered as genuine game cache entries. Reports contain
names, hashes, timings, comparison results, and errors, not shader binaries.
The regular emulator and recorded warmup remain separate from this experiment.

## Local integration result

On CUSA03281 / RTX 4080, all 276 compute templates created Vulkan pipelines.
Of 30 compute assets present in the recorded cache, 9 produced identical SPIR-V
and 21 differed. The other 246 had no recorded reference. This establishes
compilation and comparison only; gameplay latency and driver cache hits remain
unmeasured. The final packaged build repeated 12/12 creations with 2 exact matches.
Automated tests were not run at the user's request; native and CLI builds passed.

The final packaged build also replayed 748/748 genuine recorded pipelines successfully.
