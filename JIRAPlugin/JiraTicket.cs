namespace QAAnalyzer.JIRAPlugin;

/// <summary>
/// Represents a JIRA ticket with basic information
/// </summary>
public class JiraTicket
{
    public string Key { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Assignee { get; set; } = string.Empty;
    public string Reporter { get; set; } = string.Empty;
    public DateTime Created { get; set; }
    public DateTime? Updated { get; set; }
    public string Description { get; set; } = string.Empty;
    public string IssueType { get; set; } = string.Empty;
}
