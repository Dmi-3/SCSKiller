using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SCSKiller.Core.Warming;

public sealed record ShadPs4WarmResult(int Loaded, int Total);

public static class ShadPs4Warmer
{
    internal static bool SupportsWarmup(string help) => Regex.IsMatch(help, @"^\s*--warmup-cache(?:\s|$)", RegexOptions.Multiline);

    internal static ShadPs4WarmResult ParseResult(string output, int exitCode)
    {
        var rows = output.Split('\n').Where(l => l.StartsWith("SCSKILLER_WARM ", StringComparison.Ordinal)).ToArray();
        if (rows.Length != 1) throw new InvalidDataException("shadPS4 did not return one cache warmup result.");
        using var doc = JsonDocument.Parse(rows[0][15..]);
        var loaded = doc.RootElement.GetProperty("loaded").GetInt32();
        var total = doc.RootElement.GetProperty("total").GetInt32();
        if (exitCode != 0 || loaded <= 0 || total != loaded)
            throw new InvalidDataException($"shadPS4 warmed {loaded}/{total} pipelines (exit {exitCode}); the cache may be empty, stale or incompatible.");
        return new(loaded, total);
    }

    public static async Task<ShadPs4WarmResult> WarmAsync(string emulator, string game, string userData, string serial, CancellationToken ct)
    {
        emulator = Path.GetFullPath(emulator);
        game = Path.GetFullPath(game);
        userData = Path.GetFullPath(userData);
        if (!Regex.IsMatch(serial, @"^CUSA[0-9]{5}$")) throw new ArgumentException("Use a title ID such as CUSA03281.");
        if (!File.Exists(emulator) || !File.Exists(game)) throw new FileNotFoundException("The emulator and game executable must exist.");
        if (!Path.GetFileName(emulator).Equals("shadPS4.exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The warmup build must be named shadPS4.exe so NVIDIA uses the game's driver cache.");
        var config = Path.Combine(userData, "config.json");
        var cache = Path.Combine(userData, "cache", serial);
        var archive = cache + ".zip";
        if (!File.Exists(config) || (!Directory.Exists(cache) && !File.Exists(archive)))
            throw new FileNotFoundException("An existing shadPS4 configuration and recorded game cache are required.");
        var scratch = Directory.CreateTempSubdirectory("scskiller-shadps4-").FullName;
        try
        {
            var portable = Directory.CreateDirectory(Path.Combine(scratch, "user")).FullName;
            File.Copy(config, Path.Combine(portable, "config.json"));
            var custom = Path.Combine(userData, "custom_configs", serial + ".json");
            if (File.Exists(custom))
                File.Copy(custom, Path.Combine(Directory.CreateDirectory(Path.Combine(portable, "custom_configs")).FullName, serial + ".json"));
            var destination = Directory.CreateDirectory(Path.Combine(portable, "cache")).FullName;
            if (File.Exists(archive)) File.Copy(archive, Path.Combine(destination, serial + ".zip"));
            if (Directory.Exists(cache))
            {
                destination = Directory.CreateDirectory(Path.Combine(destination, serial)).FullName;
                foreach (var f in Directory.EnumerateFiles(cache)) File.Copy(f, Path.Combine(destination, Path.GetFileName(f)));
            }
            var help = await Run(emulator, scratch, ["--help"], ct);
            if (help.ExitCode != 0 || !SupportsWarmup(help.Output))
                throw new InvalidOperationException("This shadPS4 build has no --warmup-cache mode. The game was not launched.");
            var result = await Run(emulator, scratch, ["--warmup-cache", "--game", game], ct);
            return ParseResult(result.Output, result.ExitCode);
        }
        finally
        {
            try { Directory.Delete(scratch, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    static async Task<(int ExitCode, string Output)> Run(string executable, string directory, string[] args, CancellationToken ct)
    {
        var info = new ProcessStartInfo(executable) { WorkingDirectory = directory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new IOException("Could not start shadPS4.");
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        try
        {
            await process.WaitForExitAsync(ct);
            var output = await stdout;
            var errors = await stderr;
            if (process.ExitCode != 0 && !output.Contains("SCSKILLER_WARM ", StringComparison.Ordinal))
                throw new IOException($"shadPS4 failed (exit {process.ExitCode}): {errors}");
            return (process.ExitCode, output);
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
    }
}
