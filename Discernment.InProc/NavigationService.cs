using System;
using System.Threading.Tasks;
using Discernment.Contracts;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;

namespace DiscernmentInProc
{
    /// <summary>
    /// Implementation of navigation service that runs in-process and can interact with VS APIs.
    /// </summary>
    internal class NavigationService : INavigationService
    {
        private readonly DTE2 _dte;

        public NavigationService(DTE2 dte)
        {
            _dte = dte ?? throw new ArgumentNullException(nameof(dte));
        }

        /// <inheritdoc />
        public async Task NavigateToSourceAsync(string filePath, int lineNumber, int columnNumber = 1)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                System.Diagnostics.Debug.WriteLine($"NavigationService: Navigating to {filePath}:{lineNumber}:{columnNumber}");

                // Open the document
                var window = _dte.ItemOperations.OpenFile(filePath, Constants.vsViewKindCode);
                
                if (window?.Document?.Selection is TextSelection selection)
                {
                    // Move to the specified line and column
                    selection.MoveToLineAndOffset(lineNumber, columnNumber, false);
                    
                    // Select the entire line for visibility
                    selection.SelectLine();
                    
                    // Ensure the line is visible in the editor
                    selection.MoveToLineAndOffset(lineNumber, columnNumber, false);
                    
                    // Activate the window to ensure it has focus
                    window.Activate();

                    System.Diagnostics.Debug.WriteLine($"NavigationService: Successfully navigated to {filePath}:{lineNumber}:{columnNumber}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("NavigationService: Warning - Window or Selection is null");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NavigationService: Navigation failed: {ex.Message}");
                throw new InvalidOperationException($"Failed to navigate to {filePath}:{lineNumber}:{columnNumber}", ex);
            }
        }
    }
}
