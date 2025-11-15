using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;

namespace QAAnalyzer.QAStagesAnalyzer
{
    public class StagesAnalyzer
    {
        private readonly string _logDirectory;
        private readonly int _takeLast;
        public StagesAnalyzer(IConfiguration configuration)
        {

            _logDirectory = configuration["stageslogsdirectory"];
            _takeLast = int.TryParse(configuration["nooflastruns"], out var n) ? n : 3;
        }

        /// <summary>
        /// Extracts scenario execution summaries from all log files in the configured directory.
        /// </summary>
        [KernelFunction("ExtractScenarioSummaries")]
        [Description("Scans all log files and returns a structured summary of scenario execution results, including stage name, datetime, passed, failed, and total scenarios.")]
        public List<ScenarioSummary> ExtractScenarioSummaries()
        {

            var summaries = new List<ScenarioSummary>();
            try
            {
                List<(string FilePath, string StageName, string HHMM)> logFiles = GetLastLogFilesByDate(_logDirectory);

                // var pattern = @"^(?<datetime>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d+Z)\s+(?<total>\d+)\s+scenarios\s+\((?:(?<failed>\d+)\s+failed,\s+)?(?<passed>\d+)\s+passed\)";
               // var pattern = @"^(?<datetime>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d+Z)\s+(?<total>\d+)\s+scenarios\s+\((?:(?<failed>\d+)\s+failed(?:,\s*)?)?(?:(?<passed>\d+)\s+passed)?\)";
                var pattern = @"^(?<datetime>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d+Z)\s+(?<total>\d+)\s+scenario[s]?\s+\((?:(?<failed>\d+)\s+failed(?:,\s*)?)?(?:(?<passed>\d+)\s+passed)?\)";

                var regex = new Regex(pattern);

                foreach (var lfile in logFiles)
                {
                    foreach (var line in File.ReadLines(lfile.FilePath))
                    {
                        var match = regex.Match(line);
                        if (match.Success)
                        {
                            summaries.Add(new ScenarioSummary
                            {
                                StageName = lfile.StageName,
                                Datetime = match.Groups["datetime"].Value,
                                Passed = match.Groups["passed"].Success ? int.Parse(match.Groups["passed"].Value) : 0,
                                Failed = match.Groups["failed"].Success ? int.Parse(match.Groups["failed"].Value) : 0,
                                Total = int.Parse(match.Groups["total"].Value)
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                var errorLogPath = Path.Combine(Directory.GetCurrentDirectory(), "error.log");
                var errorMessage = $"{DateTime.UtcNow:O} - Error: {ex.Message}{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}";
                 File.AppendAllText(errorLogPath, errorMessage);
                throw new Exception();
               
            }
            return summaries;
        }

        /// <summary>
        /// Returns the last N files from the directory, sorted by the date in the filename (ascending).
        /// Filename format: Playwright (stagename@yyymmdd) EVV Regression Suite@@20250917
        /// </summary>
        /// <param name="directory">Directory to search for files.</param>
        /// <param name="takeLast">Number of files to return.</param>
        /// <returns>List of file paths for the last N files in ascending order by date.</returns>
        private List<(string FilePath, string StageName, string HHMM)> GetLastLogFilesByDate(string directory)
        {
            var pattern = @"^(?<stageName>.+)@@(?<date>\d{8})(?<hhmm>\d{4})(?<ss>\d{2})$";
            var regex = new Regex(pattern);

            var files = Directory.GetFiles(directory);
            var datedFiles = new List<(string FilePath, string StageName, DateTime Date, string HHMM, string SS)>();

            foreach (var file in files)
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                var match = regex.Match(fileName);
                if (match.Success)
                {
                    var dateStr = match.Groups["date"].Value;
                    var hhmm = match.Groups["hhmm"].Value;
                    var ss = match.Groups["ss"].Value;
                    var stageName = match.Groups["stageName"].Value;
                    if (DateTime.TryParseExact(dateStr, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var date))
                    {
                        datedFiles.Add((file, stageName, date, hhmm, ss));
                    }
                }
            }

            // Sort by date, hhmm, and ss descending, then take last N per stage
            var lastFiles = datedFiles
                .GroupBy(f => f.StageName)
                .SelectMany(g => g
                    .OrderByDescending(f => f.Date)
                    .ThenByDescending(f => f.HHMM)
                    .ThenByDescending(f => f.SS)
                    .Take(_takeLast))
                .Select(f => (f.FilePath, f.StageName, $"{f.HHMM}{f.SS}"))
                .ToList();

            return lastFiles;
        }

        /// <summary>
        /// Scans the last log file for the specified stage and returns a list of failed scenario descriptions.
        /// </summary>
        [KernelFunction("GetFailedScenarios")]
        [Description("Scans the last log file for the given stage name and returns a list of failed scenario descriptions.")]
        public List<FailedScenarioInfo> GetFailedScenarios(string stageName)
        {
            var failedScenarios = new List<FailedScenarioInfo>();
            try
            {
                var logFiles = GetLastLogFilesByDate(_logDirectory)
                    .Where(f => f.StageName.Equals(stageName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (!logFiles.Any())
                    return failedScenarios;

                var lastLogFile = logFiles.First().FilePath;
                string currentDatetime = null;
                string currentScenario = null;
                bool inFailureSection = false;

                var failureRegex = new Regex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d+Z\s+Failures:");
                var warningRegex = new Regex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d+Z\s+Warnings:");
                var scenarioRegex = new Regex(@"^(?<datetime>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d+Z).*\bScenario:\s+(?<scenario>.+)$");

                var lines = File.ReadLines(lastLogFile).ToList();
                for (int i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];

                    if (warningRegex.IsMatch(line))
                        break;

                    if (failureRegex.IsMatch(line))
                    {
                        inFailureSection = true;
                        continue;
                    }

                    if (!inFailureSection)
                        continue;

                    var scenarioMatch = scenarioRegex.Match(line);
                    if (scenarioMatch.Success)
                    {
                        currentDatetime = scenarioMatch.Groups["datetime"].Value;
                        currentScenario = scenarioMatch.Groups["scenario"].Value;
                        continue;
                    }

                    if (line.Contains("??") && currentScenario != null)
                    {
                        var errorDescription = new StringBuilder();
                        errorDescription.AppendLine(line.Trim());

                        // Read subsequent lines until warningRegex or next scenarioMatch
                        int j = i + 1;
                        for (; j < lines.Count; j++)
                        {
                            var nextLine = lines[j];
                            if (warningRegex.IsMatch(nextLine) || scenarioRegex.IsMatch(nextLine))
                                break;
                            if(string.IsNullOrEmpty(errorDescription.ToString()))
                                errorDescription.AppendLine(nextLine.Trim());
                        }
                        failedScenarios.Add(new FailedScenarioInfo
                        {
                            Datetime = currentDatetime,
                            ScenarioName = currentScenario,
                            ErrorDescription = errorDescription.ToString().TrimEnd()
                        });
                        currentScenario = null;
                        currentDatetime = null;
                        i = j - 1; // Continue from where we stopped
                    }
                }
            }
            catch (Exception ex)
            {
                var errorLogPath = Path.Combine(Directory.GetCurrentDirectory(), "error.log");
                var errorMessage = $"{DateTime.UtcNow:O} - Error: {ex.Message}{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}";
                File.AppendAllText(errorLogPath, errorMessage);
                throw;
            }
            return failedScenarios;
        }

        /// <summary>
        /// Analyzes failed scenarios and provides root cause analysis for each test case.
        /// </summary>
        [KernelFunction("GetFailedScenariosWithRootCause")]
        [Description("Scans the last log file for the given stage name and returns a list of failed scenarios with root cause analysis for each test case.")]
        public List<FailedScenarioInfo> GetFailedScenariosWithRootCause(string stageName)
        {
            var failedScenarios = new List<FailedScenarioInfo>();
            try
            {
                var logFiles = GetLastLogFilesByDate(_logDirectory)
                    .Where(f => f.StageName.Equals(stageName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (!logFiles.Any())
                    return failedScenarios;

                var lastLogFile = logFiles.First().FilePath;
                string currentDatetime = null;
                string currentScenario = null;
                bool inFailureSection = false;

                var failureRegex = new Regex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d+Z\s+Failures:");
                var warningRegex = new Regex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d+Z\s+Warnings:");
                var scenarioRegex = new Regex(@"^(?<datetime>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d+Z).*\bScenario:\s+(?<scenario>.+)$");
                var filePathRegex = new Regex(@"at\s+(?<filepath>[^:]+):(?<line>\d+):(?<col>\d+)");

                var lines = File.ReadLines(lastLogFile).ToList();
                for (int i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];

                    if (warningRegex.IsMatch(line))
                        break;

                    if (failureRegex.IsMatch(line))
                    {
                        inFailureSection = true;
                        continue;
                    }

                    if (!inFailureSection)
                        continue;

                    var scenarioMatch = scenarioRegex.Match(line);
                    if (scenarioMatch.Success)
                    {
                        currentDatetime = scenarioMatch.Groups["datetime"].Value;
                        currentScenario = scenarioMatch.Groups["scenario"].Value;
                        continue;
                    }

                    if (line.Contains("??") && currentScenario != null)
                    {
                        var errorDescription = new StringBuilder();
                        var stackTrace = new StringBuilder();
                        var fullError = new StringBuilder();
                        errorDescription.AppendLine(line.Trim());
                        fullError.AppendLine(line.Trim());

                        string filePath = null;
                        int? lineNumber = null;

                        // Read subsequent lines until warningRegex or next scenarioMatch
                        int j = i + 1;
                        for (; j < lines.Count; j++)
                        {
                            var nextLine = lines[j];
                            if (warningRegex.IsMatch(nextLine) || scenarioRegex.IsMatch(nextLine))
                                break;

                            fullError.AppendLine(nextLine.Trim());

                            // Check if line contains stack trace
                            if (nextLine.Contains("at ") || nextLine.Contains("Error:"))
                            {
                                stackTrace.AppendLine(nextLine.Trim());

                                // Extract file path and line number
                                var fileMatch = filePathRegex.Match(nextLine);
                                if (fileMatch.Success && filePath == null)
                                {
                                    filePath = fileMatch.Groups["filepath"].Value;
                                    lineNumber = int.Parse(fileMatch.Groups["line"].Value);
                                }
                            }
                        }

                        var scenarioInfo = new FailedScenarioInfo
                        {
                            Datetime = currentDatetime,
                            ScenarioName = currentScenario,
                            ErrorDescription = errorDescription.ToString().TrimEnd(),
                            StackTrace = stackTrace.ToString().TrimEnd(),
                            FilePath = filePath,
                            LineNumber = lineNumber
                        };

                        // Analyze and categorize the root cause
                        AnalyzeRootCause(scenarioInfo, fullError.ToString());

                        failedScenarios.Add(scenarioInfo);
                        currentScenario = null;
                        currentDatetime = null;
                        i = j - 1; // Continue from where we stopped
                    }
                }
            }
            catch (Exception ex)
            {
                var errorLogPath = Path.Combine(Directory.GetCurrentDirectory(), "error.log");
                var errorMessage = $"{DateTime.UtcNow:O} - Error: {ex.Message}{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}";
                File.AppendAllText(errorLogPath, errorMessage);
                throw;
            }
            return failedScenarios;
        }

        /// <summary>
        /// Analyzes the error details and determines the root cause category and description.
        /// </summary>
        private void AnalyzeRootCause(FailedScenarioInfo scenarioInfo, string fullError)
        {
            var errorLower = fullError.ToLower();

            // Timeout errors
            if (errorLower.Contains("timeout") || errorLower.Contains("timed out"))
            {
                scenarioInfo.ErrorCategory = "Timeout";
                if (errorLower.Contains("navigation"))
                    scenarioInfo.RootCause = "Page navigation timeout - The page took too long to load or navigate.";
                else if (errorLower.Contains("waiting for"))
                    scenarioInfo.RootCause = "Element wait timeout - Expected element or condition did not appear within the timeout period.";
                else
                    scenarioInfo.RootCause = "Operation timeout - The operation exceeded the allowed time limit.";
            }
            // Element not found errors
            else if (errorLower.Contains("not found") || errorLower.Contains("unable to find") || 
                     errorLower.Contains("does not exist") || errorLower.Contains("no such element"))
            {
                scenarioInfo.ErrorCategory = "Element Not Found";
                scenarioInfo.RootCause = "Element locator issue - The expected element could not be found on the page. This may indicate a UI change, incorrect selector, or timing issue.";
            }
            // Assertion errors
            else if (errorLower.Contains("expected") || errorLower.Contains("assert") || 
                     errorLower.Contains("to be") || errorLower.Contains("to equal"))
            {
                scenarioInfo.ErrorCategory = "Assertion Failed";
                scenarioInfo.RootCause = "Assertion mismatch - The actual value did not match the expected value. This may indicate a functional issue or incorrect test expectation.";
            }
            // Network errors
            else if (errorLower.Contains("network") || errorLower.Contains("failed to fetch") || 
                     errorLower.Contains("net::err") || errorLower.Contains("connection"))
            {
                scenarioInfo.ErrorCategory = "Network Error";
                scenarioInfo.RootCause = "Network communication issue - Failed to communicate with the server or load resources.";
            }
            // Permission/Authentication errors
            else if (errorLower.Contains("unauthorized") || errorLower.Contains("forbidden") || 
                     errorLower.Contains("access denied") || errorLower.Contains("authentication"))
            {
                scenarioInfo.ErrorCategory = "Authorization/Permission";
                scenarioInfo.RootCause = "Authorization failure - Insufficient permissions or authentication failed.";
            }
            // JavaScript errors
            else if (errorLower.Contains("javascript") || errorLower.Contains("script error") || 
                     errorLower.Contains("evaluation failed"))
            {
                scenarioInfo.ErrorCategory = "JavaScript Error";
                scenarioInfo.RootCause = "JavaScript execution error - Error occurred while executing JavaScript code on the page.";
            }
            // Element state errors
            else if (errorLower.Contains("not visible") || errorLower.Contains("not clickable") || 
                     errorLower.Contains("disabled") || errorLower.Contains("not interactable"))
            {
                scenarioInfo.ErrorCategory = "Element State";
                scenarioInfo.RootCause = "Element state issue - The element exists but is not in the expected state (hidden, disabled, or blocked by another element).";
            }
            // Frame/Context errors
            else if (errorLower.Contains("frame") || errorLower.Contains("context") || 
                     errorLower.Contains("detached"))
            {
                scenarioInfo.ErrorCategory = "Frame/Context Error";
                scenarioInfo.RootCause = "Frame or context issue - The target frame was detached or the execution context was destroyed.";
            }
            // File/Download errors
            else if (errorLower.Contains("download") || errorLower.Contains("file"))
            {
                scenarioInfo.ErrorCategory = "File/Download Error";
                scenarioInfo.RootCause = "File operation issue - Failed to download or handle file operations.";
            }
            // Screenshot/Video errors
            else if (errorLower.Contains("screenshot") || errorLower.Contains("video"))
            {
                scenarioInfo.ErrorCategory = "Media Capture Error";
                scenarioInfo.RootCause = "Media capture issue - Failed to capture screenshot or video.";
            }
            // Browser/Page crash
            else if (errorLower.Contains("crash") || errorLower.Contains("terminated") || 
                     errorLower.Contains("browser closed"))
            {
                scenarioInfo.ErrorCategory = "Browser Crash";
                scenarioInfo.RootCause = "Browser crash - The browser or page crashed unexpectedly during test execution.";
            }
            // Data-related errors
            else if (errorLower.Contains("null") || errorLower.Contains("undefined") || 
                     errorLower.Contains("invalid") || errorLower.Contains("parse"))
            {
                scenarioInfo.ErrorCategory = "Data/Validation Error";
                scenarioInfo.RootCause = "Data validation issue - Invalid, null, or malformed data was encountered.";
            }
            // Default/Unknown
            else
            {
                scenarioInfo.ErrorCategory = "Unknown";
                scenarioInfo.RootCause = "Unclassified error - Review the error description and stack trace for more details.";
            }
        }

    }
}
