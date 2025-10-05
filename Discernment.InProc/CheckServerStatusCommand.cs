using System;
using System.ComponentModel.Design;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace DiscernmentInProc
{
    /// <summary>
    /// Command to check if the RPC server is running and show its status.
    /// </summary>
    internal sealed class CheckServerStatusCommand
    {
        /// <summary>
        /// Command ID.
        /// </summary>
        public const int CommandId = 0x0100;

        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("d8f6c3a2-5b7e-4d1f-9c3a-8e2f1a5b9d4c");

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private readonly AsyncPackage package;

        /// <summary>
        /// Reference to the package to access server status.
        /// </summary>
        private readonly Package inProcPackage;

        /// <summary>
        /// Initializes a new instance of the <see cref="CheckServerStatusCommand"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        /// <param name="inProcPackage">The InProc package to check status.</param>
        /// <param name="commandService">Command service to add command to, not null.</param>
        private CheckServerStatusCommand(AsyncPackage package, Package inProcPackage, OleMenuCommandService commandService)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            this.inProcPackage = inProcPackage ?? throw new ArgumentNullException(nameof(inProcPackage));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(this.Execute, menuCommandID);
            commandService.AddCommand(menuItem);
        }

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static CheckServerStatusCommand Instance { get; private set; }

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        /// <param name="inProcPackage">The InProc package to check status.</param>
        public static async Task InitializeAsync(AsyncPackage package, Package inProcPackage)
        {
            // Switch to the main thread - the call to AddCommand in CheckServerStatusCommand's constructor requires
            // the UI thread.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new CheckServerStatusCommand(package, inProcPackage, commandService);
        }

        /// <summary>
        /// This function is the callback used to execute the command when the menu item is clicked.
        /// </summary>
        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var status = inProcPackage.GetServerStatus();
            
            string statusMessage;
            MessageBoxIcon icon;

            if (status.IsInitialized)
            {
                if (status.IsServerRunning)
                {
                    if (status.IsClientConnected)
                    {
                        statusMessage = $"✅ RPC Server Status: RUNNING & CONNECTED\n\n" +
                                      $"🟢 Server is active and has a connected client\n" +
                                      $"📡 Pipe Name: {Package.PipeName}\n" +
                                      $"🔌 Client connected: YES\n" +
                                      $"✨ Navigation service: READY\n\n" +
                                      $"Everything is working properly!";
                        icon = MessageBoxIcon.Information;
                    }
                    else
                    {
                        statusMessage = $"⚠️ RPC Server Status: RUNNING (No Client)\n\n" +
                                      $"🟡 Server is active but waiting for connections\n" +
                                      $"📡 Pipe Name: {Package.PipeName}\n" +
                                      $"🔌 Client connected: NO\n" +
                                      $"💡 The out-of-process extension needs to connect\n\n" +
                                      $"Server is ready and waiting...";
                        icon = MessageBoxIcon.Warning;
                    }
                }
                else
                {
                    statusMessage = $"❌ RPC Server Status: NOT RUNNING\n\n" +
                                  $"🔴 Server failed to start or crashed\n" +
                                  $"📡 Expected Pipe Name: {Package.PipeName}\n" +
                                  $"⚠️ Navigation will not work\n\n" +
                                  $"Check the Output window for errors.";
                    icon = MessageBoxIcon.Error;
                }
            }
            else
            {
                statusMessage = $"⏳ RPC Server Status: INITIALIZING\n\n" +
                              $"🔵 Package is still loading\n" +
                              $"⏱️ Please wait a moment and try again\n\n" +
                              $"If this persists, check the Output window for initialization errors.";
                icon = MessageBoxIcon.Information;
            }

            MessageBox.Show(
                statusMessage,
                "Discernment InProc - Server Status",
                MessageBoxButtons.OK,
                icon);
        }
    }
}
