using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace QAAnalyzer.JIRAPlugin;

/// <summary>
/// Client for interacting with Atlassian JIRA REST API
/// </summary>
public class JiraClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _email;
    private readonly string _apiToken;

    public JiraClient(IConfiguration configuration)
    {
        _baseUrl = configuration["JiraBaseUrl"]?.TrimEnd('/') ?? throw new ArgumentException("JiraBaseUrl is required");
        _email = configuration["JiraEmail"] ?? throw new ArgumentException("JiraEmail is required");
        _apiToken = configuration["JiraApiToken"] ?? throw new ArgumentException("JiraApiToken is required");
        
        _httpClient = new HttpClient();
        var authToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_email}:{_apiToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>
    /// Tests the JIRA connection and authentication
    /// </summary>
    /// <returns>Connection status message</returns>
    [KernelFunction, Description("Tests the JIRA connection and authentication")]
    public async Task<string> TestConnectionAsync()
    {
        try
        {
            // Test with a simple API call to get current user
            var requestUrl = $"{_baseUrl}/rest/api/3/myself";
            var response = await _httpClient.GetAsync(requestUrl);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                return $"Connection failed with status {response.StatusCode}: {errorContent}";
            }

            var content = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonDocument.Parse(content);
            var displayName = jsonDoc.RootElement.GetProperty("displayName").GetString();
            
            return $"Successfully connected to JIRA as: {displayName}";
        }
        catch (Exception ex)
        {
            return $"Connection test failed: {ex.Message}";
        }
    }

    
    /// <summary>
    /// Creates a new JIRA ticket
    /// </summary>
    /// <param name="projectKey">JIRA project key</param>
    /// <param name="issueType">Issue type (e.g., "Bug", "Task", "Story")</param>
    /// <param name="summary">Ticket summary/title</param>
    /// <param name="epicKey">Epic ticket key to link this ticket to (e.g., "PROJ-123")</param>
    /// <param name="description">Ticket description</param>
    /// <returns>Created ticket key</returns>
    [KernelFunction, Description("Creates a new JIRA ticket linked to an Epic")]
    public async Task<string> CreateTicketAsync(
        [Description("Ticket summary/title")] string summary,
        [Description("Ticket description")] string description = "")
    {
        try
        {
            string projectKey = "MHFW";
            string issueType = "Task";
            string epicKey = "MHFW-52438";

            var requestUrl = $"{_baseUrl}/rest/api/3/issue";
            
            // Build fields dynamically
            var fields = new Dictionary<string, object>
            {
                { "project", new { key = projectKey } },
                { "summary", summary },
                { "description", new
                    {
                        type = "doc",
                        version = 1,
                        content = new[]
                        {
                            new
                            {
                                type = "paragraph",
                                content = new[]
                                {
                                    new
                                    {
                                        type = "text",
                                        text = description
                                    }
                                }
                            }
                        }
                    }
                },
                { "issuetype", new { name = issueType } }
            };

            // Add Epic link (mandatory)
            fields.Add("parent", new { key = epicKey });

            var payload = new { fields };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync(requestUrl, content);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"JIRA API request failed with status {response.StatusCode}: {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonDocument.Parse(responseContent);
            var ticketKey = jsonDoc.RootElement.GetProperty("key").GetString();

            return $"Ticket created successfully: {ticketKey}";
        }
        catch (Exception ex)
        {
            throw new Exception($"Error creating JIRA ticket: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Adds a comment to a JIRA ticket
    /// </summary>
    /// <param name="ticketKey">JIRA ticket key</param>
    /// <param name="comment">Comment text</param>
    /// <returns>Success message</returns>
    [KernelFunction, Description("Adds a comment to a JIRA ticket")]
    public async Task<string> AddCommentAsync(
        [Description("JIRA ticket key (e.g., 'PROJ-123')")] string ticketKey,
        [Description("Comment text to add")] string comment)
    {
        try
        {
            var requestUrl = $"{_baseUrl}/rest/api/3/issue/{ticketKey}/comment";
            
            var payload = new
            {
                body = new
                {
                    type = "doc",
                    version = 1,
                    content = new[]
                    {
                        new
                        {
                            type = "paragraph",
                            content = new[]
                            {
                                new
                                {
                                    type = "text",
                                    text = comment
                                }
                            }
                        }
                    }
                }
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync(requestUrl, content);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"JIRA API request failed with status {response.StatusCode}: {errorContent}");
            }

            return $"Comment added successfully to {ticketKey}";
        }
        catch (Exception ex)
        {
            throw new Exception($"Error adding comment to {ticketKey}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Lists all boards in JIRA
    /// </summary>
    /// <returns>List of board IDs and names</returns>
    [KernelFunction, Description("Lists all available JIRA boards")]
    public async Task<string> ListBoardsAsync()
    {
        try
        {
            var requestUrl = $"{_baseUrl}/rest/agile/1.0/board";
            var response = await _httpClient.GetAsync(requestUrl);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"JIRA API request failed with status {response.StatusCode}: {errorContent}");
            }

            var content = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonDocument.Parse(content);
            var values = jsonDoc.RootElement.GetProperty("values");
            
            var boards = new List<string>();
            foreach (var board in values.EnumerateArray())
            {
                var id = board.GetProperty("id").GetInt32();
                var name = board.GetProperty("name").GetString();
                var type = board.GetProperty("type").GetString();
                boards.Add($"Board ID: {id}, Name: {name}, Type: {type}");
            }

            return string.Join("\n", boards);
        }
        catch (Exception ex)
        {
            throw new Exception($"Error retrieving boards: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Lists all sprints for a specific board
    /// </summary>
    /// <param name="boardId">JIRA board ID</param>
    /// <returns>List of sprint IDs and names</returns>
    [KernelFunction, Description("Lists all sprints for a specific JIRA board")]
    public async Task<string> ListSprintsAsync(
        [Description("JIRA board ID")] int boardId)
    {
        try
        {
            var requestUrl = $"{_baseUrl}/rest/agile/1.0/board/{boardId}/sprint";
            var response = await _httpClient.GetAsync(requestUrl);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"JIRA API request failed with status {response.StatusCode}: {errorContent}");
            }

            var content = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonDocument.Parse(content);
            var values = jsonDoc.RootElement.GetProperty("values");
            
            var sprints = new List<string>();
            foreach (var sprint in values.EnumerateArray())
            {
                var id = sprint.GetProperty("id").GetInt32();
                var name = sprint.GetProperty("name").GetString();
                var state = sprint.GetProperty("state").GetString();
                sprints.Add($"Sprint ID: {id}, Name: {name}, State: {state}");
            }

            return string.Join("\n", sprints);
        }
        catch (Exception ex)
        {
            throw new Exception($"Error retrieving sprints for board {boardId}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Adds a ticket to a sprint
    /// </summary>
    /// <param name="sprintId">JIRA sprint ID</param>
    /// <param name="ticketKey">JIRA ticket key</param>
    /// <returns>Success message</returns>
    [KernelFunction, Description("Adds a JIRA ticket to a sprint")]
    public async Task<string> AddTicketToSprintAsync(
        [Description("JIRA sprint ID")] int sprintId,
        [Description("JIRA ticket key (e.g., 'PROJ-123')")] string ticketKey)
    {
        try
        {
            // First get the issue ID from the ticket key
            var issueUrl = $"{_baseUrl}/rest/api/3/issue/{ticketKey}";
            var issueResponse = await _httpClient.GetAsync(issueUrl);
            
            if (!issueResponse.IsSuccessStatusCode)
            {
                var errorContent = await issueResponse.Content.ReadAsStringAsync();
                throw new HttpRequestException($"Failed to get ticket {ticketKey}: {errorContent}");
            }

            var issueContent = await issueResponse.Content.ReadAsStringAsync();
            var issueDoc = JsonDocument.Parse(issueContent);
            var issueId = issueDoc.RootElement.GetProperty("id").GetString();

            // Now add the issue to the sprint
            var requestUrl = $"{_baseUrl}/rest/agile/1.0/sprint/{sprintId}/issue";
            
            var payload = new
            {
                issues = new[] { issueId }
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync(requestUrl, content);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"JIRA API request failed with status {response.StatusCode}: {errorContent}");
            }

            return $"Ticket {ticketKey} added successfully to sprint {sprintId}";
        }
        catch (Exception ex)
        {
            throw new Exception($"Error adding ticket {ticketKey} to sprint {sprintId}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Creates a JIRA ticket and adds it to a sprint
    /// </summary>
    /// <param name="projectKey">JIRA project key</param>
    /// <param name="issueType">Issue type</param>
    /// <param name="summary">Ticket summary</param>
    /// <param name="sprintId">Sprint ID to add the ticket to</param>
    /// <param name="description">Ticket description</param>
    /// <returns>Created ticket key</returns>
    [KernelFunction, Description("Creates a new JIRA ticket and adds it to a sprint")]
    public async Task<string> CreateTicketInSprintAsync(
        [Description("JIRA project key")] string projectKey,
        [Description("Issue type (e.g., 'Bug', 'Task', 'Story')")] string issueType,
        [Description("Ticket summary/title")] string summary,
        [Description("Sprint ID to add the ticket to")] int sprintId,
        [Description("Ticket description")] string description = "")
    {
        try
        {
            var requestUrl = $"{_baseUrl}/rest/api/3/issue";
            
            var payload = new
            {
                fields = new
                {
                    project = new { key = projectKey },
                    summary = summary,
                    description = new
                    {
                        type = "doc",
                        version = 1,
                        content = new[]
                        {
                            new
                            {
                                type = "paragraph",
                                content = new[]
                                {
                                    new
                                    {
                                        type = "text",
                                        text = description
                                    }
                                }
                            }
                        }
                    },
                    issuetype = new { name = issueType },
                    customfield_10020 = sprintId  // Sprint field (customfield_10020 is the default sprint field ID)
                }
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync(requestUrl, content);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"JIRA API request failed with status {response.StatusCode}: {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonDocument.Parse(responseContent);
            var ticketKey = jsonDoc.RootElement.GetProperty("key").GetString();

            return $"Ticket created successfully in sprint {sprintId}: {ticketKey}";
        }
        catch (Exception ex)
        {
            throw new Exception($"Error creating JIRA ticket in sprint: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Gets a specific ticket by its key
    /// </summary>
    /// <param name="ticketKey">JIRA ticket key (e.g., "PROJ-123")</param>
    /// <returns>JIRA ticket details</returns>
    [KernelFunction, Description("Gets a specific JIRA ticket by its key")]
    public async Task<JiraTicket> GetTicketAsync(
        [Description("JIRA ticket key (e.g., 'PROJ-123')")] string ticketKey)
    {
        try
        {
            // Use API v3 as required by JIRA Cloud
            var requestUrl = $"{_baseUrl}/rest/api/3/issue/{ticketKey}";
            var response = await _httpClient.GetAsync(requestUrl);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"JIRA API request failed with status {response.StatusCode}: {errorContent}");
            }

            var content = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonDocument.Parse(content);

            return ParseJiraTicket(jsonDoc.RootElement);
        }
        catch (Exception ex)
        {
            throw new Exception($"Error retrieving JIRA ticket {ticketKey}: {ex.Message}", ex);
        }
    }

    private JiraTicket ParseJiraTicket(JsonElement issue)
    {
        var fields = issue.GetProperty("fields");
        
        return new JiraTicket
        {
            Key = issue.GetProperty("key").GetString() ?? string.Empty,
            Summary = GetStringProperty(fields, "summary"),
            Status = GetNestedStringProperty(fields, "status", "name"),
            Priority = GetNestedStringProperty(fields, "priority", "name"),
            Assignee = GetNestedStringProperty(fields, "assignee", "displayName"),
            Reporter = GetNestedStringProperty(fields, "reporter", "displayName"),
            Created = GetDateTimeProperty(fields, "created"),
            Updated = GetNullableDateTimeProperty(fields, "updated"),
            Description = GetStringProperty(fields, "description"),
            IssueType = GetNestedStringProperty(fields, "issuetype", "name")
        };
    }

    private string GetStringProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var property) && property.ValueKind != JsonValueKind.Null)
        {
            return property.GetString() ?? string.Empty;
        }
        return string.Empty;
    }

    private string GetNestedStringProperty(JsonElement element, string propertyName, string nestedPropertyName)
    {
        if (element.TryGetProperty(propertyName, out var property) && 
            property.ValueKind == JsonValueKind.Object &&
            property.TryGetProperty(nestedPropertyName, out var nestedProperty))
        {
            return nestedProperty.GetString() ?? string.Empty;
        }
        return string.Empty;
    }

    private DateTime GetDateTimeProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var property) && property.ValueKind != JsonValueKind.Null)
        {
            if (DateTime.TryParse(property.GetString(), out var dateTime))
            {
                return dateTime;
            }
        }
        return DateTime.MinValue;
    }

    private DateTime? GetNullableDateTimeProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var property) && property.ValueKind != JsonValueKind.Null)
        {
            if (DateTime.TryParse(property.GetString(), out var dateTime))
            {
                return dateTime;
            }
        }
        return null;
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}
