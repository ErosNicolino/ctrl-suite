using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text.Json;

namespace GraficaCaixa;

public static class WebQueries
{
    public static SqliteCommand Command(SqliteConnection db, string sql, params (string Name, object? Value)[] values)
    {
        var cmd = db.CreateCommand(); cmd.CommandText = sql;
        foreach (var (name, value) in values) cmd.Parameters.AddWithValue(name, value is Guid guid ? guid.ToString() : value ?? DBNull.Value);
        return cmd;
    }
    public static string Metadata(SqliteConnection db, string key, string fallback = "[]")
    {
        using var cmd = Command(db, "SELECT value FROM metadata WHERE key=@key", ("@key", key));
        return cmd.ExecuteScalar()?.ToString() ?? fallback;
    }
    public static T Json<T>(SqliteConnection db, string key, string fallback = "[]") => JsonSerializer.Deserialize<T>(Metadata(db, key, fallback))!;
    static DateTime Date(SqliteDataReader r, int column) => DateTime.Parse(r.GetString(column), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    static DateTime? OptionalDate(SqliteDataReader r, int column) => r.IsDBNull(column) ? null : Date(r, column);
    static decimal Money(SqliteDataReader r, int column) => decimal.Parse(r.GetString(column), CultureInfo.InvariantCulture);
    static Guid? OptionalGuid(SqliteDataReader r, int column) => r.IsDBNull(column) ? null : Guid.Parse(r.GetString(column));
    public static bool Exists(SqliteConnection db, string table, Guid id)
    {
        // table comes only from the fixed entity mapping in WebFiles.
        using var cmd = Command(db, $"SELECT 1 FROM {table} WHERE id=@id", ("@id", id)); return cmd.ExecuteScalar() != null;
    }
    static string Like(string value) => "%" + value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
    static WebMoney? Finance(ScheduledOrder o, IReadOnlyDictionary<string, bool> p) => p.GetValueOrDefault("orders.value") && p.GetValueOrDefault("finance.receive")
        ? new(o.OrderTotal, o.PaidAmount, o.RemainingAmount, o.PricePending,o.CollectionSurcharge,o.CustomerExtra) : null;
    public static WebOrder OrderDto(ScheduledOrder o, IReadOnlyDictionary<string, bool> p) => new(o.Id, o.Number, o.Revision,
        o.CustomerName, o.Contact, o.Description, o.Stage, o.CreatedAt, o.DueDate == DateTime.MaxValue.Date ? null : o.DueDate, o.IsUncollected, Finance(o, p))
        {Notes=o.Notes,DeadlineStatus=OrderOperations.IsImmediate(o)?"⚡ IMEDIATO":o.IsUncollected?"Arquivado":o.Stage=="Entregue"?"Concluído":o.Stage=="Pronto para retirada"?"Pronto":!o.DeadlineStarted?"Após aprovação":BusinessCalendar.RemainingBusinessDays(DateTime.Today,o.DueDate)<0?"Atrasado":o.DueDate.Date==DateTime.Today?"Hoje":"No prazo"};
    const string OrderSelect = "SELECT id,order_number,revision,customer_name,contact,description,stage,created_at,due_date,is_uncollected,notes,order_total,price_pending,deposit_sale_id,deposit_amount,final_sale_id,artwork_approved_at,sent_to_production_at,production_business_days FROM scheduled_orders";
    static ScheduledOrder ReadOrder(SqliteDataReader r) => new()
    {
        Id = Guid.Parse(r.GetString(0)), Number = r.GetInt64(1), Revision = r.GetInt64(2), CustomerName = r.GetString(3), Contact = r.GetString(4),
        Description = r.GetString(5), Stage = r.GetString(6), CreatedAt = Date(r, 7), DueDate = Date(r, 8), IsUncollected = r.GetBoolean(9),
        Notes = r.GetString(10), OrderTotal = Money(r, 11), PricePending = r.GetBoolean(12), DepositSaleId = OptionalGuid(r, 13),
        DepositAmount = Money(r, 14), FinalSaleId = OptionalGuid(r, 15), ArtworkApprovedAt = OptionalDate(r, 16), SentToProductionAt = OptionalDate(r, 17), ProductionBusinessDays = r.GetInt32(18)
    };
    public static List<ScheduledOrder> ReadOrders(SqliteConnection db, string suffix, params (string Name, object? Value)[] values)
    {
        var items = new List<ScheduledOrder>(); using var cmd = Command(db, OrderSelect + suffix, values);
        using var r = cmd.ExecuteReader(); while (r.Read()) items.Add(ReadOrder(r)); return items;
    }
    public static void Payments(SqliteConnection db, ScheduledOrder order, OrderDetailsIndex? details = null)
    {
        using var cmd = Command(db, "SELECT id,paid_at,amount,payment_type,sale_id FROM order_payments WHERE order_id=@id ORDER BY sort_index", ("@id", order.Id));
        using var r = cmd.ExecuteReader(); while (r.Read()) order.Payments.Add(new() { Id = Guid.Parse(r.GetString(0)), PaidAt = Date(r, 1), Amount = Money(r, 2), PaymentType = r.GetString(3), SaleId = OptionalGuid(r, 4) });
        if(details!=null){details.For(order.Id)?.ApplyPayments(order);return;}
        using var snapshot=Command(db,"SELECT j.value FROM metadata m,json_each(m.value) j WHERE m.key='order_details_v1' AND j.key=@id",("@id",order.Id));if(snapshot.ExecuteScalar() is string saved)JsonSerializer.Deserialize<OrderDetails>(saved)?.ApplyPayments(order);
    }
    public static WebPage<WebOrder> Orders(SqliteConnection db, Dictionary<string, bool> p, WebQuery q)
    {
        var where = " WHERE (@search='' OR customer_name LIKE @like ESCAPE '\\' OR contact LIKE @like ESCAPE '\\' OR description LIKE @like ESCAPE '\\' OR CAST(order_number AS TEXT) LIKE @like ESCAPE '\\' OR EXISTS(SELECT 1 FROM order_items i WHERE i.order_id=scheduled_orders.id AND i.product LIKE @like ESCAPE '\\')) AND (@stage IS NULL OR stage=@stage) AND (@from IS NULL OR created_at>=@from) AND (@to IS NULL OR created_at<@to)";
        (string, object?)[] values = [("@search", q.Search), ("@like", Like(q.Search)), ("@stage", q.Stage), ("@from", q.From?.ToString("O")), ("@to", q.To?.AddDays(1).ToString("O")), ("@limit", q.PageSize), ("@offset", q.Offset)];
        var deadline="(production_business_days=0 OR stage IN ('Aprovado','Produção','Acabamento','Pronto para retirada','Entregue') AND (artwork_approved_at IS NOT NULL OR substr(due_date,1,10)<'9999-12-31'))";
        where+=q.Scope switch{
            "active"=>" AND is_uncollected=0 AND stage NOT IN ('Entregue','Pronto para retirada')",
            "production"=>" AND is_uncollected=0 AND stage<>'Entregue'",
            "completed"=>" AND is_uncollected=0 AND stage='Entregue'",
            "ready"=>" AND is_uncollected=0 AND stage='Pronto para retirada'",
            "overdue"=>$" AND is_uncollected=0 AND stage NOT IN ('Entregue','Pronto para retirada') AND {deadline} AND substr(due_date,1,10)<'{DateTime.Today:yyyy-MM-dd}' AND (strftime('%w',substr(due_date,1,10)) NOT IN ('0','6') OR date(substr(due_date,1,10),'weekday 1')<'{DateTime.Today:yyyy-MM-dd}')",
            "today"=>$" AND is_uncollected=0 AND stage NOT IN ('Entregue','Pronto para retirada') AND {deadline} AND substr(due_date,1,10)='{DateTime.Today:yyyy-MM-dd}'",_=>""};
        using var count = Command(db, "SELECT COUNT(*) FROM scheduled_orders" + where, values); var total = Convert.ToInt64(count.ExecuteScalar());
        var orders = ReadOrders(db, where + $" ORDER BY CASE WHEN is_uncollected=0 AND stage<>'Entregue' AND production_business_days=0 THEN 0 WHEN is_uncollected=0 AND stage NOT IN ('Entregue','Pronto para retirada') AND {deadline} AND substr(due_date,1,10)<'{DateTime.Today:yyyy-MM-dd}' THEN 1 WHEN is_uncollected=0 AND stage NOT IN ('Entregue','Pronto para retirada') AND {deadline} AND substr(due_date,1,10)='{DateTime.Today:yyyy-MM-dd}' THEN 2 ELSE 3 END,CASE WHEN production_business_days=0 THEN created_at ELSE due_date END,order_number,id LIMIT @limit OFFSET @offset", values);
        if (p.GetValueOrDefault("orders.value") && p.GetValueOrDefault("finance.receive") && orders.Count > 0) { var details = new OrderDetailsIndex(db); foreach (var order in orders) Payments(db, order, details); }
        return new(orders.Select(o => OrderDto(o, p)).ToArray(), q.Page, q.PageSize, total);
    }
    public static WebOrderDetail? Order(SqliteConnection db, Dictionary<string, bool> p, Guid id)
    {
        var order = ReadOrders(db, " WHERE id=@id", ("@id", id)).FirstOrDefault(); if (order == null) return null;
        using (var cmd = Command(db, "SELECT id,quantity,product FROM order_items WHERE order_id=@id ORDER BY sort_index", ("@id", id)))
        using (var r = cmd.ExecuteReader()) while (r.Read()) order.Items.Add(new() { Id = Guid.Parse(r.GetString(0)), Quantity = r.GetInt32(1), Product = r.GetString(2) });
        // Extract only the selected JSON entry; do not deserialize the full metadata dictionary.
        using (var cmd = Command(db, "SELECT j.value FROM metadata m,json_each(m.value) j WHERE m.key='order_details_v1' AND j.key=@id", ("@id", id)))
            if (cmd.ExecuteScalar() is string detail) JsonSerializer.Deserialize<OrderDetails>(detail)?.Apply(order);
        var financial = Finance(order, p) != null;
        if (financial) Payments(db, order);
        WebCustomer? customer = null;
        if (p.GetValueOrDefault("view.customers"))
        {
            using var cmd = Command(db, "SELECT id,name,contact FROM customers WHERE name=@name AND contact=@contact ORDER BY id LIMIT 2", ("@name", order.CustomerName), ("@contact", order.Contact));
            using var r = cmd.ExecuteReader(); if (r.Read()) { customer = new(Guid.Parse(r.GetString(0)), r.GetString(1), r.GetString(2)); if (r.Read()) customer = null; }
        }
        return new(OrderDto(order, p), order.Items.Select(i => new WebItem(i.Id, i.ProductId, i.Product, i.Quantity, i.Unit,
            p.GetValueOrDefault("orders.value") ? i.UnitPrice : null, p.GetValueOrDefault("costs.edit") ? i.TotalCost : null)).ToArray(), order.Notes, customer,
            financial ? order.Payments.Where(x => x.SaleId != null).Select(x => new WebPayment(x.Id, x.PaidAt, x.Amount, x.PaymentType)).ToArray() : null,
            p.GetValueOrDefault("costs.edit") ? new(order.HasCosts ? order.TotalCost : null) : null,
            p.GetValueOrDefault("orders.stage") ? new(order.ArtworkApprovedAt, order.SentToProductionAt, order.ProductionBusinessDays) : null);
    }
    public static WebPage<WebCustomer> Customers(SqliteConnection db, WebQuery q)
    {
        const string where = " WHERE @search='' OR name LIKE @like ESCAPE '\\' OR contact LIKE @like ESCAPE '\\'";
        using var count = Command(db, "SELECT COUNT(*) FROM customers" + where, ("@search", q.Search), ("@like", Like(q.Search)));
        var total = Convert.ToInt64(count.ExecuteScalar()); var items = new List<WebCustomer>();
        using var cmd = Command(db, "SELECT id,name,contact FROM customers" + where + " ORDER BY name COLLATE NOCASE,id LIMIT @limit OFFSET @offset", ("@search", q.Search), ("@like", Like(q.Search)), ("@limit", q.PageSize), ("@offset", q.Offset));
        using var r = cmd.ExecuteReader(); while (r.Read()) items.Add(new(Guid.Parse(r.GetString(0)), r.GetString(1), r.GetString(2)));
        return new(items, q.Page, q.PageSize, total);
    }
    public static WebCustomerDetail? Customer(SqliteConnection db, Dictionary<string, bool> p, Guid id)
    {
        using var cmd = Command(db, "SELECT id,name,contact,email,notes,updated_at,cpf_cnpj,address FROM customers WHERE id=@id", ("@id", id));
        using var r = cmd.ExecuteReader(); if (!r.Read()) return null;
        return new(Guid.Parse(r.GetString(0)), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), Date(r, 5),
            p.GetValueOrDefault("customers.edit") ? r.GetString(6) : null, p.GetValueOrDefault("customers.edit") ? r.GetString(7) : null){Revision=CustomerRevision.Read(db,id)};
    }
    public static Product? StoredProduct(SqliteConnection db, Guid id)
    {
        using var cmd = Command(db, "SELECT j.value FROM metadata m,json_each(m.value) j WHERE m.key='products_v1' AND json_extract(j.value,'$.Id')=@id", ("@id", id));
        return cmd.ExecuteScalar() is string text ? JsonSerializer.Deserialize<Product>(text) : null;
    }
    static WebProduct ProductDto(Product x, Dictionary<string, bool> p) => new(x.Id, x.Description, x.Category, x.Unit, x.Active,
        string.IsNullOrWhiteSpace(x.PhotoPath) ? null : $"/api/v1/produtos/{x.Id}/imagem",
        p.GetValueOrDefault("orders.value") ? new(QuantityPricing.BasePrice(x), x.ExactQuantityPrices.Select(y => new WebExactPrice(y.Quantity, y.TotalPrice, p.GetValueOrDefault("costs.edit") ? y.TotalCost : null)).ToArray(),
            x.QuantityPriceTiers.Select(y => new WebPriceTier(y.From, y.Through, y.UnitPrice, p.GetValueOrDefault("costs.edit") ? y.UnitCost : null)).ToArray()) : null,
        p.GetValueOrDefault("costs.edit") ? new(x.Cost) : null, Photos(x));
    static string[]? Photos(Product x) { var all = ProductPhotoNames.All(x); return all.Count > 1 ? all.Select((_, i) => i == 0 ? $"/api/v1/produtos/{x.Id}/imagem" : $"/api/v1/produtos/{x.Id}/imagem/{i}").ToArray() : null; }
    public static WebProduct? Product(SqliteConnection db, Dictionary<string, bool> p, Guid id) => StoredProduct(db, id) is { } x ? ProductDto(x, p) : null;
    public static WebPage<WebProduct> Products(SqliteConnection db, Dictionary<string, bool> p, WebQuery q)
        => Page(Json<List<Product>>(db, "products_v1").OrderBy(x => x.Description, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Id).Select(x => ProductDto(x, p)), q, x => $"{x.Description} {x.Category}");
    public static WebPage<T> Page<T>(IEnumerable<T> source, WebQuery q, Func<T, string> search)
    {
        var items = source.Where(x => q.Search.Length == 0 || search(x).Contains(q.Search, StringComparison.OrdinalIgnoreCase)).ToList();
        return new(q.Offset > int.MaxValue ? [] : items.Skip((int)q.Offset).Take(q.PageSize).ToArray(), q.Page, q.PageSize, items.Count);
    }
    public static List<WebDebt> Debts(SqliteConnection db, Dictionary<string, bool> p)
    {
        // Build just the data required by the existing balance and loss rules, without audit/configuration/products.
        var data = new AppData { ManualDebts = Json<List<ManualDebt>>(db, "manual_debts") };
        using (var prompt = JsonDocument.Parse(Metadata(db, "prompt12_v1", "{}")))
            if (prompt.RootElement.TryGetProperty("Losses", out var losses)) data.Prompt12.Losses = losses.Deserialize<List<ReceivableLoss>>() ?? [];
        var saleIds = data.ManualDebts.SelectMany(x => x.SaleIds).Distinct().ToList();
        var session = new CashSession(); data.Sessions.Add(session);
        // Desempenho (pacote 11): uma leitura das vendas em vez de uma consulta por venda (mesma ordem, mesmos campos).
        var wanted = saleIds.Select(x => x.ToString()).ToHashSet(StringComparer.Ordinal); var found = new Dictionary<string, Sale>(StringComparer.Ordinal);
        using (var cmd = Command(db, "SELECT id,total_value,units,unit_value FROM sales"))
        using (var r = cmd.ExecuteReader()) while (r.Read()) { var key = r.GetString(0); if (wanted.Contains(key)) found.TryAdd(key, new() { Id = Guid.Parse(key), TotalValue = r.IsDBNull(1) ? null : Money(r, 1), Units = r.GetInt32(2), UnitValue = Money(r, 3) }); }
        foreach (var id in saleIds) if (found.TryGetValue(id.ToString(), out var sale)) session.Sales.Add(sale);
        var details = new OrderDetailsIndex(db);
        var items = data.ManualDebts.Where(x => LossOperations.IsNormalReceivable(data, x.Id)).Select(x => new WebDebt(x.Id, "avulso", x.CustomerName, x.Contact, x.Description, x.DebtDate, x.Remaining(data), false){Original=x.Amount,Received=x.Paid(data)}).Where(x => x.Remaining > 0).ToList();
        if (p.GetValueOrDefault("view.orders"))
            foreach (var order in ReadOrders(db, " WHERE stage='Entregue' ORDER BY order_number,id"))
            {
                if (!LossOperations.IsNormalReceivable(data, order.Id)) continue;
                OperationalQueries.Details(details, order);
                Payments(db, order, details);
                if (order.PricePending || order.RemainingAmount > 0) items.Add(new WebDebt(order.Id, "pedido", order.CustomerName, order.Contact, $"Pedido #{order.Number} • {order.Description}", order.DebtDate, order.PricePending ? null : order.RemainingAmount, order.PricePending){Original=order.PricePending?null:order.OrderTotal,Received=order.PaidAmount,DueDate=order.ExpectedPaymentDate});
            }
        var collection=Json<List<CollectionContact>>(db,"collection_contacts");var customerId=p.GetValueOrDefault("view.customers")?OperationalQueries.CustomerIds(db):null;
        return items.Select(x=>x with{CustomerId=customerId?.Invoke(x.CustomerName,x.Contact),Status=collection.Where(c=>c.DebtId==x.Id&&c.IsOrder==(x.Type=="pedido")).OrderByDescending(c=>c.CreatedAt).FirstOrDefault()?.Status??"Pendente"}).OrderBy(x => x.OriginDate).ThenBy(x => x.Type).ThenBy(x => x.Id).ToList();
    }
    public static WebDashboard Dashboard(SqliteConnection db, Dictionary<string, bool> p)
    {
        long? open = null, ready = null;
        if (p.GetValueOrDefault("view.orders"))
        {
            using var cmd = Command(db, "SELECT COUNT(*),COALESCE(SUM(stage='Pronto para retirada'),0) FROM scheduled_orders WHERE stage<>'Entregue' AND is_uncollected=0");
            using var r = cmd.ExecuteReader(); r.Read(); open = r.GetInt64(0); ready = r.GetInt64(1);
        }
        var debts = p.GetValueOrDefault("view.debtors") ? Debts(db, p) : null;
        return new(open, ready, debts?.Count, debts?.Where(x => x.Remaining > 0).Sum(x => x.Remaining!.Value), debts?.Count(x => x.PricePending));
    }
}
