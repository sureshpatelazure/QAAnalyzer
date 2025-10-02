using Microsoft.SemanticKernel;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace QAAnalyzer
{
    public static class McpServerBuilderExtensions
    {
        public static IMcpServerBuilder WithTools(this IMcpServerBuilder builder, KernelPluginCollection plugins)
        {
            foreach (var plugin in plugins)
            {
                foreach (var function in plugin)
                {
                    builder.Services.AddSingleton(services => McpServerTool.Create(function));
                }
            }
            return builder;
        }
    }
}