using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Editor;
using Microsoft.VisualStudio.Extensibility.Shell;
using Microsoft.VisualStudio.Extensibility.ToolWindows;
using Microsoft.VisualStudio.RpcContracts.RemoteUI;

namespace Discernment
{
    /// <summary>
    /// Tool window for displaying variable insight graph.
    /// </summary>
    [VisualStudioContribution]
    internal class VariableInsightWindow : ToolWindow
    {
        /// <summary>
        /// Shared data context for the tool window (since VS only creates one instance).
        /// </summary>
        internal static readonly VariableInsightWindowDataContext DataContext = new();
        
        private VariableInsightWindowControl? control;
        private IClientContext? clientContext;

        /// <summary>
        /// Singleton instance of the tool window (VS.Extensibility creates only one instance).
        /// </summary>
        internal static VariableInsightWindow? Instance { get; private set; }

        public VariableInsightWindow()
        {
            Instance = this;
        }

        /// <summary>
        /// Sets the client context for editor operations.
        /// </summary>
        public void SetClientContext(IClientContext context)
        {
            this.clientContext = context;
        }

        /// <inheritdoc />
        public override ToolWindowConfiguration ToolWindowConfiguration => new()
        {
            Placement = ToolWindowPlacement.DocumentWell,
        };

        /// <inheritdoc />
        public override Task<IRemoteUserControl> GetContentAsync(CancellationToken cancellationToken)
        {
            this.control = new VariableInsightWindowControl(DataContext);
            return Task.FromResult<IRemoteUserControl>(this.control);
        }

        /// <summary>
        /// Updates the tool window with a new variable insight graph.
        /// </summary>
        public void UpdateGraph(VariableInsightGraph graph, string filePath)
        {
            // Update the static DataContext - it's shared across all instances
            DataContext.UpdateGraph(graph, filePath);
        }

        /// <summary>
        /// Navigates to the specified line in the source file.
        /// </summary>
        public async Task NavigateToSourceAsync(string filePath, int lineNumber, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(filePath) || lineNumber <= 0 || !File.Exists(filePath))
                return;

            if (this.clientContext == null)
            {
                System.Diagnostics.Debug.WriteLine("ClientContext not available for navigation");
                return;
            }

            try
            {


                // Unfortunately, VS.Extensibility (out-of-process) does not provide APIs
                // to programmatically navigate to lines in the editor from tool windows.
                // The Selection object is read-only and has no SetSelection methods.
                
                // Show a non-blocking notification with the navigation info
                //await this.Extensibility.Shell().ShowPromptAsync(
                //    $"📍 Navigate to Line {lineNumber}\n\n" +
                //    $"File: {Path.GetFileName(filePath)}\n" +
                //    $"Line: {lineNumber}\n\n" +
                //    $"💡 Press Ctrl+G in the editor and enter {lineNumber}",
                //    PromptOptions.OK,
                //    cancellationToken);
                
                System.Diagnostics.Debug.WriteLine($"Notified user to navigate to {filePath}:{lineNumber}");
            }
            catch (Exception ex)
            {
                // Log error but don't throw - this is a best-effort operation
                System.Diagnostics.Debug.WriteLine($"Failed to navigate to source: {ex.Message}");
            }
        }
    }
}

