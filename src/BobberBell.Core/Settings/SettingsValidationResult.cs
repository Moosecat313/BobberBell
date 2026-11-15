using System.Collections.Generic;

namespace BobberBell.Core.Settings;

/// <summary>Sanitized settings plus human-readable notes about every value that was corrected.</summary>
/// <param name="Settings">Settings with every value inside its allowed range.</param>
/// <param name="Notes">Log lines describing corrections, in order.</param>
public sealed record SettingsValidationResult(AppSettings Settings, IReadOnlyList<string> Notes);
