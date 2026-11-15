using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BobberBell.Core.Audio;
using BobberBell.Core.Logging;
using SoundFingerprinting.Audio;
using SoundFingerprinting.Builder;
using SoundFingerprinting.Data;
using SoundFingerprinting.InMemory;

namespace BobberBell.Core.Detection;

/// <summary>
/// Platform-neutral detection core: an in-memory fingerprint index of the reference sounds, plus
/// <see cref="ListenSession"/>s that take raw capture bytes, convert them to 5512 Hz mono and run a
/// SoundFingerprinting realtime query against the index. All audio stays in memory.
/// </summary>
public sealed class SplashFingerprintEngine
{
    private readonly InMemoryModelService modelService = new();
    private readonly IAppLogger logger;

    // trackId -> label. Replaced as a whole (never mutated after publish), so query callbacks see a consistent set.
    private volatile IReadOnlyDictionary<string, string> activeTracks = new Dictionary<string, string>();

    /// <summary>Creates the engine.</summary>
    /// <param name="logger">Application logger.</param>
    public SplashFingerprintEngine(IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        this.logger = logger;
    }

    /// <summary>Raised (on a query thread) with the best similarity of each fingerprint query against the references.</summary>
    public event Action<float>? SimilarityMeasured;

    /// <summary>Gets a value indicating whether at least one reference sound is loaded.</summary>
    public bool HasReferences => activeTracks.Count > 0;

    internal IAppLogger Logger => logger;

    internal InMemoryModelService ModelService => modelService;

    internal IReadOnlyDictionary<string, string> ActiveTracks => activeTracks;

    /// <summary>
    /// Fingerprints the given files and makes them the only references. <paramref name="audioService"/> decodes each
    /// file to 5512 Hz mono (SoundFingerprinting asks it for exactly that), the same format the live query uses.
    /// </summary>
    /// <param name="audioService">Decoder for the reference files.</param>
    /// <param name="sounds">Canonical file paths and the label reported when each is heard.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when all references are loaded.</returns>
    public async Task LoadReferencesAsync(IAudioService audioService, IReadOnlyList<(string Path, string Label)> sounds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(audioService);
        ArgumentNullException.ThrowIfNull(sounds);
        var old = activeTracks;
        activeTracks = new Dictionary<string, string>();
        foreach (var id in old.Keys)
        {
            try { await modelService.DeleteTrackAsync(id).ConfigureAwait(false); }
            catch (InvalidOperationException) { }
        }

        var loaded = new Dictionary<string, string>();
        foreach (var (filePath, label) in sounds)
        {
            ct.ThrowIfCancellationRequested();
            string id = Guid.NewGuid().ToString();
            var hashes = await FingerprintCommandBuilder.Instance
                .BuildFingerprintCommand()
                .From(filePath)
                .UsingServices(audioService)
                .Hash()
                .ConfigureAwait(false);
            await modelService.InsertAsync(new TrackInfo(id, label, "library"), hashes).ConfigureAwait(false);
            loaded[id] = label;
        }

        activeTracks = loaded;
    }

    /// <summary>Starts a listen session for a capture stream in <paramref name="format"/>.</summary>
    /// <param name="format">Native capture format.</param>
    /// <param name="thresholds">Volume and similarity gates.</param>
    /// <param name="onLevel">Optional callback with each chunk's peak dBFS (original audio).</param>
    /// <param name="ct">Cancellation token for the session.</param>
    /// <returns>The running session; dispose it to stop the query.</returns>
    /// <exception cref="NotSupportedException">The capture format cannot be decoded.</exception>
    public ListenSession StartSession(AudioSampleFormat format, DetectionThresholds thresholds, Action<float>? onLevel, CancellationToken ct)
        => new(this, new LoopbackConverter(format), thresholds, onLevel, ct);

    // Test seam: replaces the SoundFingerprinting query with a delegate.
    internal ListenSession StartSession(AudioSampleFormat format, DetectionThresholds thresholds, Func<CancellationToken, Task> queryOverride, CancellationToken ct)
        => new(this, new LoopbackConverter(format), thresholds, null, ct, queryOverride);

    internal void ReportSimilarity(float similarity) => SimilarityMeasured?.Invoke(similarity);
}
