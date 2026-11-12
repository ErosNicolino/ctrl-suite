using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace GraficaCaixa.Wpf;

// Aba "Resumo do dia" de Relatórios: o que entrou, o que atrasou e o que vence no próximo dia útil. Sem despesas nem lucro.
public sealed class DaySummaryView : ScrollViewer
{
    static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    public DataStore? Store { get; set; }
    readonly DatePicker date = new() { Width = 180, SelectedDate = DateTime.Today };
    readonly StackPanel body = new();
    DaySummary? current; bool loading;
    public DaySummaryView()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var root = new StackPanel { Margin = new Thickness(0, 16, 6, 0) };
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        bar.Children.Add(date);
        Loaded += (_, _) => { var copy = new Button { Content = "Copiar resumo", Style = (Style)FindResource("SoftButton"), Margin = new Thickness(10, 0, 0, 0) }; copy.Click += (_, _) => Copy(); if (bar.Children.Count == 1) bar.Children.Add(copy); };
        root.Children.Add(bar); root.Children.Add(body); Content = root;
        date.SelectedDateChanged += (_, _) => Load();
        IsVisibleChanged += (_, e) => { if (e.NewValue is true) Load(); };
    }
    async void Load()
    {
        if (Store == null || loading || !IsVisible) return; loading = true;
        if (body.Children.Count == 0) body.Children.Add(new TextBlock { Text = "Carregando resumo…" });
        try
        {
            var day = (date.SelectedDate ?? DateTime.Today).Date;
            current = Store.IsRemote ? await Store.QueryRemoteAsync<DaySummary>($"desktop/reports/day?date={day:yyyy-MM-dd}") : ReportIndicators.Day(Store.Load(), day, DateTime.Today);
            Render(current);
        }
        catch (Exception ex) { body.Children.Clear(); body.Children.Add(new EmptyState { IconName = "inbox", Title = "Resumo indisponível", Hint = ex.Message }); current = null; }
        finally { loading = false; }
    }
    void Render(DaySummary d)
    {
        body.Children.Clear();
        static string Money(decimal value) => value.ToString("C2", Pt);
        var money = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 10) };
        money.Children.Add(new KpiCard { Label = "Faturamento", Value = Money(d.Billed), Hint = "pedidos e vendas do dia", Tone = "info", Margin = new Thickness(0, 0, 10, 0) });
        money.Children.Add(new KpiCard { Label = "Recebido", Value = Money(d.Received), Hint = "entradas do caixa no dia", Tone = "ok", Margin = new Thickness(0, 0, 10, 0) });
        money.Children.Add(new KpiCard { Label = "Sangrias", Value = Money(d.Withdrawals), Hint = "dinheiro retirado da gaveta", Tone = "warn" });
        body.Children.Add(money);
        var work = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 12) };
        work.Children.Add(new KpiCard { Label = "Pedidos criados", Value = d.OrdersCreated.ToString(), Hint = "no dia escolhido", Tone = "info", Margin = new Thickness(0, 0, 10, 0) });
        work.Children.Add(new KpiCard { Label = "Ficaram prontos", Value = d.Ready.ToString(), Hint = "no dia escolhido", Tone = "ok", Margin = new Thickness(0, 0, 10, 0) });
        work.Children.Add(new KpiCard { Label = "Atrasados agora", Value = d.Overdue.ToString(), Hint = "situação de hoje", Tone = d.Overdue > 0 ? "danger" : "ok", Margin = new Thickness(0, 0, 10, 0) });
        work.Children.Add(new KpiCard { Label = "Vencem no próximo dia útil", Value = d.DueNext.ToString(), Hint = "situação de hoje", Tone = "warn" });
        body.Children.Add(work);
        var card = new Border { Style = (Style)FindResource("Card") }; var list = new StackPanel(); card.Child = list;
        list.Children.Add(new TextBlock { Text = "Recebido por forma de pagamento", Style = (Style)FindResource("SectionTitle"), Margin = new Thickness(0, 0, 0, 8) });
        if (d.Methods.Count == 0) { var none = new TextBlock { Text = "Nenhum recebimento neste dia." }; none.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); list.Children.Add(none); }
        foreach (var method in d.Methods) list.Children.Add(DialogKit.Line(method.Method, Money(method.Total)));
        body.Children.Add(card);
    }
    void Copy()
    {
        if (current is not DaySummary d) return;
        var lines = new List<string> { $"Resumo de {d.Date:dd/MM/yyyy}", $"Faturamento: {d.Billed.ToString("C2", Pt)}", $"Recebido: {d.Received.ToString("C2", Pt)}" };
        lines.AddRange(d.Methods.Select(x => $"- {x.Method}: {x.Total.ToString("C2", Pt)}"));
        lines.AddRange([$"Sangrias: {d.Withdrawals.ToString("C2", Pt)}", $"Pedidos criados: {d.OrdersCreated}", $"Ficaram prontos: {d.Ready}", $"Atrasados agora: {d.Overdue}", $"Vencem no próximo dia útil: {d.DueNext}"]);
        try { Clipboard.SetText(string.Join(Environment.NewLine, lines)); if (Window.GetWindow(this) is Window owner) Toast.Show(owner, "Resumo copiado.", tone: "ok"); } catch (Exception ex) { if (Window.GetWindow(this) is Window owner) AlertWindow.Info(owner, "Não foi possível copiar", ex.Message); }
    }
}
