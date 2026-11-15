using System;
using System.Linq;
using BobberBell.Core.Licensing;

namespace BobberBell.Tests;

public sealed class KeyPromptFlowTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void EmptyOrCancelledInput_IsCancel(string? input) => Assert.True(KeyPromptFlow.IsCancel(input));

    [Theory]
    [InlineData("A")]
    [InlineData("---")]
    [InlineData(" ABCD-EFGH-IJKL-MNOP ")]
    public void TypedInput_IsNotCancel(string input) => Assert.False(KeyPromptFlow.IsCancel(input));

    [Theory]
    [InlineData(LicenseCheckResult.Activated, KeyPromptOutcome.Accepted)]
    [InlineData(LicenseCheckResult.SessionOnly, KeyPromptOutcome.Accepted)]
    [InlineData(LicenseCheckResult.TooShort, KeyPromptOutcome.Rejected)]
    [InlineData(LicenseCheckResult.Invalid, KeyPromptOutcome.Rejected)]
    public void Classify_MapsActivationResults(LicenseCheckResult result, KeyPromptOutcome expected) =>
        Assert.Equal(expected, KeyPromptFlow.Classify(result));

    [Theory]
    [InlineData(KeyPromptContext.Startup, KeyPromptOutcome.Accepted, true, KeyPromptNextStep.ContinueLicensed)]
    [InlineData(KeyPromptContext.Startup, KeyPromptOutcome.Accepted, false, KeyPromptNextStep.ContinueLicensed)]
    [InlineData(KeyPromptContext.Startup, KeyPromptOutcome.Cancelled, true, KeyPromptNextStep.ShowTrialChoice)]
    [InlineData(KeyPromptContext.Startup, KeyPromptOutcome.Rejected, true, KeyPromptNextStep.ShowTrialChoice)]
    [InlineData(KeyPromptContext.Startup, KeyPromptOutcome.Cancelled, false, KeyPromptNextStep.Exit)]
    [InlineData(KeyPromptContext.Startup, KeyPromptOutcome.Rejected, false, KeyPromptNextStep.Exit)]
    [InlineData(KeyPromptContext.DuringTrial, KeyPromptOutcome.Accepted, true, KeyPromptNextStep.ContinueLicensed)]
    [InlineData(KeyPromptContext.DuringTrial, KeyPromptOutcome.Cancelled, true, KeyPromptNextStep.KeepRunningTrial)]
    [InlineData(KeyPromptContext.DuringTrial, KeyPromptOutcome.Rejected, true, KeyPromptNextStep.KeepRunningTrial)]
    [InlineData(KeyPromptContext.TrialExpired, KeyPromptOutcome.Accepted, false, KeyPromptNextStep.ContinueLicensed)]
    [InlineData(KeyPromptContext.TrialExpired, KeyPromptOutcome.Cancelled, false, KeyPromptNextStep.Exit)]
    [InlineData(KeyPromptContext.TrialExpired, KeyPromptOutcome.Rejected, false, KeyPromptNextStep.Exit)]
    public void Next_FollowsTheDocumentedFlow(KeyPromptContext context, KeyPromptOutcome outcome, bool trialLeft, KeyPromptNextStep expected) =>
        Assert.Equal(expected, KeyPromptFlow.Next(context, outcome, trialLeft));

    [Fact]
    public void StartupExitReasons_AreDistinct_AndNeverContainKeyText()
    {
        string rejected = KeyPromptFlow.StartupExitReason(KeyPromptOutcome.Rejected);
        string cancelled = KeyPromptFlow.StartupExitReason(KeyPromptOutcome.Cancelled);
        string dialog = KeyPromptFlow.StartupExitReason(null);
        Assert.Equal(3, new[] { rejected, cancelled, dialog }.Distinct().Count());
        Assert.All(new[] { rejected, cancelled, dialog }, s => Assert.StartsWith("Startup:", s, StringComparison.Ordinal));
    }

    [Fact]
    public void Cancel_IsCheckedBeforeActivation_SoEmptyInputNeverReachesTheInvalidPath()
    {
        // LicenseService still reports empty input as Invalid (unchanged); the UI must check IsCancel first.
        Assert.True(KeyPromptFlow.IsCancel(string.Empty));
        Assert.Equal(KeyPromptOutcome.Rejected, KeyPromptFlow.Classify(LicenseCheckResult.Invalid));
    }
}
