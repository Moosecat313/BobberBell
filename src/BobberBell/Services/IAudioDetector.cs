using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BobberBell.Core.Detection;
using NAudio.CoreAudioApi;

namespace BobberBell.Services;

/// <summary>Listens to system audio and reports when a reference sound is heard.</summary>
internal interface IAudioDetector : IAsyncDisposable
{
    /// <summary>Raised (on a capture thread) with the peak level of each captured chunk in dBFS.</summary>
    event Action<float>? LevelMeasured;

    /// <summary>Raised (on a query thread) with the best similarity of each fingerprint query against the references.</summary>
    event Action<float>? SimilarityMeasured;

    /// <summary>Gets a value indicating whether at least one reference sound is loaded.</summary>
    bool HasReferences { get; }

    /// <summary>Fingerprints the given (already validated) files and makes them the only references.</summary>
    /// <param name="sounds">Canonical file paths and the label reported when each is heard.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when all references are loaded.</returns>
    Task LoadReferencesAsync(IReadOnlyList<(string Path, string Label)> sounds, CancellationToken ct = default);

    /// <summary>Captures loopback audio until a reference is heard or <paramref name="window"/> elapses.</summary>
    /// <param name="device">Output device to capture, or <c>null</c> for the default device.</param>
    /// <param name="thresholds">Volume and similarity gates.</param>
    /// <param name="window">Maximum listen time.</param>
    /// <param name="ct">Cancellation token; throws <see cref="OperationCanceledException"/> when cancelled.</param>
    /// <returns>The label of the sound heard, or <c>null</c> on timeout.</returns>
    Task<string?> ListenAsync(MMDevice? device, DetectionThresholds thresholds, TimeSpan window, CancellationToken ct);

    /// <summary>Stops any capture immediately (used by Stop and on close).</summary>
    void Stop();
}
