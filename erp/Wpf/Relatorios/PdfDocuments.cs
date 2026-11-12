using System.Diagnostics;
using System.Windows;
namespace GraficaCaixa.Wpf;
public static class PdfDocuments
{
    public static ProcessStartInfo OpenInfo(string path)=>new(Path.GetFullPath(path)){UseShellExecute=true,Verb="open"};
    public static void Open(Window owner,string path)
    {
        try{Process.Start(OpenInfo(path));}
        catch(Exception ex){AlertWindow.Info(owner,"PDF salvo; não foi possível abrir",$"O documento foi salvo em:\n{path}\n\nAbra-o no seu leitor de PDF. Nenhuma impressão foi enviada automaticamente.\n{ex.Message}");}
    }
}
