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
        private readonly Dictionary<IMethodSymbol, InvocationExpressionSyntax> _methodInvocations = new(SymbolEqualityComparer.Default);
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

            // Special handling for method symbols - only trace return-affecting symbols
            if (symbol is IMethodSymbol methodSymbol)
            {
                await TraceMethodReturnDependenciesAsync(
                    solution,
                    methodSymbol,
                    currentNode,
                    graph,
                    depth,
                    cancellationToken);
                
                // If the method is virtual or abstract, also trace all its overrides
                if (methodSymbol.IsVirtual || methodSymbol.IsAbstract || methodSymbol.IsOverride)
                {
                    await TraceMethodOverridesAsync(
                        solution,
                        methodSymbol,
                        currentNode,
                        graph,
                        depth,
                        cancellationToken);
                }
                
                return;
            }

            // Special handling for parameter symbols - map to call site arguments
            if (symbol is IParameterSymbol parameter)
            {
                await TraceParameterToArgumentAsync(
                    solution,
                    parameter,
                    currentNode,
                    graph,
                    depth,
                    cancellationToken);
                return;
            }

            // Special case: For instance fields/properties, trace to object initializer assignments
            // This happens when we're analyzing a method and encounter an instance member
            if ((symbol is IFieldSymbol fieldSymbol && !fieldSymbol.IsStatic) ||
                (symbol is IPropertySymbol propSymbol && !propSymbol.IsStatic))
            {
                // Find the method invocation that uses this instance member
                var containingType = symbol.ContainingType;
                if (containingType != null)
                {
                    // Look for method invocations on this type
                    IMethodSymbol? containingMethod = null;
                    InvocationExpressionSyntax? invocation = null;
                    
                    foreach (var kvp in _methodInvocations)
                    {
                        if (SymbolEqualityComparer.Default.Equals(kvp.Key.ContainingType, containingType) &&
                            !kvp.Key.IsStatic)
                        {
                            containingMethod = kvp.Key;
                            invocation = kvp.Value;
                            break;
                        }
                    }
                    
                    if (invocation != null && invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                    {
                        // Get the semantic model for the invocation site
                        var invocationRoot = await invocation.SyntaxTree.GetRootAsync(cancellationToken);
                        var invocationDoc = solution.GetDocument(invocation.SyntaxTree);
                        var invocationSemanticModel = invocationDoc != null ? 
                            await invocationDoc.GetSemanticModelAsync(cancellationToken) : null;
                        
                        if (invocationSemanticModel != null)
                        {
                            // Get the object symbol (e.g., 'p' in 'p.GetGreetings()')
                            var objectSymbol = invocationSemanticModel.GetSymbolInfo(memberAccess.Expression, cancellationToken).Symbol;
                            
                            if (objectSymbol != null)
                            {
                                // Check if the object's actual type is compatible with the instance member's containing type
                                // For polymorphic calls, we only want to trace if the types match
                                var objectType = objectSymbol switch
                                {
                                    ILocalSymbol local => local.Type,
                                    IParameterSymbol param => param.Type,
                                    IFieldSymbol field => field.Type,
                                    IPropertySymbol prop => prop.Type,
                                    _ => null
                                };
                                
                                // Check if object type is the actual instantiated type (not base type)
                                // For example, if 's' is declared as 'Shape s = new Rectangle()', we want Rectangle, not Shape
                                var actualType = await GetActualInstantiatedTypeAsync(solution, objectSymbol, invocationSemanticModel, cancellationToken);
                                
                                // Only trace if the actual instantiated type matches the instance member's containing type
                                if (actualType != null && !SymbolEqualityComparer.Default.Equals(actualType, containingType))
                                {
                                    // Type mismatch - this instance member is from a different override path
                                    // Don't create an edge
                                    return;
                                }
                                
                                // Find the object initializer for this object and look for the property assignment
                                var objectInitValue = await FindObjectInitializerValueAsync(
                                    solution, objectSymbol, symbol, invocationSemanticModel, cancellationToken);
                                
                                if (objectInitValue != null)
                                {
                                    // Create node and edge for the initializer value
                                    if (!_symbolToNodeMap.TryGetValue(objectInitValue, out var valueNode))
                                    {
                                        valueNode = CreateNode(objectInitValue);
                                        _symbolToNodeMap[objectInitValue] = valueNode;
                                        graph.AllNodes.Add(valueNode);
                                    }

                                    var edge = new InsightEdge
                                    {
                                        Target = valueNode,
                                        RelationKind = "Object Initializer",
                                        SourceLocation = symbol.Locations.FirstOrDefault() != null
                                            ? GetLocationString(symbol.Locations.First())
                                            : "Unknown"
                                    };
                                    currentNode.Edges.Add(edge);

                                    // Continue tracing from the initializer value
                                    await TraceDataFlowBackwardAsync(
                                        solution,
                                        objectInitValue,
                                        valueNode,
                                        graph,
                                        depth + 1,
                                        cancellationToken);
                                }
                                else
                                {
                                    // No variable found (likely a literal value like '2' or '"text"')
                                    // Trace back to the object variable itself to show the assignment relationship
                                    if (!_symbolToNodeMap.TryGetValue(objectSymbol, out var objectNode))
                                    {
                                        objectNode = CreateNode(objectSymbol);
                                        _symbolToNodeMap[objectSymbol] = objectNode;
                                        graph.AllNodes.Add(objectNode);
                                    }

                                    var edge = new InsightEdge
                                    {
                                        Target = objectNode,
                                        RelationKind = "Object Initializer",
                                        SourceLocation = objectSymbol.Locations.FirstOrDefault() != null
                                            ? GetLocationString(objectSymbol.Locations.First())
                                            : "Unknown"
                                    };
                                    currentNode.Edges.Add(edge);

                                    // DON'T continue tracing from the object variable - it's a literal assignment
                                    // The edge shows the assignment, but we stop here since the value is a constant
                                }
                            }
                        }
                    }
                }
                
                // Don't look for regular assignments for instance members
                return;
            }
            
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

                    // Check if this edge already exists
                    var existingEdge = currentNode.Edges.FirstOrDefault(e => 
                        SymbolEqualityComparer.Default.Equals(
                            _symbolToNodeMap.FirstOrDefault(kvp => kvp.Value == e.Target).Key, 
                            contributingSymbol));
                    
                    if (existingEdge == null)
                    {
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
        }

        /// <summary>
        /// Traces all overrides of a virtual or abstract method.
        /// </summary>
        private async Task TraceMethodOverridesAsync(
            Solution solution,
            IMethodSymbol methodSymbol,
            InsightNode methodNode,
            VariableInsightGraph graph,
            int depth,
            CancellationToken cancellationToken)
        {
            // Get the base method (if this is an override)
            var baseMethod = methodSymbol.IsOverride ? methodSymbol.OverriddenMethod : methodSymbol;
            if (baseMethod == null)
                baseMethod = methodSymbol;

            // Find all types in the solution that derive from the containing type
            var containingType = baseMethod.ContainingType;
            if (containingType == null)
                return;

            // Search all projects in the solution
            foreach (var project in solution.Projects)
            {
                var compilation = await project.GetCompilationAsync(cancellationToken);
                if (compilation == null)
                    continue;

                // Find all types that derive from the containing type
                var allTypes = GetAllTypes(compilation.Assembly.GlobalNamespace);
                
                foreach (var type in allTypes)
                {
                    // Skip the base type itself
                    if (SymbolEqualityComparer.Default.Equals(type, containingType))
                        continue;

                    // Check if this type derives from the containing type
                    if (!IsDerivedFrom(type, containingType))
                        continue;

                    // Find the overriding method in this derived type
                    var overridingMethod = type.GetMembers()
                        .OfType<IMethodSymbol>()
                        .FirstOrDefault(m => 
                            m.IsOverride && 
                            SymbolEqualityComparer.Default.Equals(GetBaseMethod(m), baseMethod));

                    if (overridingMethod == null)
                        continue;

                    // Create node for the override
                    if (!_symbolToNodeMap.TryGetValue(overridingMethod, out var overrideNode))
                    {
                        overrideNode = CreateNode(overridingMethod);
                        _symbolToNodeMap[overridingMethod] = overrideNode;
                        graph.AllNodes.Add(overrideNode);
                    }

                    // Create "Override" edge
                    var existingEdge = methodNode.Edges.FirstOrDefault(e => e.Target == overrideNode);
                    if (existingEdge == null)
                    {
                        var edge = new InsightEdge
                        {
                            Target = overrideNode,
                            RelationKind = "Override",
                            SourceLocation = overridingMethod.Locations.FirstOrDefault() != null
                                ? GetLocationString(overridingMethod.Locations.First())
                                : "Unknown"
                        };
                        methodNode.Edges.Add(edge);
                    }

                    // Associate the override method with the same invocation as the base method
                    // This allows instance members used by the override to trace back to the correct object
                    if (_methodInvocations.TryGetValue(methodSymbol, out var baseInvocation))
                    {
                        if (!_methodInvocations.ContainsKey(overridingMethod))
                        {
                            _methodInvocations[overridingMethod] = baseInvocation;
                        }
                    }
                    else if (_methodInvocations.TryGetValue(baseMethod, out var realBaseInvocation))
                    {
                        if (!_methodInvocations.ContainsKey(overridingMethod))
                        {
                            _methodInvocations[overridingMethod] = realBaseInvocation;
                        }
                    }

                    // Recursively trace the override's dependencies
                    await TraceMethodReturnDependenciesAsync(
                        solution,
                        overridingMethod,
                        overrideNode,
                        graph,
                        depth + 1,
                        cancellationToken);
                }
            }
        }

        /// <summary>
        /// Gets the base method for an overriding method.
        /// </summary>
        private IMethodSymbol GetBaseMethod(IMethodSymbol method)
        {
            var current = method;
            while (current.OverriddenMethod != null)
            {
                current = current.OverriddenMethod;
            }
            return current;
        }

        /// <summary>
        /// Gets all types from a namespace recursively.
        /// </summary>
        private IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceSymbol namespaceSymbol)
        {
            foreach (var type in namespaceSymbol.GetTypeMembers())
            {
                yield return type;
                
                // Recursively get nested types
                foreach (var nestedType in GetNestedTypes(type))
                {
                    yield return nestedType;
                }
            }

            foreach (var nestedNamespace in namespaceSymbol.GetNamespaceMembers())
            {
                foreach (var type in GetAllTypes(nestedNamespace))
                {
                    yield return type;
                }
            }
        }

        /// <summary>
        /// Gets all nested types recursively.
        /// </summary>
        private IEnumerable<INamedTypeSymbol> GetNestedTypes(INamedTypeSymbol type)
        {
            foreach (var nestedType in type.GetTypeMembers())
            {
                yield return nestedType;
                foreach (var deeplyNested in GetNestedTypes(nestedType))
                {
                    yield return deeplyNested;
                }
            }
        }

        /// <summary>
        /// Checks if a type derives from a base type.
        /// </summary>
        private bool IsDerivedFrom(INamedTypeSymbol derivedType, INamedTypeSymbol baseType)
        {
            var current = derivedType.BaseType;
            while (current != null)
            {
                if (SymbolEqualityComparer.Default.Equals(current, baseType))
                    return true;
                current = current.BaseType;
            }
            return false;
        }

        /// <summary>
        /// Gets the actual instantiated type of a symbol at runtime.
        /// For example, for 'Shape s = new Rectangle()', returns Rectangle, not Shape.
        /// </summary>
        private async Task<INamedTypeSymbol?> GetActualInstantiatedTypeAsync(
            Solution solution,
            ISymbol symbol,
            SemanticModel invocationSemanticModel,
            CancellationToken cancellationToken)
        {
            // Find the declaration of the symbol
            var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
            if (location == null)
                return null;

            var syntaxTree = location.SourceTree;
            if (syntaxTree == null)
                return null;

            // Get the semantic model for the symbol's declaration location
            var doc = solution.GetDocument(syntaxTree);
            var semanticModel = doc != null ? await doc.GetSemanticModelAsync(cancellationToken) : null;
            if (semanticModel == null)
                return null;

            var root = await syntaxTree.GetRootAsync(cancellationToken);
            var node = root.FindNode(location.SourceSpan);

            // Look for the variable declarator
            var variableDeclarator = node.AncestorsAndSelf().OfType<VariableDeclaratorSyntax>().FirstOrDefault();
            if (variableDeclarator?.Initializer?.Value is ObjectCreationExpressionSyntax objectCreation)
            {
                // Get the type being instantiated
                var typeInfo = semanticModel.GetTypeInfo(objectCreation, cancellationToken);
                return typeInfo.Type as INamedTypeSymbol;
            }

            // Check if it's in an assignment expression
            var assignment = node.AncestorsAndSelf().OfType<AssignmentExpressionSyntax>().FirstOrDefault();
            if (assignment?.Right is ObjectCreationExpressionSyntax assignCreation)
            {
                var typeInfo = semanticModel.GetTypeInfo(assignCreation, cancellationToken);
                return typeInfo.Type as INamedTypeSymbol;
            }

            return null;
        }

        /// <summary>
        /// Finds the value assigned to a property/field in an object initializer.
        /// For example, in 'var p = new Person() {Name = "Paul"}', finds "Paul" for Name.
        /// </summary>
        private async Task<ISymbol?> FindObjectInitializerValueAsync(
            Solution solution,
            ISymbol objectSymbol,
            ISymbol memberSymbol,
            SemanticModel invocationSemanticModel,
            CancellationToken cancellationToken)
        {
            // Find the declaration/assignment of the object
            var objectLocations = objectSymbol.Locations;
            
            foreach (var location in objectLocations)
            {
                if (!location.IsInSource)
                    continue;
                    
                var syntaxTree = location.SourceTree;
                if (syntaxTree == null)
                    continue;
                    
                // Get the semantic model for where the object is declared, not where it's invoked
                var document = solution.GetDocument(syntaxTree);
                if (document == null)
                    continue;
                    
                var declarationSemanticModel = await document.GetSemanticModelAsync(cancellationToken);
                if (declarationSemanticModel == null)
                    continue;
                    
                var root = await syntaxTree.GetRootAsync(cancellationToken);
                var node = root.FindNode(location.SourceSpan);
                
                // Look for object creation with initializer
                // The node might be the identifier in a variable declarator, so we need to find the actual initializer
                ObjectCreationExpressionSyntax? objectCreation = null;
                
                // Check if it's in a variable declarator (e.g., var p = new Person() { ... })
                var variableDeclarator = node.AncestorsAndSelf().OfType<VariableDeclaratorSyntax>().FirstOrDefault();
                if (variableDeclarator?.Initializer?.Value is ObjectCreationExpressionSyntax varDeclCreation)
                {
                    objectCreation = varDeclCreation;
                }
                
                // Check if it's in an assignment (e.g., p = new Person() { ... })
                if (objectCreation == null)
                {
                    var assignment = node.AncestorsAndSelf().OfType<AssignmentExpressionSyntax>().FirstOrDefault();
                    if (assignment?.Right is ObjectCreationExpressionSyntax assignCreation)
                    {
                        objectCreation = assignCreation;
                    }
                }
                
                // Also check if the node itself or its ancestors are the object creation
                if (objectCreation == null)
                {
                    objectCreation = node.AncestorsAndSelf()
                        .OfType<ObjectCreationExpressionSyntax>()
                        .FirstOrDefault();
                }
                    
                if (objectCreation?.Initializer == null)
                    continue;
                    
                // Find the assignment to our member in the initializer
                foreach (var expression in objectCreation.Initializer.Expressions)
                {
                    if (expression is AssignmentExpressionSyntax assignment)
                    {
                        // Check if this is assigning to our member (use declaration semantic model)
                        var leftSymbol = declarationSemanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol;
                        
                        if (SymbolEqualityComparer.Default.Equals(leftSymbol, memberSymbol))
                        {
                            // Found the assignment! Get the symbol of the right side
                            if (assignment.Right is IdentifierNameSyntax identifier)
                            {
                                var valueSymbol = declarationSemanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol;
                                if (valueSymbol != null && IsAnalyzableSymbol(valueSymbol))
                                {
                                    return valueSymbol;
                                }
                            }
                        }
                    }
                }
            }
            
            return null;
        }

        /// <summary>
        /// Traces a parameter back to its argument at the call site.
        /// </summary>
        private async Task TraceParameterToArgumentAsync(
            Solution solution,
            IParameterSymbol parameter,
            InsightNode parameterNode,
            VariableInsightGraph graph,
            int depth,
            CancellationToken cancellationToken)
        {
            // Get the method that contains this parameter
            var containingMethod = parameter.ContainingSymbol as IMethodSymbol;
            if (containingMethod == null)
                return;

            // Try to find the invocation for this method
            if (!_methodInvocations.TryGetValue(containingMethod, out var invocation))
                return;

            // Get semantic model for the invocation site
            var invocationRoot = await invocation.SyntaxTree.GetRootAsync(cancellationToken);
            var invocationDoc = solution.GetDocument(invocation.SyntaxTree);
            var invocationSemanticModel = invocationDoc != null ? await invocationDoc.GetSemanticModelAsync(cancellationToken) : null;
            
            if (invocationSemanticModel == null)
                return;

            // Map parameter to argument
            var argument = MapParameterToArgument(parameter, invocation, containingMethod, invocationSemanticModel, cancellationToken);
            if (argument != null)
            {
                // Create node for the argument
                if (!_symbolToNodeMap.TryGetValue(argument, out var argumentNode))
                {
                    argumentNode = CreateNode(argument);
                    _symbolToNodeMap[argument] = argumentNode;
                    graph.AllNodes.Add(argumentNode);
                }

                // Check if edge already exists
                var existingEdge = parameterNode.Edges.FirstOrDefault(e =>
                    SymbolEqualityComparer.Default.Equals(
                        _symbolToNodeMap.FirstOrDefault(kvp => kvp.Value == e.Target).Key,
                        argument));

                if (existingEdge == null)
                {
                    // Create edge from parameter to argument
                    var edge = new InsightEdge
                    {
                        Target = argumentNode,
                        RelationKind = "Parameter Mapping",
                        SourceLocation = invocation.GetLocation() != null
                            ? GetLocationString(invocation.GetLocation())
                            : "Unknown"
                    };
                    parameterNode.Edges.Add(edge);

                    // Continue tracing from the argument
                    await TraceDataFlowBackwardAsync(
                        solution,
                        argument,
                        argumentNode,
                        graph,
                        depth + 1,
                        cancellationToken);
                }
            }
        }

        /// <summary>
        /// Traces dependencies for a method - only symbols that affect the return value.
        /// Maps parameters back to actual arguments at the call site.
        /// </summary>
        private async Task TraceMethodReturnDependenciesAsync(
            Solution solution,
            IMethodSymbol methodSymbol,
            InsightNode methodNode,
            VariableInsightGraph graph,
            int depth,
            CancellationToken cancellationToken)
        {
            // Get the method's syntax
            var methodSyntax = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault();
            if (methodSyntax == null)
                return;

            var methodDeclaration = await methodSyntax.GetSyntaxAsync(cancellationToken) as MethodDeclarationSyntax;
            if (methodDeclaration == null)
                return;

            var root = await methodDeclaration.SyntaxTree.GetRootAsync(cancellationToken);
            var doc = solution.GetDocument(root.SyntaxTree);
            var semanticModel = doc != null ? await doc.GetSemanticModelAsync(cancellationToken) : null;

            if (semanticModel == null)
                return;

            var returnContributors = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            var returnExpressions = new List<ExpressionSyntax>();

            // Find all return statements
            var returnStatements = methodDeclaration.DescendantNodes().OfType<ReturnStatementSyntax>();
            foreach (var returnStatement in returnStatements)
            {
                if (returnStatement.Expression != null)
                {
                    returnExpressions.Add(returnStatement.Expression);
                }
            }

            // Also check for expression-bodied methods (e.g., => Width * Height)
            if (methodDeclaration.ExpressionBody?.Expression != null)
            {
                returnExpressions.Add(methodDeclaration.ExpressionBody.Expression);
            }

            // Extract contributors from all return expressions
            foreach (var returnExpression in returnExpressions)
            {
                // Extract all symbols that contribute to this return value
                var identifiers = returnExpression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>();
                foreach (var identifier in identifiers)
                {
                    var symbol = semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol;
                    if (symbol != null && IsAnalyzableSymbol(symbol))
                    {
                        returnContributors.Add(symbol);
                    }
                }

                // Handle method calls in return expressions
                var invocations = returnExpression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>();
                foreach (var inv in invocations)
                {
                    var invokedMethod = semanticModel.GetSymbolInfo(inv, cancellationToken).Symbol as IMethodSymbol;
                    if (invokedMethod != null)
                    {
                        returnContributors.Add(invokedMethod);
                    }
                }
            }

            // Create edges only for symbols that contribute to the return value
            foreach (var contributor in returnContributors)
            {
                // Create node for the contributor (parameter, local var, field, etc.)
                if (!_symbolToNodeMap.TryGetValue(contributor, out var contributorNode))
                {
                    contributorNode = CreateNode(contributor);
                    _symbolToNodeMap[contributor] = contributorNode;
                    graph.AllNodes.Add(contributorNode);
                }

                // Check if edge already exists
                var existingEdge = methodNode.Edges.FirstOrDefault(e =>
                    SymbolEqualityComparer.Default.Equals(
                        _symbolToNodeMap.FirstOrDefault(kvp => kvp.Value == e.Target).Key,
                        contributor));

                if (existingEdge == null)
                {
                    var edge = new InsightEdge
                    {
                        Target = contributorNode,
                        RelationKind = "Return Contributor",
                        SourceLocation = methodSymbol.Locations.FirstOrDefault() != null 
                            ? GetLocationString(methodSymbol.Locations.First()) 
                            : "Unknown"
                    };

                    methodNode.Edges.Add(edge);

                    // Recursively trace this contributor
                    // If this is a parameter, TraceDataFlowBackwardAsync will automatically
                    // handle mapping it to the call site argument via TraceParameterToArgumentAsync
                    await TraceDataFlowBackwardAsync(
                        solution,
                        contributor,
                        contributorNode,
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
        /// Extracts all symbols and methods that contribute to an assignment.
        /// Returns both direct symbol references and method symbols (which will be treated as nodes).
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

            // First, collect all invocation nodes to exclude their arguments and objects
            var invocations = valueExpression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().ToList();
            var excludedNodes = new HashSet<SyntaxNode>();
            
            foreach (var invocation in invocations)
            {
                // Mark all argument expressions as "inside method call" - these should NOT be direct contributors
                foreach (var argument in invocation.ArgumentList.Arguments)
                {
                    excludedNodes.Add(argument.Expression);
                    // Also mark all descendants of the argument
                    foreach (var descendant in argument.Expression.DescendantNodesAndSelf())
                    {
                        excludedNodes.Add(descendant);
                    }
                }
                
                // Also exclude the object part of instance method calls (e.g., 'p' in 'p.GetGreetings()')
                // The object should be traced through 'this' parameter mapping, not as a direct contributor
                if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                {
                    excludedNodes.Add(memberAccess.Expression);
                    foreach (var descendant in memberAccess.Expression.DescendantNodesAndSelf())
                    {
                        excludedNodes.Add(descendant);
                    }
                }
            }

            // Extract identifiers (variables, parameters, fields, properties)
            // BUT skip those inside method call arguments or object expressions
            var identifiers = valueExpression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>();
            foreach (var identifier in identifiers)
            {
                // Skip if this identifier is excluded (inside arguments or object part of member access)
                if (excludedNodes.Contains(identifier))
                {
                    continue;
                }
                
                var symbol = semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol;
                if (symbol != null && IsAnalyzableSymbol(symbol))
                {
                    // Skip if it's the method name itself (we'll handle the method separately)
                    var parent = identifier.Parent;
                    if (parent is MemberAccessExpressionSyntax memberAccess && 
                        memberAccess.Name == identifier &&
                        memberAccess.Parent is InvocationExpressionSyntax)
                    {
                        continue;
                    }
                    
                    contributors.Add(symbol);
                }
            }

            // Handle method calls - add the METHOD itself as a contributor (not its arguments)
            foreach (var invocation in invocations)
            {
                var methodSymbol = semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
                if (methodSymbol != null)
                {
                    // Store the invocation so we can map parameters to arguments later
                    _methodInvocations[methodSymbol] = invocation;
                    
                    // Add the method itself as a node
                    contributors.Add(methodSymbol);
                }
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
                || symbol is IPropertySymbol
                || symbol is IMethodSymbol;
        }
    }
}


