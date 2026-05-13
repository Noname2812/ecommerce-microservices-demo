# Refresh all EF migrations

Chay 1 lenh tu root project:

```powershell
powershell -ExecutionPolicy Bypass -File .\script\migrations\refresh_migration_all.ps1
```

Script se tu dong:
- Quet tat ca service trong `src/Services`
- Chi tim `UrbanX.*.Persistence.csproj`
- Xoa toan bo thu muc `Migrations` trong moi Persistence project
- Chay `dotnet ef migrations add InitialCreate` o tung Persistence project (khong dung API startup project)
