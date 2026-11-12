using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace GraficaCaixa.Wpf;

// UX 2.0 (pacote 3): diálogo laranja "Compra" (necessidade), com resumo ao vivo. Mesmos campos, padrões e validações.
internal sealed class NeedDialog : Window
{
    sealed record PriorityOption(PurchasePriority Value, string Label)
    {
        public override string ToString() => Label;
    }

    public PurchaseNeed? Result;
    readonly PurchaseNeed? editing;
    readonly TextBox material = new() { Tag = "Ex.: Papel couché 300g A3" };
    readonly TextBox quantity = new();
    readonly TextBox unit = new();
    readonly ComboBox priority = new();
    readonly DatePicker buyBy = new();
    readonly TextBox notes = new() { Tag = "Ex.: comprar na loja de sempre; cor exata" };
    readonly SupplierPicker supplier;
    readonly Prompt12State? state;
    readonly TextBlock comparison = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, -6, 0, 10), Visibility = Visibility.Collapsed };
    readonly TextBox requestedBy = new();
    readonly TextBlock sumMaterial = new() { FontWeight = FontWeights.SemiBold, FontSize = 15, TextWrapping = TextWrapping.Wrap }, sumQty = new(), sumBy = new(), sumSupplier = new();
    readonly StatusTag sumPriority = new() { Margin = new Thickness(0, 8, 0, 0) };
    readonly TextBlock error = new() { Visibility = Visibility.Collapsed };

    public NeedDialog(PurchaseNeed? existing = null, Prompt12State? state = null, DataStore? store = null, string? presetSupplier = null)
    {
        editing = existing; this.state = state;
        supplier = new SupplierPicker(state) { ToolTip = "Escolha um fornecedor cadastrado ou digite outro nome" };
        if (store != null) supplier.CreateNew = typed => SupplierUi.EditAsync(this, store, null, typed);
        Title = existing is null ? "Nova necessidade" : "Editar necessidade";
        Width = Math.Min(880, SystemParameters.WorkArea.Width - 32);
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var form = new Grid();
        form.ColumnDefinitions.Add(new ColumnDefinition());
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        form.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < 6; i++) form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddField(form, "Material *", material, 0, 0, 3);
        AddField(form, "Quantidade", quantity, 1, 0);
        AddField(form, "Unidade *", unit, 1, 2);
        priority.ItemsSource = new[]
        {
            new PriorityOption(PurchasePriority.Low, "Baixa"),
            new PriorityOption(PurchasePriority.Normal, "Normal"),
            new PriorityOption(PurchasePriority.High, "Alta"),
            new PriorityOption(PurchasePriority.Urgent, "Urgente")
        };
        AddField(form, "Prioridade", priority, 2, 0);
        AddField(form, "Preciso até", buyBy, 2, 2);
        notes.Height = 84;
        notes.AcceptsReturn = true;
        notes.TextWrapping = TextWrapping.Wrap;
        notes.VerticalContentAlignment = VerticalAlignment.Top;
        notes.Padding = new Thickness(0, 8, 0, 8);
        AddField(form, "Observação", notes, 3, 0, 3);
        AddField(form, "Fornecedor", supplier, 4, 0);
        AddField(form, "Solicitado por *", requestedBy, 4, 2);
        Grid.SetRow(comparison, 5); Grid.SetColumnSpan(comparison, 3); form.Children.Add(comparison);
        var body = new StackPanel();
        body.Children.Add(new SectionCard { Number = "1", Header = "O que comprar", Hint = "só o necessário para acompanhar a compra", Content = form });
        error.Style = (Style)FindResource("FieldError"); body.Children.Add(error);

        var summary = new StackPanel();
        summary.Children.Add(DialogKit.Eyebrow("PARA COMPRAR")); sumMaterial.Margin = new Thickness(0, 4, 0, 2); summary.Children.Add(sumMaterial);
        sumQty.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); summary.Children.Add(sumQty); summary.Children.Add(sumPriority);
        summary.Children.Add(DialogKit.Rule()); summary.Children.Add(DialogKit.Line("Preciso até", sumBy, top: 0)); summary.Children.Add(DialogKit.Line("Fornecedor", sumSupplier));
        summary.Children.Add(DialogKit.Hint("Ao marcar como comprado, você informa quanto pagou. A compra não entra no caixa; aparece no gasto do mês em Compras."));

        Content = new DialogFrame { Kind = "Compra", IconName = "shopping-cart", Title = Title, Subtitle = "Enter avança · Ctrl+Enter salva · Esc fecha", Content = body, Aside = DialogKit.Summary(summary), AsideWidth = 280, Footer = DialogKit.Footer(this, existing is null ? "Salvar necessidade" : "Salvar alterações", Save), ScrollBody = false };
        Fill(existing);
        if (presetSupplier != null) supplier.Value = presetSupplier;
        foreach (var box in new[] { material, quantity, unit }) box.TextChanged += (_, _) => Refresh();
        supplier.Changed += Refresh;
        priority.SelectionChanged += (_, _) => Refresh(); buyBy.SelectedDateChanged += (_, _) => Refresh(); Refresh();
        KeyboardFlow.Attach(this, Save);
        Loaded += (_, _) => material.Focus();
    }

    void Refresh()
    {
        sumMaterial.Text = material.Text.Trim().Length == 0 ? "Material" : material.Text.Trim(); sumMaterial.Opacity = material.Text.Trim().Length == 0 ? .5 : 1;
        sumQty.Text = $"{quantity.Text.Trim()} {unit.Text.Trim()}".Trim();
        var p = (priority.SelectedItem as PriorityOption)?.Value ?? PurchasePriority.Normal;
        sumPriority.Tone = p switch { PurchasePriority.Urgent => "late", PurchasePriority.High => "now", PurchasePriority.Low => "neutral", _ => "ok" };
        sumPriority.Text = (priority.SelectedItem as PriorityOption)?.Label ?? "Normal";
        sumBy.Text = buyBy.SelectedDate?.ToString("ddd, dd/MM/yyyy", CultureInfo.GetCultureInfo("pt-BR")) ?? "Sem prazo";
        sumSupplier.Text = supplier.Value.Length == 0 ? "Não informado" : supplier.Value + (supplier.LinkedId != null ? "" : " (sem cadastro)");
        var prices = state == null ? new List<SupplierPrice>() : SupplierBook.PricesFor(state, material.Text);
        comparison.Visibility = prices.Count > 0 ? Visibility.Visible : Visibility.Collapsed; comparison.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        comparison.Text = "Último preço deste material: " + string.Join(" · ", prices.Take(4).Select(x => $"{x.Supplier} {x.UnitPrice.ToString("C2", CultureInfo.GetCultureInfo("pt-BR"))}{(string.IsNullOrWhiteSpace(x.Unit) ? "" : "/" + x.Unit)} em {x.Date:dd/MM}"));
    }

    static void AddField(Grid grid, string label, Control control, int row, int column, int span = 1)
    {
        var host = DialogKit.LabeledField(label, control).Panel;
        Grid.SetRow(host, row);
        Grid.SetColumn(host, column);
        Grid.SetColumnSpan(host, span);
        grid.Children.Add(host);
    }

    void Fill(PurchaseNeed? need)
    {
        material.Text = need?.Material ?? "";
        quantity.Text = (need?.Quantity ?? 1m).ToString("0.##", CultureInfo.GetCultureInfo("pt-BR"));
        unit.Text = need?.Unit ?? "un";
        priority.SelectedIndex = need?.Priority switch { PurchasePriority.Low => 0, PurchasePriority.High => 2, PurchasePriority.Urgent => 3, _ => 1 };
        buyBy.SelectedDate = need?.BuyBy;
        notes.Text = need?.Notes ?? "";
        supplier.Value = (state != null && need != null ? SupplierBook.Find(state, need.SupplierId, need.Supplier)?.Name : null) ?? need?.Supplier ?? "";
        requestedBy.Text = need?.RequestedBy ?? App.CurrentUser.UserName;
    }

    void Save()
    {
        error.Visibility = Visibility.Collapsed;
        Prompt12Ui.Run(this, () =>
        {
            var materialValue = material.Text.Trim();
            var unitValue = unit.Text.Trim();
            var requestedByValue = requestedBy.Text.Trim();
            if (materialValue.Length == 0) { Mark(material, "Informe o material."); return; }
            if (unitValue.Length == 0) { Mark(unit, "Informe a unidade."); return; }
            if (requestedByValue.Length == 0) { Mark(requestedBy, "Informe quem solicitou."); return; }
            if (priority.SelectedItem is not PriorityOption selectedPriority) throw new InvalidOperationException("Confira a prioridade.");
            Result = new PurchaseNeed
            {
                Id = editing?.Id ?? Guid.NewGuid(),
                Material = materialValue,
                Quantity = Prompt12Ui.Money(quantity, "a quantidade"),
                Unit = unitValue,
                Priority = selectedPriority.Value,
                BuyBy = buyBy.SelectedDate,
                Notes = notes.Text.Trim(),
                Supplier = supplier.Value,
                SupplierId = supplier.LinkedId,
                RequestedBy = requestedByValue,
                Status = editing?.Status ?? PurchaseNeedStatus.ToBuy
            };
            DialogResult = true;
        });
    }
    void Mark(TextBox box, string message)
    {
        error.Text = message; error.Visibility = Visibility.Visible; Ui.SetHasError(box, true); box.Focus();
        void Clear(object? s, TextChangedEventArgs e) { box.TextChanged -= Clear; Ui.SetHasError(box, false); error.Visibility = Visibility.Collapsed; }
        box.TextChanged += Clear;
    }
}
