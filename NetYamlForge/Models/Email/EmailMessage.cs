namespace NetYamlForge.Models.Email;

public class EmailMessage
{
    public string Id { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsHtml { get; set; }
    public DateTimeOffset Date { get; set; }
    public List<EmailAttachment> Attachments { get; set; } = new();
    public string MessageId { get; set; } = string.Empty;
    public string InReplyTo { get; set; } = string.Empty;
    public string References { get; set; } = string.Empty;
}

public class EmailAttachment
{
    public string FileName { get; set; } = string.Empty;
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public string ContentType { get; set; } = "application/octet-stream";
}
