# RPC-Based Communication Architecture

## Overview

The Discernment extension now uses **StreamJsonRpc** over named pipes for robust inter-process communication between the out-of-process extension (Discernment) and the in-process VS package (Discernment.InProc).

## Architecture

### Components

1. **Discernment.Contracts** - Shared interface definitions
   - `INavigationService` - Contract for navigation operations

2. **Discernment.InProc** - RPC Server (In-Process)
   - `NavigationService` - Implements `INavigationService` using VS DTE APIs
   - `DiscernmentInProcPackage` - Hosts the RPC server on a named pipe
   - **SetSelection Implementation**: Uses `TextSelection.MoveToLineAndOffset()` and `SelectLine()` to navigate and highlight code

3. **Discernment** - RPC Client (Out-of-Process)
   - `NavigationRpcClient` - Client that connects to the RPC server
   - `VariableInsightWindow` - Uses the RPC client for navigation

### Communication Flow

```
┌─────────────────────────┐         Named Pipe          ┌──────────────────────────┐
│  Discernment (OOP)      │    "DiscernmentNavigation   │  Discernment.InProc      │
│                         │         Service"             │  (In-Process)            │
│  ┌──────────────────┐   │                              │  ┌────────────────────┐  │
│  │VariableInsight   │   │   NavigateToSourceAsync()   │  │NavigationService   │  │
│  │Window            ├───┼──────────────────────────────┼─>│ - Uses DTE2        │  │
│  └────────┬─────────┘   │                              │  │ - Opens file       │  │
│           │             │   <─── Success/Error ─────── │  │ - Sets selection   │  │
│           v             │                              │  │ - Activates window │  │
│  ┌──────────────────┐   │                              │  └────────────────────┘  │
│  │NavigationRpc     │   │                              │                          │
│  │Client            │   │                              │  ┌────────────────────┐  │
│  │ - Connects       │   │                              │  │Package             │  │
│  │ - Retries        │   │                              │  │ - Hosts RPC Server │  │
│  │ - Proxies calls  │   │                              │  │ - Handles          │  │
│  └──────────────────┘   │                              │  │   reconnections    │  │
└─────────────────────────┘                              │  └────────────────────┘  │
                                                          └──────────────────────────┘
```

## Key Features

### 1. Robust Connection Management
- **Auto-reconnection**: Client automatically retries on connection failure (up to 3 attempts)
- **Connection pooling**: Server accepts multiple sequential connections
- **Timeout handling**: 5-second connection timeout with graceful fallback

### 2. SetSelection Implementation
The `NavigationService` properly implements source navigation with selection:

```csharp
// Open the document
var window = _dte.ItemOperations.OpenFile(filePath, Constants.vsViewKindCode);

if (window?.Document?.Selection is TextSelection selection)
{
    // Move to the specified line and column
    selection.MoveToLineAndOffset(lineNumber, columnNumber, false);
    
    // Select the entire line for visibility
    selection.SelectLine();
    
    // Ensure the line is visible
    selection.MoveToLineAndOffset(lineNumber, columnNumber, false);
    
    // Activate the window
    window.Activate();
}
```

### 3. Error Handling
- **Graceful degradation**: Falls back to notification prompts if RPC fails
- **Detailed logging**: Debug output for troubleshooting
- **Non-blocking**: Navigation failures don't crash the extension

## Advantages Over Previous Implementation

### Before (File-Based) ❌
- Wrote navigation requests to temp files
- No actual navigation implementation
- Required file system polling
- Unreliable and slow
- Left temp files around
- No error handling

### Now (RPC-Based) ✅
- Direct method calls via StreamJsonRpc
- Real-time communication
- Proper navigation with text selection
- Industry-standard approach (used throughout VS SDK)
- Clean connection management
- Robust error handling with retries

## Testing

To test the navigation:
1. Build both `Discernment` and `Discernment.InProc` projects
2. Deploy the VSIX packages
3. Open a C# file and analyze a variable
4. Click on any graph node to navigate to its source location
5. The editor should open the file and highlight the line

## Troubleshooting

### RPC Connection Issues
- Check Output window → Debug for connection messages
- Ensure both extensions are loaded (check Extensions → Manage Extensions)
- Verify the named pipe name matches: `DiscernmentNavigationService`

### Navigation Failures
- Falls back to notification prompt
- User can manually navigate using Ctrl+G
- Check Debug output for error details

## Future Enhancements

Possible improvements:
- Add more navigation methods (e.g., NavigateToSymbol, FindAllReferences)
- Implement bidirectional communication for status updates
- Add telemetry for navigation success rates
- Support for multiple simultaneous clients
