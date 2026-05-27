# AI Email Chat Subproject

This subproject demonstrates how to automatically fetch emails, process them via an AI API (like OpenAI), and reply to the sender using the NetYamlForge batch job system.

## Setup

1. Copy `.env.example` to `.env` in this directory:
   ```bash
   cp .env.example .env
   ```
2. Edit `.env` and fill in your IMAP, SMTP, and AI credentials.
   * **Gmail Support**: Already pre-configured for Gmail IMAP/SMTP. You will need an "App Password".
   * **Gemini CLI Integration**: Set `USE_GEMINI_CLI=true` to use the local `gemini` command instead of direct API calls.
   * Set `TARGET_SENDER_EMAIL` to limit AI replies to a specific sender, or leave it empty to process all unread emails.

3. Run NetYamlForge:
   ```bash
   cd ../../
   dotnet run --project NetYamlForge
   ```

## How it works

The `jobs/chat_job.yml` defines a batch job that runs every 60 seconds (`intervalSeconds: 60`).
This job uses the custom type `ai_email_chat` which triggers `AiEmailChatExecutor` in the C# backend.

The executor:
1. Connects to your inbox via IMAP.
2. Finds unread emails.
3. Calls the configured AI API (`AI_API_ENDPOINT`) with the email content.
4. Sends the AI's generated response back to the original sender via SMTP.
5. Marks the processed email as read.
