using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Buddy.Server;

namespace Buddy.RegionCompletion;

public sealed record RegionValueFact(string[] Names, string[] Values);
public sealed record RegionExpectation(string Question, string[][] RequiredFactAlternatives, string[] ForbiddenClaims, RegionValueFact[]? ValueFacts = null);
public sealed record RegionFixture(byte[] Png, RegionExpectation Expectation, string Origin)
{
    public string Sha256 => Convert.ToHexString(SHA256.HashData(Png)).ToLowerInvariant();
    public const string ReviewedQuery = "What causes rainbows in water droplets?";
    public static RegionFixture Synthetic() => new(PngChart.Create(), new(
        "Explain what the two bars in this selected image show, comparing their values and heights.",
        [["blue", "bar b"], ["orange", "bar a"], ["taller", "higher", "three times", "3 times"], ["six", "6"], ["two", "2"]],
        ["red bar", "green bar", "equal height", "same height"],
        [new(["blue", "bar B", "right bar"], ["6", "six"]), new(["orange", "bar A", "left bar"], ["2", "two"])]), "deterministic synthetic two-bar chart; not a captured desktop region");

    public static RegionFixture Owned(string pngPath, string expectationPath)
    {
        byte[] png = ReadOwned(pngPath, ".png", 2_000_000);
        byte[] spec = ReadOwned(expectationPath, ".json", 8000);
        var expectation = JsonSerializer.Deserialize<RegionExpectation>(spec, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new ArgumentException("Missing owned-image expectation.");
        if (expectation.Question is not { Length: > 0 and <= 500 } || expectation.RequiredFactAlternatives is not { Length: >= 2 and <= 8 } ||
            expectation.RequiredFactAlternatives.Any(g => g is not { Length: > 0 and <= 8 } || g.Any(t => string.IsNullOrWhiteSpace(t) || t.Length > 100)) ||
            expectation.ForbiddenClaims is null || expectation.ForbiddenClaims.Any(t => string.IsNullOrWhiteSpace(t) || t.Length > 100) ||
            expectation.ValueFacts is { } facts && (facts.Length > 8 || facts.Any(f => f is null || f.Names is not { Length: > 0 and <= 8 } || f.Values is not { Length: > 0 and <= 8 } || f.Names.Concat(f.Values).Any(t => string.IsNullOrWhiteSpace(t) || t.Length > 100)))) throw new ArgumentException("Provide a bounded canned question and at least two independent known visual facts.");
        if (png.Length < 24 || !png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
            BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16, 4)) is 0 or > 2048 || BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20, 4)) is 0 or > 2048)
            throw new ArgumentException("Use an owned PNG no larger than 2048 by 2048 pixels.");
        return new(png, expectation, "explicitly supplied owned recorded PNG; ownership/nonprivate declaration by root operator");
    }

    private static byte[] ReadOwned(string path, string extension, int maxBytes)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\") || Path.GetExtension(path).ToLowerInvariant() != extension)
            throw new ArgumentException("Use an explicit absolute local owned-fixture file path.");
        var file = new FileInfo(path);
        if (!file.Exists || file.Length is < 1 || file.Length > maxBytes || (file.Attributes & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Fixture file missing, linked, empty or oversized.");
        for (var parent = file.Directory; parent is not null; parent = parent.Parent)
            if ((parent.Attributes & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Linked fixture ancestors are not supported.");
        using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maxBytes) throw new ArgumentException("Fixture grew beyond its bound.");
        var bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes); return bytes;
    }
}

public sealed record CompletionVerdict(bool Passed, string[] Failures);
public static class RegionOracle
{
    private static bool Contains(string text, string phrase) => Regex.IsMatch(text, @"(?<![\p{L}\p{N}])" + Regex.Escape(phrase) + @"(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    public static CompletionVerdict Image(TeachingTurn turn, RegionExpectation expectation)
    {
        var errors = Common(turn.Speech);
        if (turn.Targets.Count != 0) errors.Add("Explanation invented annotation targets for a question with no observed controls.");
        if (Regex.IsMatch(turn.Speech, @"\b(click|press|tap|select|open|launch|type|I have|I've)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) errors.Add("Explanation contains an unrequested action or performed-action claim.");
        for (int i = 0; i < expectation.RequiredFactAlternatives.Length; i++)
            if (!expectation.RequiredFactAlternatives[i].Any(t => Contains(turn.Speech, t))) errors.Add("Missing independent visual fact group " + (i + 1));
        if (expectation.ForbiddenClaims.Any(t => Contains(turn.Speech, t))) errors.Add("Contradicts a known visual fact.");
        if (expectation.ValueFacts is { } facts) CheckValueRelations(turn.Speech, facts, errors);
        if (turn.Speech.Trim().Equals(expectation.Question.Trim(), StringComparison.OrdinalIgnoreCase)) errors.Add("Echoes the question.");
        return new(errors.Count == 0, errors.ToArray());
    }
    private static string Terms(IEnumerable<string> values) => @"(?<![\p{L}\p{N}])(?:" + string.Join('|', values.OrderByDescending(v => v.Length).Select(Regex.Escape)) + @")(?![\p{L}\p{N}])";
    private static void CheckValueRelations(string speech, RegionValueFact[] facts, List<string> errors)
    {
        const RegexOptions options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        var limit = TimeSpan.FromMilliseconds(100);
        string numbers = Terms(facts.SelectMany(f => f.Values).Distinct(StringComparer.OrdinalIgnoreCase));
        foreach (var fact in facts) {
            bool found = false;
            var others = facts.Where(f => !ReferenceEquals(f, fact)).SelectMany(f => f.Names).ToArray();
            foreach (Match name in Regex.Matches(speech, Terms(fact.Names), options, limit)) {
                string tail = speech[(name.Index + name.Length)..];
                int bound = Math.Min(120, tail.Length);
                var punctuation = Regex.Match(tail, @"[.;!?]", options, limit);
                if (punctuation.Success) bound = Math.Min(bound, punctuation.Index);
                if (others.Length > 0) { var other = Regex.Match(tail, Terms(others), options, limit); if (other.Success) bound = Math.Min(bound, other.Index); }
                var value = Regex.Match(tail[..bound], numbers, options, limit);
                if (!value.Success) continue;
                if (!fact.Values.Contains(value.Value, StringComparer.OrdinalIgnoreCase)) errors.Add("Value is assigned to the wrong object: " + name.Value + "=" + value.Value);
                else found = true;
            }
            if (!found) errors.Add("No explicit correct object/value relation for " + fact.Names[0]);
        }
    }
    public static CompletionVerdict Research(ReviewedRegionResearchResult result, IReadOnlyList<WebSource> fetched)
    {
        var errors = Common(result.Speech);
        if (result.Sources.Count is < 1 or > 3) errors.Add("Need one to three actual fetched sources.");
        foreach (var source in result.Sources) {
            try { WebResearch.ValidateUrl(source.Url); }
            catch { errors.Add("Source is not public HTTPS."); }
            if (string.IsNullOrWhiteSpace(source.Title) || !fetched.Any(p => p.Url == source.Url && !string.IsNullOrWhiteSpace(p.Text))) errors.Add("Source is not a usable page fetched by this request.");
        }
        if (!new[] { "light", "sunlight" }.Any(t => Contains(result.Speech, t)) ||
            !new[] { "water", "droplets", "raindrops", "drops" }.Any(t => Contains(result.Speech, t)) ||
            !new[] { "refract", "refracts", "refraction", "refracted", "bends", "bending" }.Any(t => Contains(result.Speech, t)) ||
            !new[] { "reflect", "reflects", "reflection", "reflected" }.Any(t => Contains(result.Speech, t))) errors.Add("Does not explain the independently expected rainbow mechanism (light, droplets, refraction and reflection).");
        if (Regex.IsMatch(result.Speech, @"https?://|www\.|sources?\s+(?:are|is)\s+attached", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) errors.Add("Speech contains a raw address or model-authored attachment claim.");
        return new(errors.Count == 0, errors.ToArray());
    }
    private static List<string> Common(string speech)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(speech) || !ConversationalReply.IsConcise(speech)) errors.Add("Not a complete concise answer of at most three sentences.");
        if (Regex.IsMatch(speech, @"couldn't|cannot (?:verify|read|see|determine)|do not establish|which (?:part|point)|focus the|try a more specific|I can't|unclear|unreadable", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) errors.Add("Clarification/fallback is safe but does not complete this canned question.");
        return errors;
    }
}

// Pure managed PNG encoder: no desktop APIs, native drawing library or capture.
public static class PngChart
{
    public const int Width = 320, Height = 250;
    public static byte[] Create()
    {
        var rgb = Enumerable.Repeat((byte)255, Width * Height * 3).ToArray();
        void Box(int x, int y, int w, int h, byte r, byte g, byte b) { for (int row = y; row < y + h; row++) for (int col = x; col < x + w; col++) { int i = (row * Width + col) * 3; rgb[i] = r; rgb[i + 1] = g; rgb[i + 2] = b; } }
        Box(32, 210, 252, 2, 0, 0, 0); Box(32, 25, 2, 187, 0, 0, 0);
        Box(70, 150, 55, 60, 230, 125, 35); Box(190, 30, 55, 180, 35, 90, 210);
        void Glyph(int x, int y, string rows) { var lines = rows.Split('/'); for (int row = 0; row < lines.Length; row++) for (int col = 0; col < 5; col++) if (lines[row][col] == '1') Box(x + col * 2, y + row * 2, 2, 2, 0, 0, 0); }
        Glyph(92, 219, "01110/10001/10001/11111/10001/10001/10001");
        Glyph(212, 219, "11110/10001/10001/11110/10001/10001/11110");
        Glyph(92, 130, "01110/10001/00001/00010/00100/01000/11111");
        Glyph(212, 10, "01110/10000/10000/11110/10001/10001/01110");
        using var packed = new MemoryStream();
        using (var zlib = new ZLibStream(packed, CompressionLevel.SmallestSize, true))
            for (int y = 0; y < Height; y++) { zlib.WriteByte(0); zlib.Write(rgb, y * Width * 3, Width * 3); }
        using var png = new MemoryStream(); png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        byte[] header = new byte[13]; BinaryPrimitives.WriteUInt32BigEndian(header, Width); BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), Height); header[8] = 8; header[9] = 2;
        Chunk(png, "IHDR", header); Chunk(png, "IDAT", packed.ToArray()); Chunk(png, "IEND", []); return png.ToArray();
    }
    private static void Chunk(Stream stream, string type, byte[] data)
    {
        var name = Encoding.ASCII.GetBytes(type); Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(number, (uint)data.Length); stream.Write(number); stream.Write(name); stream.Write(data);
        uint crc = uint.MaxValue;
        foreach (byte value in name.Concat(data)) { crc ^= value; for (int i = 0; i < 8; i++) crc = (crc & 1) != 0 ? 0xedb88320u ^ (crc >> 1) : crc >> 1; }
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); stream.Write(number);
    }
}
