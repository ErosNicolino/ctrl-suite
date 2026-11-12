using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace GraficaCaixa;

public sealed record ServerIdentity(string Key, X509Certificate2 Certificate, string CertificatePin, string CertificatePath, string KeyPath) : IDisposable
{
    public void Dispose() => Certificate.Dispose();
}

public static class ServerIdentityStore
{
    static readonly object Gate = new();
    public const string CertificateFileName = "servidor.pfx";
    public const string KeyFileName = "chave-servidor.txt";

    public static ServerIdentity LoadOrCreate(string identityDirectory, bool allowCreate, string? legacyIdentityDirectory = null)
    {
        lock (Gate)
        {
            var target = Path.GetFullPath(identityDirectory);
            if (!HasCompleteIdentity(target) && !string.IsNullOrWhiteSpace(legacyIdentityDirectory) && HasCompleteIdentity(legacyIdentityDirectory))
                MigrateExistingIdentity(Path.GetFullPath(legacyIdentityDirectory), target);

            if (!HasCompleteIdentity(target))
            {
                var partial = File.Exists(Path.Combine(target, CertificateFileName)) || File.Exists(Path.Combine(target, KeyFileName));
                if (partial) throw new InvalidOperationException($"A identidade persistente está incompleta em:\n{target}\n\nNenhuma nova identidade será criada sobre arquivos existentes.");
                if (!allowCreate) throw new InvalidOperationException($"A identidade persistente do servidor não foi encontrada em:\n{target}\n\nRestaure servidor.pfx e chave-servidor.txt antes de iniciar o serviço.");
                CreateIdentity(target);
            }
            return LoadExisting(target);
        }
    }

    public static ServerIdentity LoadExisting(string identityDirectory)
    {
        var directory = Path.GetFullPath(identityDirectory);
        var certificatePath = Path.Combine(directory, CertificateFileName);
        var keyPath = Path.Combine(directory, KeyFileName);
        if (!File.Exists(certificatePath) || !File.Exists(keyPath))
            throw new InvalidOperationException($"A identidade persistente está ausente ou incompleta em:\n{directory}");

        string key;
        try { key = File.ReadAllText(keyPath).Trim(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new InvalidOperationException("Não foi possível ler a chave persistente do servidor.", ex); }
        try
        {
            if (Convert.FromBase64String(key).Length < 32) throw new FormatException();
        }
        catch (FormatException ex) { throw new InvalidOperationException("A chave persistente do servidor é inválida.", ex); }

        X509Certificate2 certificate;
        try
        {
            certificate = X509CertificateLoader.LoadPkcs12FromFile(certificatePath, null,
                X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        { throw new InvalidOperationException("O certificado persistente do servidor não pôde ser carregado.", ex); }
        if (!certificate.HasPrivateKey || certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow)
        {
            certificate.Dispose();
            throw new InvalidOperationException("O certificado persistente do servidor não possui chave privada válida ou está expirado.");
        }
        return new ServerIdentity(key, certificate, certificate.GetCertHashString(HashAlgorithmName.SHA256), certificatePath, keyPath);
    }

    public static bool HasCompleteIdentity(string identityDirectory) =>
        File.Exists(Path.Combine(identityDirectory, CertificateFileName)) && File.Exists(Path.Combine(identityDirectory, KeyFileName));

    static void CreateIdentity(string directory)
    {
        Directory.CreateDirectory(directory);
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Caixa da Grafica", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost"); san.AddDnsName(Environment.MachineName); san.AddIpAddress(System.Net.IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());
        using var created = request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(5));
        WriteAtomic(Path.Combine(directory, CertificateFileName), created.Export(X509ContentType.Pfx));
        WriteAtomic(Path.Combine(directory, KeyFileName), System.Text.Encoding.UTF8.GetBytes(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));
    }

    static void MigrateExistingIdentity(string source, string destination)
    {
        using var before = LoadExisting(source);
        Directory.CreateDirectory(destination);
        var destinationCertificate = Path.Combine(destination, CertificateFileName);
        var destinationKey = Path.Combine(destination, KeyFileName);
        if (File.Exists(destinationCertificate) || File.Exists(destinationKey))
            throw new InvalidOperationException("A pasta de identidade persistente contém arquivos parciais. A migração automática foi cancelada.");
        WriteAtomic(destinationCertificate, File.ReadAllBytes(before.CertificatePath));
        WriteAtomic(destinationKey, File.ReadAllBytes(before.KeyPath));
        using var after = LoadExisting(destination);
        if (!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(before.Key), System.Text.Encoding.UTF8.GetBytes(after.Key)) ||
            !before.CertificatePin.Equals(after.CertificatePin, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A identidade copiada não corresponde à identidade original.");
    }

    static void WriteAtomic(string path, byte[] content)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temp, content); File.Move(temp, path, false); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
