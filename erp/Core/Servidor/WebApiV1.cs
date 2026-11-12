using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;

namespace GraficaCaixa;

// Server configuration only. No physical reference is accepted from an HTTP caller.
public sealed record WebApiOptions
{
    public BrowserOptions Browser { get; init; } = new();
    public string[] AllowedOrigins { get; init; } = [];
    public string DocumentsDirectory { get; init; } = "";
    // Fotos de produto (ERP\Produtos\Imagens): pasta do ERP no servidor; vazio = a mesma dos documentos.
    public string PhotosDirectory { get; init; } = "";
    [System.Text.Json.Serialization.JsonIgnore] public string PhotosRoot => PhotosDirectory.Length > 0 ? PhotosDirectory : DocumentsDirectory;
    public WebDocumentBinding[] Documents { get; init; } = [];
    public void Validate()
    {
        Browser.Validate();
        foreach (var origin in AllowedOrigins)
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
                uri.GetLeftPart(UriPartial.Authority) != origin || uri.UserInfo.Length > 0)
                throw new InvalidOperationException("Origem Web deve ser HTTPS, exata e sem caminho.");
        if (DocumentsDirectory.Length > 0 && (!Path.IsPathFullyQualified(DocumentsDirectory) ||
            DocumentsDirectory.StartsWith(@"\\") || new DriveInfo(Path.GetPathRoot(DocumentsDirectory)!).DriveType == DriveType.Network))
            throw new InvalidOperationException("DocumentsDirectory deve ser uma pasta local do servidor.");
        if (PhotosDirectory.Length > 0 && (!Path.IsPathFullyQualified(PhotosDirectory) ||
            PhotosDirectory.StartsWith(@"\\") || new DriveInfo(Path.GetPathRoot(PhotosDirectory)!).DriveType == DriveType.Network))
            throw new InvalidOperationException("PhotosDirectory deve ser uma pasta local do servidor.");
        foreach (var binding in Documents)
            if (binding.EntityId == Guid.Empty || !WebFiles.ValidRelative(binding.RelativePath, WebFiles.Category(binding.Type)) ||
                !binding.RelativePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Vínculo de documento inválido.");
        if (Documents.GroupBy(x => (x.Type, x.EntityId)).Any(x => x.Count() > 1))
            throw new InvalidOperationException("Vínculo de documento duplicado.");
    }
}
public sealed record WebDocumentBinding(string Type, Guid EntityId, string RelativePath)
{
    // Explicit associations for PDFs containing two orders. Every linked entity is validated.
    public Guid[] RelatedOrderIds { get; init; } = [];
}
public sealed record WebPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, long Total);
public sealed record WebCustomer(Guid Id, string Name, string Contact);
public sealed record WebCustomerDetail(Guid Id, string Name, string Contact, string Email, string Notes, DateTime UpdatedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CpfCnpj,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Address)
{public string Revision {get;init;}="";}
public sealed record WebMoney(decimal Total, decimal Paid, decimal Remaining, bool PricePending,decimal CollectionSurcharge=0,decimal CustomerExtra=0);
public sealed record WebCost(decimal? Total);
public sealed record WebProduction(DateTime? ArtworkApprovedAt, DateTime? SentToProductionAt, int BusinessDays);
public sealed record WebOrder(Guid Id, long Number, long Revision, string CustomerName, string Contact, string Description,
    string Stage, DateTime CreatedAt, DateTime? DueDate, bool Archived,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WebMoney? Finance)
{public string Notes {get;init;}="";public string DeadlineStatus {get;init;}="";}
public sealed record WebItem(Guid Id, Guid? ProductId, string Product, int Quantity, string Unit,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? UnitPrice,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? TotalCost);
public sealed record WebPayment(Guid Id, DateTime PaidAt, decimal Amount, string PaymentType);
public sealed record WebOrderDetail(WebOrder Order, IReadOnlyList<WebItem> Items, string Notes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WebCustomer? Customer,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<WebPayment>? Payments,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WebCost? Cost,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WebProduction? Production);
public sealed record WebExactPrice(int Quantity, decimal TotalPrice,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? TotalCost);
public sealed record WebPriceTier(int From, int? Through, decimal UnitPrice,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? UnitCost);
public sealed record WebPricing(decimal? Price, IReadOnlyList<WebExactPrice> ExactQuantities, IReadOnlyList<WebPriceTier> Tiers);
public sealed record WebProduct(Guid Id, string Description, string Category, string Unit, bool Active,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ImageUrl,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WebPricing? Pricing,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WebCost? Cost,
    // Todas as fotos (até 3, a primeira é a Principal); só vem quando há mais de uma.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string[]? ImageUrls = null);
public sealed record WebDebt(Guid Id, string Type, string CustomerName, string Contact, string Description,
    DateTime OriginDate, decimal? Remaining, bool PricePending)
{
    public decimal? Original {get;init;}
    public decimal? Received {get;init;}
    public Guid? CustomerId {get;init;}
    public DateTime? DueDate {get;init;}
    public string Status {get;init;}="Pendente";
}
public sealed record WebDashboard(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? OpenOrders,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? ReadyOrders,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PendingDebts,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Receivable,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? UndefinedDebts);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WebNoteCommand(
    [property: JsonRequired] Guid OperationId,
    [property: JsonRequired] long ExpectedRevision,
    [property: JsonRequired] string Observacao);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WebStageCommand(
    [property: JsonRequired] Guid OperationId,
    [property: JsonRequired] long ExpectedRevision,
    [property: JsonRequired] string Etapa);
public sealed record WebCommandResult(Guid OperationId, long Revision, bool Replayed);
public sealed class WebRequestException(int status, string message) : Exception(message) { public int Status { get; } = status; }
public sealed record WebQuery(int Page, int PageSize, string Search, string? Stage, DateTime? From, DateTime? To)
{
    public string? Scope {get;init;}
    public long Offset => (long)(Page - 1) * PageSize;
    public static WebQuery Parse(HttpRequest request,bool desktop=false)
    {
        var q = request.Query;
        int Number(string key, int fallback)
        {
            if (!q.ContainsKey(key)) return fallback;
            if (q[key].Count != 1 || !int.TryParse(q[key], out var value) || value < 1)
                throw new WebRequestException(400, "Paginação inválida.");
            return value;
        }
        DateTime? Date(string key)
        {
            if (!q.ContainsKey(key)) return null;
            if (!DateTime.TryParseExact(q[key], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) || date == DateTime.MaxValue.Date)
                throw new WebRequestException(400, "Período inválido; use yyyy-MM-dd.");
            return date;
        }
        var search = q["search"].ToString().Trim();
        if (search.Length > 200) throw new WebRequestException(400, "Busca muito longa.");
        var stage = q.ContainsKey("stage") ? q["stage"].ToString() : null;
        if (stage != null) stage = OrderStages.Normalize(stage);
        if (stage != null && !WebApiV1.Stages.Contains(stage)) throw new WebRequestException(400, "Etapa inválida.");
        var from = Date("from"); var to = Date("to");
        if (from > to) throw new WebRequestException(400, "Período inválido.");
        var scope=q["scope"].ToString();
        if(scope.Length>0&&!(desktop?new[]{"active","overdue","today","ready","completed","unpaid","unprinted","archived","priority","all"}:new[]{"active","overdue","today","ready","completed"}).Contains(scope))throw new WebRequestException(400,"Filtro operacional inválido.");
        return new(Number("page", 1), Math.Min(Number("pageSize", 50), 100), search, stage, from, to){Scope=scope};
    }
}
public static class WebApiV1
{
    public static readonly string[] Stages = OrderStages.All;
    public static void Configure(WebApplicationBuilder builder, WebApiOptions options)
    {
        options.Validate();
        builder.Services.AddCors(cors => cors.AddPolicy("web-v1", policy =>
        {
            if (options.AllowedOrigins.Length > 0)
                policy.WithOrigins(options.AllowedOrigins).WithMethods("GET", "PATCH", "POST")
                    .WithHeaders("Authorization", "X-Device-Key", "Content-Type");
            // Bearer/device headers only. Never allow cookies or wildcard origins.
        }));
    }
    public static void Map(WebApplication app, DataStore store, WebApiOptions options)
    {
        OperationalApi.Map(app,store,options);
        MobileOrderApi.Map(app,store);MobileSaleApi.Map(app,store);
        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/api/v1")) { await next(context); return; }
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            var bodyLimit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (bodyLimit is { IsReadOnly: false }) bodyLimit.MaxRequestBodySize = 8192;
            if (context.Request.ContentLength > 8192) { context.Response.StatusCode = 413; return; }
            try { await next(context); }
            catch (WebRequestException ex) { await Results.Problem(statusCode: ex.Status, title: ex.Message).ExecuteAsync(context); }
            catch (BadHttpRequestException ex) { await Results.Problem(statusCode: ex.StatusCode, title: "Requisição inválida.").ExecuteAsync(context); }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        });
        UserIdentity Actor(HttpContext c) => (UserIdentity)c.Items["identity"]!;
        Task<IResult> Read(HttpContext c, string permission, Func<SqliteConnection, Dictionary<string, bool>, IResult> action)
            => store.WebReadAsync(Actor(c), permission, action, c.RequestAborted);
        var api = app.MapGroup("/api/v1").RequireCors("web-v1");
        // Force the Minimal API Delegate overload: RequestDelegate would discard Task<IResult>.
        async Task<IResult> Module(HttpContext c,string module)=>Results.Ok(await store.ModuleViewAsync(Actor(c),module,c.Request,c.RequestAborted));
        void Get(string path, Delegate handler) => api.MapGet(path, handler);
        void Patch(string path, Delegate handler) => api.MapPatch(path, handler);
        Get("/dashboard", (HttpContext c) => c.Request.Query["projection"]=="desktop"?Module(c,"dashboard"):Read(c, "view.home", (db, p) => Results.Ok(WebQueries.Dashboard(db, p))));
        Get("/hoje", (HttpContext c) => Read(c, "view.home", (db, p) => Results.Ok(store.TodayOn(db, p, Actor(c)))));
        Get("/pedidos", (HttpContext c) => c.Request.Query["projection"]=="desktop"?Module(c,"pedidos"):Read(c, "view.orders", (db, p) => Results.Ok(WebQueries.Orders(db, p, WebQuery.Parse(c.Request)))));
        Get("/pedidos/{id:guid}", (HttpContext c, Guid id) => Read(c, "view.orders", (db, p) =>
            WebQueries.Order(db, p, id) is { } item ? Results.Ok(item) : Results.NotFound()));
        Get("/pedidos/{id:guid}/documentos", (HttpContext c, Guid id) => Read(c, "view.orders", (db, p) =>
        {
            if (!WebQueries.Exists(db, "scheduled_orders", id)) return Results.NotFound();
            var documents = new List<object>();
            foreach (var type in new[] { "pedido", "orcamento", "recibo" })
            {
                try
                {
                    var path = WebFiles.Document(db, p, Actor(c), type, id, options);
                    if (path != null && File.Exists(path)) documents.Add(new { type, url = $"/api/v1/documentos/{type}/{id}", label = type == "pedido" ? "Ficha do pedido" : type == "orcamento" ? "Orçamento" : "Recibo" });
                }
                catch (UnauthorizedAccessException) { }
                catch (WebRequestException) { }
            }
            return Results.Ok(documents);
        }));
        Get("/clientes", (HttpContext c) => c.Request.Query["projection"]=="desktop"?Module(c,"clientes"):Read(c, "view.customers", (db, p) => Results.Ok(WebQueries.Customers(db, WebQuery.Parse(c.Request)))));
        Get("/clientes/{id:guid}", (HttpContext c, Guid id) => Read(c, "view.customers", (db, p) =>
            WebQueries.Customer(db, p, id) is { } item ? Results.Ok(item) : Results.NotFound()));
        Get("/produtos", (HttpContext c) => c.Request.Query["projection"]=="desktop"?Module(c,"produtos"):Read(c, "view.products", (db, p) => Results.Ok(WebQueries.Products(db, p, WebQuery.Parse(c.Request)))));
        Get("/produtos/{id:guid}", (HttpContext c, Guid id) => Read(c, "view.products", (db, p) =>
            WebQueries.Product(db, p, id) is { } item ? Results.Ok(item) : Results.NotFound()));
        Get("/devedores", (HttpContext c) => c.Request.Query["projection"]=="desktop"?Module(c,"devedores"):Read(c, "view.debtors", (db, p) => Results.Ok(WebQueries.Page(WebQueries.Debts(db, p), WebQuery.Parse(c.Request),
            x => $"{x.CustomerName} {x.Contact} {x.Description}"))));
        Get("/devedores/{id:guid}", (HttpContext c, Guid id) => Read(c, "view.debtors", (db, p) =>
            WebQueries.Debts(db, p).FirstOrDefault(x => x.Id == id) is { } item ? Results.Ok(item) : Results.NotFound()));
        Get("/produtos/{id:guid}/imagem", async (HttpContext c, Guid id) =>
        {
            if (c.Request.Query.Count > 0) throw new WebRequestException(400, "Esta rota aceita somente o identificador da entidade.");
            var path = await store.WebReadAsync(Actor(c), "view.products", (db, p) => WebFiles.Image(db, id, options), c.RequestAborted);
            return await WebFiles.OpenAsync(path, "image/jpeg", "produto.jpg", c.RequestAborted);
        });
        // Miniatura para listas no celular: a foto Principal já reduzida.
        Get("/produtos/{id:guid}/miniatura", async (HttpContext c, Guid id) =>
        {
            if (c.Request.Query.Count > 0) throw new WebRequestException(400, "Esta rota aceita somente o identificador da entidade.");
            var path = await store.WebReadAsync(Actor(c), "view.products", (db, p) => WebFiles.Image(db, id, options), c.RequestAborted);
            return await WebFiles.OpenAsync(path, "image/jpeg", "produto.jpg", 256, c.RequestAborted);
        });
        Get("/produtos/{id:guid}/imagem/{index:int}", async (HttpContext c, Guid id, int index) =>
        {
            if (c.Request.Query.Count > 0) throw new WebRequestException(400, "Esta rota aceita somente o identificador da entidade.");
            var path = await store.WebReadAsync(Actor(c), "view.products", (db, p) => WebFiles.Image(db, id, options, index), c.RequestAborted);
            return await WebFiles.OpenAsync(path, "image/jpeg", "produto.jpg", c.RequestAborted);
        });
        Get("/documentos/{tipo}/{id:guid}", async (HttpContext c, string tipo, Guid id) =>
        {
            if (c.Request.Query.Count > 0) throw new WebRequestException(400, "Esta rota aceita somente tipo e identificador da entidade.");
            var path = await store.WebReadAsync(Actor(c), "", (db, p) => WebFiles.Document(db, p, Actor(c), tipo, id, options), c.RequestAborted);
            return await WebFiles.OpenAsync(path, "application/pdf", $"{tipo}-{id:N}.pdf", c.RequestAborted);
        });
        Patch("/pedidos/{id:guid}/observacao", async (HttpContext c, Guid id, WebNoteCommand command) =>
            Results.Ok(await store.WebCommandAsync(Actor(c), id, command.OperationId, command.ExpectedRevision, command.Observacao, false, c.RequestAborted)));
        Patch("/pedidos/{id:guid}/etapa", async (HttpContext c, Guid id, WebStageCommand command) =>
            Results.Ok(await store.WebCommandAsync(Actor(c), id, command.OperationId, command.ExpectedRevision, command.Etapa, true, c.RequestAborted)));
    }
}

public sealed partial class DataStore
{
    // Microsoft.Data.Sqlite performs synchronous I/O. Keep it off request continuations,
    // use the established cross-process datastore lock, and cancel before dispatch/commit.
    public Task<T> WebReadAsync<T>(UserIdentity actor, string permission, Func<SqliteConnection, Dictionary<string, bool>, T> read, CancellationToken cancellation)
        => Task.Run(() => ReadWork(db =>
        {
            // ReadWork abre a transação de leitura (foto única do banco).
            cancellation.ThrowIfCancellationRequested();
            var p = Permissions.Resolve(db, actor);
            if (permission.Length > 0 && !p.GetValueOrDefault(permission)) throw new UnauthorizedAccessException();
            var result = read(db, p);
            cancellation.ThrowIfCancellationRequested();
            return result;
        }), cancellation);
    public Task<WebCommandResult> WebCommandAsync(UserIdentity actor, Guid id, Guid operationId, long revision, string value, bool stage, CancellationToken cancellation)
        => Task.Run(() => WithLock(() =>
        {
            cancellation.ThrowIfCancellationRequested();
            if (id == Guid.Empty || operationId == Guid.Empty || revision < 0 || value == null ||
                value.Length > (stage ? 80 : 4000)) throw new WebRequestException(400, "Comando inválido.");
            if (stage) value = OrderStages.Normalize(value);
            if (stage && (!WebApiV1.Stages.Contains(value) || value == "Entregue"))
                throw new WebRequestException(422, "Entrega e operações financeiras ficam para uma fase posterior.");
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(new { Contract = "web-v1", id, operationId, revision, value, stage }))));
            Dictionary<string, bool> permissions;
            using (var db = Open())
            {
                permissions = Permissions.Resolve(db, actor);
                if (!permissions.GetValueOrDefault("view.orders") || !permissions.GetValueOrDefault(stage ? "orders.stage" : "orders.edit"))
                    throw new UnauthorizedAccessException();
                using var receipt = WebQueries.Command(db, "SELECT actor_id,payload_hash FROM remote_operations WHERE id=@id", ("@id", operationId));
                using var r = receipt.ExecuteReader();
                if (r.Read())
                {
                    if (r.GetString(0) != actor.UserId.ToString() || r.GetString(1) != hash)
                        throw new WebRequestException(409, "Identificador de operação já utilizado.");
                    return new WebCommandResult(operationId, revision + 1, true);
                }
            }
            var current = ReadUnsafe();
            var old = current.ScheduledOrders.FirstOrDefault(x => x.Id == id) ?? throw new WebRequestException(404, "Pedido não encontrado.");
            if (old.Revision != revision) throw new WebRequestException(409, "Pedido alterado; atualize antes de salvar.");
            if (old.IsUncollected || old.Stage == "Entregue") throw new WebRequestException(422, "Pedido arquivado ou entregue não pode ser alterado nesta fase.");
            if(stage&&!OrderStages.MobileAllowed(old,permissions).Contains(value))throw new WebRequestException(422,"Transição de etapa inválida.");
            // Paid-order information changes require a justification in the existing 7B rules.
            // No new justification bypass is introduced by the minimal note command.
            if (!stage && old.PaidAmount > 0) throw new WebRequestException(422, "Alteração de pedido pago exige o fluxo de justificativa do desktop.");
            var next = ServerAuthorization.CloneForWrite(current);
            var order = next.ScheduledOrders.Single(x => x.Id == id);
            try
            {
                if (stage) OrderOperations.AdvanceStage(next, id, revision, value, new OperationContext(actor.Name, actor.Role) { Permissions = permissions }, false);
                else { order.Notes = value; order.Revision++; }
                // Same authoritative differential authorization, audit, financial invariants and ledger as WPF.
                ServerAuthorization.ValidateAndAudit(current, next, actor, permissions);
                next = Normalize(next);
                FinancialIntegrity.ValidateTransition(current, next);
            }
            catch (InvalidOperationException) { throw new WebRequestException(422, "Regra do pedido impede esta alteração."); }
            cancellation.ThrowIfCancellationRequested();
            CreateRoutineBackupUnsafe();
            SaveUnsafe(next, operationId, actor.UserId.ToString(), hash);
            return new WebCommandResult(operationId, revision + 1, false);
        }), cancellation);
}

