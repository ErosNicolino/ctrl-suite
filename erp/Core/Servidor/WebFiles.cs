using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Win32.SafeHandles;

namespace GraficaCaixa;

public static class WebFiles
{
    public static string Category(string type) => type switch
    {
        "pedido" => "Impressões", "recibo" => "Recibos", "orcamento" => "Orcamentos",
        "fechamento" => "Fechamentos", "fiscal" => "DocumentosFiscais", _ => ""
    };
    public static bool ValidRelative(string value, string category)
    {
        if (category.Length == 0 || string.IsNullOrWhiteSpace(value) || value.Length > 240 ||
            value.Contains('\\') || value.Contains(':') || value.Contains('%') || Path.IsPathRooted(value)) return false;
        var parts = value.Split('/');
        return parts.Length >= 2 && parts[0] == category && parts.All(x => x.Length > 0 && x is not "." and not ".." &&
            x == x.Trim() && !x.EndsWith('.') && !Regex.IsMatch(x.Split('.')[0], "^(CON|PRN|AUX|NUL|COM[0-9¹²³]|LPT[0-9¹²³])$", RegexOptions.IgnoreCase) &&
            !x.Any(c => c < 32 || Path.GetInvalidFileNameChars().Contains(c)));
    }
    static string Root(WebApiOptions options)
    {
        if (options.DocumentsDirectory.Length == 0) throw new WebRequestException(404, "Documentos não configurados.");
        return Path.GetFullPath(options.DocumentsDirectory);
    }
    static string Resolve(WebApiOptions options, string relative, string category, string? rootDirectory = null)
    {
        if (!ValidRelative(relative, category)) throw new WebRequestException(403, "Referência de arquivo recusada.");
        var root = rootDirectory == null ? Root(options) : rootDirectory.Length == 0 ? throw new WebRequestException(404, "Pasta de fotos não configurada.") : Path.GetFullPath(rootDirectory); var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new WebRequestException(403, "Referência de arquivo recusada.");
        return path;
    }
    // Foto n do produto (0 = Principal). Só nomes do contrato ProductPhotoNames, dentro de Produtos/Imagens.
    public static string? Image(SqliteConnection db, Guid id, WebApiOptions options, int index = 0)
    {
        var product = WebQueries.StoredProduct(db, id);
        var photos = product == null ? new List<string>() : ProductPhotoNames.All(product);
        if (index < 0 || index >= photos.Count) return null;
        // Nome antigo (<id>_<guid>.jpg) continua preso ao próprio produto; o nome legível novo não carrega dono.
        if (!ProductPhotoNames.Valid(photos[index]) || Regex.IsMatch(photos[index], "^Produtos/Imagens/[a-f0-9]{32}_") && !photos[index].StartsWith($"Produtos/Imagens/{id:N}_", StringComparison.Ordinal))
            throw new WebRequestException(403, "Referência da foto recusada.");
        return Resolve(options, photos[index], "Produtos", options.PhotosRoot);
    }
    // Foto enviada pelo computador (já reduzida para JPG): grava na pasta de fotos do servidor com nome legível e livre
    // (descricao-1.jpg, -2…), sem sobrescrever. Assim funciona também no notebook, que não alcança o compartilhamento.
    public static string SaveProductPhoto(string documentsDirectory, string description, byte[] jpeg)
    {
        if (jpeg.Length < 100 || jpeg.Length > 8 * 1024 * 1024 || jpeg[0] != 0xFF || jpeg[1] != 0xD8 || jpeg[2] != 0xFF || jpeg[^2] != 0xFF || jpeg[^1] != 0xD9)
            throw new WebRequestException(422, "Foto inválida. Envie uma imagem JPG de até 8 MB.");
        if (documentsDirectory.Length == 0) throw new WebRequestException(404, "Pasta de arquivos do servidor não configurada.");
        var folder = Path.Combine(Path.GetFullPath(documentsDirectory), "Produtos", "Imagens"); Directory.CreateDirectory(folder);
        // Mesma foto já na pasta (escolhida de Produtos\Imagens para outro produto): reaproveita o arquivo em vez de criar -2, -3.
        foreach (var existing in Directory.EnumerateFiles(folder, "*.jpg"))
        {
            var relative = $"{ProductPhotoNames.Folder}/{Path.GetFileName(existing)}";
            try { if (ProductPhotoNames.Valid(relative) && new FileInfo(existing).Length == jpeg.Length && File.ReadAllBytes(existing).AsSpan().SequenceEqual(jpeg)) return relative; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        var slug = ProductPhotoNames.Slug(description);
        for (var n = 1; n < 10000; n++)
        {
            var relative = $"{ProductPhotoNames.Folder}/{slug}-{n}.jpg";
            try { using var file = new FileStream(Path.Combine(folder, $"{slug}-{n}.jpg"), FileMode.CreateNew, FileAccess.Write, FileShare.None); file.Write(jpeg); file.Flush(true); return relative; }
            catch (IOException) when (File.Exists(Path.Combine(folder, $"{slug}-{n}.jpg"))) { }
        }
        throw new WebRequestException(409, "Não foi possível escolher um nome para a foto.");
    }
    // Apaga fotos que nenhum produto usa mais (trocadas, removidas, produto excluído ou gravação que falhou).
    public static int DeleteUnusedPhotos(string documentsDirectory, IEnumerable<string> candidates, IEnumerable<Product> products)
    {
        if (documentsDirectory.Length == 0) return 0;
        var used = products.SelectMany(ProductPhotoNames.All).ToHashSet(StringComparer.OrdinalIgnoreCase); var root = Path.GetFullPath(documentsDirectory); var deleted = 0;
        foreach (var relative in candidates.Where(ProductPhotoNames.Valid).Distinct(StringComparer.OrdinalIgnoreCase).Where(x => !used.Contains(x)))
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            try { if (File.Exists(path)) { File.Delete(path); deleted++; } } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return deleted;
    }
    // Até 2026.10.28.3 as fotos enviadas pelo servidor iam para a pasta de documentos do celular (ArquivosWeb8A); agora ficam em
    // ERP\Produtos\Imagens. Move as que faltam. Mesmo nome nas duas: iguais → fica a do destino; diferentes → fica a mais recente.
    // O original só sai depois de a cópia ser conferida byte a byte; nada muda no banco (o nome do arquivo é o mesmo).
    public static (int Moved, int Duplicates) MigrateProductPhotos(string fromRoot, string toRoot)
    {
        if (fromRoot.Length == 0 || toRoot.Length == 0) return (0, 0);
        var from = Path.Combine(Path.GetFullPath(fromRoot), "Produtos", "Imagens"); var to = Path.Combine(Path.GetFullPath(toRoot), "Produtos", "Imagens");
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(from)) return (0, 0);
        Directory.CreateDirectory(to); int moved = 0, duplicates = 0;
        foreach (var source in Directory.GetFiles(from))
        {
            try
            {
                var target = Path.Combine(to, Path.GetFileName(source));
                if (File.Exists(target))
                {
                    if (!SameContent(source, target) && File.GetLastWriteTimeUtc(source) > File.GetLastWriteTimeUtc(target))
                    {
                        var temp = target + ".migrando"; File.Copy(source, temp, true);
                        if (!SameContent(source, temp)) { File.Delete(temp); continue; }
                        File.Move(temp, target, true);
                    }
                    File.Delete(source); duplicates++; continue;
                }
                File.Copy(source, target);
                if (!SameContent(source, target)) { File.Delete(target); continue; }
                File.Delete(source); moved++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return (moved, duplicates);
    }
    static bool SameContent(string a, string b)
    {
        if (new FileInfo(a).Length != new FileInfo(b).Length) return false;
        using var sha = System.Security.Cryptography.SHA256.Create();
        byte[] Hash(string path) { using var stream = File.OpenRead(path); return sha.ComputeHash(stream); }
        return Hash(a).AsSpan().SequenceEqual(Hash(b));
    }
    static void Require(Dictionary<string, bool> p, params string[] keys)
    { if (keys.Any(k => !p.GetValueOrDefault(k))) throw new UnauthorizedAccessException(); }
    public static string? Document(SqliteConnection db, Dictionary<string, bool> p, UserIdentity actor, string type, Guid id, WebApiOptions options)
    {
        if (Category(type).Length == 0) throw new WebRequestException(400, "Tipo de documento inválido.");
        // Existing PDFs are opaque and may contain sensitive values. Conservative 8A policy:
        // only users allowed to see costs and private customer fields can download originals.
        Require(p, "costs.edit", "customers.edit");
        bool exists;
        switch (type)
        {
            case "pedido": case "orcamento":
                Require(p, "view.orders", "orders.value", "finance.receive");
                if (type == "orcamento") Require(p, "view.budget");
                exists = WebQueries.Exists(db, "scheduled_orders", id); break;
            case "recibo":
                Require(p, "finance.receipt");
                exists = WebQueries.Exists(db, "audit_trail", id);
                if (exists)
                {
                    using var cmd = WebQueries.Command(db, "SELECT entity_type FROM audit_trail WHERE id=@id", ("@id", id));
                    exists = cmd.ExecuteScalar()?.ToString() == "Recibo";
                }
                else { Require(p, "view.orders", "finance.receive", "orders.value"); exists = WebQueries.Exists(db, "scheduled_orders", id); }
                break;
            case "fechamento":
                Require(p, "view.closed");
                using (var cmd = WebQueries.Command(db, "SELECT 1 FROM cash_sessions WHERE id=@id AND closed_at IS NOT NULL", ("@id", id))) exists = cmd.ExecuteScalar() != null;
                break;
            default:
                Require(p, "system.owner", "view.orders");
                using (var cmd = WebQueries.Command(db, "SELECT owner_until FROM auth_sessions WHERE id=@id", ("@id", actor.SessionId)))
                    if (Convert.ToInt64(cmd.ExecuteScalar()) <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) throw new UnauthorizedAccessException();
                var prompt = WebQueries.Json<Prompt12State>(db, "prompt12_v1", "{}");
                var fiscal = prompt.FiscalDocuments.FirstOrDefault(x => x.Id == id);
                exists = fiscal != null && WebQueries.Exists(db, "scheduled_orders", fiscal.OrderId);
                break;
        }
        if (!exists) return null;
        var binding = options.Documents.FirstOrDefault(x => x.Type == type && x.EntityId == id);
        if (binding != null)
        {
            foreach (var orderId in binding.RelatedOrderIds)
            {
                Require(p, "view.orders", "orders.value", "finance.receive");
                if (!WebQueries.Exists(db, "scheduled_orders", orderId)) return null;
            }
            return Resolve(options, binding.RelativePath, Category(type));
        }
        // Persisted fiscal references can be absolute/UNC legacy paths: never follow them.
        // A trusted catalog explicitly maps such references to the local documents directory.
        if (type == "fiscal")
        {
            var fiscal = WebQueries.Json<Prompt12State>(db, "prompt12_v1", "{}").FiscalDocuments.Single(x => x.Id == id);
            if (string.IsNullOrWhiteSpace(fiscal.PdfPath)) return null;
            return Resolve(options, fiscal.PdfPath, Category(type));
        }
        if (type == "fechamento")
        {
            using var cmd = WebQueries.Command(db, "SELECT opened_at FROM cash_sessions WHERE id=@id", ("@id", id));
            var opened = DateTime.Parse(cmd.ExecuteScalar()!.ToString()!, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);
            var version = WebQueries.Json<List<CashClosureVersion>>(db, "cash_closure_versions_v1").Where(x => x.SessionId == id).OrderByDescending(x => x.Version).FirstOrDefault();
            var suffix = version?.Version > 1 ? $"_Retificado_v{version.Version}" : "_Original_v1";
            return Resolve(options, $"Fechamentos/Fechamento_{opened:dd_MM_yyyy_HH'h'mm}{suffix}.pdf", "Fechamentos");
        }
        return null;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint length, uint flags);
    static void CheckAncestors(string path)
    {
        for (string? current = path; current != null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new WebRequestException(403, "Referência de arquivo recusada.");
    }
    // Foto de produto reduzida para a tela (miniatura e foto do item em foco): ?w=… entre 64 e 800. Foto já menor vai como está.
    // Sem ?w (clientes antigos) continua a foto inteira.
    public static async Task<IResult> OpenAsync(string? path, string contentType, string downloadName, int? maxSide, CancellationToken cancellation)
    {
        var result = await OpenAsync(path, contentType, downloadName, cancellation);
        if (maxSide is not int side || result is not Microsoft.AspNetCore.Http.HttpResults.FileStreamHttpResult file) return result;
        await using var stream = file.FileStream; var bytes = new byte[stream.Length]; await stream.ReadExactlyAsync(bytes, cancellation);
        return Results.File(Shrink(bytes, Math.Clamp(side, 64, 800)), "image/jpeg", downloadName);
    }
    public static byte[] Shrink(byte[] jpeg, int maxSide)
    {
        var decoder = new System.Windows.Media.Imaging.JpegBitmapDecoder(new MemoryStream(jpeg), System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.None);
        var frame = decoder.Frames[0]; if (Math.Max(frame.PixelWidth, frame.PixelHeight) <= maxSide) return jpeg;
        var image = new System.Windows.Media.Imaging.BitmapImage(); image.BeginInit(); image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; image.StreamSource = new MemoryStream(jpeg);
        if (frame.PixelWidth >= frame.PixelHeight) image.DecodePixelWidth = maxSide; else image.DecodePixelHeight = maxSide;
        image.EndInit(); image.Freeze();
        var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder { QualityLevel = 82 }; encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
        using var output = new MemoryStream(); encoder.Save(output); return output.ToArray();
    }
    public static async Task<IResult> OpenAsync(string? path, string contentType, string downloadName, CancellationToken cancellation)
    {
        if (path == null) return Results.NotFound();
        FileStream? stream = null;
        try
        {
            CheckAncestors(path);
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            // Verify the resolved handle too, closing the junction/symlink replacement race.
            var final = new StringBuilder(32768); var length = GetFinalPathNameByHandle(stream.SafeFileHandle, final, (uint)final.Capacity, 0);
            if (length == 0 || length >= final.Capacity || !final.ToString().Equals(@"\\?\" + path, StringComparison.OrdinalIgnoreCase))
                throw new WebRequestException(403, "Referência de arquivo recusada.");
            var signature = new byte[5]; var required = contentType == "application/pdf" ? 5 : 3;
            if (stream.Length < required) throw new WebRequestException(422, "Conteúdo de arquivo inválido.");
            await stream.ReadExactlyAsync(signature.AsMemory(0, required), cancellation); stream.Position = 0;
            if (contentType == "application/pdf" ? Encoding.ASCII.GetString(signature) != "%PDF-" : signature[0] != 0xff || signature[1] != 0xd8 || signature[2] != 0xff)
                throw new WebRequestException(422, "Conteúdo de arquivo inválido.");
            return Results.File(stream, contentType, downloadName, enableRangeProcessing: false);
        }
        catch (FileNotFoundException) { stream?.Dispose(); return Results.NotFound(); }
        catch (DirectoryNotFoundException) { stream?.Dispose(); return Results.NotFound(); }
        catch { stream?.Dispose(); throw; }
    }
}
