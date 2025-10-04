using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.Extensibility;

namespace Discernment
{
    /// <summary>
    /// Extension entrypoint for the VisualStudio.Extensibility extension.
    /// </summary>
    [VisualStudioContribution]
    internal class ExtensionEntrypoint : Extension
    {
        /// <inheritdoc/>
        public override ExtensionConfiguration ExtensionConfiguration => new()
        {
            Metadata = new(
                    id: "Discernment.e9536dbe-fe41-4ddb-802b-e4b0f7488036",
                    version: this.ExtensionAssemblyVersion,
                    publisherName: "Discernment",
                    displayName: "Discernment - Variable Insight",
                    description: "Analyze C# variables and view their complete dependency graph showing all elements that affect them directly or indirectly."),
        };

        /// <inheritdoc />
        protected override void InitializeServices(IServiceCollection serviceCollection)
        {
            base.InitializeServices(serviceCollection);

            // You can configure dependency injection here by adding services to the serviceCollection.
        }
    }
}
