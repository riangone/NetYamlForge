# Email Integration Guide

NetYamlForge now supports email sending and receiving via [MailKit](https://github.com/jstedfast/MailKit).

## Configuration

Add the following section to your `appsettings.json`:

```json
{
  "Email": {
    "Smtp": {
      "Server": "smtp.gmail.com",
      "Port": 587,
      "UseSsl": false,
      "User": "your-email@gmail.com",
      "Password": "your-app-password",
      "FromName": "NetYamlForge Admin",
      "FromAddress": "your-email@gmail.com"
    },
    "Imap": {
      "Server": "imap.gmail.com",
      "Port": 993,
      "UseSsl": true,
      "User": "your-email@gmail.com",
      "Password": "your-app-password"
    }
  }
}
```

## Features

### 1. Sending Emails via Hooks

You can automatically send emails after CRUD operations using the `send_email` hook.

**Usage in `entities.yml`:**

```yaml
hooks:
  afterCreate: "send_email:RecipientField,SubjectTemplate,BodyTemplate"
```

*   `RecipientField`: The name of the field in the entity that contains the recipient's email address.
*   `SubjectTemplate`: The email subject. You can use `{FieldName}` placeholders.
*   `BodyTemplate`: The email body (supports HTML). You can use `{FieldName}` placeholders.

**Example:**

```yaml
hooks:
  afterCreate: "send_email:email,Welcome to NetYamlForge!,Hello {name}, your account has been created."
```

### 2. Receiving Emails via Batch Jobs

You can poll for new emails and save them to a database table using a `BatchJob`.

**Usage in `batch-jobs.yml`:**

```yaml
jobs:
  poll_inbox:
    displayName: "Poll Inbox"
    type: "email_fetch"
    schedule:
      intervalSeconds: 300
    settings:
      targetTable: "received_emails"
      autoMarkRead: true
      batchSize: 20
```

*   `targetTable`: The table where emails will be saved (automatically created if it doesn't exist).
*   `autoMarkRead`: Whether to mark emails as read after fetching.
*   `batchSize`: Maximum number of emails to fetch in one run.

### 3. Batch Job Error Notifications

You can receive email notifications when a batch job fails.

**Usage in `batch-jobs.yml`:**

```yaml
jobs:
  my_job:
    ...
    settings:
      notifyEmails: "admin@example.com,dev@example.com"
```

## Technical Details

*   **Service Interface**: `NetYamlForge.Services.Email.IEmailService`
*   **Implementation**: `NetYamlForge.Services.Email.MailKitEmailService`
*   **Models**: Found in `NetYamlForge.Models.Email` and `NetYamlForge.Models.Config`
