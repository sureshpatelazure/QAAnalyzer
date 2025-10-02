using Microsoft.SemanticKernel;
using QAAnalyzer;
using QAAnalyzer.AppErrorLogsAnalyzer;
using QAAnalyzer.QAStagesAnalyzer;

// configuration 
var configuration = new ConfigurationBuilder()
              .SetBasePath(Directory.GetCurrentDirectory())
              .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
              .Build();

//Semanti Kernel
var semanticbuilder = Kernel.CreateBuilder();
LogAnalyzerPlugin logAnalyzerPlugin = new LogAnalyzerPlugin(configuration);
//logAnalyzerPlugin.ReadErrorLogs("b6ecc8e2-6da2-466d-a0b1-6471fce7f092");
semanticbuilder.Plugins.AddFromObject(logAnalyzerPlugin);

StagesAnalyzer testCasesAnalyzer = new StagesAnalyzer(configuration);
//testCasesAnalyzer.GetFailedScenarios("HS Smoke_WEB06 - Playwright Suite - Scale");
semanticbuilder.Plugins.AddFromObject(testCasesAnalyzer);

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
