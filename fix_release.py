import re

with open("NetYamlForge/Services/Auth/UserAuthService.cs", "r", encoding="utf-8") as f:
    content = f.read()

content = content.replace("_connectionManager.ReleaseConnection(conn);", "conn.Close();\n                conn.Dispose();")

with open("NetYamlForge/Services/Auth/UserAuthService.cs", "w", encoding="utf-8") as f:
    f.write(content)
