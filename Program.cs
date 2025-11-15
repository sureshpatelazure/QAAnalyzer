using Microsoft.SemanticKernel;
using QAAnalyzer;
using QAAnalyzer.AppErrorLogsAnalyzer;
using QAAnalyzer.QAStagesAnalyzer;
using QAAnalyzer.JIRAPlugin;

// configuration 
var configuration = new ConfigurationBuilder()
              .SetBasePath(Directory.GetCurrentDirectory())
              .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
              .Build();

//Semanti Kernel
var semanticbuilder = Kernel.CreateBuilder();

//LogAnalyzerPlugin
LogAnalyzerPlugin logAnalyzerPlugin = new LogAnalyzerPlugin(configuration);
semanticbuilder.Plugins.AddFromObject(logAnalyzerPlugin);

//StagesAnalyzer
StagesAnalyzer testCasesAnalyzer = new StagesAnalyzer(configuration);
semanticbuilder.Plugins.AddFromObject(testCasesAnalyzer);

//JiraClient
JiraClient jiraClient = new JiraClient(configuration);
semanticbuilder.Plugins.AddFromObject(jiraClient);

var semantickernel = semanticbuilder.Build();

////////// ASP.NET Core Web Application ////////  
var builder = WebApplication.CreateBuilder(args);
//builder.Services.AddSingleton(new LogAnalyzerPlugin(logDirectory));

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools(semantickernel.Plugins); 


var app = builder.Build();
//app.MapGet("/", () => "Hello, world!");
//app.UsePathBase("/myapp");
app.MapMcp();
app.Run();
