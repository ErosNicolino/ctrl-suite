using System.Windows.Media;
namespace GraficaCaixa.Wpf;

// Cor da faixa lateral dos pedidos pela urgência (Início e Pedidos e produção):
// vermelho = atrasado / vence hoje / falta 1 dia (com fundo avermelhado e ⚠); laranja = prioridade ou atenção;
// azul = retirada; verde = entregue; cinza = aguarda arte / arquivado; verde-claro = no prazo.
public static class UrgencyColors
{
    static readonly Dictionary<string,Brush> cache=[];
    static Brush Hex(string color){if(!cache.TryGetValue(color,out var brush)){brush=(Brush)new BrushConverter().ConvertFromString(color)!;brush.Freeze();cache[color]=brush;}return brush;}
    public static bool IsUrgent(string urgency)=>urgency is "Atrasado" or "Para hoje" or "Vence hoje" or "Falta 1 dia";
    public static Brush Stripe(string urgency)=>urgency switch
    {
        "Atrasado" or "Para hoje" or "Vence hoje" or "Falta 1 dia"=>Hex("#D64545"),
        "IMEDIATO" or "Prioridade" or "Atenção"=>Hex("#E8891C"),
        "Retirada"=>Hex("#1E9BB0"),
        "Entregue"=>Hex("#3C9A6A"),
        "Aguarda arte" or "Arquivado"=>Hex("#A3ACB1"),
        _=>Hex("#7CC4B8"),
    };
    public static Brush Row(string urgency)=>ThemeManager.Get(urgency=="IMEDIATO"?"WarningSoft":IsUrgent(urgency)?"DangerSoft":"Surface");
    public static string Label(string urgency)=>urgency=="IMEDIATO"?"⚡ IMEDIATO":IsUrgent(urgency)?"⚠ "+urgency:urgency;
}
