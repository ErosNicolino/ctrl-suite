using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace GraficaCaixa.Wpf;

// Novo / editar lembrete (protótipo): título e detalhes, quando avisar (atalhos + data e horário) e, opcional, cliente com WhatsApp.
// O lembrete rápido da barra (telefone + o que verificar) continua igual.
internal sealed class ReminderDialog : Window
{
    static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    public Reminder? Result;
    readonly Reminder? editing;
    readonly TextBox title = new() { Tag = "Ex.: Cobrar saldo do cliente" }, details = new() { Tag = "Contexto, valores, o que dizer…" };
    readonly DatePicker day = new();
    readonly TextBox hour = new() { Text = "09:00", Width = 90, HorizontalAlignment = HorizontalAlignment.Left };
    readonly CheckBox noDate = new() { Content = "Sem data (só lembrar)", Margin = new Thickness(0, 8, 0, 0) };
    readonly TextBox name = new() { Tag = "Nome" }, phone = new() { Tag = "(11) 90000-0000" };
    readonly WhatsAppButton whatsapp = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
    readonly TextBlock previewTitle = new() { FontWeight = FontWeights.SemiBold, FontSize = 14, TextWrapping = TextWrapping.Wrap }, previewDetails = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12.5 }, previewWhen = new() { FontSize = 12.5, FontWeight = FontWeights.SemiBold };
    readonly DialogKit.Field titleField, hourField, phoneField;
    readonly List<Button> quick = [];

    public ReminderDialog(Reminder? existing = null, string? customerName = null)
    {
        editing = existing;
        Title = existing is null ? "Novo lembrete" : "Editar lembrete";
        Width = Math.Min(980, SystemParameters.WorkArea.Width - 32); SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        details.Height = 70; details.AcceptsReturn = true; details.TextWrapping = TextWrapping.Wrap; details.VerticalContentAlignment = VerticalAlignment.Top; details.Padding = new Thickness(0, 6, 0, 6);
        PhoneInput.Attach(phone);
        titleField = DialogKit.LabeledField("Título *", title); hourField = DialogKit.LabeledField("Horário", hour); phoneField = DialogKit.LabeledField("Telefone / WhatsApp", phone);
        var body = new StackPanel();
        var what = new StackPanel(); what.Children.Add(titleField.Panel); what.Children.Add(DialogKit.LabeledField("Detalhes", details).Panel);
        body.Children.Add(new SectionCard { Number = "1", Header = "O que lembrar", Content = what });

        var when = new StackPanel();
        var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        foreach (var (label, days) in new[] { ("Hoje", 0), ("Amanhã", 1), ("Em 3 dias", 3), ("Semana que vem", 7) })
        { var b = new Button { Content = label, Style = (Style)FindResource("SoftButton"), Margin = new Thickness(0, 0, 8, 0), Tag = days }; b.Click += (_, _) => { noDate.IsChecked = false; day.SelectedDate = DateTime.Today.AddDays(days); Refresh(); }; quick.Add(b); chips.Children.Add(b); }
        when.Children.Add(chips);
        var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) }); row.ColumnDefinitions.Add(new ColumnDefinition());
        var dayPanel = DialogKit.LabeledField("Data", day).Panel; row.Children.Add(dayPanel); Grid.SetColumn(hourField.Panel, 2); row.Children.Add(hourField.Panel);
        when.Children.Add(row); when.Children.Add(noDate);
        body.Children.Add(new SectionCard { Number = "2", Header = "Quando avisar", Hint = "aparece no Início e no contador do menu", Content = when, Margin = new Thickness(0, 12, 0, 0) });

        var who = new Grid(); who.ColumnDefinitions.Add(new ColumnDefinition()); who.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) }); who.ColumnDefinitions.Add(new ColumnDefinition());
        var namePanel = DialogKit.LabeledField("Cliente", name).Panel; who.Children.Add(namePanel); var phonePanel = new StackPanel(); phonePanel.Children.Add(phoneField.Panel); phonePanel.Children.Add(whatsapp); Grid.SetColumn(phonePanel, 2); who.Children.Add(phonePanel);
        body.Children.Add(new SectionCard { Number = "3", Header = "Ligado a um cliente", Hint = "opcional: abre a conversa direto do lembrete", IsOptional = true, Content = who, Margin = new Thickness(0, 12, 0, 0) });

        var preview = new StackPanel();
        preview.Children.Add(DialogKit.Eyebrow("COMO O AVISO VAI APARECER"));
        var card = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(4, 1, 1, 1), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 8, 0, 8) };
        card.SetResourceReference(Border.BackgroundProperty, "Surface"); card.SetResourceReference(Border.BorderBrushProperty, "KindLembrete");
        var cardBody = new StackPanel(); cardBody.Children.Add(previewTitle); previewDetails.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); cardBody.Children.Add(previewDetails); previewWhen.SetResourceReference(TextBlock.ForegroundProperty, "KindLembrete"); previewWhen.Margin = new Thickness(0, 4, 0, 0); cardBody.Children.Add(previewWhen);
        card.Child = cardBody; preview.Children.Add(card);
        preview.Children.Add(DialogKit.Hint("O lembrete aparece no Início e no contador do menu. Concluir tira da lista de pendentes; dá para reabrir depois."));

        Content = new DialogFrame { Kind = "Lembrete", IconName = "bell", Title = Title, Subtitle = "Um aviso para você não esquecer · Enter avança · Ctrl+Enter salva · Esc fecha", Content = body, Aside = DialogKit.Summary(preview), AsideWidth = 300, Footer = DialogKit.Footer(this, "Salvar lembrete", Save), ScrollBody = true };
        Fill(existing, customerName);
        foreach (var box in new[] { title, details, hour, name, phone }) box.TextChanged += (_, _) => Refresh();
        day.SelectedDateChanged += (_, _) => Refresh(); noDate.Click += (_, _) => Refresh(); phone.TextChanged += (_, _) => whatsapp.Phone = phone.Text;
        Refresh();
        KeyboardFlow.Attach(this, Save);
        Loaded += (_, _) => title.Focus();
    }

    void Fill(Reminder? r, string? customerName)
    {
        // Lembrete rápido antigo (sem título): o texto vira o título para editar.
        title.Text = r == null ? "" : r.Title.Length > 0 ? r.Title : r.Description;
        details.Text = r == null || r.Title.Length == 0 ? "" : r.Description;
        name.Text = r?.Name.Length > 0 ? r.Name : customerName ?? "";
        phone.Text = r?.Phone ?? ""; whatsapp.Phone = phone.Text;
        if (r == null) { day.SelectedDate = DateTime.Today; hour.Text = "09:00"; }
        else if (r.DueAt is DateTime due) { day.SelectedDate = due.Date; hour.Text = due.ToString("HH:mm", Pt); }
        else { noDate.IsChecked = true; day.SelectedDate = DateTime.Today; }
    }

    DateTime? Due() => noDate.IsChecked == true || day.SelectedDate is not DateTime d ? null : TimeSpan.TryParse(hour.Text, Pt, out var t) && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1) ? d.Date.Add(t) : d.Date.AddHours(9);

    void Refresh()
    {
        day.IsEnabled = hour.IsEnabled = noDate.IsChecked != true;
        foreach (var b in quick) { var on = noDate.IsChecked != true && day.SelectedDate == DateTime.Today.AddDays((int)b.Tag); b.SetResourceReference(BackgroundProperty, on ? "KindLembreteSoft" : "Surface"); b.SetResourceReference(BorderBrushProperty, on ? "KindLembrete" : "Line"); }
        previewTitle.Text = title.Text.Trim().Length == 0 ? "Título do lembrete" : title.Text.Trim(); previewTitle.Opacity = title.Text.Trim().Length == 0 ? .5 : 1;
        var who = string.Join(" · ", new[] { name.Text.Trim(), PhoneText.Digits(phone.Text).Length >= 10 ? PhoneText.Format(PhoneText.Normalize(phone.Text)) : "" }.Where(x => x.Length > 0));
        previewDetails.Text = string.Join("\n", new[] { details.Text.Trim(), who }.Where(x => x.Length > 0)); previewDetails.Visibility = previewDetails.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        previewWhen.Text = When(Due());
    }

    public static string When(DateTime? due) => due is not DateTime d ? "Sem data" : d.Date == DateTime.Today ? $"Hoje às {d:HH:mm}" : d.Date == DateTime.Today.AddDays(1) ? $"Amanhã às {d:HH:mm}" : d.ToString("ddd, dd/MM 'às' HH:mm", Pt);

    void Save()
    {
        if (title.Text.Trim().Length < 3) { DialogKit.ShowError(titleField, title, "Informe o título do lembrete (mínimo 3 letras)."); return; }
        if (noDate.IsChecked != true && hour.Text.Trim().Length > 0 && !(TimeSpan.TryParse(hour.Text, Pt, out var t) && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1))) { DialogKit.ShowError(hourField, hour, "Horário no formato 09:00."); return; }
        var digits = PhoneText.Digits(phone.Text);
        if (!PhoneText.IsCompleteOrEmpty(phone.Text)) { DialogKit.ShowError(phoneField, phone, "Informe o telefone com DDD ou deixe vazio."); return; }
        var source = editing == null ? new Reminder { CreatedBy = Environment.MachineName } : System.Text.Json.JsonSerializer.Deserialize<Reminder>(System.Text.Json.JsonSerializer.Serialize(editing))!;
        source.Title = title.Text.Trim(); source.Description = details.Text.Trim(); source.DueAt = Due();
        source.Name = name.Text.Trim(); source.Phone = digits.Length == 0 ? "" : PhoneText.Normalize(phone.Text);
        Result = source; DialogResult = true;
    }
}
