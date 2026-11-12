namespace GraficaCaixa.Wpf;

public sealed class DocumentRootResolver
{
    readonly object sync=new();
    readonly string? activeHost;
    readonly string? preferredRoot;
    string? cachedCanonical;
    string? cachedEffective;

    public DocumentRootResolver(string? activeEndpoint,string? preferredRoot=null)
    {
        if(Uri.TryCreate(activeEndpoint,UriKind.Absolute,out var endpoint)&&endpoint.Scheme==Uri.UriSchemeHttps&&!string.IsNullOrWhiteSpace(endpoint.Host))activeHost=endpoint.Host;
        if(!string.IsNullOrWhiteSpace(preferredRoot))
        {
            var normalized=NormalizeRoot(preferredRoot);
            if(!normalized.StartsWith(@"\\",StringComparison.Ordinal))throw new InvalidOperationException("DocumentsRoot da configuração deve ser uma pasta UNC.");
            this.preferredRoot=normalized;
        }
    }

    public string? ActiveHost=>activeHost;

    public string Resolve(string canonicalRoot,Func<string,Exception?> accessProbe)
    {
        var canonical=NormalizeRoot(canonicalRoot);
        lock(sync)if(string.Equals(cachedCanonical,canonical,StringComparison.OrdinalIgnoreCase)&&cachedEffective!=null)return cachedEffective;

        // A configuração do novo ERP prevalece mesmo enquanto o share antigo existir.
        // Se a raiz nova falhar, não se deve gravar silenciosamente em G:.
        if(preferredRoot!=null)
        {
            var preferredError=accessProbe(preferredRoot);
            if(preferredError==null)return Cache(canonical,preferredRoot);
            var preferredAlternate=activeHost==null?null:ReplaceUncHost(preferredRoot,activeHost);
            if(preferredAlternate!=null&&!preferredAlternate.Equals(preferredRoot,StringComparison.OrdinalIgnoreCase))
            {
                var preferredAlternateError=accessProbe(preferredAlternate);
                if(preferredAlternateError==null)return Cache(canonical,preferredAlternate);
                throw Unavailable(preferredRoot,preferredAlternate,preferredAlternateError);
            }
            throw Unavailable(preferredRoot,null,preferredError);
        }

        var originalError=accessProbe(canonical);
        if(originalError==null)return Cache(canonical,canonical);

        string? alternate=null;Exception? alternateError=null;
        if(activeHost!=null)alternate=ReplaceUncHost(canonical,activeHost);
        if(alternate!=null&&!alternate.Equals(canonical,StringComparison.OrdinalIgnoreCase))
        {
            alternateError=accessProbe(alternate);
            if(alternateError==null)return Cache(canonical,alternate);
        }
        throw Unavailable(canonical,alternate,alternateError??originalError);
    }

    public string EffectiveOrCanonical(string canonicalRoot)
    {
        var canonical=NormalizeRoot(canonicalRoot);
        lock(sync)return string.Equals(cachedCanonical,canonical,StringComparison.OrdinalIgnoreCase)&&cachedEffective!=null?cachedEffective:preferredRoot??canonical;
    }

    public void Invalidate()
    {
        lock(sync){cachedCanonical=null;cachedEffective=null;}
    }

    string Cache(string canonical,string effective)
    {
        lock(sync){cachedCanonical=canonical;cachedEffective=effective;return effective;}
    }

    public static string NormalizeRoot(string root)
    {
        if(string.IsNullOrWhiteSpace(root)||!Path.IsPathFullyQualified(root))throw new InvalidOperationException("A pasta de documentos configurada não é um caminho completo válido.");
        var full=Path.GetFullPath(root);var pathRoot=Path.GetPathRoot(full);
        return string.Equals(full,pathRoot,StringComparison.OrdinalIgnoreCase)?full:full.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
    }

    public static string? ReplaceUncHost(string root,string host)
    {
        if(string.IsNullOrWhiteSpace(host)||!root.StartsWith(@"\\",StringComparison.Ordinal))return null;
        var remainder=root[2..];var separator=remainder.IndexOfAny(['\\','/']);
        if(separator<=0)return null;
        return @"\\"+host+remainder[separator..].Replace('/',Path.DirectorySeparatorChar);
    }

    public static bool IsWithinRoot(string path,string root)
    {
        try
        {
            var fullPath=Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
            var fullRoot=NormalizeRoot(root);
            var prefix=fullRoot.EndsWith(Path.DirectorySeparatorChar)?fullRoot:fullRoot+Path.DirectorySeparatorChar;
            return fullPath.Equals(fullRoot.TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)||fullPath.StartsWith(prefix,StringComparison.OrdinalIgnoreCase);
        }
        catch(Exception ex)when(ex is ArgumentException or NotSupportedException or PathTooLongException){return false;}
    }

    public static bool IsWithinEquivalentRoot(string path,string canonicalRoot,string effectiveRoot)
        => IsWithinRoot(path,canonicalRoot)||IsWithinRoot(path,effectiveRoot);

    public static string MapToEffective(string path,string canonicalRoot,string effectiveRoot)
        => Rebase(path,canonicalRoot,effectiveRoot);

    public static string MapToCanonical(string path,string canonicalRoot,string effectiveRoot)
        => IsWithinRoot(path,canonicalRoot)?Path.GetFullPath(path):Rebase(path,effectiveRoot,canonicalRoot);

    static string Rebase(string path,string fromRoot,string toRoot)
    {
        var full=Path.GetFullPath(path);var source=NormalizeRoot(fromRoot);var target=NormalizeRoot(toRoot);
        if(!IsWithinRoot(full,source))return full;
        var suffix=full[source.Length..].TrimStart(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        return suffix.Length==0?target:Path.Combine(target,suffix);
    }

    static InvalidOperationException Unavailable(string canonical,string? alternate,Exception cause)
    {
        var reason=cause switch
        {
            UnauthorizedAccessException=>"O Windows negou o acesso à pasta compartilhada.",
            DirectoryNotFoundException or FileNotFoundException=>"O servidor, compartilhamento ou pasta não foi encontrado.",
            _=>"O compartilhamento está indisponível."
        };
        var remote=alternate==null?"":"\nA conexão com os dados está ativa, mas também não foi possível acessar a pasta pelo host da sessão.";
        return new InvalidOperationException($"{reason}{remote}\n\nVerifique se o Tailscale está conectado, se o PC1 está ligado, se o compartilhamento está disponível e se este computador possui permissão de acesso.\n\nPasta configurada: {canonical}",cause);
    }
}
