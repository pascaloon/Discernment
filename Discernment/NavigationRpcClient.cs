using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Discernment.Contracts;
using StreamJsonRpc;

namespace Discernment
{
    /// <summary>
    /// RPC client for communicating with the in-process navigation service.
    /// </summary>
    internal class NavigationRpcClient : IDisposable
    {
        private const string PipeName = "DiscernmentNavigationService";
        private const int ConnectionTimeoutMs = 5000;
        private const int MaxRetries = 3;

        private NamedPipeClientStream? _pipeClient;
        private JsonRpc? _rpc;
        private INavigationService? _proxy;
        private readonly SemaphoreSlim _connectionLock = new SemaphoreSlim(1, 1);
        private bool _disposed;

        /// <summary>
        /// Navigates to a source location using the in-process navigation service.
        /// </summary>
        public async Task<bool> NavigateToSourceAsync(string filePath, int lineNumber, int columnNumber = 1, CancellationToken cancellationToken = default)
        {
            for (int retry = 0; retry < MaxRetries; retry++)
            {
                try
                {
                    await EnsureConnectedAsync(cancellationToken);

                    if (_proxy != null)
                    {
                        await _proxy.NavigateToSourceAsync(filePath, lineNumber, columnNumber);
                        System.Diagnostics.Debug.WriteLine($"Successfully navigated to {filePath}:{lineNumber}:{columnNumber}");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Navigation attempt {retry + 1} failed: {ex.Message}");
                    
                    // Dispose the connection on error to force reconnect
                    DisposeConnection();

                    if (retry == MaxRetries - 1)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to navigate after {MaxRetries} attempts");
                        return false;
                    }

                    // Wait a bit before retrying
                    await Task.Delay(500 * (retry + 1), cancellationToken);
                }
            }

            return false;
        }

        private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
        {
            await _connectionLock.WaitAsync(cancellationToken);
            try
            {
                // Check if we're already connected
                if (_rpc != null && !_rpc.IsDisposed)
                {
                    return;
                }

                // Create new connection
                _pipeClient = new NamedPipeClientStream(
                    ".",
                    PipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous);

                System.Diagnostics.Debug.WriteLine($"Connecting to RPC server via pipe: {PipeName}");

                // Connect with timeout
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(ConnectionTimeoutMs);
                    await _pipeClient.ConnectAsync(cts.Token);
                }

                System.Diagnostics.Debug.WriteLine("Connected to RPC server");

                // Create JSON-RPC client
                _rpc = new JsonRpc(_pipeClient);
                _rpc.StartListening();

                // Create proxy for INavigationService
                _proxy = _rpc.Attach<INavigationService>();

                System.Diagnostics.Debug.WriteLine("RPC client initialized successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to connect to RPC server: {ex.Message}");
                DisposeConnection();
                throw;
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        private void DisposeConnection()
        {
            try
            {
                _proxy = null;
                
                _rpc?.Dispose();
                _rpc = null;

                _pipeClient?.Dispose();
                _pipeClient = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error disposing connection: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            DisposeConnection();
            _connectionLock?.Dispose();
        }
    }
}
