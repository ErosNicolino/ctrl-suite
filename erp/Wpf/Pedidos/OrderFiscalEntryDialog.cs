using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace GraficaCaixa.Wpf;

internal sealed class OrderFiscalEntryDialog : Window
{
    public FiscalDocument? Result;

    readonly TextBox number = new();
    readonly TextBox amount = new();
    readonly TextBox type = new();
    readonly TextBox notes = new();
    readonly TextBox pdf = new();
    readonly DatePicker date = new() { SelectedDate = DateTime.Today };

    public OrderFiscalEntryDialog(ScheduledOrder order)
    {
        Title = "Registrar NF emitida";
        // UX 2.0 (pacote 3): diálogo simples ardósia; mesmos campos e validação (número e tipo obrigatórios).
        Width = Math.Min(640, SystemParameters.WorkArea.Width - 32);
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        var form = new StackPanel();
        Grid Pair(FrameworkElement x, FrameworkElement y) { var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) }); g.ColumnDefinitions.Add(new ColumnDefinition()); Grid.SetColumn(y, 2); g.Children.Add(x); g.Children.Add(y); return g; }
        type.Tag = "Ex.: NFS-e"; number.Tag = "Número da nota"; amount.TextAlignment = TextAlignment.Right;
        form.Children.Add(Pair(DialogKit.LabeledField("Número *", number).Panel, DialogKit.LabeledField("Tipo do documento *", type).Panel));
        amount.Text = order.OrderTotal.ToString("N2", CultureInfo.GetCultureInfo("pt-BR"));
        form.Children.Add(Pair(DialogKit.LabeledField("Data", date).Panel, DialogKit.LabeledField("Valor (R$)", amount).Panel));
        form.Children.Add(DialogKit.LabeledField("Observação", notes).Panel);
        var pdfRow = new Grid(); pdfRow.ColumnDefinitions.Add(new ColumnDefinition()); pdfRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pdf.IsReadOnly = true; pdf.Tag = "Nenhum PDF selecionado"; pdfRow.Children.Add(pdf);
        var choose = new Button { Content = "Selecionar PDF", Style = (Style)FindResource("SoftButton"), Margin = new Thickness(8, 0, 0, 0) }; choose.Click += (_, _) => SelectPdf(); Grid.SetColumn(choose, 1); pdfRow.Children.Add(choose);
        form.Children.Add(DialogKit.LabeledField("PDF da nota (opcional)", pdfRow).Panel);
        Content = new DialogFrame { Kind = "Recibo", IconName = "file-text", Title = "Registrar NF emitida", Subtitle = $"Pedido #{order.Number} · dados informados no emissor · Ctrl+Enter registra · Esc fecha", Content = form, Footer = DialogKit.Footer(this, "Registrar", () => Save(order)), ScrollBody = false };
        KeyboardFlow.Attach(this, () => Save(order));
        Loaded += (_, _) => number.Focus();
    }
    static void AddField(Panel panel, string label, Control control)
    {
        panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4) });
        panel.Children.Add(control);
    }

    void SelectPdf()
    {
        var picker = new OpenFileDialog { Filter = "PDF (*.pdf)|*.pdf", CheckFileExists = true };
        if (picker.ShowDialog(this) == true) pdf.Text = picker.FileName;
    }

    void Save(ScheduledOrder order)
    {
        Prompt12Ui.Run(this, () =>
        {
            if (string.IsNullOrWhiteSpace(number.Text) || string.IsNullOrWhiteSpace(type.Text))
                throw new InvalidOperationException("Informe número e tipo do documento.");
            Result = new FiscalDocument
            {
                OrderId = order.Id,
                Number = number.Text.Trim(),
                Date = date.SelectedDate ?? DateTime.Today,
                Amount = Prompt12Ui.Money(amount, "o valor"),
                Type = type.Text.Trim(),
                Notes = notes.Text.Trim(),
                PdfPath = pdf.Text.Trim()
            };
            DialogResult = true;
        });
    }
}
