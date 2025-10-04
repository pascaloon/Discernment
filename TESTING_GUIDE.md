# Testing Guide for Variable Insight Extension

## Installation
1. Build the extension: `dotnet build Discernment\Discernment.csproj`
2. Install the VSIX: Double-click `Discernment\bin\Debug\net8.0-windows\Discernment.vsix`
3. Restart Visual Studio 2022 if it's already running

## How to Use

1. **Open** the test file: `Sample\Program.cs`
2. **Select** a variable name (or place cursor on it)
3. **Run** the command using one of these methods:
   - **Quick Launch**: Press `Ctrl+Q`, type "Variable Insight", Enter (fastest!)
   - **Extensions Menu**: `Extensions` → `Variable Insight`
   - **Tools Menu**: `Tools` → `Variable Insight`
4. **View** the interactive 2D dependency graph in the dockable tool window
5. **Pan** the graph using the scrollbars - you'll see blue edges connecting nodes
6. **Click** on any node to:
   - Select it (highlighted with a thick blue border)
   - Open the source file in the editor (you may need to use Ctrl+G to jump to the line)
7. **Inspect** each node to see the variable name, type, and actual source code line
8. **Follow** the blue edges to understand data flow from contributing variables
9. **Dock** the window where you prefer (try bottom or right panel)
10. **Keep it open** while coding - run the command again to update it with new variables

**Tips:**
- Nodes are spaced further apart now (400px horizontal, 200px vertical) to make edges clearly visible
- Edges are thick blue lines (3px) connecting the bottom of a node to the top of its dependency
- The graph can be quite large - use scrollbars to explore all relationships

## Example Test Scenarios

### Scenario 1: Simple Chain (Line 48)
```csharp
int base1 = 10;
int doubled = base1 * 2;
int sum = doubled + base2;
int result = sum + 5;  // ← Select 'result' here
```

**Expected Output in Tool Window:**

**Header:**
- Variable: `result`
- Type: `int`
- Location: `Program.cs:48`

**Summary:**
- Total Affecting Elements: 3

**Interactive Graph:**
You'll see a hierarchical 2D graph with nodes and edges:
- **Root Node (Level 0)**: `result` showing the line `int result = sum + 5;`
- **Level 1 Node**: `sum` connected to `result`, showing the line `int sum = doubled + base2;`
- **Level 2 Nodes**: 
  - `doubled` connected to `sum`, showing `int doubled = base1 * 2;`
  - `base2` connected to `sum`, showing its assignment line
- **Level 3 Node**: `base1` connected to `doubled`, showing `int base1 = 10;`

Each node displays:
- Variable name (bold)
- Type (colored)
- Actual source code line (in a code box)
- Location (file:line)

Edges show the dependency flow with arrows pointing from contributors to the variable they affect.

### Scenario 2: Field Mutation (Line 63)
```csharp
globalCounter = 0;  // ← Select 'globalCounter' here
IncrementCounter();
IncrementCounter();
globalCounter += 10;
ModifyCounterBy(5);
```

**Expected Output:**
Shows all the references where `globalCounter` is modified, including:
- Direct assignments (line 63, 68)
- Method calls that mutate it (`IncrementCounter`, `ModifyCounterBy`)
- The `delta` parameter in `ModifyCounterBy` that affects the counter

### Scenario 3: Parameter Flow (Line 100)
```csharp
int value = 100;
int processed = ProcessValue(value);
int final = ProcessValue(processed) + 10;  // ← Select 'final' here
```

**Expected Output:**
Shows how `final` depends on `processed`, which depends on `value` through the `ProcessValue` method.

### Scenario 4: Complex Dependencies (Line 152)
```csharp
int baseScore = 100;
int bonus = 50;
double multiplier = 1.5;
int totalScore = baseScore + bonus;
double adjustedScore = totalScore * multiplier;
double finalScore = adjustedScore + (bonus * 0.5);  // ← Select 'finalScore' here
```

**Expected Output:**
Shows the full dependency tree with multiple levels:
- `finalScore` depends on `adjustedScore` and `bonus`
- `adjustedScore` depends on `totalScore` and `multiplier`
- `totalScore` depends on `baseScore` and `bonus`

Notice: `bonus` appears at multiple levels in the tree!

## Understanding the Tool Window

### Window Sections

1. **Header Section** (Top - Dark Background)
   - Variable name you analyzed
   - Variable type
   - Location where it's defined

2. **Summary Section** (Middle)
   - Total count of affecting elements found

3. **Dependency Tree** (Bottom - Scrollable)
   - Complete tree of all dependencies
   - Visual indentation shows hierarchy levels
   - Each node shows name, type, and location

### Tree Structure
- `•` marks first-level dependencies (direct influences)
- `└──` marks nested dependencies (indirect influences)
- Indentation increases with each level (4 spaces per level)
- Each dependency shows:
  - Variable name and type on first line
  - `↳` Relation type and location on second line

### Relation Types
- **Assignment**: Direct assignment (`x = y`)
- **Initialization**: Variable declaration with initializer (`int x = 5`)
- **Method Call**: Method invoked on an object that may modify it
- **Reference**: General reference (less common in output)

### Tool Window Benefits
- **Dockable**: Place it anywhere in your IDE layout
- **Persistent**: Stays open while you work
- **Reusable**: Run command again to analyze a different variable
- **Visual**: Tree structure makes dependencies easy to understand

## Troubleshooting

### "No active editor window"
- Make sure a C# file is open and focused in Visual Studio

### "No text selected"
- Place your cursor directly on a variable name, or select it

### "Could not analyze the selected symbol"
- Make sure you've selected a variable, field, parameter, or property
- Methods, classes, and namespaces are not supported

### "This extension only works with C# files"
- Open a `.cs` file, not `.txt`, `.json`, or other file types

## Advanced Testing

### Test with Your Own Code
1. Open any of your C# projects in Visual Studio
2. Find a variable you want to understand
3. Select it and run Variable Insight
4. Explore its dependency graph!

### Test Edge Cases
- **Circular dependencies**: The analyzer stops at 5 levels
- **Large projects**: May take a moment to analyze
- **Multiple files**: Works across file boundaries within the same document context

## What Makes a Good Test Case?

Good variables to analyze:
✅ Local variables with clear dependencies
✅ Fields that are modified in multiple places
✅ Parameters passed through method chains
✅ Variables in complex expressions

Not as useful:
❌ Constants (no dependencies)
❌ Variables only used, never assigned
❌ Method names or class names

## Feedback

If you find any issues or have suggestions:
- The extension shows error messages in dialogs
- Check Visual Studio's Output window for detailed logs
- Test with simpler cases first to isolate issues

