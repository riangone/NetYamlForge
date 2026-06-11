import re

with open("NetYamlForge/Services/Auth/UserAuthService.cs", "r", encoding="utf-8") as f:
    content = f.read()

# Fix SELECT statements
content = content.replace("SELECT * FROM AppUser WHERE UserName", "SELECT * FROM app_user WHERE user_name")
content = content.replace("SELECT * FROM AppUser WHERE Id", "SELECT * FROM app_user WHERE id")
content = content.replace("SELECT RoleName FROM AppUserRole WHERE UserName", "SELECT role_name FROM app_user_role WHERE user_name")
content = content.replace("SELECT * FROM AppUser ORDER BY Id ASC", "SELECT * FROM app_user ORDER BY id ASC")
content = content.replace("SELECT * FROM AppUser WHERE OwningProject", "SELECT * FROM app_user WHERE owning_project")
content = content.replace("SELECT COUNT(*) FROM AppUser WHERE UserName", "SELECT COUNT(*) FROM app_user WHERE user_name")

# Fix UPDATE LastLoginAt
content = content.replace("UPDATE AppUser SET LastLoginAt = @Now WHERE Id = @Id", "UPDATE app_user SET last_login_at = @Now WHERE id = @Id")

# Fix UPDATE AppUser block
old_update = """UPDATE AppUser
SET UserName = @UserName,
    PasswordHash = @PasswordHash,
    DisplayName = @DisplayName,
    PreferredLanguage = @PreferredLanguage,
    IsAdmin = @IsAdmin,
    IsActive = @IsActive,
    OwningProject = @OwningProject
WHERE Id = @Id"""
new_update = """UPDATE app_user
SET user_name = @UserName,
    password_hash = @PasswordHash,
    display_name = @DisplayName,
    preferred_language = @PreferredLanguage,
    is_admin = @IsAdmin,
    is_active = @IsActive,
    owning_project = @OwningProject
WHERE id = @Id"""
content = content.replace(old_update, new_update)

# Fix AppUserRole updates/inserts
content = content.replace("UPDATE AppUserRole SET UserName = @NewUserName WHERE UserName = @OldUserName", "UPDATE app_user_role SET user_name = @NewUserName WHERE user_name = @OldUserName")
content = content.replace("INSERT INTO AppUserRole (UserName, RoleName) VALUES (@UserName, @RoleName)", "INSERT INTO app_user_role (user_name, role_name, created_at) VALUES (@UserName, @RoleName, @Now)")
content = content.replace("DELETE FROM AppUserRole WHERE UserName = @UserName AND RoleName = @RoleName", "DELETE FROM app_user_role WHERE user_name = @UserName AND role_name = @RoleName")
content = content.replace("DELETE FROM AppUserRole WHERE UserName = @UserName", "DELETE FROM app_user_role WHERE user_name = @UserName")
content = content.replace("DELETE FROM AppUser WHERE Id = @Id", "DELETE FROM app_user WHERE id = @Id")

# Fix InsertUserAsync SQL for SQLServer
old_insert_sqlserver = """INSERT INTO AppUser (UserName, PasswordHash, DisplayName, PreferredLanguage, IsAdmin, IsActive, ExternalId, ExternalSource, OwningProject, CreatedAt)
OUTPUT INSERTED.Id
VALUES (@UserName, @PasswordHash, @DisplayName, @PreferredLanguage, @IsAdmin, @IsActive, @ExternalId, @ExternalSource, @OwningProject, @CreatedAt);"""
new_insert_sqlserver = """INSERT INTO app_user (user_name, password_hash, display_name, preferred_language, is_admin, is_active, external_id, external_source, owning_project, created_at, updated_at)
OUTPUT INSERTED.Id
VALUES (@UserName, @PasswordHash, @DisplayName, @PreferredLanguage, @IsAdmin, @IsActive, @ExternalId, @ExternalSource, @OwningProject, @CreatedAt, @CreatedAt);"""
content = content.replace(old_insert_sqlserver, new_insert_sqlserver)

# Fix InsertUserAsync SQL for Postgres
old_insert_pg = """INSERT INTO AppUser (UserName, PasswordHash, DisplayName, PreferredLanguage, IsAdmin, IsActive, ExternalId, ExternalSource, OwningProject, CreatedAt)
VALUES (@UserName, @PasswordHash, @DisplayName, @PreferredLanguage, @IsAdmin, @IsActive, @ExternalId, @ExternalSource, @OwningProject, @CreatedAt)
RETURNING Id;"""
new_insert_pg = """INSERT INTO app_user (user_name, password_hash, display_name, preferred_language, is_admin, is_active, external_id, external_source, owning_project, created_at, updated_at)
VALUES (@UserName, @PasswordHash, @DisplayName, @PreferredLanguage, @IsAdmin, @IsActive, @ExternalId, @ExternalSource, @OwningProject, @CreatedAt, @CreatedAt)
RETURNING Id;"""
content = content.replace(old_insert_pg, new_insert_pg)

# Fix InsertUserAsync SQL for MySQL
old_insert_mysql = """INSERT INTO AppUser (UserName, PasswordHash, DisplayName, PreferredLanguage, IsAdmin, IsActive, ExternalId, ExternalSource, OwningProject, CreatedAt)
VALUES (@UserName, @PasswordHash, @DisplayName, @PreferredLanguage, @IsAdmin, @IsActive, @ExternalId, @ExternalSource, @OwningProject, @CreatedAt);
SELECT LAST_INSERT_ID();"""
new_insert_mysql = """INSERT INTO app_user (user_name, password_hash, display_name, preferred_language, is_admin, is_active, external_id, external_source, owning_project, created_at, updated_at)
VALUES (@UserName, @PasswordHash, @DisplayName, @PreferredLanguage, @IsAdmin, @IsActive, @ExternalId, @ExternalSource, @OwningProject, @CreatedAt, @CreatedAt);
SELECT LAST_INSERT_ID();"""
content = content.replace(old_insert_mysql, new_insert_mysql)

# Fix InsertUserAsync SQL for SQLite
old_insert_sqlite = """INSERT INTO AppUser (UserName, PasswordHash, DisplayName, PreferredLanguage, IsAdmin, IsActive, ExternalId, ExternalSource, OwningProject, CreatedAt)
VALUES (@UserName, @PasswordHash, @DisplayName, @PreferredLanguage, @IsAdmin, @IsActive, @ExternalId, @ExternalSource, @OwningProject, @CreatedAt);
SELECT last_insert_rowid();"""
new_insert_sqlite = """INSERT INTO app_user (user_name, password_hash, display_name, preferred_language, is_admin, is_active, external_id, external_source, owning_project, created_at, updated_at)
VALUES (@UserName, @PasswordHash, @DisplayName, @PreferredLanguage, @IsAdmin, @IsActive, @ExternalId, @ExternalSource, @OwningProject, @CreatedAt, @CreatedAt);
SELECT last_insert_rowid();"""
content = content.replace(old_insert_sqlite, new_insert_sqlite)

# Fix missing @Now parameter
content = content.replace("new { UserName = input.UserName, RoleName = \"Admin\" }", "new { UserName = input.UserName, RoleName = \"Admin\", Now = DateTime.UtcNow.ToString(\"yyyy-MM-dd HH:mm:ss\") }")
content = content.replace("new { input.UserName, RoleName = \"customer\" }", "new { input.UserName, RoleName = \"customer\", Now = DateTime.UtcNow.ToString(\"yyyy-MM-dd HH:mm:ss\") }")

with open("NetYamlForge/Services/Auth/UserAuthService.cs", "w", encoding="utf-8") as f:
    f.write(content)
