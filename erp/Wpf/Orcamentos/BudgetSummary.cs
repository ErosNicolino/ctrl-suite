namespace GraficaCaixa.Wpf;
public static class BudgetSummary
{
    public static string Format(string product,decimal price,decimal quantity=1,string? detail=null) => $"*Orçamento - {DateTime.Now:dd/MM/yyyy}*\n\n{quantity:00} - {product}\n"+(string.IsNullOrWhiteSpace(detail)?"":detail+"\n")+$"*Total: {price.ToString("C2",System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))}*";
}
