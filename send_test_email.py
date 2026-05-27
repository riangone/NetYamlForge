import smtplib
from email.mime.text import MIMEText
from email.mime.multipart import MIMEMultipart
import os

# Load env from the project directory
env_path = 'NetYamlForge/projects/email-chat/.env'
if not os.path.exists(env_path):
    print(f"Error: .env not found at {env_path}")
    exit(1)

with open(env_path, 'r') as f:
    for line in f:
        line = line.strip()
        if not line or line.startswith('#'):
            continue
        if '=' in line:
            key, value = line.split('=', 1)
            # Remove quotes
            value = value.strip()
            if (value.startswith('"') and value.endswith('"')) or (value.startswith("'") and value.endswith("'")):
                value = value[1:-1]
            os.environ[key.strip()] = value

smtp_server = os.environ.get('SMTP_SERVER')
smtp_port = int(os.environ.get('SMTP_PORT', 587))
smtp_user = os.environ.get('SMTP_USER')
smtp_pass = os.environ.get('SMTP_PASSWORD')

if not all([smtp_server, smtp_user, smtp_pass]):
    print("Error: Missing SMTP configuration in .env")
    exit(1)

sender_email = smtp_user
receiver_email = smtp_user # Send to self

message = MIMEMultipart()
message["From"] = f"Test Sender <{sender_email}>"
message["To"] = receiver_email
message["Subject"] = "Test AI Reply"

body = "Hello AI, this is a test message to verify if you can reply correctly without leaking CLI logs. How are you today?"
message.attach(MIMEText(body, "plain"))

try:
    print(f"Connecting to {smtp_server}:{smtp_port}...")
    server = smtplib.SMTP(smtp_server, smtp_port)
    server.starttls()
    print(f"Logging in as {smtp_user}...")
    server.login(smtp_user, smtp_pass)
    print("Sending email...")
    server.sendmail(sender_email, receiver_email, message.as_string())
    server.quit()
    print("Test email sent successfully!")
except Exception as e:
    print(f"Error: {e}")
