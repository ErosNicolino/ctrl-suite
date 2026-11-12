namespace GraficaCaixa.Wpf;

// Pasta das configurações deste computador (modo, pasta do banco, código de conexão, aparência).
// Com a variável GRAFICA_CAIXA_PERFIL definida (atalho "Abrir_Teste"), usa uma pasta separada:
// a versão de teste nunca lê nem altera a configuração real e não se conecta ao banco oficial por engano.
public static class AppProfile
{
    static readonly string? custom=Environment.GetEnvironmentVariable("GRAFICA_CAIXA_PERFIL");
    public static bool IsTest=>!string.IsNullOrWhiteSpace(custom);
    public static string Folder=>IsTest?custom!:Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GraficaCaixa");
}
