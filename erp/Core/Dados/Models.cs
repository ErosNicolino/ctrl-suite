namespace GraficaCaixa;

public class AppData
{
    public List<Product> Products { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? ProductCategories { get; set; }
    // Lembretes (telefone + descrição). Nulo quando vem de um computador com versão antiga: o servidor preserva os atuais.
    public List<Reminder>? Reminders { get; set; }
    public int ProductCostsVersion { get; set; }

    public List<CollectionContact> CollectionContacts { get; set; } = [];
    public List<ManualDebt> ManualDebts { get; set; } = [];
    public List<Prospect> Prospects { get; set; } = [];
    public int FollowUpVersion { get; set; }
    public int OrderDetailsVersion { get; set; }
    public List<CashSession> Sessions { get; set; } = [];
    public List<LostEntry> LostEntries { get; set; } = [];
    public List<ScheduledOrder> ScheduledOrders { get; set; } = [];
    public List<CustomerProfile> Customers { get; set; } = [];
    public List<AuditEntry> AuditTrail { get; set; } = [];
    public List<FinancialCorrection> FinancialCorrections { get; set; } = [];
    public List<CashClosureVersion> CashClosureVersions { get; set; } = [];
    public List<ProductionDeadline>? ProductionDeadlines { get; set; }
    public List<BudgetRule> BudgetRules { get; set; } = [];
    public string SharedFolder { get; set; } = "";
    public string DocumentsRoot { get; set; } = "";
    public Dictionary<string,decimal> PaymentRates { get; set; } = [];
    public string LogoPath { get; set; } = "";
    public CompanyProfile Company { get; set; } = new();
    public BackupSchedule Backup { get; set; } = new();
    // Configurações da calculadora de produção (JSON de CalculatorSettings), iguais para todos os computadores e o celular.
    public string CalculatorSettingsJson { get; set; } = "";
    // Regras de negócio configuráveis (prazos, feriados, sinal, faixas de devedores...): ver BusinessRules.
    public BusinessRules Rules { get; set; } = new();
    public int SettingsVersion { get; set; }
    public string ImprovementNotes { get; set; } = "";
    public long DataVersion { get; set; }
    public int FinancialIntegrityVersion { get; set; }
    public Prompt12State Prompt12 { get; set; } = new();
}

public enum BusinessPermission { ReceivePayment, CorrectFinancialEntry, CancelFinancialEntry, CloseCash, RectifyCash, OwnerFinance }

public sealed record OperationContext(string UserName, string Role)
{
    public IReadOnlyDictionary<string,bool>? Permissions { get; init; }
    public static OperationContext Administrator(string userName) => new(userName, "Administrador");
    public bool Has(BusinessPermission permission) => Permissions!=null ? Permissions.GetValueOrDefault(permission switch {BusinessPermission.ReceivePayment=>"finance.receive",BusinessPermission.CorrectFinancialEntry=>"finance.correct",BusinessPermission.CancelFinancialEntry=>"finance.reverse",BusinessPermission.CloseCash=>"cash.close",BusinessPermission.RectifyCash=>"cash.rectify",_=>"system.owner"}) : Role.Equals("Administrador", StringComparison.OrdinalIgnoreCase)
        || (Role.Equals("Financeiro", StringComparison.OrdinalIgnoreCase) && permission is BusinessPermission.ReceivePayment or BusinessPermission.CloseCash);
    public void Require(BusinessPermission permission)
    {
        if (!Has(permission)) throw new UnauthorizedAccessException($"O usuário {UserName} não possui permissão para {PermissionText(permission)}.");
    }
    static string PermissionText(BusinessPermission permission) => permission switch
    {
        BusinessPermission.ReceivePayment => "registrar pagamentos",
        BusinessPermission.CorrectFinancialEntry => "corrigir lançamentos financeiros",
        BusinessPermission.CancelFinancialEntry => "cancelar lançamentos financeiros",
        BusinessPermission.CloseCash => "fechar o caixa",
        BusinessPermission.RectifyCash => "retificar fechamentos",
        BusinessPermission.OwnerFinance => "acessar dados financeiros privados",
        _ => "realizar esta operação"
    };
}

public class BudgetRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Product { get; set; } = "";
    public decimal SquareMeterRate { get; set; }
    public decimal SizeFixedCost { get; set; }
    public decimal MinimumCost { get; set; }
    public decimal MarkupMultiplier { get; set; } = 2;
    public decimal NoArtworkFee { get; set; }
    public decimal ArtworkFee { get; set; }
    public decimal PriceBySize(decimal width, decimal height, decimal quantity = 1) => width * height * quantity * SquareMeterRate + NoArtworkFee + ArtworkFee;
    public decimal MinimumSale(bool includesArtwork) => MinimumCost * MarkupMultiplier + NoArtworkFee + ArtworkFee;
    // Valor para orçar: o maior entre tamanho e mínimo, arredondado para o real mais perto (216,12 → 216,00; 216,50 → 217,00).
    public decimal Recommended(decimal width, decimal height, decimal quantity = 1) => Math.Round(Math.Max(PriceBySize(width, height, quantity), MinimumSale(true)), 0, MidpointRounding.AwayFromZero);
    // Custo interno: o R$/m² já é o custo × multiplicador, então custo = área × R$/m² ÷ multiplicador, nunca abaixo do custo mínimo.
    // Retirada e arte não são custo. O mínimo vale uma vez por orçamento, como a venda mínima.
    public decimal CostBySize(decimal width, decimal height, decimal quantity = 1) => MarkupMultiplier > 0 ? width * height * quantity * SquareMeterRate / MarkupMultiplier : 0;
    public decimal Cost(decimal width, decimal height, decimal quantity = 1) => Math.Round(Math.Max(CostBySize(width, height, quantity), MinimumCost), 2, MidpointRounding.AwayFromZero);
    public override string ToString() => Product;
}

public static class BudgetDefaults
{
    public static List<BudgetRule> Create() =>
    [
        Rule("Adesivo Normal (Branco / Transparente)",98,75,27,25,30),
        Rule("Adesivo Laminado",142,75,33,25,50),
        Rule("Adesivo Corte Contorno",142,55,33,25,50),
        Rule("Banner e Faixa",114,75,38,25,40),
        Rule("Adesivo Depilado e Mascarado",138,30,120,0,30),
        Rule("Papel Impresso",150,55,35,25,30),
        Rule("Lona Impressa",98,75,28,25,50),
        Rule("Adesivo Corte Contorno Laminado",186,55,38,25,30),
        Rule("Placa de PS 1m adesivado",212,75,40,25,50),
        Rule("Placa de PS 1m adesivado laminado",256,75,45,25,50),
        Rule("Placa de PS 2m adesivado",264,75,40,25,50),
        Rule("Placa de PS 2m adesivado laminado",298,75,45,25,50),
        Rule("Placa de PS 3m adesivado",308,75,40,25,50),
        Rule("Placa de PS 3m adesivado laminado",356,75,45,25,50),
        Rule("Lona Impressão barrada com reforço",136,75,35,25,50),
        Rule("Adesivo Perfurado",130,75,35,25,50)
    ];

    static BudgetRule Rule(string product,decimal rate,decimal fixedCost,decimal minimumCost,decimal noArtworkFee,decimal artworkFee) => new()
    {
        Product=product,SquareMeterRate=rate,SizeFixedCost=fixedCost,MinimumCost=minimumCost,MarkupMultiplier=2,NoArtworkFee=noArtworkFee,ArtworkFee=artworkFee
    };
}

public class ScheduledOrder
{
    public Guid? CustomerId { get; set; }
    public string PaymentCorrectionType { get; set; } = "";
    public List<PaymentPart> PendingPayments { get; set; } = [];
    // Lançamentos do caixa aberto a ligar ao pedido ao salvar (já recebidos; não cria outro lançamento).
    public List<Guid> LinkSaleIds { get; set; } = [];
    public decimal EstimatedFees => Payments.Where(x=>x.SaleId!=null).Sum(x=>x.EstimatedFee);
    public decimal EstimatedNet => PaidAmount - EstimatedFees;
    public Guid Id { get; set; } = Guid.NewGuid();
    public long Number { get; set; }
    public long Revision { get; set; }
    public bool PricePending { get; set; }
    public DateTime? PdfGeneratedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime DueDate { get; set; } = DateTime.MaxValue.Date;
    public DateTime? ArtworkApprovedAt { get; set; }
    public int ProductionBusinessDays { get; set; } = 3;
    public string CustomerName { get; set; } = "";
    public string Contact { get; set; } = "";
    public string Description { get; set; } = "";
    public List<OrderItem> Items { get; set; } = [];
    string stage = OrderStages.Art;
    // Qualquer nome antigo de fase (banco, API, tela em outro computador) entra já convertido para o nome atual.
    public string Stage { get => stage; set => stage = OrderStages.Normalize(value); }
    public decimal OrderTotal { get; set; }
    public bool UseItemPrices { get; set; }
    public decimal? OriginalTotal { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal? FixedDiscount { get; set; }
    public decimal DiscountAmount => FixedDiscount ?? decimal.Round((OriginalTotal ?? OrderTotal) * DiscountPercent / 100, 2, MidpointRounding.AwayFromZero);
    public decimal? FreightCost { get; set; }
    public decimal? AdditionalCost { get; set; }
    public string CostNotes { get; set; } = "";
    public bool IgnoreCosts { get; set; }
    public bool ItemCostsEnabled { get; set; }
    public string CostChangeReason { get; set; } = "";
    public DateTime? DebtOriginDate { get; set; }
    public DateTime DebtDate => (DebtOriginDate ?? CreatedAt).Date;
    public string CostStatus => IgnoreCosts ? "Ignorado" : ItemCostsEnabled && Items.Count > 0 && Items.All(x => x.CostIgnored || x.CostReviewed && x.TotalCost.HasValue) ? "Conferido" : "Pendente";
    public bool HasCosts => CostStatus == "Conferido";
    public decimal ItemsCost => Items.Sum(x => x.TotalCost ?? 0);
    public decimal TotalCost => ItemsCost + (FreightCost ?? 0) + (AdditionalCost ?? 0);
    public decimal? Profit => PricePending || !HasCosts ? null : OrderTotal - TotalCost;
    public string ProfitText => CostStatus != "Conferido" ? CostStatus : Profit?.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")) ?? "Venda a definir";
    public decimal? ProfitMargin => OrderTotal > 0 && Profit.HasValue ? Profit.Value / OrderTotal * 100 : null;
    public bool DepositPaid { get; set; }
    public decimal DepositAmount { get; set; }
    public string DepositPaymentType { get; set; } = "PIX";
    public Guid? DepositSaleId { get; set; }
    public bool FullyPaid { get; set; }
    public string FinalPaymentType { get; set; } = "PIX";
    public Guid? FinalSaleId { get; set; }
    public bool PayAfterDelivery { get; set; }
    public DateTime? ExpectedPaymentDate { get; set; }
    public DateTime? PaymentReceivedAt { get; set; }
    public DateTime? SentToProductionAt { get; set; }
    // Quando entrou em "Pronto para retirada" (o servidor preenche ao mudar a etapa; sai ao mudar para outra). Base do lembrete de retirada.
    public DateTime? ReadyAt { get; set; }
    public bool IsUncollected { get; set; }
    public DateTime? ArchivedAsUncollectedAt { get; set; }
    // Cancelado (≠ arquivado): sai de todas as listas, inclusive Arquivados, e não conta em faturamento nem em a receber. Fica só em Relatórios › Cancelados.
    public DateTime? CancelledAt { get; set; }
    public string CancelReason { get; set; } = "";
    public string CancelledBy { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore] public bool IsCancelled => CancelledAt != null;
    [System.Text.Json.Serialization.JsonIgnore] public bool IsArchived => IsUncollected && CancelledAt == null;
    public string Notes { get; set; } = "";
    public List<OrderPayment> Payments { get; set; } = [];
    public decimal PaidAmount => Payments.Count>0 ? Payments.Where(x=>x.SaleId!=null).Sum(x=>x.Amount) : (DepositSaleId is not null ? DepositAmount : 0) + (FinalSaleId is not null ? Math.Max(0, OrderTotal - DepositAmount) : 0);
    public decimal AppliedAmount=>Payments.Count>0?Payments.Where(x=>x.SaleId!=null).Sum(x=>x.AppliedAmount):PaidAmount;
    public decimal CollectionSurcharge=>Payments.Where(x=>x.SaleId!=null&&x.ExcessKind==PaymentPolicy.CollectionSurcharge).Sum(x=>x.ExcessAmount);
    public decimal CustomerExtra=>Payments.Where(x=>x.SaleId!=null&&x.ExcessKind==PaymentPolicy.CustomerExtra).Sum(x=>x.ExcessAmount);
    public decimal RemainingAmount => Math.Max(0, OrderTotal - AppliedAmount);
    public bool DeadlineStarted => ProductionBusinessDays == 0 || OrderStages.ApprovedOrLater(Stage)
        && (ArtworkApprovedAt is not null || DueDate.Date < DateTime.MaxValue.Date);
    public string ItemsDescription => Items.Count == 0 ? Description : string.Join(" + ", Items.Select(x => $"{x.Quantity} {x.Product}"));

    public void StartProductionDeadline(DateTime approvedAt)
    {
        ArtworkApprovedAt ??= approvedAt;
        DueDate = ProductionBusinessDays == 0 ? DebtDate : BusinessCalendar.AddBusinessDays(ArtworkApprovedAt.Value.Date, ProductionBusinessDays);
    }

    public void ResetProductionDeadline()
    {
        ArtworkApprovedAt = null;
        DueDate = ProductionBusinessDays == 0 ? DebtDate : DateTime.MaxValue.Date;
    }
}

// Regras das datas comemorativas: "dd/MM" (fixa), "Nº <dia da semana> de <mês>" (móvel, ex.: 2º domingo de maio) ou "Páscoa".
public static class SeasonalCalendar
{
    static readonly string[] Months = ["janeiro", "fevereiro", "março", "abril", "maio", "junho", "julho", "agosto", "setembro", "outubro", "novembro", "dezembro"];
    static readonly string[] Weekdays = ["domingo", "segunda", "terça", "quarta", "quinta", "sexta", "sábado"];
    public static bool TryParse(string line, out string name, out string rule)
    {
        name = rule = ""; var parts = (line ?? "").Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || parts[0].Length is 0 or > 60 || parts[1].Length == 0) return false;
        name = parts[0]; rule = parts[1]; return DateOf(rule, 2028) != null;
    }
    public static DateTime? DateOf(string rule, int year)
    {
        var text = rule.Trim().ToLowerInvariant();
        if (text is "páscoa" or "pascoa") return BusinessRules.Easter(year);
        var fixedDate = System.Text.RegularExpressions.Regex.Match(text, @"^(\d{1,2})/(\d{1,2})$");
        if (fixedDate.Success) { int day = int.Parse(fixedDate.Groups[1].Value), month = int.Parse(fixedDate.Groups[2].Value); return month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month) ? new DateTime(year, month, day) : null; }
        var moving = System.Text.RegularExpressions.Regex.Match(text, @"^([1-5])\s*[ºªo°]?\s+(domingo|segunda|terça|terca|quarta|quinta|sexta|sábado|sabado)(?:-feira)?\s+de\s+(\p{L}+)$");
        if (!moving.Success) return null;
        var monthIndex = Array.IndexOf(Months, moving.Groups[3].Value.Replace("marco", "março")); if (monthIndex < 0) return null;
        var weekday = (DayOfWeek)Array.IndexOf(Weekdays, moving.Groups[2].Value.Replace("terca", "terça").Replace("sabado", "sábado"));
        var first = new DateTime(year, monthIndex + 1, 1); var date = first.AddDays(((int)weekday - (int)first.DayOfWeek + 7) % 7 + 7 * (int.Parse(moving.Groups[1].Value) - 1));
        return date.Month == first.Month ? date : null;
    }
}
public class OrderItem
{
    public Guid? ProductId { get; set; }
    public string Unit { get; set; } = "";
    public decimal? UnitCost { get; set; }
    public decimal? EnteredTotalCost { get; set; }
    public bool CostReviewed { get; set; }
    public bool CostIgnored { get; set; }
    public string CostSource { get; set; } = "Manual";
    public decimal? TotalCost => EnteredTotalCost ?? (UnitCost.HasValue ? decimal.Round(Quantity * UnitCost.Value, 2, MidpointRounding.AwayFromZero) : null);
    public decimal? UnitPrice { get; set; }
    public decimal? LineTotal => UnitPrice.HasValue ? decimal.Round(Quantity * UnitPrice.Value, 2) : null;
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Quantity { get; set; } = 1;
    public string Product { get; set; } = "";
}

public static class BusinessCalendar
{
    // Opções oferecidas ao criar/editar pedido (Sistema › Configurações › Regras). Um prazo já gravado é sempre respeitado.
    public static int[] AllowedDeadlines => BusinessRules.Current.DeadlineOptions.ToArray();
    public static DateTime AddBusinessDays(DateTime start, int businessDays)
    {
        if (businessDays is < 0 or > BusinessRules.MaxDeadline) businessDays = BusinessRules.Current.DefaultDeadline;
        var rules = BusinessRules.Current; var result = start.Date;
        for (var remaining = businessDays; remaining > 0;)
        {
            result = result.AddDays(1);
            if (rules.IsBusinessDay(result)) remaining--;
        }
        return result;
    }

    public static int RemainingBusinessDays(DateTime from, DateTime due)
    {
        var start = from.Date; var end = due.Date;
        if (start == end) return 0;
        var rules = BusinessRules.Current; var direction = end > start ? 1 : -1; var count = 0; var cursor = start;
        while (cursor != end)
        {
            cursor = cursor.AddDays(direction);
            if (rules.IsBusinessDay(cursor)) count += direction;
        }
        return count;
    }
}

// Regras de negócio configuráveis (Sistema › Configurações › Regras), gravadas no banco (business_rules_v1) e iguais em todos os
// computadores e no celular. Os valores padrão reproduzem o comportamento anterior, exceto os feriados de São Paulo, que passam a contar.
public sealed class BusinessRules
{
    public const int MaxDeadline = 60;
    public static BusinessRules Current { get; set; } = new();

    public List<int> DeadlineOptions { get; set; } = [0, 1, 3, 5, 7, 10];
    public int DefaultDeadline { get; set; } = 3;
    public int PriorityDays { get; set; } = 3;
    public bool SaturdayIsBusinessDay { get; set; }
    public bool SaoPauloHolidays { get; set; } = true;
    public List<DateTime> ExtraHolidays { get; set; } = [];
    public List<DateTime> WorkedHolidays { get; set; } = [];
    public decimal DepositPercent { get; set; } = 50;
    public int DebtRecentDays { get; set; } = 90;
    public int DebtOldDays { get; set; } = 365;
    public int FollowUpDays { get; set; } = 3;
    public int PickupReminderDays { get; set; } = 3;
    public decimal ExcessMinAmount { get; set; } = 50;
    public decimal ExcessMinPercent { get; set; } = 20;
    public int InternalBackups { get; set; } = 30;
    public string BudgetConditions { get; set; } = "Valores sujeitos à confirmação e aprovação do orçamento. Prazos de produção contam a partir da aprovação da arte.";
    public int BudgetValidityDays { get; set; }
    // Cor da margem em Produtos, Pedidos e Relatórios: verde a partir de MarginGood %, âmbar a partir de MarginLow %, vermelho abaixo.
    public decimal MarginGood { get; set; } = 50;
    public decimal MarginLow { get; set; } = 30;
    public string MarginTone(decimal marginPercent) => marginPercent >= MarginGood ? "COk" : marginPercent >= MarginLow ? "CWarn" : "CDanger";
    // Preço por tipo de cliente: desconto (%) sobre o preço do produto (já com a faixa por quantidade). 0 = sem desconto.
    public static readonly string[] PriceTypes = ["Revenda", "Empresa"];
    public decimal ResellerDiscount { get; set; }
    public decimal CompanyDiscount { get; set; }
    public decimal DiscountFor(string? priceType) => priceType switch { "Revenda" => ResellerDiscount, "Empresa" => CompanyDiscount, _ => 0 };
    // "Calc venda" no cadastro do produto: preço sugerido = custo × SaleMultiplier (frete incluso no custo) + arte (só no preço total da quantidade exata).
    public decimal SaleMultiplier { get; set; } = 2;
    public decimal ArtworkFee { get; set; } = 50;
    // Limite anual do MEI (Área do proprietário): editável porque a lei pode mudar. 0 desliga o painel.
    public decimal MeiLimit { get; set; } = 81000;
    public decimal SuggestedPrice(decimal cost, bool withArtwork) => decimal.Round(cost * SaleMultiplier + (withArtwork ? ArtworkFee : 0), 2, MidpointRounding.AwayFromZero);
    // Datas comemorativas (aviso no Início com sugestões do ano passado): "Nome: regra", regra = dd/MM, "2º domingo de maio" ou "Páscoa".
    public List<string> SeasonalDates { get; set; } = ["Volta às aulas: 01/02", "Páscoa: Páscoa", "Dia das Mães: 2º domingo de maio", "Dia dos Namorados: 12/06", "Dia dos Pais: 2º domingo de agosto", "Dia das Crianças: 12/10", "Dia dos Professores: 15/10", "Natal: 25/12"];
    public int SeasonalLeadDays { get; set; } = 30;
    // Próxima data comemorativa entre hoje e hoje + SeasonalLeadDays (null se nenhuma).
    public (string Name, DateTime Date, string Rule)? UpcomingSeason(DateTime today)
    {
        today = today.Date; (string Name, DateTime Date, string Rule)? best = null;
        foreach (var line in SeasonalDates)
            if (SeasonalCalendar.TryParse(line, out var name, out var rule))
                foreach (var year in new[] { today.Year, today.Year + 1 })
                    if (SeasonalCalendar.DateOf(rule, year) is DateTime date && date >= today && date <= today.AddDays(SeasonalLeadDays) && (best == null || date < best.Value.Date)) best = (name, date, rule);
        return best;
    }    public static decimal ApplyDiscount(decimal unitPrice, decimal percent) => percent <= 0 ? unitPrice : Math.Round(unitPrice * (100 - percent) / 100, 4, MidpointRounding.AwayFromZero);

    public void Validate()
    {
        DeadlineOptions = DeadlineOptions.Distinct().OrderBy(x => x).ToList();
        if (DeadlineOptions.Count == 0 || DeadlineOptions.Any(x => x is < 0 or > MaxDeadline)) throw new InvalidOperationException($"Informe as opções de prazo entre 0 e {MaxDeadline} dias úteis.");
        if (!DeadlineOptions.Contains(DefaultDeadline)) throw new InvalidOperationException("O prazo padrão precisa estar entre as opções de prazo.");
        if (PriorityDays is < 1 or > 30) throw new InvalidOperationException("A prioridade deve ficar entre 1 e 30 dias úteis.");
        if (DepositPercent is <= 0 or > 100) throw new InvalidOperationException("O sinal deve ficar entre 1% e 100%.");
        if (DebtRecentDays < 1 || DebtOldDays <= DebtRecentDays || DebtOldDays > 3650) throw new InvalidOperationException("As faixas de devedores precisam ser crescentes (ex.: 90 e 365 dias).");
        if (ExcessMinAmount < 0 || ExcessMinPercent is < 0 or > 1000) throw new InvalidOperationException("Confira os limites do valor acima do pedido.");
        if (FollowUpDays is < 1 or > 60) throw new InvalidOperationException("O próximo contato do acompanhamento deve ficar entre 1 e 60 dias.");
        if (PickupReminderDays is < 1 or > 60) throw new InvalidOperationException("O lembrete de retirada deve ficar entre 1 e 60 dias.");
        if (InternalBackups is < 5 or > 365) throw new InvalidOperationException("Guarde entre 5 e 365 cópias internas.");
        if (BudgetValidityDays is < 0 or > 365) throw new InvalidOperationException("A validade do orçamento deve ficar entre 0 e 365 dias.");
        if (ResellerDiscount is < 0 or > 90 || CompanyDiscount is < 0 or > 90) throw new InvalidOperationException("O desconto por tipo de cliente deve ficar entre 0% e 90%.");
        if (MarginLow is < 0 or > 100 || MarginGood is <= 0 or > 100 || MarginLow >= MarginGood) throw new InvalidOperationException("As faixas de margem precisam ser crescentes, entre 0% e 100% (ex.: 30% e 50%).");
        if (SaleMultiplier is < 1 or > 20) throw new InvalidOperationException("O multiplicador do preço de venda deve ficar entre 1 e 20.");
        if (MeiLimit is < 0 or > 10000000) throw new InvalidOperationException("O limite do MEI deve ficar entre R$ 0 e R$ 10.000.000.");
        if (ArtworkFee is < 0 or > 10000) throw new InvalidOperationException("O valor da arte deve ficar entre R$ 0 e R$ 10.000.");
        if (SeasonalLeadDays is < 1 or > 90) throw new InvalidOperationException("O aviso das datas comemorativas deve ficar entre 1 e 90 dias antes.");
        SeasonalDates = (SeasonalDates ?? []).Select(x => x.Trim()).Where(x => x.Length > 0).ToList(); if (SeasonalDates.Count > 40) throw new InvalidOperationException("Cadastre até 40 datas comemorativas.");
        foreach (var line in SeasonalDates) if (!SeasonalCalendar.TryParse(line, out _, out _)) throw new InvalidOperationException($"Data comemorativa inválida: \"{line}\". Use Nome: 12/06, Nome: 2º domingo de maio ou Nome: Páscoa.");
        BudgetConditions = (BudgetConditions ?? "").Trim(); if (BudgetConditions.Length > 1000) throw new InvalidOperationException("As condições do orçamento podem ter até 1000 caracteres.");
        ExtraHolidays = ExtraHolidays.Select(x => x.Date).Distinct().OrderBy(x => x).ToList(); WorkedHolidays = WorkedHolidays.Select(x => x.Date).Distinct().OrderBy(x => x).ToList();
    }

    public bool IsBusinessDay(DateTime day)
    {
        day = day.Date;
        if (day.DayOfWeek == DayOfWeek.Sunday || day.DayOfWeek == DayOfWeek.Saturday && !SaturdayIsBusinessDay) return false;
        return !IsHoliday(day);
    }
    public bool IsHoliday(DateTime day)
    {
        day = day.Date;
        if (ExtraHolidays.Contains(day)) return true;
        return SaoPauloHolidays && !WorkedHolidays.Contains(day) && SaoPauloHolidaysOf(day.Year).Any(x => x.Date == day);
    }

    // Feriados que valem na cidade de São Paulo: nacionais (inclui Sexta-feira Santa e Consciência Negra), estadual (09/07) e municipais (25/01 e Corpus Christi).
    public static IEnumerable<(DateTime Date, string Name)> SaoPauloHolidaysOf(int year)
    {
        var easter = Easter(year);
        yield return (new(year, 1, 1), "Confraternização Universal");
        yield return (new(year, 1, 25), "Aniversário de São Paulo");
        yield return (easter.AddDays(-2), "Sexta-feira Santa");
        yield return (new(year, 4, 21), "Tiradentes");
        yield return (new(year, 5, 1), "Dia do Trabalho");
        yield return (easter.AddDays(60), "Corpus Christi");
        yield return (new(year, 7, 9), "Revolução Constitucionalista");
        yield return (new(year, 9, 7), "Independência do Brasil");
        yield return (new(year, 10, 12), "Nossa Senhora Aparecida");
        yield return (new(year, 11, 2), "Finados");
        yield return (new(year, 11, 15), "Proclamação da República");
        yield return (new(year, 11, 20), "Consciência Negra");
        yield return (new(year, 12, 25), "Natal");
    }
    // Domingo de Páscoa (algoritmo de Meeus/Jones/Butcher, calendário gregoriano).
    public static DateTime Easter(int year)
    {
        int a = year % 19, b = year / 100, c = year % 100, d = b / 4, e = b % 4, f = (b + 8) / 25, g = (b - f + 1) / 3, h = (19 * a + b - d - g + 15) % 30;
        int i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7, m = (a + 11 * h + 22 * l) / 451, month = (h + l - 7 * m + 114) / 31, day = (h + l - 7 * m + 114) % 31 + 1;
        return new DateTime(year, month, day);
    }
}

public class OrderPayment
{
    public decimal ExcessAmount { get; set; }
    public string ExcessKind { get; set; } = "";
    public bool ExcessConfirmed { get; set; }
    public Guid? OperationId { get; set; }
    public decimal AppliedAmount=>Amount-ExcessAmount;
    public decimal? FeePercent { get; set; }
    public decimal EstimatedFee => PaymentPolicy.Fee(Amount, FeePercent ?? PaymentPolicy.HistoricalRate(PaymentType));
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime PaidAt { get; set; } = DateTime.Now;
    public decimal Amount { get; set; }
    public string PaymentType { get; set; } = "PIX";
    public Guid? SaleId { get; set; }
    public string Note { get; set; } = "";
}

public class CustomerProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Contact { get; set; } = "";
    public string Email { get; set; } = "";
    public string CpfCnpj { get; set; } = "";
    public string Address { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    // Tipo de preço: "" (Balcão), "Revenda" ou "Empresa". O desconto de cada tipo fica em Configurações › Regras.
    public string PriceType { get; set; } = "";
    // Nomes dos cadastros unidos a este em Mesclar, um por linha: os lançamentos antigos sem telefone continuam contando aqui.
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? MergedNames { get; set; }
    public IEnumerable<string> AllNames()=>new[]{Name}.Concat((MergedNames??"").Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)).Where(x=>x.Length>0);
}

public static class DataRelations
{
    public static void RequireUnchanged<T>(T current, T original)
    {
        if (System.Text.Json.JsonSerializer.Serialize(current) != System.Text.Json.JsonSerializer.Serialize(original))
            throw new InvalidOperationException("Este registro mudou em outro computador. Atualize a tela e confira os valores antes de tentar novamente.");
    }
    public static void SyncEditedPayment(AppData data, Sale sale)
    {
        foreach (var order in data.ScheduledOrders)
        {
            if (order.DepositSaleId == sale.Id) { order.DepositAmount = sale.Total; order.DepositPaid = true; }
            if (order.FinalSaleId == sale.Id) order.FullyPaid = true;
            var payment=order.Payments.FirstOrDefault(x=>x.SaleId==sale.Id);if(payment!=null)payment.Amount=sale.Total;
            if (payment != null) { OrderOperations.RefreshPaymentState(order); order.Revision++; }
        }
    }

    public static void DetachRemovedPayments(AppData data, IEnumerable<Guid> saleIds)
    {
        var ids = saleIds.ToHashSet();
        foreach (var order in data.ScheduledOrders)
        {
            var changed = order.Payments.Any(x=>x.SaleId is Guid id && ids.Contains(id)) || (order.DepositSaleId is Guid dep && ids.Contains(dep)) || (order.FinalSaleId is Guid fin && ids.Contains(fin));
            if (order.DepositSaleId is Guid depositId && ids.Contains(depositId)) { order.DepositSaleId = null; order.DepositPaid = false; }
            if (order.FinalSaleId is Guid finalId && ids.Contains(finalId)) { order.FinalSaleId = null; order.FullyPaid = false; order.PaymentReceivedAt = null; }
            foreach(var payment in order.Payments.Where(x=>x.SaleId is Guid id&&ids.Contains(id)))payment.SaleId=null;
            if(changed) { OrderOperations.RefreshPaymentState(order); order.Revision++; }
        }
    }
}

public class LostEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime EntryDate { get; set; } = DateTime.Now;
    public string CustomerName { get; set; } = "";
    public string Contact { get; set; } = "";
    public string Description { get; set; } = "";
    public string PaymentType { get; set; } = "";
    public decimal Amount { get; set; }
}

public class AuditEntry
{
    public string UserId { get; set; } = "";
    public string UserLogin { get; set; } = "";
    public string SessionId { get; set; } = "";
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Action { get; set; } = "";
    public string Description { get; set; } = "";
    public string Computer { get; set; } = Environment.MachineName;
    public Guid? SaleId { get; set; }
    public string UserName { get; set; } = "Sistema";
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string OldValue { get; set; } = "";
    public string NewValue { get; set; } = "";
    public string Reason { get; set; } = "";
}

public sealed class FinancialCorrection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OriginalSaleId { get; set; }
    public Guid AdjustmentSaleId { get; set; }
    public Guid? OrderId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string UserName { get; set; } = "";
    public string CorrectionType { get; set; } = "Ajuste de valor";
    public string Reason { get; set; } = "";
    public decimal OriginalAmount { get; set; }
    public decimal CorrectedAmount { get; set; }
    public decimal Difference => CorrectedAmount - OriginalAmount;
}

public sealed class CashClosureVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public int Version { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string UserName { get; set; } = "";
    public string Reason { get; set; } = "";
    public bool IsOriginal { get; set; }
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public decimal OpeningAmount { get; set; }
    public decimal? DeclaredClosingAmount { get; set; }
    public decimal SalesTotal { get; set; }
    public decimal ExpectedCash { get; set; }
    public string SnapshotJson { get; set; } = "";
}

public class BackupInfo
{
    public string Path { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public long Size { get; set; }
    public string FileName => System.IO.Path.GetFileName(Path);
}

public class CashSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime OpenedAt { get; set; } = DateTime.Now;
    public DateTime? ClosedAt { get; set; }
    public decimal OpeningAmount { get; set; }
    public decimal? DeclaredClosingAmount { get; set; }
    public string OpenedBy { get; set; } = "Gráfica";
    public string ClosedBy { get; set; } = "";
    public List<Sale> Sales { get; set; } = [];
    public List<CashWithdrawal> Withdrawals { get; set; } = [];
    public decimal WithdrawalsTotal => Withdrawals.Sum(x=>x.Amount);
    public List<CashAdjustment> Adjustments { get; set; } = [];
    public decimal AdjustmentsTotal => Adjustments.Sum(x=>x.Amount);
    public bool IsOpen => ClosedAt is null;
    public decimal SalesTotal => Sales.Sum(s => s.Total);
    public decimal ExpectedCash => OpeningAmount + Sales.Where(s => s.PaymentType == "Dinheiro").Sum(s => s.Total) - WithdrawalsTotal + AdjustmentsTotal;
}

public class Sale
{
    public List<OrderItem> Items { get; set; } = [];
    public List<PaymentPart> PendingPayments { get; set; } = [];
    public Guid? PaymentGroupId { get; set; }
    public decimal? ItemsTotal { get; set; }
    public string Notes { get; set; } = "";
    public decimal? FeePercent { get; set; }
    public decimal EstimatedFee => PaymentPolicy.Fee(Total, FeePercent ?? PaymentPolicy.HistoricalRate(PaymentType));
    public Guid? ProductId { get; set; }
    public decimal? SuggestedUnitCost { get; set; }
    public string ProductUnit { get; set; } = "";
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string CustomerName { get; set; } = "";
    public string Contact { get; set; } = "";
    public string Product { get; set; } = "";
    public int Units { get; set; }
    public string PaymentType { get; set; } = "";
    public decimal UnitValue { get; set; }
    public decimal? TotalValue { get; set; }
    public decimal Total => TotalValue ?? Units * UnitValue;
}

public static class DisplayText
{
    public static string Customer(Sale sale)
    {
        var name = Clean(sale.CustomerName);
        var contact = PhoneText.Format(Clean(sale.Contact));
        if (string.IsNullOrWhiteSpace(name)) return string.IsNullOrWhiteSpace(contact) ? "Não informado" : contact;
        if (string.IsNullOrWhiteSpace(contact) || name.Contains(contact, StringComparison.CurrentCultureIgnoreCase)) return name;
        return $"{name} • {contact}";
    }

    static string Clean(string? value) => string.IsNullOrWhiteSpace(value) || value.Equals("Não informado", StringComparison.CurrentCultureIgnoreCase) ? "" : value.Trim();
}

public class ManualDebt
{
    public Guid? CustomerId { get; set; }
    public List<OrderItem> Items { get; set; } = [];
    public DateTime? OriginDate { get; set; }
    public DateTime DebtDate => (OriginDate ?? CreatedAt).Date;
    public int DaysOpen => Math.Max(0, (DateTime.Today - DebtDate).Days);
    public string InitialCollectionStatus { get; set; } = "Não contatado";
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CustomerName { get; set; } = "";
    public string Contact { get; set; } = "";
    public string Description { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public decimal Amount { get; set; }
    public List<Guid> SaleIds { get; set; } = [];
    // Preenchidos quando o lançamento de origem é cancelado: a conta deixa de ser cobrada e some de Devedores.
    // Omitidos quando vazios: servidores antigos (que recusam campos desconhecidos) continuam aceitando a conta.
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? CancelledAt { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? CancelReason { get; set; }
    // Valor recebido acima do total no lançamento (cliente deixou o troco), confirmado e classificado como nos pedidos.
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public decimal? CollectionSurcharge { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public decimal? CustomerExtra { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public decimal Excess => (CollectionSurcharge??0)+(CustomerExtra??0);
    // Correção, devolução ou itens novos reduzem o excedente para o que ainda passa do total (primeiro o valor adicional).
    public void TrimExcess(decimal paid)
    {
        var over=Excess-Math.Max(0,paid-Amount);if(over<=0)return;
        var cut=Math.Min(over,CustomerExtra??0);CustomerExtra=CustomerExtra-cut is >0 and var e?e:null;over-=cut;
        if(over>0)CollectionSurcharge=CollectionSurcharge-over is >0 and var s?s:null;
    }
    public bool IsCancelled => CancelledAt.HasValue;
    public decimal Paid(AppData data) => data.Sessions.SelectMany(x=>x.Sales).Where(x=>SaleIds.Contains(x.Id)).Sum(x=>x.Total);
    public decimal Remaining(AppData data) => IsCancelled || WasReversed(data) ? 0 : Math.Max(0, Amount - Paid(data));
    // Lançamentos cancelados antes desta correção: todos os pagamentos do grupo foram zerados por correção financeira.
    public bool WasReversed(AppData data)
    {
        var adjustments=data.FinancialCorrections.Select(c=>c.AdjustmentSaleId).ToHashSet();
        var group=data.Sessions.SelectMany(x=>x.Sales).Where(x=>x.PaymentGroupId==Id&&!adjustments.Contains(x.Id)).Select(x=>x.Id).ToList();
        return group.Count>0 && Paid(data)==0 && group.All(id=>data.FinancialCorrections.Where(c=>c.OriginalSaleId==id).OrderBy(c=>c.CreatedAt).LastOrDefault()?.CorrectedAmount==0);
    }
    public bool IsClosedByCancellation(AppData data) => IsCancelled || WasReversed(data);
}
public class Prospect
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CustomerName { get; set; } = "";
    public string Contact { get; set; } = "";
    public string Description { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Status { get; set; } = "Aguardando retorno";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? LastContactAt { get; set; }
    public DateTime? NextContactAt { get; set; }
    public Guid? OrderId { get; set; }
    // Valor do orçamento (opcional): soma em "Orçamentos em acompanhamento", vai na mensagem de retorno e no pedido ao converter.
    public decimal? Amount { get; set; }
}

public sealed class CashWithdrawal
{
    public string UserName { get; set; } = "";
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public decimal Amount { get; set; }
    public string Reason { get; set; } = "";
}

// Correção do dinheiro esperado na gaveta: positivo aumenta, negativo diminui. Não é venda, recebimento, despesa nem sangria.
public sealed class CashAdjustment
{
    public string UserName { get; set; } = "";
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public decimal Amount { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class CollectionContact
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DebtId { get; set; }
    public bool IsOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string Status { get; set; } = "";
    public string Notes { get; set; } = "";
}

// Lembrete rápido: alguém que ainda não foi lançado no sistema, para verificar depois na conversa do WhatsApp.
public class Reminder
{
    public Dictionary<Guid,DateTime> ReadByUsers { get; set; } = [];
    public Guid Id { get; set; } = Guid.NewGuid();
    // Novo lembrete (protótipo): título, quando avisar e, opcional, cliente/telefone. Lembretes rápidos antigos só têm telefone e descrição.
    public string Title { get; set; } = "";
    public DateTime? DueAt { get; set; }
    public string Phone { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string CreatedBy { get; set; } = "";
    public DateTime? DoneAt { get; set; }
}

public class ProductionDeadline
{
    public Guid Id {get;set;}=Guid.NewGuid();
    public string Description {get;set;}="";
    public string DeadlineText {get;set;}="";
    public string Note {get;set;}="";
    public int Order {get;set;}
    public bool Active {get;set;}=true;
    public long Revision {get;set;}=1;
    public string Status=>Active?"Ativo":"Inativo";
}
public static class ProductionDeadlineDefaults
{
    public static List<ProductionDeadline> Create()
    {
        var rows=new (string,string,string)[]{("Panfletos","5 dias",""),("Cartão de visita","5 dias",""),("Cartão com verniz e bordas arredondadas","7 dias",""),("Banner","3 dias","Prazo pode ser negociado"),("Vinil","3 dias",""),("Fotográfico e couché","De um dia para o outro",""),("DTF - folha completa","2 dias",""),("Envelope","5 a 7 dias",""),("Bloco de 2 vias","5 dias",""),("Calendário","5 a 7 dias",""),("Foto 70 × 70 (mínimo)","3 dias",""),("DTF pequeno","Prazo a confirmar","Aguardando a entrada de mais material para completar a folha. Formatos: UV A3; Têxtil 58 × 50.")};
        return rows.Select((r,i)=>new ProductionDeadline{Id=Guid.Parse($"cac80000-0000-4000-8000-{i+1:000000000000}"),Description=r.Item1,DeadlineText=r.Item2,Note=r.Item3,Order=(i+1)*10}).ToList();
    }
}

// Dados da gráfica impressos nos PDFs (recibos, orçamentos). Os valores padrão são os da instalação original.
public sealed record CompanyProfile
{
    public string Name { get; set; } = "Gráfica Rápida CtrlA CtrlP";
    public string Document { get; set; } = "11.222.333/0001-81";
    public string Address { get; set; } = "Rua Exemplo, 100 - Centro";
    public string Phone { get; set; } = "11 98765-1002";
    public string Email { get; set; } = "contato@grafica.example";
    public string City { get; set; } = "São Paulo";
    // Chave Pix da gráfica (CPF/CNPJ, e-mail, +55 telefone ou chave aleatória): gera o "Pix copia e cola" com o valor nas mensagens do WhatsApp.
    public string PixKey { get; set; } = "";
    // Link "avaliar no Google" (Perfil da Empresa no Google › Pedir avaliações). Aparece na página do pedido depois de entregue.
    public string ReviewUrl { get; set; } = "";
    // Horário de funcionamento, uma linha por período (aparece na página do pedido, embaixo do endereço).
    public string OpeningHours { get; set; } = "Segunda a sexta: 9h às 18h\nSábado: 9h às 13h";
    // Carimbo dos PDFs: true + StampPath vazio = carimbo original do programa; true + StampPath = imagem própria (PNG/JPEG na pasta compartilhada); false = sem carimbo.
    public bool ShowStamp { get; set; } = true;
    public string StampPath { get; set; } = "";
}

// Backup programado (feito pelo serviço do computador principal). Destino é uma pasta vista pelo PC principal (HD externo, pendrive, OneDrive…).
// Mode: "intervalo" (a cada IntervalHours), "diario" (todo dia em Time) ou "semanal" (nos Days, em Time). KeepCopies: quantas cópias manter no destino.
public sealed record BackupSchedule
{
    public bool Enabled { get; set; }
    public string Destination { get; set; } = "";
    public string Mode { get; set; } = "diario";
    public int IntervalHours { get; set; } = 6;
    public string Time { get; set; } = "19:00";
    public List<int> Days { get; set; } = [1,2,3,4,5,6];
    public int KeepCopies { get; set; } = 30;
    public bool Equals(BackupSchedule? other)=>other is not null&&Enabled==other.Enabled&&Destination==other.Destination&&Mode==other.Mode&&IntervalHours==other.IntervalHours&&Time==other.Time&&KeepCopies==other.KeepCopies&&Days.SequenceEqual(other.Days);
    public override int GetHashCode()=>HashCode.Combine(Enabled,Destination,Mode,IntervalHours,Time,KeepCopies,Days.Count);
}

// Resultado do último backup programado (gravado só pelo serviço).
public sealed record BackupStatus(DateTime? LastAttemptAt,DateTime? LastSuccessAt,string LastFile,string LastError,DateTime? NextRunAt,int FilesCopied=0,int FilesTotal=0,string FilesError="");
