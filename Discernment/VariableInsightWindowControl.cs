using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Extensibility.UI;

namespace Discernment
{
    /// <summary>
    /// Remote user control for the Variable Insight tool window.
    /// </summary>
    internal class VariableInsightWindowControl : RemoteUserControl
    {
        public VariableInsightWindowControl(object? dataContext)
            : base(dataContext)
        {
        }
    }
}

