using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace QAAnalyzer.AppErrorLogsAnalyzer
{
    public class LogAnalyzerPlugin
    {
        private readonly string _logDirectory;

        public LogAnalyzerPlugin(IConfiguration configuration)
        {
            _logDirectory = configuration["apperrorlogsdirectory"];
        }

        /// <summary>
        /// Reads and parses all multi-line log files in the configured directory.
        /// </summary>
        /// <returns>A list of parsed LogEntry records.</returns>
        private List<LogEntry> ReadAllLogs()
        {
            var logEntries = new List<LogEntry>();

            // Get all *.log files and Error.Harmony.log.1/2 files
            var logFiles = Directory.GetFiles(_logDirectory, "*.log").ToList();

            var harmonyFiles = new[] { "Error.Harmony.log.1", "Error.Harmony.log.2" }
                .Select(f => Path.Combine(_logDirectory, f))
                .Where(File.Exists);

            logFiles.AddRange(harmonyFiles);

            // Order by last write time descending and take only the 3 most recent files
            logFiles = logFiles
                .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                .Take(3)
                .ToList();

            var logEntryPattern = new Regex(
                @"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2},\d{3}) \[(ERROR)\](.+?)(?=^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2},\d{3} \[|\z)",
                RegexOptions.Multiline | RegexOptions.Singleline
            );

            foreach (var file in logFiles)
            {
                string content;
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream))
                {
                    content = reader.ReadToEnd();
                }

                var matches = logEntryPattern.Matches(content);

                foreach (Match match in matches)
                {
                    if (match.Success)
                    {
                        try
                        {
                            var timestampStr = match.Groups[1].Value;
                            var level = match.Groups[2].Value;
                            var fullMessage = match.Groups[3].Value.Trim();
                            var mainMessage = ExtractMainErrorMessage(fullMessage);

                            if (string.IsNullOrEmpty(mainMessage))
                                continue;

                            var timestamp = DateTime.ParseExact(timestampStr, "yyyy-MM-dd HH:mm:ss,fff", CultureInfo.InvariantCulture);

                            if (level.Equals("ERROR", StringComparison.OrdinalIgnoreCase))
                            {
                                logEntries.Add(new LogEntry(timestamp, level, mainMessage));
                            }
                        }
                        catch (FormatException)
                        {
                            continue;
                        }
                    }
                }
            }


            return logEntries.OrderBy(e => e.Timestamp).ToList();
        }


        /// <summary>
        /// Extracts the main error message, ignoring the extensive stack trace.
        /// </summary>
        private string ExtractMainErrorMessage(string fullMessage)
        {
            if (fullMessage.Contains("ErrorID"))
            {
                var match = Regex.Match(fullMessage, @"Message=(.+?)\r?\n");
                if (match.Success)
                {
                    return match.Groups[1].Value.Trim();
                }
                else
                    return string.Empty;
            }
            else
                return string.Empty;
            // Use a simple heuristic to find the main error line, like "Invalid column name..."
            //var match = Regex.Match(fullMessage, @"Message=(.+?)\r?\n");
            //if (match.Success)
            //{
            //    return match.Groups[1].Value.Trim();
            //}

            //// As a fallback, find the "Error occured while processing the request" line
            //var firstLine = fullMessage.Split('\n').First().Trim();
            //if (firstLine.Contains("Error occured"))
            //{
            //    return firstLine;
            //}

            //return fullMessage; // Return the full message if a simple pattern isn't found
        }

        //
        // The methods below are the same as in the previous response, but use the updated `ReadAllLogs` function.
        //

        ///// <summary>
        ///// Provides a complete summary of all errors across all log files.
        ///// </summary>
        //[KernelFunction("GetComprehensiveErrorSummary")]
        //[Description("Summarizes all errors from all log files, including date-wise summaries, recurring issues, and most frequent errors.")]
        //public string GetComprehensiveErrorSummary()
        //{
        //    var allErrors = ReadAllLogs();
        //    if (!allErrors.Any())
        //    {
        //        return "No errors found in the logs.";
        //    }

        //    var dateSummary = SummarizeErrorsDateWise(allErrors);
        //   // var recurringSummary = GetRecurringErrors(allErrors);
        //    var frequentSummary = GetFrequentErrors(allErrors);

        //    return $"--- Date-wise Error Summary ---\n{dateSummary}\n\n" +
        //          // $"--- Recurring Error Report ---\n{recurringSummary}\n\n" +
        //           $"--- Most Frequent Errors ---\n{frequentSummary}";
        //}

        ///// <summary>
        ///// Summarizes all errors on a date-by-date basis.
        ///// </summary>
        //[KernelFunction("SummarizeErrorsDateWise")]
        //[Description("Provides a summary of errors, showing the number of errors on each date.")]
        //public string SummarizeErrorsDateWise([Description("List of log entries to analyze.")] List<LogEntry> logEntries)
        //{
        //    var errorSummary = logEntries
        //        .GroupBy(e => e.Timestamp.Date)
        //        .ToDictionary(g => g.Key, g => g.Count());

        //    if (!errorSummary.Any())
        //    {
        //        return "No errors to summarize.";
        //    }

        //    var sb = new StringBuilder();
        //    foreach (var entry in errorSummary.OrderBy(kvp => kvp.Key))
        //    {
        //        sb.AppendLine($"{entry.Key:yyyy-MM-dd}: {entry.Value} errors");
        //    }
        //    return sb.ToString();
        //}

        ///// <summary>
        ///// Finds and reports errors that have occurred more than once.
        ///// </summary>
        //[KernelFunction("GetRecurringErrors")]
        //[Description("Identifies and reports errors that have occurred more than once.")]
        //public string GetRecurringErrors([Description("List of log entries to analyze.")] List<LogEntry> logEntries)
        //{
        //    var recurringErrors = logEntries
        //        .GroupBy(e => e.Message)
        //        .Where(g => g.Count() > 1)
        //        .ToDictionary(g => g.Key, g => g.Count());

        //    if (!recurringErrors.Any())
        //    {
        //        return "No recurring errors found.";
        //    }

        //    var sb = new StringBuilder();
        //    foreach (var entry in recurringErrors.OrderByDescending(kvp => kvp.Value))
        //    {
        //        sb.AppendLine($"Count: {entry.Value} - Error: {entry.Key}");
        //    }
        //    return sb.ToString();
        //}

        ///// <summary>
        ///// Identifies the most frequent errors based on message content.
        ///// </summary>
        //[KernelFunction("GetFrequentErrors")]
        //[Description("Retrieves the most frequent errors by counting their occurrences.")]
        //public string GetFrequentErrors([Description("List of log entries to analyze.")] List<LogEntry> logEntries, int topN = 5)
        //{
        //    var frequentErrors = logEntries
        //        .GroupBy(e => e.Message)
        //        .OrderByDescending(g => g.Count())
        //        .Take(topN)
        //        .ToDictionary(g => g.Key, g => g.Count());

        //    if (!frequentErrors.Any())
        //    {
        //        return "No errors found to determine frequency.";
        //    }

        //    var sb = new StringBuilder();
        //    foreach (var entry in frequentErrors)
        //    {
        //        sb.AppendLine($"Frequency: {entry.Value} - Error: {entry.Key}");
        //    }
        //    return sb.ToString();
        //}

        ///// <summary>
        ///// Provides a summary of errors for a specific date range.
        ///// </summary>
        //[KernelFunction("GetErrorSummaryByDateRange")]
        //[Description("Summarizes errors that occurred within a specific date range.")]
        //public string GetErrorSummaryByDateRange(DateTime startDate, DateTime endDate)
        //{
        //    var allLogs = ReadAllLogs();
        //    var errorsInDateRange = allLogs
        //        .Where(e => e.Timestamp.Date >= startDate.Date && e.Timestamp.Date <= endDate.Date)
        //        .ToList();

        //    if (!errorsInDateRange.Any())
        //    {
        //        return $"No errors found between {startDate:yyyy-MM-dd} and {endDate:yyyy-MM-dd}.";
        //    }

        //    var dateSummary = SummarizeErrorsDateWise(errorsInDateRange);
        //   // var recurringSummary = GetRecurringErrors(errorsInDateRange);
        //    var frequentSummary = GetFrequentErrors(errorsInDateRange);

        //    return $"--- Error Summary from {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd} ---\n\n" +
        //           $"--- Date-wise Breakdown ---\n{dateSummary}\n\n" +
        //           //$"--- Recurring Issues in Range ---\n{recurringSummary}\n\n" +
        //           $"--- Most Frequent Errors in Range ---\n{frequentSummary}";
        //}

        /// <summary>
        /// Finds the code file and line number for a given error name from the logs.
        /// </summary>
        /// <param name="errorName">The error message or part of it to search for.</param>
        /// <returns>A string with the file and line number, or a not found message.</returns>
        [KernelFunction("FindErrorCodeLocation")]
        [Description("Given an error name, returns the code file and line number where the error occurred, if available.")]
        public string FindErrorCodeLocation(string errorName)
        {
            var sb = new StringBuilder();

            var logEntries = ReadAllLogs();
            if (!logEntries.Any())
                return "No code line found for the given error name.";

            // Find the first log entry that matches the error name
            var entry = logEntries.FirstOrDefault(e => e.Message.Contains(errorName, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
                return "No code line found for the given error name.";

            // Search the original log files for the stack trace related to this error
            var logFiles = Directory.GetFiles(_logDirectory, "*.log");
            var fileLineRegex = new Regex(@"in (.+\.cs):line (\d+)", RegexOptions.Compiled);

            foreach (var file in logFiles)
            {
                var content = File.ReadAllText(file);
                // Find the log entry block that contains the error message
                if (content.Contains(entry.Message))
                {
                    // Search for file and line number in the block after the error message
                    var startIdx = content.IndexOf(entry.Message, StringComparison.OrdinalIgnoreCase);
                    if (startIdx >= 0)
                    {
                        var snippet = content.Substring(startIdx, Math.Min(10000, content.Length - startIdx)); // Search next 1000 chars
                        var match = fileLineRegex.Match(snippet);
                        if (match.Success)
                        {
                            var codeFile = match.Groups[1].Value;
                            var lineNumber = match.Groups[2].Value;
                            sb.AppendLine($"File: {codeFile}, Line: {lineNumber}");
                            //return $"File: {codeFile}, Line: {lineNumber}";
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(sb.ToString()))
                return "No code line found for the given error name.";
            else
                return sb.ToString();
        }

        /// <summary>
        /// Provides a summary of the top 10 most frequently occurring error messages and their counts.
        /// </summary>
        [KernelFunction("GetErrorCountSummary")]
        [Description("Returns a summary of each unique error message and the number of times it occurred.")]
        public string GetErrorCountSummary()
        {
            var sb = new StringBuilder();

            try
            {

                var logEntries = ReadAllLogs();
                if (!logEntries.Any())
                    return "No errors found in the logs.";

                var errorCounts = logEntries
                    .GroupBy(e => e.Message)
                    .OrderByDescending(g => g.Count())
                    //.Take(10)
                    .ToDictionary(g => g.Key, g => g.Count());


                foreach (var entry in errorCounts)
                {
                    sb.AppendLine($"Count: {entry.Value} - Error: {entry.Key}");
                }

                var length = sb.ToString().Length;


            }
            catch (Exception ex)
            {
                var errorLogPath = Path.Combine(Directory.GetCurrentDirectory(), "error.log");
                var errorMessage = $"{DateTime.UtcNow:O} - Error: {ex.Message}{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}";
                File.AppendAllText(errorLogPath, errorMessage);
                throw new Exception();

            }
            return sb.ToString();

        }

        // LogEntry record definition (unchanged)
        public record LogEntry(DateTime Timestamp, string Level, string Message);

    }
}