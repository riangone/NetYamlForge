namespace NetYamlForge.Models.Config;

public class EmailSettings
{
    public SmtpSettings Smtp { get; set; } = new();
    public ImapSettings Imap { get; set; } = new();
}

public class SmtpSettings
{
    public string Server { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = false;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromName { get; set; } = "NetYamlForge";
    public string FromAddress { get; set; } = string.Empty;
}

public class ImapSettings
{
    public string Server { get; set; } = string.Empty;
    public int Port { get; set; } = 993;
    public bool UseSsl { get; set; } = true;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
