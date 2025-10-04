using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;

namespace Discernment
{
    /// <summary>
    /// Analyzes C# code to build variable insight graphs using Roslyn.
    /// Traces backward through data flow to find all variables that contribute to a target variable.
    /// </summary>
    internal class VariableInsightAnalyzer
    {
        private readonly HashSet<ISymbol> _visitedSymbols = new(SymbolEqualityComparer.Default);
        private readonly Dictionary<ISymbol, InsightNode> _symbolToNodeMap = new(SymbolEqualityComparer.Default);
        private Solution? _solution;

        /// <summary>
        /// Analyzes a variable or field at the given position and builds an insight graph.
        /// </summary>
        public async Task<VariableInsightGraph?> AnalyzeAsync(
            Document document,
            int position,
            CancellationToken cancellationToken)
        {
            _solution = document.Project.Solution;
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            var root = await document.GetSyntaxRootAsync(cancellationToken);
            
            if (semanticModel == null || root == null)
                return null;

            // Find the symbol at the cursor position
            var token = root.FindToken(position);
            var node = token.Parent;
            
            if (node == null)
                return null;

            var symbol = semanticModel.GetSymbolInfo(node, cancellationToken).Symbol 
                ?? semanticModel.GetDeclaredSymbol(node, cancellationToken);

            if (symbol == null)
                return null;

            // Only analyze variables, parameters, fields, and properties
            if (!IsAnalyzableSymbol(symbol))
                return null;

            // Build the insight graph by tracing backward through assignments
            var rootNode = CreateNode(symbol);
            var graph = new VariableInsightGraph
            {
                RootNode = rootNode,
                AllNodes = new HashSet<InsightNode> { rootNode }
            };

            await TraceDataFlowBackwardAsync(_solution, symbol, rootNode, graph, 0, cancellationToken);

            graph.TotalReferences = graph.AllNodes.Count - 1; // Don't count the root
            return graph;
        }

        /// <summary>
        /// Traces backward through data flow to find all variables that contribute to the target symbol.
        /// </summary>
        private async Task TraceDataFlowBackwardAsync(
            Solution solution,
            ISymbol symbol,
            InsightNode currentNode,
            VariableInsightGraph graph,
            int depth,
            CancellationToken cancellationToken)
        {
            // Limit recursion depth to prevent infinite loops
            if (depth > 15 || !_visitedSymbols.Add(symbol))
                return;

            // Find all write operations (assignments) to this symbol
            var assignments = await FindAssignmentsToSymbolAsync(solution, symbol, cancellationToken);

            foreach (var (assignmentNode, semanticModel, location) in assignments)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                // Extract all contributing symbols from the right side of the assignment
                var contributingSymbols = await ExtractContributingSymbolsAsync(
                    assignmentNode, 
                    semanticModel, 
                    cancellationToken);

                foreach (var contributingSymbol in contributingSymbols)
                {
                    // Skip self-references
                    if (SymbolEqualityComparer.Default.Equals(contributingSymbol, symbol))
                        continue;

                    // Get or create node for this contributing symbol
                    if (!_symbolToNodeMap.TryGetValue(contributingSymbol, out var contributingNode))
                    {
                        contributingNode = CreateNode(contributingSymbol);
                        _symbolToNodeMap[contributingSymbol] = contributingNode;
                        graph.AllNodes.Add(contributingNode);
                    }

                    // Create edge from current node to contributing node
                    var edge = new InsightEdge
                    {
                        Target = contributingNode,
                        RelationKind = GetAssignmentKind(assignmentNode),
                        SourceLocation = GetLocationString(location)
                    };

                    currentNode.Edges.Add(edge);

                    // Recursively trace this contributing symbol
                    await TraceDataFlowBackwardAsync(
                        solution,
                        contributingSymbol,
                        contributingNode,
                        graph,
                        depth + 1,
                        cancellationToken);
                }
            }
        }

        /// <summary>
        /// Finds all assignments to a symbol (including initialization, assignments, out parameters).
        /// </summary>
        private async Task<List<(SyntaxNode Node, SemanticModel Model, Location Location)>> FindAssignmentsToSymbolAsync(
            Solution solution,
            ISymbol symbol,
            CancellationToken cancellationToken)
        {
            var assignments = new List<(SyntaxNode, SemanticModel, Location)>();

            // First, check the declaration itself if it has an initializer
            var declarationLocation = symbol.Locations.FirstOrDefault(loc => loc.IsInSource);
            if (declarationLocation != null)
            {
                var declarationDocument = solution.GetDocument(declarationLocation.SourceTree);
                if (declarationDocument != null)
                {
                    var semanticModel = await declarationDocument.GetSemanticModelAsync(cancellationToken);
                    var root = await declarationDocument.GetSyntaxRootAsync(cancellationToken);

                    if (semanticModel != null && root != null)
                    {
                        var declarationNode = root.FindNode(declarationLocation.SourceSpan);
                        
                        // Check if this is a declaration with an initializer
                        var variableDeclarator = declarationNode.AncestorsAndSelf()
                            .OfType<VariableDeclaratorSyntax>()
                            .FirstOrDefault();
                        
                        if (variableDeclarator?.Initializer != null)
                        {
                            var declaredSymbol = semanticModel.GetDeclaredSymbol(variableDeclarator, cancellationToken);
                            if (SymbolEqualityComparer.Default.Equals(declaredSymbol, symbol))
                            {
                                assignments.Add((variableDeclarator, semanticModel, declarationLocation));
                            }
                        }
                    }
                }
            }

            // Find all references to the symbol (for subsequent assignments)
            var references = await SymbolFinder.FindReferencesAsync(symbol, solution, cancellationToken);

            foreach (var reference in references)
            {
                foreach (var location in reference.Locations)
                {
                    var document = solution.GetDocument(location.Document.Id);
                    if (document == null)
                        continue;

                    var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                    var root = await document.GetSyntaxRootAsync(cancellationToken);

                    if (semanticModel == null || root == null)
                        continue;

                    var node = root.FindNode(location.Location.SourceSpan);

                    // Check if this is a write operation (assignment, but not the declaration we already handled)
                    if (IsAssignment(node, symbol, semanticModel, cancellationToken))
                    {
                        // Avoid adding the declaration twice
                        var isDeclaration = node.AncestorsAndSelf().OfType<VariableDeclaratorSyntax>().Any(vd =>
                        {
                            var declSym = semanticModel.GetDeclaredSymbol(vd, cancellationToken);
                            return declSym != null && SymbolEqualityComparer.Default.Equals(declSym, symbol);
                        });

                        if (!isDeclaration)
                        {
                            assignments.Add((node, semanticModel, location.Location));
                        }
                    }
                }
            }

            return assignments;
        }

        /// <summary>
        /// Checks if a node represents an assignment/write to the symbol.
        /// </summary>
        private bool IsAssignment(SyntaxNode node, ISymbol symbol, SemanticModel semanticModel, CancellationToken cancellationToken)
        {
            // Check for variable declaration with initializer: int x = value;
            var variableDeclarator = node.AncestorsAndSelf().OfType<VariableDeclaratorSyntax>().FirstOrDefault();
            if (variableDeclarator?.Initializer != null)
            {
                var declaredSymbol = semanticModel.GetDeclaredSymbol(variableDeclarator, cancellationToken);
                if (SymbolEqualityComparer.Default.Equals(declaredSymbol, symbol))
                    return true;
            }

            // Check for assignment expression: x = value;
            var assignment = node.AncestorsAndSelf().OfType<AssignmentExpressionSyntax>().FirstOrDefault();
            if (assignment != null)
            {
                var leftSymbol = semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol;
                if (SymbolEqualityComparer.Default.Equals(leftSymbol, symbol))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Extracts all symbols that contribute to an assignment (variables, parameters from method calls, etc.)
        /// </summary>
        private async Task<List<ISymbol>> ExtractContributingSymbolsAsync(
            SyntaxNode assignmentNode,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var contributors = new List<ISymbol>();

            // Get the expression that's being assigned (right side of assignment or initializer)
            ExpressionSyntax? valueExpression = null;

            var variableDeclarator = assignmentNode.AncestorsAndSelf().OfType<VariableDeclaratorSyntax>().FirstOrDefault();
            if (variableDeclarator?.Initializer != null)
            {
                valueExpression = variableDeclarator.Initializer.Value;
            }

            var assignment = assignmentNode.AncestorsAndSelf().OfType<AssignmentExpressionSyntax>().FirstOrDefault();
            if (assignment != null)
            {
                valueExpression = assignment.Right;
            }

            if (valueExpression == null)
                return contributors;

            // Extract all identifiers (variables, parameters, fields, properties)
            var identifiers = valueExpression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>();
            foreach (var identifier in identifiers)
            {
                var symbol = semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol;
                if (symbol != null && IsAnalyzableSymbol(symbol))
                {
                    contributors.Add(symbol);
                }
            }

            // Handle method calls - trace into the method to find what contributes to the return value
            var invocations = valueExpression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>();
            foreach (var invocation in invocations)
            {
                var methodSymbols = await TraceMethodReturnContributorsAsync(
                    invocation,
                    semanticModel,
                    cancellationToken);
                
                contributors.AddRange(methodSymbols);
            }

            return contributors.Distinct(SymbolEqualityComparer.Default).ToList();
        }

        /// <summary>
        /// Traces into a method call to find all variables that contribute to its return value.
        /// </summary>
        private async Task<List<ISymbol>> TraceMethodReturnContributorsAsync(
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var contributors = new List<ISymbol>();

            // Get the method being called
            var methodSymbol = semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
            if (methodSymbol == null)
                return contributors;

            // Get the method's syntax reference
            var methodSyntax = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault();
            if (methodSyntax == null)
                return contributors;

            var methodDeclaration = await methodSyntax.GetSyntaxAsync(cancellationToken) as MethodDeclarationSyntax;
            if (methodDeclaration == null)
                return contributors;

            // Get semantic model for the method's document
            var methodDocument = _solution?.GetDocument(methodSyntax.SyntaxTree);
            if (methodDocument == null)
                return contributors;

            var methodSemanticModel = await methodDocument.GetSemanticModelAsync(cancellationToken);
            if (methodSemanticModel == null)
                return contributors;

            // Find all return statements in the method
            var returnStatements = methodDeclaration.DescendantNodes().OfType<ReturnStatementSyntax>();
            foreach (var returnStatement in returnStatements)
            {
                if (returnStatement.Expression == null)
                    continue;

                // Extract all identifiers from the return expression
                var returnIdentifiers = returnStatement.Expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>();
                foreach (var identifier in returnIdentifiers)
                {
                    var symbol = methodSemanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol;
                    if (symbol != null && IsAnalyzableSymbol(symbol))
                    {
                        // If it's a parameter, map it back to the argument at the call site
                        if (symbol is IParameterSymbol paramSymbol)
                        {
                            var argumentSymbol = MapParameterToArgument(
                                paramSymbol,
                                invocation,
                                methodSymbol,
                                semanticModel,
                                cancellationToken);
                            
                            if (argumentSymbol != null)
                            {
                                contributors.Add(argumentSymbol);
                            }
                        }
                        else
                        {
                            contributors.Add(symbol);
                        }
                    }
                }
            }

            return contributors;
        }

        /// <summary>
        /// Maps a method parameter back to the argument symbol at the call site.
        /// </summary>
        private ISymbol? MapParameterToArgument(
            IParameterSymbol parameter,
            InvocationExpressionSyntax invocation,
            IMethodSymbol method,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var parameterIndex = method.Parameters.IndexOf(parameter);
            if (parameterIndex < 0 || parameterIndex >= invocation.ArgumentList.Arguments.Count)
                return null;

            var argument = invocation.ArgumentList.Arguments[parameterIndex];
            
            // Get the symbol of the argument expression
            var argumentIdentifier = argument.Expression as IdentifierNameSyntax;
            if (argumentIdentifier != null)
            {
                return semanticModel.GetSymbolInfo(argumentIdentifier, cancellationToken).Symbol;
            }

            // Handle more complex expressions
            var identifiers = argument.Expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>();
            foreach (var identifier in identifiers)
            {
                var symbol = semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol;
                if (symbol != null && IsAnalyzableSymbol(symbol))
                {
                    return symbol; // Return the first analyzable symbol found
                }
            }

            return null;
        }

        private string GetAssignmentKind(SyntaxNode node)
        {
            if (node.AncestorsAndSelf().OfType<VariableDeclaratorSyntax>().Any())
                return "Initialization";
            if (node.AncestorsAndSelf().OfType<AssignmentExpressionSyntax>().Any())
                return "Assignment";
            
            return "Assignment";
        }

        private InsightNode CreateNode(ISymbol symbol)
        {
            var kind = symbol switch
            {
                ILocalSymbol => InsightNodeKind.Variable,
                IParameterSymbol => InsightNodeKind.Parameter,
                IFieldSymbol => InsightNodeKind.Field,
                IPropertySymbol => InsightNodeKind.Property,
                IMethodSymbol => InsightNodeKind.Method,
                _ => InsightNodeKind.Expression
            };

            var location = symbol.Locations.FirstOrDefault();
            var locationString = location != null ? GetLocationString(location) : "Unknown";
            var sourceCodeLine = GetSourceCodeLine(location);

            return new InsightNode
            {
                Name = symbol.Name,
                Type = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                Location = locationString,
                SourceCodeLine = sourceCodeLine,
                Kind = kind
            };
        }

        private string GetSourceCodeLine(Location? location)
        {
            if (location == null || !location.IsInSource)
                return string.Empty;

            try
            {
                var sourceTree = location.SourceTree;
                if (sourceTree == null)
                    return string.Empty;

                var lineSpan = location.GetLineSpan();
                var line = sourceTree.GetText().Lines[lineSpan.StartLinePosition.Line];
                return line.ToString().Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        private string GetLocationString(Location location)
        {
            if (location.IsInSource)
            {
                var lineSpan = location.GetLineSpan();
                return $"{System.IO.Path.GetFileName(lineSpan.Path)}:{lineSpan.StartLinePosition.Line + 1}";
            }
            return "Metadata";
        }

        private bool IsAnalyzableSymbol(ISymbol symbol)
        {
            return symbol is ILocalSymbol 
                || symbol is IParameterSymbol 
                || symbol is IFieldSymbol 
                || symbol is IPropertySymbol;
        }
    }
}

