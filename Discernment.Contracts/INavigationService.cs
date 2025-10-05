using System.Threading.Tasks;

namespace Discernment.Contracts
{
    /// <summary>
    /// Service interface for navigation operations that can be called by out-of-process extensions.
    /// </summary>
    public interface INavigationService
    {
        /// <summary>
        /// Navigates to a specific line in a source file.
        /// </summary>
        /// <param name="filePath">The full path to the file to navigate to.</param>
        /// <param name="lineNumber">The line number to navigate to (1-based).</param>
        /// <param name="columnNumber">Optional column number (1-based). If not specified, defaults to 1.</param>
        /// <returns>A task representing the navigation operation.</returns>
        Task NavigateToSourceAsync(string filePath, int lineNumber, int columnNumber = 1);
    }
}
