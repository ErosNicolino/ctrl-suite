using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace GraficaCaixa.Wpf;

// Aba "Fluxo previsto" da Área do proprietário: o que deve entrar e sair nas próximas 5 semanas (CashForecast), com aviso das contas que vencem logo.
public sealed partial class OwnerAreaWindow
{
    readonly CashForecastView forecastView = new();
    UIElement ForecastTab(){forecastView.ShowExpenses += () => tabs.SelectedIndex = 1;return forecastView;}
    async void RefreshForecast()
    {
        await Prompt12Ui.Run(this, async () =>
        {
            OwnerSecurity.Require(session);
            forecastView.Render(store.IsRemote ? await store.QueryRemoteAsync<CashForecast>("desktop/owner/forecast") : CashForecast.Build(store.Load(), DateTime.Today));
        });
    }
}

public sealed class CashForecastView : ScrollViewer
{
    readonly StackPanel forecastBody = new() { Margin = new Thickness(0, 14, 0, 0) };
    static readonly System.Globalization.CultureInfo ForecastPt = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
    public event Action? ShowExpenses;
    public CashForecastView() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto; Content = forecastBody; }
    Border Card(Thickness? margin = null) => new() { Style = (Style)FindResource("Card"), Margin = margin ?? new Thickness(0) };
    static TextBlock Section(string text) => new() { Text = text, FontSize = 18, FontWeight = FontWeights.SemiBold };
    System.Windows.Media.Brush Brush(string key) => (System.Windows.Media.Brush)FindResource(key);

    public void Render(CashForecast f)
    {
        forecastBody.Children.Clear();
        static string Money(decimal value) => value.ToString("C2", ForecastPt);
        static string Signed(decimal value) => (value > 0 ? "+ " : value < 0 ? "− " : "") + Math.Abs(value).ToString("C2", ForecastPt);
        static string Range(DateTime s, DateTime e) => s.Month == e.Month ? $"{s:dd} a {e:dd/MM}" : $"{s:dd/MM} a {e:dd/MM}";

        // Aviso: contas vencidas ou que vencem nos próximos 3 dias.
        var limit = DateTime.Today.AddDays(3);
        var soon = f.Items.Where(x => !x.In && !x.Projected && (x.Overdue || x.Date <= limit)).ToList();
        if (soon.Count > 0)
        {
            var alert = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 12) };
            alert.SetResourceReference(Border.BackgroundProperty, "WarningSoft");
            var row = new DockPanel();
            var go = new Button { Content = "Ver em Despesas", Style = (Style)FindResource("SoftButton") }; go.Click += (_, _) => ShowExpenses?.Invoke();
            go.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(go, Dock.Right); row.Children.Add(go);
            var overdue = soon.Count(x => x.Overdue);
            var title = overdue > 0 ? $"{soon.Count} conta(s) vencida(s) ou vencendo até {limit:dd/MM}" : $"{soon.Count} conta(s) vencem até {limit:dd/MM}";
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            text.Inlines.Add(new System.Windows.Documents.Run(title + ": ") { FontWeight = FontWeights.SemiBold });
            text.Inlines.Add(new System.Windows.Documents.Run(string.Join(" · ", soon.Take(4).Select(x => $"{x.Title} {Money(x.Amount)}" + (x.Overdue ? " (vencida)" : "")))+(soon.Count > 4 ? $" · e mais {soon.Count - 4}" : "")));
            text.SetResourceReference(TextBlock.ForegroundProperty, "Warning");
            row.Children.Add(text); alert.Child = row; forecastBody.Children.Add(alert);
        }

        // Indicadores das 5 semanas.
        var tight = f.Weeks.Where(x => x.Net < 0).OrderBy(x => x.Net).FirstOrDefault();
        var kpis = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 12) };
        var weeksText = $"{f.Weeks.Count} semanas";
        kpis.Children.Add(new KpiCard { Label = $"Entra ({weeksText})", Value = Money(f.TotalIn), Hint = $"{f.Items.Count(x => x.In)} pedido(s) com saldo e prazo", Tone = "ok", Margin = new Thickness(0, 0, 10, 0) });
        kpis.Children.Add(new KpiCard { Label = $"Sai ({weeksText})", Value = Money(f.TotalOut), Hint = $"{f.Items.Count(x => !x.In)} conta(s), {f.Items.Count(x => x.Projected)} prevista(s)", Tone = "danger", Margin = new Thickness(0, 0, 10, 0) });
        var net = f.TotalIn - f.TotalOut;
        var netCard = new KpiCard { Label = "Diferença prevista", Value = Signed(net), Hint = "sem contar vendas de balcão", Tone = "info", Margin = new Thickness(0, 0, 10, 0) };
        netCard.SetResourceReference(KpiCard.ValueBrushProperty, net < 0 ? "Danger" : "COk");
        kpis.Children.Add(netCard);
        kpis.Children.Add(tight == null
            ? new KpiCard { Label = "Semana mais apertada", Value = "Nenhuma", Hint = "todas as semanas fecham no azul", Tone = "ok" }
            : new KpiCard { Label = "Semana mais apertada", Value = Range(tight.Start, tight.End), Hint = $"sai {Money(-tight.Net)} a mais do que entra", Tone = "warn" });
        forecastBody.Children.Add(kpis);

        // Semanas: entra × sai em barras na mesma escala, e o saldo de cada uma.
        var scale = Math.Max(1, f.Weeks.Max(x => Math.Max(x.In, x.Out)));
        var weeks = new UniformGrid { Columns = f.Weeks.Count, Margin = new Thickness(0, 0, 0, 12) };
        for (var i = 0; i < f.Weeks.Count; i++)
        {
            var w = f.Weeks[i];
            var card = Card(new Thickness(0, 0, i < f.Weeks.Count - 1 ? 10 : 0, 0)); card.Padding = new Thickness(12, 10, 12, 10);
            if (w == tight) { card.SetResourceReference(Border.BorderBrushProperty, "CWarn"); card.BorderThickness = new Thickness(1.5); }
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = Range(w.Start, w.End), FontWeight = FontWeights.SemiBold });
            stack.Children.Add(new TextBlock { Text = i == 0 ? "esta semana" : i == 1 ? "próxima semana" : $"daqui a {i} semanas", FontSize = 11, Foreground = Brush("Muted"), Margin = new Thickness(0, 0, 0, 8) });
            if (w.In == 0 && w.Out == 0) { stack.Children.Add(new TextBlock { Text = "Nada previsto", Foreground = Brush("Muted"), FontSize = 12, Margin = new Thickness(0, 6, 0, 22) }); card.Child = stack; weeks.Children.Add(card); continue; }
            stack.Children.Add(WeekLine("entra", w.In, "COk", scale));
            stack.Children.Add(WeekLine("sai", w.Out, "CDanger", scale));
            var total = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var value = new TextBlock { Text = Signed(w.Net), FontWeight = FontWeights.SemiBold }; DockPanel.SetDock(value, Dock.Right); total.Children.Add(value);
            value.SetResourceReference(TextBlock.ForegroundProperty, w.Net < 0 ? "Danger" : "Ink");
            total.Children.Add(new TextBlock { Text = "saldo", Foreground = Brush("Muted"), FontSize = 12 });
            var separator = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 8, 0, 0) }; separator.SetResourceReference(Border.BorderBrushProperty, "Line");
            stack.Children.Add(separator); stack.Children.Add(total);
            card.Child = stack; weeks.Children.Add(card);
        }
        forecastBody.Children.Add(weeks);

        // Lista do que entra e sai, por data.
        var listCard = Card(); var listStack = new StackPanel(); listCard.Child = listStack;
        listStack.Children.Add(Section("Entradas e saídas previstas"));
        listStack.Children.Add(new TextBlock { Text = "Despesas vencidas e pedidos atrasados aparecem na data de hoje.", Foreground = Brush("Muted"), FontSize = 12, Margin = new Thickness(0, 3, 0, 10) });
        if (f.Items.Count == 0) listStack.Children.Add(new TextBlock { Text = "Nada previsto para as próximas semanas.", Foreground = Brush("Muted") });
        else
        {
            var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, HeadersVisibility = DataGridHeadersVisibility.Column, ItemsSource = f.Items.Select(x => new ForecastRow(x)).ToList(), MaxHeight = 420 };
            grid.Columns.Add(new DataGridTextColumn { Header = "Data", Binding = new Binding(nameof(ForecastRow.Date)), Width = new DataGridLength(110) });
            var kind = new FrameworkElementFactory(typeof(Border));
            kind.SetValue(Border.CornerRadiusProperty, new CornerRadius(9)); kind.SetValue(Border.PaddingProperty, new Thickness(8, 2, 8, 2)); kind.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left); kind.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            kind.SetBinding(Border.BackgroundProperty, new Binding(nameof(ForecastRow.KindBack)));
            var kindText = new FrameworkElementFactory(typeof(TextBlock)); kindText.SetBinding(TextBlock.TextProperty, new Binding(nameof(ForecastRow.Kind))); kindText.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(ForecastRow.KindFore))); kindText.SetValue(TextBlock.FontSizeProperty, 12d); kindText.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            kind.AppendChild(kindText);
            grid.Columns.Add(new DataGridTemplateColumn { Header = "Tipo", Width = new DataGridLength(190), CellTemplate = new DataTemplate { VisualTree = kind } });
            grid.Columns.Add(new DataGridTextColumn { Header = "Descrição", Binding = new Binding(nameof(ForecastRow.Title)), Width = new DataGridLength(1, DataGridLengthUnitType.Star), ElementStyle = (Style)FindResource("TrimCell") });
            var money = new Style(typeof(TextBlock), (Style)FindResource("MoneyCell")); money.Setters.Add(new Setter(TextBlock.ForegroundProperty, new Binding(nameof(ForecastRow.AmountBrush))));
            grid.Columns.Add(new DataGridTextColumn { Header = "Valor", Binding = new Binding(nameof(ForecastRow.Amount)), Width = new DataGridLength(130), ElementStyle = money, HeaderStyle = (Style)FindResource("MoneyHeader") });
            listStack.Children.Add(grid);
        }
        var note = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Brush("Muted"), Margin = new Thickness(0, 10, 0, 0) };
        note.Text = (f.UndatedReceivable > 0 ? $"Fora da previsão (sem data): {Money(f.UndatedReceivable)} a receber em {f.UndatedCount} pendência(s) — pedidos entregues e dívidas em Devedores, e pedidos sem prazo iniciado. " : "")
            + "Compras a fazer não têm valor cadastrado e não entram. Vendas de balcão também não, porque não dá para prever.";
        listStack.Children.Add(note);
        forecastBody.Children.Add(listCard);
    }

    UIElement WeekLine(string label, decimal value, string color, decimal scale)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 2, 0, 4) };
        var line = new DockPanel();
        var amount = new TextBlock { Text = value.ToString("C0", ForecastPt), FontSize = 12, FontWeight = FontWeights.SemiBold }; amount.SetResourceReference(TextBlock.ForegroundProperty, color);
        DockPanel.SetDock(amount, Dock.Right); line.Children.Add(amount);
        line.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Brush("Muted") });
        stack.Children.Add(line);
        var bar = new Grid { Height = 6, Margin = new Thickness(0, 3, 0, 0) };
        var share = (double)(value / scale);
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, share), GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, 1 - share), GridUnitType.Star) });
        var track = new Border { CornerRadius = new CornerRadius(3) }; track.SetResourceReference(Border.BackgroundProperty, "SurfaceAlt"); Grid.SetColumnSpan(track, 2); bar.Children.Add(track);
        var fill = new Border { CornerRadius = new CornerRadius(3), Visibility = value > 0 ? Visibility.Visible : Visibility.Collapsed }; fill.SetResourceReference(Border.BackgroundProperty, color); bar.Children.Add(fill);
        stack.Children.Add(bar);
        return stack;
    }

    sealed class ForecastRow
    {
        public string Date { get; }
        public string Kind { get; }
        public string Title { get; }
        public string Amount { get; }
        public System.Windows.Media.Brush AmountBrush { get; }
        public System.Windows.Media.Brush KindBack { get; }
        public System.Windows.Media.Brush KindFore { get; }
        public ForecastRow(ForecastItem x)
        {
            Date = x.Date == DateTime.Today ? "hoje" : x.Date.ToString("dd/MM · ddd", ForecastPt);
            Kind = x.Projected ? "sai · recorrente prevista" : x.In ? (x.Overdue ? "entra · pedido atrasado" : "entra · pedido") : (x.Overdue ? "sai · despesa vencida" : "sai · despesa");
            Title = x.Title;
            Amount = (x.In ? "+ " : "− ") + x.Amount.ToString("C2", ForecastPt);
            AmountBrush = ThemeManager.Get(x.In ? "COk" : "Danger");
            KindBack = ThemeManager.Get(x.Projected ? "SurfaceAlt" : x.In ? "AccentSoft" : "DangerSoft");
            KindFore = ThemeManager.Get(x.Projected ? "Muted" : x.In ? "AccentInk" : "Danger");
        }
    }
}
