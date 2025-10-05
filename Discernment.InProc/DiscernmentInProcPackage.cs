using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE80;
using StreamJsonRpc;
using Task = System.Threading.Tasks.Task;

namespace DiscernmentInProc
{
    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The minimum requirement for a class to be considered a valid package for Visual Studio
    /// is to implement the IVsPackage interface and register itself with the shell.
    /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
    /// to do it: it derives from the Package class that provides the implementation of the
    /// IVsPackage interface and uses the registration attributes defined in the framework to
    /// register itself and its components with the shell. These attributes tell the pkgdef creation
    /// utility what data to put into .pkgdef file.
    /// </para>
    /// <para>
    /// To get loaded into VS, the package must be referred by &lt;Asset Type="Microsoft.VisualStudio.VsPackage" ...&gt; in .vsixmanifest file.
    /// </para>
    /// </remarks>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [Guid(DiscernmentInProc.Package.PackageGuidString)]
    public sealed class Package : AsyncPackage
    {
        /// <summary>
        /// Discernment.InProcPackage GUID string.
        /// </summary>
        public const string PackageGuidString = "a3c031cd-0e46-4af9-ae49-f7ba66f4efe1";

        /// <summary>
        /// Named pipe name for RPC communication.
        /// </summary>
        public const string PipeName = "DiscernmentNavigationService";

        private NamedPipeServerStream _pipeServer;
        private JsonRpc _rpc;
        private NavigationService _navigationService;
        private bool _isInitialized = false;
        private bool _isServerRunning = false;
        private bool _isClientConnected = false;

        /// <summary>
        /// Server status information.
        /// </summary>
        public class ServerStatus
        {
            public bool IsInitialized { get; set; }
            public bool IsServerRunning { get; set; }
            public bool IsClientConnected { get; set; }
        }

        /// <summary>
        /// Gets the current server status.
        /// </summary>
        public ServerStatus GetServerStatus()
        {
            return new ServerStatus
            {
                IsInitialized = _isInitialized,
                IsServerRunning = _isServerRunning,
                IsClientConnected = _isClientConnected
            };
        }

        #region Package Members

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            try
            {
                await base.InitializeAsync(cancellationToken, progress);
                await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

                // Get DTE for navigation
                var dte = await GetServiceAsync(typeof(EnvDTE.DTE)) as DTE2;
                if (dte == null)
                {
                    System.Diagnostics.Debug.WriteLine("Discernment.InProc: Failed to get DTE service");
                    return;
                }

                // Create navigation service
                _navigationService = new NavigationService(dte);

                // Start RPC server in background
                _ = Task.Run(async () => await StartRpcServerAsync(cancellationToken), cancellationToken);

                // Initialize the command
                await CheckServerStatusCommand.InitializeAsync(this, this);

                _isInitialized = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Discernment.InProc: Exception during initialization: {ex.Message}");
                throw;
            }
        }

        private async Task StartRpcServerAsync(CancellationToken cancellationToken)
        {
            System.Diagnostics.Debug.WriteLine($"Discernment.InProc: RPC Server starting on pipe: {PipeName}");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Create a new named pipe server for each connection
                    _pipeServer = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    _isServerRunning = true;
                    _isClientConnected = false;

                    System.Diagnostics.Debug.WriteLine($"Discernment.InProc: RPC Server waiting for connection on pipe: {PipeName}");

                    // Wait for a client to connect
                    await _pipeServer.WaitForConnectionAsync(cancellationToken);

                    _isClientConnected = true;

                    System.Diagnostics.Debug.WriteLine("Discernment.InProc: RPC Server - Client connected");

                    // Create JSON-RPC connection
                    _rpc = JsonRpc.Attach(_pipeServer, _navigationService);

                    System.Diagnostics.Debug.WriteLine("Discernment.InProc: JSON-RPC attached, ready to receive calls");

                    // Wait for the connection to close
                    await _rpc.Completion;

                    _isClientConnected = false;

                    System.Diagnostics.Debug.WriteLine("Discernment.InProc: RPC Server - Client disconnected");
                }
                catch (OperationCanceledException)
                {
                    _isServerRunning = false;
                    System.Diagnostics.Debug.WriteLine("Discernment.InProc: RPC Server cancelled (shutdown requested)");
                    // Expected when cancellation is requested
                    break;
                }
                catch (Exception ex)
                {
                    _isServerRunning = false;
                    _isClientConnected = false;

                    System.Diagnostics.Debug.WriteLine($"Discernment.InProc: RPC Server error: {ex.Message}");
                    
                    // Wait a bit before retrying
                    await Task.Delay(1000, cancellationToken);
                }
                finally
                {
                    _rpc?.Dispose();
                    _rpc = null;
                    
                    _pipeServer?.Dispose();
                    _pipeServer = null;
                }
            }

            _isServerRunning = false;
            _isClientConnected = false;

            System.Diagnostics.Debug.WriteLine("Discernment.InProc: RPC Server exiting");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                System.Diagnostics.Debug.WriteLine("Discernment.InProc: Disposing package and RPC resources");
                _rpc?.Dispose();
                _pipeServer?.Dispose();
            }

            base.Dispose(disposing);
        }

        #endregion
    }
}
