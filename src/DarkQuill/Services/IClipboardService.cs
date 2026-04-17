namespace DarkQuill.Services;

/// <summary>
/// Abstracts system clipboard operations for MVVM testability.
/// </summary>
public interface IClipboardService
{
    /// <summary>
    /// Copies text to the system clipboard.
    /// </summary>
    /// <param name="text">The text to copy.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CopyToClipboardAsync(string text, CancellationToken cancellationToken = default);
}
