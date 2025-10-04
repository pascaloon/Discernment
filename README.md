# Summary
This is a Visual Studio 2022 extension to help navigate C# code. This is using `VisualStudio.Extensibility` out-of-proc extension model.

# Features
## Variable Insight Tool Window
Select any variable or class field in the text editor and use the extension command to open the Variable Insight tool window with a complete dependency graph.

The **Variable Insight** is an interactive dockable tool window that displays a comprehensive 2D graph of all references affecting a selected variable. For each reference, we recursively analyze what elements affect it, building a complete dependency graph showing everything that influences the variable directly and indirectly. Only write operations (assignments, initializations) and method calls are included to show truly impactful relationships.

### Key Features:
- **Interactive 2D Graph**: Visual node-and-edge graph with pan capabilities and visible edges
- **Clickable Nodes**: Click any node to select it (highlighted with blue border) and open the source file
- **Dockable Tool Window**: Full Visual Studio tool window that can be docked anywhere
- **Complete Dependency Graph**: Shows all direct and indirect influences on a variable
- **Source Code Display**: Each node shows the actual line of code where the variable is used
- **Visual Hierarchy**: Hierarchical layout with connecting blue edges showing dependency flow
- **Real-time Analysis**: Uses Roslyn semantic analysis for accurate backward data flow analysis
- **Filtered References**: Only shows operations that actually affect the variable

**Note**: Due to out-of-process extension limitations, clicking a node opens the source file but doesn't automatically jump to the specific line. You'll need to use Ctrl+G to go to the line number shown on the node.

# Usage
1. Build the extension: `dotnet build Discernment\Discernment.csproj`
2. The VSIX file will be created at: `Discernment\bin\Debug\net8.0-windows\Discernment.vsix`
3. Install the VSIX by double-clicking it (Visual Studio 2022 version 17.9+ required)
4. Open a C# file in Visual Studio 2022
5. Select a variable, field, parameter, or property name (or place cursor on it)
6. Run the "Variable Insight" command using one of these methods:
   - **Quick Launch**: Press `Ctrl+Q`, type "Variable Insight", press Enter (fastest!)
   - **Extensions Menu**: `Extensions` → `Variable Insight`
   - **Tools Menu**: `Tools` → `Variable Insight`
7. The Variable Insight tool window will open showing the interactive dependency graph
8. **Navigate**: Use the scrollbars to pan around the graph - edges are displayed as blue lines
9. **Select Nodes**: Click any node to highlight it with a blue border and open the source file
10. **View Details**: Each node shows the variable name, type, and actual source code line
11. **Follow Dependencies**: Blue edges show how data flows from one variable to another
12. Dock the window wherever you prefer (recommended: bottom or side panel)

# Testing

## Quick Start
1. Open `Sample\Program.cs` in Visual Studio 2022
2. Select any variable name (or place cursor on it)
3. Press `Ctrl+Q` and type "Variable Insight" (or use `Extensions` menu)
4. View the dependency graph in the tool window!

## Test Cases

The `Sample\Program.cs` file includes 6 comprehensive test cases:

### Test Case 1: Simple Variable Chains
**Variables to try**: `result`, `sum`, `doubled`, `base1`
- Select `result` to see the full dependency chain: `result` ← `sum` ← `doubled` ← `base1`
- Demonstrates basic variable assignment chains

### Test Case 2: Field Mutation Tracking  
**Variables to try**: `globalCounter` (at different locations)
- Shows all places where a field is modified
- Tracks assignments, increments, and method calls that mutate the field
- Try selecting `globalCounter` at line 63 to see all its mutations

### Test Case 3: Parameter Flow
**Variables to try**: `processed`, `final`, `input`
- Shows how values flow through method parameters
- Select `final` to see how it depends on `value` through multiple method calls

### Test Case 4: Property Dependencies
**Variables to try**: `fullName`, `greeting`, `userName`
- Demonstrates property and field dependencies
- Select `greeting` to see it depends on both `fullName` and the `userName` field

### Test Case 5: Complex Multi-Level Dependencies
**Variables to try**: `finalScore`, `adjustedScore`, `totalScore`
- Shows complex dependency trees with multiple levels
- Select `finalScore` to see: `finalScore` ← `adjustedScore` ← `totalScore` ← `baseScore`, `bonus`
- Notice how `bonus` appears at multiple levels

### Test Case 6: Collection Operations
**Variables to try**: `sum`, `doubled`, `filtered`, `numbers`
- Demonstrates LINQ chain dependencies
- Select `sum` to see the full LINQ operation chain

## What to Expect

When you run Variable Insight on a variable, you'll see:
- **Variable name and type**
- **Location** where it's defined
- **Affecting elements**: All variables, fields, and expressions that influence it
- **Relation types**: Assignment, Initialization, Method Call, or Reference
- **Recursive dependencies**: The complete tree showing direct and indirect influences

## Tips
- Works best with **local variables**, **fields**, **parameters**, and **properties**
- Select the variable name, not the entire statement
- The analysis is recursive up to 5 levels deep to prevent infinite loops
- Only shows "write" operations (assignments) and method calls that affect the variable