using System.IO;
namespace GraficaCaixa.Wpf;

// UX 2.0 (pacote 5): frase do dia do Início. Lista local (Assets\frases.txt, embutida no programa, sem internet),
// escolhida pela data: o mesmo dia mostra a mesma frase em todos os computadores e nenhuma se repete antes de a
// lista acabar (157 frases ⇒ mais de 30 dias sem repetir). Pode ser ocultada em Sistema › Aparência.
public static class DailyPhrases
{
    static IReadOnlyList<string>? all;
    public static IReadOnlyList<string> All=>all??=Load();
    static List<string> Load()
    {
        using var stream=typeof(DailyPhrases).Assembly.GetManifestResourceStream("GraficaCaixa.DailyPhrases.txt");
        if(stream==null)return [];
        using var reader=new StreamReader(stream);
        return reader.ReadToEnd().Split('\n').Select(x=>x.Trim()).Where(x=>x.Length>0&&!x.StartsWith('#')).ToList();
    }
    public static string For(DateTime date)
    {
        var list=All;if(list.Count==0)return "";
        var day=(int)(date.Date-new DateTime(2026,1,1)).TotalDays;
        return list[((day%list.Count)+list.Count)%list.Count];
    }
}
