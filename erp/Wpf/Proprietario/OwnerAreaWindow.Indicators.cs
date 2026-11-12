using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace GraficaCaixa.Wpf;

// Abas "Limite do MEI" e "Retrabalho" da Área do proprietário: só leitura, com a sessão do proprietário liberada.
public sealed partial class OwnerAreaWindow
{
    readonly MeiLimitView meiView = new();
    readonly ReworkListView reworkView = new();
    async void RefreshIndicators()
    {
        await Prompt12Ui.Run(this, async () =>
        {
            OwnerSecurity.Require(session);
            var indicators = store.IsRemote ? await store.QueryRemoteAsync<OwnerIndicators>("desktop/owner/indicators") : OwnerIndicators.Of(store.Load(), DateTime.Today);
            meiView.Render(indicators.Mei); reworkView.Render(indicators.Reworks);
        });
    }
}

public sealed class MeiLimitView : ScrollViewer
{
    static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    readonly StackPanel body = new() { Margin = new Thickness(0, 14, 0, 0) };
    public MeiLimitView() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto; Content = body; }
    static string Money(decimal value) => value.ToString("C2", Pt);

    public void Render(MeiStatus m)
    {
        body.Children.Clear();
        if (m.Limit <= 0) { body.Children.Add(new EmptyState { IconName = "inbox", Title = "Limite do MEI desligado", Hint = "Informe o limite anual em Sistema › Configurações › Regras." }); return; }
        var bars = new Border { Style = (Style)FindResource("Card") }; var stack = new StackPanel(); bars.Child = stack;
        stack.Children.Add(new TextBlock { Text = $"Limite de {m.Year}: {Money(m.Limit)}", FontSize = 18, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(Row("Faturamento total", "Pedidos e vendas de balcão, com ou sem nota", m.Billed, m.Limit, m.BilledReach));
        stack.Children.Add(Row("Notas registradas", $"{m.NotesCount} nota(s) registrada(s) no ERP", m.Notes, m.Limit, m.NotesReach));
        body.Children.Add(bars);

        var kpis = new UniformGrid { Columns = 3, Margin = new Thickness(0, 12, 0, 12) };
        kpis.Children.Add(new KpiCard { Label = "Falta até o limite", Value = Money(Math.Max(0, m.Limit - m.Billed)), Hint = "pelo faturamento total", Tone = "info", Margin = new Thickness(0, 0, 10, 0) });
        kpis.Children.Add(new KpiCard { Label = "Faturado sem nota registrada", Value = Money(m.WithoutNote), Hint = "faturamento menos notas", Tone = "warn", Margin = new Thickness(0, 0, 10, 0) });
        kpis.Children.Add(new KpiCard { Label = "Recebido no ano", Value = Money(m.Received), Hint = "entradas registradas no caixa", Tone = "ok" });
        body.Children.Add(kpis);

        var excess = m.Billed > m.Limit * 1.2m ? "O faturamento passou mais de 20% do limite. Fale com seu contador." : m.Billed > m.Limit ? "O faturamento passou do limite. Fale com seu contador." : "";
        var note = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 14, 10) };
        note.SetResourceReference(Border.BackgroundProperty, "WarningSoft");
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = (excess.Length > 0 ? excess + " " : "") + "O limite do MEI considera toda a receita, com ou sem nota. Vendas fora do ERP não entram nestas contas. Confirme os valores com seu contador." };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Warning"); note.Child = text; body.Children.Add(note);
    }

    FrameworkElement Row(string title, string hint, decimal value, decimal limit, DateTime? reach)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        var head = new DockPanel();
        var amount = new TextBlock { Text = $"{Money(value)} · {MeiStatus.Percent(value, limit):N1}%", FontWeight = FontWeights.SemiBold }; DockPanel.SetDock(amount, Dock.Right); head.Children.Add(amount);
        head.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold }); panel.Children.Add(head);
        var fill = (double)Math.Clamp(value / limit * 100, 0, 100);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(fill, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 - fill, GridUnitType.Star) });
        var bar = new Border { CornerRadius = new CornerRadius(5) }; bar.SetResourceReference(Border.BackgroundProperty, MeiStatus.Tone(value, limit)); grid.Children.Add(bar);
        var track = new Border { Height = 10, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 7, 0, 0), ClipToBounds = true, Child = grid }; track.SetResourceReference(Border.BackgroundProperty, "Line"); panel.Children.Add(track);
        var pace = value >= limit ? "limite atingido" : reach is DateTime date ? $"no ritmo atual, chega ao limite em {date:dd/MM}" : "no ritmo atual, não chega ao limite neste ano";
        var caption = new TextBlock { Text = $"{hint} · {pace}", FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) }; caption.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); panel.Children.Add(caption);
        return panel;
    }
}

public sealed class ReworkListView : Grid
{
    static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    readonly UniformGrid kpis = new() { Columns = 3, Margin = new Thickness(0, 14, 0, 12) };
    readonly DataGrid grid = new() { IsReadOnly = true, AutoGenerateColumns = false };
    readonly EmptyState empty = new() { IconName = "inbox", Title = "Nenhum retrabalho neste ano", Hint = "Registre pelo botão Retrabalho e terceiros, dentro do pedido.", IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    public ReworkListView()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); RowDefinitions.Add(new RowDefinition());
        Children.Add(kpis);
        void Column(string header, string path, double width) => grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new System.Windows.Data.Binding(path), Width = new DataGridLength(width, DataGridLengthUnitType.Star) });
        Column("Data", nameof(Line.Date), .7); Column("Pedido", nameof(Line.Order), .6); Column("Cliente", nameof(Line.Customer), 1.3); Column("Motivo", nameof(Line.Reason), 2); Column("Custo", nameof(Line.Cost), .8);
        var host = new Grid(); host.Children.Add(grid); host.Children.Add(empty); Grid.SetRow(host, 1); Children.Add(host);
    }
    public void Render(List<ReworkRow> rows)
    {
        var today = DateTime.Today; var month = rows.Where(x => x.Date.Month == today.Month).ToList();
        var top = rows.GroupBy(x => x.Reason.Trim(), StringComparer.CurrentCultureIgnoreCase).OrderByDescending(x => x.Sum(y => y.Cost)).ThenByDescending(x => x.Count()).FirstOrDefault();
        kpis.Children.Clear();
        kpis.Children.Add(new KpiCard { Label = "Custo neste mês", Value = month.Sum(x => x.Cost).ToString("C2", Pt), Hint = $"{month.Count} retrabalho(s); já descontado do resultado", Tone = "danger", Margin = new Thickness(0, 0, 10, 0) });
        kpis.Children.Add(new KpiCard { Label = "Custo no ano", Value = rows.Sum(x => x.Cost).ToString("C2", Pt), Hint = $"{rows.Count} retrabalho(s)", Tone = "warn", Margin = new Thickness(0, 0, 10, 0) });
        kpis.Children.Add(new KpiCard { Label = "Motivo que mais custou", Value = top == null ? "—" : top.Sum(x => x.Cost).ToString("C2", Pt), Hint = top?.Key ?? "sem registros", Tone = "info" });
        grid.ItemsSource = rows.Select(x => new Line(x)).ToList();
        empty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    sealed class Line(ReworkRow row)
    {
        public string Date => row.Date.ToString("dd/MM/yyyy");
        public string Order => row.Number > 0 ? $"#{row.Number}" : "—";
        public string Customer => row.Customer;
        public string Reason => row.Reason;
        public string Cost => row.Cost.ToString("C2", Pt);
    }
}
