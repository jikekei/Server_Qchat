namespace Server.Qcat.Configuration;

public sealed class MySqlBackupOptions
{
    public bool Enabled { get; set; } = true;
    public string DumpExecutable { get; set; } = "mysqldump";
}
