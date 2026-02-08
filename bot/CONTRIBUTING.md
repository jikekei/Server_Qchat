# Contributing

## 开发环境

- .NET 8 SDK

## 本地运行

```powershell
dotnet test -c Release
dotnet run --project src/Server_Qcha.Bot -c Release
```

## 提交规范

- 不要提交 `appsettings.Local.json`、token、密码或任何隐私数据
- 如果新增命令/协议，请同步更新 `README.md`
