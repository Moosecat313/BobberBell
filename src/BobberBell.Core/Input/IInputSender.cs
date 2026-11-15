using System.Threading.Tasks;

namespace BobberBell.Core.Input;

/// <summary>Sends one key press or mouse-button click to the foreground window.</summary>
public interface IInputSender
{
    /// <summary>Presses and releases <paramref name="key"/>. The key is always released, even if the caller stops mid-hold.</summary>
    /// <param name="key">An allow-listed key from <see cref="KeyCatalog"/>.</param>
    /// <returns>A task that completes after the key is released.</returns>
    Task PressAsync(KeyOption key);
}
