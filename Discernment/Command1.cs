using System;
using System.Diagnostics;
using System.Linq;
using Microsoft;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Editor;
using Microsoft.VisualStudio.Extensibility.Shell;

namespace Discernment
{
    /// <summary>
    /// Variable Insight command handler.
    /// </summary>
    [VisualStudioContribution]
    internal class Command1 : Command
    {
        private readonly TraceSource logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="Command1"/> class.
        /// </summary>
        /// <param name="traceSource">Trace source instance to utilize.</param>
        public Command1(TraceSource traceSource)
        {
            this.logger = Requires.NotNull(traceSource, nameof(traceSource));
        }

        /// <inheritdoc />
        public override CommandConfiguration CommandConfiguration => new("%Discernment.Command1.DisplayName%")
        {
            Icon = new(ImageMoniker.KnownValues.Search, IconSettings.IconAndText),
            Placements = [
                CommandPlacement.KnownPlacements.ExtensionsMenu,
                CommandPlacement.KnownPlacements.ToolsMenu,
            ],
        };

        /// <inheritdoc />
        public override Task InitializeAsync(CancellationToken cancellationToken)
        {
            return base.InitializeAsync(cancellationToken);
        }

        /// <inheritdoc />
        public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
        {
            try
            {
                // Get the active text view
                var textView = await context.GetActiveTextViewAsync(cancellationToken);
                
                if (textView == null)
                {
                    await this.Extensibility.Shell().ShowPromptAsync(
                        "No active editor window. Please open a C# file and select a variable.",
                        PromptOptions.OK,
                        cancellationToken);
                    return;
                }

                // Get the selection
                var selection = textView.Selection;
                if (selection.IsEmpty)
                {
                    await this.Extensibility.Shell().ShowPromptAsync(
                        "No text selected. Please select a variable or place the cursor on a variable name.",
                        PromptOptions.OK,
                        cancellationToken);
                    return;
                }

                var document = textView.Document;
                var textRange = document.Text;
                
                // Convert TextRange to string
                var buffer = new char[textRange.Length];
                textRange.CopyTo(buffer);
                var documentText = new string(buffer);
                
                var documentPath = textView.FilePath ?? "temp.cs";

                // Check if this is a C# file
                if (!documentPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    await this.Extensibility.Shell().ShowPromptAsync(
                        "This extension only works with C# files.",
                        PromptOptions.OK,
                        cancellationToken);
                    return;
                }

                // Create a Roslyn workspace and document
                var workspace = new AdhocWorkspace();
                var projectInfo = ProjectInfo.Create(
                    ProjectId.CreateNewId(),
                    VersionStamp.Create(),
                    "TempProject",
                    "TempProject",
                    LanguageNames.CSharp,
                    metadataReferences: new[]
                    {
                        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                        MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
                    });

                var project = workspace.AddProject(projectInfo);
                var roslynDocument = workspace.AddDocument(
                    project.Id,
                    System.IO.Path.GetFileName(documentPath),
                    SourceText.From(documentText));

                // Analyze the variable at the selection position
                var analyzer = new VariableInsightAnalyzer();
                var position = selection.Start.Offset;
                var graph = await analyzer.AnalyzeAsync(roslynDocument, position, cancellationToken);

                if (graph == null)
                {
                    await this.Extensibility.Shell().ShowPromptAsync(
                        "Could not analyze the selected symbol. Please ensure you have selected a variable, field, parameter, or property.",
                        PromptOptions.OK,
                        cancellationToken);
                    return;
                }

                // Update the data context BEFORE showing the window
                // This ensures that on first load, GetContentAsync will use the correct data
                if (VariableInsightWindow.Instance != null)
                {
                    // Window already exists, update it
                    VariableInsightWindow.Instance.UpdateGraph(graph, documentPath);
                    VariableInsightWindow.Instance.SetClientContext(context);
                }
                else
                {
                    // Window doesn't exist yet, pre-populate the static data context
                    VariableInsightWindow.DataContext.UpdateGraph(graph, documentPath);
                }
                
                // Now show the tool window (creates instance if needed, or activates existing)
                await this.Extensibility.Shell().ShowToolWindowAsync<VariableInsightWindow>(activate: true, cancellationToken);
                
                // Set context after window is created
                if (VariableInsightWindow.Instance != null)
                {
                    VariableInsightWindow.Instance.SetClientContext(context);
                }
                
                this.logger.TraceInformation($"Variable Insight analysis completed for '{graph.RootNode.Name}'. Found {graph.TotalReferences} related elements.");
            }
            catch (Exception ex)
            {
                this.logger.TraceEvent(TraceEventType.Error, 0, $"Error during Variable Insight analysis: {ex}");
                await this.Extensibility.Shell().ShowPromptAsync(
                    $"An error occurred during analysis: {ex.Message}",
                    PromptOptions.OK,
                    cancellationToken);
            }
        }
    }
}
