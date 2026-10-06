using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SCSKiller.Core.Warming;

public sealed record Ps4TemplateAttempt(string Name, string Hash, bool Compiled, bool? MatchesRecorded, long Milliseconds, string? Error);
public sealed record Ps4TemplateReport(int ShaderAssets, int ComputeAssets, int AlreadyRecorded, bool AssumedResources, IReadOnlyList<Ps4TemplateAttempt> Attempts);

/// <summary>Experimental resource templates: compilation success is not proof that the game will reuse the result.</summary>
public static class ShadPs4TemplateWarmer
{
    public static async Task<Ps4TemplateReport> WarmAsync(string emulator, string game, string userData, string serial,
        string reportPath, int limit, bool includeRecorded, IProgress<string>? progress, CancellationToken ct)
    {
        emulator = Path.GetFullPath(emulator);
        game = Path.GetFullPath(game);
        userData = Path.GetFullPath(userData);
        reportPath = Path.GetFullPath(reportPath);
        if (limit < 1 || !Regex.IsMatch(serial, "^CUSA[0-9]{5}$")) throw new ArgumentException("Positive limit and a CUSA title ID required.");
        if (!File.Exists(emulator) || !File.Exists(game) || !Path.GetFileName(emulator).Equals("shadPS4.exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Existing game and warmup build named shadPS4.exe required.");
        var scratch = Directory.CreateTempSubdirectory("scskiller-shadps4-template-").FullName;
        var attempts = new List<Ps4TemplateAttempt>();
        try
        {
            var portable = Directory.CreateDirectory(Path.Combine(scratch, "user")).FullName;
            File.Copy(Path.Combine(userData, "config.json"), Path.Combine(portable, "config.json"));
            var custom = Path.Combine(userData, "custom_configs", serial + ".json");
            if (File.Exists(custom)) File.Copy(custom, Path.Combine(Directory.CreateDirectory(Path.Combine(portable, "custom_configs")).FullName, serial + ".json"));
            var help = await ShadPs4Warmer.Run(emulator, scratch, ["--help"], ct);
            if (!Regex.IsMatch(help.Output, @"^\s*--warmup-template(?:\s|$)", RegexOptions.Multiline))
                throw new InvalidOperationException("This emulator has no experimental template mode. The game was not launched.");
            var sourceCache = Path.Combine(userData, "cache", serial);
            var reference = Directory.CreateDirectory(Path.Combine(portable, "cache", serial)).FullName;
            var recorded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(sourceCache))
                foreach (var file in Directory.EnumerateFiles(sourceCache, "*.spv"))
                {
                    recorded.Add(Path.GetFileNameWithoutExtension(file).Split('_')[0].Replace("0x", "", StringComparison.OrdinalIgnoreCase));
                    File.Copy(file, Path.Combine(reference, Path.GetFileName(file)));
                }
            var candidates = new List<(Ps4ShaderEntry Entry, string File)>();
            var shaders = Directory.CreateDirectory(Path.Combine(scratch, "shaders")).FullName;
            var index = ShadPs4ShaderIndex.Read(Path.GetDirectoryName(game)!, (entry, bytes) =>
            {
                ct.ThrowIfCancellationRequested();
                if (entry.Stage != "cxo" || entry.Hash == null || (!includeRecorded && recorded.Contains(entry.Hash)) || candidates.Count >= limit) return;
                var file = Path.Combine(shaders, $"{candidates.Count:D4}.cxo");
                File.WriteAllBytes(file, bytes);
                candidates.Add((entry, file));
            });
            progress?.Report($"{index.Count} shader assets; {candidates.Count} compute templates selected. Resource descriptors are assumed.");
            foreach (var (entry, file) in candidates)
            {
                ct.ThrowIfCancellationRequested();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(60));
                var watch = Stopwatch.StartNew();
                Ps4TemplateAttempt attempt;
                try
                {
                    var run = await ShadPs4Warmer.Run(emulator, scratch, ["--warmup-template", file, "--game", game], timeout.Token, allowFailure: true);
                    const string prefix = "SCSKILLER_TEMPLATE ";
                    var rows = run.Output.Split('\n').Where(l => l.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
                    if (run.ExitCode != 0 || rows.Length != 1) throw new IOException($"exit {run.ExitCode}: {run.Error.Trim()}");
                    using var json = JsonDocument.Parse(rows[0][prefix.Length..]);
                    var result = json.RootElement;
                    if (result.GetProperty("compiled").GetInt32() != 1 || result.GetProperty("hash").GetString() != entry.Hash
                        || !result.GetProperty("assumed").GetBoolean()) throw new InvalidDataException("Invalid template result.");
                    var match = result.GetProperty("recorded_match").GetInt32();
                    if (match is < -1 or > 1) throw new InvalidDataException("Invalid recorded comparison.");
                    attempt = new(entry.Name, entry.Hash!, true, match < 0 ? null : match == 1, watch.ElapsedMilliseconds, null);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    attempt = new(entry.Name, entry.Hash!, false, null, watch.ElapsedMilliseconds, "Template timed out after 60 seconds.");
                }
                catch (Exception e) when (e is IOException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
                {
                    attempt = new(entry.Name, entry.Hash!, false, null, watch.ElapsedMilliseconds, e.Message);
                }
                attempts.Add(attempt);
                WriteReport();
                progress?.Report($"{attempts.Count}/{candidates.Count}: {(attempt.Compiled ? "compiled template" : "failed")} {Path.GetFileName(entry.Name)}; recorded match: {attempt.MatchesRecorded?.ToString() ?? "unknown"}");
            }
            return WriteReport();

            Ps4TemplateReport WriteReport()
            {
                var report = new Ps4TemplateReport(index.Count, index.Count(s => s.Stage == "cxo"),
                    index.Count(s => s.Stage == "cxo" && s.Hash != null && recorded.Contains(s.Hash)), true, attempts);
                Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
                File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
                return report;
            }
        }
        finally
        {
            try { Directory.Delete(scratch, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
