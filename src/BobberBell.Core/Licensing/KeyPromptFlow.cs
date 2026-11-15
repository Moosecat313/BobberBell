namespace BobberBell.Core.Licensing;

/// <summary>What the user did at the license key prompt.</summary>
public enum KeyPromptOutcome
{
    /// <summary>The key was accepted (saved, or valid for this session only).</summary>
    Accepted,

    /// <summary>The prompt was cancelled or closed, or OK was pressed with nothing typed. No error is shown.</summary>
    Cancelled,

    /// <summary>A key was typed but it is too short or unknown. The error message is shown.</summary>
    Rejected,
}

/// <summary>Where the key prompt was opened from.</summary>
public enum KeyPromptContext
{
    /// <summary>At startup, with no valid stored license.</summary>
    Startup,

    /// <summary>From the trial footer or the License button while the trial is running.</summary>
    DuringTrial,

    /// <summary>After the trial time ran out.</summary>
    TrialExpired,
}

/// <summary>What the app does after the key prompt closes.</summary>
public enum KeyPromptNextStep
{
    /// <summary>Run (or keep running) as licensed.</summary>
    ContinueLicensed,

    /// <summary>Show the startup choice between a key and the trial again.</summary>
    ShowTrialChoice,

    /// <summary>Close the prompt and keep running the trial.</summary>
    KeepRunningTrial,

    /// <summary>Exit the app (quietly when cancelled).</summary>
    Exit,
}

/// <summary>
/// UI-independent decisions around the license key prompt. The key check itself stays in <see cref="LicenseService"/>;
/// this only decides whether input counts as a cancel and what happens next.
/// </summary>
public static class KeyPromptFlow
{
    /// <summary>Returns true when the prompt should be treated as cancelled: closed, cancelled, or nothing but whitespace entered.</summary>
    /// <param name="input">The text returned by the prompt, or <c>null</c> when it was cancelled.</param>
    /// <returns><c>true</c> for a cancel; <c>false</c> when there is something to check.</returns>
    public static bool IsCancel(string? input) => string.IsNullOrWhiteSpace(input);

    /// <summary>Maps a <see cref="LicenseService.Activate"/> result to a prompt outcome.</summary>
    /// <param name="result">The activation result.</param>
    /// <returns><see cref="KeyPromptOutcome.Accepted"/> or <see cref="KeyPromptOutcome.Rejected"/>.</returns>
    public static KeyPromptOutcome Classify(LicenseCheckResult result) =>
        result is LicenseCheckResult.Activated or LicenseCheckResult.SessionOnly
            ? KeyPromptOutcome.Accepted
            : KeyPromptOutcome.Rejected;

    /// <summary>Decides what the app does after the key prompt.</summary>
    /// <param name="context">Where the prompt was opened from.</param>
    /// <param name="outcome">What the user did.</param>
    /// <param name="trialTimeLeft">Whether this PC still has trial time (only used at startup).</param>
    /// <returns>The next step.</returns>
    public static KeyPromptNextStep Next(KeyPromptContext context, KeyPromptOutcome outcome, bool trialTimeLeft)
    {
        if (outcome == KeyPromptOutcome.Accepted) return KeyPromptNextStep.ContinueLicensed;
        return context switch
        {
            KeyPromptContext.Startup => trialTimeLeft ? KeyPromptNextStep.ShowTrialChoice : KeyPromptNextStep.Exit,
            KeyPromptContext.DuringTrial => KeyPromptNextStep.KeepRunningTrial,
            _ => KeyPromptNextStep.Exit,
        };
    }

    /// <summary>The log line written when the app exits at startup without a license.</summary>
    /// <param name="outcome">The last key prompt outcome, or <c>null</c> when the startup dialog itself was cancelled.</param>
    /// <returns>A short reason, with no key text in it.</returns>
    public static string StartupExitReason(KeyPromptOutcome? outcome) => outcome switch
    {
        KeyPromptOutcome.Rejected => "Startup: license key not accepted → closing",
        KeyPromptOutcome.Cancelled => "Startup: no license key entered → closing",
        _ => "Startup: cancelled → closing",
    };
}
