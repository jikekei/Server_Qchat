# Database Credential Exposure and Live Tests

[English documentation index](../README.md) · [简体中文](../../zh-CN/guides/database-security.md)

## Changes in the current source tree

The old `DatabaseManagementTests.cs` containing a database connection string was removed from the current tree and local Git history. The live test moved to `LiveDatabaseTests.cs`; it contains no database address, username, or password and does not read the application's production connection settings.

xUnit skips the live test unless both conditions are met:

- `QCHA_RUN_LIVE_DB_TESTS` is exactly `1`.
- `QCHA_TEST_MYSQL_CONNECTION_STRING` contains a dedicated non-empty test-database connection string.

The test queries only the `PlayerData` table, permits an empty table, and cancels queries after 15 seconds. The test database must use the schema expected by the project. Use a read-only test account with only the required `SELECT` permission; never use production. Supply the connection string through a local environment or CI secret. Do not put it in the repository, shell history, or test output.

Run the normal suite from the `bot` directory:

```powershell
dotnet test tests/Server_Qcha.Bot.Tests/Server_Qcha.Bot.Tests.csproj -c Release
```

To opt into the live test, inject the test connection string through a secure channel first:

```powershell
$env:QCHA_RUN_LIVE_DB_TESTS = '1'
try {
    dotnet test tests/Server_Qcha.Bot.Tests/Server_Qcha.Bot.Tests.csproj -c Release --filter 'Category=LiveDatabase'
} finally {
    Remove-Item Env:QCHA_RUN_LIVE_DB_TESTS -ErrorAction SilentlyContinue
    Remove-Item Env:QCHA_TEST_MYSQL_CONNECTION_STRING -ErrorAction SilentlyContinue
}
```

## Actions for database administrators

Deleting a credential from source does not revoke a password that was already exposed. General response steps are to identify every `user@host` entry for the exposed username, rotate the password, update authorized clients through secret storage, and confirm the old password no longer works. If the same password was reused elsewhere, rotate it there too.

Restrict the MySQL port in cloud security groups and host firewalls to confirmed server egress addresses, a VPN, or private network sources. Remove public-anywhere rules and inspect IPv6 rules. Limit MySQL account Host fields to required sources. Create a separate read-only test account and verify that an allowed source can connect while an unlisted source is denied. Review database authentication/audit logs for unusual access and terminate suspicious sessions as needed.

A MySQL account is identified by both username and client Host. Verify the exact account entry before changing it. Consult the MySQL documentation on [account names and hosts](https://dev.mysql.com/doc/refman/8.4/en/user-names.html) and [password assignment](https://dev.mysql.com/doc/refman/8.4/en/assigning-passwords.html); exact steps depend on the deployed MySQL version.

## Reported server configuration

The server's `D:\mysql\my.ini` was backed up and configured under `[mysqld]` with:

```ini
bind-address=127.0.0.1
mysqlx-bind-address=127.0.0.1
```

After restarting MySQL, listener checks reported that ports 3306 and 33060 were bound only to `127.0.0.1`, without IPv6 or non-loopback listeners. Login and a `PlayerData` query using the existing account/password succeeded locally. A backup was reported at `D:\mysql\my.ini.before-local-only-20260927-001042.bak`.

External probing received no MySQL protocol data and timed out while reading. Because the external TCP connection was still accepted, this should not be described as the port being fully unreachable. No Windows `portproxy` entry was reported. Listener-address inspection was the primary check that this MySQL instance accepted only local connections.

Windows Firewall profiles were reported disabled, and neither the global firewall nor cloud security group was changed. At the user's request, the password was not rotated and the account's global administrator privileges were not changed. Local clients should connect through `127.0.0.1`; this MySQL instance no longer accepts remote database connections.

## Git history

Commit `8003a02` introduced the test containing credentials. Local branches and tags were reported rewritten and old reflog/object data removed, while preserving four local commits. At the time this status was recorded, remote-history cleanup for `main`, `master`, `v1.4.0`, and `v2.0.0` had not been completed because the force-update operation was blocked by an approval review. After a history rewrite is pushed, collaborators must re-clone or follow the maintainer's migration instructions so the old commit is not reintroduced.

## Game assemblies and build output

Local branch and tag history was reported cleaned of `.dll`, `.exe`, `.pdb`, `.nupkg`, and build/cache directories such as `bin/`, `obj/`, `packages/`, and root `net6.0/`. Current-index and local-history scans found no matching files, and `.gitignore` covers these outputs.

The EXILED plugin restores `ExMod.Exiled` 9.5.0 from NuGet; the LabAPI API restores `Northwood.LabAPI` 1.1.7 from Northwood's NuGet feed. Both plugin projects also need some game-provided references, which are not stored in this repository. Point `SCPSL_REFERENCES` to `SCPSL_Data/Managed` in a legally installed SCP:SL Dedicated Server. See [`Northwood.LabAPI`](https://www.nuget.org/packages/Northwood.LabAPI) and [`ExMod.Exiled`](https://www.nuget.org/packages/ExMod.Exiled/9.5.0).
