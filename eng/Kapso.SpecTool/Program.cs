using System.Text.RegularExpressions;

using YamlDotNet.RepresentationModel;

namespace Kapso.SpecTool;

/// <summary>
/// Build-time utility over the vendored OpenAPI documents.
///
/// It exists because of one property of Kiota: Kiota models an API as a tree of
/// URI templates keyed on the *shape* of each path, with every path parameter
/// normalized to the same placeholder. Two paths that differ only in the name of
/// a parameter — <c>/{media_id}</c> and <c>/{phone_number_id}</c> — therefore
/// collapse onto a single node, and the operations that lose the collision are
/// dropped from the generated client with no error.
///
/// Kapso's WhatsApp API is Meta Graph-shaped, so its first segment is a
/// polymorphic ID and it trips this in two places. Left alone, 6 of its 42
/// operations disappear silently.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            return Fail("usage: spectool <split|check> [...]");
        }

        try
        {
            return args[0] switch
            {
                "split" when args.Length == 3 => Split(args[1], args[2]),
                "check" when args.Length >= 2 => Check(args[1..]),
                _ => Fail("usage: spectool split <input.yaml> <output-dir>\n       spectool check <spec.yaml> [spec.yaml ...]"),
            };
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    /// <summary>
    /// Path prefixes that each become their own OpenAPI document, and therefore
    /// their own Kiota client.
    ///
    /// The grouping is by the resource the leading ID denotes, which is exactly
    /// the distinction Kiota erases. Every path in the source document must match
    /// exactly one group; see the unassigned-path check in <see cref="Split"/>.
    /// </summary>
    private static readonly (string Name, string[] Prefixes)[] WhatsAppGroups =
    [
        ("phone", ["/{phone_number_id}"]),
        ("waba", ["/{business_account_id}"]),
        ("flow", ["/{flow_id}", "/flows/"]),
        ("media", ["/{media_id}", "/media_download"]),
    ];

    private static int Split(string inputPath, string outputDir)
    {
        var (root, _) = Load(inputPath);
        var paths = (YamlMappingNode)root[new YamlScalarNode("paths")];

        Directory.CreateDirectory(outputDir);

        var assigned = new HashSet<string>(StringComparer.Ordinal);
        var totalOperations = 0;
        var failed = false;

        foreach (var (name, prefixes) in WhatsAppGroups)
        {
            var selected = paths.Children
                .Where(kv => prefixes.Any(p => Key(kv.Key).StartsWith(p, StringComparison.Ordinal)))
                .ToList();

            foreach (var kv in selected)
            {
                assigned.Add(Key(kv.Key));
            }

            // A group that still collides internally would lose operations just
            // like the undivided document, so refuse to emit it.
            var collisions = FindCollisions(selected.Select(kv => Key(kv.Key)));
            if (collisions.Count > 0)
            {
                failed = true;
                Console.Error.WriteLine($"error: group '{name}' still has colliding path signatures:");
                Report(collisions);
            }

            var subPaths = new YamlMappingNode();
            foreach (var kv in selected)
            {
                subPaths.Add(kv.Key, kv.Value);
            }

            var document = new YamlMappingNode();
            foreach (var kv in root.Children)
            {
                document.Add(kv.Key, Key(kv.Key) == "paths" ? subPaths : kv.Value);
            }

            RetitleInPlace(document, name);

            var outputPath = Path.Combine(outputDir, $"whatsapp-{name}.yaml");
            Save(document, outputPath);

            var operations = selected.Sum(kv => CountOperations(kv.Value));
            totalOperations += operations;
            Console.WriteLine($"  whatsapp-{name}.yaml  paths={selected.Count,-3} operations={operations}");
        }

        // A path matching no group would vanish from every generated client. That
        // is the failure mode this whole tool exists to prevent, so it is fatal
        // rather than a warning — a new Kapso resource root must be handled here.
        var unassigned = paths.Children.Select(kv => Key(kv.Key)).Where(p => !assigned.Contains(p)).ToList();
        if (unassigned.Count > 0)
        {
            failed = true;
            Console.Error.WriteLine("error: paths match no group and would be dropped:");
            foreach (var p in unassigned)
            {
                Console.Error.WriteLine($"  {p}");
            }

            Console.Error.WriteLine("       add a prefix to WhatsAppGroups in eng/Kapso.SpecTool/Program.cs");
        }

        var sourceOperations = paths.Children.Sum(kv => CountOperations(kv.Value));
        if (totalOperations != sourceOperations)
        {
            failed = true;
            Console.Error.WriteLine($"error: split produced {totalOperations} operations but the source has {sourceOperations}");
        }

        if (failed)
        {
            return 1;
        }

        Console.WriteLine($"  all {sourceOperations} operations preserved across {WhatsAppGroups.Length} documents");
        return 0;
    }

    /// <summary>
    /// Reports Kiota path-signature collisions and the operation count for each
    /// document. Run against every spec on every sync, so that a collision
    /// introduced upstream surfaces as a build failure instead of as methods
    /// quietly missing from the client.
    /// </summary>
    private static int Check(string[] specPaths)
    {
        var failed = false;

        foreach (var specPath in specPaths)
        {
            var (root, _) = Load(specPath);
            var paths = (YamlMappingNode)root[new YamlScalarNode("paths")];
            var operations = paths.Children.Sum(kv => CountOperations(kv.Value));
            var collisions = FindCollisions(paths.Children.Select(kv => Key(kv.Key)));

            var status = collisions.Count == 0 ? "ok" : $"{collisions.Count} COLLISION(S)";
            Console.WriteLine($"  {Path.GetFileName(specPath),-28} paths={paths.Children.Count,-4} operations={operations,-4} {status}");

            if (collisions.Count > 0)
            {
                failed = true;
                Report(collisions);
            }
        }

        return failed ? 1 : 0;
    }

    /// <summary>
    /// Groups paths by the signature Kiota derives from them: every path
    /// parameter flattened to a single placeholder. More than one path per
    /// signature means Kiota will merge those nodes and discard operations.
    /// </summary>
    private static Dictionary<string, List<string>> FindCollisions(IEnumerable<string> paths)
    {
        return paths
            .GroupBy(p => Regex.Replace(p, @"\{[^}]+\}", "{}"))
            .Where(g => g.Count() > 1)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
    }

    private static void Report(Dictionary<string, List<string>> collisions)
    {
        foreach (var (signature, members) in collisions)
        {
            Console.Error.WriteLine($"  {signature}  <=  {string.Join("  ", members)}");
        }
    }

    private static readonly string[] HttpMethods =
        ["get", "put", "post", "delete", "options", "head", "patch", "trace"];

    private static int CountOperations(YamlNode pathItem) =>
        pathItem is YamlMappingNode m
            ? m.Children.Count(kv => HttpMethods.Contains(Key(kv.Key), StringComparer.Ordinal))
            : 0;

    /// <summary>
    /// Distinguishes the sub-documents in Kiota's logs and in any tooling that
    /// reads the title. Operates on the emitted copy only.
    /// </summary>
    private static void RetitleInPlace(YamlMappingNode document, string groupName)
    {
        var infoKey = new YamlScalarNode("info");
        if (!document.Children.TryGetValue(infoKey, out var infoNode) || infoNode is not YamlMappingNode info)
        {
            return;
        }

        var titleKey = new YamlScalarNode("title");
        var title = info.Children.TryGetValue(titleKey, out var t) ? Key(t) : "Kapso WhatsApp API";

        var replacement = new YamlMappingNode();
        foreach (var kv in info.Children)
        {
            replacement.Add(kv.Key, Key(kv.Key) == "title" ? new YamlScalarNode($"{title} ({groupName})") : kv.Value);
        }

        document.Children[infoKey] = replacement;
    }

    private static (YamlMappingNode Root, YamlStream Stream) Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"spec not found: {path}");
        }

        var stream = new YamlStream();
        using var reader = new StreamReader(path);
        stream.Load(reader);
        return ((YamlMappingNode)stream.Documents[0].RootNode, stream);
    }

    private static void Save(YamlMappingNode document, string path)
    {
        // Written with a trailing newline and LF endings so the committed files
        // are byte-identical regardless of the machine that regenerates them.
        using var writer = new StreamWriter(path) { NewLine = "\n" };
        new YamlStream(new YamlDocument(document)).Save(writer, assignAnchors: false);
    }

    private static string Key(YamlNode node) => ((YamlScalarNode)node).Value ?? string.Empty;

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}
