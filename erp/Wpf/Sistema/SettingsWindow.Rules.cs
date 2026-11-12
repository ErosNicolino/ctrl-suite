using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace GraficaCaixa.Wpf;

// Aba "Regras": prazos e calendário (feriados de São Paulo), sinal, faixas de devedores, próximo contato do acompanhamento, valor acima do pedido, cores da margem,
// datas comemorativas, cópias internas e condições do orçamento. Grava pelo comando "rules.save" (mesma permissão das Configurações).
public sealed partial class SettingsWindow
{
    static readonly CultureInfo RulesPt = CultureInfo.GetCultureInfo("pt-BR");
    readonly TextBox ruleOptions = new() { Tag = "0, 1, 3, 5, 7, 10" }, ruleDefault = new() { Width = 90, TextAlignment = TextAlignment.Right }, rulePriority = new() { Width = 90, TextAlignment = TextAlignment.Right };
    readonly CheckBox ruleSaturday = new() { Content = "Sábado conta como dia útil" }, ruleSpHolidays = new() { Content = "Usar os feriados de São Paulo (nacionais, estadual e municipais)" };
    readonly TextBox ruleExtra = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 74, VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(0, 6, 0, 6), Tag = "Ex.: 16/02/2027 (Carnaval), 24/12/2026 (recesso) — um por linha" };
    readonly TextBox ruleWorked = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 54, VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(0, 6, 0, 6), Tag = "Feriado da lista acima em que a gráfica abre — um por linha" };
    readonly TextBlock ruleHolidayList = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBox ruleDeposit = new() { Width = 90, TextAlignment = TextAlignment.Right }, ruleExcessAmount = new() { Width = 90, TextAlignment = TextAlignment.Right }, ruleExcessPercent = new() { Width = 90, TextAlignment = TextAlignment.Right };
    readonly TextBox ruleDebtRecent = new() { Width = 90, TextAlignment = TextAlignment.Right }, ruleDebtOld = new() { Width = 90, TextAlignment = TextAlignment.Right }, ruleBackups = new() { Width = 90, TextAlignment = TextAlignment.Right }, ruleFollowUp = new() { Width = 90, TextAlignment = TextAlignment.Right }, rulePickup = new() { Width = 90, TextAlignment = TextAlignment.Right };
    readonly TextBox ruleMarginGood = new() { Width = 90, TextAlignment = TextAlignment.Right }, ruleMarginLow = new() { Width = 90, TextAlignment = TextAlignment.Right };
    readonly TextBox ruleMeiLimit = new() { Width = 110, TextAlignment = TextAlignment.Right };
    readonly TextBox ruleSaleMultiplier = new() { Width = 90, TextAlignment = TextAlignment.Right }, ruleArtworkFee = new() { Width = 90, TextAlignment = TextAlignment.Right };
    readonly TextBox ruleReseller = new() { Width = 90, TextAlignment = TextAlignment.Right }, ruleCompany = new() { Width = 90, TextAlignment = TextAlignment.Right };
    readonly TextBox ruleSeasonal = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 150, VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(0, 6, 0, 6), VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, ruleSeasonalLead = new() { Width = 90, TextAlignment = TextAlignment.Right };
    readonly TextBox ruleValidity = new() { Width = 90, TextAlignment = TextAlignment.Right }, ruleConditions = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 74, VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(0, 6, 0, 6), MaxLength = 1000 };

    void BuildRulesTab(StackPanel panel)
    {
        var r = original.Rules ?? new BusinessRules();
        Hint(panel, "Regras usadas em todo o ERP e no celular. Os prazos de pedidos já criados não mudam; vale para os novos e quando o prazo recomeça.");
        Section(panel, "Prazos e calendário");
        ruleOptions.Text = string.Join(", ", r.DeadlineOptions); ruleDefault.Text = r.DefaultDeadline.ToString(); rulePriority.Text = r.PriorityDays.ToString();
        panel.Children.Add(DialogKit.LabeledField("Opções de prazo (dias úteis, separadas por vírgula; 0 = imediato)", ruleOptions).Panel);
        panel.Children.Add(DialogKit.LabeledField("Prazo padrão do pedido novo", Row(ruleDefault, "dias úteis (precisa estar entre as opções)")).Panel);
        panel.Children.Add(DialogKit.LabeledField("Prioridade", Row(rulePriority, "dias úteis ou menos até o prazo = pedido prioritário (1 a 30)")).Panel);
        ruleSaturday.IsChecked = r.SaturdayIsBusinessDay; ruleSaturday.Style = TryFindResource("Switch") as Style ?? ruleSaturday.Style; ruleSaturday.Margin = new Thickness(0, 0, 0, 10); panel.Children.Add(ruleSaturday);
        ruleSpHolidays.IsChecked = r.SaoPauloHolidays; ruleSpHolidays.Style = TryFindResource("Switch") as Style ?? ruleSpHolidays.Style; ruleSpHolidays.Margin = new Thickness(0, 0, 0, 6); panel.Children.Add(ruleSpHolidays);
        var holidays = new Border { Style = (Style)FindResource("NoteCalm"), Child = ruleHolidayList, Margin = new Thickness(0, 0, 0, 12) }; panel.Children.Add(holidays);
        ruleSpHolidays.Click += (_, _) => ShowHolidays(); ShowHolidays();
        ruleExtra.Text = string.Join(Environment.NewLine, r.ExtraHolidays.Select(x => x.ToString("dd/MM/yyyy")));
        ruleWorked.Text = string.Join(Environment.NewLine, r.WorkedHolidays.Select(x => x.ToString("dd/MM/yyyy")));
        panel.Children.Add(DialogKit.LabeledField("Outros dias sem expediente (Carnaval, recesso…)", ruleExtra).Panel);
        panel.Children.Add(DialogKit.LabeledField("Feriados em que a gráfica trabalha", ruleWorked).Panel);

        Section(panel, "Pedidos e recebimentos");
        ruleDeposit.Text = r.DepositPercent.ToString("0.##", RulesPt);
        panel.Children.Add(DialogKit.LabeledField("Sinal sugerido", Row(ruleDeposit, "% do saldo (botão \"Sinal\" do pedido)")).Panel);
        ruleExcessAmount.Text = r.ExcessMinAmount.ToString("0.##", RulesPt); ruleExcessPercent.Text = r.ExcessMinPercent.ToString("0.##", RulesPt);
        var excess = new StackPanel { Orientation = Orientation.Horizontal, Children = { Label("acima de R$"), ruleExcessAmount, Label("e acima de"), ruleExcessPercent, Label("% do saldo") } };
        panel.Children.Add(DialogKit.LabeledField("Pedir confirmação reforçada quando o recebido passar do pedido", excess).Panel);
        rulePickup.Text = r.PickupReminderDays.ToString();
        panel.Children.Add(DialogKit.LabeledField("Lembrete de retirada", Row(rulePickup, "dias depois de pronto (botão \"Avisar retiradas\" em Pedidos; 1 a 60)")).Panel);

        Section(panel, "Devedores");
        ruleDebtRecent.Text = r.DebtRecentDays.ToString(); ruleDebtOld.Text = r.DebtOldDays.ToString();
        var bands = new StackPanel { Orientation = Orientation.Horizontal, Children = { Label("até"), ruleDebtRecent, Label("dias  ·  de lá até"), ruleDebtOld, Label("dias  ·  depois disso: cobrar uma última vez ou declarar perda") } };
        panel.Children.Add(DialogKit.LabeledField("Faixas de idade das pendências", bands).Panel);
        ruleFollowUp.Text = r.FollowUpDays.ToString();
        panel.Children.Add(DialogKit.LabeledField("Próximo contato do acompanhamento", Row(ruleFollowUp, "dias depois de registrar um contato (1 a 60)")).Panel);

        Section(panel, "Preço por tipo de cliente");
        ruleReseller.Text = r.ResellerDiscount.ToString("0.##", RulesPt); ruleCompany.Text = r.CompanyDiscount.ToString("0.##", RulesPt);
        var priceTypes = new StackPanel { Orientation = Orientation.Horizontal, Children = { Label("Revenda"), ruleReseller, Label("% de desconto  ·  Empresa"), ruleCompany, Label("% de desconto  ·  Balcão: preço normal") } };
        panel.Children.Add(DialogKit.LabeledField("Desconto automático no lançamento e no pedido (o tipo fica na ficha do cliente; 0 = sem desconto)", priceTypes).Panel);

        Section(panel, "Preço de venda sugerido (Calc venda)");
        ruleSaleMultiplier.Text = r.SaleMultiplier.ToString("0.##", RulesPt); ruleArtworkFee.Text = r.ArtworkFee.ToString("0.00", RulesPt);
        var suggestion = new StackPanel { Orientation = Orientation.Horizontal, Children = { Label("custo ×"), ruleSaleMultiplier, Label("+ arte R$"), ruleArtworkFee, Label("(a arte entra só no preço total da quantidade exata)") } };
        panel.Children.Add(DialogKit.LabeledField("Botão \"Calc venda\" no cadastro do produto (o custo já inclui o frete; nada muda sozinho)", suggestion).Panel);

        Section(panel, "Limite do MEI");
        ruleMeiLimit.Text = r.MeiLimit.ToString("0.00", RulesPt);
        panel.Children.Add(DialogKit.LabeledField("Limite anual, mostrado na Área do proprietário", Row(ruleMeiLimit, "R$ por ano (0 desliga o painel)")).Panel);

        Section(panel, "Margem (cores)");
        ruleMarginGood.Text = r.MarginGood.ToString("0.##", RulesPt); ruleMarginLow.Text = r.MarginLow.ToString("0.##", RulesPt);
        var margin = new StackPanel { Orientation = Orientation.Horizontal, Children = { Label("verde a partir de"), ruleMarginGood, Label("%  ·  âmbar a partir de"), ruleMarginLow, Label("%  ·  abaixo disso: vermelho") } };
        panel.Children.Add(DialogKit.LabeledField("Cor da margem em Produtos, no lucro dos Pedidos e nos Relatórios", margin).Panel);

        Section(panel, "Datas comemorativas");
        ruleSeasonal.Text = string.Join(Environment.NewLine, r.SeasonalDates); ruleSeasonalLead.Text = r.SeasonalLeadDays.ToString();
        panel.Children.Add(DialogKit.LabeledField("Uma por linha: Nome: 12/06  ·  Nome: 2º domingo de maio  ·  Nome: Páscoa", ruleSeasonal).Panel);
        panel.Children.Add(DialogKit.LabeledField("Aviso no Início", Row(ruleSeasonalLead, "dias antes (mostra os mais vendidos e os clientes da mesma época do ano passado; 1 a 90)")).Panel);

        Section(panel, "Orçamento (PDF)");
        ruleValidity.Text = r.BudgetValidityDays.ToString(); ruleConditions.Text = r.BudgetConditions;
        panel.Children.Add(DialogKit.LabeledField("Validade", Row(ruleValidity, "dias (0 = não mostrar validade)")).Panel);
        panel.Children.Add(DialogKit.LabeledField("Condições impressas no orçamento", ruleConditions).Panel);

        Section(panel, "Segurança");
        ruleBackups.Text = r.InternalBackups.ToString();
        panel.Children.Add(DialogKit.LabeledField("Cópias internas do banco", Row(ruleBackups, "versões mais recentes guardadas na pasta do banco (5 a 365)")).Panel);

        void Section(StackPanel p, string title) => p.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("SectionTitle"), Margin = new Thickness(0, 14, 0, 8) });
        void Hint(StackPanel p, string text) => p.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Style = (Style)FindResource("Hint"), Margin = new Thickness(0, 4, 0, 4) });
        StackPanel Row(FrameworkElement box, string text) => new() { Orientation = Orientation.Horizontal, Children = { box, new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), TextWrapping = TextWrapping.Wrap } } };
        static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
    }

    void ShowHolidays()
    {
        if (ruleSpHolidays.IsChecked != true) { ruleHolidayList.Text = "Sem feriados automáticos: só contam os dias cadastrados abaixo."; return; }
        var year = DateTime.Today.Year;
        var list = BusinessRules.SaoPauloHolidaysOf(year).Concat(BusinessRules.SaoPauloHolidaysOf(year + 1)).Where(x => x.Date >= DateTime.Today).OrderBy(x => x.Date).Take(6);
        ruleHolidayList.Text = "Próximos feriados que não contam no prazo:" + string.Concat(list.Select(x => $"\n• {x.Date:dd/MM/yyyy} {x.Name}"));
    }

    // Lê e valida a aba; erro com o campo em foco. Chamado antes de gravar qualquer coisa.
    BusinessRules ReadRules()
    {
        int Int(TextBox box, string what) { if (!int.TryParse(box.Text.Trim(), out var v)) { box.Focus(); throw new InvalidOperationException($"Informe {what} com número inteiro."); } return v; }
        decimal Dec(TextBox box, string what) { if (!decimal.TryParse(box.Text.Trim(), NumberStyles.Number, RulesPt, out var v)) { box.Focus(); throw new InvalidOperationException($"Informe {what} com número."); } return v; }
        List<DateTime> Dates(TextBox box, string what)
        {
            var result = new List<DateTime>();
            foreach (var line in box.Text.Split(['\n', '\r', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var token = line.Split(' ', 2)[0];
                if (!DateTime.TryParseExact(token, ["dd/MM/yyyy", "d/M/yyyy"], RulesPt, DateTimeStyles.None, out var day)) { box.Focus(); throw new InvalidOperationException($"Data inválida em {what}: \"{token}\". Use dd/mm/aaaa."); }
                result.Add(day.Date);
            }
            return result;
        }
        var options = new List<int>();
        foreach (var part in ruleOptions.Text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(part, out var v)) { ruleOptions.Focus(); throw new InvalidOperationException("As opções de prazo devem ser números inteiros separados por vírgula."); }
            options.Add(v);
        }
        var rules = new BusinessRules
        {
            DeadlineOptions = options, DefaultDeadline = Int(ruleDefault, "o prazo padrão"), PriorityDays = Int(rulePriority, "os dias de prioridade"),
            SaturdayIsBusinessDay = ruleSaturday.IsChecked == true, SaoPauloHolidays = ruleSpHolidays.IsChecked == true,
            ExtraHolidays = Dates(ruleExtra, "outros dias sem expediente"), WorkedHolidays = Dates(ruleWorked, "feriados trabalhados"),
            DepositPercent = Dec(ruleDeposit, "o sinal"), ExcessMinAmount = Dec(ruleExcessAmount, "o valor mínimo do excedente"), ExcessMinPercent = Dec(ruleExcessPercent, "o percentual do excedente"),
            DebtRecentDays = Int(ruleDebtRecent, "a primeira faixa de devedores"), DebtOldDays = Int(ruleDebtOld, "a segunda faixa de devedores"), FollowUpDays = Int(ruleFollowUp, "os dias até o próximo contato"), PickupReminderDays = Int(rulePickup, "os dias do lembrete de retirada"),
            BudgetValidityDays = Int(ruleValidity, "a validade do orçamento"), BudgetConditions = ruleConditions.Text, InternalBackups = Int(ruleBackups, "as cópias internas"),
            MarginGood = Dec(ruleMarginGood, "a margem verde"), MarginLow = Dec(ruleMarginLow, "a margem âmbar"),
            ResellerDiscount = Dec(ruleReseller, "o desconto de Revenda"), CompanyDiscount = Dec(ruleCompany, "o desconto de Empresa"),
            MeiLimit = Dec(ruleMeiLimit, "o limite do MEI"), SaleMultiplier = Dec(ruleSaleMultiplier, "o multiplicador do preço de venda"), ArtworkFee = Dec(ruleArtworkFee, "o valor da arte"),
            SeasonalDates = ruleSeasonal.Text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(), SeasonalLeadDays = Int(ruleSeasonalLead, "os dias do aviso das datas comemorativas"),
        };
        rules.Validate();
        return rules;
    }

    async Task SaveRulesAsync(BusinessRules rules)
    {
        var before = original.Rules ?? new BusinessRules();
        if (JsonSerializer.Serialize(before) == JsonSerializer.Serialize(rules)) return;
        await BusinessUi.ExecuteAsync(store, "rules.save", Guid.Empty, before, new RulesArgs(rules), () => store.Update(d =>
        {
            var old = JsonSerializer.Serialize(d.Rules); d.Rules = rules;
            AuditService.Record(d, App.CurrentUser, "Regras alteradas", "Configurações", "global", old, JsonSerializer.Serialize(rules), "", "Prazos, feriados, sinal, devedores, acompanhamento, preço por tipo de cliente, preço sugerido, margem, datas comemorativas, orçamento e cópias internas.");
            return 0;
        }));
        BusinessRules.Current = rules;
    }
}
