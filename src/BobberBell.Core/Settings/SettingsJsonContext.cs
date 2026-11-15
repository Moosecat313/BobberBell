using System.Text.Json;
using System.Text.Json.Serialization;

namespace BobberBell.Core.Settings;

/// <summary>
/// Source-generated, strongly-typed JSON contract for <see cref="AppSettings"/>: no reflection, no polymorphic
/// type handling, strict JSON (no comments or trailing commas), shallow depth, numbers must be JSON numbers.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    MaxDepth = 8,
    AllowTrailingCommas = false,
    ReadCommentHandling = JsonCommentHandling.Disallow,
    NumberHandling = JsonNumberHandling.Strict,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    AllowDuplicateProperties = false)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext
{
}
