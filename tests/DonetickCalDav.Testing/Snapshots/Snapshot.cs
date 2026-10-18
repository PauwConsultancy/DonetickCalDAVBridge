using System.Runtime.CompilerServices;
using System.Text;

namespace DonetickCalDav.Testing.Snapshots;

/// <summary>
/// Approval-style snapshot assertion (testing strategy, chapter 13 and appendix C): compares text with
/// an approved file <c>Snapshots/{TestClass}.{TestMethod}[.{discriminator}].verified.{extension}</c>
/// next to the test source file.
/// </summary>
/// <remarks>
/// On a mismatch, or when no approved file exists, the helper writes a <c>.received</c> file beside it
/// and fails with a line diff. <c>SNAPSHOT_ACCEPT=1</c> approves instead of failing, except when the
/// <c>CI</c> variable is set (GitHub Actions sets it), so a CI run can never approve its own output.
/// Line endings are normalised to LF with a single trailing newline before comparing.
/// </remarks>
public static class Snapshot
{
    public const string AcceptVariable = "SNAPSHOT_ACCEPT";

    /// <summary>Compares <paramref name="actual"/> with the approved snapshot of the calling test.</summary>
    /// <param name="actual">Text to compare, for example ICS or XML.</param>
    /// <param name="extension">File extension of the snapshot, so it opens with the right highlighting.</param>
    /// <param name="discriminator">Distinguishes several snapshots of one test, for example per theory case.</param>
    /// <param name="scrubbers">Replace volatile values (see <see cref="Scrubbers"/>) before comparing.</param>
    public static void Match(
        string actual,
        string extension = "txt",
        string? discriminator = null,
        IEnumerable<Func<string, string>>? scrubbers = null,
        [CallerFilePath] string sourceFile = "",
        [CallerMemberName] string testName = "")
    {
        Verify(actual, SnapshotFile.For(sourceFile, testName, discriminator, extension), scrubbers,
            Environment.GetEnvironmentVariable);
    }

    /// <summary>The comparison itself, with file location and environment passed in, so it can be tested.</summary>
    internal static void Verify(
        string actual,
        SnapshotFile file,
        IEnumerable<Func<string, string>>? scrubbers,
        Func<string, string?> environment)
    {
        var received = Normalise((scrubbers ?? []).Aggregate(actual, (text, scrub) => scrub(text)));
        var verified = File.Exists(file.VerifiedPath) ? Normalise(File.ReadAllText(file.VerifiedPath)) : null;

        if (verified == received)
        {
            File.Delete(file.ReceivedPath);
            return;
        }

        Directory.CreateDirectory(file.Directory);
        if (environment(AcceptVariable) == "1" && string.IsNullOrEmpty(environment("CI")))
        {
            File.WriteAllText(file.VerifiedPath, received);
            File.Delete(file.ReceivedPath);
            return;
        }

        File.WriteAllText(file.ReceivedPath, received);
        var message = verified is null
            ? $"New snapshot, review {file.ReceivedPath} and rename it to {Path.GetFileName(file.VerifiedPath)} " +
              $"(or run the tests with {AcceptVariable}=1)."
            : $"Snapshot mismatch for {Path.GetFileName(file.VerifiedPath)} (received: {file.ReceivedPath}):\n" +
              LineDiff(verified, received);
        throw new SnapshotMismatchException(message);
    }

    /// <summary>LF line endings and exactly one trailing newline.</summary>
    internal static string Normalise(string text) => text.Replace("\r\n", "\n").TrimEnd('\n') + "\n";

    /// <summary>Line diff based on the longest common subsequence, with one line of context around changes.</summary>
    internal static string LineDiff(string expected, string actual)
    {
        var a = expected.TrimEnd('\n').Split('\n');
        var b = actual.TrimEnd('\n').Split('\n');
        var lcs = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
            for (var j = b.Length - 1; j >= 0; j--)
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        var lines = new List<(char Kind, string Text)>();
        int x = 0, y = 0;
        while (x < a.Length || y < b.Length)
        {
            if (x < a.Length && y < b.Length && a[x] == b[y]) { lines.Add((' ', a[x])); x++; y++; }
            else if (x < a.Length && (y == b.Length || lcs[x + 1, y] >= lcs[x, y + 1])) lines.Add(('-', a[x++]));
            else lines.Add(('+', b[y++]));
        }

        var output = new StringBuilder();
        var previousPrinted = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            var nearChange = Enumerable.Range(Math.Max(0, i - 1), Math.Min(i + 2, lines.Count) - Math.Max(0, i - 1))
                .Any(k => lines[k].Kind != ' ');
            if (!nearChange) continue;
            if (previousPrinted >= 0 && i > previousPrinted + 1) output.AppendLine("  ...");
            output.AppendLine($"{lines[i].Kind} {lines[i].Text}");
            previousPrinted = i;
        }

        return output.ToString();
    }
}

/// <summary>Where the approved and received files of one snapshot live.</summary>
internal sealed record SnapshotFile(string Directory, string BaseName, string Extension)
{
    public string VerifiedPath => Path.Combine(Directory, $"{BaseName}.verified.{Extension}");

    public string ReceivedPath => Path.Combine(Directory, $"{BaseName}.received.{Extension}");

    public static SnapshotFile For(string sourceFile, string testName, string? discriminator, string extension) =>
        new(Path.Combine(Path.GetDirectoryName(sourceFile)!, "Snapshots"),
            $"{Path.GetFileNameWithoutExtension(sourceFile)}.{testName}" + (discriminator is null ? "" : $".{discriminator}"),
            extension);
}

/// <summary>Thrown when a snapshot is new or differs; xUnit reports it as a test failure.</summary>
public sealed class SnapshotMismatchException(string message) : Xunit.Sdk.XunitException(message);
