using GraficaCaixa.Wpf;

public static class DocumentRootResolverTests
{
    const string Canonical=@"\\pc1\Grafica CA CP\ERP";
    const string Tail=@"\\100.90.80.70\Grafica CA CP\ERP";
    const string NewRoot=@"\\PC1\ERP";

    public static void Run()
    {
        LanPreserved();TailscaleFallback();Hostname();SharePreserved();Subfolders();RelativePhoto();LocalPath();InvalidRoot();MissingEndpoint();Duplicate();EquivalentLogo();OutsideLogo();PreferredShare();PreferredFailure();PreferredTailscale();
        Console.WriteLine("DOCUMENT ROOT TESTS PASSED: 15/15");
    }

    static void LanPreserved()
    {
        var calls=new List<string>();var resolver=new DocumentRootResolver("https://100.90.80.70:5074");
        var result=resolver.Resolve(Canonical,path=>{calls.Add(path);return null;});resolver.Resolve(Canonical,path=>{calls.Add(path);return null;});
        Check(result==Canonical&&calls.SequenceEqual([Canonical]),"A: raiz LAN preservada e mantida em cache");
    }
    static void TailscaleFallback()
    {
        var resolver=new DocumentRootResolver("https://100.90.80.70:5074");
        var result=resolver.Resolve(Canonical,path=>path==Tail?null:new DirectoryNotFoundException());
        Check(result==Tail,"B: fallback usa host Tailscale");
    }
    static void Hostname()
    {
        var expected=@"\\pc1-grafica.exemplo.ts.net\Grafica CA CP\ERP";var resolver=new DocumentRootResolver("https://pc1-grafica.exemplo.ts.net:5074");
        Check(resolver.Resolve(Canonical,path=>path==expected?null:new IOException())==expected,"C: hostname Tailscale suportado");
    }
    static void SharePreserved()
    {
        Check(DocumentRootResolver.ReplaceUncHost(Canonical,"100.90.80.70")==Tail,"D: somente o host UNC é substituído");
    }
    static void Subfolders()
    {
        var expected=new[]{"Fechamentos","Impressões",Path.Combine("Produtos","Imagens")}.Select(x=>Path.Combine(Tail,x));
        Check(expected.SequenceEqual(new[]{Path.Combine(Tail,"Fechamentos"),Path.Combine(Tail,"Impressões"),Path.Combine(Tail,"Produtos","Imagens")}),"E: subpastas preservadas");
    }
    static void RelativePhoto()
    {
        const string relative="Produtos/Imagens/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa_bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.jpg";
        Check(!Path.IsPathRooted(relative)&&Path.Combine(Tail,relative.Replace('/',Path.DirectorySeparatorChar)).StartsWith(Tail,StringComparison.OrdinalIgnoreCase),"F: referência de foto permanece relativa");
    }
    static void LocalPath()
    {
        const string local=@"G:\Grafica CA CP\ERP";var resolver=new DocumentRootResolver("https://100.90.80.70:5074");
        Check(resolver.Resolve(local,path=>path==local?null:new IOException())==local&&DocumentRootResolver.ReplaceUncHost(local,"100.90.80.70")==null,"G: caminho local não é transformado");
    }
    static void InvalidRoot()
    {
        Reject(()=>new DocumentRootResolver(null).Resolve("relativo",_=>null),"caminho completo","H: DocumentsRoot inválido falha de forma controlada");
    }
    static void MissingEndpoint()
    {
        Reject(()=>new DocumentRootResolver(null).Resolve(Canonical,_=>new DirectoryNotFoundException()),"não foi encontrado","I: ausência de endpoint alternativo falha de forma controlada");
    }
    static void Duplicate()
    {
        var calls=0;var resolver=new DocumentRootResolver("https://pc1:5074");
        try{resolver.Resolve(Canonical,_=>{calls++;return new IOException();});throw new Exception("FALHOU: J");}
        catch(InvalidOperationException ex)when(ex.Message.Contains("indisponível",StringComparison.OrdinalIgnoreCase)&&calls==1){Console.WriteLine("OK: J: caminho alternativo duplicado não é testado");}
    }
    static void EquivalentLogo()
    {
        var alternateLogo=Path.Combine(Tail,"Imagens","logo.png");var canonicalLogo=Path.Combine(Canonical,"Imagens","logo.png");
        Check(DocumentRootResolver.IsWithinEquivalentRoot(alternateLogo,Canonical,Tail)&&DocumentRootResolver.MapToCanonical(alternateLogo,Canonical,Tail)==canonicalLogo,"K: logotipo equivalente é validado e canonizado");
    }
    static void OutsideLogo()
    {
        Check(!DocumentRootResolver.IsWithinEquivalentRoot(@"\\100.90.80.70\Outro\logo.png",Canonical,Tail),"L: caminho fora da raiz continua rejeitado");
    }
    static void PreferredShare()
    {
        var calls=new List<string>();var resolver=new DocumentRootResolver("https://pc1:5074",NewRoot);
        var result=resolver.Resolve(Canonical,path=>{calls.Add(path);return null;});
        Check(result==NewRoot&&calls.SequenceEqual([NewRoot])&&
              DocumentRootResolver.MapToEffective(Path.Combine(Canonical,"Fechamentos","novo.pdf"),Canonical,result)==Path.Combine(NewRoot,"Fechamentos","novo.pdf"),
              "M: share novo prevalece sobre o antigo sem alterar o banco");
    }
    static void PreferredFailure()
    {
        var calls=new List<string>();var resolver=new DocumentRootResolver(null,NewRoot);
        Reject(()=>resolver.Resolve(Canonical,path=>{calls.Add(path);return new DirectoryNotFoundException();}),"não foi encontrado","N: falha do share novo não volta ao antigo");
        Check(calls.SequenceEqual([NewRoot]),"N2: share antigo não foi sondado");
    }
    static void PreferredTailscale()
    {
        var alternate=@"\\100.90.80.70\ERP";var resolver=new DocumentRootResolver("https://100.90.80.70:5074",NewRoot);
        Check(resolver.Resolve(Canonical,path=>path==alternate?null:new DirectoryNotFoundException())==alternate,"O: Tailscale preserva o novo nome do share");
    }

    static void Check(bool condition,string name){if(!condition)throw new Exception("FALHOU: "+name);Console.WriteLine("OK: "+name);}
    static void Reject(Action action,string message,string name){try{action();throw new Exception("FALHOU: "+name);}catch(InvalidOperationException ex)when(ex.Message.Contains(message,StringComparison.OrdinalIgnoreCase)){Console.WriteLine("OK: "+name);}}
}
