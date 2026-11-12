using System.Text.Json;

namespace GraficaCaixa;

public sealed record ServerConfiguration
{
    public string DataDirectory { get; init; } = "";
    public string IdentityDirectory { get; init; } = "";
    public string LogsDirectory { get; init; } = "";
    public int Port { get; init; } = 5074;
    public WebApiOptions WebApi { get; init; } = new();
    // Leituras sem esperar as gravações (2026.10.21.3). false volta ao comportamento anterior sem reinstalar.
    public bool LeiturasSemTrava { get; init; } = true;
}

public sealed record DataDirectoryResolution(string DataDirectory, bool IsExplicit, string ConfigurationPath);
public sealed record ServerRuntimePaths(string ErpRoot, string DataDirectory, string IdentityDirectory, string LogsDirectory, int Port, string ConfigurationPath);

public static class DataDirectoryResolver
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string DefaultConfigurationPath
    {
        get
        {
            var programDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
            var erpRoot = Directory.GetParent(programDirectory)?.FullName ?? programDirectory;
            return Path.Combine(erpRoot, "Configuracao", "servidor.json");
        }
    }

    public static DataDirectoryResolution? ResolveForMode(int mode, string individualDirectory, string legacyPrincipalDirectory, string? configurationPath = null)
    {
        if (mode == 2) return null;
        if (mode == 1) return ResolveForPrincipal(legacyPrincipalDirectory, configurationPath);
        if (string.IsNullOrWhiteSpace(individualDirectory)) throw new InvalidOperationException("Escolha a pasta dos dados para o uso individual.");
        return new DataDirectoryResolution(Path.GetFullPath(individualDirectory), false, configurationPath ?? DefaultConfigurationPath);
    }

    public static DataDirectoryResolution ResolveForPrincipal(string legacyDirectory, string? configurationPath = null)
    {
        var path = Path.GetFullPath(configurationPath ?? DefaultConfigurationPath);
        if (!File.Exists(path)) return new DataDirectoryResolution(Path.GetFullPath(legacyDirectory), false, path);
        var configuration = LoadRequired(path);
        return new DataDirectoryResolution(ValidateExplicitDirectory(configuration.DataDirectory), true, path);
    }

    public static ServerRuntimePaths ResolveForServer(string? configurationPath = null)
    {
        var path = Path.GetFullPath(configurationPath ?? DefaultConfigurationPath);
        if (!File.Exists(path))
            throw new InvalidOperationException($"A configuração persistente do servidor não foi encontrada:\n{path}\n\nO serviço não iniciará com caminhos implícitos.");
        var configuration = LoadRequired(path);
        var configurationDirectory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Caminho de configuração inválido.");
        var erpRoot = Directory.GetParent(configurationDirectory)?.FullName
            ?? throw new InvalidOperationException("Não foi possível determinar a raiz persistente do ERP a partir de servidor.json.");
        var identity = ResolveLocalDirectory(configuration.IdentityDirectory, Path.Combine(erpRoot, "Servidor", "Identidade"), "IdentityDirectory");
        var logs = ResolveLocalDirectory(configuration.LogsDirectory, Path.Combine(erpRoot, "Servidor", "Logs"), "LogsDirectory");
        if (configuration.Port is < 1 or > 65535) throw new InvalidOperationException("Port deve estar entre 1 e 65535 em servidor.json.");
        return new ServerRuntimePaths(erpRoot, ValidateExplicitDirectory(configuration.DataDirectory), identity, logs, configuration.Port, path);
    }

    public static string GetDisplayPath(string legacyDirectory, string? configurationPath = null)
    {
        var path = Path.GetFullPath(configurationPath ?? DefaultConfigurationPath);
        if (!File.Exists(path)) return Path.GetFullPath(legacyDirectory);
        try
        {
            var configuration = JsonSerializer.Deserialize<ServerConfiguration>(File.ReadAllText(path), Json);
            return string.IsNullOrWhiteSpace(configuration?.DataDirectory) ? Path.GetFullPath(legacyDirectory) : configuration.DataDirectory.Trim();
        }
        catch { return "Configuração do servidor inválida — corrija servidor.json"; }
    }

    public static string ValidateExplicitDirectory(string? configuredDirectory)
    {
        if (string.IsNullOrWhiteSpace(configuredDirectory))
            throw new InvalidOperationException("DataDirectory não foi informado em servidor.json.");
        if (!Path.IsPathFullyQualified(configuredDirectory))
            throw new InvalidOperationException("DataDirectory deve ser um caminho absoluto local.");

        var full = Path.GetFullPath(configuredDirectory);
        if (full.StartsWith("\\\\", StringComparison.Ordinal))
            throw new InvalidOperationException("DataDirectory não pode ser um caminho UNC. O banco SQLite principal deve permanecer em um disco local do PC1.");
        var programDirectory=Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory))+Path.DirectorySeparatorChar;
        if((full+Path.DirectorySeparatorChar).StartsWith(programDirectory,StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("DataDirectory não pode ficar dentro da pasta Programa, pois essa pasta é substituída durante atualizações.");
        if (!Directory.Exists(full))
            throw MissingDatabase(full, "A pasta configurada não existe ou está indisponível.");

        var root = Path.GetPathRoot(full) ?? throw new InvalidOperationException("Não foi possível identificar a unidade de DataDirectory.");
        var drive = new DriveInfo(root);
        if (!drive.IsReady) throw MissingDatabase(full, "A unidade configurada não está disponível.");
        if (drive.DriveType is DriveType.Network or DriveType.NoRootDirectory or DriveType.CDRom)
            throw new InvalidOperationException($"A unidade {root} não é um armazenamento local compatível para o banco SQLite principal.");
        if (!drive.DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase) && !drive.DriveFormat.Equals("ReFS", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"O filesystem {drive.DriveFormat} da unidade {root} não é aceito para o banco central. Use NTFS ou ReFS local.");

        var database = Path.Combine(full, "caixa_dados.db");
        if (!File.Exists(database)) throw MissingDatabase(full, "O arquivo caixa_dados.db esperado não foi encontrado.");

        try
        {
            using (File.Open(database, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) { }
            var probe = Path.Combine(full, $".grafica-caixa-write-{Guid.NewGuid():N}.tmp");
            try { using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { } }
            finally { if (File.Exists(probe)) File.Delete(probe); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw MissingDatabase(full, "A pasta configurada não possui acesso de leitura e gravação.", ex);
        }
        return full;
    }

    public static void Save(string configurationPath, ServerConfiguration configuration)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(configurationPath)) ?? throw new InvalidOperationException("Caminho de configuração inválido.");
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $"servidor-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(configuration, Json));
            File.Move(temp, configurationPath, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    static InvalidOperationException MissingDatabase(string directory, string reason, Exception? inner = null) =>
        new($"Não foi possível localizar a base central configurada em:\n{directory}\n\n{reason}\n\nO ERP não criará uma nova base automaticamente para evitar perda ou separação de dados.", inner);

    static ServerConfiguration LoadRequired(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<ServerConfiguration>(File.ReadAllText(path), Json)
                ?? throw new InvalidDataException("O arquivo está vazio.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            throw new InvalidOperationException($"A configuração do banco central é inválida:\n{path}\n\n{ex.Message}\n\nO ERP não usará o caminho legado automaticamente porque existe uma configuração explícita.", ex);
        }
    }

    static string ResolveLocalDirectory(string configured, string fallback, string settingName)
    {
        var candidate = string.IsNullOrWhiteSpace(configured) ? fallback : configured.Trim();
        if (!Path.IsPathFullyQualified(candidate) || candidate.StartsWith("\\\\", StringComparison.Ordinal))
            throw new InvalidOperationException($"{settingName} deve ser um caminho absoluto em disco local.");
        var full = Path.GetFullPath(candidate);
        var root = Path.GetPathRoot(full) ?? throw new InvalidOperationException($"Não foi possível identificar a unidade de {settingName}.");
        var drive = new DriveInfo(root);
        if (!drive.IsReady || drive.DriveType is DriveType.Network or DriveType.NoRootDirectory or DriveType.CDRom)
            throw new InvalidOperationException($"{settingName} deve permanecer em armazenamento local disponível.");
        return full;
    }
}
