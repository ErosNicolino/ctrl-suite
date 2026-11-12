using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace GraficaCaixa.Wpf;

// Fornecedores: lista (ativos/inativos/todos, busca e filtro pelo que fornecem) e painel do selecionado com contatos,
// condições, últimos preços pagos e "Nova compra deste fornecedor". Dados vêm do módulo Compras (mesmas permissões).
public sealed class SuppliersWindow : Window
{
    enum Scope { Active, Inactive, All }
    static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    static readonly double[] Columns = { 1.5, 1.3, 1.25, 1.15, .9 };
    readonly DataStore store; readonly ModuleLoader remoteLoad;
    readonly TextBox searchBox = new() { ToolTip = "Pesquisar nome, CNPJ, contato ou material", Tag = "Pesquisar nome, CNPJ, contato ou material…" };
    readonly ComboBox suppliesBox = new() { Width = 210, Margin = new Thickness(10, 0, 0, 0) };
    readonly KpiCard activeKpi = new() { Label = "Fornecedores ativos", Tone = "info" }, spendKpi = new() { Label = "Gasto em compras no mês", Tone = "warn" }, pendingKpi = new() { Label = "Compras pendentes", Tone = "warn" }, topKpi = new() { Label = "Mais usado no mês", Tone = "ok" };
    readonly StackPanel rows = new(); readonly Dictionary<Scope, Button> scopeButtons = [];
    readonly ContentControl detail = new();
    readonly EmptyState empty = new() { IconName = "truck", Title = "Nenhum fornecedor cadastrado", Hint = "Cadastre quem vende seus materiais para comparar preços e ver o histórico.", ActionText = "Novo fornecedor", Visibility = Visibility.Collapsed };
    Scope scope = Scope.Active; Prompt12State state = new(); Guid? selected; bool busy;

    public SuppliersWindow(DataStore store)
    {
        this.store = store; Title = "Fornecedores"; Width = 1260; Height = 760; MinWidth = 1050; MinHeight = 620; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new Grid { Margin = new Thickness(24, 22, 24, 18) }; Content = root;
        foreach (var h in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star) }) root.RowDefinitions.Add(new RowDefinition { Height = h });

        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var add = new Button { Content = "+ Novo fornecedor", Style = S("PrimaryButton"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) }; add.Click += (_, _) => Edit(null);
        var compare = new Button { Content = "Comparar preços", Style = S("SoftButton"), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Último preço de cada material em cada fornecedor" }; compare.Click += (_, _) => ComparePrices();
        DockPanel.SetDock(add, Dock.Right); heading.Children.Add(add); DockPanel.SetDock(compare, Dock.Right); heading.Children.Add(compare);
        var titles = new StackPanel(); titles.Children.Add(new TextBlock { Text = "Fornecedores", Style = S("Title") });
        var sub = new TextBlock { Text = "Quem vende cada material, contatos, prazos e o histórico de preços das compras.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) }; sub.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); titles.Children.Add(sub);
        heading.Children.Add(titles); root.Children.Add(heading);

        var kpis = new UniformGrid { Columns = 4, Margin = new Thickness(-6, 0, -6, 14) };
        foreach (var k in new[] { activeKpi, spendKpi, pendingKpi, topKpi }) { k.Margin = new Thickness(6, 0, 6, 0); k.IsLoading = true; kpis.Children.Add(k); }
        spendKpi.SetResourceReference(KpiCard.StripeProperty, "KindCompra");
        Grid.SetRow(kpis, 1); root.Children.Add(kpis);

        var tools = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var filters = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 0) };
        foreach (var (value, text) in new[] { (Scope.Active, "Ativos"), (Scope.Inactive, "Inativos"), (Scope.All, "Todos") })
        { var b = new Button { Content = text, Style = S("SoftButton"), Margin = new Thickness(0, 0, 8, 0) }; b.Click += (_, _) => { scope = value; Refresh(); }; scopeButtons[value] = b; filters.Children.Add(b); }
        DockPanel.SetDock(filters, Dock.Left); tools.Children.Add(filters); DockPanel.SetDock(suppliesBox, Dock.Right); tools.Children.Add(suppliesBox); tools.Children.Add(searchBox);
        searchBox.TextChanged += (_, _) => Draw(); SearchShortcut.SetIsTarget(searchBox, true); suppliesBox.SelectionChanged += (_, _) => Draw();
        Grid.SetRow(tools, 2); root.Children.Add(tools);

        var split = new Grid(); split.ColumnDefinitions.Add(new ColumnDefinition()); split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) }); split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(400) });
        var tableCard = new Border { Style = S("Card"), Padding = new Thickness(0), ClipToBounds = true };
        var table = new DockPanel(); var header = Line(new[] { "Fornecedor", "O que fornece", "Contato", "Última compra", "Gasto no mês" }.Select(Header).ToArray());
        header.SetResourceReference(Border.BackgroundProperty, "SurfaceAlt"); header.Padding = new Thickness(14, 9, 14, 9); DockPanel.SetDock(header, Dock.Top); table.Children.Add(header);
        var area = new Grid(); area.Children.Add(new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        empty.Action += (_, _) => Edit(null); area.Children.Add(empty); table.Children.Add(area); tableCard.Child = table; split.Children.Add(tableCard);
        var detailCard = new Border { Style = S("Card"), Child = new ScrollViewer { Content = detail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }; Grid.SetColumn(detailCard, 2); split.Children.Add(detailCard);
        Grid.SetRow(split, 3); root.Children.Add(split);

        remoteLoad = new(this, store); Loaded += (_, _) => Refresh();
    }

    Style S(string key) => (Style)FindResource(key);
    static TextBlock Header(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, FontSize = 12, TextAlignment = text == "Gasto no mês" ? TextAlignment.Right : TextAlignment.Left };
    static TextBlock Muted(string text, double size = 12) { var t = new TextBlock { Text = text, FontSize = size, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = text.Length > 0 ? text : null }; t.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); return t; }
    static Border Line(UIElement[] cells)
    {
        var grid = new Grid();
        for (var i = 0; i < Columns.Length; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Columns[i], GridUnitType.Star) });
        for (var i = 0; i < cells.Length; i++) { if (cells[i] is FrameworkElement f) f.Margin = new Thickness(0, 0, i == cells.Length - 1 ? 0 : 10, 0); Grid.SetColumn(cells[i], i); grid.Children.Add(cells[i]); }
        return new Border { Child = grid };
    }
    static StackPanel TwoLines(string top, string bottom, bool strong = false)
    {
        var s = new StackPanel(); s.Children.Add(new TextBlock { Text = top, FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = top });
        if (bottom.Length > 0) s.Children.Add(Muted(bottom)); return s;
    }

    async void Refresh()
    {
        if (!IsLoaded) return;
        try { var data = store.IsRemote ? await remoteLoad.LoadAsync("compras", refresh: Refresh) : store.Load(); if (data == null) return; state = data.Prompt12; state.Suppliers ??= []; }
        catch (Exception) { return; }
        var month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1); var next = month.AddMonths(1);
        var actives = state.Suppliers.Count(x => x.Active);
        activeKpi.Value = actives.ToString(); activeKpi.Hint = state.Suppliers.Count - actives is var off && off > 0 ? $"{off} inativo(s)" : "cadastrados";
        var monthPurchases = SupplierBook.Purchases(state).Where(x => x.Date >= month && x.Date < next).ToList();
        spendKpi.Value = monthPurchases.Sum(x => x.Amount).ToString("C2", Pt); spendKpi.Hint = $"{monthPurchases.Count} compra(s) · {monthPurchases.Select(x => SupplierBook.Key(x.Supplier)).Where(x => x.Length > 0).Distinct().Count()} fornecedor(es)";
        var pending = state.PurchaseNeeds.Where(x => x.Status == PurchaseNeedStatus.ToBuy).ToList();
        pendingKpi.Value = pending.Count.ToString(); pendingKpi.Hint = $"{pending.Count(x => x.Supplier.Trim().Length > 0)} já com fornecedor definido";
        var top = SupplierBook.SpendBySupplier(state, month, next).FirstOrDefault(x => x.Supplier != "Sem fornecedor");
        topKpi.Value = top.Supplier ?? "—"; topKpi.Hint = top.Supplier == null ? "nenhuma compra no mês" : $"{top.Amount.ToString("C2", Pt)} · {top.Count} compra(s)";
        foreach (var k in new[] { activeKpi, spendKpi, pendingKpi, topKpi }) k.IsLoading = false;
        var current = suppliesBox.SelectedItem as string;
        var supplies = state.Suppliers.SelectMany(x => x.Supplies).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList(); supplies.Insert(0, "Tudo que fornecem");
        suppliesBox.ItemsSource = supplies; suppliesBox.SelectedItem = current != null && supplies.Contains(current) ? current : supplies[0];
        Draw();
    }

    void Draw()
    {
        foreach (var pair in scopeButtons) { var on = pair.Key == scope; pair.Value.SetResourceReference(BackgroundProperty, on ? "AccentSoft" : "Surface"); pair.Value.SetResourceReference(ForegroundProperty, on ? "AccentInk" : "Ink"); pair.Value.SetResourceReference(BorderBrushProperty, on ? "Accent" : "Line"); }
        scopeButtons[Scope.Active].Content = $"Ativos ({state.Suppliers.Count(x => x.Active)})"; scopeButtons[Scope.Inactive].Content = $"Inativos ({state.Suppliers.Count(x => !x.Active)})"; scopeButtons[Scope.All].Content = $"Todos ({state.Suppliers.Count})";
        var term = SupplierBook.Key(searchBox.Text); var supply = suppliesBox.SelectedIndex > 0 ? suppliesBox.SelectedItem as string : null;
        var month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var visible = state.Suppliers.Where(x => scope == Scope.All || (scope == Scope.Active) == x.Active)
            .Where(x => supply == null || x.Supplies.Contains(supply, StringComparer.CurrentCultureIgnoreCase))
            .Where(x => term.Length == 0 || SupplierBook.Key($"{x.Name} {x.LegalName} {x.Document} {x.ContactPerson} {x.WhatsApp} {x.Phone} {string.Join(' ', x.Supplies)}").Contains(term) || SupplierBook.Purchases(state, x).Any(p => SupplierBook.Key(p.Material).Contains(term)))
            .Select(x => (Supplier: x, History: SupplierBook.Purchases(state, x).OrderByDescending(p => p.Date).ToList()))
            .OrderByDescending(x => x.History.Where(p => p.Date >= month).Sum(p => p.Amount)).ThenBy(x => x.Supplier.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (selected == null || visible.All(x => x.Supplier.Id != selected)) selected = visible.FirstOrDefault().Supplier?.Id;
        rows.Children.Clear();
        foreach (var (s, history) in visible)
        {
            var last = history.FirstOrDefault(); var spend = history.Where(p => p.Date >= month).Sum(p => p.Amount);
            var tags = new WrapPanel(); foreach (var t in s.Supplies.Take(3)) { var tag = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 1, 7, 1), Margin = new Thickness(0, 1, 4, 1), Child = new TextBlock { Text = t, FontSize = 11, FontWeight = FontWeights.SemiBold } }; tag.SetResourceReference(Border.BackgroundProperty, "KindCompraSoft"); ((TextBlock)tag.Child).SetResourceReference(TextBlock.ForegroundProperty, "KindCompra"); tags.Children.Add(tag); }
            var money = new TextBlock { Text = spend.ToString("C2", Pt), TextAlignment = TextAlignment.Right, FontWeight = spend > 0 ? FontWeights.SemiBold : FontWeights.Normal };
            var phone = s.WhatsApp.Length > 0 ? s.WhatsApp : s.Phone;
            var line = Line(new UIElement[] { TwoLines(s.Name, s.Active ? (s.Document.Length > 0 ? s.Document : s.LeadTime.Length > 0 ? "Prazo " + s.LeadTime : "") : "Inativo", true), tags, TwoLines(phone.Length > 0 ? phone : "—", s.ContactPerson), TwoLines(last == null ? "Sem compras" : last.Date.ToString("dd/MM", Pt), last?.Material ?? ""), money });
            line.Padding = new Thickness(14, 9, 14, 9); line.BorderThickness = new Thickness(0, 1, 0, 0); line.SetResourceReference(Border.BorderBrushProperty, "Line"); line.Cursor = Cursors.Hand; line.Opacity = s.Active ? 1 : .65;
            if (s.Id == selected) { line.SetResourceReference(Border.BackgroundProperty, "Selected"); line.BorderThickness = new Thickness(3, 1, 0, 0); line.SetResourceReference(Border.BorderBrushProperty, "Accent"); }
            else line.SetResourceReference(Border.BackgroundProperty, "Surface");
            var id = s.Id; line.MouseLeftButtonDown += (_, e) => { selected = id; if (e.ClickCount == 2) Edit(state.Suppliers.FirstOrDefault(x => x.Id == id)); else Draw(); };
            rows.Children.Add(line);
        }
        empty.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (state.Suppliers.Count > 0) { empty.Title = "Nenhum fornecedor encontrado"; empty.Hint = "Tente outro nome, material ou filtro."; empty.ActionText = ""; }
        ShowDetail(state.Suppliers.FirstOrDefault(x => x.Id == selected));
    }

    void ShowDetail(Supplier? s)
    {
        var panel = new StackPanel { Margin = new Thickness(2) }; detail.Content = panel;
        if (s == null) { panel.Children.Add(Muted("Selecione um fornecedor para ver contatos, condições e preços.", 13)); return; }
        panel.Children.Add(new TextBlock { Text = s.Name, Style = S("SectionTitle"), TextWrapping = TextWrapping.Wrap });
        var ident = string.Join(" · ", new[] { s.LegalName, s.Document.Length > 0 ? "CNPJ/CPF " + s.Document : "" }.Where(x => x.Length > 0));
        if (ident.Length > 0) { var t = Muted(ident); t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 3, 0, 0); panel.Children.Add(t); }
        if (!s.Active) panel.Children.Add(new StatusTag { Text = "Inativo", Tone = "neutral", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) });
        var contacts = new UniformGrid { Columns = 3, Margin = new Thickness(-3, 12, -3, 10) };
        Button Contact(string label, string tip, bool enabled, Action act) { var b = new Button { Content = label, Style = S("SoftButton"), Margin = new Thickness(3, 0, 3, 0), Padding = new Thickness(4, 0, 4, 0), MinWidth = 0, FontSize = 13, ToolTip = tip, IsEnabled = enabled }; b.Click += (_, _) => act(); contacts.Children.Add(b); return b; }
        Contact("WhatsApp", s.WhatsApp.Length > 0 ? "Abrir conversa no WhatsApp" : "WhatsApp não informado", s.WhatsApp.Length > 0, () => WhatsAppActions.Open(this, s.WhatsApp, ""));
        var phone = s.Phone.Length > 0 ? s.Phone : s.WhatsApp;
        Contact("Telefone", phone.Length > 0 ? "Copiar o telefone " + phone : "Telefone não informado", phone.Length > 0, () => { Clipboard.SetText(phone); Toast.Show(this, "Telefone copiado: " + phone, tone: "ok"); });
        Contact("E-mail", s.Email.Length > 0 ? "Escrever para " + s.Email : "E-mail não informado", s.Email.Length > 0, () => Prompt12Ui.Run(this, () => Process.Start(new ProcessStartInfo("mailto:" + s.Email) { UseShellExecute = true })));
        panel.Children.Add(contacts);
        var info = new UniformGrid { Columns = 2 };
        void Info(string label, string value) { var p = new StackPanel { Margin = new Thickness(0, 0, 10, 8) }; p.Children.Add(Muted(label)); p.Children.Add(new TextBlock { Text = value.Length > 0 ? value : "Não informado", FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap }); info.Children.Add(p); }
        Info("Contato", s.ContactPerson); Info("Prazo de entrega", s.LeadTime); Info("Pagamento", s.PaymentTerms); Info("Pedido mínimo", s.MinimumOrder is decimal m ? m.ToString("C2", Pt) : "");
        panel.Children.Add(info);
        if (s.Address.Length > 0) { var a = Muted(s.Address); a.TextWrapping = TextWrapping.Wrap; a.Margin = new Thickness(0, 0, 0, 6); panel.Children.Add(a); }
        if (s.Notes.Length > 0) { var n = new TextBlock { Text = s.Notes, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 0, 0, 6) }; panel.Children.Add(n); }
        panel.Children.Add(DialogKit.Eyebrow("ÚLTIMOS PREÇOS PAGOS"));
        var prices = SupplierBook.LastPrices(state, s);
        if (prices.Count == 0) panel.Children.Add(Muted("Nenhuma compra registrada com este fornecedor ainda.", 12));
        foreach (var p in prices.Take(6))
        {
            var g = new Grid { Margin = new Thickness(0, 4, 0, 0) }; g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
            g.Children.Add(new TextBlock { Text = p.Material + (p.Unit.Length > 0 ? $" ({p.Unit})" : ""), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = p.Material });
            var price = new StackPanel { Orientation = Orientation.Horizontal }; price.Children.Add(new TextBlock { Text = p.UnitPrice.ToString("C2", Pt), FontWeight = FontWeights.SemiBold, FontSize = 12 });
            if (p.PreviousUnitPrice is decimal before && before > 0 && before != p.UnitPrice)
            { var pct = (p.UnitPrice - before) / before * 100; var tr = new TextBlock { Text = $" {(pct > 0 ? "▲" : "▼")} {Math.Abs(pct):0}%", FontSize = 11, FontWeight = FontWeights.SemiBold, ToolTip = "Antes: " + before.ToString("C2", Pt) }; tr.SetResourceReference(TextBlock.ForegroundProperty, pct > 0 ? "Danger" : "AccentInk"); price.Children.Add(tr); }
            Grid.SetColumn(price, 1); g.Children.Add(price); var d = Muted(p.Date.ToString("dd/MM", Pt)); d.TextAlignment = TextAlignment.Right; Grid.SetColumn(d, 2); g.Children.Add(d);
            panel.Children.Add(g);
        }
        var actions = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        Button Act(string label, string style, Action act) { var b = new Button { Content = label, Style = S(style), Margin = new Thickness(0, 0, 8, 8) }; b.Click += (_, _) => act(); actions.Children.Add(b); return b; }
        Act("Editar", "SoftButton", () => Edit(s));
        Act($"Histórico ({SupplierBook.Purchases(state, s).Count()})", "SoftButton", () => History(s));
        Act(s.Active ? "Inativar" : "Reativar", "GhostButton", () => ToggleActive(s));
        if (s.Active) Act("Nova compra deste fornecedor", "PrimaryButton", () => NewPurchase(s));
        panel.Children.Add(actions);
    }

    async void Edit(Supplier? existing)
    {
        if (busy) return; busy = true;
        try { if (await SupplierUi.EditAsync(this, store, existing) is Supplier saved) { selected = saved.Id; remoteLoad.Reset(); Refresh(); } }
        finally { busy = false; }
    }

    async void ToggleActive(Supplier s)
    {
        if (busy) return;
        if (s.Active && !AlertWindow.Confirm(this, "Inativar fornecedor", $"\"{s.Name}\" sai da lista de escolha nas compras. O histórico de preços e gastos continua guardado. Inativar?")) return;
        busy = true;
        try
        {
            var copy = System.Text.Json.JsonSerializer.Deserialize<Supplier>(System.Text.Json.JsonSerializer.Serialize(s))!; copy.Active = !s.Active;
            await BusinessUi.ExecuteAsync(store, "supplier.save", s.Id, s, new SupplierArgs(copy), () => store.Update(d => { var text = SupplierBook.Save(d.Prompt12, copy); AuditService.Record(d, App.CurrentUser, copy.Active ? "Fornecedor reativado" : "Fornecedor inativado", "Fornecedor", copy.Id.ToString(), text.Before, text.After, "", copy.Name); return 0; }));
            Toast.Show(this, copy.Active ? "Fornecedor reativado." : "Fornecedor inativado.", tone: "ok"); remoteLoad.Reset(); Refresh();
        }
        catch (Exception ex) { AlertWindow.Info(this, "Não foi possível alterar o fornecedor", ex.Message); }
        finally { busy = false; }
    }

    async void NewPurchase(Supplier s)
    {
        if (busy) return; busy = true;
        try { if (await PurchaseNeedsWindow.CreateNeedAsync(this, store, state, s.Name)) { Toast.Show(this, "Necessidade de compra criada em Compras.", tone: "ok"); remoteLoad.Reset(); Refresh(); } }
        finally { busy = false; }
    }

    void History(Supplier s)
    {
        var list = SupplierBook.Purchases(state, s).OrderByDescending(x => x.Date).ToList();
        var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, HeadersVisibility = DataGridHeadersVisibility.Column, Height = 360, ItemsSource = list.Select(x => new { Data = x.Date.ToString("dd/MM/yyyy", Pt), x.Material, Quantidade = $"{x.Quantity.ToString("0.##", Pt)} {x.Unit}".Trim(), Valor = x.Amount.ToString("C2", Pt), Pagamento = x.PaymentMethod }).ToList() };
        foreach (var (h, b, w) in new[] { ("Data", "Data", 90d), ("Material", "Material", 0), ("Quantidade", "Quantidade", 100), ("Valor", "Valor", 100), ("Pagamento", "Pagamento", 120) })
            grid.Columns.Add(new DataGridTextColumn { Header = h, Binding = new System.Windows.Data.Binding(b), Width = w == 0 ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(w) });
        var body = new StackPanel(); body.Children.Add(DialogKit.Line("Total comprado", list.Sum(x => x.Amount).ToString("C2", Pt), strong: true, top: 0)); body.Children.Add(DialogKit.Rule()); body.Children.Add(list.Count == 0 ? Muted("Nenhuma compra registrada com este fornecedor.", 13) : grid);
        var window = new Window { Title = "Histórico · " + s.Name, Width = Math.Min(760, SystemParameters.WorkArea.Width - 32), SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, Owner = this };
        var close = new Button { Content = "Fechar", Style = S("GhostButton"), IsCancel = true }; close.Click += (_, _) => window.Close();
        window.Content = new DialogFrame { Kind = "Compra", IconName = "history", Title = "Histórico de compras", Subtitle = s.Name, Content = body, Footer = close, ScrollBody = false };
        window.ShowDialog();
    }

    void ComparePrices()
    {
        var materials = SupplierBook.Purchases(state).Select(x => x.Material).Concat(state.PurchaseNeeds.Select(x => x.Material)).Where(x => x.Trim().Length > 0).GroupBy(SupplierBook.Key).Select(g => g.First()).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList();
        var pick = new ComboBox { ItemsSource = materials, IsEditable = true, IsTextSearchEnabled = true }; var result = new StackPanel { Margin = new Thickness(0, 12, 0, 0), MinHeight = 120 };
        void Show()
        {
            result.Children.Clear(); var prices = SupplierBook.PricesFor(state, pick.Text);
            if (prices.Count == 0) { result.Children.Add(Muted(pick.Text.Trim().Length == 0 ? "Escolha um material." : "Nenhuma compra deste material com fornecedor informado.", 13)); return; }
            var best = prices[0].UnitPrice;
            foreach (var p in prices) result.Children.Add(DialogKit.Line($"{p.Supplier} · {p.Date:dd/MM/yyyy}", p.UnitPrice.ToString("C2", Pt) + (p.Unit.Length > 0 ? "/" + p.Unit : "") + (p.UnitPrice == best && prices.Count > 1 ? "  (menor)" : ""), strong: p.UnitPrice == best));
        }
        pick.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(Show); pick.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => Show()));
        var body = new StackPanel(); body.Children.Add(DialogKit.LabeledField("Material", pick).Panel); body.Children.Add(DialogKit.Eyebrow("ÚLTIMO PREÇO UNITÁRIO EM CADA FORNECEDOR")); body.Children.Add(result); Show();
        var window = new Window { Title = "Comparar preços", Width = Math.Min(620, SystemParameters.WorkArea.Width - 32), SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, Owner = this };
        var close = new Button { Content = "Fechar", Style = S("GhostButton"), IsCancel = true }; close.Click += (_, _) => window.Close();
        window.Content = new DialogFrame { Kind = "Compra", IconName = "trending-up", Title = "Comparar preços", Subtitle = "Compras registradas, do mais barato ao mais caro", Content = body, Footer = close, ScrollBody = false };
        window.Loaded += (_, _) => pick.Focus(); window.ShowDialog();
    }
}
